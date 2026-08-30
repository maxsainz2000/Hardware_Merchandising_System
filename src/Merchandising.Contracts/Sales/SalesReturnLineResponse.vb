' Merchandising.Contracts.Sales.SalesReturnLineResponse
'
' One line of a SalesReturnResponse - the committed row plus its Product/
' SaleLines projections (ProductSku, ProductName, UnitPrice), the same
' shape SaleLineResponse/PurchaseReturnLineResponse already use.

Imports System.Text.Json.Serialization

Namespace Sales

    Public NotInheritable Class SalesReturnLineResponse

        <JsonPropertyName("id")>
        Public Property Id As Integer

        <JsonPropertyName("saleLineId")>
        Public Property SaleLineId As Integer

        <JsonPropertyName("productId")>
        Public Property ProductId As Integer

        <JsonPropertyName("productSku")>
        Public Property ProductSku As String = String.Empty

        <JsonPropertyName("productName")>
        Public Property ProductName As String = String.Empty

        <JsonPropertyName("quantityReturned")>
        Public Property QuantityReturned As Decimal

        ''' <summary>Joined from SaleLines.UnitPrice - never re-captured on this row (0012's own migration comment).</summary>
        <JsonPropertyName("unitPrice")>
        Public Property UnitPrice As Decimal

        <JsonPropertyName("restocksItem")>
        Public Property RestocksItem As Boolean

        <JsonPropertyName("createdAtUtc")>
        Public Property CreatedAtUtc As DateTime

    End Class

End Namespace
