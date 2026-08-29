' Merchandising.Api.Controllers.StockCountsController
'
' P4-09: spec section 10.2's stock counts - "Open a count, record counted
' quantities per product, compute variance against the system quantity at
' the moment of counting, and close it." Gated by StockCounts.Perform
' (InventoryAndAbove, pre-registered at P2-02 with no live route until this
' card).
'
' THREE ROUTES, ONE RESOURCE. POST /api/v1/stock-counts opens a session;
' POST .../{id}/lines records one product at a time (a counter walks the
' floor and enters quantities as they go, not all at once); POST
' .../{id}/close locks it. GET .../{id} reads it back, open or closed -
' card Done-when box 4: "A closed count is immutable and remains fully
' readable."
'
' WHAT THIS CONTROLLER OWNS, AND WHAT IT DELIBERATELY DOES NOT - the same
' division ReceivingController's header states. It owns HTTP shape only:
' parsing, refusing a malformed request with field-level detail (ADR-014),
' and mapping a service outcome onto a status code. Every business rule -
' session status, product existence, the variance computation itself - is
' decided inside StockCountService, in one transaction per command.

Imports System.Collections.Generic
Imports System.Security.Claims
Imports System.Threading.Tasks
Imports Merchandising.Api.Inventory
Imports Merchandising.Api.Middleware
Imports Merchandising.Api.Security
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Inventory
Imports Merchandising.Domain
Imports Merchandising.Domain.Security
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Mvc

