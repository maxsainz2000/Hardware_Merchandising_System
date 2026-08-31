' Merchandising.Contracts.Reporting.LowStockReportResponse
'
' GET /api/v1/reports/inventory/low-stock - spec section 14 row 9. Items is
' Merchandising.Contracts.Inventory.LowStockItemResponse ITSELF, not a new
' report-shaped duplicate: this report's Done-when box reuses P4-11's
' existing SearchLowStockAsync logic rather than restating the threshold
' comparison (tasks.md P6-05), and reusing its response item type too is the
' same discipline carried one layer further - there is no second definition
' of "low stock" anywhere in this codebase, only one query and one shape,
' wrapped here in the report envelope (Range/paging). Range.FromDate/ToDate
' are always null - see CurrentStockReportResponse's header for why a
' point-in-time snapshot carries no date dimension.

Imports System.Collections.Generic
Imports System.Text.Json.Serialization
Imports Merchandising.Contracts.Inventory

Namespace Reporting

    Public NotInheritable Class LowStockReportResponse

        <JsonPropertyName("range")>
        Public Property Range As ReportRangeEnvelope = New ReportRangeEnvelope()

        <JsonPropertyName("items")>
        Public Property Items As IReadOnlyList(Of LowStockItemResponse) =
            Array.Empty(Of LowStockItemResponse)()

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
