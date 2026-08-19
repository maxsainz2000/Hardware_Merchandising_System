' Merchandising.Domain.Security.AuthenticationPolicy
'
' The "configured window" spec section 9 refers to for account lockout, and
' the session lifetime ADR-005 settled on. Pure constants, no I/O - kept in
' Domain so the lockout rule is stated once and is not duplicated between
' Merchandising.Api (which enforces it) and any future admin tool that needs
' to display it.

Namespace Security

    ''' <summary>
    ''' Login-protection and session constants. Not yet exposed through
    ''' SystemSettings for runtime configuration - a fixed policy is enough
    ''' to prove the rule for the Phase 1 Foundation Proof-of-Concept, and
    ''' the values are the ones spec section 9 and ADR-005 name directly.
    ''' </summary>
    Public NotInheritable Class AuthenticationPolicy

        ''' <summary>Failed attempts within the window before an account locks (spec section 9).</summary>
        Public Const MaxFailedLoginAttempts As Integer = 5

        ''' <summary>How long a locked account stays locked once the threshold is reached.</summary>
        Public Shared ReadOnly LockoutDuration As TimeSpan = TimeSpan.FromMinutes(15)

        ''' <summary>
        ''' How long an issued session token remains valid. ADR-005's
        ''' baseline lean called this "expiry matched to a work shift"; the
        ''' opaque-token decision kept that reasoning even though it no
        ''' longer needs a JWT expiry claim to carry it.
        ''' </summary>
        Public Shared ReadOnly SessionLifetime As TimeSpan = TimeSpan.FromHours(8)

        Private Sub New()
        End Sub

    End Class

End Namespace
