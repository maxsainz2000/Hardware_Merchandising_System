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
    Private Const BaselineQuantity As Decimal = 100.000D

    ''' <summary>
    ''' Actions that require login but deliberately carry no named policy -
    ''' spec section 13: "any authenticated role" for /me, and logout needs
    ''' only a valid token to end its own session. Anything else reaching
    ''' the coverage test's "authenticated, no policy" bucket fails instead
    ''' of joining this list silently.
    ''' </summary>
    Private Shared ReadOnly AuthenticatedNoPolicyAllowlist As String() = {
        "Merchandising.Api.Controllers.AuthController.GetCurrentUser",
        "Merchandising.Api.Controllers.AuthController.Logout",
        "Merchandising.Api.Controllers.SystemSettingsController.GetSettings"
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

            Using balanceCommand As MySqlCommand = connection.CreateCommand()
                balanceCommand.CommandText =
                    "INSERT INTO StockBalances (ProductId, Quantity, RowVersion, UpdatedAtUtc) " &
                    "VALUES (@productId, @quantity, 0, UTC_TIMESTAMP(6));"
                balanceCommand.Parameters.AddWithValue("@productId", productId)
                balanceCommand.Parameters.AddWithValue("@quantity", BaselineQuantity)
                Await balanceCommand.ExecuteNonQueryAsync()
            End Using

            Return productId

        End Using

    End Function

    Private Async Function ResetStockBalanceDirectlyAsync(productId As Integer, quantity As Decimal) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "UPDATE StockBalances SET Quantity = @quantity, UpdatedAtUtc = UTC_TIMESTAMP(6) WHERE ProductId = @productId;"
                command.Parameters.AddWithValue("@quantity", quantity)
                command.Parameters.AddWithValue("@productId", productId)
                Await command.ExecuteNonQueryAsync()
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
