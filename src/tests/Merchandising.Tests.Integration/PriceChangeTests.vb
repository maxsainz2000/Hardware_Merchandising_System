' Merchandising.Tests.Integration.PriceChangeTests
'
' P2-08: Catalog.PriceChangeService against the real, pinned MariaDB instance
' (ADR-006) - proves the Products/PriceHistory/audit atomicity spec section
' 11 requires. Role-gating (Admin succeeds, Cashier refused) is
' AuthorizationMatrixTests.ProductsChangePrice_MatrixMatchesPolicyRegistry's
' job, the same split every other Track B/C endpoint uses - this file proves
' the feature behavior: the write itself, the forced-failure rollback (P1-12
' shape), and PriceHistory's append-only grant.
'
' Fixture product/actor are real, permanent rows, the same shape
' StockDecrementTests uses - Products has no DELETE grant for merch_api
' (0002_post-migration-grants.sql), so nothing here is torn down; each test
' resets the fixture's Price/Cost to a known baseline before asserting.

Imports System.Collections.Generic
Imports System.Threading.Tasks
Imports Merchandising.Api.Catalog
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class PriceChangeTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"

    Private Const FixtureSku As String = "p2_08_fixture_sku"
    Private Const FixtureActorUsername As String = "p2_08_fixture_actor"
    Private Const FixtureActorPassword As String = "P2-08 Fixture Passw0rd!"

    Private Const BaselinePrice As Decimal = 10.0000D
    Private Const BaselineCost As Decimal = 5.0000D

    Private _connectionFactory As ConnectionFactory
    Private _priceChangeService As PriceChangeService
    Private _productId As Integer
    Private _actorUserId As Integer

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        _priceChangeService = New PriceChangeService(_connectionFactory)

        _productId = Await EnsureFixtureProductAsync()
        Await ResetPriceAsync(_productId, BaselinePrice, BaselineCost)
        _actorUserId = Await EnsureFixtureActorAsync()

    End Function

    ''' <summary>Done-when box 2: PriceHistory captures old value, new value, actor, effective timestamp UTC, correlation ID.</summary>
    <TestMethod>
    Public Async Function ChangePriceAsync_PriceOnly_UpdatesProductWritesOnePriceHistoryRowAndAudit() As Task

        Dim correlationId As String = Guid.NewGuid().ToString()
        Dim newPrice As Decimal = 12.5000D

        Dim outcome As PriceChangeOutcome =
            Await _priceChangeService.ChangePriceAsync(_productId, newPrice, Nothing, _actorUserId, correlationId)

        Assert.AreEqual(PriceChangeOutcomeKind.Success, outcome.Kind)
        Assert.AreEqual(newPrice, outcome.Response.Price)
        Assert.AreEqual(BaselineCost, outcome.Response.Cost, "Cost must be unchanged - only Price was supplied.")

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            Dim stored = Await ReadProductPriceCostAsync(connection, _productId)
            Assert.AreEqual(newPrice, stored.Price)
            Assert.AreEqual(BaselineCost, stored.Cost)

            Dim rows = Await ReadPriceHistoryRowsAsync(connection, correlationId)
            Assert.HasCount(1, rows, "Exactly one PriceHistory row for a Price-only change.")

            Dim row = rows(0)
            Assert.AreEqual("Price", row.ChangedField)
            Assert.AreEqual(BaselinePrice, row.OldValue)
            Assert.AreEqual(newPrice, row.NewValue)
            Assert.AreEqual(_actorUserId, row.ActorUserId)
            Assert.AreEqual(correlationId, row.CorrelationId)
            Assert.IsTrue((DateTime.UtcNow - row.EffectiveAtUtc).Duration() < TimeSpan.FromMinutes(1), "EffectiveAtUtc must be a real, current UTC timestamp.")

            Dim auditCount As Long = Await CountAuditRowsAsync(connection, correlationId, "ProductPriceChanged")
            Assert.AreEqual(1L, auditCount, "Exactly one AuditLogs row must be written.")

        End Using

    End Function

    ''' <summary>Both fields changed in one command writes two independent PriceHistory rows, one per field.</summary>
    <TestMethod>
    Public Async Function ChangePriceAsync_PriceAndCost_WritesTwoPriceHistoryRows() As Task

        Dim correlationId As String = Guid.NewGuid().ToString()
        Dim newPrice As Decimal = 15.0000D
        Dim newCost As Decimal = 7.5000D

        Dim outcome As PriceChangeOutcome =
            Await _priceChangeService.ChangePriceAsync(_productId, newPrice, newCost, _actorUserId, correlationId)

        Assert.AreEqual(PriceChangeOutcomeKind.Success, outcome.Kind)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            Dim rows = Await ReadPriceHistoryRowsAsync(connection, correlationId)
            Assert.HasCount(2, rows, "One PriceHistory row per changed field.")
            Assert.IsTrue(rows.Exists(Function(r) r.ChangedField = "Price" AndAlso r.OldValue = BaselinePrice AndAlso r.NewValue = newPrice))
            Assert.IsTrue(rows.Exists(Function(r) r.ChangedField = "Cost" AndAlso r.OldValue = BaselineCost AndAlso r.NewValue = newCost))

        End Using

    End Function

    ''' <summary>A request that would not actually change anything is refused, not silently accepted with an empty audit trail.</summary>
    <TestMethod>
    Public Async Function ChangePriceAsync_SameAsCurrentValue_ReturnsNoChangeAndWritesNoRows() As Task

        Dim correlationId As String = Guid.NewGuid().ToString()

        Dim outcome As PriceChangeOutcome =
            Await _priceChangeService.ChangePriceAsync(_productId, BaselinePrice, BaselineCost, _actorUserId, correlationId)

        Assert.AreEqual(PriceChangeOutcomeKind.NoChange, outcome.Kind)
        Assert.IsNull(outcome.Response)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            Dim stored = Await ReadProductPriceCostAsync(connection, _productId)
            Assert.AreEqual(BaselinePrice, stored.Price)
            Assert.AreEqual(BaselineCost, stored.Cost)

            Dim rows = Await ReadPriceHistoryRowsAsync(connection, correlationId)
            Assert.IsEmpty(rows)

            Dim auditCount As Long = Await CountAuditRowsAsync(connection, correlationId, "ProductPriceChanged")
            Assert.AreEqual(0L, auditCount)

        End Using

    End Function

    ''' <summary>An unknown product Id is a controlled outcome, not an exception.</summary>
    <TestMethod>
    Public Async Function ChangePriceAsync_UnknownProduct_ReturnsProductNotFound() As Task

        Dim outcome As PriceChangeOutcome =
            Await _priceChangeService.ChangePriceAsync(999999999, 1.0000D, Nothing, _actorUserId, Guid.NewGuid().ToString())

        Assert.AreEqual(PriceChangeOutcomeKind.ProductNotFound, outcome.Kind)

    End Function

    ''' <summary>
    ''' Done-when box 1: fault injected after the audit insert, still inside
    ''' the open transaction, immediately before commit - the same shape
    ''' StockDecrementTests.Decrement_FaultInjectedBeforeCommit_
    ''' RollsBackBalanceMovementAndAuditRows uses for P1-12. The unhandled
    ''' exception propagating out of ChangePriceAsync is what proves the
    ''' rollback: Products.Price/Cost, the PriceHistory row, and the audit
    ''' row must all still be exactly where they were before this call.
    ''' </summary>
    <TestMethod>
    Public Async Function ChangePriceAsync_FaultInjectedBeforeCommit_RollsBackProductPriceHistoryAndAuditRows() As Task

        Dim correlationId As String = Guid.NewGuid().ToString()

        Dim priceBefore As Decimal
        Dim historyCountBefore As Long
        Dim auditCountBefore As Long

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            priceBefore = (Await ReadProductPriceCostAsync(connection, _productId)).Price
            historyCountBefore = (Await ReadPriceHistoryRowsAsync(connection, correlationId)).Count
            auditCountBefore = Await CountAuditRowsAsync(connection, correlationId, "ProductPriceChanged")
        End Using

        Assert.AreEqual(BaselinePrice, priceBefore, "Fixture price must start at the known baseline.")
        Assert.AreEqual(0L, historyCountBefore, "A fresh correlation Id must start with no PriceHistory rows.")
        Assert.AreEqual(0L, auditCountBefore, "A fresh correlation Id must start with no AuditLogs rows.")

        Dim faultInjected As Boolean = False

        Await Assert.ThrowsExactlyAsync(Of InvalidOperationException)(
            Function() _priceChangeService.ChangePriceAsync(
                _productId, 20.0000D, Nothing, _actorUserId, correlationId,
                testOnlyFaultAfterAuditInsert:=Sub()
                                                   faultInjected = True
                                                   Throw New InvalidOperationException("P2-08 forced failure: after audit insert, before commit.")
                                               End Sub))

        Assert.IsTrue(faultInjected, "The fault-injection delegate must actually have fired for this proof to mean anything.")

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            Dim priceAfter As Decimal = (Await ReadProductPriceCostAsync(connection, _productId)).Price
            Dim historyCountAfter As Long = (Await ReadPriceHistoryRowsAsync(connection, correlationId)).Count
            Dim auditCountAfter As Long = Await CountAuditRowsAsync(connection, correlationId, "ProductPriceChanged")

            Console.WriteLine($"P2-08 before/after - price: {priceBefore:0.0000} -> {priceAfter:0.0000}; " &
                               $"PriceHistory rows: {historyCountBefore} -> {historyCountAfter}; " &
                               $"audit rows: {auditCountBefore} -> {auditCountAfter}")

            Assert.AreEqual(priceBefore, priceAfter, "A rolled-back price change must leave Products.Price untouched.")
            Assert.AreEqual(0L, historyCountAfter, "A rolled-back price change must leave zero PriceHistory rows.")
            Assert.AreEqual(0L, auditCountAfter, "A rolled-back price change must leave zero AuditLogs rows.")

        End Using

    End Function

    ''' <summary>Done-when box 4: PriceHistory is append-only by privilege, not merely by convention.</summary>
    <TestMethod>
    Public Async Function PriceHistory_UpdateAndDeleteAsMerchApi_BothDenied() As Task

        Dim correlationId As String = Guid.NewGuid().ToString()
        Await _priceChangeService.ChangePriceAsync(_productId, 30.0000D, Nothing, _actorUserId, correlationId)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            Dim rowId As Integer = (Await ReadPriceHistoryRowsAsync(connection, correlationId))(0).Id

            Dim updateException As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"UPDATE PriceHistory SET OldValue = 0 WHERE Id = {rowId};"))
            Assert.AreEqual(1142, updateException.Number, "UPDATE on PriceHistory must be denied ERROR 1142.")

            Dim deleteException As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM PriceHistory WHERE Id = {rowId};"))
            Assert.AreEqual(1142, deleteException.Number, "DELETE on PriceHistory must be denied ERROR 1142.")

        End Using

    End Function

    ''' <summary>Done-when box 5: a later change never rewrites an earlier row - it appends a new one that picks up where the last committed value left off.</summary>
    <TestMethod>
    Public Async Function ChangePriceAsync_TwoSequentialChanges_FirstRowNeverRewritten() As Task

        Dim firstCorrelationId As String = Guid.NewGuid().ToString()
        Dim secondCorrelationId As String = Guid.NewGuid().ToString()

        Await _priceChangeService.ChangePriceAsync(_productId, 40.0000D, Nothing, _actorUserId, firstCorrelationId)
        Await _priceChangeService.ChangePriceAsync(_productId, 45.0000D, Nothing, _actorUserId, secondCorrelationId)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            Dim firstRows = Await ReadPriceHistoryRowsAsync(connection, firstCorrelationId)
            Dim secondRows = Await ReadPriceHistoryRowsAsync(connection, secondCorrelationId)

            Assert.HasCount(1, firstRows)
            Assert.HasCount(1, secondRows)

            Assert.AreEqual(BaselinePrice, firstRows(0).OldValue)
            Assert.AreEqual(40.0000D, firstRows(0).NewValue)

            ' The row from the FIRST change, re-read after the SECOND
            ' change committed, must still show exactly what it showed
            ' right after the first change - proving the second write
            ' appended rather than rewrote it.
            Assert.AreEqual(40.0000D, secondRows(0).OldValue, "The second row's old value must be the first row's new value - a fresh row, not an edit.")
            Assert.AreEqual(45.0000D, secondRows(0).NewValue)

        End Using

    End Function

    ' --------------------------------------------------------------- shared helpers

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
                    "VALUES (@sku, NULL, 'P2-08 Fixture Product', @price, @cost, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                insertCommand.Parameters.AddWithValue("@sku", FixtureSku)
                insertCommand.Parameters.AddWithValue("@price", BaselinePrice)
                insertCommand.Parameters.AddWithValue("@cost", BaselineCost)
                Await insertCommand.ExecuteNonQueryAsync()
                productId = CInt(insertCommand.LastInsertedId)
            End Using

            Return productId

        End Using

    End Function

    Private Async Function ResetPriceAsync(productId As Integer, price As Decimal, cost As Decimal) As Task

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

    Private Shared Async Function ReadProductPriceCostAsync(connection As MySqlConnection, productId As Integer) As Task(Of (Price As Decimal, Cost As Decimal))

        Using command As MySqlCommand = connection.CreateCommand()
            command.CommandText = "SELECT Price, Cost FROM Products WHERE Id = @productId;"
            command.Parameters.AddWithValue("@productId", productId)
            Using reader As MySqlDataReader = Await command.ExecuteReaderAsync()
                Await reader.ReadAsync()
                Return (Price:=reader.GetDecimal(0), Cost:=reader.GetDecimal(1))
            End Using
        End Using

    End Function

    Private Shared Async Function ReadPriceHistoryRowsAsync(connection As MySqlConnection, correlationId As String) As Task(Of List(Of (Id As Integer, ChangedField As String, OldValue As Decimal, NewValue As Decimal, ActorUserId As Integer, EffectiveAtUtc As DateTime, CorrelationId As String)))

        Dim rows As New List(Of (Id As Integer, ChangedField As String, OldValue As Decimal, NewValue As Decimal, ActorUserId As Integer, EffectiveAtUtc As DateTime, CorrelationId As String))

        Using command As MySqlCommand = connection.CreateCommand()
            command.CommandText =
                "SELECT Id, ChangedField, OldValue, NewValue, ActorUserId, EffectiveAtUtc, CorrelationId " &
                "FROM PriceHistory WHERE CorrelationId = @correlationId ORDER BY Id;"
            command.Parameters.AddWithValue("@correlationId", correlationId)

            Using reader As MySqlDataReader = Await command.ExecuteReaderAsync()
                While Await reader.ReadAsync()
                    rows.Add((
                        Id:=reader.GetInt32(0),
                        ChangedField:=reader.GetString(1),
                        OldValue:=reader.GetDecimal(2),
                        NewValue:=reader.GetDecimal(3),
                        ActorUserId:=reader.GetInt32(4),
                        EffectiveAtUtc:=reader.GetDateTime(5),
                        CorrelationId:=reader.GetValue(6).ToString()))
                End While
            End Using
        End Using

        Return rows

    End Function

    Private Shared Async Function CountAuditRowsAsync(connection As MySqlConnection, correlationId As String, action As String) As Task(Of Long)

        Using command As MySqlCommand = connection.CreateCommand()
            command.CommandText = "SELECT COUNT(*) FROM AuditLogs WHERE CorrelationId = @correlationId AND Action = @action;"
            command.Parameters.AddWithValue("@correlationId", correlationId)
            command.Parameters.AddWithValue("@action", action)
            Return CLng(Await command.ExecuteScalarAsync())
        End Using

    End Function

    Private Shared Async Function ExecuteAsync(connection As MySqlConnection, sql As String) As Task
        Using command As MySqlCommand = connection.CreateCommand()
            command.CommandText = sql
            Await command.ExecuteNonQueryAsync()
        End Using
    End Function

    Private Shared Function LoadMigratorOptions() As DatabaseOptions
        Dim migratorConfigPath As String =
            IO.Path.Combine(IO.Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName)
        Return DatabaseOptionsLoader.Load(migratorConfigPath)
    End Function

End Class
