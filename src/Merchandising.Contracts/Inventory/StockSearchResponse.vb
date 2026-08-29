' Merchandising.Contracts.Inventory.StockSearchResponse
'
' GET /api/v1/inventory/stock - the same Items/TotalCount/Page/PageSize/
' MaxPageSize/Sort shape PurchaseOrderSearchResponse (P3-03) established,
' applied to the Stock.Read surface.

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Inventory

    Public NotInheritable Class StockSearchResponse

        <JsonPropertyName("items")>
        Public Property Items As IReadOnlyList(Of StockBalanceResponse) =
            Array.Empty(Of StockBalanceResponse)()

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
