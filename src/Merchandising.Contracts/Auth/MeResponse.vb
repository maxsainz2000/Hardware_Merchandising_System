' Merchandising.Contracts.Auth.MeResponse
'
' 200 response body for GET /api/v1/auth/me - the identity a valid session
' token resolves to. No password hash, no token, no internal Id: username
' and roles are all a caller legitimately needs from this endpoint.

Imports System.Text.Json.Serialization

Namespace Auth

    Public NotInheritable Class MeResponse

        <JsonPropertyName("username")>
        Public Property Username As String = String.Empty

        <JsonPropertyName("roles")>
        Public Property Roles As IReadOnlyList(Of String) = Array.Empty(Of String)()

    End Class

End Namespace
