' Merchandising.Contracts.Reporting.ProductPerformanceResponse
'
' GET /api/v1/reports/sales/product-performance - spec section 14 row 12.
' Range describes the window NetQuantity/NetSalesValue/RecordedCostEstimate
' respect - it does NOT describe CurrentStockQuantity, which is always live
' (ProductPerformanceItemResponse's own header).

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Reporting

    Public NotInheritable Class ProductPerformanceResponse

        <JsonPropertyName("range")>
        Public Property Range As ReportRangeEnvelope

        <JsonPropertyName("items")>
        Public Property Items As IReadOnlyList(Of ProductPerformanceItemResponse) =
            Array.Empty(Of ProductPerformanceItemResponse)()

        <JsonPropertyName("totalCount")>
        Public Property TotalCount As Integer

        <JsonPropertyName("page")>
        Public Property Page As Integer

        <JsonPropertyName("pageSize")>
        Public Property PageSize As Integer

        <JsonPropertyName("maxPageSize")>
        Public Property MaxPageSize As Integer

        <JsonPropertyName("sort")>
        Public Property Sort As String = String.Empty

    End Class

End Namespace
