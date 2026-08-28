' Merchandising.Tests.Integration.LedgerReconciliationTests
'
' P4-01: proves LedgerReconciliation against the real, pinned MariaDB
' instance, and wires it to run automatically after this whole suite via
' AssemblyCleanup - dotnet test fails the run if it ever finds drift, which
' is what "an assertion, not a report someone remembers to read" means in
' practice (plan.md section 7).
'
' Fixture product is real and permanent, the same shape every other
' integration suite here uses (Products has no DELETE grant for merch_api).
' Its StockBalances.Quantity is reset, every test run, to whatever
' StockMovements currently sums to for it - never to a fixed baseline like
' 0.000 - because StockMovements is append-only and a prior interrupted run
' may have left real movement rows behind that can never be un-written.
' Tracking the ledger rather than assuming a baseline is what makes the
' fixture idempotent across runs without ever touching the ledger.
'
' The induced-drift test writes its own unmatched StockMovements row
' directly, as merch_migrator, to prove detection - then heals the drift by
' updating StockBalances to match, in a Finally block so healing runs even
' if an assertion fails first. CLAUDE.md section 5 and section 7 stop
' condition 7 forbid ever UPDATE/DELETE-ing a StockMovements row, and that
' is a project rule on top of merch_migrator's own DB grant (which does
' include UPDATE/DELETE at the schema level - db/grants/0001) - so "removing
' the drift" here always means closing the gap with an ordinary StockBalances
' write, the same kind every real stock-in performs, never editing or
' deleting the movement row that proved detection worked.

Imports System.Collections.Generic
Imports System.Linq
Imports System.Runtime.ExceptionServices
Imports System.Threading.Tasks
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class LedgerReconciliationTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"

    Private Const FixtureSku As String = "p4_01_fixture_sku"
    Private Const FixtureActorUsername As String = "p4_01_fixture_actor"
    Private Const FixtureActorPassword As String = "P4-01 Fixture Passw0rd!"

    Private _connectionFactory As ConnectionFactory
    Private _productId As Integer
    Private _actorUserId As Integer

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        _productId = Await EnsureFixtureProductAsync()
        _actorUserId = Await EnsureFixtureActorAsync()
        Await ReconcileFixtureBalanceToLedgerAsync()

    End Function

    ''' <summary>Done-when box 1: a product with a settled ledger and a balance row is not reported.</summary>
    <TestMethod>
    Public Async Function FindDiscrepanciesAsync_SettledFixture_ReportsNothingForIt() As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            Dim discrepancies As IReadOnlyList(Of LedgerDiscrepancy) = Await LedgerReconciliation.FindDiscrepanciesAsync(connection)

            Assert.IsFalse(discrepancies.Any(Function(d) d.ProductId = _productId),
                            "A product whose ledger sum matches its balance must not be reported.")

        End Using

    End Function

    ''' <summary>Done-when boxes 2-4: the induced drift is named with exact expected/actual/delta, then closed.</summary>
    <TestMethod>
    Public Async Function FindDiscrepanciesAsync_InducedDrift_NamesTheProductThenIsHealed() As Task

        Dim ledgerSumBeforeDrift As Decimal = Await ReconcileFixtureBalanceToLedgerAsync()
        Dim driftDelta As Decimal = 7.500D
        Dim correlationId As String = Guid.NewGuid().ToString()

        Await InsertUnmatchedMovementAsMigratorAsync(driftDelta, ledgerSumBeforeDrift, correlationId)

        ' VB does not allow Await inside a Finally block (BC36943), so the
        ' healing step below cannot live in one. Any assertion failure is
        ' captured instead of thrown immediately, healing always runs next,
        ' and the capture is rethrown afterwards with its original stack -
        ' the same "heal no matter what" guarantee a Finally would give.
        Dim capturedFailure As ExceptionDispatchInfo = Nothing

        Try
            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

                Dim discrepancies As IReadOnlyList(Of LedgerDiscrepancy) = Await LedgerReconciliation.FindDiscrepanciesAsync(connection)
                Dim found As LedgerDiscrepancy = discrepancies.SingleOrDefault(Function(d) d.ProductId = _productId)

                Assert.IsNotNull(found, "A movement with no matching balance change must be reported for the fixture product.")

                Dim reportedExpectedQuantity As Decimal = found.ExpectedQuantity
                Dim reportedActualQuantity As Decimal = found.ActualQuantity
                Dim reportedDelta As Decimal = found.Delta

                Assert.AreEqual(ledgerSumBeforeDrift + driftDelta, reportedExpectedQuantity, "Expected is the ledger sum, including the induced movement.")
                Assert.AreEqual(ledgerSumBeforeDrift, reportedActualQuantity, "Actual is the balance, untouched by the induced movement.")
                Assert.AreEqual(ledgerSumBeforeDrift - (ledgerSumBeforeDrift + driftDelta), reportedDelta, "Delta is Actual minus Expected, exact at DECIMAL(19,3).")

            End Using

        Catch ex As Exception

            capturedFailure = ExceptionDispatchInfo.Capture(ex)

        End Try

        Await ReconcileFixtureBalanceToLedgerAsync()

        capturedFailure?.Throw()

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            Dim afterHealing As IReadOnlyList(Of LedgerDiscrepancy) = Await LedgerReconciliation.FindDiscrepanciesAsync(connection)
            Assert.IsFalse(afterHealing.Any(Function(d) d.ProductId = _productId),
                            "Once the balance is reconciled to the ledger sum, the product must no longer be reported.")

        End Using

    End Function

    ''' <summary>
    ''' P4-01: run once, after every integration test in this assembly has
    ''' finished, so ledger drift fails the test run rather than waiting for
    ''' someone to read a report (plan.md section 7). Every fixture used
    ''' above heals itself before returning control, so a clean suite must
    ''' reach this with zero discrepancies across every product, not only
    ''' this class's own fixture.
    ''' </summary>
    <AssemblyCleanup>
    Public Shared Async Function ReconcileLedgerAfterSuiteAsync() As Task

        Dim connectionFactory As New ConnectionFactory(DatabaseOptionsLoader.Load())

        Using connection As MySqlConnection = Await connectionFactory.CreateOpenConnectionAsync()

            Dim discrepancies As IReadOnlyList(Of LedgerDiscrepancy) = Await LedgerReconciliation.FindDiscrepanciesAsync(connection)

            If discrepancies.Count > 0 Then

                Dim detail As String = String.Join(
                    Environment.NewLine,
                    discrepancies.Select(Function(d) $"  ProductId={d.ProductId} Expected={d.ExpectedQuantity} Actual={d.ActualQuantity} Delta={d.Delta}"))

                Assert.Fail(
                    $"Ledger reconciliation found {discrepancies.Count} product(s) where SUM(StockMovements) <> StockBalances after the integration suite:" &
                    Environment.NewLine & detail)

            End If

        End Using

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
                    "VALUES (@sku, NULL, 'P4-01 Fixture Product', 5.0000, 2.5000, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                insertCommand.Parameters.AddWithValue("@sku", FixtureSku)
                Await insertCommand.ExecuteNonQueryAsync()
                productId = CInt(insertCommand.LastInsertedId)
            End Using

            Return productId

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

    ''' <summary>
    ''' Sets StockBalances.Quantity to the fixture product's current
    ''' StockMovements sum, and returns that sum. Idempotent and safe to
    ''' call at the start of every test regardless of what a prior run left
    ''' behind, because it never assumes a fixed baseline - only that the
    ''' balance must track whatever the append-only ledger already says.
    ''' </summary>
    Private Async Function ReconcileFixtureBalanceToLedgerAsync() As Task(Of Decimal)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            Dim ledgerSum As Decimal

            Using sumCommand As MySqlCommand = connection.CreateCommand()
                sumCommand.CommandText = "SELECT COALESCE(SUM(Delta), 0.000) FROM StockMovements WHERE ProductId = @productId;"
                sumCommand.Parameters.AddWithValue("@productId", _productId)
                ledgerSum = CDec(Await sumCommand.ExecuteScalarAsync())
            End Using

            Using balanceCommand As MySqlCommand = connection.CreateCommand()
                balanceCommand.CommandText =
                    "INSERT INTO StockBalances (ProductId, Quantity, RowVersion, UpdatedAtUtc) " &
                    "VALUES (@productId, @quantity, 0, UTC_TIMESTAMP(6)) " &
                    "ON DUPLICATE KEY UPDATE Quantity = VALUES(Quantity), UpdatedAtUtc = VALUES(UpdatedAtUtc);"
                balanceCommand.Parameters.AddWithValue("@productId", _productId)
                balanceCommand.Parameters.AddWithValue("@quantity", ledgerSum)
                Await balanceCommand.ExecuteNonQueryAsync()
            End Using

            Return ledgerSum

        End Using

    End Function

    ''' <summary>
    ''' Writes one StockMovements row for the fixture product, as
    ''' merch_migrator, with no matching StockBalances change - the exact
    ''' shape of drift this card must detect. QuantityBefore/After record
    ''' the ledger-implied trajectory (ledgerSumBeforeDrift, +driftDelta),
    ''' not a real StockBalances read, because desyncing the two is the
    ''' point of this row.
    ''' </summary>
    Private Async Function InsertUnmatchedMovementAsMigratorAsync(driftDelta As Decimal, ledgerSumBeforeDrift As Decimal, correlationId As String) As Task

        Dim migratorFactory As New ConnectionFactory(LoadMigratorOptions())

        Using connection As MySqlConnection = Await migratorFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO StockMovements (ProductId, Delta, QuantityBefore, QuantityAfter, Reason, ActorUserId, CorrelationId, CreatedAtUtc) " &
                    "VALUES (@productId, @delta, @before, @after, 'P4-01 induced drift (LedgerReconciliationTests)', @actorUserId, @correlationId, UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@productId", _productId)
                command.Parameters.AddWithValue("@delta", driftDelta)
                command.Parameters.AddWithValue("@before", ledgerSumBeforeDrift)
                command.Parameters.AddWithValue("@after", ledgerSumBeforeDrift + driftDelta)
                command.Parameters.AddWithValue("@actorUserId", _actorUserId)
                command.Parameters.AddWithValue("@correlationId", correlationId)
                Await command.ExecuteNonQueryAsync()
            End Using
        End Using

    End Function

    Private Shared Function LoadMigratorOptions() As DatabaseOptions
        Dim migratorConfigPath As String =
            IO.Path.Combine(IO.Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName)
        Return DatabaseOptionsLoader.Load(migratorConfigPath)
    End Function

End Class
