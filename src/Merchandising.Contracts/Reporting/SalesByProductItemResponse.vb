' Merchandising.Contracts.Reporting.SalesByProductItemResponse
'
' P6-02: one row of spec section 14's "Sales by product" - "Quantity sold,
' returned quantity, net quantity, gross sales value, and captured cost
' basis for the selected period."
'
' GrossSalesValue sums SaleLines.LineTotal - the captured, already-rounded
' value (0011_pos.sql's own header), never Quantity * a re-read
' Products.Price. CapturedCostBasis sums SaleLines.Cost * SaleLines.Quantity -
' the captured unit cost, never Products.Cost - docs/report-specification.md
' section 5's rule, and P6-02's own Done-when box 2 asserts this directly by
' changing a product's cost after the sale and confirming the figure is
' unmoved.

Imports System.Text.Json.Serialization

Namespace Reporting

    Public NotInheritable Class SalesByProductItemResponse

        <JsonPropertyName("productId")>
        Public Property ProductId As Integer

        <JsonPropertyName("productSku")>
        Public Property ProductSku As String = String.Empty

        <JsonPropertyName("productName")>
        Public Property ProductName As String = String.Empty

        <JsonPropertyName("quantitySold")>
        Public Property QuantitySold As Decimal

        <JsonPropertyName("quantityReturned")>
        Public Property QuantityReturned As Decimal

        ''' <summary>QuantitySold - QuantityReturned.</summary>
        <JsonPropertyName("netQuantity")>
        Public Property NetQuantity As Decimal

        ''' <summary>Sum of SaleLines.LineTotal - the captured value, never re-derived from a current price.</summary>
        <JsonPropertyName("grossSalesValue")>
        Public Property GrossSalesValue As Decimal

        ''' <summary>Sum of SaleLines.Cost * SaleLines.Quantity - the captured cost, never Products.Cost (docs/report-specification.md section 5).</summary>
        <JsonPropertyName("capturedCostBasis")>
        Public Property CapturedCostBasis As Decimal

    End Class

End Namespace
