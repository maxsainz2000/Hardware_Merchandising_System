' Merchandising.Contracts.Products.ProductResponse
'
' P5-06 adds AvailableStock, nullable and populated ONLY by the search/
' lookup endpoint (ProductsController.SearchProducts) - GetProduct/
' CreateProduct/UpdateProduct do not join StockBalances and leave it null,
' rather than reporting a misleading 0. Null here means "not computed for
' this call", never "zero stock" - the same distinction a missing
' StockBalances row gets inside the search join itself (COALESCE to 0
' there, because THAT case genuinely is zero).

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

        ''' <summary>Current available stock - only populated by the search/lookup endpoint (spec section 10.3). Null, never 0, when not computed for this call.</summary>
        <JsonPropertyName("availableStock")>
        Public Property AvailableStock As Decimal?

    End Class

End Namespace
