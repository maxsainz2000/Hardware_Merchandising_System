' Merchandising.Contracts.Reporting.StockAdjustmentReportResponse
'
' GET /api/v1/reports/inventory/stock-adjustments - spec section 14 row 11.
' Date filter: StockAdjustments.CreatedAtUtc, the request time - the only
' timestamp this table carries a natural report dimension on.

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Reporting

    Public NotInheritable Class StockAdjustmentReportResponse

        <JsonPropertyName("range")>
        Public Property Range As ReportRangeEnvelope = New ReportRangeEnvelope()

        <JsonPropertyName("items")>
        Public Property Items As IReadOnlyList(Of StockAdjustmentReportItemResponse) =
            Array.Empty(Of StockAdjustmentReportItemResponse)()

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
