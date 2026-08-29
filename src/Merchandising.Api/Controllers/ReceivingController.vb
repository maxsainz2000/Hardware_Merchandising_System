' Merchandising.Api.Controllers.ReceivingController
'
' P4-05: spec section 10.1's `POST /purchase-orders/{id}/receive` -
' implemented here as `POST /api/v1/receipts`, its own resource rather than a
' purchase-order sub-route, because a receipt is itself the durable record
' spec section 12 names (Receipts/ReceiptLines) and spec section 14's later
' "Goods-receiving history" report reads receipts, not purchase-order
' actions.
'
' Gated by Receiving.Confirm - PolicyRegistry's InventoryAndAbove policy,
' pre-registered at P3-01/P3-03 with no live route until this card (spec
' section 9: "Inventory Clerk 'receiving confirmation'"). Receiving.Prepare
' (ProcurementAndAbove) has no endpoint yet; nothing in this card's Done-when
' asks for one.
'
' WHAT THIS CONTROLLER OWNS, AND WHAT IT DELIBERATELY DOES NOT - the same
' division PurchaseOrdersController's header states. It owns HTTP shape only:
' parsing, refusing a malformed request with field-level detail (ADR-014),
' and mapping a service outcome onto a status code. Every business rule -
' order status, line identity, over-receiving's backstop, the atomic five
' effects - is decided inside ReceivingService, in one transaction. There is
' no courtesy pre-check read here (contrast CreatePurchaseOrder's
' CheckReferencesAsync): ReceivingService's own outcomes already carry
' PurchaseOrderNotFound and LineNotFound with everything this controller
' needs to build a response, the same simpler shape Submit/Cancel/Close
' already use.

Imports System.Collections.Generic
Imports System.Linq
Imports System.Security.Claims
Imports System.Threading.Tasks
Imports Merchandising.Api.Middleware
Imports Merchandising.Api.Receiving
Imports Merchandising.Api.Security
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Receiving
Imports Merchandising.Domain
Imports Merchandising.Domain.Procurement
Imports Merchandising.Domain.Security
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Mvc

