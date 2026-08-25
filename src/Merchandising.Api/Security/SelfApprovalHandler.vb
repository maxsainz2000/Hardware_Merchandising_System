' Merchandising.Api.Security.SelfApprovalHandler
'
' P2-02: spec section 9's "cannot approve their own purchase order" /
' "cannot approve their own adjustment", enforced server-side by comparing
' the authenticated actor to the resource's requester - never by a
' controller `If`, per the done-when box. Phase 3 calls
' IAuthorizationService.AuthorizeAsync(User, resource, policyName) with the
' loaded PurchaseOrder/StockAdjustment (an IOwnershipResource) as the
' resource once those entities exist; this handler is what that call
' reaches.
'
' Explicit context.Fail() on a match, not merely withholding Succeed(). A
' requirement nobody satisfies already denies by default, but Fail() states
' the rule as an absolute veto - short-circuiting even a hypothetical future
' handler for the same requirement that might otherwise succeed it - which
' matches spec section 9's wording ("cannot") better than a silent
' non-match would.

Imports System.Security.Claims
Imports System.Threading.Tasks
Imports Microsoft.AspNetCore.Authorization

Namespace Security

    Public NotInheritable Class SelfApprovalHandler
        Inherits AuthorizationHandler(Of SelfApprovalRequirement, IOwnershipResource)

        Protected Overrides Function HandleRequirementAsync(
                context As AuthorizationHandlerContext,
                requirement As SelfApprovalRequirement,
                resource As IOwnershipResource) As Task

            Dim actorIdClaim As String = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            Dim actorId As Integer

            If Integer.TryParse(actorIdClaim, actorId) AndAlso actorId = resource.RequestedByUserId Then
                context.Fail()
            Else
                context.Succeed(requirement)
            End If

            Return Task.CompletedTask

        End Function

    End Class

End Namespace
