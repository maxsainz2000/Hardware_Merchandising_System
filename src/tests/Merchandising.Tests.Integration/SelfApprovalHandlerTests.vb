' Merchandising.Tests.Integration.SelfApprovalHandlerTests
'
' P2-02's done-when box: "Self-approval prohibition is a policy requirement
' with the actor and the target's owner compared server-side, not a
' controller If". Exercises SelfApprovalHandler directly against
' AuthorizationHandlerContext - no HTTP round trip and no database, because
' no endpoint uses PurchaseOrders.Approve or Adjustments.Approve yet
' (Phase 3 builds the first one). This is the test CLAUDE.md's task loop
' means by "failing test first for any server-side business rule": the rule
' exists and is proven here, ahead of the controller that will eventually
' invoke it.

Imports System.Security.Claims
Imports Merchandising.Api.Security
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class SelfApprovalHandlerTests

    ''' <summary>
    ''' Constructor parameter is "requesterId", not "requestedByUserId" - see
    ''' PolicyDefinition.vb's constructor comment. A parameter matching the
    ''' property name only by case turns "RequestedByUserId = requestedByUserId"
    ''' into a self-assignment; this fixture caught its own version of that
    ''' bug (RequestedByUserId silently stayed 0) while writing this test.
    ''' </summary>
    Private NotInheritable Class FakeOwnedResource
        Implements IOwnershipResource

        Public Sub New(requesterId As Integer)
            RequestedByUserId = requesterId
        End Sub

        Public ReadOnly Property RequestedByUserId As Integer Implements IOwnershipResource.RequestedByUserId

    End Class

    Private Shared Function PrincipalForUser(userId As Integer) As ClaimsPrincipal

        Dim identity As New ClaimsIdentity(
            {New Claim(ClaimTypes.NameIdentifier, userId.ToString(Globalization.CultureInfo.InvariantCulture))},
            "TestScheme")

        Return New ClaimsPrincipal(identity)

    End Function

    ''' <summary>The actor who requested the resource must not also be the one approving it.</summary>
    <TestMethod>
    Public Async Function ActorIsRequester_Fails() As Task

        Dim requirement As New SelfApprovalRequirement()
        Dim resource As New FakeOwnedResource(requesterId:=42)
        Dim context As New AuthorizationHandlerContext({requirement}, PrincipalForUser(42), resource)

        Await New SelfApprovalHandler().HandleAsync(context)

        Assert.IsTrue(context.HasFailed, "Self-approval must be an explicit failure, not merely an unmet requirement.")
        Assert.IsFalse(context.HasSucceeded)

    End Function

    ''' <summary>A different actor than the requester satisfies the requirement.</summary>
    <TestMethod>
    Public Async Function ActorIsNotRequester_Succeeds() As Task

        Dim requirement As New SelfApprovalRequirement()
        Dim resource As New FakeOwnedResource(requesterId:=42)
        Dim context As New AuthorizationHandlerContext({requirement}, PrincipalForUser(99), resource)

        Await New SelfApprovalHandler().HandleAsync(context)

        Assert.IsTrue(context.HasSucceeded)
        Assert.IsFalse(context.HasFailed)

    End Function

End Class
