' Merchandising.Api.Controllers.InventoryController
'
' POST /api/v1/inventory/stock/decrement - spec section 24 Foundation POC
' item 7 ("execute a stock decrement that creates a movement and audit
' record in one transaction"). Gated to the roles spec section 9 assigns
' adjustment responsibility to: Inventory Clerk raises adjustments, Admin
' approves them, Super Admin has blanket access. Cashier decrements stock
' only through a completed sale, which is a Phase 2+ endpoint this task does
' not build (CLAUDE.md's Phase 1 scope discipline).
'
' Field validation - including the ADR-004.1 storage-scale check - happens
' here, at the API boundary a client-supplied value crosses, exactly like
' AuthController's inline validation of username/password. By the time
' StockService.DecrementAsync is called, quantity is already known valid.

Imports Merchandising.Api.Inventory
Imports Merchandising.Api.Middleware
Imports Merchandising.Api.Security
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Inventory
Imports Merchandising.Domain
Imports Merchandising.Domain.Security
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Mvc
Imports System.Security.Claims

Namespace Controllers

    <ApiController>
    <Route("api/v1/inventory")>
    <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.AdjustmentsRequest)>
    Public Class InventoryController
        Inherits ControllerBase

        Private Const MaxReasonLength As Integer = 255

        Private ReadOnly _stockService As StockService

        Public Sub New(stockService As StockService)
            _stockService = stockService
        End Sub

        ''' <summary>
        ''' Decrements one product's stock balance. Returns 200 with the
        ''' before/after balance on success, 400 for a validation failure
        ''' (including an over-scale quantity, ADR-004.1), 409 when the
        ''' current balance cannot satisfy the request (ADR-006) - the same
        ''' response for "no such product" and "not enough stock", since the
        ''' conditional UPDATE's WHERE clause cannot and need not tell them
        ''' apart.
        ''' </summary>
        <HttpPost("stock/decrement")>
        Public Async Function DecrementStock(<FromBody> request As StockDecrementRequest) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            If request Is Nothing OrElse request.ProductId <= 0 Then
                fieldErrors("productId") = {"Product Id is required and must be a positive integer."}
            End If

            If request Is Nothing OrElse request.Quantity <= 0D Then
                fieldErrors("quantity") = {"Quantity is required and must be greater than zero."}
            ElseIf Not fieldErrors.ContainsKey("quantity") Then
                Try
                    DecimalScaleGuard.EnsureQuantityScale(request.Quantity)
                Catch ex As ArgumentException
                    fieldErrors("quantity") = {ex.Message}
                End Try
            End If

            If request Is Nothing OrElse String.IsNullOrWhiteSpace(request.Reason) Then
                fieldErrors("reason") = {"Reason is required."}
            ElseIf request.Reason.Length > MaxReasonLength Then
                fieldErrors("reason") = {$"Reason must be {MaxReasonLength} characters or fewer."}
            End If

            ' P1-14 / ADR-007: required, and validated to the same canonical
            ' 36-character GUID shape CorrelationIdMiddleware enforces on the
            ' header - IdempotencyKeys.KeyValue is CHAR(36) NOT NULL under
            ' STRICT_TRANS_TABLES, so an over-length value must be rejected
            ' here rather than surfacing as an ERROR 1406 from the database.
            If request Is Nothing OrElse String.IsNullOrWhiteSpace(request.IdempotencyKey) Then
                fieldErrors("idempotencyKey") = {"Idempotency key is required."}
            ElseIf Not IsWellFormedIdempotencyKey(request.IdempotencyKey) Then
                fieldErrors("idempotencyKey") = {
                    "Idempotency key must be a UUID in the canonical 36-character form, for example " &
                    "3f2504e0-4f89-41d3-9a0c-0305e82c3301."}
            End If

            If fieldErrors.Count > 0 Then
                Return BadRequest(New ApiErrorResponse With {
                    .ErrorCode = "VALIDATION_FAILED",
                    .Message = "The stock decrement request failed validation.",
                    .CorrelationId = correlationId,
                    .Errors = fieldErrors
                })
            End If

            Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

            Dim outcome As StockDecrementOutcome =
                Await _stockService.DecrementAsync(
                    request.ProductId, request.Quantity, request.Reason, actorUserId, correlationId, request.IdempotencyKey)

            Select Case outcome.Kind

                Case StockDecrementOutcomeKind.Success
                    Return Ok(outcome.Response)

                Case Else
                    Return Conflict(New ApiErrorResponse With {
                        .ErrorCode = "INSUFFICIENT_STOCK",
                        .Message = "The requested quantity is not available for this product.",
                        .CorrelationId = correlationId
                    })

            End Select

        End Function

        ''' <summary>
        ''' True only for the canonical 36-character "D" format - the same
        ''' check CorrelationIdMiddleware applies to X-Correlation-Id, for
        ''' the same reason: Guid.TryParse alone also accepts "B"/"P"/"N"/"X"
        ''' formats that would not round-trip to what the caller sent.
        ''' </summary>
        Private Shared Function IsWellFormedIdempotencyKey(value As String) As Boolean

            Dim parsed As Guid = Guid.Empty
            Return Guid.TryParseExact(value, "D", parsed) AndAlso parsed <> Guid.Empty

        End Function

    End Class

End Namespace
