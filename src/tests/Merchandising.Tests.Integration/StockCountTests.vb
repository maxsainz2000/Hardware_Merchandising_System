' Merchandising.Tests.Integration.StockCountTests
'
' P4-09: StockCountService.{OpenAsync,RecordLineAsync,CloseAsync,GetAsync}
' and the StockCountsController routes, proven against the real pinned
' MariaDB - spec section 10.2's "Stock counts record the counted quantity,
' system quantity, variance, count session, counted by, reviewed by,
' reason, and approval state."
'
' Role gating (StockCounts.Perform resolves to the roles PolicyRegistry
' says it does) is AuthorizationMatrixTests' job. This file proves the
' BEHAVIOUR:
'
'   box 1   variance is computed and stored server-side AT COUNT TIME; a
'           later balance change does not retroactively alter it
'   box 2   an Open count session takes no lock a sale or a receipt would
'           also need - StockRepository.GetQuantityAsync is a plain,
'           non-locking read, proven by running a real StockService
'           decrement against the SAME product while the count is still
'           Open
'   box 3   an over-scale counted quantity is refused 400 before any
'           connection is opened, HTTP-level (ADR-004.1) - the same
'           ReceiveGoods_OverScaleQuantity_Returns400 shape
'   box 4   a Closed count is immutable (no further line, cannot be closed
'           twice) and remains fully readable, both through the Close
'           response itself and a separate GET
'
' A fixture product is created fresh per test that needs one - CreateFixtureProductAsync
' - never shared, because several tests need to control its starting balance
' exactly and StockBalances/Products carry no DELETE grant for merch_api
' (ADR-013), the same "create fresh, never delete" shape ReceivingTests uses.

