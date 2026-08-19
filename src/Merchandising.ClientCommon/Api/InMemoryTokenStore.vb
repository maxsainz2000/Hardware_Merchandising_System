' Merchandising.ClientCommon.Api.InMemoryTokenStore
'
' Holds the session bearer token for the lifetime of the process and no
' longer. Spec section 17 keeps credentials off client machines; P1-15's
' acceptance criterion states it more bluntly - "token never written to disk".
'
' The guarantee here is structural rather than defensive: this class has no
' file, registry, settings, or isolated-storage API in it, so there is no
' persistence path to disable. Closing the application is the whole of the
' logout story if the network is gone.
'
' Deliberately NOT SecureString. It is obsolete for new development on .NET,
' it does not protect a value that must be handed to HttpClient as a plain
' String anyway, and using it here would buy the appearance of protection
' rather than protection. The real control is that the token is short-lived
' server-side (ADR-005) and can be revoked by logout.

Namespace Api

    ''' <summary>In-process, in-memory storage for the session bearer token.</summary>
    Public NotInheritable Class InMemoryTokenStore

        Private _token As String

        ''' <summary>True when a token is held.</summary>
        Public ReadOnly Property HasToken As Boolean
            Get
                Return Not String.IsNullOrEmpty(_token)
            End Get
        End Property

        ''' <summary>
        ''' The held token, or an empty string. Never persisted; never logged.
        ''' </summary>
        Public ReadOnly Property Token As String
            Get
                Return If(_token, String.Empty)
            End Get
        End Property

        ''' <summary>Stores the token issued by a successful login.</summary>
        Public Sub Store(token As String)

            If String.IsNullOrWhiteSpace(token) Then
                Throw New ArgumentException("A token cannot be blank.", NameOf(token))
            End If

            _token = token

        End Sub

        ''' <summary>Discards the held token.</summary>
        Public Sub Clear()

            _token = Nothing

        End Sub

    End Class

End Namespace
