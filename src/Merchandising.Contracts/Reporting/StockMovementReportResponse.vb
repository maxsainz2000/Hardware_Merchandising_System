' Merchandising.Contracts.Reporting.StockMovementReportResponse
'
' GET /api/v1/reports/inventory/stock-movements - spec section 14 row 10.
' Unlike GET /api/v1/inventory/stock/movements (P4-11), productId is
' OPTIONAL here: omitting it returns every product's movements in the date
' window, which the report's own Done-when box needs - "current stock and
' the movement report agree with each other for every product," not one
' product at a time.

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Reporting

    Public NotInheritable Class StockMovementReportResponse

        <JsonPropertyName("range")>
        Public Property Range As ReportRangeEnvelope = New ReportRangeEnvelope()

        <JsonPropertyName("items")>
        Public Property Items As IReadOnlyList(Of StockMovementReportItemResponse) =
            Array.Empty(Of StockMovementReportItemResponse)()

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
