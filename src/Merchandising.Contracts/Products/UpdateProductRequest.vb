' Merchandising.Contracts.Products.UpdateProductRequest
'
' PUT /api/v1/products/{id} body. Deliberately excludes Sku (immutable once
' created - no card asks for SKU renumbering, and allowing it would reopen
' the uniqueness race for no requirement), Price/Cost (Products.Manage's own
' definition excludes them - P2-08 owns price/cost changes, atomically with
' PriceHistory), and IsActive (P2-09 owns the deactivate/reactivate
' lifecycle). Absent from the contract, not runtime-rejected - there is no
' code path here that could touch them.

Imports System.Text.Json.Serialization

Namespace Products

    Public NotInheritable Class UpdateProductRequest

        <JsonPropertyName("name")>
        Public Property Name As String = String.Empty

        <JsonPropertyName("description")>
        Public Property Description As String = Nothing

        <JsonPropertyName("categoryId")>
        Public Property CategoryId As Integer?

        <JsonPropertyName("brandId")>
        Public Property BrandId As Integer?

        <JsonPropertyName("unitId")>
        Public Property UnitId As Integer?

        <JsonPropertyName("barcode")>
        Public Property Barcode As String = Nothing

        <JsonPropertyName("reorderLevel")>
        Public Property ReorderLevel As Decimal?

    End Class

End Namespace
