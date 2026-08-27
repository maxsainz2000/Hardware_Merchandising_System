' Merchandising.Contracts.Procurement.PurchaseOrderSearchResponse
'
' GET /api/v1/purchase-orders - the same Items/TotalCount/Page/PageSize shape
' ProductSearchResponse and SupplierSearchResponse already use (spec section
' 13 list-endpoint pagination), plus the two fields this endpoint has that
' they do not: the sort that was applied and the maximum page size the server
' will honour.
'
' WHY Sort AND MaxPageSize ARE IN THE BODY. Spec section 13 requires a list
' endpoint to DEFINE its pagination, maximum page size, sorting and
' filtering. A caller that asks for pageSize=5000 gets 100 rows back; without
' MaxPageSize echoed here it cannot tell that from "there were only 100".
' Same for sort: the server clamps and defaults, and says what it did rather
' than leaving the client to assume. P3-08 documents these; this is the
' running registration P3-08 verifies itself against.

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Procurement

    Public NotInheritable Class PurchaseOrderSearchResponse

        <JsonPropertyName("items")>
        Public Property Items As IReadOnlyList(Of PurchaseOrderSummaryResponse) =
            Array.Empty(Of PurchaseOrderSummaryResponse)()

        <JsonPropertyName("totalCount")>
        Public Property TotalCount As Integer

        <JsonPropertyName("page")>
        Public Property Page As Integer

        <JsonPropertyName("pageSize")>
        Public Property PageSize As Integer

        ''' <summary>The largest page size this endpoint will honour, whatever the caller asked for.</summary>
        <JsonPropertyName("maxPageSize")>
        Public Property MaxPageSize As Integer

        ''' <summary>The sort actually applied, in the "field:direction" form the sort query parameter accepts.</summary>
        <JsonPropertyName("sort")>
        Public Property Sort As String = String.Empty

    End Class

End Namespace
