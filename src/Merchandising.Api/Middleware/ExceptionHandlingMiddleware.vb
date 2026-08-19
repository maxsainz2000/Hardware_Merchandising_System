' Merchandising.Api.Middleware.ExceptionHandlingMiddleware
'
' The single place an unhandled exception becomes a client-visible response.
'
' WHY IT EXISTS (P1-10, done-when box 4; spec sections 13 and 17; CLAUDE.md
' section 5). Until this card, Program.vb registered no exception handler,
' and that failed box 4 in two different ways depending on environment:
'
'   Development - WebApplication auto-registers DeveloperExceptionPage as the
'                 first middleware, so an unhandled exception returned a full
'                 stack trace, file paths and source snippets to the caller.
'   Production  - nothing handled it at all, so the caller got a bare 500 with
'                 an EMPTY body: no error code, no message, no correlation Id,
'                 which spec section 13 requires on every error response.
'
' Registering this middleware fixes both at once. It sits outermost in the
' pipeline, so the developer exception page - which is registered ahead of it
' by the framework and cannot be removed from a WebApplication - never sees an
' exception to format, because this one has already handled it. That is
' verified live in evidence/phase-1/p1-10-denials.txt with
' ASPNETCORE_ENVIRONMENT=Development, not assumed from the ordering.
'
' WHAT THE CALLER IS ALLOWED TO LEARN: that something failed, and one
' correlation Id to quote to an operator. Nothing else. Not the exception
' type, not its message, not a stack frame. A MySqlException's message alone
' names tables and columns, and a connection failure's message can carry the
' server, port, database and user - CLAUDE.md section 5, "errors never leak
' internals".
'
' WHAT THE OPERATOR GETS: the whole exception, logged server-side against the
' same correlation Id the caller was handed. CLAUDE.md section 10 forbids
' swallowing an exception silently; this catch is only permissible because it
' logs in full and converts to a controlled response, which is handling it.

Imports System.Text.Json
Imports Merchandising.Contracts.Errors
Imports Microsoft.AspNetCore.Http
Imports Microsoft.Extensions.Logging

Namespace Middleware

    ''' <summary>
    ''' Converts any unhandled exception into the standard
    ''' <see cref="ApiErrorResponse"/> envelope, logging the original
    ''' server-side and revealing nothing about it to the caller.
    ''' </summary>
    Public NotInheritable Class ExceptionHandlingMiddleware

        ''' <summary>Stable error code for anything that reaches this handler.</summary>
        Public Const ErrorCode As String = "INTERNAL_ERROR"

        ''' <summary>
        ''' Deliberately says nothing about the cause. The correlation Id is
        ''' the caller's entire route to a diagnosis, via an operator who can
        ''' read the log.
        ''' </summary>
        Public Const ErrorMessage As String =
            "The request could not be completed because of an unexpected error. " &
            "Quote the correlation ID when reporting this."

        Private ReadOnly _next As RequestDelegate
        Private ReadOnly _logger As ILogger(Of ExceptionHandlingMiddleware)

        ''' <summary>
        ''' Creates the middleware.
        ''' </summary>
        ''' <param name="nextMiddleware">The rest of the pipeline.</param>
        ''' <param name="logger">
        ''' Resolved from DI by <c>UseMiddleware</c>. Optional so the tests can
        ''' construct the real type directly without standing up a container;
        ''' a missing logger degrades diagnostics, never the response.
        ''' </param>
        Public Sub New(nextMiddleware As RequestDelegate,
                       Optional logger As ILogger(Of ExceptionHandlingMiddleware) = Nothing)

            _next = nextMiddleware
            _logger = logger

        End Sub

        Public Async Function InvokeAsync(context As HttpContext) As Task

            ' VB GOTCHA, and a genuine C#/VB divergence rather than a style
            ' choice: `Await` is legal inside a C# catch block since C# 6, and
            ' is a compile error in Visual Basic to this day - BC36943. So the
            ' exception is captured here and handled after the Try closes.
            ' Writing this the C# way is the natural mistake; CLAUDE.md
            ' section 3.
            Dim captured As Exception = Nothing

            Try

                Await _next(context)

            Catch ex As Exception When Not context.Response.HasStarted

                ' The `When Not HasStarted` filter is the important half. Once
                ' the response has begun, the status code and headers are
                ' already on the wire and an envelope written now would be
                ' appended to a half-sent body - producing a corrupt response
                ' that still LOOKS like whatever status was already sent.
                ' Letting the exception escape instead makes the host abort the
                ' connection, which the client sees as the failure it is.
                '
                ' An exception filter rather than an If inside the Catch,
                ' because a filter that does not match leaves the exception
                ' entirely unhandled - original stack trace intact - instead of
                ' needing a rethrow that truncates it.

                captured = ex

            End Try

            If captured IsNot Nothing Then
                Await WriteErrorResponseAsync(context, captured)
            End If

        End Function

        Private Async Function WriteErrorResponseAsync(context As HttpContext, ex As Exception) As Task

            ' Read the Id straight out of Items rather than through
            ' HttpContext.GetCorrelationId(). That helper falls back to
            ' TraceIdentifier, which is not a Guid and would not fit
            ' AuditLogs.CorrelationId CHAR(36) if a later card logged this
            ' failure. If CorrelationIdMiddleware has not run - it is inside
            ' this one, so it may have thrown before stamping - mint a Guid.
            Dim stamped As Object = Nothing
            Dim correlationId As String

            If context.Items.TryGetValue(CorrelationIdMiddleware.ItemsKey, stamped) AndAlso
               TypeOf stamped Is String AndAlso
               Not String.IsNullOrWhiteSpace(CStr(stamped)) Then

                correlationId = CStr(stamped)
            Else
                correlationId = Guid.NewGuid().ToString()
            End If

            ' Logged in full, with the exception object so the stack trace is
            ' preserved for the operator. This is the half that makes catching
            ' legitimate rather than swallowing (CLAUDE.md section 10).
            If _logger IsNot Nothing Then
                _logger.LogError(
                    ex,
                    "Unhandled exception serving {Method} {Path}. CorrelationId={CorrelationId}",
                    context.Request.Method,
                    context.Request.Path.Value,
                    correlationId)
            End If

            ' Clear anything a partially-run handler queued onto the response.
            ' Safe because HasStarted was false when the filter matched.
            context.Response.Clear()
            context.Response.StatusCode = StatusCodes.Status500InternalServerError
            context.Response.ContentType = "application/json; charset=utf-8"
            context.Response.Headers(CorrelationIdMiddleware.HeaderName) = correlationId

            Dim body As New ApiErrorResponse With {
                .ErrorCode = ErrorCode,
                .Message = ErrorMessage,
                .CorrelationId = correlationId
            }

            ' Reflection-based serialization, never source-generated: the
            ' System.Text.Json generator is C#-only (CLAUDE.md section 3).
            Await context.Response.WriteAsync(JsonSerializer.Serialize(body))

        End Function

    End Class

End Namespace
