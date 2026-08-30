' Merchandising.Tests.Integration.SalesReturnSchemaTests
'
' P5-03: migration 0012 (SalesReturns, SalesReturnLines) and db/grants/0014,
' proven against the real pinned MariaDB 10.4.32 instance (ADR-000, ADR-002,
' ADR-009) - never a substitute. Structured the same way P4-02's
' ReceivingSchemaTests, P4-03's StockCountSchemaTests and P5-02's
' PosSchemaTests prove their migrations: every "done when" claim that must
' keep being true on every future run is a test here, not a one-off
' transcript.
'
' THERE IS NO SALES-RETURN API HERE YET. P5-11 builds the first endpoint.
' Every statement below is raw SQL on a raw connection on purpose - there is
' no API to bypass at this card, only a database to trust.
'
' TWO IDENTITIES, DELIBERATELY (ADR-013). Statements that must succeed as the
' application run as merch_api. Fixture setup/teardown and the FK-restriction
' proofs - which have to attempt a DELETE that merch_api is not granted at
' all - run as merch_migrator, the same split every prior schema test file
' uses.
'
' Fixtures are named p5_03_<suffix> with a fresh GUID per run and removed in
' TearDown as merch_migrator, the same per-run isolation the prior schema
' test files use. Nothing here drops or truncates a real table. Every
' SalesReturns row needs a real Sale/SaleLine to point at (0012's FK to
' 0011's tables), so fixture setup builds a small completed sale first.

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
Public Class SalesReturnSchemaTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const MigrationId As String = "0012_sales-returns"

    ''' <summary>ERROR 1451: cannot delete a parent row, a foreign key constraint fails.</summary>
    Private Const RowIsReferencedErrorNumber As Integer = 1451

    ''' <summary>ERROR 1142: the command is denied to this user for this table.</summary>
    Private Const TableAccessDeniedErrorNumber As Integer = 1142

    ''' <summary>ERROR 4025: CONSTRAINT ... failed - MariaDB 10.4's CHECK violation.</summary>
    Private Const CheckConstraintFailedErrorNumber As Integer = 4025

    Private _apiFactory As ConnectionFactory
    Private _migratorFactory As ConnectionFactory
    Private _suffix As String

    Private _productId As Integer
    Private _cashierUserId As Integer
    Private _returnedByUserId As Integer
    Private _approvedByUserId As Integer

    Private _sessionId As Integer
    Private _saleId As Integer
    Private _saleLineId As Integer

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
    ''' The runner recorded 0012 once, with a checksum, on a database that
    ''' already carried 0001-0011. A second row - or a Succeeded = 0 row -
    ''' would mean the runner reapplied or half-applied it.
    ''' </summary>
    <TestMethod>
    Public Async Function Migration0012_IsRecordedExactlyOnce_AfterMigrations0001To0011() As Task

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
            Assert.IsTrue(rows(0).Succeeded, "The recorded application of 0012 must have succeeded.")
            Assert.AreEqual(64, rows(0).Checksum.Length, "A SHA-256 checksum must be recorded for 0012.")

            Dim applied As Integer = Await ScalarIntAsync(
                connection,
                "SELECT COUNT(*) FROM SchemaMigrations WHERE MigrationId IN " &
                "('0001_foundation','0002_authentication','0003_backup','0004_maintenance'," &
                "'0005_identity','0006_product-master','0007_suppliers','0008_purchase-orders'," &
                "'0009_receiving','0010_counts-and-adjustments','0011_pos') AND Succeeded = 1;")

            Assert.AreEqual(11, applied, "0012 must apply on top of 0001-0011, not in place of them.")

        End Using

    End Function

    ' =========================================================================
    ' Done-when box 3 (card) - FKs prevent deletion, proven by 1451.
    ' =========================================================================

    ''' <summary>
    ''' Sales is a protected parent of SalesReturns, structurally - checked
    ''' against information_schema rather than by DELETE. A return can never
    ''' exist without a sale line on that sale (SalesReturnLines.SaleLineId
    ''' requires one), so FK_SaleLines_Sales (0011) always refuses a DELETE
    ''' first regardless of whether FK_SalesReturns_Sales exists at all - the
    ''' behavioural proof every other FK test in this file uses cannot
    ''' isolate this one, so this test checks the constraint directly instead.
    ''' </summary>
    <TestMethod>
    Public Async Function SalesReturns_SaleIdIsAForeignKeyToSales() As Task

        Await InsertReturnAsMigratorAsync("FK-SAL")

        Dim fks As IReadOnlyList(Of (Column As String, RefTable As String)) =
            Await DescribeForeignKeysAsync("salesreturns")

        Assert.AreEqual(1, fks.Where(Function(f) f.Column = "saleid" AndAlso f.RefTable = "sales").Count(),
            "SaleId must be a foreign key to Sales.")

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM Sales WHERE Id = {_saleId};"))

            Console.WriteLine($"P5-03 sale delete (return and sale line both exist) -> ERROR {ex.Number}: {ex.Message}")

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A sale referenced by a sales return (and its sale line) must not be deletable - " &
                "behaviourally true even though FK_SaleLines_Sales is what actually fires first.")

        End Using

    End Function

    ''' <summary>Users is a protected parent of SalesReturns (ReturnedByUserId).</summary>
    <TestMethod>
    Public Async Function DeletingAUserReferencedAsReturnedBy_IsRefusedWith1451() As Task

        Await InsertReturnAsMigratorAsync("FK-RTB")

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM Users WHERE Id = {_returnedByUserId};"))

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A user referenced as a return's ReturnedByUserId must not be deletable.")

        End Using

    End Function

    ''' <summary>Users is also a protected parent of SalesReturns (ApprovedByUserId).</summary>
    <TestMethod>
    Public Async Function DeletingAUserReferencedAsApprovedBy_IsRefusedWith1451() As Task

        Await InsertReturnAsMigratorAsync("FK-APB", status:="Completed", exceedsThreshold:=True,
                                          approvedByUserId:=_approvedByUserId, refundMethod:="Cash", refundAmount:=5D)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM Users WHERE Id = {_approvedByUserId};"))

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A user referenced as a return's ApprovedByUserId must not be deletable.")

        End Using

    End Function

    ''' <summary>SalesReturns is a protected parent of SalesReturnLines.</summary>
    <TestMethod>
    Public Async Function DeletingASalesReturnReferencedByALine_IsRefusedWith1451() As Task

        Dim returnId As Integer = Await InsertReturnAsMigratorAsync("FK-LIN")
        Await InsertReturnLineAsMigratorAsync(returnId, _saleLineId, _productId, 1D, True)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM SalesReturns WHERE Id = {returnId};"))

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A sales-return header with lines must not be deletable.")

        End Using

    End Function

    ''' <summary>Card's own box: SaleLines is a protected parent of SalesReturnLines.</summary>
    <TestMethod>
    Public Async Function DeletingASaleLineReferencedByAReturnLine_IsRefusedWith1451() As Task

        Dim returnId As Integer = Await InsertReturnAsMigratorAsync("FK-SLN")
        Await InsertReturnLineAsMigratorAsync(returnId, _saleLineId, _productId, 1D, True)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM SaleLines WHERE Id = {_saleLineId};"))

            Console.WriteLine($"P5-03 sale line delete (return line exists) -> ERROR {ex.Number}: {ex.Message}")

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A sale line referenced by a return line must not be deletable.")

        End Using

    End Function

    ''' <summary>Products is a protected parent of SalesReturnLines.</summary>
    <TestMethod>
    Public Async Function DeletingAProductReferencedByAReturnLine_IsRefusedWith1451() As Task

        Dim returnId As Integer = Await InsertReturnAsMigratorAsync("FK-PRD")
        Await InsertReturnLineAsMigratorAsync(returnId, _saleLineId, _productId, 1D, True)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM Products WHERE Id = {_productId};"))

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A product referenced by a return line must not be deletable.")

        End Using

    End Function

    ' =========================================================================
    ' Done-when box 1 (card) - the stock-eligibility flag is a real column.
    ' =========================================================================

    ''' <summary>RestocksItem exists, is not nullable, and both values are stored distinctly - never inferred or collapsed.</summary>
    <TestMethod>
    Public Async Function RestocksItem_IsARealNonNullableColumn_AndBothValuesRoundTrip() As Task

        Dim columns As IReadOnlyDictionary(Of String, (DataType As String, IsNullable As Boolean)) =
            Await DescribeColumnsAsync("salesreturnlines")

        Assert.IsTrue(columns.ContainsKey("restocksitem"), "SalesReturnLines must carry a RestocksItem column.")
        Assert.IsFalse(columns("restocksitem").IsNullable, "RestocksItem is never inferred - always recorded.")

        Dim returnId As Integer = Await InsertReturnAsMigratorAsync("RST")
        Dim eligibleLineId As Integer = Await InsertReturnLineAsMigratorAsync(returnId, _saleLineId, _productId, 1D, True)
        Dim damagedLineId As Integer = Await InsertReturnLineAsMigratorAsync(returnId, _saleLineId, _productId, 1D, False)

        Dim eligible As Integer = Await ScalarIntViaApiAsync($"SELECT RestocksItem FROM SalesReturnLines WHERE Id = {eligibleLineId};")
        Dim damaged As Integer = Await ScalarIntViaApiAsync($"SELECT RestocksItem FROM SalesReturnLines WHERE Id = {damagedLineId};")

        Assert.AreEqual(1, eligible, "RestocksItem = True must round-trip as 1, not be defaulted away.")
        Assert.AreEqual(0, damaged, "RestocksItem = False must round-trip as 0, not be defaulted away.")

    End Function

    ' =========================================================================
    ' Done-when box 2 (card) - ReturnedBy/ApprovedBy are separate columns,
    ' approver nullable.
    ' =========================================================================

    ''' <summary>
    ''' ADR-017 section 6's self-approval veto compares these two columns. It
    ''' cannot if they are one field. Asserted against information_schema
    ''' rather than by reading the migration.
    ''' </summary>
    <TestMethod>
    Public Async Function ReturnedByAndApprovedBy_AreTwoDistinctUserColumns() As Task

        Dim columns As IReadOnlyDictionary(Of String, (DataType As String, IsNullable As Boolean)) =
            Await DescribeColumnsAsync("salesreturns")

        Assert.IsTrue(columns.ContainsKey("returnedbyuserid"), "SalesReturns must carry ReturnedByUserId.")
        Assert.IsTrue(columns.ContainsKey("approvedbyuserid"), "SalesReturns must carry ApprovedByUserId.")

        Assert.IsFalse(columns("returnedbyuserid").IsNullable,
            "Every return has a returner from the moment it exists.")
        Assert.IsTrue(columns("approvedbyuserid").IsNullable,
            "A within-scope or PendingApproval return has no approver yet - ApprovedByUserId must be nullable.")

        Dim fks As IReadOnlyList(Of (Column As String, RefTable As String)) =
            Await DescribeForeignKeysAsync("salesreturns")

        Assert.AreEqual(1, fks.Where(Function(f) f.Column = "returnedbyuserid" AndAlso f.RefTable = "users").Count(),
            "ReturnedByUserId must be a foreign key to Users.")
        Assert.AreEqual(1, fks.Where(Function(f) f.Column = "approvedbyuserid" AndAlso f.RefTable = "users").Count(),
            "ApprovedByUserId must be a separate foreign key to Users.")

    End Function

    ' =========================================================================
    ' Status is a stable identifier the Domain enum maps to, not an ordinal.
    ' =========================================================================

    ''' <summary>ADR-020 section 5: enum NAMES round-trip, never the ordinal.</summary>
    <TestMethod>
    Public Async Function StatusColumn_AcceptsEveryDomainStatusName() As Task

        Dim names As String() = [Enum].GetNames(GetType(SalesReturnStatus))
        Assert.HasCount(3, names, "P5-03 models three sales-return statuses.")

        Dim index As Integer = 0
        For Each statusName As String In names
            index += 1
            Dim returnId As Integer

            If String.Equals(statusName, "Completed", StringComparison.Ordinal) Then
                returnId = Await InsertReturnAsMigratorAsync($"ST{index}", status:=statusName, exceedsThreshold:=True,
                                                              approvedByUserId:=_approvedByUserId, refundMethod:="Cash", refundAmount:=5D)
            ElseIf String.Equals(statusName, "Rejected", StringComparison.Ordinal) Then
                returnId = Await InsertReturnAsMigratorAsync($"ST{index}", status:=statusName, exceedsThreshold:=True,
                                                              approvedByUserId:=_approvedByUserId)
            Else
                returnId = Await InsertReturnAsMigratorAsync($"ST{index}", status:=statusName)
            End If

            Dim stored As String = Await ScalarStringAsync($"SELECT Status FROM SalesReturns WHERE Id = {returnId};")
            Assert.AreEqual(statusName, stored, $"'{statusName}' must round-trip verbatim.")
        Next

    End Function

    ''' <summary>An ordinal and a miscased/unknown name are both refused - the utf8mb4_bin-guarded defect, found repeatedly already.</summary>
    <TestMethod>
    Public Async Function StatusColumn_RefusesAnOrdinalAndAnUnknownName() As Task

        For Each bad As String In New String() {"1", "completedd", "completed", ""}
            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() InsertReturnAsMigratorAsync("BAD" & Guid.NewGuid().ToString("N").Substring(0, 4), status:=bad))
            Console.WriteLine($"P5-03 return status '{bad}' -> ERROR {ex.Number}")
            Assert.AreEqual(CheckConstraintFailedErrorNumber, ex.Number, $"Status '{bad}' must be refused by CK_SalesReturns_Status.")
        Next

    End Function

    ' =========================================================================
    ' RefundMethod/RefundAmount pairing - this migration's own CHECK.
    ' =========================================================================

    ''' <summary>A RefundMethod without a RefundAmount is refused.</summary>
    <TestMethod>
    Public Async Function RefundMethodWithoutRefundAmount_IsRefused() As Task

        Dim ex As MySqlException =
            Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                Function() InsertReturnAsMigratorAsync("RF1", status:="Completed", refundMethod:="Cash", refundAmount:=Nothing))

        Assert.AreEqual(CheckConstraintFailedErrorNumber, ex.Number,
            "RefundMethod without RefundAmount must be refused by CK_SalesReturns_RefundPairing.")

    End Function

    ''' <summary>A RefundAmount without a RefundMethod is refused.</summary>
    <TestMethod>
    Public Async Function RefundAmountWithoutRefundMethod_IsRefused() As Task

        Dim ex As MySqlException =
            Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                Function() InsertReturnAsMigratorAsync("RF2", status:="Completed", refundMethod:=Nothing, refundAmount:=5D))

        Assert.AreEqual(CheckConstraintFailedErrorNumber, ex.Number,
            "RefundAmount without RefundMethod must be refused by CK_SalesReturns_RefundPairing.")

    End Function

    ''' <summary>An unknown RefundMethod name is refused.</summary>
    <TestMethod>
    Public Async Function UnknownRefundMethod_IsRefused() As Task

        Dim ex As MySqlException =
            Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                Function() InsertReturnAsMigratorAsync("RF3", status:="Completed", refundMethod:="BankTransfer", refundAmount:=5D))

        Assert.AreEqual(CheckConstraintFailedErrorNumber, ex.Number,
            "An unknown RefundMethod must be refused by CK_SalesReturns_RefundMethod.")

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

        Dim returnId As Integer = Await InsertReturnAsMigratorAsync(
            "DEC", status:="Completed", exceedsThreshold:=True, approvedByUserId:=_approvedByUserId,
            refundMethod:="Cash", refundAmount:=largestMoney)
        Dim lineId As Integer = Await InsertReturnLineAsMigratorAsync(returnId, _saleLineId, _productId, smallestQuantity, True)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT QuantityReturned FROM SalesReturnLines WHERE Id = @id;"
                command.Parameters.AddWithValue("@id", lineId)
                Assert.AreEqual(smallestQuantity, CDec(Await command.ExecuteScalarAsync()),
                    "0.001 must survive SalesReturnLines.QuantityReturned (DECIMAL(19,3)) exactly.")
            End Using

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT RefundAmount FROM SalesReturns WHERE Id = @id;"
                command.Parameters.AddWithValue("@id", returnId)
                Assert.AreEqual(largestMoney, CDec(Await command.ExecuteScalarAsync()),
                    "The largest money value must survive SalesReturns.RefundAmount exactly.")
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

        Await AssertColumnTypeAsync("salesreturns", "refundamount", "decimal(19,4)")
        Await AssertColumnTypeAsync("salesreturnlines", "quantityreturned", "decimal(19,3)")

        For Each timestampColumn As String In New String() {"returnedatutc", "createdatutc", "updatedatutc"}
            Await AssertColumnTypeAsync("salesreturns", timestampColumn, "datetime(6)")
        Next

        For Each tableName As String In New String() {"salesreturns", "salesreturnlines"}
            Dim collation As String = Await ScalarStringAsync(
                "SELECT TABLE_COLLATION FROM information_schema.TABLES " &
                $"WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{tableName}';")
            Assert.AreEqual("utf8mb4_unicode_ci", collation,
                $"{tableName} must state utf8mb4_unicode_ci, not inherit the server's general_ci default.")
        Next

    End Function

    ' =========================================================================
    ' Done-when box 5 (card) - grants 0014, and only what it grants.
    ' =========================================================================

    ''' <summary>merch_api can INSERT and UPDATE salesreturns; INSERT only on salesreturnlines.</summary>
    <TestMethod>
    Public Async Function MerchApi_CanInsertAndUpdateHeader_AndInsertOnlyLines() As Task

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Await ExecuteAsync(connection,
                "INSERT INTO SalesReturns (SaleId, ReturnedByUserId, Reason, ExceedsThreshold, Status, " &
                "ReturnedAtUtc, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                $"VALUES ({_saleId}, {_returnedByUserId}, 'API test', 1, 'PendingApproval', " &
                "UTC_TIMESTAMP(6), 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));")
            Dim returnId As Integer = Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

            Await ExecuteAsync(connection,
                "INSERT INTO SalesReturnLines (SalesReturnId, SaleLineId, ProductId, QuantityReturned, RestocksItem, CreatedAtUtc) " &
                $"VALUES ({returnId}, {_saleLineId}, {_productId}, 1.000, 1, UTC_TIMESTAMP(6));")

            Await ExecuteAsync(connection,
                $"UPDATE SalesReturns SET Status = 'Completed', ApprovedByUserId = {_approvedByUserId}, " &
                "RefundMethod = 'Cash', RefundAmount = 10.0000, ApprovedAtUtc = UTC_TIMESTAMP(6), " &
                $"RowVersion = RowVersion + 1 WHERE Id = {returnId};")

            Assert.AreEqual("Completed", Await ScalarStringAsync($"SELECT Status FROM SalesReturns WHERE Id = {returnId};"))

        End Using

    End Function

    ''' <summary>DELETE is denied ERROR 1142 on both new tables.</summary>
    <TestMethod>
    Public Async Function MerchApi_HasNoDeleteGrant_OnEitherTable() As Task

        Dim returnId As Integer = Await InsertReturnAsMigratorAsync("NODEL")
        Dim lineId As Integer = Await InsertReturnLineAsMigratorAsync(returnId, _saleLineId, _productId, 1D, True)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Dim lineDelete As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM SalesReturnLines WHERE Id = {lineId};"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, lineDelete.Number, "DELETE on SalesReturnLines must be denied ERROR 1142.")

            Dim returnDelete As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM SalesReturns WHERE Id = {returnId};"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, returnDelete.Number, "DELETE on SalesReturns must be denied ERROR 1142.")

        End Using

    End Function

    ''' <summary>merch_api cannot UPDATE SalesReturnLines - only the header is mutable.</summary>
    <TestMethod>
    Public Async Function MerchApi_HasNoUpdateGrant_OnSalesReturnLines() As Task

        Dim returnId As Integer = Await InsertReturnAsMigratorAsync("NOUPD")
        Dim lineId As Integer = Await InsertReturnLineAsMigratorAsync(returnId, _saleLineId, _productId, 1D, True)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Dim lineUpdate As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"UPDATE SalesReturnLines SET QuantityReturned = 99 WHERE Id = {lineId};"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, lineUpdate.Number, "UPDATE on SalesReturnLines must be denied ERROR 1142.")

        End Using

    End Function

    ''' <summary>merch_api holds no DDL on the new tables either - ADR-013's split is not weakened by adding two tables to it.</summary>
    <TestMethod>
    Public Async Function MerchApi_CannotAlterOrDropTheNewTables() As Task

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Dim alterEx As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, "ALTER TABLE SalesReturns ADD COLUMN Sneaked INT NULL;"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, alterEx.Number, "ALTER must be denied ERROR 1142.")

            Dim dropEx As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, "DROP TABLE SalesReturnLines;"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, dropEx.Number, "DROP must be denied ERROR 1142.")

        End Using

    End Function

    ' =========================================================================
    ' Fixtures and helpers
    ' =========================================================================

    Private Function MakeTag(suffixTag As String) As String
        Return $"P5-03-{_suffix}-{suffixTag}"
    End Function

    Private Shared Function LoadMigratorOptions() As DatabaseOptions
        Dim migratorConfigPath As String =
            Path.Combine(Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName)
        Return DatabaseOptionsLoader.Load(migratorConfigPath)
    End Function

    ''' <summary>
    ''' Creates the product, three users, and a small completed sale (one
    ''' session, one sale, one sale line) this run's returns point at. As
    ''' merch_migrator, so TearDown can remove it all again - merch_api holds
    ''' no DELETE on any of these tables.
    ''' </summary>
    Private Async Function CreateFixturesAsync() As Task

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Await ExecuteAsync(connection,
                $"INSERT INTO Products (Sku, Name, Price, Cost, ReorderLevel, IsActive, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                $"VALUES ('p5_03_{_suffix}_sku', 'P5-03 Fixture Product', 10.0000, 5.0000, 0.000, 1, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));")
            _productId = Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

            _cashierUserId = Await CreateFixtureUserAsync(connection, "cashier")
            _returnedByUserId = Await CreateFixtureUserAsync(connection, "returnedby")
            _approvedByUserId = Await CreateFixtureUserAsync(connection, "approvedby")

            Await ExecuteAsync(connection,
                $"INSERT INTO CashierSessions (OpenedByUserId, OpeningFloat, Status, OpenedAtUtc, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                $"VALUES ({_cashierUserId}, 500.0000, 'Open', UTC_TIMESTAMP(6), 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));")
            _sessionId = Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

            Await ExecuteAsync(connection,
                "INSERT INTO Sales (CashierSessionId, CashierUserId, Total, Status, CorrelationId, CreatedAtUtc) " &
                $"VALUES ({_sessionId}, {_cashierUserId}, 10.0000, 'Completed', '{Guid.NewGuid()}', UTC_TIMESTAMP(6));")
            _saleId = Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

            Await ExecuteAsync(connection,
                "INSERT INTO SaleLines (SaleId, ProductId, Quantity, UnitPrice, Cost, LineTotal, CreatedAtUtc) " &
                $"VALUES ({_saleId}, {_productId}, 1.000, 10.0000, 5.0000, 10.0000, UTC_TIMESTAMP(6));")
            _saleLineId = Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

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
            $"VALUES ('p5_03_{_suffix}_{role}', 'not-a-usable-hash', 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));")

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

            If Await TableExistsAsync(connection, "salesreturnlines") Then
                Await ExecuteAsync(connection,
                    "DELETE FROM SalesReturnLines WHERE SalesReturnId IN " &
                    "(SELECT Id FROM (SELECT sr.Id FROM SalesReturns sr " &
                    $"WHERE sr.ReturnedByUserId IN (SELECT Id FROM Users WHERE Username LIKE 'p5_03_{_suffix}_%')) AS x);")
            End If

            If Await TableExistsAsync(connection, "salesreturns") Then
                Await ExecuteAsync(connection,
                    "DELETE FROM SalesReturns WHERE ReturnedByUserId IN " &
                    $"(SELECT Id FROM (SELECT Id FROM Users WHERE Username LIKE 'p5_03_{_suffix}_%') AS u);")
            End If

            If Await TableExistsAsync(connection, "salelines") Then
                Await ExecuteAsync(connection,
                    "DELETE FROM SaleLines WHERE SaleId IN " &
                    "(SELECT Id FROM (SELECT s.Id FROM Sales s " &
                    $"WHERE s.CashierUserId IN (SELECT Id FROM Users WHERE Username LIKE 'p5_03_{_suffix}_%')) AS x);")
            End If

            If Await TableExistsAsync(connection, "sales") Then
                Await ExecuteAsync(connection,
                    "DELETE FROM Sales WHERE CashierUserId IN " &
                    $"(SELECT Id FROM (SELECT Id FROM Users WHERE Username LIKE 'p5_03_{_suffix}_%') AS u);")
            End If

            If Await TableExistsAsync(connection, "cashiersessions") Then
                Await ExecuteAsync(connection,
                    "DELETE FROM CashierSessions WHERE OpenedByUserId IN " &
                    $"(SELECT Id FROM (SELECT Id FROM Users WHERE Username LIKE 'p5_03_{_suffix}_%') AS u);")
            End If

            Await ExecuteAsync(connection, $"DELETE FROM Products WHERE Sku      = 'p5_03_{_suffix}_sku';")
            Await ExecuteAsync(connection, $"DELETE FROM Users    WHERE Username LIKE 'p5_03_{_suffix}_%';")

        End Using

    End Function

    Private Async Function InsertReturnAsMigratorAsync(tag As String,
                                                        Optional status As String = "PendingApproval",
                                                        Optional exceedsThreshold As Boolean = False,
                                                        Optional approvedByUserId As Integer? = Nothing,
                                                        Optional refundMethod As String = Nothing,
                                                        Optional refundAmount As Decimal? = Nothing) As Task(Of Integer)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO SalesReturns (SaleId, ReturnedByUserId, ApprovedByUserId, Reason, ExceedsThreshold, " &
                    "Status, RefundMethod, RefundAmount, ReturnedAtUtc, ApprovedAtUtc, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@saleId, @returnedBy, @approvedBy, @reason, @exceeds, @status, @refundMethod, @refundAmount, " &
                    "UTC_TIMESTAMP(6), @approvedAt, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@saleId", _saleId)
                command.Parameters.AddWithValue("@returnedBy", _returnedByUserId)
                command.Parameters.AddWithValue("@approvedBy", ToDbValue(approvedByUserId))
                command.Parameters.AddWithValue("@reason", MakeTag(tag))
                command.Parameters.AddWithValue("@exceeds", If(exceedsThreshold, 1, 0))
                command.Parameters.AddWithValue("@status", status)
                command.Parameters.AddWithValue("@refundMethod", ToDbValue(refundMethod))
                command.Parameters.AddWithValue("@refundAmount", ToDbValue(refundAmount))
                command.Parameters.AddWithValue("@approvedAt", If(approvedByUserId.HasValue, CObj(DateTime.UtcNow), DBNull.Value))
                Await command.ExecuteNonQueryAsync()
            End Using

            Return Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

        End Using

    End Function

    Private Async Function InsertReturnLineAsMigratorAsync(returnId As Integer,
                                                            saleLineId As Integer,
                                                            productId As Integer,
                                                            quantityReturned As Decimal,
                                                            restocksItem As Boolean) As Task(Of Integer)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO SalesReturnLines (SalesReturnId, SaleLineId, ProductId, QuantityReturned, RestocksItem, CreatedAtUtc) " &
                    "VALUES (@returnId, @saleLineId, @productId, @quantity, @restocks, UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@returnId", returnId)
                command.Parameters.AddWithValue("@saleLineId", saleLineId)
                command.Parameters.AddWithValue("@productId", productId)
                command.Parameters.AddWithValue("@quantity", quantityReturned)
                command.Parameters.AddWithValue("@restocks", If(restocksItem, 1, 0))
                Await command.ExecuteNonQueryAsync()
            End Using

            Return Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

        End Using

    End Function

    ''' <summary>DBNull.Value for an absent Nullable(Of Decimal) - AddWithValue does not do this conversion itself.</summary>
    Private Shared Function ToDbValue(value As Decimal?) As Object
        Return If(value.HasValue, CType(value.Value, Object), DBNull.Value)
    End Function

    ''' <summary>DBNull.Value for an absent Nullable(Of Integer) - AddWithValue does not do this conversion itself.</summary>
    Private Shared Function ToDbValue(value As Integer?) As Object
        Return If(value.HasValue, CType(value.Value, Object), DBNull.Value)
    End Function

    ''' <summary>DBNull.Value for a Nothing String - AddWithValue does not do this conversion itself.</summary>
    Private Shared Function ToDbValue(value As String) As Object
        Return If(value Is Nothing, DBNull.Value, CType(value, Object))
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

    Private Async Function ScalarIntViaApiAsync(sql As String) As Task(Of Integer)
        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()
            Return Await ScalarIntAsync(connection, sql)
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

    Private Async Function DescribeForeignKeysAsync(tableName As String) _
        As Task(Of IReadOnlyList(Of (Column As String, RefTable As String)))

        Dim keys As New List(Of (Column As String, RefTable As String))

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT LOWER(COLUMN_NAME), LOWER(REFERENCED_TABLE_NAME) " &
                    "FROM information_schema.KEY_COLUMN_USAGE " &
                    "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @tableName " &
                    "AND REFERENCED_TABLE_NAME IS NOT NULL;"
                command.Parameters.AddWithValue("@tableName", tableName)
                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync()
                    While Await reader.ReadAsync()
                        keys.Add((reader.GetString(0), reader.GetString(1)))
                    End While
                End Using
            End Using
        End Using

        Return keys

    End Function

    Private Async Function AssertColumnTypeAsync(tableName As String, columnName As String, expectedType As String) As Task

        Dim columns As IReadOnlyDictionary(Of String, (DataType As String, IsNullable As Boolean)) =
            Await DescribeColumnsAsync(tableName)

        Assert.IsTrue(columns.ContainsKey(columnName), $"{tableName} must carry a {columnName} column.")
        Assert.AreEqual(expectedType, columns(columnName).DataType,
            $"{tableName}.{columnName} must be declared {expectedType}.")

    End Function

End Class
