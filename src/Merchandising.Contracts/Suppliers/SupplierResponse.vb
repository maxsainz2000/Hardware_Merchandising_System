' Merchandising.Contracts.Suppliers.SupplierResponse

Imports System.Text.Json.Serialization

Namespace Suppliers

    Public NotInheritable Class SupplierResponse

        <JsonPropertyName("id")>
        Public Property Id As Integer

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
