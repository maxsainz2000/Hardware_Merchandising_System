' Merchandising.Api.Security.SessionTokenGenerator
'
' Creates the opaque bearer token handed to the client (ADR-005) and the
' SHA-256 hash of it that is the only form ever written to the database
' (Sessions.TokenHash) - if the database were ever read by someone without
' the token, they would still not be able to authenticate as the session.

Imports System.Security.Cryptography
Imports System.Text

Namespace Security

    Public NotInheritable Class SessionTokenGenerator

        ''' <summary>Bytes of randomness in a new token. 256 bits.</summary>
        Private Const TokenSizeBytes As Integer = 32

        ''' <summary>
        ''' A new cryptographically random token, Base64Url encoded so it is
        ''' safe to carry as a bearer header value with no further escaping.
        ''' </summary>
        Public Shared Function NewToken() As String

            Dim bytes(TokenSizeBytes - 1) As Byte
            RandomNumberGenerator.Fill(bytes)

            Return Convert.ToBase64String(bytes).
                Replace("+"c, "-"c).
                Replace("/"c, "_"c).
                TrimEnd("="c)

        End Function

        ''' <summary>
        ''' The SHA-256 hex digest of <paramref name="token"/> - what gets
        ''' stored and looked up, never the token itself.
        ''' </summary>
        Public Shared Function Hash(token As String) As String

            If token Is Nothing Then
                Throw New ArgumentNullException(NameOf(token))
            End If

            Dim hashBytes As Byte() = SHA256.HashData(Encoding.UTF8.GetBytes(token))

            Dim builder As New StringBuilder(hashBytes.Length * 2)
            For Each b As Byte In hashBytes
                builder.Append(b.ToString("x2"))
            Next

            Return builder.ToString()

        End Function

    End Class

End Namespace
