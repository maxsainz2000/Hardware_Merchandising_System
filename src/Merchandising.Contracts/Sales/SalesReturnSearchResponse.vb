' Merchandising.Contracts.Sales.SalesReturnSearchResponse
'
' P6-03: wrapper for GET /api/v1/sales/returns - the detail endpoint the
' returns-and-cancellations and product-performance reports (spec section 14
' rows 5 and 12) reconcile against, the same "add the missing detail
' endpoint in the card that needs it" scope addition P6-02 made for
' GET /api/v1/sales (docs/report-specification.md section 7, ADR-023 point
' 6). Gated by Reports.View, not a new Sales.View/SalesReturns.View policy -
' this endpoint exists to serve report reconciliation, not as a cashier- or
' approver-facing feature (those are already served by the POST routes on
' SalesReturnsController); the identical reasoning P6-02 recorded for
' GET /api/v1/sales, applied here rather than re-decided.

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Sales

    Public NotInheritable Class SalesReturnSearchResponse

        <JsonPropertyName("items")>
        Public Property Items As IReadOnlyList(Of SalesReturnResponse) =
            Array.Empty(Of SalesReturnResponse)()

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

        <JsonPropertyName("fromDate")>
        Public Property FromDate As String = Nothing

        <JsonPropertyName("toDate")>
        Public Property ToDate As String = Nothing

        <JsonPropertyName("timeZone")>
        Public Property TimeZone As String = String.Empty

    End Class

End Namespace
