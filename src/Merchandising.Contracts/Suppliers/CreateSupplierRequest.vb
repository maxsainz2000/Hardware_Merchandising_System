' Merchandising.Contracts.Suppliers.CreateSupplierRequest
'
' POST /api/v1/suppliers body. No IsActive - a created supplier is always
' Active; deactivation is its own endpoint (mirrors P2-09's ProductLifecycle
' shape). No RowVersion - there is nothing to compare against yet.

Imports System.Text.Json.Serialization

Namespace Suppliers

    Public NotInheritable Class CreateSupplierRequest

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