Namespace Controllers

    <ApiController>
    <Route("api/v1/stock-counts")>
    <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.StockCountsPerform)>
    Public Class StockCountsController
        Inherits ControllerBase

        Private ReadOnly _stockCountService As StockCountService

        Public Sub New(stockCountService As StockCountService)
            _stockCountService = stockCountService
        End Sub

        ''' <summary>Opens a new, empty count session.</summary>
        <AuditRequired>
        <HttpPost>
        Public Async Function OpenStockCount(<FromBody> request As OpenStockCountRequest) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            ValidateIdempotencyKey(request?.IdempotencyKey, fieldErrors)

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

            Dim outcome As StockCountOutcome =
                Await _stockCountService.OpenAsync(
                    actorUserId, correlationId, request.IdempotencyKey, cancellationToken:=HttpContext.RequestAborted)

            Select Case outcome.Kind

                Case StockCountOutcomeKind.Replayed
                    ' ADR-007: the ORIGINAL committed result, byte for byte.
                    ' 200, not 201 - this request opened nothing new.
                    Return Content(outcome.ReplayPayload, "application/json")

                Case Else ' Created
                    Return Created($"/api/v1/stock-counts/{outcome.Session.Id}", outcome.Session)

            End Select

        End Function

        ''' <summary>Records one product's counted quantity against an Open session.</summary>
        <AuditRequired>
        <HttpPost("{stockCountId}/lines")>
        Public Async Function RecordStockCountLine(
            stockCountId As Integer, <FromBody> request As RecordStockCountLineRequest) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            ValidateRecordLineRequestShape(request, fieldErrors)

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

            Dim outcome As StockCountOutcome =
                Await _stockCountService.RecordLineAsync(
                    stockCountId, request.ProductId, request.CountedQuantity, actorUserId, correlationId,
                    request.IdempotencyKey, cancellationToken:=HttpContext.RequestAborted)

            Select Case outcome.Kind

                Case StockCountOutcomeKind.Replayed
                    Return Content(outcome.ReplayPayload, "application/json")

                Case StockCountOutcomeKind.NotFound
                    Return NotFound(New ApiErrorResponse With {
                        .ErrorCode = "STOCK_COUNT_NOT_FOUND",
                        .Message = $"No stock count with Id {stockCountId} exists.",
                        .CorrelationId = correlationId
                    })

                Case StockCountOutcomeKind.NotOpen
                    Return Conflict(New ApiErrorResponse With {
                        .ErrorCode = StockCountOutcome.NotOpenErrorCode,
                        .Message = "This stock count is not open - a counted quantity can only be recorded while it is Open.",
                        .CorrelationId = correlationId
                    })

                Case StockCountOutcomeKind.ProductNotFound
                    Return ValidationFailed(
                        New Dictionary(Of String, String()) From {
                            {"productId", New String() {"No such product exists."}}},
                        correlationId)

                Case Else ' Created
                    Return Created($"/api/v1/stock-counts/{stockCountId}/lines/{outcome.Line.Id}", outcome.Line)

            End Select

        End Function

        ''' <summary>Closes an Open session - immutable from this point on.</summary>
        <AuditRequired>
        <HttpPost("{stockCountId}/close")>
        Public Async Function CloseStockCount(
            stockCountId As Integer, <FromBody> request As CloseStockCountRequest) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            ValidateIdempotencyKey(request?.IdempotencyKey, fieldErrors)

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

            Dim outcome As StockCountOutcome =
                Await _stockCountService.CloseAsync(
                    stockCountId, actorUserId, correlationId, request.IdempotencyKey, cancellationToken:=HttpContext.RequestAborted)

            Select Case outcome.Kind

                Case StockCountOutcomeKind.Replayed
                    Return Content(outcome.ReplayPayload, "application/json")

                Case StockCountOutcomeKind.NotFound
                    Return NotFound(New ApiErrorResponse With {
                        .ErrorCode = "STOCK_COUNT_NOT_FOUND",
                        .Message = $"No stock count with Id {stockCountId} exists.",
                        .CorrelationId = correlationId
                    })

                Case StockCountOutcomeKind.NotOpen
                    Return Conflict(New ApiErrorResponse With {
                        .ErrorCode = StockCountOutcome.NotOpenErrorCode,
                        .Message = "This stock count is not open - it may already be closed.",
                        .CorrelationId = correlationId
                    })

                Case Else ' Created (closed)
                    Return Ok(outcome.Session)

            End Select

        End Function

        ''' <summary>Reads one session back, with its lines - open, closed, or otherwise. No audit: a read is not a privileged action (AuditPipelineFilter's header).</summary>
        <HttpGet("{stockCountId}")>
        Public Async Function GetStockCount(stockCountId As Integer) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()

            Dim session As StockCountResponse =
                Await _stockCountService.GetAsync(stockCountId, cancellationToken:=HttpContext.RequestAborted)

            If session Is Nothing Then
                Return NotFound(New ApiErrorResponse With {
                    .ErrorCode = "STOCK_COUNT_NOT_FOUND",
                    .Message = $"No stock count with Id {stockCountId} exists.",
                    .CorrelationId = correlationId
                })
            End If

            Return Ok(session)

        End Function

        ' --------------------------------------------------------- validation

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

        Private Shared Sub ValidateRecordLineRequestShape(
            request As RecordStockCountLineRequest, fieldErrors As Dictionary(Of String, String()))

            If request Is Nothing Then
                fieldErrors("request") = {"A request body is required."}
                Return
            End If

            If request.ProductId <= 0 Then
                fieldErrors("productId") = {"Product Id is required and must be a positive integer."}
            End If

            If request.CountedQuantity < 0D Then
                fieldErrors("countedQuantity") = {"Counted quantity cannot be negative."}
            ElseIf Not DecimalScaleGuard.IsAtQuantityScale(request.CountedQuantity) Then
                fieldErrors("countedQuantity") = {
                    $"Counted quantity must have no more than {DecimalScaleGuard.QuantityScale} decimal places."}
            End If

            ValidateIdempotencyKey(request.IdempotencyKey, fieldErrors)

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
                .Message = "The stock count request could not be processed because of a validation failure.",
                .CorrelationId = correlationId,
                .Errors = fieldErrors
            })

        End Function

    End Class

End Namespace
