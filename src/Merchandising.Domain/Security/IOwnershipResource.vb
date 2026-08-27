' Merchandising.Domain.Security.IOwnershipResource
'
' P2-02: the shape SelfApprovalHandler needs to compare "who is approving"
' against "who requested this" - spec section 9's "cannot approve their own
' purchase order" (Procurement Officer) and "cannot approve their own
' adjustment" (Inventory Clerk). No Phase 2 entity implements this yet:
' PurchaseOrder and StockAdjustment are Phase 3 builds. This interface exists
' now, ahead of them, because AuthorizationPolicyRegistration wires the
' requirement onto PurchaseOrders.Approve and Adjustments.Approve at startup
' (CLAUDE.md's "Phase 3 and Phase 5 consume these - they do not add their
' own"), and the requirement needs somewhere to point.
'
' RELOCATED AT P3-04 from Merchandising.Api.Security to here. This interface
' has zero ASP.NET Core dependency - it is pure data shape, the same category
' PolicyRegistry/RoleNames/PolicyDefinition already occupy in this namespace,
' distinct from the framework-dependent handlers that consume it
' (SelfApprovalHandler stays in Merchandising.Api.Security). It had to move
' before Merchandising.Domain.Entities.PurchaseOrder could implement it
' directly, as ADR-017 section 6 always intended: Domain depends on nothing
' (CLAUDE.md section 4), so a Domain entity cannot implement an interface
' declared in Api. See ADR-017's P3-04 addendum for the full note.

Namespace Security

    ''' <summary>A resource whose approval must not be granted by the same user who requested it.</summary>
    Public Interface IOwnershipResource

        ''' <summary>The <c>Users.Id</c> of whoever created/requested this resource - never the approver.</summary>
        ReadOnly Property RequestedByUserId As Integer

    End Interface

End Namespace
