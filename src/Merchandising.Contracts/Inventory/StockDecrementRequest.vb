' Merchandising.Contracts.Inventory.StockDecrementRequest
'
' POST /api/v1/inventory/stock/decrement request body. P1-11 / ADR-006 - the
' Foundation POC's stock-decrement proof (spec section 24 item 7). Quantity
' is validated at the controller: present, positive, at or below
' DecimalScaleGuard.QuantityScale (ADR-004.1).
'
' P1-14 / ADR-007: IdempotencyKey identifies the logical command across
' retries - distinct from the X-Correlation-Id header, which traces one HTTP
' attempt. Required, validated at the controller as a well-formed GUID, the
' same shape correlation Ids already use.

Imports System.Text.Json.Serialization

Namespace Inventory

    Public NotInheritable Class StockDecrementRequest

        <JsonPropertyName("productId")>
        Public Property ProductId As Integer

        <JsonPropertyName("quantity")>
        Public Property Quantity As Decimal

        <JsonPropertyName("reason")>
        Public Property Reason As String = String.Empty

        <JsonPropertyName("idempotencyKey")>
        Public Property IdempotencyKey As String = String.Empty

    End Class

End Namespace
