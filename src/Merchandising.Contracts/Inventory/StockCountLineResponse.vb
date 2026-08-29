' Merchandising.Contracts.Inventory.StockCountLineResponse
'
' One line of a stock count. SystemQuantity and Variance are CAPTURED at the
' moment this line was recorded, never recomputed on read - spec section
' 10.2's "variance ... at the moment of counting" (card Done-when box 1). A
' StockBalances change after this line was written does not change what is
' returned here.

Imports System.Text.Json.Serialization

Namespace Inventory

    Public NotInheritable Class StockCountLineResponse

        <JsonPropertyName("id")>
        Public Property Id As Integer

        <JsonPropertyName("productId")>
        Public Property ProductId As Integer

        <JsonPropertyName("productSku")>
        Public Property ProductSku As String = String.Empty

        <JsonPropertyName("productName")>
        Public Property ProductName As String = String.Empty

        <JsonPropertyName("countedQuantity")>
        Public Property CountedQuantity As Decimal

        ''' <summary>StockBalances.Quantity as it stood at the moment this line was recorded.</summary>
        <JsonPropertyName("systemQuantity")>
        Public Property SystemQuantity As Decimal

        ''' <summary>CountedQuantity - SystemQuantity, computed and stored once, at count time.</summary>
        <JsonPropertyName("variance")>
        Public Property Variance As Decimal

        <JsonPropertyName("createdAtUtc")>
        Public Property CreatedAtUtc As DateTime

    End Class

End Namespace
