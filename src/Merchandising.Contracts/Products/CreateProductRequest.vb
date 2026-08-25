' Merchandising.Contracts.Products.CreateProductRequest
'
' POST /api/v1/products body. No IsActive - a created product is always
' Active; deactivation is its own endpoint (P2-09), not a creation flag. No
' RowVersion - there is nothing to compare against yet.

Imports System.Text.Json.Serialization

Namespace Products

    Public NotInheritable Class CreateProductRequest

        <JsonPropertyName("sku")>
        Public Property Sku As String = String.Empty

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

        <JsonPropertyName("price")>
        Public Property Price As Decimal

        <JsonPropertyName("cost")>
        Public Property Cost As Decimal

        <JsonPropertyName("reorderLevel")>
        Public Property ReorderLevel As Decimal?

    End Class

End Namespace
