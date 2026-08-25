' Merchandising.Contracts.Products.ProductResponse

Imports System.Text.Json.Serialization

Namespace Products

    Public NotInheritable Class ProductResponse

        <JsonPropertyName("id")>
        Public Property Id As Integer

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
        Public Property ReorderLevel As Decimal

        <JsonPropertyName("isActive")>
        Public Property IsActive As Boolean

        <JsonPropertyName("rowVersion")>
        Public Property RowVersion As Long

        <JsonPropertyName("createdAtUtc")>
        Public Property CreatedAtUtc As DateTime

        <JsonPropertyName("updatedAtUtc")>
        Public Property UpdatedAtUtc As DateTime

    End Class

End Namespace
