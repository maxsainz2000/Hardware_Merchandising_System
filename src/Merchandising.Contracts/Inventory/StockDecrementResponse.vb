' Merchandising.Contracts.Inventory.StockDecrementResponse
'
' 200 response body for POST /api/v1/inventory/stock/decrement. Carries the
' before/after balance (proving the conditional update actually moved the
' number, not just that it reported success) and the movement row's Id so a
' caller can cross-reference StockMovements directly.

Imports System.Text.Json.Serialization

Namespace Inventory

    Public NotInheritable Class StockDecrementResponse

        <JsonPropertyName("productId")>
        Public Property ProductId As Integer

        <JsonPropertyName("movementId")>
        Public Property MovementId As Integer

        <JsonPropertyName("quantityBefore")>
        Public Property QuantityBefore As Decimal

        <JsonPropertyName("quantityAfter")>
        Public Property QuantityAfter As Decimal

        <JsonPropertyName("correlationId")>
        Public Property CorrelationId As String = String.Empty

    End Class

End Namespace
