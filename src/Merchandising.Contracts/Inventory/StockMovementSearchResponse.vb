' Merchandising.Contracts.Inventory.StockMovementSearchResponse
'
' GET /api/v1/inventory/stock/movements - same Items/TotalCount/Page/PageSize/
' MaxPageSize/Sort/FromDate/ToDate/TimeZone shape PurchaseOrderHistoryResponse
' (P3-06) established for a date-bounded report surface, plus the two fields
' unique to a single-product ledger read: which product, and its CURRENT
' balance.
'
' CURRENTBALANCE IS ECHOED HERE SO A CALLER CAN RECONCILE WITHOUT A SECOND
' REQUEST - the card's own instruction ("shape them so those reports
' reconcile rather than re-query"). Summing every returned Delta across the
' full unfiltered history must equal this value - the same invariant P4-01
' asserts as a standing server-side check, now provable by a caller of this
' endpoint too.

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Inventory

    Public NotInheritable Class StockMovementSearchResponse

        <JsonPropertyName("items")>
        Public Property Items As IReadOnlyList(Of StockMovementItemResponse) =
            Array.Empty(Of StockMovementItemResponse)()

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

        <JsonPropertyName("productId")>
        Public Property ProductId As Integer

        ''' <summary>StockBalances.Quantity for ProductId at the moment of this read - not affected by FromDate/ToDate, which bound only which movement rows are listed.</summary>
        <JsonPropertyName("currentBalance")>
        Public Property CurrentBalance As Decimal

        ''' <summary>The inclusive lower date bound actually applied, store-local (yyyy-MM-dd). Null when unbounded.</summary>
        <JsonPropertyName("fromDate")>
        Public Property FromDate As String = Nothing

        ''' <summary>The inclusive upper date bound actually applied, store-local (yyyy-MM-dd). Null when unbounded.</summary>
        <JsonPropertyName("toDate")>
        Public Property ToDate As String = Nothing

        ''' <summary>The IANA id the date range was interpreted against - Merchandising.Domain.StoreTimeZone.IanaId, always "Asia/Manila".</summary>
        <JsonPropertyName("timeZone")>
        Public Property TimeZone As String = String.Empty

    End Class

End Namespace
