' Merchandising.Contracts.Inventory.StockMovementItemResponse
'
' One item of GET /api/v1/inventory/stock/movements (Stock.ReviewMovements,
' P4-11) - a StockMovements row as StockMovementWriter wrote it.

Imports System.Text.Json.Serialization

Namespace Inventory

    Public NotInheritable Class StockMovementItemResponse

        <JsonPropertyName("id")>
        Public Property Id As Integer

        <JsonPropertyName("productId")>
        Public Property ProductId As Integer

        ''' <summary>Signed - a decrement is negative, an increase is positive.</summary>
        <JsonPropertyName("delta")>
        Public Property Delta As Decimal

        <JsonPropertyName("quantityBefore")>
        Public Property QuantityBefore As Decimal

        <JsonPropertyName("quantityAfter")>
        Public Property QuantityAfter As Decimal

        <JsonPropertyName("reason")>
        Public Property Reason As String = String.Empty

        <JsonPropertyName("actorUserId")>
        Public Property ActorUserId As Integer

        <JsonPropertyName("correlationId")>
        Public Property CorrelationId As String = String.Empty

        <JsonPropertyName("createdAtUtc")>
        Public Property CreatedAtUtc As DateTime

    End Class

End Namespace
