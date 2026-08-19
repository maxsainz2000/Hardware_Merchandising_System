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

    ' P1-13: a separate fixture product, not the p1_11/p1_12 one above - the
    ' concurrency proof needs a balance of exactly one unit, reset between
    ' rounds, and must not disturb BaselineQuantity for the other tests in
    ' this class.
    Private Const ConcurrencyFixtureSku As String = "p1_13_fixture_sku"

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

    ''' <summary>
    ''' P1-12: fault injected after both the StockMovements and AuditLogs
    ''' inserts, still inside the open transaction, immediately before
    ''' commit. The class header's own comment already documents why the
    ''' unhandled exception is enough - the connection Dispose()s on the way
    ''' out and MariaDB rolls back whatever was still uncommitted. This test
    ''' proves that in practice: before/after counts for balance, movement,
    ''' and audit are recorded and asserted identical.
    ''' </summary>
    <TestMethod>
    Public Async Function Decrement_FaultInjectedBeforeCommit_RollsBackBalanceMovementAndAuditRows() As Task

        Dim correlationId As String = Guid.NewGuid().ToString()
        Dim decrementQuantity As Decimal = 7.000D

        Dim balanceBefore As Decimal
        Dim movementCountBefore As Long
        Dim auditCountBefore As Long

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()
            balanceBefore = Await ReadBalanceAsync(connection, _productId)
            movementCountBefore = Await CountMovementRowsAsync(connection, correlationId)
            auditCountBefore = Await CountAuditRowsAsync(connection, correlationId, "StockDecremented")
        End Using

        Assert.AreEqual(BaselineQuantity, balanceBefore, "Fixture balance must start at the known baseline.")
        Assert.AreEqual(0L, movementCountBefore, "A fresh correlation Id must start with no movement rows.")
        Assert.AreEqual(0L, auditCountBefore, "A fresh correlation Id must start with no audit rows.")

        Dim faultInjected As Boolean = False

        Await Assert.ThrowsExactlyAsync(Of InvalidOperationException)(
            Function() _stockService.DecrementAsync(
                _productId, decrementQuantity, "P1-12 forced-failure rollback proof", _actorUserId, correlationId,
                testOnlyFaultAfterAuditInsert:=Sub()
                                                   faultInjected = True
                                                   Throw New InvalidOperationException("P1-12 forced failure: after audit insert, before commit.")
                                               End Sub))

        Assert.IsTrue(faultInjected, "The fault-injection delegate must actually have fired for this proof to mean anything.")

        Dim balanceAfter As Decimal
        Dim movementCountAfter As Long
        Dim auditCountAfter As Long

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()
            balanceAfter = Await ReadBalanceAsync(connection, _productId)
            movementCountAfter = Await CountMovementRowsAsync(connection, correlationId)
            auditCountAfter = Await CountAuditRowsAsync(connection, correlationId, "StockDecremented")
        End Using

        Console.WriteLine($"P1-12 before/after - balance: {balanceBefore:0.000} -> {balanceAfter:0.000}; " &
                           $"movement rows: {movementCountBefore} -> {movementCountAfter}; " &
                           $"audit rows: {auditCountBefore} -> {auditCountAfter}")

        Assert.AreEqual(balanceBefore, balanceAfter, "A rolled-back decrement must leave the balance untouched.")
        Assert.AreEqual(0L, movementCountAfter, "A rolled-back decrement must leave zero StockMovements rows.")
        Assert.AreEqual(0L, auditCountAfter, "A rolled-back decrement must leave zero AuditLogs rows.")

    End Function

    ''' <summary>
    ''' P1-13/ADR-006: fires N concurrent DecrementAsync calls against a
    ''' product holding exactly one unit of stock, twice - once with N=2,
    ''' once with N=10, resetting the balance to 1.000 between rounds.
    ''' StockRepository.TryDecrementAsync's conditional UPDATE carries the
    ''' sufficiency check inside the same statement as the mutation, so this
    ''' is InnoDB row-locking on that UPDATE doing the serializing, not
    ''' READ COMMITTED (see ADR-006's Reasoning - isolation level alone is
    ''' explicitly not relied upon). Each attempt is caught individually so
    ''' one unexpected exception cannot hide the outcome of the other N-1
    ''' attempts - "raw result distribution recorded" needs all of them.
    ''' </summary>
    <TestMethod>
    Public Async Function Decrement_TwoThenTenSimultaneousRequests_ExactlyOneSucceedsEachRound() As Task

        Dim concurrencyProductId As Integer = Await EnsureConcurrencyFixtureProductAsync()

        Await RunConcurrencyRoundAsync(concurrencyProductId, requestCount:=2)
        Await RunConcurrencyRoundAsync(concurrencyProductId, requestCount:=10)

    End Function

    Private Async Function RunConcurrencyRoundAsync(productId As Integer, requestCount As Integer) As Task

        Await ResetStockBalanceAsync(productId, 1.000D)

        Dim correlationIds(requestCount - 1) As String
        For i = 0 To requestCount - 1
            correlationIds(i) = Guid.NewGuid().ToString()
        Next

        ' None of these are awaited individually, so all requestCount calls
        ' are underway - each on its own connection and transaction, per
        ' ConnectionFactory - before this method awaits any of them.
        Dim tasks(requestCount - 1) As Task(Of (Kind As String, Detail As String))
        For i = 0 To requestCount - 1
            tasks(i) = AttemptDecrementAsync(productId, correlationIds(i), requestCount)
        Next

        Dim results = Await Task.WhenAll(tasks)

        Dim successCount As Integer = 0
        Dim insufficientCount As Integer = 0
        Dim exceptionCount As Integer = 0
        Dim distribution As New System.Text.StringBuilder()

        For i = 0 To requestCount - 1
            Select Case results(i).Kind
                Case NameOf(StockDecrementOutcomeKind.Success)
                    successCount += 1
                Case NameOf(StockDecrementOutcomeKind.InsufficientStock)
                    insufficientCount += 1
                Case Else
                    exceptionCount += 1
            End Select
            If i > 0 Then distribution.Append(" | ")
            distribution.Append($"{results(i).Kind}: {results(i).Detail}")
        Next

        Console.WriteLine($"P1-13 x{requestCount} raw distribution -> {distribution}")
        Console.WriteLine($"P1-13 x{requestCount} summary -> Success:{successCount} InsufficientStock:{insufficientCount} Exception:{exceptionCount}")

        Assert.AreEqual(1, successCount, $"Exactly one of {requestCount} simultaneous requests must succeed.")
        Assert.AreEqual(0, exceptionCount, "No attempt may fail with an unexpected exception - only Success or InsufficientStock are controlled outcomes.")
        Assert.AreEqual(requestCount - 1, insufficientCount, "Every non-winning request must receive the controlled insufficient-stock/concurrency response.")

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Dim balanceAfter As Decimal = Await ReadBalanceAsync(connection, productId)
            Assert.AreEqual(0.000D, balanceAfter, "Final balance must be exactly zero - never negative.")

            Dim movementCount As Long = Await CountMovementRowsForAnyCorrelationIdAsync(connection, correlationIds)
            Assert.AreEqual(1L, movementCount, $"Exactly one StockMovements row may exist across all {requestCount} attempts this round.")

            Dim auditCount As Long = Await CountAuditRowsForAnyCorrelationIdAsync(connection, correlationIds, "StockDecremented")
            Assert.AreEqual(1L, auditCount, $"Exactly one AuditLogs row may exist across all {requestCount} attempts this round.")

        End Using

    End Function

    Private Async Function AttemptDecrementAsync(productId As Integer, correlationId As String, requestCount As Integer) As Task(Of (Kind As String, Detail As String))

        Try
            Dim outcome As StockDecrementOutcome =
                Await _stockService.DecrementAsync(productId, 1.000D, $"P1-13 concurrency proof x{requestCount}", _actorUserId, correlationId)

            Dim detail As String =
                If(outcome.Response IsNot Nothing,
                   $"quantityBefore={outcome.Response.QuantityBefore:0.000}, quantityAfter={outcome.Response.QuantityAfter:0.000}",
                   "no response body")

            Return (Kind:=outcome.Kind.ToString(), Detail:=detail)

        Catch ex As Exception
            Return (Kind:="Exception", Detail:=$"{ex.GetType().Name}: {ex.Message}")
        End Try

    End Function

    Private Async Function EnsureConcurrencyFixtureProductAsync() As Task(Of Integer)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Using selectCommand As MySqlCommand = connection.CreateCommand()
                selectCommand.CommandText = "SELECT Id FROM Products WHERE Sku = @sku;"
                selectCommand.Parameters.AddWithValue("@sku", ConcurrencyFixtureSku)
                Dim existing As Object = Await selectCommand.ExecuteScalarAsync()
                If existing IsNot Nothing Then
                    Return CInt(existing)
                End If
            End Using

            Dim productId As Integer

            Using insertCommand As MySqlCommand = connection.CreateCommand()
                insertCommand.CommandText =
                    "INSERT INTO Products (Sku, Barcode, Name, Price, Cost, IsActive, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@sku, NULL, 'P1-13 Fixture Product', 1.0000, 0.5000, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                insertCommand.Parameters.AddWithValue("@sku", ConcurrencyFixtureSku)
                Await insertCommand.ExecuteNonQueryAsync()
                productId = CInt(insertCommand.LastInsertedId)
            End Using

            Using balanceCommand As MySqlCommand = connection.CreateCommand()
                balanceCommand.CommandText =
                    "INSERT INTO StockBalances (ProductId, Quantity, RowVersion, UpdatedAtUtc) " &
                    "VALUES (@productId, @quantity, 0, UTC_TIMESTAMP(6));"
                balanceCommand.Parameters.AddWithValue("@productId", productId)
                balanceCommand.Parameters.AddWithValue("@quantity", 1.000D)
                Await balanceCommand.ExecuteNonQueryAsync()
            End Using

            Return productId

        End Using

    End Function

    Private Shared Async Function CountMovementRowsForAnyCorrelationIdAsync(connection As MySqlConnection, correlationIds As String()) As Task(Of Long)

        Using command As MySqlCommand = connection.CreateCommand()

            Dim placeholders(correlationIds.Length - 1) As String
            For i = 0 To correlationIds.Length - 1
                Dim paramName As String = $"@correlationId{i}"
                placeholders(i) = paramName
                command.Parameters.AddWithValue(paramName, correlationIds(i))
            Next

            command.CommandText = $"SELECT COUNT(*) FROM StockMovements WHERE CorrelationId IN ({String.Join(",", placeholders)});"
            Return CLng(Await command.ExecuteScalarAsync())

        End Using

    End Function

    Private Shared Async Function CountAuditRowsForAnyCorrelationIdAsync(connection As MySqlConnection, correlationIds As String(), action As String) As Task(Of Long)

        Using command As MySqlCommand = connection.CreateCommand()

            Dim placeholders(correlationIds.Length - 1) As String
            For i = 0 To correlationIds.Length - 1
                Dim paramName As String = $"@correlationId{i}"
                placeholders(i) = paramName
                command.Parameters.AddWithValue(paramName, correlationIds(i))
            Next
            command.Parameters.AddWithValue("@action", action)

            command.CommandText =
                $"SELECT COUNT(*) FROM AuditLogs WHERE Action = @action AND CorrelationId IN ({String.Join(",", placeholders)});"
            Return CLng(Await command.ExecuteScalarAsync())

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
