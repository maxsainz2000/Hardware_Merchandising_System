' Merchandising.Contracts.Auth.LoginRequest
'
' POST /api/v1/auth/login request body (spec section 13). Shared between
' Merchandising.Api and, from P1-15 onward, Merchandising.ClientCommon -
' Contracts has no dependency on Infrastructure or MySqlConnector, so this
' type is safe for a client project to reference (CLAUDE.md section 4).

Imports System.Text.Json.Serialization

Namespace Auth

    Public NotInheritable Class LoginRequest

        <JsonPropertyName("username")>
        Public Property Username As String = String.Empty

        <JsonPropertyName("password")>
        Public Property Password As String = String.Empty

    End Class

End Namespace
