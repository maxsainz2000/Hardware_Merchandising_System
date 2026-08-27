' Merchandising.Contracts.Procurement.PurchaseOrderHistoryResponse
'
' GET /api/v1/purchase-orders/history - same Items/TotalCount/Page/PageSize/
' MaxPageSize/Sort shape PurchaseOrderSearchResponse (P3-03) already uses,
' plus the date-range fields THIS endpoint has that the plain list does not.
'
' FromDate/ToDate/TimeZone ECHO WHAT WAS ACTUALLY APPLIED - spec section 14:
' "show the selected date range." Null means unbounded on that side, not
' "today" or any other silent default; a caller must never have to guess
' which boundary the server used to decide a store-local calendar day.

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Procurement

    Public NotInheritable Class PurchaseOrderHistoryResponse

        <JsonPropertyName("items")>
        Public Property Items As IReadOnlyList(Of PurchaseOrderHistoryItemResponse) =
            Array.Empty(Of PurchaseOrderHistoryItemResponse)()

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

        ''' <summary>The IANA id the date range was interpreted against - Merchandising.Domain.StoreTimeZone.IanaId, always "Asia/Manila".</summary>
        <JsonPropertyName("timeZone")>
        Public Property TimeZone As String = String.Empty

    End Class

End Namespace
