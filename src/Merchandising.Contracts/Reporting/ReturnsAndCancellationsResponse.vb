' Merchandising.Contracts.Reporting.ReturnsAndCancellationsResponse
'
' GET /api/v1/reports/sales/returns - spec section 14 row 5. Same
' Range/Items/TotalCount/Page/PageSize/MaxPageSize/Sort shape P6-02's
' SalesByProductResponse/SalesByCashierResponse already establish.

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Reporting

    Public NotInheritable Class ReturnsAndCancellationsResponse

        <JsonPropertyName("range")>
        Public Property Range As ReportRangeEnvelope

        <JsonPropertyName("items")>
        Public Property Items As IReadOnlyList(Of ReturnsAndCancellationsItemResponse) =
            Array.Empty(Of ReturnsAndCancellationsItemResponse)()

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
