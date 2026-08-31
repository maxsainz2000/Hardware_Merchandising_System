' Merchandising.Contracts.Reporting.CurrentStockReportResponse
'
' GET /api/v1/reports/inventory/current-stock - the same Items/TotalCount/
' Page/PageSize/MaxPageSize/Sort/Range shape every other Track B report
' response uses (ReportRangeEnvelope, P6-01). Range.FromDate/ToDate are
' always null - current stock is a point-in-time snapshot with no date
' dimension (docs/report-specification.md section 4 row 8), the same reason
' GET /api/v1/inventory/stock (P4-11) takes no date parameter either.

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Reporting

    Public NotInheritable Class CurrentStockReportResponse

        <JsonPropertyName("range")>
        Public Property Range As ReportRangeEnvelope = New ReportRangeEnvelope()

        <JsonPropertyName("items")>
        Public Property Items As IReadOnlyList(Of CurrentStockReportItemResponse) =
            Array.Empty(Of CurrentStockReportItemResponse)()

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
