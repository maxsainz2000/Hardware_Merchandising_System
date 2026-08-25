' Merchandising.Contracts.Suppliers.UpdateSupplierRequest
'
' PUT /api/v1/suppliers/{id} body. Deliberately excludes IsActive - the
' deactivate/reactivate lifecycle owns that (mirrors P2-09's
' UpdateProductRequest precedent). Name IS included, unlike Product.Sku on
' UpdateProductRequest: a supplier's name is ordinary contact/maintenance
' data (spec section 10.1), not an immutable identifier a downstream record
' keys off of the way a Product's Sku is - confirmed with the user before
' implementing.

Imports System.Text.Json.Serialization

Namespace Suppliers

    Public NotInheritable Class UpdateSupplierRequest

        <JsonPropertyName("name")>
        Public Property Name As String = String.Empty

        <JsonPropertyName("contactName")>
        Public Property ContactName As String = Nothing

        <JsonPropertyName("phone")>
        Public Property Phone As String = Nothing

        <JsonPropertyName("email")>
        Public Property Email As String = Nothing

        <JsonPropertyName("address")>
        Public Property Address As String = Nothing

    End Class

End Namespace
