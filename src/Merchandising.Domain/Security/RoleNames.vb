' Merchandising.Domain.Security.RoleNames
'
' The five spec section 9 roles, exactly as seeded into Roles.Name by
' db/migrations/0001_foundation.sql and exactly as SessionAuthenticationHandler
' writes them into ClaimTypes.Role at login. PolicyRegistry is the "policy
' layer" P2-01's done-when box promised these would key on - this class is
' that key, so a typo in a role name fails to compile instead of failing to
' authorize at 2am during a demo.

Namespace Security

    ''' <summary>The stable role identifiers the authorization layer keys on.</summary>
    Public NotInheritable Class RoleNames

        Public Const SuperAdmin As String = "SuperAdmin"
        Public Const Admin As String = "Admin"
        Public Const ProcurementOfficer As String = "ProcurementOfficer"
        Public Const InventoryClerk As String = "InventoryClerk"
        Public Const Cashier As String = "Cashier"

        Private Sub New()
        End Sub

    End Class

End Namespace
