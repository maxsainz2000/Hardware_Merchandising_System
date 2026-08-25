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
' (RequireRole). Two names carry an additional SelfApprovalRequirement,
' listed explicitly below rather than inferred from a naming convention -
' spec section 9's "cannot approve their own purchase order" / "...
' adjustment" is not implied by the word "Approve" in general
' (SalesReturns.ApproveExceptional has no self-approval rule; nothing
' requests an exceptional return for itself to approve).

Imports Merchandising.Domain.Security
Imports Microsoft.AspNetCore.Authorization

Namespace Security

    Public NotInheritable Class AuthorizationPolicyRegistration

        ''' <summary>Policy names that also require the actor not be the resource's own requester (spec section 9).</summary>
        Private Shared ReadOnly SelfApprovalGuardedPolicies As String() = {
            PolicyRegistry.Names.PurchaseOrdersApprove,
            PolicyRegistry.Names.AdjustmentsApprove
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
