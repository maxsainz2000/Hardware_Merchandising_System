' Merchandising.Contracts.Maintenance.ReleaseMaintenanceRequest
'
' Spec section 15 step 7: "releases maintenance mode ONLY AFTER VERIFICATION
' SUCCEEDS." That makes the verification result a required input to the
' release, not a formality - the API refuses to release on a False.
'
' Deliberately not a bare POST with no body. An empty release call would make
' "I verified" the default, and the whole point of step 7 is that releasing
' without verifying should take a deliberate lie rather than a missing field.

Imports System.Text.Json.Serialization

Namespace Maintenance

    Public NotInheritable Class ReleaseMaintenanceRequest

        ''' <summary>
        ''' Whether post-restore verification succeeded (spec section 15
        ''' step 6: expected users, products, balances and recent transactions
        ''' are present). False is refused - the lock stays held.
        ''' </summary>
        <JsonPropertyName("verificationPassed")>
        Public Property VerificationPassed As Boolean

        ''' <summary>What was checked, for the lock row and the audit trail.</summary>
        <JsonPropertyName("detail")>
        Public Property Detail As String = String.Empty

    End Class

End Namespace
