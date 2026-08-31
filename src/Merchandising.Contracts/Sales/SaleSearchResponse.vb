' Merchandising.Contracts.Sales.SaleSearchResponse
'
' P6-02: GET /api/v1/sales - the sales-side "detail endpoint" ADR-023 point 6
' flagged as owed to this card. No GET existed anywhere for Sales before this
' card (confirmed by inspection at P6-01); every one of P6-02's four report
' reconciliation tests needs an INDEPENDENT code path to sum against
' (docs/report-specification.md section 7, ADR-023 point 3), and this is
' that path - a plain, filtered, paginated listing of committed sales, each
' with its own Lines and Payment already embedded (SaleResponse, unchanged
' from P5-07 - no new response shape for a single sale, only this list
' wrapper around it).
'
' SAME "field:direction" / FromDate-ToDate-TimeZone ECHO SHAPE
' PurchaseOrderHistoryResponse (P3-06) ALREADY USES - this document does not
' invent a second pagination contract; docs/report-specification.md section 3
' names this inheritance explicitly.
'
' Gated by Reports.View, not a new Sales.View policy (confirmed with the user
' at P6-02): this endpoint exists to serve reconciliation and reporting, not
' as a cashier-facing sales-history feature - the same "no report needs a new
' grant" principle report-specification.md section 8 already states.

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Sales

    Public NotInheritable Class SaleSearchResponse

        <JsonPropertyName("items")>
        Public Property Items As IReadOnlyList(Of SaleResponse) =
            Array.Empty(Of SaleResponse)()

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

        ''' <summary>The inclusive lower date bound actually applied, store-local (yyyy-MM-dd). Null when unbounded.</summary>
        <JsonPropertyName("fromDate")>
        Public Property FromDate As String = Nothing

        ''' <summary>The inclusive upper date bound actually applied, store-local (yyyy-MM-dd). Null when unbounded.</summary>
        <JsonPropertyName("toDate")>
        Public Property ToDate As String = Nothing

        ''' <summary>Always "Asia/Manila" - Merchandising.Domain.StoreTimeZone.IanaId.</summary>
        <JsonPropertyName("timeZone")>
        Public Property TimeZone As String = String.Empty

    End Class

End Namespace
