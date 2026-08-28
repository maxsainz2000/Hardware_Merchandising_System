' Merchandising.Tests.Integration.AuthorizationMatrixTests
'
' P2-03: spec section 9's role matrix, proven at the HTTP layer through the
' real ASP.NET Core pipeline (MerchandisingApiFactory - the same seam
' MaintenanceModeTests uses), against the real pinned MariaDB.
'
' TWO MECHANISMS, NOT ONE.
'
' EveryControllerAction_DeclaresAllowAnonymousOrARegisteredPolicy is the
' data-driven half the done-when box asks for: it enumerates every action in
' the API assembly and demands each one prove its own authorization intent -
' AllowAnonymous, a Policy name that exists in PolicyRegistry.Definitions, or
' membership in a two-item "authenticated, any role, deliberately no policy"
' allowlist (AuthController.GetCurrentUser/Logout - spec section 13 grants
' /me to any authenticated caller). A future endpoint that forgets
' authorization entirely, or that names a policy nobody registered, fails
' this test by name rather than shipping untested.
'
' The four *_MatrixMatchesPolicyRegistry tests are the behavioral half, one
' per action that carries a policy TODAY. Most of PolicyRegistry's 29
' policies have no endpoint yet - Products, Suppliers, Procurement and POS
' arrive in later Phase 2/3 cards, and this suite is expected to grow a
' matrix test alongside each one, not be rewritten to cover them in advance.
' Each test asks PolicyRegistry.Definitions for the policy's own allowed-role
' list rather than hard-coding a second copy of it, so a role added to or
' removed from a policy changes what this suite expects automatically.
'
' Fixture accounts are real, permanent rows (p2_03_fixture_<role>), the same
' shape every earlier integration suite uses - AuditLogs.ActorUserId is a
' foreign key to Users and AuditLogs is append-only, so a test account is
' never scratch data to delete.

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Net.Http.Json
Imports System.Text.Json
Imports System.Threading.Tasks
Imports Merchandising.Contracts.Auth
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Inventory
Imports Merchandising.Contracts.Maintenance
Imports Merchandising.Contracts.Procurement
Imports Merchandising.Contracts.Products
Imports Merchandising.Contracts.Settings
Imports Merchandising.Domain.Security
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Mvc.Abstractions
Imports Microsoft.AspNetCore.Mvc.Controllers
Imports Microsoft.AspNetCore.Mvc.Infrastructure
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class AuthorizationMatrixTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const FixturePassword As String = "P2-03 Fixture Passw0rd!"
    Private Const FixtureProductSku As String = "p2_03_fixture_sku"
    Private Const FixtureSupplierName As String = "P2-03 Fixture Supplier"
    Private Const BaselineQuantity As Decimal = 100.000D

    ''' <summary>
    ''' Actions that require login but deliberately carry no named policy -
    ''' spec section 13: "any authenticated role" for /me, and logout needs
    ''' only a valid token to end its own session. Anything else reaching
    ''' the coverage test's "authenticated, no policy" bucket fails instead
    ''' of joining this list silently.
    '''
    ''' The fourth entry is P3-04's PurchaseOrdersController.ApprovePurchaseOrder.
    ''' PurchaseOrders.Approve carries ADR-017 section 6's resource-based
    ''' SelfApprovalRequirement. A declarative &lt;Authorize(Policy:=...)&gt;
    ''' attribute evaluates that policy with resource=Nothing, so
    ''' SelfApprovalHandler (typed to IOwnershipResource) would never see a
    ''' matching resource and could never succeed - denying EVERY caller,
    ''' always. So this action carries only &lt;Authorize&gt; (authentication)
    ''' and calls IAuthorizationService.AuthorizeAsync(User, order, ...)
    ''' itself once the order is loaded. It is still fully policy-gated - see
    ''' PurchaseOrdersApprove_MatrixMatchesPolicyRegistry below - just not
    ''' through this coverage test's declarative-attribute mechanism.
    ''' </summary>
    Private Shared ReadOnly AuthenticatedNoPolicyAllowlist As String() = {
        "Merchandising.Api.Controllers.AuthController.GetCurrentUser",
        "Merchandising.Api.Controllers.AuthController.Logout",
        "Merchandising.Api.Controllers.SystemSettingsController.GetSettings",
        "Merchandising.Api.Controllers.PurchaseOrdersController.ApprovePurchaseOrder"
    }

    Private Shared ReadOnly AllFiveRoles As String() = {
        RoleNames.SuperAdmin, RoleNames.Admin, RoleNames.ProcurementOfficer, RoleNames.InventoryClerk, RoleNames.Cashier}

    Private _factory As MerchandisingApiFactory
    Private _connectionFactory As ConnectionFactory

    <TestInitialize>
    Public Sub SetUp()
        _factory = New MerchandisingApiFactory()
        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
    End Sub

    <TestCleanup>
    Public Sub TearDown()
        ReleaseAnyActiveLockDirectlyAsync().GetAwaiter().GetResult()
        _factory?.Dispose()
    End Sub

    ''' <summary>Done-when box 4: a new endpoint without a policy fails the suite rather than passing untested.</summary>
    <TestMethod>
    Public Sub EveryControllerAction_DeclaresAllowAnonymousOrARegisteredPolicy()

        Dim provider As IActionDescriptorCollectionProvider =
            _factory.Services.GetRequiredService(Of IActionDescriptorCollectionProvider)()

        Dim uncovered As New List(Of String)

        For Each descriptor As ActionDescriptor In provider.ActionDescriptors.Items

            Dim controllerAction As ControllerActionDescriptor = TryCast(descriptor, ControllerActionDescriptor)
            If controllerAction Is Nothing Then
                Continue For
            End If

            Dim actionKey As String = $"{controllerAction.ControllerTypeInfo.FullName}.{controllerAction.ActionName}"

            Dim isAnonymous As Boolean = descriptor.EndpointMetadata.OfType(Of IAllowAnonymous)().Any()
            If isAnonymous Then
                Continue For
            End If

            Dim authorizeData As List(Of IAuthorizeData) = descriptor.EndpointMetadata.OfType(Of IAuthorizeData)().ToList()
            Dim policyNames As List(Of String) =
                authorizeData.Where(Function(a) Not String.IsNullOrEmpty(a.Policy)).Select(Function(a) a.Policy).Distinct().ToList()

            If policyNames.Count > 0 Then
                For Each policyName As String In policyNames
                    If Not PolicyRegistry.Definitions.Any(Function(d) d.PolicyName = policyName) Then
                        uncovered.Add($"{actionKey}: names policy '{policyName}', which PolicyRegistry.Definitions does not register.")
                    End If
                Next
                Continue For
            End If

            If authorizeData.Count > 0 AndAlso AuthenticatedNoPolicyAllowlist.Contains(actionKey) Then
                Continue For
            End If

            uncovered.Add(
                $"{actionKey}: carries neither AllowAnonymous nor a named Policy, and is not in the explicit " &
                "authenticated-any-role allowlist. Every action must declare its authorization intent.")

        Next

        Assert.IsEmpty(
            uncovered,
            "Endpoint(s) not provably covered by an authorization policy:" & vbLf & String.Join(vbLf, uncovered))

    End Sub

    ''' <summary>P1-08's placeholder proof endpoint, now behind Diagnostics.AdminPing.</summary>
    <TestMethod>
    Public Async Function DiagnosticsAdminPing_MatrixMatchesPolicyRegistry() As Task

        Await EnsureAllFixtureUsersAsync()

        Dim allowedRoles As IReadOnlyList(Of String) = RolesFor(PolicyRegistry.Names.DiagnosticsAdminPing)

        Using client As HttpClient = _factory.CreateClient()

            For Each roleName As String In AllFiveRoles
                Dim token As String = Await LoginAsync(client, roleName)
                Using response As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Get, "/api/v1/admin/ping", token, requestBody:=Nothing)
                    Await AssertCellAsync("Diagnostics.AdminPing", roleName, allowedRoles.Contains(roleName), response)
                End Using
            Next

            Using anonymousResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Get, "/api/v1/admin/ping", token:=Nothing, requestBody:=Nothing)
                Await AssertUnauthenticatedAsync("Diagnostics.AdminPing", anonymousResponse)
            End Using

        End Using

    End Function

    ''' <summary>
    ''' Enter and Release share one test method because Release's positive
    ''' cell needs an active lock to return 200 rather than 409, and this
    ''' class's own SuperAdmin call is the only thing allowed to create one.
    ''' Negative cells run first, before any lock exists, so a wrongly
    ''' 200'd negative attempt could never leave one behind unnoticed.
    ''' </summary>
    <TestMethod>
    Public Async Function MaintenancePerform_MatrixMatchesPolicyRegistry() As Task

        Await EnsureAllFixtureUsersAsync()
        Await ReleaseAnyActiveLockDirectlyAsync()

        Dim allowedRoles As IReadOnlyList(Of String) = RolesFor(PolicyRegistry.Names.MaintenancePerform)

        Using client As HttpClient = _factory.CreateClient()

            For Each roleName As String In AllFiveRoles
                If allowedRoles.Contains(roleName) Then
                    Continue For
                End If

                Dim token As String = Await LoginAsync(client, roleName)

                Using enterResponse As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Post, "/api/v1/admin/maintenance/enter", token,
                                     New EnterMaintenanceRequest With {.Reason = $"P2-03 negative probe ({roleName})"})
                    Await AssertCellAsync("Maintenance.Perform (enter)", roleName, isAllowed:=False, enterResponse)
                End Using

                Using releaseResponse As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Post, "/api/v1/admin/maintenance/release", token,
                                     New ReleaseMaintenanceRequest With {.VerificationPassed = True, .Detail = $"P2-03 negative probe ({roleName})"})
                    Await AssertCellAsync("Maintenance.Perform (release)", roleName, isAllowed:=False, releaseResponse)
                End Using
            Next

            Dim superAdminToken As String = Await LoginAsync(client, RoleNames.SuperAdmin)

            Using enterResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/admin/maintenance/enter", superAdminToken,
                                 New EnterMaintenanceRequest With {.Reason = "P2-03 positive probe"})
                Await AssertCellAsync("Maintenance.Perform (enter)", RoleNames.SuperAdmin, isAllowed:=True, enterResponse)
            End Using

            Using releaseResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/admin/maintenance/release", superAdminToken,
                                 New ReleaseMaintenanceRequest With {.VerificationPassed = True, .Detail = "P2-03 positive probe"})
                Await AssertCellAsync("Maintenance.Perform (release)", RoleNames.SuperAdmin, isAllowed:=True, releaseResponse)
            End Using

            Using anonymousEnterResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/admin/maintenance/enter", token:=Nothing,
                                 requestBody:=New EnterMaintenanceRequest With {.Reason = "P2-03 anonymous probe"})
                Await AssertUnauthenticatedAsync("Maintenance.Perform (enter)", anonymousEnterResponse)
            End Using

            Using anonymousReleaseResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/admin/maintenance/release", token:=Nothing,
                                 requestBody:=New ReleaseMaintenanceRequest With {.VerificationPassed = True, .Detail = "P2-03 anonymous probe"})
                Await AssertUnauthenticatedAsync("Maintenance.Perform (release)", anonymousReleaseResponse)
            End Using

        End Using

    End Function

    ''' <summary>P1-11's stock-decrement proof endpoint, now behind Adjustments.Request.</summary>
    <TestMethod>
    Public Async Function AdjustmentsRequest_MatrixMatchesPolicyRegistry() As Task

        Await EnsureAllFixtureUsersAsync()
        Dim productId As Integer = Await EnsureFixtureProductAsync()

        Dim allowedRoles As IReadOnlyList(Of String) = RolesFor(PolicyRegistry.Names.AdjustmentsRequest)

        Using client As HttpClient = _factory.CreateClient()

            For Each roleName As String In AllFiveRoles

                Dim isAllowed As Boolean = allowedRoles.Contains(roleName)
                If isAllowed Then
                    Await ResetStockBalanceDirectlyAsync(productId, BaselineQuantity)
                End If

                Dim token As String = Await LoginAsync(client, roleName)
                Dim body As New StockDecrementRequest With {
                    .ProductId = productId,
                    .Quantity = 1.000D,
                    .Reason = $"P2-03 matrix probe ({roleName})",
                    .IdempotencyKey = Guid.NewGuid().ToString("d")
                }

                Using response As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Post, "/api/v1/inventory/stock/decrement", token, body)
                    Await AssertCellAsync("Adjustments.Request", roleName, isAllowed, response)
                End Using

            Next

            Dim anonymousBody As New StockDecrementRequest With {
                .ProductId = productId, .Quantity = 1.000D, .Reason = "P2-03 anonymous probe", .IdempotencyKey = Guid.NewGuid().ToString("d")
            }
            Using anonymousResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/inventory/stock/decrement", token:=Nothing, requestBody:=anonymousBody)
                Await AssertUnauthenticatedAsync("Adjustments.Request", anonymousResponse)
            End Using

        End Using

    End Function

    ''' <summary>P2-05's SystemSettings write endpoint - Configuration.Manage's first live endpoint.</summary>
    <TestMethod>
    Public Async Function ConfigurationManage_MatrixMatchesPolicyRegistry() As Task

        Await EnsureAllFixtureUsersAsync()

        Dim allowedRoles As IReadOnlyList(Of String) = RolesFor(PolicyRegistry.Names.ConfigurationManage)

        Using client As HttpClient = _factory.CreateClient()

            For Each roleName As String In AllFiveRoles

                Dim token As String = Await LoginAsync(client, roleName)
                Dim body As New UpdateSystemSettingRequest With {.Value = "PHP"}

                Using response As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Put, "/api/v1/admin/settings/currency.code", token, body)
                    Await AssertCellAsync("Configuration.Manage", roleName, allowedRoles.Contains(roleName), response)
                End Using

            Next

            Dim anonymousBody As New UpdateSystemSettingRequest With {.Value = "PHP"}
            Using anonymousResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Put, "/api/v1/admin/settings/currency.code", token:=Nothing, requestBody:=anonymousBody)
                Await AssertUnauthenticatedAsync("Configuration.Manage", anonymousResponse)
            End Using

        End Using

    End Function

    ''' <summary>P2-08's price-change endpoint - Products.ChangePrice's first live endpoint. Admin succeeds, Cashier (and every other non-Admin/SuperAdmin role) is refused 403.</summary>
    <TestMethod>
    Public Async Function ProductsChangePrice_MatrixMatchesPolicyRegistry() As Task

        Await EnsureAllFixtureUsersAsync()
        Dim productId As Integer = Await EnsureFixtureProductAsync()

        Dim allowedRoles As IReadOnlyList(Of String) = RolesFor(PolicyRegistry.Names.ProductsChangePrice)

        Using client As HttpClient = _factory.CreateClient()

            Dim priceOffset As Integer = 0
            For Each roleName As String In AllFiveRoles

                Dim isAllowed As Boolean = allowedRoles.Contains(roleName)
                priceOffset += 1

                ' Reset to a known baseline before every ALLOWED attempt, so
                ' the request below always represents a real change - a
                ' second consecutive allowed attempt sending the same target
                ' price the first one already committed would otherwise hit
                ' PriceChangeService's own NoChange outcome (400), not the
                ' 2xx this cell expects.
                If isAllowed Then
                    Await ResetProductPriceDirectlyAsync(productId, 1.0000D, 0.5000D)
                End If

                Dim token As String = Await LoginAsync(client, roleName)
                Dim body As New ChangeProductPriceRequest With {.Price = 1.0000D + priceOffset}

                Using response As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Put, $"/api/v1/products/{productId}/price", token, body)
                    Await AssertCellAsync("Products.ChangePrice", roleName, isAllowed, response)
                End Using

            Next

            Dim anonymousBody As New ChangeProductPriceRequest With {.Price = 99.0000D}
            Using anonymousResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Put, $"/api/v1/products/{productId}/price", token:=Nothing, requestBody:=anonymousBody)
                Await AssertUnauthenticatedAsync("Products.ChangePrice", anonymousResponse)
            End Using

        End Using

    End Function

    ''' <summary>
    ''' P3-03's purchase-order creation endpoint - PurchaseOrders.Create's
    ''' first live endpoint. Every allowed role creates a real Draft order;
    ''' Inventory Clerk and Cashier are refused 403.
    ''' </summary>
    <TestMethod>
    Public Async Function PurchaseOrdersCreate_MatrixMatchesPolicyRegistry() As Task

        Await EnsureAllFixtureUsersAsync()
        Dim productId As Integer = Await EnsureFixtureProductAsync()
        Dim supplierId As Integer = Await EnsureFixtureSupplierAsync()

        Dim allowedRoles As IReadOnlyList(Of String) = RolesFor(PolicyRegistry.Names.PurchaseOrdersCreate)

        Using client As HttpClient = _factory.CreateClient()

            For Each roleName As String In AllFiveRoles

                Dim token As String = Await LoginAsync(client, roleName)

                Using response As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Post, "/api/v1/purchase-orders", token,
                                     NewPurchaseOrderBody(supplierId, productId))
                    Await AssertCellAsync("PurchaseOrders.Create", roleName, allowedRoles.Contains(roleName), response)
                End Using

            Next

            Using anonymousResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/purchase-orders", token:=Nothing,
                                 requestBody:=NewPurchaseOrderBody(supplierId, productId))
                Await AssertUnauthenticatedAsync("PurchaseOrders.Create", anonymousResponse)
            End Using

        End Using

    End Function

    ''' <summary>P3-03's purchase-order reads - PurchaseOrders.Track's first live endpoints. Both the list and the by-id read are probed.</summary>
    <TestMethod>
    Public Async Function PurchaseOrdersTrack_MatrixMatchesPolicyRegistry() As Task

        Await EnsureAllFixtureUsersAsync()

        Dim allowedRoles As IReadOnlyList(Of String) = RolesFor(PolicyRegistry.Names.PurchaseOrdersTrack)

        Using client As HttpClient = _factory.CreateClient()

            For Each roleName As String In AllFiveRoles

                Dim token As String = Await LoginAsync(client, roleName)

                Using listResponse As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Get, "/api/v1/purchase-orders?pageSize=1", token, requestBody:=Nothing)
                    Await AssertCellAsync("PurchaseOrders.Track (list)", roleName, allowedRoles.Contains(roleName), listResponse)
                End Using

                ' P3-06: the history report surface shares PurchaseOrders.Track
                ' with the plain list above - same policy, one more route.
                Using historyResponse As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Get, "/api/v1/purchase-orders/history?pageSize=1", token, requestBody:=Nothing)
                    Await AssertCellAsync("PurchaseOrders.Track (history)", roleName, allowedRoles.Contains(roleName), historyResponse)
                End Using

                ' A disallowed role must be refused 403 BEFORE the handler
                ' looks the order up - never 404. An endpoint that answered
                ' "no such order" to a caller with no right to ask would leak
                ' which ids exist.
                If Not allowedRoles.Contains(roleName) Then
                    Using detailResponse As HttpResponseMessage =
                        Await SendAsync(client, HttpMethod.Get, "/api/v1/purchase-orders/999999999", token, requestBody:=Nothing)
                        Await AssertCellAsync("PurchaseOrders.Track (by id)", roleName, isAllowed:=False, detailResponse)
                    End Using
                End If

            Next

            Using anonymousResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Get, "/api/v1/purchase-orders", token:=Nothing, requestBody:=Nothing)
                Await AssertUnauthenticatedAsync("PurchaseOrders.Track (list)", anonymousResponse)
            End Using

            Using anonymousHistoryResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Get, "/api/v1/purchase-orders/history", token:=Nothing, requestBody:=Nothing)
                Await AssertUnauthenticatedAsync("PurchaseOrders.Track (history)", anonymousHistoryResponse)
            End Using

        End Using

    End Function

    ''' <summary>
    ''' P3-04's submit endpoint. Every probed order is created fresh via the
    ''' SuperAdmin fixture (always allowed to create, PurchaseOrders.Create
    ''' being ProcurementAndAbove) rather than via roleName itself - this
    ''' test isolates PurchaseOrders.Submit's OWN role cell, decoupled from
    ''' PurchaseOrders.Create's, which already has its own matrix test above.
    ''' </summary>
    <TestMethod>
    Public Async Function PurchaseOrdersSubmit_MatrixMatchesPolicyRegistry() As Task

        Await EnsureAllFixtureUsersAsync()
        Dim supplierId As Integer = Await EnsureFixtureSupplierAsync()
        Dim productId As Integer = Await EnsureFixtureProductAsync()

        Dim allowedRoles As IReadOnlyList(Of String) = RolesFor(PolicyRegistry.Names.PurchaseOrdersSubmit)

        Using client As HttpClient = _factory.CreateClient()

            Dim creatorToken As String = Await LoginAsync(client, RoleNames.SuperAdmin)

            For Each roleName As String In AllFiveRoles

                Dim orderId As Integer = Await CreateDraftOrderAsync(client, creatorToken, supplierId, productId)
                Dim token As String = Await LoginAsync(client, roleName)

                Using submitResponse As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Post, $"/api/v1/purchase-orders/{orderId}/submit", token, requestBody:=Nothing)
                    Await AssertCellAsync("PurchaseOrders.Submit", roleName, allowedRoles.Contains(roleName), submitResponse)
                End Using

            Next

            Dim anonymousOrderId As Integer = Await CreateDraftOrderAsync(client, creatorToken, supplierId, productId)

            Using anonymousResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, $"/api/v1/purchase-orders/{anonymousOrderId}/submit", token:=Nothing, requestBody:=Nothing)
                Await AssertUnauthenticatedAsync("PurchaseOrders.Submit", anonymousResponse)
            End Using

        End Using

    End Function

    ''' <summary>
    ''' P3-04's approve endpoint - the ROLE dimension only. Every probed
    ''' order is created AND submitted via the ProcurementOfficer fixture,
    ''' which is never in PurchaseOrders.Approve's allowed-role set
    ''' (AdminAndAbove) - so no role under test ever collides with the
    ''' order's own requester, and this test never accidentally exercises the
    ''' self-approval veto instead of the role check. Self-approval itself is
    ''' PurchaseOrderApprovalTests' job, with two real users over HTTP.
    ''' </summary>
    <TestMethod>
    Public Async Function PurchaseOrdersApprove_MatrixMatchesPolicyRegistry() As Task

        Await EnsureAllFixtureUsersAsync()
        Dim supplierId As Integer = Await EnsureFixtureSupplierAsync()
        Dim productId As Integer = Await EnsureFixtureProductAsync()

        Dim allowedRoles As IReadOnlyList(Of String) = RolesFor(PolicyRegistry.Names.PurchaseOrdersApprove)

        Using client As HttpClient = _factory.CreateClient()

            Dim requesterToken As String = Await LoginAsync(client, RoleNames.ProcurementOfficer)

            For Each roleName As String In AllFiveRoles

                Dim orderId As Integer = Await CreateDraftOrderAsync(client, requesterToken, supplierId, productId)

                Using submitResponse As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Post, $"/api/v1/purchase-orders/{orderId}/submit", requesterToken, requestBody:=Nothing)
                    Assert.AreEqual(HttpStatusCode.OK, submitResponse.StatusCode, "Fixture submit for the approve matrix must succeed.")
                End Using

                Dim token As String = Await LoginAsync(client, roleName)

                Using approveResponse As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Post, $"/api/v1/purchase-orders/{orderId}/approve", token, requestBody:=Nothing)
                    Await AssertCellAsync("PurchaseOrders.Approve", roleName, allowedRoles.Contains(roleName), approveResponse)
                End Using

            Next

            Dim anonymousOrderId As Integer = Await CreateDraftOrderAsync(client, requesterToken, supplierId, productId)

            Using anonymousResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, $"/api/v1/purchase-orders/{anonymousOrderId}/approve", token:=Nothing, requestBody:=Nothing)
                Await AssertUnauthenticatedAsync("PurchaseOrders.Approve", anonymousResponse)
            End Using

        End Using

    End Function

    ''' <summary>
    ''' P3-05's cancel endpoint. Every probed order is a fresh Draft, created
    ''' via the SuperAdmin fixture - Cancel is legal from Draft (P3-01's
    ''' table), so no submit step is needed to isolate this cell.
    ''' </summary>
    <TestMethod>
    Public Async Function PurchaseOrdersCancel_MatrixMatchesPolicyRegistry() As Task

        Await EnsureAllFixtureUsersAsync()
        Dim supplierId As Integer = Await EnsureFixtureSupplierAsync()
        Dim productId As Integer = Await EnsureFixtureProductAsync()

        Dim allowedRoles As IReadOnlyList(Of String) = RolesFor(PolicyRegistry.Names.PurchaseOrdersCancel)

        Using client As HttpClient = _factory.CreateClient()

            Dim creatorToken As String = Await LoginAsync(client, RoleNames.SuperAdmin)

            For Each roleName As String In AllFiveRoles

                Dim orderId As Integer = Await CreateDraftOrderAsync(client, creatorToken, supplierId, productId)
                Dim token As String = Await LoginAsync(client, roleName)

                Using cancelResponse As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Post, $"/api/v1/purchase-orders/{orderId}/cancel", token, NewReasonBody())
                    Await AssertCellAsync("PurchaseOrders.Cancel", roleName, allowedRoles.Contains(roleName), cancelResponse)
                End Using

            Next

            Dim anonymousOrderId As Integer = Await CreateDraftOrderAsync(client, creatorToken, supplierId, productId)

            Using anonymousResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, $"/api/v1/purchase-orders/{anonymousOrderId}/cancel", token:=Nothing, requestBody:=NewReasonBody())
                Await AssertUnauthenticatedAsync("PurchaseOrders.Cancel", anonymousResponse)
            End Using

        End Using

    End Function

    ''' <summary>
    ''' P3-05's close endpoint. Close is legal only from PartiallyReceived or
    ''' FullyReceived (P3-01's table), which no live Phase 3 endpoint can
    ''' reach - Phase 4 owns receiving. Each probed order is forced directly
    ''' into FullyReceived via SQL purely to arrange fixture state (merch_api
    ''' holds UPDATE on purchaseorders, db/grants/0010) - this is fixture
    ''' setup, not something the assertions below rely on for their meaning.
    ''' </summary>
    <TestMethod>
    Public Async Function PurchaseOrdersClose_MatrixMatchesPolicyRegistry() As Task

        Await EnsureAllFixtureUsersAsync()
        Dim supplierId As Integer = Await EnsureFixtureSupplierAsync()
        Dim productId As Integer = Await EnsureFixtureProductAsync()

        Dim allowedRoles As IReadOnlyList(Of String) = RolesFor(PolicyRegistry.Names.PurchaseOrdersClose)

        Using client As HttpClient = _factory.CreateClient()

            Dim creatorToken As String = Await LoginAsync(client, RoleNames.SuperAdmin)

            For Each roleName As String In AllFiveRoles

                Dim orderId As Integer = Await CreateDraftOrderAsync(client, creatorToken, supplierId, productId)
                Await ForcePurchaseOrderStatusDirectlyAsync(orderId, "FullyReceived")
                Dim token As String = Await LoginAsync(client, roleName)

                Using closeResponse As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Post, $"/api/v1/purchase-orders/{orderId}/close", token, NewReasonBody())
                    Await AssertCellAsync("PurchaseOrders.Close", roleName, allowedRoles.Contains(roleName), closeResponse)
                End Using

            Next

            Dim anonymousOrderId As Integer = Await CreateDraftOrderAsync(client, creatorToken, supplierId, productId)
            Await ForcePurchaseOrderStatusDirectlyAsync(anonymousOrderId, "FullyReceived")

            Using anonymousResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, $"/api/v1/purchase-orders/{anonymousOrderId}/close", token:=Nothing, requestBody:=NewReasonBody())
                Await AssertUnauthenticatedAsync("PurchaseOrders.Close", anonymousResponse)
            End Using

        End Using

    End Function

    ' --------------------------------------------------------------- shared assertions

    Private Shared Function RolesFor(policyName As String) As IReadOnlyList(Of String)
        Return PolicyRegistry.Definitions.Single(Function(d) d.PolicyName = policyName).AllowedRoles
    End Function

    Private Shared Async Function AssertCellAsync(
        endpointLabel As String, roleName As String, isAllowed As Boolean, response As HttpResponseMessage) As Task

        Console.WriteLine($"{endpointLabel} | {roleName} | expected {If(isAllowed, "2xx", "403")} | got {CInt(response.StatusCode)} {response.StatusCode}")

        If isAllowed Then

            Dim statusCode As Integer = CInt(response.StatusCode)
            Assert.IsTrue(
                statusCode >= 200 AndAlso statusCode < 300,
                $"{endpointLabel}: role '{roleName}' is allowed by PolicyRegistry but got {statusCode} {response.StatusCode}.")

        Else

            Assert.AreEqual(
                HttpStatusCode.Forbidden, response.StatusCode,
                $"{endpointLabel}: role '{roleName}' is NOT allowed by PolicyRegistry but got " &
                $"{CInt(response.StatusCode)} {response.StatusCode} instead of 403.")
            Await AssertCleanErrorBodyAsync(response, "FORBIDDEN")

        End If

    End Function

    Private Shared Async Function AssertUnauthenticatedAsync(endpointLabel As String, response As HttpResponseMessage) As Task

        Console.WriteLine($"{endpointLabel} | (anonymous) | expected 401 | got {CInt(response.StatusCode)} {response.StatusCode}")

        Assert.AreEqual(
            HttpStatusCode.Unauthorized, response.StatusCode,
            $"{endpointLabel}: an unauthenticated request must be 401, got {CInt(response.StatusCode)} {response.StatusCode}.")
        Await AssertCleanErrorBodyAsync(response, "UNAUTHORIZED")

    End Function

    ''' <summary>ADR-014: every error body carries a correlation ID and leaks no stack trace, SQL, or internal detail.</summary>
    Private Shared Async Function AssertCleanErrorBodyAsync(response As HttpResponseMessage, expectedErrorCode As String) As Task

        Dim raw As String = Await response.Content.ReadAsStringAsync()
        Dim body As ApiErrorResponse = JsonSerializer.Deserialize(Of ApiErrorResponse)(raw)

        Assert.AreEqual(expectedErrorCode, body.ErrorCode, "Body: " & raw)
        Assert.IsFalse(String.IsNullOrWhiteSpace(body.CorrelationId), "Error body carried no correlation ID. Body: " & raw)

        Dim forbiddenFragments As String() = {
            "Exception", "   at ", ".vb:line", "SELECT ", "INSERT ", "UPDATE ", "DELETE ",
            "Server=", "Uid=", "Pwd=", "Password="
        }

        For Each fragment As String In forbiddenFragments
            Assert.IsFalse(
                raw.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                $"Error body leaked '{fragment}'. Full body: {raw}")
        Next

    End Function

    Private Shared Async Function SendAsync(
        client As HttpClient, method As HttpMethod, path As String, token As String, requestBody As Object) As Task(Of HttpResponseMessage)

        Dim request As New HttpRequestMessage(method, path)

        If token IsNot Nothing Then
            request.Headers.Authorization = New AuthenticationHeaderValue("Bearer", token)
        End If

        If requestBody IsNot Nothing Then
            request.Content = JsonContent.Create(requestBody)
        End If

        Return Await client.SendAsync(request)

    End Function

    ' --------------------------------------------------------------- fixtures

    Private Shared Function FixtureUsername(roleName As String) As String
        Return $"p2_03_fixture_{roleName.ToLowerInvariant()}"
    End Function

    Private Async Function LoginAsync(client As HttpClient, roleName As String) As Task(Of String)

        Using response As HttpResponseMessage =
            Await client.PostAsJsonAsync("/api/v1/auth/login",
                                         New LoginRequest With {.Username = FixtureUsername(roleName), .Password = FixturePassword})

            Assert.AreEqual(
                HttpStatusCode.OK, response.StatusCode,
                $"Fixture user for role '{roleName}' could not log in. Body: " & Await response.Content.ReadAsStringAsync())

            Dim login As LoginResponse = Await response.Content.ReadFromJsonAsync(Of LoginResponse)()
            Return login.Token

        End Using

    End Function

    Private Async Function EnsureAllFixtureUsersAsync() As Task
        For Each roleName As String In AllFiveRoles
            Await EnsureFixtureUserAsync(roleName)
        Next
    End Function

    Private Async Function EnsureFixtureUserAsync(roleName As String) As Task

        Dim username As String = FixtureUsername(roleName)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Dim existing As Merchandising.Domain.Entities.User = Await UserRepository.FindByUsernameAsync(connection, username)
            If existing IsNot Nothing Then
                Return
            End If
        End Using

        Dim migratorFactory As New ConnectionFactory(LoadMigratorOptions())
        Await CreateUserCommand.RunAsync(migratorFactory, username, FixturePassword, roleName)

    End Function

    Private Async Function EnsureFixtureProductAsync() As Task(Of Integer)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            Using selectCommand As MySqlCommand = connection.CreateCommand()
                selectCommand.CommandText = "SELECT Id FROM Products WHERE Sku = @sku;"
                selectCommand.Parameters.AddWithValue("@sku", FixtureProductSku)
                Dim existing As Object = Await selectCommand.ExecuteScalarAsync()
                If existing IsNot Nothing Then
                    Return CInt(existing)
                End If
            End Using

            Dim productId As Integer

            Using insertCommand As MySqlCommand = connection.CreateCommand()
                insertCommand.CommandText =
                    "INSERT INTO Products (Sku, Barcode, Name, Price, Cost, IsActive, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@sku, NULL, 'P2-03 Fixture Product', 1.0000, 0.5000, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                insertCommand.Parameters.AddWithValue("@sku", FixtureProductSku)
                Await insertCommand.ExecuteNonQueryAsync()
                productId = CInt(insertCommand.LastInsertedId)
            End Using

            ' 0.000, not BaselineQuantity: no StockMovements row is written
            ' for this initial creation, and P4-01's ledger reconciliation
            ' asserts SUM(StockMovements) = StockBalances for every product,
            ' so a starting balance of anything but 0 here would be drift
            ' from the moment this fixture is first created. Whichever test
            ' needs BaselineQuantity calls ResetStockBalanceDirectlyAsync,
            ' which raises it with a matching compensating movement.
            Using balanceCommand As MySqlCommand = connection.CreateCommand()
                balanceCommand.CommandText =
                    "INSERT INTO StockBalances (ProductId, Quantity, RowVersion, UpdatedAtUtc) " &
                    "VALUES (@productId, 0.000, 0, UTC_TIMESTAMP(6));"
                balanceCommand.Parameters.AddWithValue("@productId", productId)
                Await balanceCommand.ExecuteNonQueryAsync()
            End Using

            Return productId

        End Using

    End Function

    ''' <summary>A permanent active supplier for the P3-03 purchase-order cells. Suppliers has no DELETE grant, so it is created once and reused.</summary>
    Private Async Function EnsureFixtureSupplierAsync() As Task(Of Integer)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            Using selectCommand As MySqlCommand = connection.CreateCommand()
                selectCommand.CommandText = "SELECT Id FROM Suppliers WHERE Name = @name;"
                selectCommand.Parameters.AddWithValue("@name", FixtureSupplierName)
                Dim existing As Object = Await selectCommand.ExecuteScalarAsync()
                If existing IsNot Nothing Then
                    Return CInt(existing)
                End If
            End Using

            Using insertCommand As MySqlCommand = connection.CreateCommand()
                insertCommand.CommandText =
                    "INSERT INTO Suppliers (Name, IsActive, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@name, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                insertCommand.Parameters.AddWithValue("@name", FixtureSupplierName)
                Await insertCommand.ExecuteNonQueryAsync()
                Return CInt(insertCommand.LastInsertedId)
            End Using

        End Using

    End Function

    ''' <summary>A fresh idempotency key per call - a matrix probe must never replay an earlier cell's committed order.</summary>
    Private Shared Function NewPurchaseOrderBody(supplierId As Integer, productId As Integer) As CreatePurchaseOrderRequest

        Return New CreatePurchaseOrderRequest With {
            .SupplierId = supplierId,
            .IdempotencyKey = Guid.NewGuid().ToString("d"),
            .Lines = New List(Of CreatePurchaseOrderLineRequest) From {
                New CreatePurchaseOrderLineRequest With {
                    .ProductId = productId, .OrderedQuantity = 1.000D, .PurchaseCost = 1.0000D}
            }
        }

    End Function

    ''' <summary>P3-05: the required-Reason body every cancel/close probe sends.</summary>
    Private Shared Function NewReasonBody() As CancelPurchaseOrderRequest
        Return New CancelPurchaseOrderRequest With {.Reason = "P3-05 matrix probe"}
    End Function

    ''' <summary>
    ''' P3-05: forces an order directly into <paramref name="status"/> - fixture
    ''' setup ONLY, standing in for Phase 4's receiving endpoints (which do not
    ''' exist yet) so the Close matrix cell has a PartiallyReceived/FullyReceived
    ''' order to probe. Same "direct SQL to arrange a precondition no live
    ''' endpoint can reach yet" shape as ResetStockBalanceDirectlyAsync below.
    ''' </summary>
    Private Async Function ForcePurchaseOrderStatusDirectlyAsync(orderId As Integer, status As String) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "UPDATE PurchaseOrders SET Status = @status, UpdatedAtUtc = UTC_TIMESTAMP(6) WHERE Id = @id;"
                command.Parameters.AddWithValue("@status", status)
                command.Parameters.AddWithValue("@id", orderId)
                Await command.ExecuteNonQueryAsync()
            End Using
        End Using

    End Function

    ''' <summary>P3-04: a fresh Draft order over real HTTP, using <paramref name="creatorToken"/>. Fails the test loudly if creation itself did not succeed - a matrix cell for Submit/Approve means nothing against a fixture that was never actually created.</summary>
    Private Async Function CreateDraftOrderAsync(
        client As HttpClient, creatorToken As String, supplierId As Integer, productId As Integer) As Task(Of Integer)

        Using response As HttpResponseMessage =
            Await SendAsync(client, HttpMethod.Post, "/api/v1/purchase-orders", creatorToken,
                             requestBody:=NewPurchaseOrderBody(supplierId, productId))

            Assert.AreEqual(
                HttpStatusCode.Created, response.StatusCode,
                "Fixture purchase-order creation must succeed. Body: " & Await response.Content.ReadAsStringAsync())

            Dim created As PurchaseOrderResponse = Await response.Content.ReadFromJsonAsync(Of PurchaseOrderResponse)()
            Return created.Id

        End Using

    End Function

    ''' <summary>
    ''' Resets the fixture's balance to <paramref name="quantity"/>. P4-01's
    ''' ledger reconciliation asserts SUM(StockMovements) = StockBalances
    ''' for every product, so this now writes the matching compensating
    ''' StockMovements row in the same transaction first, the same "no
    ''' balance change without a movement" shape every real command follows
    ''' - a bare balance UPDATE here would otherwise break that invariant
    ''' for this fixture on every run. Attributed to the fixture SuperAdmin
    ''' user, which every caller of this method has already ensured exists
    ''' via EnsureAllFixtureUsersAsync.
    ''' </summary>
    Private Async Function ResetStockBalanceDirectlyAsync(productId As Integer, quantity As Decimal) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using transaction As MySqlTransaction = Await connection.BeginTransactionAsync()

                Dim currentQuantity As Decimal = 0D
                Using selectCommand As MySqlCommand = connection.CreateCommand()
                    selectCommand.Transaction = transaction
                    selectCommand.CommandText = "SELECT Quantity FROM StockBalances WHERE ProductId = @productId;"
                    selectCommand.Parameters.AddWithValue("@productId", productId)
                    Dim existing As Object = Await selectCommand.ExecuteScalarAsync()
                    If existing IsNot Nothing Then
                        currentQuantity = CDec(existing)
                    End If
                End Using

                Dim delta As Decimal = quantity - currentQuantity

                If delta <> 0D Then

                    Dim actorUserId As Integer
                    Using actorCommand As MySqlCommand = connection.CreateCommand()
                        actorCommand.Transaction = transaction
                        actorCommand.CommandText = "SELECT Id FROM Users WHERE Username = @username;"
                        actorCommand.Parameters.AddWithValue("@username", FixtureUsername(RoleNames.SuperAdmin))
                        actorUserId = CInt(Await actorCommand.ExecuteScalarAsync())
                    End Using

                    Using movementCommand As MySqlCommand = connection.CreateCommand()
                        movementCommand.Transaction = transaction
                        movementCommand.CommandText =
                            "INSERT INTO StockMovements (ProductId, Delta, QuantityBefore, QuantityAfter, Reason, ActorUserId, CorrelationId, CreatedAtUtc) " &
                            "VALUES (@productId, @delta, @before, @after, 'Test fixture balance reset (AuthorizationMatrixTests)', @actorUserId, @correlationId, UTC_TIMESTAMP(6));"
                        movementCommand.Parameters.AddWithValue("@productId", productId)
                        movementCommand.Parameters.AddWithValue("@delta", delta)
                        movementCommand.Parameters.AddWithValue("@before", currentQuantity)
                        movementCommand.Parameters.AddWithValue("@after", quantity)
                        movementCommand.Parameters.AddWithValue("@actorUserId", actorUserId)
                        movementCommand.Parameters.AddWithValue("@correlationId", Guid.NewGuid().ToString())
                        Await movementCommand.ExecuteNonQueryAsync()
                    End Using

                End If

                Using balanceCommand As MySqlCommand = connection.CreateCommand()
                    balanceCommand.Transaction = transaction
                    balanceCommand.CommandText =
                        "UPDATE StockBalances SET Quantity = @quantity, RowVersion = RowVersion + 1, UpdatedAtUtc = UTC_TIMESTAMP(6) WHERE ProductId = @productId;"
                    balanceCommand.Parameters.AddWithValue("@quantity", quantity)
                    balanceCommand.Parameters.AddWithValue("@productId", productId)
                    Await balanceCommand.ExecuteNonQueryAsync()
                End Using

                Await transaction.CommitAsync()

            End Using
        End Using

    End Function

    Private Async Function ResetProductPriceDirectlyAsync(productId As Integer, price As Decimal, cost As Decimal) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "UPDATE Products SET Price = @price, Cost = @cost, UpdatedAtUtc = UTC_TIMESTAMP(6) WHERE Id = @productId;"
                command.Parameters.AddWithValue("@price", price)
                command.Parameters.AddWithValue("@cost", cost)
                command.Parameters.AddWithValue("@productId", productId)
                Await command.ExecuteNonQueryAsync()
            End Using
        End Using

    End Function

    ''' <summary>Releases rather than deletes - merch_api holds no DELETE on MaintenanceLocks (db/grants/0005), same reasoning as MaintenanceModeTests.</summary>
    Private Async Function ReleaseAnyActiveLockDirectlyAsync() As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "UPDATE MaintenanceLocks " &
                    "SET ReleasedAtUtc = UTC_TIMESTAMP(6), VerificationPassed = 1, " &
                    "    Detail = CONCAT(COALESCE(Detail,''), ' [released by P2-03 test cleanup]') " &
                    "WHERE ReleasedAtUtc IS NULL;"
                Await command.ExecuteNonQueryAsync()
            End Using
        End Using

    End Function

    Private Shared Function LoadMigratorOptions() As DatabaseOptions
        Dim migratorConfigPath As String =
            Path.Combine(Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName)
        Return DatabaseOptionsLoader.Load(migratorConfigPath)
    End Function

End Class
