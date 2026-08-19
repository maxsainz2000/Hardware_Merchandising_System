' Merchandising.Api.Security.LoginOutcome
'
' Result of AuthService.LoginAsync. A discriminated result rather than
' throwing for "wrong password" or "locked" - those are expected outcomes
' of calling the endpoint, not exceptional conditions, and AuthController
' needs to map each to a specific status code (200, 401, 423).

Imports Merchandising.Contracts.Auth

Namespace Security

    Public Enum LoginOutcomeKind
        Success
        InvalidCredentials
        Locked
    End Enum

    Public NotInheritable Class LoginOutcome

        Public Property Kind As LoginOutcomeKind
        Public Property Response As LoginResponse

        Private Sub New()
        End Sub

        Public Shared Function Success(response As LoginResponse) As LoginOutcome
            Return New LoginOutcome With {.Kind = LoginOutcomeKind.Success, .Response = response}
        End Function

        Public Shared Function InvalidCredentials() As LoginOutcome
            Return New LoginOutcome With {.Kind = LoginOutcomeKind.InvalidCredentials}
        End Function

        Public Shared Function Locked() As LoginOutcome
            Return New LoginOutcome With {.Kind = LoginOutcomeKind.Locked}
        End Function

    End Class

End Namespace
