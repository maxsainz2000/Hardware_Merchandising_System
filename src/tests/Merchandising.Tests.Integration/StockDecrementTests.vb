' Merchandising.Tests.Integration.StockDecrementTests
'
' P1-11 evidence: StockService against the real, pinned MariaDB instance
' (ADR-006) - proves the conditional-update / movement / audit atomicity
' spec section 11 requires. HTTP-level status codes (200/400/409) through
' InventoryController are proven separately by a live run captured to
' p1-11-happy-path.txt / p1-11-insufficient-stock.txt / p1-11-decimal-
' scale.txt, the same split P1-08 used (AuthenticationTests here,
' p1-08-auth-matrix.txt for the HTTP shape) - WebApplicationFactory is not
' wired until P1-19.
'
' Fixture product/balance/actor are real, permanent rows, not scratch data
' dropped in TearDown - Products and StockBalances have no DELETE grant for
' merch_api (db/grants/0002_post-migration-grants.sql: "deactivate, do not
' delete"), and StockMovements/AuditLogs referencing them are append-only
' regardless. A fixed "p1_11_fixture_*" product and actor are created once
' and reused; each test resets only the balance quantity it needs before
' asserting, never rows - the same shape AuthenticationTests uses for its
' fixture accounts.

Imports System.IO
Imports System.Threading.Tasks
Imports Merchandising.Api.Inventory
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class StockDecrementTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"

    Private Const FixtureSku As String = "p1_11_fixture_sku"
    Private Const FixtureActorUsername As String = "p1_11_fixture_actor"
    Private Const FixtureActorPassword As String = "P1-11 Fixture Passw0rd!"

    Private Const BaselineQuantity As Decimal = 100.000D

    Private _apiFactory As ConnectionFactory
    Private _stockService As StockService
    Private _productId As Integer
    Private _actorUserId As Integer

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _apiFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        _stockService = New StockService(_apiFactory)

        _productId = Await EnsureFixtureProductAsync()
        Await ResetStockBalanceAsync(_productId, BaselineQuantity)
        _actorUserId = Await EnsureFixtureActorAsync()

    End Function

    ''' <summary>Done-when box 1: success produces exactly one movement, one audit row, correct balance.</summary>
    <TestMethod>
    Public Async Function Decrement_Success_CreatesOneMovementOneAuditRowAndCorrectBalance() As Task

        Dim correlationId As String = Guid.NewGuid().ToString()
        Dim decrementQuantity As Decimal = 12.500D

        Dim outcome As StockDecrementOutcome =
            Await _stockService.DecrementAsync(_productId, decrementQuantity, "P1-11 happy path", _actorUserId, correlationId)

        Assert.AreEqual(StockDecrementOutcomeKind.Success, outcome.Kind)
        Assert.IsNotNull(outcome.Response)
        Assert.AreEqual(BaselineQuantity, outcome.Response.QuantityBefore)
        Assert.AreEqual(BaselineQuantity - decrementQuantity, outcome.Response.QuantityAfter)
        Assert.AreEqual(correlationId, outcome.Response.CorrelationId)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Dim balance As Decimal = Await ReadBalanceAsync(connection, _productId)
            Assert.AreEqual(BaselineQuantity - decrementQuantity, balance, "StockBalances must reflect the decrement.")

            Dim movementCount As Long = Await CountMovementRowsAsync(connection, correlationId)
            Assert.AreEqual(1L, movementCount, "Exactly one StockMovements row must be written.")

            Dim auditCount As Long = Await CountAuditRowsAsync(connection, correlationId, "StockDecremented")
            Assert.AreEqual(1L, auditCount, "Exactly one AuditLogs row must be written.")

        End Using

    End Function

    ''' <summary>Done-when box 4: correlation Id recorded on both the movement and audit rows.</summary>
    <TestMethod>
    Public Async Function Decrement_Success_RecordsCorrelationIdOnMovementAndAudit() As Task

        Dim correlationId As String = Guid.NewGuid().ToString()

        Dim outcome As StockDecrementOutcome =
            Await _stockService.DecrementAsync(_productId, 1.000D, "P1-11 correlation check", _actorUserId, correlationId)

        Assert.AreEqual(StockDecrementOutcomeKind.Success, outcome.Kind)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            ' MySqlConnector reports a CHAR(36) column back as a System.Guid,
            ' not a String - CStr() on a Guid throws InvalidCastException, so
            ' .ToString() is used instead. Guid.ToString()'s default "D"
            ' format is the same lowercase-with-hyphens shape
            ' Guid.NewGuid().ToString() produces, which is what was stored.
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT CorrelationId FROM StockMovements WHERE ProductId = @productId ORDER BY Id DESC LIMIT 1;"
                command.Parameters.AddWithValue("@productId", _productId)
                Dim movementCorrelationId As String = (Await command.ExecuteScalarAsync()).ToString()
                Assert.AreEqual(correlationId, movementCorrelationId)
            End Using

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT CorrelationId FROM AuditLogs WHERE Action = 'StockDecremented' AND CorrelationId = @correlationId;"
                command.Parameters.AddWithValue("@correlationId", correlationId)
                Dim auditCorrelationId As String = (Await command.ExecuteScalarAsync()).ToString()
                Assert.AreEqual(correlationId, auditCorrelationId)
            End Using

        End Using

    End Function

    ''' <summary>Done-when box 2: insufficient stock returns a controlled response and creates zero rows.</summary>
    <TestMethod>
    Public Async Function Decrement_InsufficientStock_ReturnsControlledOutcomeAndCreatesNoRows() As Task

        Dim correlationId As String = Guid.NewGuid().ToString()
        Dim tooMuch As Decimal = BaselineQuantity + 1.000D

        Dim outcome As StockDecrementOutcome =
            Await _stockService.DecrementAsync(_productId, tooMuch, "P1-11 insufficient stock", _actorUserId, correlationId)

        Assert.AreEqual(StockDecrementOutcomeKind.InsufficientStock, outcome.Kind)
        Assert.IsNull(outcome.Response)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Dim balance As Decimal = Await ReadBalanceAsync(connection, _productId)
            Assert.AreEqual(BaselineQuantity, balance, "An insufficient-stock attempt must not change the balance.")

            Dim movementCount As Long = Await CountMovementRowsAsync(connection, correlationId)
            Assert.AreEqual(0L, movementCount, "No StockMovements row may be written on a rejected decrement.")

            Dim auditCount As Long = Await CountAuditRowsAsync(connection, correlationId, "StockDecremented")
            Assert.AreEqual(0L, auditCount, "No AuditLogs row may be written on a rejected decrement.")

        End Using

    End Function

    ''' <summary>ADR-004.1: an over-scale quantity is rejected before any row is written, not silently rounded.</summary>
    <TestMethod>
    Public Async Function Decrement_OverScaleQuantity_RejectedBeforeAnyRowWritten() As Task

        Dim correlationId As String = Guid.NewGuid().ToString()
        Dim overScaleQuantity As Decimal = 1.9999D ' 4 decimal places; StockBalances.Quantity is DECIMAL(19,3)

        Await Assert.ThrowsExactlyAsync(Of ArgumentException)(
            Function() _stockService.DecrementAsync(_productId, overScaleQuantity, "P1-11 decimal scale", _actorUserId, correlationId))

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Dim balance As Decimal = Await ReadBalanceAsync(connection, _productId)
            Assert.AreEqual(BaselineQuantity, balance, "A rejected over-scale request must not touch the balance.")

            Dim movementCount As Long = Await CountMovementRowsAsync(connection, correlationId)
            Assert.AreEqual(0L, movementCount, "No StockMovements row may be written for a rejected over-scale quantity.")

        End Using

    End Function

    Private Async Function EnsureFixtureProductAsync() As Task(Of Integer)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

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
                    "VALUES (@sku, NULL, 'P1-11 Fixture Product', 1.0000, 0.5000, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                insertCommand.Parameters.AddWithValue("@sku", FixtureSku)
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

    Private Async Function ResetStockBalanceAsync(productId As Integer, quantity As Decimal) As Task

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "UPDATE StockBalances SET Quantity = @quantity, UpdatedAtUtc = UTC_TIMESTAMP(6) WHERE ProductId = @productId;"
                command.Parameters.AddWithValue("@quantity", quantity)
                command.Parameters.AddWithValue("@productId", productId)
                Await command.ExecuteNonQueryAsync()
            End Using
        End Using

    End Function

    Private Async Function EnsureFixtureActorAsync() As Task(Of Integer)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()
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
        Return Await CreateUserCommand.RunAsync(migratorFactory, FixtureActorUsername, FixtureActorPassword, "InventoryClerk")

    End Function

    Private Shared Async Function ReadBalanceAsync(connection As MySqlConnection, productId As Integer) As Task(Of Decimal)

        Using command As MySqlCommand = connection.CreateCommand()
            command.CommandText = "SELECT Quantity FROM StockBalances WHERE ProductId = @productId;"
            command.Parameters.AddWithValue("@productId", productId)
            Return CDec(Await command.ExecuteScalarAsync())
        End Using

    End Function

    Private Shared Async Function CountMovementRowsAsync(connection As MySqlConnection, correlationId As String) As Task(Of Long)

        Using command As MySqlCommand = connection.CreateCommand()
            command.CommandText = "SELECT COUNT(*) FROM StockMovements WHERE CorrelationId = @correlationId;"
            command.Parameters.AddWithValue("@correlationId", correlationId)
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
            Path.Combine(Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName)
        Return DatabaseOptionsLoader.Load(migratorConfigPath)
    End Function

End Class
