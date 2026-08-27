' Merchandising.Tests.Integration.ApiSpecificationDocumentationTests
'
' P3-08: docs/api-specification.md's procurement section must be "verified
' against the running registration rather than transcribed by hand" - the
' P2-12 shape. RolePermissionMatrixDocumentationTests achieves that for
' docs/role-permission-matrix.md by regenerating the ENTIRE file from
' PolicyRegistry.Definitions and diffing byte-for-byte; that only works
' because that document is pure data. The API specification is prose and
' tables around a handful of facts, so this test takes the other half of the
' same idea: reflect the live PurchaseOrdersController for its actual route
' templates, HTTP verbs, and declared policy, read the real error-code and
' error-envelope constants straight off the compiled types, and assert each
' one appears correctly in the committed document. A route renamed, a policy
' swapped, or an error code renamed without updating the document fails this
' suite rather than shipping a stale one.
'
' No database needed - this is reflection and two file reads, run alongside
' the DB-backed integration tests only because Merchandising.Tests.Integration
' is the project that already references Merchandising.Api (WindowsServiceInfoTests
' is the precedent for a DB-free test living here).

Imports System.IO
Imports System.Reflection
Imports Merchandising.Api.Controllers
Imports Merchandising.Api.Middleware
Imports Merchandising.Domain.Procurement
Imports Merchandising.Domain.Security
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Mvc
Imports Microsoft.AspNetCore.Mvc.Routing
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class ApiSpecificationDocumentationTests

    ''' <summary>
    ''' Walks up from the test assembly until it finds the repository root,
    ''' identified by <c>CLAUDE.md</c> - same marker
    ''' WindowsServiceInfoTests.FindRepositoryRoot and
    ''' RolePermissionMatrixDocumentationTests.FindRepositoryRoot use.
    ''' </summary>
    Private Shared Function FindRepositoryRoot() As DirectoryInfo

        Dim current As DirectoryInfo = New DirectoryInfo(AppContext.BaseDirectory)

        While current IsNot Nothing
            If File.Exists(Path.Combine(current.FullName, "CLAUDE.md")) Then
                Return current
            End If
            current = current.Parent
        End While

        Assert.Fail(
            "Could not locate the repository root above '" & AppContext.BaseDirectory &
            "'. This test reads docs/api-specification.md and source files from the working tree.")
        Return Nothing

    End Function

    Private Shared Function ReadDocument() As String
        Dim documentPath As String =
            Path.Combine(FindRepositoryRoot().FullName, "docs", "api-specification.md")

        Assert.IsTrue(
            File.Exists(documentPath),
            "docs/api-specification.md is missing. P3-08 owes a written document, not a planned one.")

        Return File.ReadAllText(documentPath)
    End Function

    Private Shared Function ReadSourceFile(relativePath As String) As String
        Dim fullPath As String = Path.Combine(FindRepositoryRoot().FullName, relativePath)
        Assert.IsTrue(File.Exists(fullPath), $"Expected source file '{relativePath}' does not exist.")
        Return File.ReadAllText(fullPath)
    End Function

    ''' <summary>
    ''' One documented route: the action's name (for reflection), the route
    ''' text this document must contain verbatim (e.g.
    ''' "POST /api/v1/purchase-orders/{id}/submit"), and the policy name
    ''' expected on a DECLARATIVE &lt;Authorize(Policy:=...)&gt; attribute -
    ''' Nothing for ApprovePurchaseOrder, which enforces its policy
    ''' imperatively (see that action's own XML doc comment).
    ''' </summary>
    Private Class ExpectedRoute
        Public ReadOnly ActionName As String
        Public ReadOnly RouteText As String
        Public ReadOnly ExpectedPolicy As String

        ''' <summary>
        ''' Parameter names differ from the field names ONLY by case
        ''' (ActionName/actionName) - VB identifiers are case-insensitive, so
        ''' an unqualified "ActionName = actionName" here would resolve BOTH
        ''' sides to the local parameter (self-assignment, no compile error,
        ''' silent no-op) rather than setting the field. The Me. qualifier is
        ''' load-bearing, not style - found by this test's fields all coming
        ''' back Nothing at run time with a clean build.
        ''' </summary>
        Public Sub New(actionName As String, routeText As String, expectedPolicy As String)
            Me.ActionName = actionName
            Me.RouteText = routeText
            Me.ExpectedPolicy = expectedPolicy
        End Sub
    End Class

    Private Shared Function ExpectedRoutes() As IReadOnlyList(Of ExpectedRoute)
        Return New List(Of ExpectedRoute) From {
            New ExpectedRoute(NameOf(PurchaseOrdersController.CreatePurchaseOrder),
                "POST /api/v1/purchase-orders", PolicyRegistry.Names.PurchaseOrdersCreate),
            New ExpectedRoute(NameOf(PurchaseOrdersController.SearchPurchaseOrders),
                "GET /api/v1/purchase-orders", PolicyRegistry.Names.PurchaseOrdersTrack),
            New ExpectedRoute(NameOf(PurchaseOrdersController.GetPurchaseOrderHistory),
                "GET /api/v1/purchase-orders/history", PolicyRegistry.Names.PurchaseOrdersTrack),
            New ExpectedRoute(NameOf(PurchaseOrdersController.GetPurchaseOrder),
                "GET /api/v1/purchase-orders/{id}", PolicyRegistry.Names.PurchaseOrdersTrack),
            New ExpectedRoute(NameOf(PurchaseOrdersController.SubmitPurchaseOrder),
                "POST /api/v1/purchase-orders/{id}/submit", PolicyRegistry.Names.PurchaseOrdersSubmit),
            New ExpectedRoute(NameOf(PurchaseOrdersController.ApprovePurchaseOrder),
                "POST /api/v1/purchase-orders/{id}/approve", Nothing),
            New ExpectedRoute(NameOf(PurchaseOrdersController.CancelPurchaseOrder),
                "POST /api/v1/purchase-orders/{id}/cancel", PolicyRegistry.Names.PurchaseOrdersCancel),
            New ExpectedRoute(NameOf(PurchaseOrdersController.ClosePurchaseOrder),
                "POST /api/v1/purchase-orders/{id}/close", PolicyRegistry.Names.PurchaseOrdersClose)
        }
    End Function

    ''' <summary>The document text for one documented route's own subsection - from its "### `VERB /route`" heading up to the next "### " heading or end of file.</summary>
    Private Shared Function ExtractSection(document As String, routeText As String) As String

        Dim heading As String = "### `" & routeText & "`"
        Dim start As Integer = document.IndexOf(heading, StringComparison.Ordinal)

        Assert.AreNotEqual(-1, start,
            $"docs/api-specification.md has no '### `{routeText}` ...' heading. " &
            "The route reflected off PurchaseOrdersController has no matching documentation.")

        Dim nextHeading As Integer = document.IndexOf(vbLf & "### ", start + heading.Length, StringComparison.Ordinal)

        Return If(nextHeading >= 0,
            document.Substring(start, nextHeading - start),
            document.Substring(start))

    End Function

    <TestMethod>
    Public Sub EveryTrackCRoute_MatchesTheLiveController()

        Dim document As String = ReadDocument()
        Dim controllerType As Type = GetType(PurchaseOrdersController)

        Dim classRoute As RouteAttribute = controllerType.GetCustomAttribute(Of RouteAttribute)()
        Assert.IsNotNull(classRoute, "PurchaseOrdersController has no [Route] attribute.")

        For Each expected As ExpectedRoute In ExpectedRoutes()

            Dim method As MethodInfo = controllerType.GetMethod(
                expected.ActionName, BindingFlags.Public Or BindingFlags.Instance Or BindingFlags.DeclaredOnly)

            Assert.IsNotNull(method,
                $"PurchaseOrdersController no longer declares an action named '{expected.ActionName}'.")

            Dim httpAttribute As HttpMethodAttribute = method.GetCustomAttribute(Of HttpMethodAttribute)()
            Assert.IsNotNull(httpAttribute,
                $"'{expected.ActionName}' has no Http* method attribute.")

            Dim verb As String = httpAttribute.HttpMethods.Single()
            Dim template As String = httpAttribute.Template
            Dim actualRoute As String =
                If(String.IsNullOrEmpty(template),
                   classRoute.Template,
                   classRoute.Template & "/" & template)

            Dim actualRouteText As String = $"{verb} /{actualRoute}"

            Assert.AreEqual(
                expected.RouteText, actualRouteText,
                $"'{expected.ActionName}' is actually routed as '{actualRouteText}', " &
                "but docs/api-specification.md documents a different route for it.")

            Dim section As String = ExtractSection(document, expected.RouteText)

            Dim authorizeAttribute As AuthorizeAttribute = method.GetCustomAttribute(Of AuthorizeAttribute)()
            Assert.IsNotNull(authorizeAttribute,
                $"'{expected.ActionName}' has no [Authorize] attribute at all - every route in this section must require the Session scheme.")

            If expected.ExpectedPolicy Is Nothing Then

                Assert.IsNull(
                    authorizeAttribute.Policy,
                    $"'{expected.ActionName}' now declares Policy='{authorizeAttribute.Policy}' on its [Authorize] attribute. " &
                    "This test expected it to remain imperative (documented as such) - update the document " &
                    "AND this test's ExpectedPolicy together if that changed on purpose.")

                StringAssert.Contains(section, "imperatively",
                    "The approve section no longer explains that its policy is enforced imperatively.")

            Else

                Assert.AreEqual(
                    expected.ExpectedPolicy, authorizeAttribute.Policy,
                    $"'{expected.ActionName}' is actually gated by policy '{authorizeAttribute.Policy}', " &
                    "not what docs/api-specification.md documents for it.")

                StringAssert.Contains(section, expected.ExpectedPolicy,
                    $"docs/api-specification.md's section for '{expected.RouteText}' does not mention its own policy " &
                    $"'{expected.ExpectedPolicy}'.")

            End If

        Next

    End Sub

    ''' <summary>
    ''' Only three of PurchaseOrderTransitionErrors' four codes are reachable
    ''' through a Track C action. PurchaseOrderTransitionErrors.FullyReceived
    ''' is returned only when RefusalCodeFor sees an order already
    ''' FullyReceived AND a "receiving" action (PurchaseOrderTransitions.
    ''' IsReceiving: ReceivePartially/ReceiveFully) - Phase 4's endpoints,
    ''' which do not exist yet. None of Submit/Approve/Cancel/Close is a
    ''' receiving action, so documenting FullyReceived under this section
    ''' would claim a reachable outcome that is not.
    ''' </summary>
    <TestMethod>
    Public Sub EveryTrackCReachableRefusalCode_IsDocumented()

        Dim document As String = ReadDocument()

        For Each code As String In New String() {
            PurchaseOrderTransitionErrors.Cancelled,
            PurchaseOrderTransitionErrors.Closed,
            PurchaseOrderTransitionErrors.InvalidTransition
        }

            StringAssert.Contains(
                document, code,
                $"docs/api-specification.md does not mention PurchaseOrderTransitionErrors.{code} " &
                "- a stable error code a Track C action can actually return.")

        Next

    End Sub

    <TestMethod>
    Public Sub CrossCuttingErrorCodes_MatchTheLiveHandlers()

        Dim document As String = ReadDocument()

        StringAssert.Contains(
            document, ExceptionHandlingMiddleware.ErrorCode,
            "docs/api-specification.md does not mention ExceptionHandlingMiddleware.ErrorCode.")

        Dim authHandlerSource As String =
            ReadSourceFile(Path.Combine("src", "Merchandising.Api", "Security", "SessionAuthenticationHandler.vb"))

        For Each code As String In New String() {"UNAUTHORIZED", "FORBIDDEN"}

            StringAssert.Contains(
                authHandlerSource, $"""{code}""",
                $"SessionAuthenticationHandler.vb no longer writes the literal error code '{code}' - " &
                "update docs/api-specification.md's cross-cutting table to match whatever it writes now.")

            StringAssert.Contains(
                document, code,
                $"docs/api-specification.md does not mention the cross-cutting error code '{code}'.")

        Next

    End Sub

End Class
