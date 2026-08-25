' Merchandising.Tests.Integration.ProductLifecycleTests
'
' P2-09: Catalog.ProductLifecycleService against the real, pinned MariaDB
' instance - proves spec section 12's "master data is deactivated where
' possible" and its historical-reference-preservation half. Role-gating
' (Products.Manage) is AuthorizationMatrixTests' own generic coverage test's
' job - this card's own done-when boxes ask for none of the specific-cell
' matrix proof P2-08's did, so none is added here.
'
' Fixture product/actor are real, permanent rows, the same shape
' PriceChangeTests/StockDecrementTests use - Products has no DELETE grant
' for merch_api, so nothing here is torn down; each test resets the
' fixture's IsActive to the known baseline (Active) before asserting.

Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Net.Http.Json
Imports System.Threading.Tasks
Imports Merchandising.Api.Catalog
Imports Merchandising.Api.Inventory
Imports Merchandising.Contracts.Auth
Imports Merchandising.Contracts.Products
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class ProductLifecycleTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"

    Private Const FixtureSku As String = "p2_09_fixture_sku"
    Private Const FixtureActorUsername As String = "p2_09_fixture_actor"
    Private Const FixtureActorPassword As String = "P2-09 Fixture Passw0rd!"
    Private Const FixtureInventoryClerkUsername As String = "p2_09_fixture_inventoryclerk"

    Private Const BaselineStockQuantity As Decimal = 20.000D

    Private _connectionFactory As ConnectionFactory
    Private _lifecycleService As ProductLifecycleService
    Private _productId As Integer
    Private _actorUserId As Integer

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        _lifecycleService = New ProductLifecycleService(_connectionFactory)

        _productId = Await EnsureFixtureProductAsync()
        Await ResetActiveStateDirectlyAsync(_productId, isActive:=True)
        _actorUserId = Await EnsureFixtureActorAsync()
        Await EnsureFixtureInventoryClerkAsync()

    End Function

    ''' <summary>Baseline: a successful deactivate flips the flag and audits.</summary>
    <TestMethod>
    Public Async Function DeactivateAsync_Success_SetsInactiveAndAudits() As Task

        Dim correlationId As String = Guid.NewGuid().ToString()

        Dim outcome As ProductLifecycleOutcome = Await _lifecycleService.DeactivateAsync(_productId, _actorUserId, correlationId)

        Assert.AreEqual(ProductLifecycleOutcomeKind.Success, outcome.Kind)
        Assert.IsFalse(outcome.Response.IsActive)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Assert.IsFalse(Await ReadIsActiveAsync(connection, _productId))
            Dim auditCount As Long = Await CountAuditRowsAsync(connection, correlationId, "ProductDeactivated")
            Assert.AreEqual(1L, auditCount)
        End Using

    End Function

    ''' <summary>Deactivating an already-inactive product is refused, not a silent no-op.</summary>
    <TestMethod>
    Public Async Function DeactivateAsync_AlreadyInactive_ReturnsNoChange() As Task

        Await _lifecycleService.DeactivateAsync(_productId, _actorUserId, Guid.NewGuid().ToString())

        Dim outcome As ProductLifecycleOutcome = Await _lifecycleService.DeactivateAsync(_productId, _actorUserId, Guid.NewGuid().ToString())

        Assert.AreEqual(ProductLifecycleOutcomeKind.NoChange, outcome.Kind)

    End Function

    <TestMethod>
    Public Async Function DeactivateAsync_UnknownProduct_ReturnsProductNotFound() As Task

        Dim outcome As ProductLifecycleOutcome = Await _lifecycleService.DeactivateAsync(999999999, _actorUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(ProductLifecycleOutcomeKind.ProductNotFound, outcome.Kind)

    End Function

    ''' <summary>Done-when box 4: reactivation restores availability without duplicating the record.</summary>
    <TestMethod>
    Public Async Function ReactivateAsync_Success_RestoresActiveAndAudits_NoDuplicateRow() As Task

        Await _lifecycleService.DeactivateAsync(_productId, _actorUserId, Guid.NewGuid().ToString())

        Dim correlationId As String = Guid.NewGuid().ToString()
        Dim outcome As ProductLifecycleOutcome = Await _lifecycleService.ReactivateAsync(_productId, _actorUserId, correlationId)

        Assert.AreEqual(ProductLifecycleOutcomeKind.Success, outcome.Kind)
        Assert.IsTrue(outcome.Response.IsActive)
        Assert.AreEqual(_productId, outcome.Response.Id, "Reactivation must restore the SAME row, not create a new one.")

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            Assert.IsTrue(Await ReadIsActiveAsync(connection, _productId))

            Dim rowCount As Long = Await CountProductRowsForSkuAsync(connection, FixtureSku)
            Assert.AreEqual(1L, rowCount, "Reactivation must not duplicate the product row.")

            Dim auditCount As Long = Await CountAuditRowsAsync(connection, correlationId, "ProductReactivated")
            Assert.AreEqual(1L, auditCount)

        End Using

    End Function

    <TestMethod>
    Public Async Function ReactivateAsync_AlreadyActive_ReturnsNoChange() As Task

        Dim outcome As ProductLifecycleOutcome = Await _lifecycleService.ReactivateAsync(_productId, _actorUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(ProductLifecycleOutcomeKind.NoChange, outcome.Kind)

    End Function

    ''' <summary>Done-when box 1: deactivating a product referenced by a stock movement succeeds and the movement still resolves its product.</summary>
    <TestMethod>
    Public Async Function DeactivateAsync_ProductReferencedByStockMovement_MovementStillResolvesProduct() As Task

        Dim stockService As New StockService(_connectionFactory)
        Await ResetStockBalanceDirectlyAsync(_productId, BaselineStockQuantity)

        Dim decrementOutcome = Await stockService.DecrementAsync(
            _productId, 3.000D, "P2-09 fixture movement", _actorUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(StockDecrementOutcomeKind.Success, decrementOutcome.Kind, "Fixture movement must actually be created for this proof to mean anything.")

        Dim outcome As ProductLifecycleOutcome = Await _lifecycleService.DeactivateAsync(_productId, _actorUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(ProductLifecycleOutcomeKind.Success, outcome.Kind)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            ' The movement row itself, and its join back to the (now
            ' inactive) product, must both still resolve - no orphaning.
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT sm.Id, p.Name, p.IsActive FROM StockMovements sm " &
                    "JOIN Products p ON p.Id = sm.ProductId " &
                    "WHERE sm.ProductId = @productId ORDER BY sm.Id DESC LIMIT 1;"
                command.Parameters.AddWithValue("@productId", _productId)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync()
                    Assert.IsTrue(Await reader.ReadAsync(), "The movement must still join to its product after deactivation.")
                    Assert.AreEqual("P2-09 Fixture Product", reader.GetString(1))
                    Assert.IsFalse(reader.GetBoolean(2), "The product must actually be inactive at the time of this read.")
                End Using
            End Using

            ' Also resolvable through the ordinary repository read path
            ' GetProduct(id) uses - history/detail views are not filtered
            ' by IsActive.
            Dim product = Await ProductRepository.GetByIdAsync(connection, _productId)
            Assert.IsNotNull(product, "GetByIdAsync must still resolve an inactive product.")

        End Using

    End Function

    ''' <summary>
    ''' Done-when box 2 setup: leaves a real StockMovements-referenced fixture
    ''' row in place so evidence/phase-2/p2-09-lifecycle.txt's own DELETE
    ''' attempt (run via the mysql.exe CLI as root, the same isolation
    ''' p2-06-schema.txt used, and the same reason CLAUDE.md section 6.1
    ''' reserves root for "no application, ever" - not this test suite) has
    ''' something real to fail against. The FK error itself is captured
    ''' there, not asserted here through a root connection this codebase
    ''' otherwise never opens.
    ''' </summary>
    <TestMethod>
    Public Async Function EnsureFixtureHasStockMovement_ForForeignKeyProofInEvidence() As Task

        Dim stockService As New StockService(_connectionFactory)
        Await ResetStockBalanceDirectlyAsync(_productId, BaselineStockQuantity)
        Dim outcome = Await stockService.DecrementAsync(
            _productId, 1.000D, "P2-09 FK proof movement", _actorUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(StockDecrementOutcomeKind.Success, outcome.Kind)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM StockMovements WHERE ProductId = @productId;"
                command.Parameters.AddWithValue("@productId", _productId)
                Dim count As Long = CLng(Await command.ExecuteScalarAsync())
                Assert.IsGreaterThan(0L, count)
            End Using
        End Using

    End Function

    ''' <summary>Done-when box 3 (HTTP half): search excludes an inactive product by default, includes it when asked.</summary>
    <TestMethod>
    Public Async Function SearchProducts_DefaultExcludesInactive_IncludeInactiveShowsIt() As Task

        Await _lifecycleService.DeactivateAsync(_productId, _actorUserId, Guid.NewGuid().ToString())

        Using factory As New MerchandisingApiFactory()
            Using client As HttpClient = factory.CreateClient()

                Dim token As String = Await LoginAsync(client, FixtureInventoryClerkUsername)

                Using defaultResponse As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Get, $"/api/v1/products?q={FixtureSku}", token)
                    Dim defaultBody As ProductSearchResponse = Await defaultResponse.Content.ReadFromJsonAsync(Of ProductSearchResponse)()
                    Assert.IsFalse(defaultBody.Items.Any(Function(p) p.Sku = FixtureSku), "Default search must exclude an inactive product.")
                End Using

                Using includeResponse As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Get, $"/api/v1/products?q={FixtureSku}&includeInactive=true", token)
                    Dim includeBody As ProductSearchResponse = Await includeResponse.Content.ReadFromJsonAsync(Of ProductSearchResponse)()
                    Assert.IsTrue(includeBody.Items.Any(Function(p) p.Sku = FixtureSku), "includeInactive=true must still surface it (history/reports visibility, spec section 12).")
                End Using

                Using getResponse As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Get, $"/api/v1/products/{_productId}", token)
                    Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode, "GetProduct by Id must never be filtered by IsActive.")
                End Using

            End Using
        End Using

    End Function

    ''' <summary>End-to-end through the real HTTP pipeline: deactivate, then reactivate, both succeed and both audit.</summary>
    <TestMethod>
    Public Async Function DeactivateThenReactivate_ThroughHttp_BothSucceed() As Task

        Using factory As New MerchandisingApiFactory()
            Using client As HttpClient = factory.CreateClient()

                Dim token As String = Await LoginAsync(client, FixtureInventoryClerkUsername)

                Using deactivateResponse As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Post, $"/api/v1/products/{_productId}/deactivate", token)
                    Assert.AreEqual(HttpStatusCode.OK, deactivateResponse.StatusCode)
                    Dim body As ProductResponse = Await deactivateResponse.Content.ReadFromJsonAsync(Of ProductResponse)()
                    Assert.IsFalse(body.IsActive)
                End Using

                Using secondDeactivateResponse As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Post, $"/api/v1/products/{_productId}/deactivate", token)
                    Assert.AreEqual(HttpStatusCode.BadRequest, secondDeactivateResponse.StatusCode)
                End Using

                Using reactivateResponse As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Post, $"/api/v1/products/{_productId}/reactivate", token)
                    Assert.AreEqual(HttpStatusCode.OK, reactivateResponse.StatusCode)
                    Dim body As ProductResponse = Await reactivateResponse.Content.ReadFromJsonAsync(Of ProductResponse)()
                    Assert.IsTrue(body.IsActive)
                End Using

            End Using
        End Using

    End Function

    ' --------------------------------------------------------------- shared helpers

    Private Async Function LoginAsync(client As HttpClient, username As String) As Task(Of String)

        Using response As HttpResponseMessage =
            Await client.PostAsJsonAsync("/api/v1/auth/login", New LoginRequest With {.Username = username, .Password = FixtureActorPassword})

            Assert.AreEqual(
                HttpStatusCode.OK, response.StatusCode,
                $"Fixture user '{username}' could not log in. Body: " & Await response.Content.ReadAsStringAsync())

            Dim login As LoginResponse = Await response.Content.ReadFromJsonAsync(Of LoginResponse)()
            Return login.Token

        End Using

    End Function

    Private Shared Async Function SendAsync(client As HttpClient, method As HttpMethod, path As String, token As String) As Task(Of HttpResponseMessage)

        Dim request As New HttpRequestMessage(method, path)
        request.Headers.Authorization = New AuthenticationHeaderValue("Bearer", token)
        Return Await client.SendAsync(request)

    End Function

    Private Async Function EnsureFixtureProductAsync() As Task(Of Integer)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            Using selectCommand As MySqlCommand = connection.CreateCommand()
                selectCommand.CommandText = "SELECT Id FROM Products WHERE Sku = @sku;"
                selectCommand.Parameters.AddWithValue("@sku", FixtureSku)
                Dim existing As Object = Await selectCommand.ExecuteScalarAsync()
                If existing IsNot Nothing Then
                    Return CInt(existing)
                End If
            End Using

            Dim productId As Integer

            Using insertCommand As MySqlCommand = connection.CreateCommand()
                insertCommand.CommandText =
                    "INSERT INTO Products (Sku, Barcode, Name, Price, Cost, IsActive, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@sku, NULL, 'P2-09 Fixture Product', 5.0000, 2.5000, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                insertCommand.Parameters.AddWithValue("@sku", FixtureSku)
                Await insertCommand.ExecuteNonQueryAsync()
                productId = CInt(insertCommand.LastInsertedId)
            End Using

            Return productId

        End Using

    End Function

    Private Async Function ResetActiveStateDirectlyAsync(productId As Integer, isActive As Boolean) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "UPDATE Products SET IsActive = @isActive, UpdatedAtUtc = UTC_TIMESTAMP(6) WHERE Id = @productId;"
                command.Parameters.AddWithValue("@isActive", isActive)
                command.Parameters.AddWithValue("@productId", productId)
                Await command.ExecuteNonQueryAsync()
            End Using
        End Using

    End Function

    Private Async Function ResetStockBalanceDirectlyAsync(productId As Integer, quantity As Decimal) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO StockBalances (ProductId, Quantity, RowVersion, UpdatedAtUtc) " &
                    "VALUES (@productId, @quantity, 0, UTC_TIMESTAMP(6)) " &
                    "ON DUPLICATE KEY UPDATE Quantity = VALUES(Quantity), UpdatedAtUtc = VALUES(UpdatedAtUtc);"
                command.Parameters.AddWithValue("@productId", productId)
                command.Parameters.AddWithValue("@quantity", quantity)
                Await command.ExecuteNonQueryAsync()
            End Using
        End Using

    End Function

    Private Async Function EnsureFixtureActorAsync() As Task(Of Integer)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT Id FROM Users WHERE Username = @username;"
                command.Parameters.AddWithValue("@username", FixtureActorUsername)
                Dim existing As Object = Await command.ExecuteScalarAsync()
                If existing IsNot Nothing Then
                    Return CInt(existing)
                End If
            End Using
        End Using

        Dim migratorFactory As New ConnectionFactory(LoadMigratorOptions())
        Return Await CreateUserCommand.RunAsync(migratorFactory, FixtureActorUsername, FixtureActorPassword, "Admin")

    End Function

    ''' <summary>A second fixture user, same password as the actor, so the HTTP-level tests can log in without a separate constant.</summary>
    Private Async Function EnsureFixtureInventoryClerkAsync() As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT Id FROM Users WHERE Username = @username;"
                command.Parameters.AddWithValue("@username", FixtureInventoryClerkUsername)
                Dim existing As Object = Await command.ExecuteScalarAsync()
                If existing IsNot Nothing Then
                    Return
                End If
            End Using
        End Using

        Dim migratorFactory As New ConnectionFactory(LoadMigratorOptions())
        Await CreateUserCommand.RunAsync(migratorFactory, FixtureInventoryClerkUsername, FixtureActorPassword, "InventoryClerk")

    End Function

    Private Shared Async Function ReadIsActiveAsync(connection As MySqlConnection, productId As Integer) As Task(Of Boolean)

        Using command As MySqlCommand = connection.CreateCommand()
            command.CommandText = "SELECT IsActive FROM Products WHERE Id = @productId;"
            command.Parameters.AddWithValue("@productId", productId)
            Return CBool(Await command.ExecuteScalarAsync())
        End Using

    End Function

    Private Shared Async Function CountProductRowsForSkuAsync(connection As MySqlConnection, sku As String) As Task(Of Long)

        Using command As MySqlCommand = connection.CreateCommand()
            command.CommandText = "SELECT COUNT(*) FROM Products WHERE Sku = @sku;"
            command.Parameters.AddWithValue("@sku", sku)
            Return CLng(Await command.ExecuteScalarAsync())
        End Using

    End Function

    Private Shared Async Function CountAuditRowsAsync(connection As MySqlConnection, correlationId As String, action As String) As Task(Of Long)

        Using command As MySqlCommand = connection.CreateCommand()
            command.CommandText = "SELECT COUNT(*) FROM AuditLogs WHERE CorrelationId = @correlationId AND Action = @action;"
            command.Parameters.AddWithValue("@correlationId", correlationId)
            command.Parameters.AddWithValue("@action", action)
            Return CLng(Await command.ExecuteScalarAsync())
        End Using

    End Function

    Private Shared Function LoadMigratorOptions() As DatabaseOptions
        Dim migratorConfigPath As String =
            IO.Path.Combine(IO.Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName)
        Return DatabaseOptionsLoader.Load(migratorConfigPath)
    End Function

End Class