Imports System.Collections.Generic
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Net.Http.Json
Imports System.Threading.Tasks
Imports Merchandising.Api.Inventory
Imports Merchandising.Contracts.Auth
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Inventory
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class StockCountTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const FixturePassword As String = "P4-09 Fixture Passw0rd!"
    Private Const InventoryUsername As String = "p4_09_fixture_inventory"
    Private Const FixtureProductSkuPrefix As String = "p4_09_fixture_sku_"

    Private _factory As MerchandisingApiFactory
    Private _connectionFactory As ConnectionFactory
    Private _stockCountService As StockCountService
    Private _stockService As StockService
    Private _inventoryUserId As Integer

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _factory = New MerchandisingApiFactory()
        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        _stockCountService = New StockCountService(_connectionFactory)
        _stockService = New StockService(_connectionFactory)

        _inventoryUserId = Await EnsureFixtureUserAsync(InventoryUsername, "InventoryClerk")

    End Function

    <TestCleanup>
    Public Sub TearDown()
        _factory?.Dispose()
    End Sub

    ' ------------------------------------------------------------------ open

    ''' <summary>Opening a session creates it Open, with no lines yet.</summary>
    <TestMethod>
    Public Async Function OpenAsync_CreatesOpenSessionWithNoLines() As Task

        Dim outcome As StockCountOutcome =
            Await _stockCountService.OpenAsync(_inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(StockCountOutcomeKind.Created, outcome.Kind)
        Assert.AreEqual("Open", outcome.Session.Status)
        Assert.AreEqual(_inventoryUserId, outcome.Session.CountedByUserId)
        Assert.IsNull(outcome.Session.ApprovedByUserId)
        Assert.IsNull(outcome.Session.ApprovedAtUtc)
        Assert.IsEmpty(outcome.Session.Lines)

    End Function

    ''' <summary>ADR-007: a repeated key replays the ORIGINAL session rather than opening a second one.</summary>
    <TestMethod>
    Public Async Function OpenAsync_SameIdempotencyKeyTwice_ReturnsSameSession() As Task

        Dim idempotencyKey As String = Guid.NewGuid().ToString()

        Dim first As StockCountOutcome =
            Await _stockCountService.OpenAsync(_inventoryUserId, Guid.NewGuid().ToString(), idempotencyKey)
        Assert.AreEqual(StockCountOutcomeKind.Created, first.Kind)

        Dim second As StockCountOutcome =
            Await _stockCountService.OpenAsync(_inventoryUserId, Guid.NewGuid().ToString(), idempotencyKey)
        Assert.AreEqual(StockCountOutcomeKind.Replayed, second.Kind)

        Dim replayed As StockCountResponse = System.Text.Json.JsonSerializer.Deserialize(Of StockCountResponse)(second.ReplayPayload)
        Assert.AreEqual(first.Session.Id, replayed.Id, "A repeated key must replay the SAME session, not open a second one.")

    End Function

    ' ------------------------------------------------------------ record line

    ''' <summary>Box 1: SystemQuantity and Variance are computed from StockBalances at the moment this line is recorded.</summary>
    <TestMethod>
    Public Async Function RecordLineAsync_ComputesVarianceFromCurrentBalance_AndStoresIt() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SetBalanceAsync(productId, 25.500D)

        Dim session As StockCountOutcome =
            Await _stockCountService.OpenAsync(_inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Dim outcome As StockCountOutcome =
            Await _stockCountService.RecordLineAsync(
                session.Session.Id, productId, 20.000D, _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(StockCountOutcomeKind.Created, outcome.Kind)
        Assert.AreEqual(productId, outcome.Line.ProductId)
        Assert.AreEqual(20.000D, outcome.Line.CountedQuantity)
        Assert.AreEqual(25.500D, outcome.Line.SystemQuantity, "SystemQuantity must be the balance at count time.")
        Assert.AreEqual(-5.500D, outcome.Line.Variance, "Variance = Counted - System.")

    End Function

    ''' <summary>
    ''' Box 1 + Box 2 together: while the session is still Open, a real
    ''' StockService decrement against the SAME product succeeds immediately
    ''' (the count took no lock that could have blocked it); after the
    ''' session is Closed, the line's stored SystemQuantity/Variance are
    ''' still exactly what they were at count time, unaffected by that later
    ''' decrement.
    ''' </summary>
    <TestMethod>
    Public Async Function RecordLineAsync_LaterBalanceChangeDoesNotAlterStoredVariance_AndDoesNotBlockADecrement() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SetBalanceAsync(productId, 50.000D)

        Dim session As StockCountOutcome =
            Await _stockCountService.OpenAsync(_inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Dim lineOutcome As StockCountOutcome =
            Await _stockCountService.RecordLineAsync(
                session.Session.Id, productId, 48.000D, _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(StockCountOutcomeKind.Created, lineOutcome.Kind)
        Assert.AreEqual(50.000D, lineOutcome.Line.SystemQuantity)
        Assert.AreEqual(-2.000D, lineOutcome.Line.Variance)

        ' The count session is still Open here - nothing has closed it. If
        ' RecordLineAsync had locked StockBalances, this decrement would hang
        ' or fail; it must simply succeed.
        Dim decrementOutcome As StockDecrementOutcome =
            Await _stockService.DecrementAsync(
                productId, 10.000D, "P4-09 concurrency proof", _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(StockDecrementOutcomeKind.Success, decrementOutcome.Kind,
            "An open stock count must not block a decrement against the same product.")
        Assert.AreEqual(40.000D, decrementOutcome.Response.QuantityAfter)

        Dim closeOutcome As StockCountOutcome =
            Await _stockCountService.CloseAsync(
                session.Session.Id, _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(StockCountOutcomeKind.Created, closeOutcome.Kind)

        Dim closedLine As StockCountLineResponse = closeOutcome.Session.Lines(0)
        Assert.AreEqual(50.000D, closedLine.SystemQuantity,
            "The balance dropped to 40 AFTER this line was recorded - SystemQuantity must still read 50.")
        Assert.AreEqual(-2.000D, closedLine.Variance, "Variance must still be -2.000, not recomputed against the new balance of 40.")

    End Function

    ''' <summary>No session with the requested Id exists.</summary>
    <TestMethod>
    Public Async Function RecordLineAsync_UnknownStockCount_ReturnsNotFound() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()

        Dim outcome As StockCountOutcome =
            Await _stockCountService.RecordLineAsync(
                999999999, productId, 1.000D, _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(StockCountOutcomeKind.NotFound, outcome.Kind)

    End Function

    ''' <summary>A line naming a ProductId that does not exist is refused, and nothing is written.</summary>
    <TestMethod>
    Public Async Function RecordLineAsync_UnknownProduct_ReturnsProductNotFoundAndWritesNothing() As Task

        Dim session As StockCountOutcome =
            Await _stockCountService.OpenAsync(_inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Dim outcome As StockCountOutcome =
            Await _stockCountService.RecordLineAsync(
                session.Session.Id, 999999999, 1.000D, _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(StockCountOutcomeKind.ProductNotFound, outcome.Kind)

        Dim reread As StockCountResponse = Await _stockCountService.GetAsync(session.Session.Id)
        Assert.IsEmpty(reread.Lines, "A refused ProductNotFound line must not be written.")

    End Function

    ''' <summary>A Closed session refuses a further line.</summary>
    <TestMethod>
    Public Async Function RecordLineAsync_ClosedSession_RefusedNotOpen() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()

        Dim session As StockCountOutcome =
            Await _stockCountService.OpenAsync(_inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Await _stockCountService.CloseAsync(session.Session.Id, _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Dim outcome As StockCountOutcome =
            Await _stockCountService.RecordLineAsync(
                session.Session.Id, productId, 1.000D, _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(StockCountOutcomeKind.NotOpen, outcome.Kind)

    End Function

    ' ------------------------------------------------------------------ close

    <TestMethod>
    Public Async Function CloseAsync_UnknownSession_ReturnsNotFound() As Task

        Dim outcome As StockCountOutcome =
            Await _stockCountService.CloseAsync(999999999, _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(StockCountOutcomeKind.NotFound, outcome.Kind)

    End Function

    ''' <summary>Box 4: a Closed count cannot be closed again.</summary>
    <TestMethod>
    Public Async Function CloseAsync_AlreadyClosed_RefusedNotOpen() As Task

        Dim session As StockCountOutcome =
            Await _stockCountService.OpenAsync(_inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Await _stockCountService.CloseAsync(session.Session.Id, _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Dim outcome As StockCountOutcome =
            Await _stockCountService.CloseAsync(session.Session.Id, _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(StockCountOutcomeKind.NotOpen, outcome.Kind)

    End Function

    ''' <summary>Box 4: a Closed count's lines are returned by the Close response itself, and remain identically readable through a separate GET.</summary>
    <TestMethod>
    Public Async Function CloseAsync_ReturnsAllLinesForReading_AndGetAsyncMatchesAfterward() As Task

        Dim productA As Integer = Await CreateFixtureProductAsync()
        Dim productB As Integer = Await CreateFixtureProductAsync()
        Await SetBalanceAsync(productA, 10.000D)
        Await SetBalanceAsync(productB, 5.000D)

        Dim session As StockCountOutcome =
            Await _stockCountService.OpenAsync(_inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Await _stockCountService.RecordLineAsync(
            session.Session.Id, productA, 9.000D, _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Await _stockCountService.RecordLineAsync(
            session.Session.Id, productB, 5.000D, _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Dim closeOutcome As StockCountOutcome =
            Await _stockCountService.CloseAsync(session.Session.Id, _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(StockCountOutcomeKind.Created, closeOutcome.Kind)
        Assert.AreEqual("Closed", closeOutcome.Session.Status)
        Assert.HasCount(2, closeOutcome.Session.Lines)

        Dim reread As StockCountResponse = Await _stockCountService.GetAsync(session.Session.Id)
        Assert.AreEqual("Closed", reread.Status)
        Assert.HasCount(2, reread.Lines)
        Assert.AreEqual(closeOutcome.Session.Lines(0).Variance, reread.Lines(0).Variance)
        Assert.AreEqual(closeOutcome.Session.Lines(1).Variance, reread.Lines(1).Variance)

    End Function

    <TestMethod>
    Public Async Function GetAsync_UnknownId_ReturnsNothing() As Task

        Dim result As StockCountResponse = Await _stockCountService.GetAsync(999999999)
        Assert.IsNull(result)

    End Function

    ' ------------------------------------------------------------------ HTTP

    ''' <summary>Box 3: an over-scale counted quantity is refused 400 before any connection is opened - never silently rounded (ADR-004.1). Same shape ReceiveGoods_OverScaleQuantity_Returns400 uses.</summary>
    <TestMethod>
    Public Async Function RecordStockCountLine_OverScaleQuantity_Returns400AndWritesNothing() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, InventoryUsername)

            Using openResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/stock-counts", token,
                                 New OpenStockCountRequest With {.IdempotencyKey = Guid.NewGuid().ToString("d")})
                Assert.AreEqual(HttpStatusCode.Created, openResponse.StatusCode)

                Dim opened As StockCountResponse = Await openResponse.Content.ReadFromJsonAsync(Of StockCountResponse)()

                Dim request As New RecordStockCountLineRequest With {
                    .ProductId = productId, .CountedQuantity = 1.9999D, .IdempotencyKey = Guid.NewGuid().ToString("d")}

                Using lineResponse As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Post, $"/api/v1/stock-counts/{opened.Id}/lines", token, request)

                    Assert.AreEqual(HttpStatusCode.BadRequest, lineResponse.StatusCode)
                    Dim body As ApiErrorResponse = Await lineResponse.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                    Assert.AreEqual("VALIDATION_FAILED", body.ErrorCode)
                    Assert.IsTrue(body.Errors.ContainsKey("countedQuantity"))

                End Using

                Dim reread As StockCountResponse = Await _stockCountService.GetAsync(opened.Id)
                Assert.IsEmpty(reread.Lines, "A refused over-scale line must never reach a bound SQL parameter.")

            End Using

        End Using

    End Function

    ' --------------------------------------------------------------- helpers (HTTP)

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

    ' --------------------------------------------------------------- helpers (direct service / raw)

    Private Async Function CreateFixtureProductAsync() As Task(Of Integer)

        Dim sku As String = FixtureProductSkuPrefix & Guid.NewGuid().ToString("N").Substring(0, 16)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO Products (Sku, Barcode, Name, Price, Cost, IsActive, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@sku, NULL, 'P4-09 Fixture Product', 9.0000, 4.0000, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@sku", sku)
                Await command.ExecuteNonQueryAsync()
                Return CInt(command.LastInsertedId)
            End Using
        End Using

    End Function

    ''' <summary>
    ''' Sets a fresh product's balance to <paramref name="quantity"/>, via
    ''' StockRepository.IncrementAsync's own upsert (safe against a product
    ''' that has never had a StockBalances row) plus a matching compensating
    ''' StockMovements row, so P4-01's ledger reconciliation stays satisfied
    ''' for this fixture - the same shape AuthorizationMatrixTests'
    ''' ResetStockBalanceDirectlyAsync uses.
    ''' </summary>
    Private Async Function SetBalanceAsync(productId As Integer, quantity As Decimal) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using transaction As MySqlTransaction = Await connection.BeginTransactionAsync()

                Dim result = Await StockRepository.IncrementAsync(connection, transaction, productId, quantity)

                Using movementCommand As MySqlCommand = connection.CreateCommand()
                    movementCommand.Transaction = transaction
                    movementCommand.CommandText =
                        "INSERT INTO StockMovements (ProductId, Delta, QuantityBefore, QuantityAfter, Reason, ActorUserId, CorrelationId, CreatedAtUtc) " &
                        "VALUES (@productId, @delta, @before, @after, 'Test fixture balance seed (StockCountTests)', @actorUserId, @correlationId, UTC_TIMESTAMP(6));"
                    movementCommand.Parameters.AddWithValue("@productId", productId)
                    movementCommand.Parameters.AddWithValue("@delta", quantity)
                    movementCommand.Parameters.AddWithValue("@before", result.QuantityBefore)
                    movementCommand.Parameters.AddWithValue("@after", result.QuantityAfter)
                    movementCommand.Parameters.AddWithValue("@actorUserId", _inventoryUserId)
                    movementCommand.Parameters.AddWithValue("@correlationId", Guid.NewGuid().ToString())
                    Await movementCommand.ExecuteNonQueryAsync()
                End Using

                Await transaction.CommitAsync()

            End Using
        End Using

    End Function

    Private Async Function EnsureFixtureUserAsync(username As String, roleName As String) As Task(Of Integer)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Dim existing = Await UserRepository.FindByUsernameAsync(connection, username)
            If existing IsNot Nothing Then
                Return existing.Id
            End If
        End Using

        Dim migratorFactory As New ConnectionFactory(LoadMigratorOptions())
        Return Await CreateUserCommand.RunAsync(migratorFactory, username, FixturePassword, roleName)

    End Function

    Private Shared Function LoadMigratorOptions() As DatabaseOptions
        Dim migratorConfigPath As String =
            IO.Path.Combine(IO.Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName)
        Return DatabaseOptionsLoader.Load(migratorConfigPath)
    End Function

End Class
