' Merchandising.Contracts.Receiving.PurchaseReturnLineResponse
'
' One line of a committed purchase return. ProductSku/ProductName are a live
' join against Products; Cost is CAPTURED, copied from the originating
' ReceiptLines row at return time, never client-supplied (RecordPurchaseReturnLineRequest's
' header). MovementId is Nothing when RemovesStock is False - no
' StockMovements row exists for that line.

Imports System.Text.Json.Serialization

Namespace Receiving

    Public NotInheritable Class PurchaseReturnLineResponse

        <JsonPropertyName("id")>
        Public Property Id As Integer

        <JsonPropertyName("receiptLineId")>
        Public Property ReceiptLineId As Integer

        <JsonPropertyName("productId")>
        Public Property ProductId As Integer

        <JsonPropertyName("productSku")>
        Public Property ProductSku As String = String.Empty

        <JsonPropertyName("productName")>
        Public Property ProductName As String = String.Empty

        <JsonPropertyName("quantityReturned")>
        Public Property QuantityReturned As Decimal

        <JsonPropertyName("cost")>
        Public Property Cost As Decimal

        <JsonPropertyName("reason")>
        Public Property Reason As String = String.Empty

        <JsonPropertyName("removesStock")>
        Public Property RemovesStock As Boolean

        ''' <summary>The StockMovements row this line produced. Nothing when RemovesStock is False.</summary>
        <JsonPropertyName("movementId")>
        Public Property MovementId As Integer?

        <JsonPropertyName("createdAtUtc")>
        Public Property CreatedAtUtc As DateTime

    End Class

End Namespace
