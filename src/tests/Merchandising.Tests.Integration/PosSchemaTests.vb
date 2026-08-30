' Merchandising.Tests.Integration.PosSchemaTests
'
' P5-02: migration 0011 (CashierSessions, Sales, SaleLines, SalePayments) and
' db/grants/0013, proven against the real pinned MariaDB 10.4.32 instance
' (ADR-000, ADR-002, ADR-009) - never a substitute. Structured the same way
' P3-02's PurchaseOrderSchemaTests, P4-02's ReceivingSchemaTests and P4-03's
' StockCountSchemaTests prove their migrations: every "done when" claim that
' must keep being true on every future run is a test here, not a one-off
' transcript.
'
' THERE IS NO POS API HERE YET. P5-04 builds the first cashier-session
' endpoint and P5-07 the first sale endpoint. Every statement below is raw
' SQL on a raw connection on purpose - the open-session-uniqueness box
' specifically demands a race that bypasses any API check, and at this card
' there is no API to bypass, only a database to trust.
'
' TWO IDENTITIES, DELIBERATELY (ADR-013). Statements that must succeed as the
' application run as merch_api. Fixture setup/teardown and the FK-restriction
' proofs - which have to attempt a DELETE that merch_api is not granted at
' all - run as merch_migrator, the same split every prior schema test file
' uses.
'
' Fixtures are named p5_02_<suffix> with a fresh GUID per run and removed in
' TearDown as merch_migrator, the same per-run isolation the prior schema
' test files use. Nothing here drops or truncates a real table.

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks
Imports Merchandising.Domain.Sales
Imports Merchandising.Infrastructure.Data
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class PosSchemaTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const MigrationId As String = "0011_pos"

    ''' <summary>ERROR 1451: cannot delete a parent row, a foreign key constraint fails.</summary>
    Private Const RowIsReferencedErrorNumber As Integer = 1451

    ''' <summary>ERROR 1062: duplicate entry for a unique key.</summary>
    Private Const DuplicateKeyErrorNumber As Integer = 1062

    ''' <summary>ERROR 1142: the command is denied to this user for this table.</summary>
    Private Const TableAccessDeniedErrorNumber As Integer = 1142

    ''' <summary>ERROR 4025: CONSTRAINT ... failed - MariaDB 10.4's CHECK violation.</summary>
    Private Const CheckConstraintFailedErrorNumber As Integer = 4025

    Private _apiFactory As ConnectionFactory
    Private _migratorFactory As ConnectionFactory
    Private _suffix As String

    Private _productId As Integer
    Private _openedByUserId As Integer
    Private _closedByUserId As Integer
    Private _cashierUserId As Integer

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _apiFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        _migratorFactory = New ConnectionFactory(LoadMigratorOptions())
        _suffix = Guid.NewGuid().ToString("N").Substring(0, 8)

        Await CreateFixturesAsync()

    End Function

    <TestCleanup>
    Public Async Function TearDownAsync() As Task
        Await CleanUpFixturesAsync()
    End Function

    ' =========================================================================
    ' Done-when box 4 - the migration applies, and applies exactly once.
    ' =========================================================================

    ''' <summary>
    ''' The runner recorded 0011 once, with a checksum, on a database that
    ''' already carried 0001-0010. A second row - or a Succeeded = 0 row -
    ''' would mean the runner reapplied or half-applied it.
    ''' </summary>
    <TestMethod>
    Public Async Function Migration0011_IsRecordedExactlyOnce_AfterMigrations0001To0010() As Task

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Dim rows As New List(Of (Id As String, Checksum As String, Succeeded As Boolean))

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT MigrationId, Checksum, Succeeded FROM SchemaMigrations " &
                    "WHERE MigrationId = @migrationId;"
                command.Parameters.AddWithValue("@migrationId", MigrationId)
                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync()
                    While Await reader.ReadAsync()
                        rows.Add((reader.GetString(0), reader.GetString(1), reader.GetBoolean(2)))
                    End While
                End Using
            End Using

            Assert.HasCount(1, rows, $"'{MigrationId}' must be recorded exactly once, not {rows.Count} times.")
            Assert.IsTrue(rows(0).Succeeded, "The recorded application of 0011 must have succeeded.")
            Assert.AreEqual(64, rows(0).Checksum.Length, "A SHA-256 checksum must be recorded for 0011.")

            Dim applied As Integer = Await ScalarIntAsync(
                connection,
                "SELECT COUNT(*) FROM SchemaMigrations WHERE MigrationId IN " &
                "('0001_foundation','0002_authentication','0003_backup','0004_maintenance'," &
                "'0005_identity','0006_product-master','0007_suppliers','0008_purchase-orders'," &
                "'0009_receiving','0010_counts-and-adjustments') AND Succeeded = 1;")

            Assert.AreEqual(10, applied, "0011 must apply on top of 0001-0010, not in place of them.")

        End Using

    End Function

    ' =========================================================================
    ' Done-when box 1 - all four tables created, FKs prevent deletion (1451).
    ' =========================================================================

    ''' <summary>Users is a protected parent of CashierSessions (OpenedByUserId).</summary>
    <TestMethod>
    Public Async Function DeletingAUserReferencedAsSessionOpener_IsRefusedWith1451() As Task

        Await InsertSessionAsMigratorAsync("FK-OPN")

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM Users WHERE Id = {_openedByUserId};"))

            Console.WriteLine($"P5-02 session opener delete -> ERROR {ex.Number}: {ex.Message}")

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A user referenced as a session's OpenedByUserId must not be deletable.")

        End Using

    End Function

    ''' <summary>Users is also a protected parent of CashierSessions (ClosedByUserId).</summary>
    <TestMethod>
    Public Async Function DeletingAUserReferencedAsSessionCloser_IsRefusedWith1451() As Task

        Await InsertClosedSessionAsMigratorAsync("FK-CLS")

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM Users WHERE Id = {_closedByUserId};"))

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A user referenced as a session's ClosedByUserId must not be deletable.")

        End Using

    End Function

    ''' <summary>CashierSessions is a protected parent of Sales.</summary>
    <TestMethod>
    Public Async Function DeletingACashierSessionReferencedBySale_IsRefusedWith1451() As Task

        Dim sessionId As Integer = Await InsertSessionAsMigratorAsync("FK-SES")
        Await InsertSaleAsMigratorAsync(sessionId, "FK-SES")

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM CashierSessions WHERE Id = {sessionId};"))

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A cashier session referenced by a sale must not be deletable.")

        End Using

    End Function

    ''' <summary>Users is a protected parent of Sales (CashierUserId).</summary>
    <TestMethod>
    Public Async Function DeletingAUserReferencedAsSaleCashier_IsRefusedWith1451() As Task

        Dim sessionId As Integer = Await InsertSessionAsMigratorAsync("FK-CSH")
        Await InsertSaleAsMigratorAsync(sessionId, "FK-CSH")

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM Users WHERE Id = {_cashierUserId};"))

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A user referenced as a sale's CashierUserId must not be deletable.")

        End Using

    End Function

    ''' <summary>Sales is a protected parent of SaleLines.</summary>
    <TestMethod>
    Public Async Function DeletingASaleReferencedByALine_IsRefusedWith1451() As Task

        Dim sessionId As Integer = Await InsertSessionAsMigratorAsync("FK-LIN")
        Dim saleId As Integer = Await InsertSaleAsMigratorAsync(sessionId, "FK-LIN")
        Await InsertSaleLineAsMigratorAsync(saleId, _productId, 1D, 10D, 5D, 10D)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM Sales WHERE Id = {saleId};"))

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A sale header with lines must not be deletable.")

        End Using

    End Function

    ''' <summary>Products is a protected parent of SaleLines.</summary>
    <TestMethod>
    Public Async Function DeletingAProductReferencedByASaleLine_IsRefusedWith1451() As Task

        Dim sessionId As Integer = Await InsertSessionAsMigratorAsync("FK-PRD")
        Dim saleId As Integer = Await InsertSaleAsMigratorAsync(sessionId, "FK-PRD")
        Await InsertSaleLineAsMigratorAsync(saleId, _productId, 1D, 10D, 5D, 10D)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM Products WHERE Id = {_productId};"))

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A product referenced by a sale line must not be deletable.")

        End Using

    End Function

    ''' <summary>Sales is also a protected parent of SalePayments.</summary>
    <TestMethod>
    Public Async Function DeletingASaleReferencedByAPayment_IsRefusedWith1451() As Task

        Dim sessionId As Integer = Await InsertSessionAsMigratorAsync("FK-PAY")
        Dim saleId As Integer = Await InsertSaleAsMigratorAsync(sessionId, "FK-PAY")
        Await InsertPaymentAsMigratorAsync(saleId, "Cash", 10D, 10D, 0D)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM Sales WHERE Id = {saleId};"))

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A sale referenced by a payment must not be deletable.")

        End Using

    End Function

    ' =========================================================================
    ' Done-when box 2 - SaleLines carries its own UnitPrice/Cost, not a join.
    ' =========================================================================

    ''' <summary>The columns live directly on SaleLines, asserted from information_schema.</summary>
    <TestMethod>
    Public Async Function SaleLines_CarriesItsOwnUnitPriceAndCostColumns() As Task

        Dim columns As IReadOnlyDictionary(Of String, (DataType As String, IsNullable As Boolean)) =
            Await DescribeColumnsAsync("salelines")

        Assert.IsTrue(columns.ContainsKey("unitprice"), "SaleLines must carry its own UnitPrice column.")
        Assert.IsTrue(columns.ContainsKey("cost"), "SaleLines must carry its own Cost column.")
        Assert.IsFalse(columns("unitprice").IsNullable, "UnitPrice is captured at sale time - never absent.")
        Assert.IsFalse(columns("cost").IsNullable, "Cost is captured at sale time - never absent.")

    End Function

    ''' <summary>
    ''' The defect this box exists to catch: a later price change must not
    ''' retroactively alter a line already written. Proven by writing a line,
    ''' changing Products.Price, and re-reading the line unmoved - not merely
    ''' by inspecting the column list.
    ''' </summary>
    <TestMethod>
    Public Async Function SaleLines_UnitPriceIsUnmoved_AfterTheProductsPriceLaterChanges() As Task

        Dim sessionId As Integer = Await InsertSessionAsMigratorAsync("PRC")
        Dim saleId As Integer = Await InsertSaleAsMigratorAsync(sessionId, "PRC")
        Dim lineId As Integer = Await InsertSaleLineAsMigratorAsync(saleId, _productId, 1D, 12.5D, 7.25D, 12.5D)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()
            Await ExecuteAsync(connection, $"UPDATE Products SET Price = 999.9999 WHERE Id = {_productId};")
        End Using

        Dim storedPrice As Decimal = 0D
        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT UnitPrice FROM SaleLines WHERE Id = @id;"
                command.Parameters.AddWithValue("@id", lineId)
                storedPrice = CDec(Await command.ExecuteScalarAsync())
            End Using
        End Using

        Assert.AreEqual(12.5D, storedPrice,
            "SaleLines.UnitPrice must be unmoved by a later change to Products.Price - it is captured, not joined.")

    End Function

    ' =========================================================================
    ' Done-when box 3 - status/method are stable identifiers the Domain enums
    ' map to, not an ordinal, on all three columns.
    ' =========================================================================

    ''' <summary>ADR-020 section 5: enum NAMES round-trip, never the ordinal.</summary>
    <TestMethod>
    Public Async Function CashierSessionsStatusColumn_AcceptsEveryDomainStatusName() As Task

        Dim names As String() = [Enum].GetNames(GetType(CashierSessionStatus))
        Assert.HasCount(2, names, "P5-02 models two cashier-session statuses.")

        Dim index As Integer = 0
        For Each statusName As String In names
            index += 1
            Dim sessionId As Integer

            If String.Equals(statusName, "Closed", StringComparison.Ordinal) Then
                sessionId = Await InsertClosedSessionAsMigratorAsync($"ST{index}")
            Else
                sessionId = Await InsertSessionAsMigratorAsync($"ST{index}")
            End If

            Dim stored As String = Await ScalarStringAsync($"SELECT Status FROM CashierSessions WHERE Id = {sessionId};")
            Assert.AreEqual(statusName, stored, $"'{statusName}' must round-trip verbatim.")
        Next

    End Function

    ''' <summary>The same proof for Sales.Status against SaleStatus.</summary>
    <TestMethod>
    Public Async Function SalesStatusColumn_AcceptsEveryDomainStatusName() As Task

        Dim names As String() = [Enum].GetNames(GetType(SaleStatus))
        Assert.HasCount(2, names, "P5-02 models two sale statuses.")

        Dim sessionId As Integer = Await InsertSessionAsMigratorAsync("SST")
        Dim index As Integer = 0
        For Each statusName As String In names
            index += 1
            Dim saleId As Integer = Await InsertSaleAsMigratorAsync(sessionId, $"SST{index}", status:=statusName)
            Dim stored As String = Await ScalarStringAsync($"SELECT Status FROM Sales WHERE Id = {saleId};")
            Assert.AreEqual(statusName, stored, $"'{statusName}' must round-trip verbatim.")
        Next

    End Function

    ''' <summary>The same proof for SalePayments.Method against PaymentMethod.</summary>
    <TestMethod>
    Public Async Function SalePaymentsMethodColumn_AcceptsEveryDomainMethodName() As Task

        Dim names As String() = [Enum].GetNames(GetType(PaymentMethod))
        Assert.HasCount(3, names, "P5-02 models three payment methods.")

        Dim sessionId As Integer = Await InsertSessionAsMigratorAsync("PMT")
        Dim saleId As Integer = Await InsertSaleAsMigratorAsync(sessionId, "PMT")

        For Each methodName As String In names
            Dim paymentId As Integer

            If String.Equals(methodName, "Cash", StringComparison.Ordinal) Then
                paymentId = Await InsertPaymentAsMigratorAsync(saleId, methodName, 10D, 10D, 0D)
            Else
                paymentId = Await InsertPaymentAsMigratorAsync(saleId, methodName, 10D, Nothing, Nothing)
            End If

            Dim stored As String = Await ScalarStringAsync($"SELECT Method FROM SalePayments WHERE Id = {paymentId};")
            Assert.AreEqual(methodName, stored, $"'{methodName}' must round-trip verbatim.")
        Next

    End Function

    ''' <summary>An ordinal and a miscased/unknown name are both refused - the utf8mb4_bin-guarded defect, found twice already.</summary>
    <TestMethod>
    Public Async Function CashierSessionsStatusColumn_RefusesAnOrdinalAndAnUnknownName() As Task

        For Each bad As String In New String() {"1", "openn", "open", ""}
            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() InsertSessionAsMigratorAsync("BAD" & Guid.NewGuid().ToString("N").Substring(0, 4), status:=bad))
            Console.WriteLine($"P5-02 session status '{bad}' -> ERROR {ex.Number}")
            Assert.AreEqual(CheckConstraintFailedErrorNumber, ex.Number, $"Status '{bad}' must be refused by CK_CashierSessions_Status.")
        Next

    End Function

    ''' <summary>The same refusal proof on Sales.Status.</summary>
    <TestMethod>
    Public Async Function SalesStatusColumn_RefusesAnOrdinalAndAnUnknownName() As Task

        Dim sessionId As Integer = Await InsertSessionAsMigratorAsync("SBAD")

        For Each bad As String In New String() {"1", "completedd", "completed", ""}
            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() InsertSaleAsMigratorAsync(sessionId, "SBAD" & Guid.NewGuid().ToString("N").Substring(0, 4), status:=bad))
            Console.WriteLine($"P5-02 sale status '{bad}' -> ERROR {ex.Number}")
            Assert.AreEqual(CheckConstraintFailedErrorNumber, ex.Number, $"Status '{bad}' must be refused by CK_Sales_Status.")
        Next

    End Function

    ''' <summary>The same refusal proof on SalePayments.Method.</summary>
    <TestMethod>
    Public Async Function SalePaymentsMethodColumn_RefusesAnOrdinalAndAnUnknownName() As Task

        Dim sessionId As Integer = Await InsertSessionAsMigratorAsync("PBAD")
        Dim saleId As Integer = Await InsertSaleAsMigratorAsync(sessionId, "PBAD")

        For Each bad As String In New String() {"1", "cashh", "cash", ""}
            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() InsertPaymentAsMigratorAsync(saleId, bad, 10D, Nothing, Nothing))
            Console.WriteLine($"P5-02 payment method '{bad}' -> ERROR {ex.Number}")
            Assert.AreEqual(CheckConstraintFailedErrorNumber, ex.Number, $"Method '{bad}' must be refused by CK_SalePayments_Method.")
        Next

    End Function

    ' =========================================================================
    ' SalePayments cash/tendered/change pairing - this migration's own CHECKs.
    ' =========================================================================

    ''' <summary>A Cash row missing Tendered/Change is refused.</summary>
    <TestMethod>
    Public Async Function SalePayments_CashRow_RequiresTenderedAndChange() As Task

        Dim sessionId As Integer = Await InsertSessionAsMigratorAsync("CTC")
        Dim saleId As Integer = Await InsertSaleAsMigratorAsync(sessionId, "CTC")

        Dim ex As MySqlException =
            Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                Function() InsertPaymentAsMigratorAsync(saleId, "Cash", 10D, Nothing, Nothing))

        Assert.AreEqual(CheckConstraintFailedErrorNumber, ex.Number,
            "A Cash payment without TenderedAmount/ChangeAmount must be refused by CK_SalePayments_CashTenderPairing.")

    End Function

    ''' <summary>A Card/EWallet row carrying Tendered/Change is refused.</summary>
    <TestMethod>
    Public Async Function SalePayments_NonCashRow_MustNotCarryTenderedOrChange() As Task

        Dim sessionId As Integer = Await InsertSessionAsMigratorAsync("NCC")
        Dim saleId As Integer = Await InsertSaleAsMigratorAsync(sessionId, "NCC")

        Dim ex As MySqlException =
            Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                Function() InsertPaymentAsMigratorAsync(saleId, "Card", 10D, 10D, 0D))

        Assert.AreEqual(CheckConstraintFailedErrorNumber, ex.Number,
            "A Card payment carrying TenderedAmount/ChangeAmount must be refused by CK_SalePayments_CashTenderPairing.")

    End Function

    ''' <summary>Tendered below Amount is refused - a cash payment cannot fall short and still record change.</summary>
    <TestMethod>
    Public Async Function SalePayments_TenderedBelowAmount_IsRefused() As Task

        Dim sessionId As Integer = Await InsertSessionAsMigratorAsync("TLO")
        Dim saleId As Integer = Await InsertSaleAsMigratorAsync(sessionId, "TLO")

        Dim ex As MySqlException =
            Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                Function() InsertPaymentAsMigratorAsync(saleId, "Cash", 10D, 5D, -5D))

        Assert.AreEqual(CheckConstraintFailedErrorNumber, ex.Number,
            "TenderedAmount below Amount must be refused by CK_SalePayments_TenderedAmount.")

    End Function

    ''' <summary>Change not equal to Tendered - Amount is refused.</summary>
    <TestMethod>
    Public Async Function SalePayments_ChangeNotEqualToTenderedMinusAmount_IsRefused() As Task

        Dim sessionId As Integer = Await InsertSessionAsMigratorAsync("CHW")
        Dim saleId As Integer = Await InsertSaleAsMigratorAsync(sessionId, "CHW")

        Dim ex As MySqlException =
            Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                Function() InsertPaymentAsMigratorAsync(saleId, "Cash", 10D, 20D, 5D))

        Assert.AreEqual(CheckConstraintFailedErrorNumber, ex.Number,
            "ChangeAmount not equal to TenderedAmount - Amount must be refused by CK_SalePayments_ChangeAmount.")

    End Function

    ' =========================================================================
    ' One open session per cashier - the ADR-018 generated-column trick.
    ' =========================================================================

    ''' <summary>
    ''' The P2-07/P3-02/P4-02 shape: two inserts fired without awaiting
    ''' between them, on two separate connections, sharing one OpenedByUserId
    ''' while both are Open. No API check exists to bypass at this card,
    ''' which is the point - uniqueness must be a fact about the server
    ''' before P5-04's endpoint ever relies on it.
    ''' </summary>
    <TestMethod>
    Public Async Function TwoConcurrentOpenSessionInserts_SameCashier_ExactlyOneSucceeds() As Task

        Dim raceOwnerId As Integer = Await CreateStandaloneFixtureUserAsync("raceowner")

        Dim first As Task(Of (Succeeded As Boolean, ErrorNumber As Integer)) = InsertOpenSessionRaceAsync(raceOwnerId)
        Dim second As Task(Of (Succeeded As Boolean, ErrorNumber As Integer)) = InsertOpenSessionRaceAsync(raceOwnerId)

        Dim results = Await Task.WhenAll(first, second)

        Console.WriteLine($"P5-02 concurrent open-session insert -> " &
                          $"{DescribeOutcome(results(0))} | {DescribeOutcome(results(1))}")

        Assert.AreEqual(1, results.Count(Function(r) r.Succeeded),
            "Exactly one of two simultaneous Open-session inserts sharing a cashier must succeed.")
        Assert.AreEqual(1, results.Count(Function(r) Not r.Succeeded AndAlso r.ErrorNumber = DuplicateKeyErrorNumber),
            "The other must be refused ERROR 1062 by the unique index, not by anything else.")

    End Function

    ''' <summary>Sequential proof of the same rule: a second Open session is refused until the first Closes.</summary>
    <TestMethod>
    Public Async Function OpeningASecondSession_SucceedsOnlyAfterTheFirstCloses() As Task

        Dim ownerId As Integer = Await CreateStandaloneFixtureUserAsync("seqowner")

        Dim firstSessionId As Integer = Await InsertOpenSessionForUserAsMigratorAsync(ownerId)

        Dim ex As MySqlException =
            Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                Function() InsertOpenSessionForUserAsMigratorAsync(ownerId))
        Assert.AreEqual(DuplicateKeyErrorNumber, ex.Number,
            "A second Open session for the same cashier must be refused while the first is still Open.")

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()
            Await ExecuteAsync(connection,
                $"UPDATE CashierSessions SET Status = 'Closed', ClosedByUserId = {ownerId}, " &
                $"ClosedAtUtc = UTC_TIMESTAMP(6), RowVersion = RowVersion + 1 WHERE Id = {firstSessionId};")
        End Using

        Dim secondSessionId As Integer = Await InsertOpenSessionForUserAsMigratorAsync(ownerId)
        Assert.AreNotEqual(0, secondSessionId, "Opening a second session must succeed once the first is Closed.")

    End Function

    ' =========================================================================
    ' Decimal round trip, exact, and declared types checked (CLAUDE.md 6.3).
    ' =========================================================================

    ''' <summary>
    ''' 0.001 is the smallest representable quantity at DECIMAL(19,3) and
    ''' 12345678901234.5678 exercises all 18 significant digits of
    ''' DECIMAL(19,4). Compared as Decimal, never as Double - CLAUDE.md
    ''' section 5.
    ''' </summary>
    <TestMethod>
    Public Async Function DecimalColumns_RoundTripBoundaryValuesExactly() As Task

        Const smallestQuantity As Decimal = 0.001D
        Const largestMoney As Decimal = 12345678901234.5678D

        Dim sessionId As Integer = Await InsertSessionAsMigratorAsync("DEC")
        Dim saleId As Integer = Await InsertSaleAsMigratorAsync(sessionId, "DEC", total:=largestMoney)
        Dim lineId As Integer = Await InsertSaleLineAsMigratorAsync(saleId, _productId, smallestQuantity, largestMoney, largestMoney, largestMoney)
        Dim paymentId As Integer = Await InsertPaymentAsMigratorAsync(saleId, "Cash", largestMoney, largestMoney, 0D)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT Quantity, UnitPrice, Cost, LineTotal FROM SaleLines WHERE Id = @id;"
                command.Parameters.AddWithValue("@id", lineId)
                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync()
                    Assert.IsTrue(Await reader.ReadAsync(), "The sale line must be readable.")
                    Assert.AreEqual(smallestQuantity, reader.GetDecimal(0), "0.001 must survive SaleLines.Quantity (DECIMAL(19,3)) exactly.")
                    Assert.AreEqual(largestMoney, reader.GetDecimal(1), "The largest money value must survive SaleLines.UnitPrice exactly.")
                    Assert.AreEqual(largestMoney, reader.GetDecimal(2), "The largest money value must survive SaleLines.Cost exactly.")
                    Assert.AreEqual(largestMoney, reader.GetDecimal(3), "The largest money value must survive SaleLines.LineTotal exactly.")
                End Using
            End Using

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT Amount, TenderedAmount FROM SalePayments WHERE Id = @id;"
                command.Parameters.AddWithValue("@id", paymentId)
                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync()
                    Assert.IsTrue(Await reader.ReadAsync(), "The payment must be readable.")
                    Assert.AreEqual(largestMoney, reader.GetDecimal(0), "The largest money value must survive SalePayments.Amount exactly.")
                    Assert.AreEqual(largestMoney, reader.GetDecimal(1), "The largest money value must survive SalePayments.TenderedAmount exactly.")
                End Using
            End Using

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT Total FROM Sales WHERE Id = @id;"
                command.Parameters.AddWithValue("@id", saleId)
                Assert.AreEqual(largestMoney, CDec(Await command.ExecuteScalarAsync()), "The largest money value must survive Sales.Total exactly.")
            End Using

        End Using

    End Function

    ''' <summary>
    ''' The declared types themselves, from information_schema. A value that
    ''' round-trips proves the value; only the catalogue proves the column
    ''' (CLAUDE.md section 6.3 - scale is silently rounded, so a correct
    ''' stored value on its own proves nothing about precision).
    ''' </summary>
    <TestMethod>
    Public Async Function MoneyAndQuantityColumns_AreDeclaredAtTheRequiredPrecision() As Task

        Await AssertColumnTypeAsync("cashiersessions", "openingfloat", "decimal(19,4)")
        Await AssertColumnTypeAsync("cashiersessions", "declaredcash", "decimal(19,4)")
        Await AssertColumnTypeAsync("cashiersessions", "calculatedcash", "decimal(19,4)")
        Await AssertColumnTypeAsync("cashiersessions", "cashvariance", "decimal(19,4)")
        Await AssertColumnTypeAsync("sales", "total", "decimal(19,4)")
        Await AssertColumnTypeAsync("salelines", "quantity", "decimal(19,3)")
        Await AssertColumnTypeAsync("salelines", "unitprice", "decimal(19,4)")
        Await AssertColumnTypeAsync("salelines", "cost", "decimal(19,4)")
        Await AssertColumnTypeAsync("salelines", "linetotal", "decimal(19,4)")
        Await AssertColumnTypeAsync("salepayments", "amount", "decimal(19,4)")
        Await AssertColumnTypeAsync("salepayments", "tenderedamount", "decimal(19,4)")
        Await AssertColumnTypeAsync("salepayments", "changeamount", "decimal(19,4)")

        For Each timestampColumn As String In New String() {"openedatutc", "createdatutc", "updatedatutc"}
            Await AssertColumnTypeAsync("cashiersessions", timestampColumn, "datetime(6)")
        Next

        For Each timestampColumn As String In New String() {"createdatutc"}
            Await AssertColumnTypeAsync("sales", timestampColumn, "datetime(6)")
        Next

        For Each tableName As String In New String() {"cashiersessions", "sales", "salelines", "salepayments"}
            Dim collation As String = Await ScalarStringAsync(
                "SELECT TABLE_COLLATION FROM information_schema.TABLES " &
                $"WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{tableName}';")
            Assert.AreEqual("utf8mb4_unicode_ci", collation,
                $"{tableName} must state utf8mb4_unicode_ci, not inherit the server's general_ci default.")
        Next

    End Function

    ' =========================================================================
    ' Done-when box 5 - grants 0013, and only what it grants.
    ' =========================================================================

    ''' <summary>merch_api can INSERT and UPDATE cashiersessions, and INSERT only on sales/salelines/salepayments.</summary>
    <TestMethod>
    Public Async Function MerchApi_CanInsertAndUpdateCashierSessions_AndInsertOnlySalesTables() As Task

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Await ExecuteAsync(connection,
                "INSERT INTO CashierSessions (OpenedByUserId, OpeningFloat, Status, OpenedAtUtc, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                $"VALUES ({_openedByUserId}, 500.0000, 'Open', UTC_TIMESTAMP(6), 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));")
            Dim sessionId As Integer = Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

            Await ExecuteAsync(connection,
                $"UPDATE CashierSessions SET Status = 'Closed', ClosedByUserId = {_closedByUserId}, " &
                $"DeclaredCash = 500.0000, CalculatedCash = 500.0000, CashVariance = 0.0000, " &
                $"ClosedAtUtc = UTC_TIMESTAMP(6), RowVersion = RowVersion + 1 WHERE Id = {sessionId};")

            Assert.AreEqual("Closed", Await ScalarStringAsync($"SELECT Status FROM CashierSessions WHERE Id = {sessionId};"))

            Dim openSessionId As Integer = Await InsertSessionAsMigratorAsync("API")

            Await ExecuteAsync(connection,
                "INSERT INTO Sales (CashierSessionId, CashierUserId, Total, Status, CorrelationId, CreatedAtUtc) " &
                $"VALUES ({openSessionId}, {_cashierUserId}, 10.0000, 'Completed', '{Guid.NewGuid()}', UTC_TIMESTAMP(6));")
            Dim saleId As Integer = Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

            Await ExecuteAsync(connection,
                "INSERT INTO SaleLines (SaleId, ProductId, Quantity, UnitPrice, Cost, LineTotal, CreatedAtUtc) " &
                $"VALUES ({saleId}, {_productId}, 1.000, 10.0000, 5.0000, 10.0000, UTC_TIMESTAMP(6));")

            Await ExecuteAsync(connection,
                "INSERT INTO SalePayments (SaleId, Method, Amount, TenderedAmount, ChangeAmount, CreatedAtUtc) " &
                $"VALUES ({saleId}, 'Cash', 10.0000, 10.0000, 0.0000, UTC_TIMESTAMP(6));")

            Assert.AreEqual(1, Await ScalarIntAsync(connection, $"SELECT COUNT(*) FROM SalePayments WHERE SaleId = {saleId};"))

        End Using

    End Function

    ''' <summary>DELETE is denied ERROR 1142 on all four new tables.</summary>
    <TestMethod>
    Public Async Function MerchApi_HasNoDeleteGrant_OnAnyOfTheFourTables() As Task

        Dim sessionId As Integer = Await InsertSessionAsMigratorAsync("NODEL")
        Dim saleId As Integer = Await InsertSaleAsMigratorAsync(sessionId, "NODEL")
        Dim lineId As Integer = Await InsertSaleLineAsMigratorAsync(saleId, _productId, 1D, 10D, 5D, 10D)
        Dim paymentId As Integer = Await InsertPaymentAsMigratorAsync(saleId, "Cash", 10D, 10D, 0D)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Dim paymentDelete As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM SalePayments WHERE Id = {paymentId};"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, paymentDelete.Number, "DELETE on SalePayments must be denied ERROR 1142.")

            Dim lineDelete As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM SaleLines WHERE Id = {lineId};"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, lineDelete.Number, "DELETE on SaleLines must be denied ERROR 1142.")

            Dim saleDelete As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM Sales WHERE Id = {saleId};"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, saleDelete.Number, "DELETE on Sales must be denied ERROR 1142.")

            Dim sessionDelete As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM CashierSessions WHERE Id = {sessionId};"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, sessionDelete.Number, "DELETE on CashierSessions must be denied ERROR 1142.")

        End Using

    End Function

    ''' <summary>merch_api cannot UPDATE Sales, SaleLines or SalePayments - only CashierSessions is mutable.</summary>
    <TestMethod>
    Public Async Function MerchApi_HasNoUpdateGrant_OnTheThreeImmutableTables() As Task

        Dim sessionId As Integer = Await InsertSessionAsMigratorAsync("NOUPD")
        Dim saleId As Integer = Await InsertSaleAsMigratorAsync(sessionId, "NOUPD")
        Dim lineId As Integer = Await InsertSaleLineAsMigratorAsync(saleId, _productId, 1D, 10D, 5D, 10D)
        Dim paymentId As Integer = Await InsertPaymentAsMigratorAsync(saleId, "Cash", 10D, 10D, 0D)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Dim saleUpdate As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"UPDATE Sales SET Total = 0 WHERE Id = {saleId};"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, saleUpdate.Number, "UPDATE on Sales must be denied ERROR 1142.")

            Dim lineUpdate As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"UPDATE SaleLines SET Cost = 0 WHERE Id = {lineId};"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, lineUpdate.Number, "UPDATE on SaleLines must be denied ERROR 1142.")

            Dim paymentUpdate As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"UPDATE SalePayments SET Amount = 0 WHERE Id = {paymentId};"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, paymentUpdate.Number, "UPDATE on SalePayments must be denied ERROR 1142.")

        End Using

    End Function

    ''' <summary>merch_api holds no DDL on the new tables either - ADR-013's split is not weakened by adding a table to it.</summary>
    <TestMethod>
    Public Async Function MerchApi_CannotAlterOrDropTheNewTables() As Task

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Dim alterEx As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, "ALTER TABLE Sales ADD COLUMN Sneaked INT NULL;"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, alterEx.Number, "ALTER must be denied ERROR 1142.")

            Dim dropEx As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, "DROP TABLE SalePayments;"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, dropEx.Number, "DROP must be denied ERROR 1142.")

        End Using

    End Function

    ' =========================================================================
    ' Fixtures and helpers
    ' =========================================================================

    Private Function MakeTag(suffixTag As String) As String
        Return $"P5-02-{_suffix}-{suffixTag}"
    End Function

    Private Shared Function DescribeOutcome(outcome As (Succeeded As Boolean, ErrorNumber As Integer)) As String
        Return If(outcome.Succeeded, "success", $"ERROR {outcome.ErrorNumber}")
    End Function

    Private Shared Function LoadMigratorOptions() As DatabaseOptions
        Dim migratorConfigPath As String =
            Path.Combine(Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName)
        Return DatabaseOptionsLoader.Load(migratorConfigPath)
    End Function

    ''' <summary>
    ''' A fixture user created and cleaned up outside CreateFixturesAsync's
    ''' fixed roster, for a test that needs its own dedicated cashier (the
    ''' open-session race and the sequential open/close proof). Still matched
    ''' by TearDown's 'p5_02_{suffix}_%' pattern.
    ''' </summary>
    Private Async Function CreateStandaloneFixtureUserAsync(role As String) As Task(Of Integer)
        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()
            Return Await CreateFixtureUserAsync(connection, role)
        End Using
    End Function

    ''' <summary>
    ''' Creates the product and three users this run's sessions, sales and
    ''' payments point at. As merch_migrator, so TearDown can remove them
    ''' again - merch_api holds no DELETE on any of the four new tables.
    ''' </summary>
    Private Async Function CreateFixturesAsync() As Task

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Await ExecuteAsync(connection,
                $"INSERT INTO Products (Sku, Name, Price, Cost, ReorderLevel, IsActive, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                $"VALUES ('p5_02_{_suffix}_sku', 'P5-02 Fixture Product', 10.0000, 5.0000, 0.000, 1, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));")
            _productId = Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

            _openedByUserId = Await CreateFixtureUserAsync(connection, "openedby")
            _closedByUserId = Await CreateFixtureUserAsync(connection, "closedby")
            _cashierUserId = Await CreateFixtureUserAsync(connection, "cashier")

        End Using

    End Function

    ''' <summary>
    ''' A row in Users, not an account anyone can log in as - the password
    ''' hash is a placeholder. This card needs a valid foreign key target and
    ''' nothing more.
    ''' </summary>
    Private Async Function CreateFixtureUserAsync(connection As MySqlConnection, role As String) As Task(Of Integer)

        Await ExecuteAsync(connection,
            $"INSERT INTO Users (Username, PasswordHash, IsActive, CreatedAtUtc, UpdatedAtUtc) " &
            $"VALUES ('p5_02_{_suffix}_{role}_{Guid.NewGuid():N}', 'not-a-usable-hash', 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));")

        Return Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

    End Function

    ''' <summary>
    ''' Removes this run's rows, children first. Runs as merch_migrator and
    ''' matches only on this run's GUID suffix, so a concurrent run's rows and
    ''' every real row are untouched. Written defensively: a test that failed
    ''' before the tables existed must still leave the fixtures cleaned up.
    ''' </summary>
    Private Async Function CleanUpFixturesAsync() As Task

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            If Await TableExistsAsync(connection, "salepayments") Then
                Await ExecuteAsync(connection,
                    "DELETE FROM SalePayments WHERE SaleId IN " &
                    "(SELECT Id FROM (SELECT s.Id FROM Sales s " &
                    $"WHERE s.CashierUserId IN (SELECT Id FROM Users WHERE Username LIKE 'p5_02_{_suffix}_%')) AS x);")
            End If

            If Await TableExistsAsync(connection, "salelines") Then
                Await ExecuteAsync(connection,
                    "DELETE FROM SaleLines WHERE SaleId IN " &
                    "(SELECT Id FROM (SELECT s.Id FROM Sales s " &
                    $"WHERE s.CashierUserId IN (SELECT Id FROM Users WHERE Username LIKE 'p5_02_{_suffix}_%')) AS x);")
            End If

            If Await TableExistsAsync(connection, "sales") Then
                Await ExecuteAsync(connection,
                    "DELETE FROM Sales WHERE CashierUserId IN " &
                    $"(SELECT Id FROM (SELECT Id FROM Users WHERE Username LIKE 'p5_02_{_suffix}_%') AS u);")
            End If

            If Await TableExistsAsync(connection, "cashiersessions") Then
                Await ExecuteAsync(connection,
                    "DELETE FROM CashierSessions WHERE OpenedByUserId IN " &
                    $"(SELECT Id FROM (SELECT Id FROM Users WHERE Username LIKE 'p5_02_{_suffix}_%') AS u);")
            End If

            Await ExecuteAsync(connection, $"DELETE FROM Products WHERE Sku      = 'p5_02_{_suffix}_sku';")
            Await ExecuteAsync(connection, $"DELETE FROM Users    WHERE Username LIKE 'p5_02_{_suffix}_%';")

        End Using

    End Function

    Private Async Function InsertSessionAsMigratorAsync(tag As String,
                                                         Optional status As String = "Open") As Task(Of Integer)
        Return Await InsertSessionForUserAsMigratorAsync(_openedByUserId, status)
    End Function

    Private Async Function InsertSessionForUserAsMigratorAsync(openedByUserId As Integer, status As String) As Task(Of Integer)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO CashierSessions (OpenedByUserId, OpeningFloat, Status, OpenedAtUtc, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@openedBy, 500.0000, @status, UTC_TIMESTAMP(6), 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@openedBy", openedByUserId)
                command.Parameters.AddWithValue("@status", status)
                Await command.ExecuteNonQueryAsync()
            End Using

            Return Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

        End Using

    End Function

    Private Async Function InsertOpenSessionForUserAsMigratorAsync(openedByUserId As Integer) As Task(Of Integer)
        Return Await InsertSessionForUserAsMigratorAsync(openedByUserId, "Open")
    End Function

    Private Async Function InsertClosedSessionAsMigratorAsync(tag As String) As Task(Of Integer)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO CashierSessions (OpenedByUserId, ClosedByUserId, OpeningFloat, DeclaredCash, " &
                    "CalculatedCash, CashVariance, Status, OpenedAtUtc, ClosedAtUtc, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@openedBy, @closedBy, 500.0000, 500.0000, 500.0000, 0.0000, 'Closed', " &
                    "UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@openedBy", _openedByUserId)
                command.Parameters.AddWithValue("@closedBy", _closedByUserId)
                Await command.ExecuteNonQueryAsync()
            End Using

            Return Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

        End Using

    End Function

    Private Async Function InsertSaleAsMigratorAsync(sessionId As Integer,
                                                      tag As String,
                                                      Optional status As String = "Completed",
                                                      Optional total As Decimal = 10D) As Task(Of Integer)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO Sales (CashierSessionId, CashierUserId, Total, Status, CorrelationId, CreatedAtUtc) " &
                    "VALUES (@sessionId, @cashierId, @total, @status, @correlationId, UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@sessionId", sessionId)
                command.Parameters.AddWithValue("@cashierId", _cashierUserId)
                command.Parameters.AddWithValue("@total", total)
                command.Parameters.AddWithValue("@status", status)
                command.Parameters.AddWithValue("@correlationId", Guid.NewGuid().ToString())
                Await command.ExecuteNonQueryAsync()
            End Using

            Return Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

        End Using

    End Function

    Private Async Function InsertSaleLineAsMigratorAsync(saleId As Integer,
                                                          productId As Integer,
                                                          quantity As Decimal,
                                                          unitPrice As Decimal,
                                                          cost As Decimal,
                                                          lineTotal As Decimal) As Task(Of Integer)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO SaleLines (SaleId, ProductId, Quantity, UnitPrice, Cost, LineTotal, CreatedAtUtc) " &
                    "VALUES (@saleId, @productId, @quantity, @unitPrice, @cost, @lineTotal, UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@saleId", saleId)
                command.Parameters.AddWithValue("@productId", productId)
                command.Parameters.AddWithValue("@quantity", quantity)
                command.Parameters.AddWithValue("@unitPrice", unitPrice)
                command.Parameters.AddWithValue("@cost", cost)
                command.Parameters.AddWithValue("@lineTotal", lineTotal)
                Await command.ExecuteNonQueryAsync()
            End Using

            Return Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

        End Using

    End Function

    Private Async Function InsertPaymentAsMigratorAsync(saleId As Integer,
                                                         method As String,
                                                         amount As Decimal,
                                                         tenderedAmount As Decimal?,
                                                         changeAmount As Decimal?) As Task(Of Integer)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO SalePayments (SaleId, Method, Amount, TenderedAmount, ChangeAmount, CreatedAtUtc) " &
                    "VALUES (@saleId, @method, @amount, @tendered, @change, UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@saleId", saleId)
                command.Parameters.AddWithValue("@method", method)
                command.Parameters.AddWithValue("@amount", amount)
                command.Parameters.AddWithValue("@tendered", ToDbValue(tenderedAmount))
                command.Parameters.AddWithValue("@change", ToDbValue(changeAmount))
                Await command.ExecuteNonQueryAsync()
            End Using

            Return Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

        End Using

    End Function

    ''' <summary>
    ''' One racer. Opens its own connection and reports the outcome instead of
    ''' throwing, so the caller can fire two and inspect both - a throwing
    ''' racer would fail the test rather than being one of its two results.
    ''' </summary>
    Private Async Function InsertOpenSessionRaceAsync(openedByUserId As Integer) As Task(Of (Succeeded As Boolean, ErrorNumber As Integer))

        Try
            Await InsertSessionForUserAsMigratorAsync(openedByUserId, "Open")
            Return (True, 0)
        Catch ex As MySqlException
            Return (False, ex.Number)
        End Try

    End Function

    ''' <summary>DBNull.Value for an absent Nullable(Of Decimal) - AddWithValue does not do this conversion itself.</summary>
    Private Shared Function ToDbValue(value As Decimal?) As Object
        Return If(value.HasValue, CType(value.Value, Object), DBNull.Value)
    End Function

    Private Shared Async Function ExecuteAsync(connection As MySqlConnection, sql As String) As Task
        Using command As MySqlCommand = connection.CreateCommand()
            command.CommandText = sql
            Await command.ExecuteNonQueryAsync()
        End Using
    End Function

    Private Shared Async Function ScalarIntAsync(connection As MySqlConnection, sql As String) As Task(Of Integer)
        Using command As MySqlCommand = connection.CreateCommand()
            command.CommandText = sql
            Dim value As Object = Await command.ExecuteScalarAsync()
            Return Convert.ToInt32(value, CultureInfo.InvariantCulture)
        End Using
    End Function

    Private Async Function ScalarStringAsync(sql As String) As Task(Of String)
        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = sql
                Dim value As Object = Await command.ExecuteScalarAsync()
                Return If(value Is Nothing OrElse value Is DBNull.Value, Nothing, Convert.ToString(value, CultureInfo.InvariantCulture))
            End Using
        End Using
    End Function

    Private Shared Async Function TableExistsAsync(connection As MySqlConnection, tableName As String) As Task(Of Boolean)
        Using command As MySqlCommand = connection.CreateCommand()
            command.CommandText =
                "SELECT COUNT(*) FROM information_schema.TABLES " &
                "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @tableName;"
            command.Parameters.AddWithValue("@tableName", tableName)
            Return Convert.ToInt32(Await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) > 0
        End Using
    End Function

    Private Async Function DescribeColumnsAsync(tableName As String) _
        As Task(Of IReadOnlyDictionary(Of String, (DataType As String, IsNullable As Boolean)))

        Dim columns As New Dictionary(Of String, (DataType As String, IsNullable As Boolean))(StringComparer.OrdinalIgnoreCase)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT LOWER(COLUMN_NAME), LOWER(COLUMN_TYPE), IS_NULLABLE " &
                    "FROM information_schema.COLUMNS " &
                    "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @tableName;"
                command.Parameters.AddWithValue("@tableName", tableName)
                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync()
                    While Await reader.ReadAsync()
                        columns(reader.GetString(0)) =
                            (reader.GetString(1), String.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase))
                    End While
                End Using
            End Using
        End Using

        Return columns

    End Function

    Private Async Function AssertColumnTypeAsync(tableName As String, columnName As String, expectedType As String) As Task

        Dim columns As IReadOnlyDictionary(Of String, (DataType As String, IsNullable As Boolean)) =
            Await DescribeColumnsAsync(tableName)

        Assert.IsTrue(columns.ContainsKey(columnName), $"{tableName} must carry a {columnName} column.")
        Assert.AreEqual(expectedType, columns(columnName).DataType,
            $"{tableName}.{columnName} must be declared {expectedType}.")

    End Function

End Class
