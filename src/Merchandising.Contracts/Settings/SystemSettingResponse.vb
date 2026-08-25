' Merchandising.Contracts.Settings.SystemSettingResponse
'
' P2-05: one row of GET /api/v1/admin/settings, and the 200 body of
' PUT /api/v1/admin/settings/{key}. Value is the setting's current stored
' value, or its SystemSettingRegistry default if the key has never been
' written - a caller cannot tell the difference from this shape alone,
' which is correct: both are the value the system is actually using.

Imports System.Text.Json.Serialization

Namespace Settings

    Public NotInheritable Class SystemSettingResponse

        <JsonPropertyName("key")>
        Public Property Key As String = String.Empty

        <JsonPropertyName("value")>
        Public Property Value As String = String.Empty

    End Class

End Namespace
