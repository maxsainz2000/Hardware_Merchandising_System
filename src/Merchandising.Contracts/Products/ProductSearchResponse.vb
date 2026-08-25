' Merchandising.Contracts.Products.ProductSearchResponse
'
' GET /api/v1/products - spec section 13 "List endpoints define pagination,
' maximum page size, sorting, filtering". A concrete type rather than a
' generic PagedResponse(Of T): this is the first list endpoint Phase 2
' builds, and no second caller needing the same generic shape exists yet
' (CLAUDE.md: no abstraction ahead of a second, real use).

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Products

    Public NotInheritable Class ProductSearchResponse

        <JsonPropertyName("items")>
        Public Property Items As IReadOnlyList(Of ProductResponse) = Array.Empty(Of ProductResponse)()

        <JsonPropertyName("totalCount")>
        Public Property TotalCount As Integer

        <JsonPropertyName("page")>
        Public Property Page As Integer

        <JsonPropertyName("pageSize")>
        Public Property PageSize As Integer

    End Class

End Namespace
