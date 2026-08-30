' Merchandising.Contracts.Products.ProductSearchResponse
'
' GET /api/v1/products - spec section 13 "List endpoints define pagination,
' maximum page size, sorting, filtering". A concrete type rather than a
' generic PagedResponse(Of T): this is the first list endpoint Phase 2
' builds, and no second caller needing the same generic shape exists yet
' (CLAUDE.md: no abstraction ahead of a second, real use).
'
' P5-06 brings this up to the P3-08-verified P3-03 contract
' PurchaseOrderSearchResponse already carries: MaxPageSize and Sort, echoed
' rather than left for the caller to infer (that class's own header explains
' why). IncludeInactive is new here specifically - P5-06's own Done-when box
' 2 asks that an inactive-excluded default be something the RESPONSE says,
' not only something the caller has to already know to ask for.

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

        ''' <summary>The largest page size this endpoint will honour, whatever the caller asked for.</summary>
        <JsonPropertyName("maxPageSize")>
        Public Property MaxPageSize As Integer

        ''' <summary>The sort actually applied, in the "field:direction" form the sort query parameter accepts.</summary>
        <JsonPropertyName("sort")>
        Public Property Sort As String = String.Empty

        ''' <summary>Whether inactive products were included - False (the default) unless the caller asked otherwise.</summary>
        <JsonPropertyName("includeInactive")>
        Public Property IncludeInactive As Boolean

    End Class

End Namespace
