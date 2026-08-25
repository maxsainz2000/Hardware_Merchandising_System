' Merchandising.Contracts.Settings.UpdateSystemSettingRequest
'
' PUT /api/v1/admin/settings/{key} request body. The key itself travels in
' the route, not the body - there is exactly one resource being replaced,
' the same shape spec section 13's product endpoints use for their own
' {id} routes.

Imports System.Text.Json.Serialization

Namespace Settings

    Public NotInheritable Class UpdateSystemSettingRequest

        <JsonPropertyName("value")>
        Public Property Value As String = String.Empty

    End Class

End Namespace
