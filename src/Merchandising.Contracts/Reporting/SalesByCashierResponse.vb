' Merchandising.Contracts.Reporting.SalesByCashierResponse
'
' P6-02: GET /api/v1/reports/sales/by-cashier's response envelope - the same
' page/pageSize/sort/range echo shape SalesByProductResponse uses.

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Reporting

    Public NotInheritable Class SalesByCashierResponse

        <JsonPropertyName("range")>
        Public Property Range As ReportRangeEnvelope

        <JsonPropertyName("items")>
        Public Property Items As IReadOnlyList(Of SalesByCashierItemResponse) =
            Array.Empty(Of SalesByCashierItemResponse)()

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
