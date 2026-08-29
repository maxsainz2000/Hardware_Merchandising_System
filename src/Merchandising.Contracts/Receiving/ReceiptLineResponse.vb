' Merchandising.Contracts.Receiving.ReceiptLineResponse
'
' One line of a committed receipt. ProductSku/ProductName are a live join
' against Products (the same asymmetry PurchaseOrderLineResponse's header
' argues for); Cost is captured, the actual received/invoiced value, which
' can differ from the order line's agreed PurchaseCost. MovementId lets a
' caller cross-reference the StockMovements row this line produced, the same
' shape StockDecrementResponse already uses.

Imports System.Text.Json.Serialization

Namespace Receiving

    Public NotInheritable Class ReceiptLineResponse

        <JsonPropertyName("id")>
        Public Property Id As Integer

        <JsonPropertyName("purchaseOrderLineId")>
        Public Property PurchaseOrderLineId As Integer

        <JsonPropertyName("productId")>
        Public Property ProductId As Integer

        <JsonPropertyName("productSku")>
        Public Property ProductSku As String = String.Empty

        <JsonPropertyName("productName")>
        Public Property ProductName As String = String.Empty

        <JsonPropertyName("quantityReceived")>
        Public Property QuantityReceived As Decimal

        <JsonPropertyName("cost")>
        Public Property Cost As Decimal

        <JsonPropertyName("movementId")>
        Public Property MovementId As Integer

        <JsonPropertyName("createdAtUtc")>
        Public Property CreatedAtUtc As DateTime

    End Class

End Namespace
