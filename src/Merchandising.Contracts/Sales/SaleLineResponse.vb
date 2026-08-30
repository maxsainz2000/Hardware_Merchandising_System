' Merchandising.Contracts.Sales.SaleLineResponse
'
' One line of a committed sale. UnitPrice/Cost are the CAPTURED, effective
' values (Merchandising.Domain.Sales.SaleLine, P5-01) - never re-read from
' Products at response time, the same immutability this card's own Done-when
' box 2 tests directly (change the product's price after the sale; the line
' must be unmoved). ProductSku/ProductName are a live join, the same
' asymmetry ReceiptLineResponse's header argues for - display text may
' change; the transactional figures never do.

Imports System.Text.Json.Serialization

Namespace Sales

    Public NotInheritable Class SaleLineResponse

        <JsonPropertyName("id")>
        Public Property Id As Integer

        <JsonPropertyName("productId")>
        Public Property ProductId As Integer

        <JsonPropertyName("productSku")>
        Public Property ProductSku As String = String.Empty

        <JsonPropertyName("productName")>
        Public Property ProductName As String = String.Empty

        <JsonPropertyName("quantity")>
        Public Property Quantity As Decimal

        ''' <summary>The effective unit price captured at sale time - never Products.Price re-read later.</summary>
        <JsonPropertyName("unitPrice")>
        Public Property UnitPrice As Decimal

        ''' <summary>The effective unit cost captured at sale time - never Products.Cost re-read later.</summary>
        <JsonPropertyName("cost")>
        Public Property Cost As Decimal

        <JsonPropertyName("lineTotal")>
        Public Property LineTotal As Decimal

    End Class

End Namespace
