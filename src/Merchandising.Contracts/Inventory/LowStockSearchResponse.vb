' Merchandising.Contracts.Inventory.LowStockSearchResponse
'
' GET /api/v1/inventory/low-stock - same Items/TotalCount/Page/PageSize/
' MaxPageSize/Sort shape StockSearchResponse (P4-11) uses.

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Inventory

    Public NotInheritable Class LowStockSearchResponse

        <JsonPropertyName("items")>
        Public Property Items As IReadOnlyList(Of LowStockItemResponse) =
            Array.Empty(Of LowStockItemResponse)()

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
