' Merchandising.Contracts.Reporting.SalesByProductResponse
'
' P6-02: GET /api/v1/reports/sales/by-product's response envelope - the same
' page/pageSize/sort/range echo shape PurchaseOrderHistoryResponse (P3-06)
' already uses, per docs/report-specification.md section 3's inherited
' contract. Only products with at least one Completed sale line in the
' selected period appear - Items is never padded with a zero row for an
' unsold product (this is a SALES report, not a catalogue listing).

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Reporting

    Public NotInheritable Class SalesByProductResponse

        <JsonPropertyName("range")>
        Public Property Range As ReportRangeEnvelope

        <JsonPropertyName("items")>
        Public Property Items As IReadOnlyList(Of SalesByProductItemResponse) =
            Array.Empty(Of SalesByProductItemResponse)()

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
