' Merchandising.Tests.Integration.ProductsControllerTests
'
' P2-07: Product CRUD with uniqueness enforced at DB AND API. Role-gating
' itself (Products.Manage/Products.Read resolve to the roles PolicyRegistry
' says they do) is AuthorizationMatrixTests' own generic coverage test's
' job, the same split P2-05 used - this file proves the feature behavior:
' duplicate SKU/barcode rejected at the API layer with a usable message,
' AND at the database layer under real concurrency that bypasses the API's
' own pre-check (done-when boxes 1-3); over-scale Price/Cost/ReorderLevel
' rejected before binding (box 4, ADR-004.1); every mutation audited
' (box 5).
'
' Fixture accounts/products are real, permanent rows, the same shape every
' earlier integration suite uses - Products has no DELETE grant for
' merch_api (0002_post-migration-grants.sql), so nothing here is cleaned up
' in TearDown; each test uses a fresh Guid-derived SKU/barcode so runs never
' collide with each other or with earlier runs.

Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Net.Http.Json
Imports System.Threading.Tasks
Imports Merchandising.Contracts.Auth
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Products
Imports Merchandising.Domain.Entities
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class ProductsControllerTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const FixturePassword As String = "P2-07 Fixture Passw0rd!"
    Private Const InventoryClerkFixtureUsername As String = "p2_07_fixture_inventoryclerk"
    Private Const CashierFixtureUsername As String = "p2_07_fixture_cashier"

    Private _factory As MerchandisingApiFactory
    Private _connectionFactory As ConnectionFactory

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _factory = New MerchandisingApiFactory()
        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        Await EnsureFixtureUserAsync(InventoryClerkFixtureUsername, "InventoryClerk")
        Await EnsureFixtureUserAsync(CashierFixtureUsername, "Cashier")

    End Function

    <TestCleanup>
    Public Sub TearDown()
        _factory?.Dispose()
    End Sub

    ''' <summary>Happy path: create, then read it back, then update it.</summary>
    <TestMethod>
    Public Async Function Create_ThenGet_ThenUpdate_RoundTrips() As Task

        Dim sku As String = FreshSku()

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, InventoryClerkFixtureUsername)

            Dim createResponse As ProductResponse = Await CreateProductAsync(
                client, token, New CreateProductRequest With {
                    .Sku = sku, .Name = "P2-07 Round Trip Product", .Price = 19.9900D, .Cost = 10.0000D
                })

            Assert.IsGreaterThan(0, createResponse.Id)
            Assert.AreEqual(sku, createResponse.Sku)
            Assert.IsTrue(createResponse.IsActive, "A newly created product must be Active.")
            Assert.AreEqual(0L, createResponse.RowVersion)

            Using getResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Get, $"/api/v1/products/{createResponse.Id}", token, Nothing)
                Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode)
                Dim fetched As ProductResponse = Await getResponse.Content.ReadFromJsonAsync(Of ProductResponse)()
                Assert.AreEqual(sku, fetched.Sku)
            End Using

            Using updateResponse As HttpResponseMessage =
                Await SendAsync(
                    client, HttpMethod.Put, $"/api/v1/products/{createResponse.Id}", token,
                    New UpdateProductRequest With {.Name = "P2-07 Renamed Product", .ReorderLevel = 5.000D})

                Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode)
                Dim updated As ProductResponse = Await updateResponse.Content.ReadFromJsonAsync(Of ProductResponse)()
                Assert.AreEqual("P2-07 Renamed Product", updated.Name)
                Assert.AreEqual(5.000D, updated.ReorderLevel)
                Assert.AreEqual(1L, updated.RowVersion, "RowVersion must advance on update.")
                Assert.AreEqual(19.9900D, updated.Price, "Price must be unchanged - PUT does not touch it.")

            End Using

        End Using

    End Function

    ''' <summary>Done-when box 1: duplicate SKU rejected with a stable error code and field-level detail.</summary>
    <TestMethod>
    Public Async Function CreateProduct_DuplicateSku_RejectedWithStableErrorCodeAndFieldDetail() As Task

        Dim sku As String = FreshSku()

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, InventoryClerkFixtureUsername)

            Await CreateProductAsync(client, token, New CreateProductRequest With {
                .Sku = sku, .Name = "P2-07 Duplicate Sku Original", .Price = 5.0000D, .Cost = 2.0000D})

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/products", token,
                    New CreateProductRequest With {.Sku = sku, .Name = "P2-07 Duplicate Sku Second", .Price = 5.0000D, .Cost = 2.0000D})

                Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("DUPLICATE_SKU", body.ErrorCode)
                Assert.IsTrue(body.Errors IsNot Nothing AndAlso body.Errors.ContainsKey("sku"))
                Assert.IsFalse(String.IsNullOrWhiteSpace(body.CorrelationId))

            End Using

        End Using

    End Function

    ''' <summary>Done-when box 3 (HTTP half): duplicate ACTIVE barcode rejected the same way.</summary>
    <TestMethod>
    Public Async Function CreateProduct_DuplicateActiveBarcode_RejectedWithStableErrorCodeAndFieldDetail() As Task

        Dim barcode As String = FreshBarcode()

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, InventoryClerkFixtureUsername)

            Await CreateProductAsync(client, token, New CreateProductRequest With {
                .Sku = FreshSku(), .Name = "P2-07 Duplicate Barcode Original", .Barcode = barcode, .Price = 5.0000D, .Cost = 2.0000D})

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/products", token,
                    New CreateProductRequest With {.Sku = FreshSku(), .Name = "P2-07 Duplicate Barcode Second", .Barcode = barcode, .Price = 5.0000D, .Cost = 2.0000D})

                Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("DUPLICATE_BARCODE", body.ErrorCode)
                Assert.IsTrue(body.Errors IsNot Nothing AndAlso body.Errors.ContainsKey("barcode"))

            End Using

        End Using

    End Function

    ''' <summary>Done-when box 2: duplicate SKU rejected by the DATABASE, bypassing the API's own pre-check, under real concurrency.</summary>
    <TestMethod>
    Public Async Function InsertAsync_TwoConcurrentInsertsSameSku_ExactlyOneSucceeds() As Task

        Dim sku As String = FreshSku()

        Dim task1 As Task(Of (Kind As ProductWriteOutcomeKind, ProductId As Integer)) = InsertRaceProductAsync(sku, FreshBarcode())
        Dim task2 As Task(Of (Kind As ProductWriteOutcomeKind, ProductId As Integer)) = InsertRaceProductAsync(sku, FreshBarcode())

        Dim results = Await Task.WhenAll(task1, task2)

        Dim successCount As Integer = results.Count(Function(r) r.Kind = ProductWriteOutcomeKind.Success)
        Dim duplicateCount As Integer = results.Count(Function(r) r.Kind = ProductWriteOutcomeKind.DuplicateSku)

        Console.WriteLine($"P2-07 concurrent SKU insert -> {results(0).Kind} | {results(1).Kind}")

        Assert.AreEqual(1, successCount, "Exactly one of two simultaneous inserts sharing a SKU must succeed.")
        Assert.AreEqual(1, duplicateCount, "The other must be reported as DuplicateSku, not an unhandled exception.")

    End Function

    ''' <summary>Done-when box 3 (DB half): the same proof for an active barcode, bypassing the API's own pre-check.</summary>
    <TestMethod>
    Public Async Function InsertAsync_TwoConcurrentInsertsSameActiveBarcode_ExactlyOneSucceeds() As Task

        Dim barcode As String = FreshBarcode()

        Dim task1 As Task(Of (Kind As ProductWriteOutcomeKind, ProductId As Integer)) = InsertRaceProductAsync(FreshSku(), barcode)
        Dim task2 As Task(Of (Kind As ProductWriteOutcomeKind, ProductId As Integer)) = InsertRaceProductAsync(FreshSku(), barcode)

        Dim results = Await Task.WhenAll(task1, task2)

        Dim successCount As Integer = results.Count(Function(r) r.Kind = ProductWriteOutcomeKind.Success)
        Dim duplicateCount As Integer = results.Count(Function(r) r.Kind = ProductWriteOutcomeKind.DuplicateBarcode)

        Console.WriteLine($"P2-07 concurrent barcode insert -> {results(0).Kind} | {results(1).Kind}")

        Assert.AreEqual(1, successCount, "Exactly one of two simultaneous inserts sharing an active barcode must succeed.")
        Assert.AreEqual(1, duplicateCount, "The other must be reported as DuplicateBarcode, not an unhandled exception.")

    End Function

    ''' <summary>Done-when box 4: an over-scale Price is rejected before anything is stored, not silently rounded.</summary>
    <TestMethod>
    Public Async Function CreateProduct_OverScalePrice_RejectedBeforeAnyRowWritten() As Task

        Dim sku As String = FreshSku()

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, InventoryClerkFixtureUsername)

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/products", token,
                    New CreateProductRequest With {.Sku = sku, .Name = "P2-07 Over Scale", .Price = 1.99999D, .Cost = 1.0000D})

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("VALIDATION_FAILED", body.ErrorCode)
                Assert.IsTrue(body.Errors IsNot Nothing AndAlso body.Errors.ContainsKey("price"))

            End Using

            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
                Dim existing As Product = Await ProductRepository.FindBySkuAsync(connection, sku)
                Assert.IsNull(existing, "A rejected over-scale create must not write a row.")
            End Using

        End Using

    End Function

    ''' <summary>Done-when box 4: an over-scale ReorderLevel (quantity scale) is rejected the same way.</summary>
    <TestMethod>
    Public Async Function CreateProduct_OverScaleReorderLevel_RejectedBeforeAnyRowWritten() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, InventoryClerkFixtureUsername)

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/products", token,
                    New CreateProductRequest With {
                        .Sku = FreshSku(), .Name = "P2-07 Over Scale Reorder", .Price = 1.0000D, .Cost = 1.0000D, .ReorderLevel = 1.9999D})

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("VALIDATION_FAILED", body.ErrorCode)
                Assert.IsTrue(body.Errors IsNot Nothing AndAlso body.Errors.ContainsKey("reorderLevel"))

            End Using

        End Using

    End Function

    ''' <summary>Done-when box 5: a successful create writes an AuditLogs row.</summary>
    <TestMethod>
    Public Async Function CreateProduct_Success_WritesAuditRow() As Task

        Dim sku As String = FreshSku()

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, InventoryClerkFixtureUsername)
            Dim correlationId As String = Guid.NewGuid().ToString()

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/products", token,
                    New CreateProductRequest With {.Sku = sku, .Name = "P2-07 Audited Create", .Price = 3.0000D, .Cost = 1.0000D},
                    correlationId)

                Assert.AreEqual(HttpStatusCode.Created, response.StatusCode)

            End Using

            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
                Dim auditCount As Long = Await CountAuditRowsAsync(connection, correlationId, "ProductCreated")
                Assert.AreEqual(1L, auditCount, "Exactly one AuditLogs row must be written for a successful create.")
            End Using

        End Using

    End Function

    ''' <summary>Done-when box 5: a successful update also writes an AuditLogs row.</summary>
    <TestMethod>
    Public Async Function UpdateProduct_Success_WritesAuditRow() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, InventoryClerkFixtureUsername)

            Dim created As ProductResponse = Await CreateProductAsync(client, token, New CreateProductRequest With {
                .Sku = FreshSku(), .Name = "P2-07 Audited Update Original", .Price = 3.0000D, .Cost = 1.0000D})

            Dim correlationId As String = Guid.NewGuid().ToString()

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Put, $"/api/v1/products/{created.Id}", token,
                    New UpdateProductRequest With {.Name = "P2-07 Audited Update Renamed"}, correlationId)

                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode)

            End Using

            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
                Dim auditCount As Long = Await CountAuditRowsAsync(connection, correlationId, "ProductUpdated")
                Assert.AreEqual(1L, auditCount, "Exactly one AuditLogs row must be written for a successful update.")
            End Using

        End Using

    End Function

    ''' <summary>A product not found returns a controlled 404, not an unhandled error.</summary>
    <TestMethod>
    Public Async Function GetProduct_UnknownId_ReturnsControlled404() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, InventoryClerkFixtureUsername)

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Get, "/api/v1/products/999999999", token, Nothing)

                Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("PRODUCT_NOT_FOUND", body.ErrorCode)

            End Using

        End Using

    End Function

    ''' <summary>Search by SKU (spec section 10.3) finds the fixture product it was created with.</summary>
    <TestMethod>
    Public Async Function SearchProducts_ByExactSku_FindsIt() As Task

        Dim sku As String = FreshSku()

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, InventoryClerkFixtureUsername)

            Await CreateProductAsync(client, token, New CreateProductRequest With {
                .Sku = sku, .Name = "P2-07 Searchable Product", .Price = 1.0000D, .Cost = 0.5000D})

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Get, $"/api/v1/products?q={sku}", token, Nothing)

                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode)
                Dim body As ProductSearchResponse = Await response.Content.ReadFromJsonAsync(Of ProductSearchResponse)()
                Assert.IsTrue(body.Items.Any(Function(p) String.Equals(p.Sku, sku, StringComparison.Ordinal)))

            End Using

        End Using

    End Function

    ''' <summary>A Cashier - not in Products.Manage's InventoryAndAbove list - is refused.</summary>
    <TestMethod>
    Public Async Function CreateProduct_AsCashier_Refused403() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, CashierFixtureUsername)

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/products", token,
                    New CreateProductRequest With {.Sku = FreshSku(), .Name = "P2-07 Cashier Attempt", .Price = 1.0000D, .Cost = 0.5000D})

                Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode)

            End Using

        End Using

    End Function

    ' --------------------------------------------------------------- shared helpers

    Private Shared Function FreshSku() As String
        Return "p2_07_" & Guid.NewGuid().ToString("N").Substring(0, 12)
    End Function

    Private Shared Function FreshBarcode() As String
        Return "P2-07-" & Guid.NewGuid().ToString("N").Substring(0, 12)
    End Function

    Private Async Function InsertRaceProductAsync(sku As String, barcode As String) As Task(Of (Kind As ProductWriteOutcomeKind, ProductId As Integer))

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Dim transaction As MySqlTransaction = Await connection.BeginTransactionAsync()

            Dim product As New Product With {
                .Sku = sku, .Barcode = barcode, .Name = "P2-07 Race Product", .Price = 1.0000D, .Cost = 0.5000D, .ReorderLevel = 0D}

            Dim result = Await ProductRepository.InsertAsync(connection, transaction, product)

            If result.Kind = ProductWriteOutcomeKind.Success Then
                Await transaction.CommitAsync()
            Else
                Await transaction.RollbackAsync()
            End If
            Await transaction.DisposeAsync()

            Return result
        End Using

    End Function

    Private Async Function LoginAsync(client As HttpClient, username As String) As Task(Of String)

        Using response As HttpResponseMessage =
            Await client.PostAsJsonAsync("/api/v1/auth/login", New LoginRequest With {.Username = username, .Password = FixturePassword})

            Assert.AreEqual(
                HttpStatusCode.OK, response.StatusCode,
                $"Fixture user '{username}' could not log in. Body: " & Await response.Content.ReadAsStringAsync())

            Dim login As LoginResponse = Await response.Content.ReadFromJsonAsync(Of LoginResponse)()
            Return login.Token

        End Using

    End Function

    Private Async Function CreateProductAsync(client As HttpClient, token As String, request As CreateProductRequest) As Task(Of ProductResponse)

        Using response As HttpResponseMessage = Await SendAsync(client, HttpMethod.Post, "/api/v1/products", token, request)
            Assert.AreEqual(
                HttpStatusCode.Created, response.StatusCode,
                "Fixture product creation failed. Body: " & Await response.Content.ReadAsStringAsync())
            Return Await response.Content.ReadFromJsonAsync(Of ProductResponse)()
        End Using

    End Function

    Private Shared Async Function SendAsync(
        client As HttpClient, method As HttpMethod, path As String, token As String, requestBody As Object,
        Optional correlationId As String = Nothing) As Task(Of HttpResponseMessage)

        Dim request As New HttpRequestMessage(method, path)

        If token IsNot Nothing Then
            request.Headers.Authorization = New AuthenticationHeaderValue("Bearer", token)
        End If

        If correlationId IsNot Nothing Then
            request.Headers.Add("X-Correlation-Id", correlationId)
        End If

        If requestBody IsNot Nothing Then
            request.Content = JsonContent.Create(requestBody)
        End If

        Return Await client.SendAsync(request)

    End Function

    Private Async Function EnsureFixtureUserAsync(username As String, roleName As String) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Dim existing As User = Await UserRepository.FindByUsernameAsync(connection, username)
            If existing IsNot Nothing Then
                Return
            End If
        End Using

        Dim migratorFactory As New ConnectionFactory(LoadMigratorOptions())
        Await CreateUserCommand.RunAsync(migratorFactory, username, FixturePassword, roleName)

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
