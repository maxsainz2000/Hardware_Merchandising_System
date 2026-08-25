' Merchandising.Api.Controllers.MaintenanceController
'
' P1-18. Spec section 15 steps 1, 2 and 7 - the authorized administrative
' workflow for entering and leaving maintenance mode.
'
' WHAT THIS CONTROLLER DELIBERATELY DOES NOT DO: restore anything.
' Spec section 15 is explicit that restore "is not executed as a normal
' request inside a live API process that depends on the database", and the
' reason is structural rather than stylistic - a restore drops and recreates
' the schema the API is mid-query against, and a process cannot coherently
' survive having its own data source replaced underneath it. The restore lives
' in Merchandising.Maintenance, runs from the console with the service
' stopped, and there is no route here that could trigger it. That absence is
' asserted by MaintenanceModeTests.Api_ExposesNoRestoreEndpoint rather than
' left to trust.

Imports System.Security.Claims
Imports System.Threading.Tasks
Imports Merchandising.Api.Middleware
Imports Merchandising.Api.Security
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Maintenance
Imports Merchandising.Domain.Security
Imports Merchandising.Infrastructure.Data
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Http
Imports Microsoft.AspNetCore.Mvc
Imports MySqlConnector

Namespace Controllers

    <ApiController>
    <Route("api/v1/admin/maintenance")>
    Public Class MaintenanceController
        Inherits ControllerBase

        Private Const MaxReasonLength As Integer = 500
        Private Const MaxDetailLength As Integer = 2000
        Private Const ClientMessageSettingKey As String = "maintenance.clientMessage"

        Private ReadOnly _repository As MaintenanceLockRepository
        Private ReadOnly _connectionFactory As ConnectionFactory

        Public Sub New(repository As MaintenanceLockRepository, connectionFactory As ConnectionFactory)
            _repository = repository
            _connectionFactory = connectionFactory
        End Sub

        ''' <summary>
        ''' Current maintenance state, for clients to display the warning
        ''' spec section 15 step 2 requires.
        ''' </summary>
        ''' <remarks>
        ''' ANONYMOUS ON PURPOSE. A client that cannot log in - because the
        ''' system is mid-restore - is exactly the client that most needs to
        ''' show "under maintenance" instead of a login error. Gating this
        ''' behind a token would make the warning unreachable at the moment it
        ''' matters. It discloses only that maintenance is on and why, which
        ''' spec section 15 requires be displayed anyway.
        ''' </remarks>
        <AllowAnonymous>
        <HttpGet("status")>
        Public Async Function GetStatus() As Task(Of IActionResult)

            Dim activeLock As MaintenanceLock = Await _repository.GetActiveAsync(HttpContext.RequestAborted)

            If activeLock Is Nothing Then
                Return Ok(New MaintenanceStatusResponse With {.InMaintenance = False})
            End If

            Return Ok(New MaintenanceStatusResponse With {
                .InMaintenance = True,
                .Reason = activeLock.Reason,
                .Message = Await ReadClientMessageAsync(),
                .SinceUtc = activeLock.AcquiredAtUtc
            })

        End Function

        ''' <summary>
        ''' Takes the maintenance lock (spec section 15 step 1). SuperAdmin
        ''' only - narrower than the rest of AdminController, because this
        ''' stops the store trading.
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.MaintenancePerform)>
        <HttpPost("enter")>
        Public Async Function Enter(<FromBody> request As EnterMaintenanceRequest) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            If request Is Nothing OrElse String.IsNullOrWhiteSpace(request.Reason) Then
                fieldErrors("reason") = {"A reason is required to enter maintenance mode."}
            ElseIf request.Reason.Length > MaxReasonLength Then
                fieldErrors("reason") = {$"Reason must be {MaxReasonLength} characters or fewer."}
            End If

            If fieldErrors.Count > 0 Then
                Return BadRequest(New ApiErrorResponse With {
                    .ErrorCode = "VALIDATION_FAILED",
                    .Message = "The maintenance request failed validation.",
                    .CorrelationId = correlationId,
                    .Errors = fieldErrors
                })
            End If

            Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

            Dim acquired As MaintenanceLock =
                Await _repository.TryAcquireAsync(request.Reason, actorUserId, correlationId, HttpContext.RequestAborted)

            If acquired Is Nothing Then
                ' The database refused a second active lock (ERROR 1062).
                Return Conflict(New ApiErrorResponse With {
                    .ErrorCode = "MAINTENANCE_ALREADY_ACTIVE",
                    .Message = "The system is already in maintenance mode.",
                    .CorrelationId = correlationId
                })
            End If

            Await WriteAuditAsync("MaintenanceEnter", $"lock:{acquired.Id}", "Success",
                                  actorUserId, correlationId, request.Reason)

            Return Ok(New MaintenanceStatusResponse With {
                .InMaintenance = True,
                .Reason = acquired.Reason,
                .Message = Await ReadClientMessageAsync(),
                .SinceUtc = acquired.AcquiredAtUtc
            })

        End Function

        ''' <summary>
        ''' Releases the lock - only when verification passed (spec section 15
        ''' step 7).
        ''' </summary>
        ''' <remarks>
        ''' A release claiming verificationPassed = False is REFUSED, and the
        ''' lock stays held. This is the one rule on this controller that is
        ''' worth arguing about: it means an operator who genuinely cannot
        ''' verify has to say so, be refused, and then deal with the situation
        ''' rather than quietly reopening a system whose data they do not
        ''' trust. The escape hatch is a second restore, not a shrug.
        ''' </remarks>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.MaintenancePerform)>
        <HttpPost("release")>
        Public Async Function Release(<FromBody> request As ReleaseMaintenanceRequest) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

            If request Is Nothing OrElse Not request.VerificationPassed Then

                Await WriteAuditAsync("MaintenanceReleaseRefused", "lock:active", "Refused",
                                      actorUserId, correlationId,
                                      "Release attempted without a passing verification result.")

                Return StatusCode(StatusCodes.Status409Conflict, New ApiErrorResponse With {
                    .ErrorCode = "VERIFICATION_NOT_CONFIRMED",
                    .Message =
                        "Maintenance mode is not released until verification succeeds. " &
                        "Confirm that the expected users, products and balances are present, then release again.",
                    .CorrelationId = correlationId
                })

            End If

            Dim detail As String = If(request.Detail, String.Empty)
            If detail.Length > MaxDetailLength Then
                detail = detail.Substring(0, MaxDetailLength)
            End If

            Dim released As Boolean =
                Await _repository.ReleaseAsync(actorUserId, True, detail, HttpContext.RequestAborted)

            If Not released Then
                Return Conflict(New ApiErrorResponse With {
                    .ErrorCode = "MAINTENANCE_NOT_ACTIVE",
                    .Message = "The system is not in maintenance mode.",
                    .CorrelationId = correlationId
                })
            End If

            Await WriteAuditAsync("MaintenanceRelease", "lock:released", "Success",
                                  actorUserId, correlationId, detail)

            Return Ok(New MaintenanceStatusResponse With {.InMaintenance = False})

        End Function

        Private Async Function ReadClientMessageAsync() As Task(Of String)

            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync(HttpContext.RequestAborted)

                Dim settings = Await SystemSettingsRepository.LoadByPrefixAsync(
                    connection, "maintenance.", HttpContext.RequestAborted)

                Dim message As String = Nothing
                If settings.TryGetValue(ClientMessageSettingKey, message) Then
                    Return message
                End If

                Return "The system is under maintenance."

            End Using

        End Function

        ''' <summary>
        ''' Entering and leaving maintenance are sensitive actions, so both
        ''' get an audit row (spec section 9). AuditLogs is append-only, which
        ''' is what makes it - not MaintenanceLocks - the immutable record:
        ''' the lock row is updated on release, the audit rows never are.
        ''' </summary>
        Private Async Function WriteAuditAsync(
            action As String, target As String, result As String,
            actorUserId As Integer, correlationId As String, detail As String) As Task

            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync(HttpContext.RequestAborted)
                Await AuditLogWriter.WriteAsync(
                    connection, actorUserId, action, target, result, correlationId, detail, HttpContext.RequestAborted)
            End Using

        End Function

    End Class

End Namespace
