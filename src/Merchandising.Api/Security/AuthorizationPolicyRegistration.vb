' Merchandising.Api.Security.AuthorizationPolicyRegistration
'
' P2-02 / ADR-017: the only place AddPolicy is called. Reads
' Merchandising.Domain.Security.PolicyRegistry.Definitions - the same list
' RolePermissionMatrixFormatter renders into docs/role-permission-matrix.md -
' so the enforced policy set and the document can never independently drift.
' Program.vb calls Configure via
' builder.Services.AddAuthorization(AddressOf AuthorizationPolicyRegistration.Configure)
' rather than the bare AddAuthorization() it used before this card.
'
' Every registered policy is at minimum a role-membership check
' (RequireRole). Three names carry an additional SelfApprovalRequirement,
' listed explicitly below rather than inferred from a naming convention -
' spec section 9's "cannot approve their own purchase order" / "...
' adjustment" is not implied by the word "Approve" in general.
'
' P5-11 CORRECTION: this header previously claimed
' "SalesReturns.ApproveExceptional has no self-approval rule; nothing
' requests an exceptional return for itself to approve." That was wrong.
' PolicyRegistry.vb's CashierAndAbove (SalesReturns.Create's role set) and
' AdminAndAbove (SalesReturns.ApproveExceptional's role set) both include
' Admin and SuperAdmin - so an Admin who requests their own exceptional
' return could otherwise approve it themselves, the identical shape ADR-017
' section 6 already forbids for PurchaseOrders.Approve/Adjustments.Approve.
' P5-11 adds SalesReturns.ApproveExceptional here, reusing the same
' IOwnershipResource/SelfApprovalRequirement/SelfApprovalHandler mechanism -
' not a new check (the P3-04/P4-10 precedent this correction follows).
' Merchandising.Domain.Entities.SalesReturn implements IOwnershipResource on
' its ReturnedByUserId column.

Imports Merchandising.Domain.Security
Imports Microsoft.AspNetCore.Authorization

Namespace Security

    Public NotInheritable Class AuthorizationPolicyRegistration

        ''' <summary>Policy names that also require the actor not be the resource's own requester (spec section 9).</summary>
        Private Shared ReadOnly SelfApprovalGuardedPolicies As String() = {
            PolicyRegistry.Names.PurchaseOrdersApprove,
            PolicyRegistry.Names.AdjustmentsApprove,
            PolicyRegistry.Names.SalesReturnsApproveExceptional
        }

        Public Shared Sub Configure(options As AuthorizationOptions)

            For Each definition As PolicyDefinition In PolicyRegistry.Definitions

                options.AddPolicy(
                    definition.PolicyName,
                    Sub(policyBuilder As AuthorizationPolicyBuilder)

                        policyBuilder.RequireRole(definition.AllowedRoles)

                        If Array.IndexOf(SelfApprovalGuardedPolicies, definition.PolicyName) >= 0 Then
                            policyBuilder.AddRequirements(New SelfApprovalRequirement())
                        End If

                    End Sub)

            Next

        End Sub

    End Class

End Namespace
