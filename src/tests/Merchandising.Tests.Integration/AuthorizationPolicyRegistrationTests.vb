' Merchandising.Tests.Integration.AuthorizationPolicyRegistrationTests
'
' P2-02's done-when boxes, proven directly against AuthorizationOptions
' rather than through HTTP - no controller change is needed to prove the
' registration itself is correct, and most of PolicyRegistry's policies have
' no endpoint yet (Phase 3/5 build them):
'   - "Every role x operation cell resolves to exactly one named policy"
'   - "Restore stays behind its own policy, separate from Admin"
' AuthorizationMatrixTests (P2-03) is where the positive/negative HTTP
' assertions belong, once endpoints exist to assert against.

Imports System.Linq
Imports Merchandising.Api.Security
Imports Merchandising.Domain.Security
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Authorization.Infrastructure
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class AuthorizationPolicyRegistrationTests

    Private Shared Function BuildOptions() As AuthorizationOptions
        Dim options As New AuthorizationOptions()
        AuthorizationPolicyRegistration.Configure(options)
        Return options
    End Function

    ''' <summary>Every PolicyRegistry entry becomes exactly one registered policy, requiring exactly its own allowed roles.</summary>
    <TestMethod>
    Public Sub EveryDefinition_ResolvesToExactlyOneRegisteredPolicy()

        Dim options As AuthorizationOptions = BuildOptions()

        For Each definition As PolicyDefinition In PolicyRegistry.Definitions

            Dim policy As AuthorizationPolicy = options.GetPolicy(definition.PolicyName)
            Assert.IsNotNull(policy, $"No policy registered for '{definition.PolicyName}'.")

            Dim roleRequirements As RolesAuthorizationRequirement() =
                policy.Requirements.OfType(Of RolesAuthorizationRequirement)().ToArray()
            Assert.HasCount(1, roleRequirements, $"'{definition.PolicyName}' must carry exactly one role requirement.")

            CollectionAssert.AreEquivalent(
                definition.AllowedRoles.ToArray(), roleRequirements(0).AllowedRoles.ToArray(),
                $"'{definition.PolicyName}' registered role set does not match PolicyRegistry.")

        Next

    End Sub

    ''' <summary>Spec section 9: Admin "cannot restore unless explicitly granted a separate policy" - Restore.Perform's role set must not include Admin.</summary>
    <TestMethod>
    Public Sub RestorePolicy_DoesNotGrantAdmin()

        Dim options As AuthorizationOptions = BuildOptions()
        Dim policy As AuthorizationPolicy = options.GetPolicy(PolicyRegistry.Names.RestorePerform)
        Dim roleRequirement As RolesAuthorizationRequirement =
            policy.Requirements.OfType(Of RolesAuthorizationRequirement)().Single()

        CollectionAssert.DoesNotContain(roleRequirement.AllowedRoles.ToArray(), RoleNames.Admin)

    End Sub

    ''' <summary>Configuration.Manage and Restore.Perform are registered independently - widening one must not widen the other.</summary>
    <TestMethod>
    Public Sub RestorePolicy_IsNotTheSamePolicyObjectAsConfigurationManage()

        Dim options As AuthorizationOptions = BuildOptions()

        Dim restorePolicy As AuthorizationPolicy = options.GetPolicy(PolicyRegistry.Names.RestorePerform)
        Dim configurationPolicy As AuthorizationPolicy = options.GetPolicy(PolicyRegistry.Names.ConfigurationManage)

        Assert.AreNotSame(restorePolicy, configurationPolicy)

    End Sub

    ''' <summary>PurchaseOrders.Approve and Adjustments.Approve carry SelfApprovalRequirement; nothing else does.</summary>
    <TestMethod>
    Public Sub OnlyTheTwoApprovalPolicies_CarrySelfApprovalRequirement()

        Dim options As AuthorizationOptions = BuildOptions()
        Dim guarded As String() = {PolicyRegistry.Names.PurchaseOrdersApprove, PolicyRegistry.Names.AdjustmentsApprove}

        For Each definition As PolicyDefinition In PolicyRegistry.Definitions

            Dim policy As AuthorizationPolicy = options.GetPolicy(definition.PolicyName)
            Dim carriesSelfApproval As Boolean = policy.Requirements.OfType(Of SelfApprovalRequirement)().Any()

            If Array.IndexOf(guarded, definition.PolicyName) >= 0 Then
                Assert.IsTrue(carriesSelfApproval, $"'{definition.PolicyName}' must carry SelfApprovalRequirement.")
            Else
                Assert.IsFalse(carriesSelfApproval, $"'{definition.PolicyName}' must not carry SelfApprovalRequirement.")
            End If

        Next

    End Sub

End Class
