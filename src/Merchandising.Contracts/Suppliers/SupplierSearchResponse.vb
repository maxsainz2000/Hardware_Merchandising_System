' Merchandising.Contracts.Suppliers.SupplierSearchResponse
'
' GET /api/v1/suppliers - mirrors ProductSearchResponse's shape (spec section
' 13 list-endpoint pagination). A concrete type, not a generic PagedResponse
' (Of T) - see ProductSearchResponse's own header.

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Suppliers

    Public NotInheritable Class SupplierSearchResponse

        <JsonPropertyName("items")>
        Public Property Items As IReadOnlyList(Of SupplierResponse) = Array.Empty(Of SupplierResponse)()

        <JsonPropertyName("totalCount")>
        Public Property TotalCount As Integer

        <JsonPropertyName("page")>
        Public Property Page As Integer

        <JsonPropertyName("pageSize")>
        Public Property PageSize As Integer

    End Class

End Namespace
