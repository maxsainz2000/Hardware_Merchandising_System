' Merchandising.Api.Controllers.CashierSessionsController
'
' P5-04: spec section 10.3's "A sale requires an open cashier session." Two
' routes, gated by CashierSessions.Manage (CashierAndAbove):
'
'   POST /api/v1/cashier-sessions          - opens a session with a declared
'                                             opening float. 409 AlreadyOpen
'                                             if this cashier already holds
'                                             one (card Done-when box 1).
'   POST /api/v1/cashier-sessions/{id}/close - closes an Open session.
'
' WHAT THIS CONTROLLER OWNS, AND WHAT IT DELIBERATELY DOES NOT - the same
' division StockCountsController's header states. It owns HTTP shape only:
' parsing, refusing a malformed request with field-level detail (ADR-014),
' and mapping a service outcome onto a status code. Every business rule -
' session uniqueness, the Open/Closed status transition - is decided inside
' CashierSessionService, in one transaction per command.

Imports System.Collections.Generic
Imports System.Security.Claims
Imports System.Threading.Tasks
Imports Merchandising.Api.Middleware
Imports Merchandising.Api.Sales
Imports Merchandising.Api.Security
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Sales
Imports Merchandising.Domain
Imports Merchandising.Domain.Security
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Mvc

Namespace Controllers

    <ApiController>
    <Route("api/v1/cashier-sessions")>
    <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.CashierSessionsManage)>
    Public Class CashierSessionsController
        Inherits ControllerBase

        Private ReadOnly _cashierSessionService As CashierSessionService

        Public Sub New(cashierSessionService As CashierSessionService)
            _cashierSessionService = cashierSessionService
        End Sub

        ''' <summary>Opens a new session for the calling cashier.</summary>
        <AuditRequired>
        <HttpPost>
        Public Async Function OpenCashierSession(<FromBody> request As OpenCashierSessionRequest) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            ValidateOpenRequestShape(request, fieldErrors)

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

            Dim outcome As CashierSessionOutcome =
                Await _cashierSessionService.OpenAsync(
                    actorUserId, request.OpeningFloat, correlationId, request.IdempotencyKey,
                    cancellationToken:=HttpContext.RequestAborted)

            Select Case outcome.Kind

                Case CashierSessionOutcomeKind.Created
                    Return Created($"/api/v1/cashier-sessions/{outcome.Session.Id}", outcome.Session)

                Case CashierSessionOutcomeKind.Replayed
                    ' ADR-007: the ORIGINAL committed result, byte for byte.
                    ' 200, not 201 - this request opened nothing new.
                    Return Content(outcome.ReplayPayload, "application/json")

                Case Else ' AlreadyOpen
                    Return Conflict(New ApiErrorResponse With {
                        .ErrorCode = CashierSessionOutcome.AlreadyOpenErrorCode,
                        .Message = "This cashier already holds an open session - it must be closed before another can be opened.",
                        .CorrelationId = correlationId
                    })

            End Select

        End Function

        ''' <summary>Closes an Open session.</summary>
        <AuditRequired>
        <HttpPost("{id}/close")>
        Public Async Function CloseCashierSession(
            id As Integer, <FromBody> request As CloseCashierSessionRequest) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            ValidateIdempotencyKey(request?.IdempotencyKey, fieldErrors)

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

            Dim outcome As CashierSessionOutcome =
                Await _cashierSessionService.CloseAsync(
                    id, actorUserId, correlationId, request.IdempotencyKey, cancellationToken:=HttpContext.RequestAborted)

            Select Case outcome.Kind

                Case CashierSessionOutcomeKind.Replayed
                    Return Content(outcome.ReplayPayload, "application/json")

                Case CashierSessionOutcomeKind.NotFound
                    Return NotFound(New ApiErrorResponse With {
                        .ErrorCode = "CASHIER_SESSION_NOT_FOUND",
                        .Message = $"No cashier session with Id {id} exists.",
                        .CorrelationId = correlationId
                    })

                Case CashierSessionOutcomeKind.NotOpen
                    Return Conflict(New ApiErrorResponse With {
                        .ErrorCode = CashierSessionOutcome.NotOpenErrorCode,
                        .Message = "This cashier session is not open - it may already be closed.",
                        .CorrelationId = correlationId
                    })

                Case Else ' Created (closed)
                    Return Ok(outcome.Session)

            End Select

        End Function

        ' --------------------------------------------------------- validation

        Private Shared Sub ValidateOpenRequestShape(
            request As OpenCashierSessionRequest, fieldErrors As Dictionary(Of String, String()))

            If request Is Nothing Then
                fieldErrors("request") = {"A request body is required."}
                Return
            End If

            If request.OpeningFloat < 0D Then
                fieldErrors("openingFloat") = {"Opening float cannot be negative."}
            ElseIf Not DecimalScaleGuard.IsAtMoneyScale(request.OpeningFloat) Then
                fieldErrors("openingFloat") = {
                    $"Opening float must have no more than {DecimalScaleGuard.MoneyScale} decimal places."}
            End If

            ValidateIdempotencyKey(request.IdempotencyKey, fieldErrors)

        End Sub

        ''' <summary>ADR-007: required on every write command, validated to the same canonical 36-character UUID shape IdempotencyKeys.KeyValue is declared as.</summary>
        Private Shared Sub ValidateIdempotencyKey(idempotencyKey As String, fieldErrors As Dictionary(Of String, String()))

            If String.IsNullOrWhiteSpace(idempotencyKey) Then
                fieldErrors("idempotencyKey") = {"Idempotency key is required."}
            ElseIf Not IsWellFormedIdempotencyKey(idempotencyKey) Then
                fieldErrors("idempotencyKey") = {
                    "Idempotency key must be a UUID in the canonical 36-character form, for example " &
                    "3f2504e0-4f89-41d3-9a0c-0305e82c3301."}
            End If

        End Sub

        Private Shared Function IsWellFormedIdempotencyKey(value As String) As Boolean

            Dim parsed As Guid = Guid.Empty
            Return Guid.TryParseExact(value, "D", parsed) AndAlso parsed <> Guid.Empty

        End Function

        ' ------------------------------------------------------------ results

        Private Function ValidationFailed(
            fieldErrors As Dictionary(Of String, String()), correlationId As String) As IActionResult

            Return BadRequest(New ApiErrorResponse With {
                .ErrorCode = "VALIDATION_FAILED",
                .Message = "The cashier session request could not be processed because of a validation failure.",
                .CorrelationId = correlationId,
                .Errors = fieldErrors
            })

        End Function

    End Class

End Namespace
