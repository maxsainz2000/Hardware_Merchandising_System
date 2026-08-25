' Merchandising.Api.Security.SelfApprovalRequirement
'
' P2-02: a marker requirement - no data of its own, because the comparison
' it needs (actor vs resource owner) only exists once both the ClaimsPrincipal
' and the resource are in hand, which is SelfApprovalHandler's job.
' Registered on PurchaseOrders.Approve and Adjustments.Approve in
' AuthorizationPolicyRegistration, alongside the ordinary RequireRole check -
' both must pass.

Imports Microsoft.AspNetCore.Authorization

Namespace Security

    ''' <summary>Denies authorization when the current user is the same user who requested the resource being approved.</summary>
    Public NotInheritable Class SelfApprovalRequirement
        Implements IAuthorizationRequirement

    End Class

End Namespace
