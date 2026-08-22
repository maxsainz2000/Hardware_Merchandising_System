' Merchandising.Api.Middleware.MaintenanceModeMiddleware
'
' P1-18. Spec section 15 step 2: while the maintenance lock is held, the API
' "rejects new ordinary writes, and displays a warning to connected clients".
'
' WHY THIS RUNS BEFORE AUTHENTICATION, NOT AFTER.
' The obvious placement is after UseAuthorization, so that only authenticated
' callers learn the system is in maintenance. That was tried and is wrong
' here, for three reasons:
'
'   1. A client whose session expired during the maintenance window would get
'      401 - "your login is bad" - when the true answer is "the system is
'      down for maintenance". That sends the operator to debug the wrong
'      problem at the worst possible moment.
'   2. Maintenance state is not a secret. Spec section 15 step 2 requires it
'      be DISPLAYED to connected clients; withholding it from an
'      unauthenticated caller protects nothing.
'   3. It is cheaper. A request that is going to be refused should not first
'      cost a session lookup against the database that is about to be
'      restored.
'
' WHY WRITES ONLY, NOT ALL TRAFFIC.
' Spec section 15 says "ordinary writes", not "all traffic". Maintenance mode
' exists so a restore can proceed without concurrent mutation; blocking reads
' as well would take the whole system down for no additional safety, and
' would break the very status endpoint clients poll to discover the state.
'
' ON THE PER-REQUEST QUERY.
' The lock is read from the database on every mutating request rather than
' cached. That is one indexed lookup on a table with a handful of rows, and it
' is only paid by writes. A cache would need invalidating across acquire and
' release, and a stale "not in maintenance" cache entry would let a write
' through during a restore - the exact failure this middleware exists to
' prevent. Correctness over a saving that has not been measured as needed.

Imports System.Text.Json
Imports System.Threading.Tasks
Imports Merchandising.Contracts.Errors
Imports Merchandising.Infrastructure.Data
Imports Microsoft.AspNetCore.Http

Namespace Middleware

    Public NotInheritable Class MaintenanceModeMiddleware

        ''' <summary>
        ''' Paths exempt from the write block, matched as prefixes.
        ''' </summary>
        ''' <remarks>
        ''' Each of these would otherwise deadlock the system or mislead the
        ''' caller:
        '''   - the maintenance endpoints themselves, or the lock could be
        '''     taken and never released;
        '''   - login and logout, because releasing the lock needs a token and
        '''     obtaining one is a POST. Blocking it would make maintenance
        '''     mode escapable only by restarting the service.
        ''' Both remain protected by their own authorization attributes; being
        ''' exempt from the maintenance gate is not being exempt from auth.
        ''' </remarks>
        Private Shared ReadOnly ExemptPathPrefixes As String() = {
            "/api/v1/admin/maintenance",
            "/api/v1/auth/login",
            "/api/v1/auth/logout"
        }

        Private ReadOnly _next As RequestDelegate

        Public Sub New([next] As RequestDelegate)
            _next = [next]
        End Sub

        Public Async Function InvokeAsync(
            context As HttpContext,
            repository As MaintenanceLockRepository) As Task

            If Not IsMutating(context.Request.Method) OrElse IsExempt(context.Request.Path) Then
                Await _next(context).ConfigureAwait(False)
                Return
            End If

            Dim activeLock As MaintenanceLock = Await repository.GetActiveAsync(context.RequestAborted).ConfigureAwait(False)

            If activeLock Is Nothing Then
                Await _next(context).ConfigureAwait(False)
                Return
            End If

            Await WriteMaintenanceResponseAsync(context, activeLock).ConfigureAwait(False)

        End Function

        Private Shared Function IsMutating(method As String) As Boolean

            ' GET, HEAD, OPTIONS and TRACE are safe by definition. Everything
            ' else is treated as a write, including verbs this API does not
            ' currently expose - a future PATCH endpoint should be blocked by
            ' default rather than needing someone to remember to add it here.
            Return Not (
                String.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) OrElse
                String.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase) OrElse
                String.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase) OrElse
                String.Equals(method, "TRACE", StringComparison.OrdinalIgnoreCase))

        End Function

        Private Shared Function IsExempt(path As PathString) As Boolean

            For Each prefix As String In ExemptPathPrefixes
                If path.StartsWithSegments(New PathString(prefix), StringComparison.OrdinalIgnoreCase) Then
                    Return True
                End If
            Next

            Return False

        End Function

        Private Shared Async Function WriteMaintenanceResponseAsync(
            context As HttpContext, activeLock As MaintenanceLock) As Task

            Dim correlationId As String = context.GetCorrelationId()

            ' 503, not 409. The request is not in conflict with anything - the
            ' service is deliberately unavailable for writes and will be
            ' available again later, which is precisely what 503 means.
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable
            context.Response.ContentType = "application/json"

            ' Retry-After is what turns "try again later" from advice into
            ' something a client can act on without guessing. 300 seconds is a
            ' deliberate under-promise: a restore takes longer, and a client
            ' that retries early gets another 503 rather than a wrong answer.
            context.Response.Headers.RetryAfter = "300"

            Dim payload As New ApiErrorResponse With {
                .ErrorCode = "MAINTENANCE_MODE",
                .Message =
                    "The system is under maintenance and is not accepting changes. Reason: " &
                    activeLock.Reason,
                .CorrelationId = correlationId
            }

            ' The lock's own reason is included; nothing else about the lock
            ' is. Who requested it and when are operational details that do
            ' not belong in a response to an arbitrary caller (CLAUDE.md
            ' section 5: errors never leak internals).
            Await context.Response.WriteAsync(JsonSerializer.Serialize(payload)).ConfigureAwait(False)

        End Function

    End Class

End Namespace
