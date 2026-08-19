' Merchandising.Contracts.Auth.LoginResponse
'
' 200 response body for POST /api/v1/auth/login. Token is the opaque bearer
' value itself (ADR-005) - the client's only copy of it; the server stores
' only its hash. The client must hold it in memory only (spec section 9),
' never write it to disk.

Imports System.Text.Json.Serialization

Namespace Auth

    Public NotInheritable Class LoginResponse

        <JsonPropertyName("token")>
        Public Property Token As String = String.Empty

        <JsonPropertyName("expiresAtUtc")>
        Public Property ExpiresAtUtc As DateTime

        <JsonPropertyName("username")>
        Public Property Username As String = String.Empty

        <JsonPropertyName("roles")>
        Public Property Roles As IReadOnlyList(Of String) = Array.Empty(Of String)()

    End Class

End Namespace
