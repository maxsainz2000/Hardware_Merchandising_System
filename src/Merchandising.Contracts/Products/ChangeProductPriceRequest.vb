' Merchandising.Contracts.Products.ChangeProductPriceRequest
'
' PUT /api/v1/products/{id}/price body. Both fields optional - a caller may
' change only Price, only Cost, or both in the same command (spec section
' 11: "Price changed | Current price, price-history record, and audit event
' commit together" covers either). At least one must actually differ from
' the product's current value - PriceChangeService rejects a request where
' nothing would change (see its own header).

Imports System.Text.Json.Serialization

Namespace Products

    Public NotInheritable Class ChangeProductPriceRequest

        <JsonPropertyName("price")>
        Public Property Price As Decimal?

        <JsonPropertyName("cost")>
        Public Property Cost As Decimal?

    End Class

End Namespace
