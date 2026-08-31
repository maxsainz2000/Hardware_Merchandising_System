' Merchandising.Contracts.Reporting.PurchaseOrderHistoryReportResponse
'
' GET /api/v1/reports/procurement/purchase-orders - the same Items/
' TotalCount/Page/PageSize/MaxPageSize/Sort/Range shape every other Track B
' report response uses (ReportRangeEnvelope, P6-01).

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Reporting

    Public NotInheritable Class PurchaseOrderHistoryReportResponse

        <JsonPropertyName("range")>
        Public Property Range As ReportRangeEnvelope = New ReportRangeEnvelope()

        <JsonPropertyName("items")>
        Public Property Items As IReadOnlyList(Of PurchaseOrderHistoryReportItemResponse) =
            Array.Empty(Of PurchaseOrderHistoryReportItemResponse)()

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
