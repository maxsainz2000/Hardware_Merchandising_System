' Merchandising.Contracts.Reporting.CurrentStockReportItemResponse
'
' P6-05: spec section 14 row 8 - "Product, category, active state, current
' quantity, reorder level, and stock status." CategoryId/CategoryName are
' both nullable - Products.CategoryId is nullable at the database layer
' (0006_product-master.sql), and a product with no category assigned is a
' real, valid state, never coerced to a sentinel.
'
' STOCKSTATUS IS COMPUTED, NOT STORED - ReportService derives it from
' Quantity/ReorderLevel (docs/report-specification.md names no storage for
' it, and CLAUDE.md section 5 reserves StockBalances.Quantity as the one
' persisted balance). "OutOfStock" (Quantity <= 0), "Low" (Quantity <=
' ReorderLevel, the exact threshold StockRepository.SearchLowStockAsync's
' own WHERE clause already uses), else "Normal".

Imports System.Text.Json.Serialization

Namespace Reporting

    Public NotInheritable Class CurrentStockReportItemResponse

        <JsonPropertyName("productId")>
        Public Property ProductId As Integer

        <JsonPropertyName("sku")>
        Public Property Sku As String = String.Empty

        <JsonPropertyName("name")>
        Public Property Name As String = String.Empty

        <JsonPropertyName("categoryId")>
        Public Property CategoryId As Integer?

        <JsonPropertyName("categoryName")>
        Public Property CategoryName As String = Nothing

        <JsonPropertyName("isActive")>
        Public Property IsActive As Boolean

        <JsonPropertyName("quantity")>
        Public Property Quantity As Decimal

        <JsonPropertyName("reorderLevel")>
        Public Property ReorderLevel As Decimal

        ''' <summary>"OutOfStock", "Low", or "Normal" - see this class's header for the rule.</summary>
        <JsonPropertyName("stockStatus")>
        Public Property StockStatus As String = String.Empty

    End Class

End Namespace