Namespace Controllers

    <ApiController>
    <Route("api/v1/receipts")>
    Public Class ReceivingController
        Inherits ControllerBase

        ''' <summary>Matches Receipts.ReferenceNumber VARCHAR(50) (0009) - refused here rather than surfacing as ERROR 1406.</summary>
        Private Const MaxReferenceNumberLength As Integer = 50

        Private ReadOnly _receivingService As ReceivingService

        Public Sub New(receivingService As ReceivingService)
            _receivingService = receivingService
        End Sub

        ''' <summary>
        ''' Receives goods against an Approved (or already PartiallyReceived)
        ''' purchase order. One transaction commits the receipt, its lines,
        ''' the stock effects and the audit event, or none of them (spec
        ''' section 11).
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.ReceivingConfirm)>
        <AuditRequired>
        <HttpPost>
        Public Async Function ReceiveGoods(<FromBody> request As ReceiveGoodsRequest) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            ValidateRequestShape(request, fieldErrors)

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

            Dim outcome As ReceivingOutcome =
                Await _receivingService.ReceiveAsync(
                    request.PurchaseOrderId, request.ReferenceNumber, request.Lines, actorUserId, correlationId,
                    request.IdempotencyKey, cancellationToken:=HttpContext.RequestAborted)

            Select Case outcome.Kind

                Case ReceivingOutcomeKind.Created
                    Return Created($"/api/v1/receipts/{outcome.Response.Id}", outcome.Response)

                Case ReceivingOutcomeKind.Replayed
                    ' ADR-007: the ORIGINAL committed result, byte for byte -
                    ' PurchaseOrderService's identical reasoning. 200, not
                    ' 201 - this request received nothing.
                    Return Content(outcome.ReplayPayload, "application/json")

                Case ReceivingOutcomeKind.PurchaseOrderNotFound
                    Return NotFound(New ApiErrorResponse With {
                        .ErrorCode = "PURCHASE_ORDER_NOT_FOUND",
                        .Message = $"No purchase order with Id {request.PurchaseOrderId} exists.",
                        .CorrelationId = correlationId
                    })

                Case ReceivingOutcomeKind.LineNotFound
                    Return ValidationFailed(
                        New Dictionary(Of String, String()) From {
                            {LineFieldFor(request, outcome.OffendingPurchaseOrderLineId),
                             New String() {"No such purchase-order line exists on this order."}}},
                        correlationId)

                Case ReceivingOutcomeKind.DuplicateReferenceNumber
                    Return Conflict(New ApiErrorResponse With {
                        .ErrorCode = "RECEIPT_REFERENCE_NUMBER_ALREADY_USED",
                        .Message = "A receipt with this reference number has already been recorded.",
                        .CorrelationId = correlationId
                    })

                Case ReceivingOutcomeKind.OverReceived
                    ' P4-07: CK_PurchaseOrderLines_ReceivedQuantity (P3-02) is
                    ' the enforced bound; this is the controlled response for
                    ' it, checked before any row is written - see
                    ' ReceivingOutcome's header for why this is a distinct
                    ' code from a CanTransition (status) refusal.
                    Return Conflict(New ApiErrorResponse With {
                        .ErrorCode = ReceivingOutcome.OverReceivedErrorCode,
                        .Message = $"The quantity requested for {LineFieldFor(request, outcome.OffendingPurchaseOrderLineId)} " &
                                   "would exceed what remains to be received on that line.",
                        .CorrelationId = correlationId
                    })

                Case Else ' Refused - PurchaseOrderTransitions.CanTransition's stable error code
                    Return Conflict(New ApiErrorResponse With {
                        .ErrorCode = outcome.ErrorCode,
                        .Message = TransitionRefusalMessage(outcome.ErrorCode),
                        .CorrelationId = correlationId
                    })

            End Select

        End Function

        ' --------------------------------------------------------- validation

        ''' <summary>
        ''' Everything about the request that can be judged without touching
        ''' the database - runs first so an over-scale value or a malformed
        ''' request is refused before a connection is opened and before any
        ''' idempotency key is claimed (ADR-004.1, box 3).
        ''' </summary>
        Private Shared Sub ValidateRequestShape(
            request As ReceiveGoodsRequest, fieldErrors As Dictionary(Of String, String()))

            If request Is Nothing Then
                fieldErrors("request") = {"A request body is required."}
                Return
            End If

            If request.PurchaseOrderId <= 0 Then
                fieldErrors("purchaseOrderId") = {"A purchase order is required."}
            End If

            If String.IsNullOrWhiteSpace(request.ReferenceNumber) Then
                fieldErrors("referenceNumber") = {"A reference number is required."}
            ElseIf request.ReferenceNumber.Length > MaxReferenceNumberLength Then
                fieldErrors("referenceNumber") = {$"Reference number must be {MaxReferenceNumberLength} characters or fewer."}
            End If

            ' ADR-007: required on every write command, validated to the same
            ' canonical 36-character UUID shape IdempotencyKeys.KeyValue is
            ' declared as - PurchaseOrdersController's identical check.
            If String.IsNullOrWhiteSpace(request.IdempotencyKey) Then
                fieldErrors("idempotencyKey") = {"Idempotency key is required."}
            ElseIf Not IsWellFormedIdempotencyKey(request.IdempotencyKey) Then
                fieldErrors("idempotencyKey") = {
                    "Idempotency key must be a UUID in the canonical 36-character form, for example " &
                    "3f2504e0-4f89-41d3-9a0c-0305e82c3301."}
            End If

            If request.Lines Is Nothing OrElse request.Lines.Count = 0 Then
                fieldErrors("lines") = {"A receipt must have at least one line."}
                Return
            End If

            Dim seenLineIds As New HashSet(Of Integer)

            For index As Integer = 0 To request.Lines.Count - 1

                Dim line As ReceiveGoodsLineRequest = request.Lines(index)

                If line Is Nothing Then
                    fieldErrors($"lines[{index}]") = {"A line is required."}
                    Continue For
                End If

                If line.PurchaseOrderLineId <= 0 Then
                    fieldErrors($"lines[{index}].purchaseOrderLineId") = {"A purchase-order line is required."}
                ElseIf Not seenLineIds.Add(line.PurchaseOrderLineId) Then
                    fieldErrors($"lines[{index}].purchaseOrderLineId") = {"This purchase-order line is already named by another line in this receipt."}
                End If

                ValidateQuantity(line.QuantityReceived, $"lines[{index}].quantityReceived", fieldErrors)
                ValidateCost(line.Cost, $"lines[{index}].cost", fieldErrors)

            Next

        End Sub

        ''' <summary>Quantity must be positive (0009's CK_ReceiptLines_QuantityReceived) and already at DECIMAL(19,3) scale.</summary>
        Private Shared Sub ValidateQuantity(
            value As Decimal, field As String, fieldErrors As Dictionary(Of String, String()))

            If value <= 0D Then
                fieldErrors(field) = {"Quantity received must be greater than zero."}
            ElseIf Not DecimalScaleGuard.IsAtQuantityScale(value) Then
                fieldErrors(field) = {
                    $"Quantity received must have no more than {DecimalScaleGuard.QuantityScale} decimal places."}
            End If

        End Sub

        ''' <summary>Cost must be non-negative (CK_ReceiptLines_Cost) and already at DECIMAL(19,4) scale.</summary>
        Private Shared Sub ValidateCost(
            value As Decimal, field As String, fieldErrors As Dictionary(Of String, String()))

            If value < 0D Then
                fieldErrors(field) = {"Cost cannot be negative."}
            ElseIf Not DecimalScaleGuard.IsAtMoneyScale(value) Then
                fieldErrors(field) = {
                    $"Cost must have no more than {DecimalScaleGuard.MoneyScale} decimal places."}
            End If

        End Sub

        Private Shared Function IsWellFormedIdempotencyKey(value As String) As Boolean

            Dim parsed As Guid = Guid.Empty
            Return Guid.TryParseExact(value, "D", parsed) AndAlso parsed <> Guid.Empty

        End Function

        ''' <summary>The field name for a line the SERVICE rejected. The service reports a PurchaseOrderLineId, not a position; this maps it back to the line the caller wrote.</summary>
        Private Shared Function LineFieldFor(request As ReceiveGoodsRequest, purchaseOrderLineId As Integer) As String

            For index As Integer = 0 To request.Lines.Count - 1
                If request.Lines(index).PurchaseOrderLineId = purchaseOrderLineId Then
                    Return $"lines[{index}].purchaseOrderLineId"
                End If
            Next

            Return "lines"

        End Function

        ' ------------------------------------------------------------ results

        Private Function ValidationFailed(
            fieldErrors As Dictionary(Of String, String()), correlationId As String) As IActionResult

            Return BadRequest(New ApiErrorResponse With {
                .ErrorCode = "VALIDATION_FAILED",
                .Message = "The receipt could not be processed because of a validation failure.",
                .CorrelationId = correlationId,
                .Errors = fieldErrors
            })

        End Function

        ''' <summary>Human-readable text for each of PurchaseOrderTransitionErrors' stable codes - PurchaseOrdersController's identical mapping.</summary>
        Private Shared Function TransitionRefusalMessage(errorCode As String) As String

            Select Case errorCode

                Case PurchaseOrderTransitionErrors.Cancelled
                    Return "This purchase order is cancelled and cannot receive goods."

                Case PurchaseOrderTransitionErrors.Closed
                    Return "This purchase order is closed and cannot receive goods."

                Case PurchaseOrderTransitionErrors.FullyReceived
                    Return "This purchase order is already fully received."

                Case Else ' InvalidTransition
                    Return "Goods cannot be received against this purchase order's current status."

            End Select

        End Function

    End Class

End Namespace
