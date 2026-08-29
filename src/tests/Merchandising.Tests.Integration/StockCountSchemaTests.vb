' Merchandising.Tests.Integration.StockCountSchemaTests
'
' P4-03: migration 0010 (StockCounts, StockCountLines, StockAdjustments) and
' db/grants/0012, proven against the real pinned MariaDB 10.4.32 instance
' (ADR-000, ADR-002, ADR-009) - never a substitute. Structured the same way
' P3-02's PurchaseOrderSchemaTests and P4-02's ReceivingSchemaTests prove
' their migrations: every "done when" claim that must keep being true on
' every future run is a test here, not a one-off transcript.
'
' THERE IS NO COUNTS OR ADJUSTMENTS API HERE YET. P4-09 builds the first
' stock-count endpoint and P4-10 the first adjustment endpoint. Every
' statement below is raw SQL on a raw connection on purpose - there is no API
' to bypass at this card, only a database to trust.
'
' TWO IDENTITIES, DELIBERATELY (ADR-013). Statements that must succeed as the
' application run as merch_api. Fixture setup/teardown and the FK-restriction
' proofs - which have to attempt a DELETE that merch_api is not granted at
' all - run as merch_migrator, the same split PurchaseOrderSchemaTests and
' ReceivingSchemaTests use.
'
' Fixtures are named p4_03_<suffix> with a fresh GUID per run and removed in
' TearDown as merch_migrator, the same per-run isolation the two prior schema
' test files use. Nothing here drops or truncates a real table.

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks
Imports Merchandising.Domain.Inventory
Imports Merchandising.Infrastructure.Data
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class StockCountSchemaTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const MigrationId As String = "0010_counts-and-adjustments"

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
    Private _countedByUserId As Integer
    Private _approverUserId As Integer
    Private _requesterUserId As Integer

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
    ''' The runner recorded 0010 once, with a checksum, on a database that
    ''' already carried 0001-0009. A second row - or a Succeeded = 0 row -
    ''' would mean the runner reapplied or half-applied it.
    ''' </summary>
    <TestMethod>
    Public Async Function Migration0010_IsRecordedExactlyOnce_AfterMigrations0001To0009() As Task

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
            Assert.IsTrue(rows(0).Succeeded, "The recorded application of 0010 must have succeeded.")
            Assert.AreEqual(64, rows(0).Checksum.Length, "A SHA-256 checksum must be recorded for 0010.")

            Dim applied As Integer = Await ScalarIntAsync(
                connection,
                "SELECT COUNT(*) FROM SchemaMigrations WHERE MigrationId IN " &
                "('0001_foundation','0002_authentication','0003_backup','0004_maintenance'," &
                "'0005_identity','0006_product-master','0007_suppliers','0008_purchase-orders'," &
                "'0009_receiving') AND Succeeded = 1;")

            Assert.AreEqual(9, applied, "0010 must apply on top of 0001-0009, not in place of them.")

        End Using

    End Function

    ' =========================================================================
    ' Done-when box 4 (continued) - FKs prevent deletion, proven by 1451.
    ' =========================================================================

    ''' <summary>Users is a protected parent of StockCounts (CountedByUserId).</summary>
    <TestMethod>
    Public Async Function DeletingAUserReferencedAsCountedBy_IsRefusedWith1451() As Task

        Await InsertCountAsMigratorAsync("FK-CNT")

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM Users WHERE Id = {_countedByUserId};"))

            Console.WriteLine($"P4-03 counted-by delete -> ERROR {ex.Number}: {ex.Message}")

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A user referenced as a count's CountedByUserId must not be deletable.")

        End Using

    End Function

    ''' <summary>StockCounts is a protected parent of StockCountLines.</summary>
    <TestMethod>
    Public Async Function DeletingAStockCountReferencedByALine_IsRefusedWith1451() As Task

        Dim countId As Integer = Await InsertCountAsMigratorAsync("FK-HDR")
        Await InsertCountLineAsMigratorAsync(countId, _productId, countedQuantity:=5D, systemQuantity:=6D, variance:=-1D)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM StockCounts WHERE Id = {countId};"))

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A count header with lines must not be deletable.")

        End Using

    End Function

    ''' <summary>Products is a protected parent of StockCountLines.</summary>
    <TestMethod>
    Public Async Function DeletingAProductReferencedByAStockCountLine_IsRefusedWith1451() As Task

        Dim countId As Integer = Await InsertCountAsMigratorAsync("FK-PRD")
        Await InsertCountLineAsMigratorAsync(countId, _productId, countedQuantity:=5D, systemQuantity:=5D, variance:=0D)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM Products WHERE Id = {_productId};"))

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A product referenced by a stock-count line must not be deletable.")

        End Using

    End Function

    ''' <summary>Products is also a protected parent of StockAdjustments.</summary>
    <TestMethod>
    Public Async Function DeletingAProductReferencedByAnAdjustment_IsRefusedWith1451() As Task

        Await InsertAdjustmentAsMigratorAsync("FK-ADJ")

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM Products WHERE Id = {_productId};"))

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A product referenced by an adjustment must not be deletable.")

        End Using

    End Function

    ''' <summary>Users is a protected parent of StockAdjustments (RequestedByUserId).</summary>
    <TestMethod>
    Public Async Function DeletingAUserReferencedAsAdjustmentRequester_IsRefusedWith1451() As Task

        Await InsertAdjustmentAsMigratorAsync("FK-REQ")

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM Users WHERE Id = {_requesterUserId};"))

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A user referenced as an adjustment's RequestedByUserId must not be deletable.")

        End Using

    End Function

    ' =========================================================================
    ' Done-when box 2 - RequestedBy/ApprovedBy (and CountedBy/ApprovedBy) are
    ' separate columns, both nullable-approver.
    ' =========================================================================

    ''' <summary>
    ''' P4-10's self-approval veto (ADR-017 section 6) compares the requester
    ''' with the approver. It cannot if they are one field. Asserted against
    ''' information_schema rather than by reading the migration.
    ''' </summary>
    <TestMethod>
    Public Async Function RequestedByAndApprovedBy_AreTwoDistinctUserColumns_OnStockAdjustments() As Task

        Dim columns As IReadOnlyDictionary(Of String, (DataType As String, IsNullable As Boolean)) =
            Await DescribeColumnsAsync("stockadjustments")

        Assert.IsTrue(columns.ContainsKey("requestedbyuserid"), "StockAdjustments must carry RequestedByUserId.")
        Assert.IsTrue(columns.ContainsKey("approvedbyuserid"), "StockAdjustments must carry ApprovedByUserId.")

        Assert.IsFalse(columns("requestedbyuserid").IsNullable,
            "Every adjustment has a requester from the moment it exists.")
        Assert.IsTrue(columns("approvedbyuserid").IsNullable,
            "A Pending adjustment has no approver yet - ApprovedByUserId must be nullable.")

        Dim fks As IReadOnlyList(Of (Column As String, RefTable As String)) =
            Await DescribeForeignKeysAsync("stockadjustments")

        ' Enumerable.Count(predicate) written as Where(...).Count() on purpose:
        ' IReadOnlyList exposes a Count PROPERTY, and VB resolves the property
        ' first, so fks.Count(Function(f) ...) is a compile error (BC32016).
        Assert.AreEqual(1, fks.Where(Function(f) f.Column = "requestedbyuserid" AndAlso f.RefTable = "users").Count(),
            "RequestedByUserId must be a foreign key to Users.")
        Assert.AreEqual(1, fks.Where(Function(f) f.Column = "approvedbyuserid" AndAlso f.RefTable = "users").Count(),
            "ApprovedByUserId must be a separate foreign key to Users.")

    End Function

    ''' <summary>The same two-column, nullable-approver shape on StockCounts' CountedBy/ApprovedBy.</summary>
    <TestMethod>
    Public Async Function CountedByAndApprovedBy_AreTwoDistinctUserColumns_OnStockCounts() As Task

        Dim columns As IReadOnlyDictionary(Of String, (DataType As String, IsNullable As Boolean)) =
            Await DescribeColumnsAsync("stockcounts")

        Assert.IsTrue(columns.ContainsKey("countedbyuserid"), "StockCounts must carry CountedByUserId.")
        Assert.IsTrue(columns.ContainsKey("approvedbyuserid"), "StockCounts must carry ApprovedByUserId.")

        Assert.IsFalse(columns("countedbyuserid").IsNullable,
            "Every count session has someone who counted it.")
        Assert.IsTrue(columns("approvedbyuserid").IsNullable,
            "An Open or Closed-but-unreviewed count has no approver yet.")

    End Function

    ' =========================================================================
    ' Done-when box 3 - status is a stable identifier the Domain enum maps
    ' to, not an ordinal, on both StockCounts and StockAdjustments.
    ' =========================================================================

    ''' <summary>
    ''' ADR-020 section 5: enum NAMES are what round-trip, never the ordinal.
    ''' This test walks the Domain enum itself rather than a hand-typed list,
    ''' so a status added to <see cref="StockCountStatus"/> without a
    ''' matching migration fails here instead of at runtime.
    ''' </summary>
    <TestMethod>
    Public Async Function StockCountsStatusColumn_AcceptsEveryDomainStatusName() As Task

        Dim names As String() = [Enum].GetNames(GetType(StockCountStatus))

        Assert.HasCount(4, names, "P4-03 models four stock-count statuses.")

        Dim index As Integer = 0

        For Each statusName As String In names

            index += 1
            Dim countId As Integer = Await InsertCountAsMigratorAsync($"ST{index}", status:=statusName)

            Dim stored As String = Await ScalarStringAsync(
                $"SELECT Status FROM StockCounts WHERE Id = {countId};")

            Assert.AreEqual(statusName, stored,
                $"'{statusName}' must round-trip verbatim - stored as its name, never as an ordinal.")

        Next

    End Function

    ''' <summary>The same proof for StockAdjustments.Status against StockAdjustmentStatus.</summary>
    <TestMethod>
    Public Async Function StockAdjustmentsStatusColumn_AcceptsEveryDomainStatusName() As Task

        Dim names As String() = [Enum].GetNames(GetType(StockAdjustmentStatus))

        Assert.HasCount(4, names, "P4-03 models four stock-adjustment statuses.")

        Dim index As Integer = 0

        For Each statusName As String In names

            index += 1
            Dim adjustmentId As Integer = Await InsertAdjustmentAsMigratorAsync($"ST{index}", status:=statusName)

            Dim stored As String = Await ScalarStringAsync(
                $"SELECT Status FROM StockAdjustments WHERE Id = {adjustmentId};")

            Assert.AreEqual(statusName, stored,
                $"'{statusName}' must round-trip verbatim - stored as its name, never as an ordinal.")

        Next

    End Function

    ''' <summary>
    ''' An ordinal and a miscased/unknown name are both refused on both
    ''' status columns - the exact defect P3-02 hit under a case-insensitive
    ''' collation, guarded here by utf8mb4_bin.
    ''' </summary>
    <TestMethod>
    Public Async Function StockCountsStatusColumn_RefusesAnOrdinalAndAnUnknownName() As Task

        For Each bad As String In New String() {"1", "openn", "open", ""}

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() InsertCountAsMigratorAsync("BAD" & Guid.NewGuid().ToString("N").Substring(0, 4), status:=bad))

            Console.WriteLine($"P4-03 count status '{bad}' -> ERROR {ex.Number}")

            Assert.AreEqual(CheckConstraintFailedErrorNumber, ex.Number,
                $"Status '{bad}' must be refused by CK_StockCounts_Status.")

        Next

    End Function

    ''' <summary>The same refusal proof on StockAdjustments.Status.</summary>
    <TestMethod>
    Public Async Function StockAdjustmentsStatusColumn_RefusesAnOrdinalAndAnUnknownName() As Task

        For Each bad As String In New String() {"1", "pendingg", "pending", ""}

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() InsertAdjustmentAsMigratorAsync("BAD" & Guid.NewGuid().ToString("N").Substring(0, 4), status:=bad))

            Console.WriteLine($"P4-03 adjustment status '{bad}' -> ERROR {ex.Number}")

            Assert.AreEqual(CheckConstraintFailedErrorNumber, ex.Number,
                $"Status '{bad}' must be refused by CK_StockAdjustments_Status.")

        Next

    End Function

    ' =========================================================================
    ' Decimal round trip, exact, and declared types checked (CLAUDE.md 6.3).
    ' =========================================================================

    ''' <summary>
    ''' 0.001 is the smallest representable quantity at DECIMAL(19,3);
    ''' -0.001 exercises the signed side of StockAdjustments.QuantityVariance.
    ''' Compared as Decimal, never as Double - CLAUDE.md section 5.
    ''' </summary>
    <TestMethod>
    Public Async Function DecimalColumns_RoundTripBoundaryValuesExactly() As Task

        Const smallestQuantity As Decimal = 0.001D

        Dim countId As Integer = Await InsertCountAsMigratorAsync("DEC")
        Dim lineId As Integer =
            Await InsertCountLineAsMigratorAsync(countId, _productId, smallestQuantity, smallestQuantity, 0D)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT CountedQuantity, SystemQuantity, Variance FROM StockCountLines WHERE Id = @id;"
                command.Parameters.AddWithValue("@id", lineId)
                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync()
                    Assert.IsTrue(Await reader.ReadAsync(), "The count line must be readable.")
                    Assert.AreEqual(smallestQuantity, reader.GetDecimal(0), "0.001 must survive CountedQuantity exactly.")
                    Assert.AreEqual(smallestQuantity, reader.GetDecimal(1), "0.001 must survive SystemQuantity exactly.")
                    Assert.AreEqual(0D, reader.GetDecimal(2), "Variance must survive exactly.")
                End Using
            End Using

        End Using

        Dim negativeVariance As Decimal = -0.001D
        Dim adjustmentId As Integer = Await InsertAdjustmentAsMigratorAsync("DECNEG", quantityVariance:=negativeVariance)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT QuantityVariance FROM StockAdjustments WHERE Id = @id;"
                command.Parameters.AddWithValue("@id", adjustmentId)
                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync()
                    Assert.IsTrue(Await reader.ReadAsync(), "The adjustment must be readable.")
                    Assert.AreEqual(negativeVariance, reader.GetDecimal(0), "-0.001 must survive QuantityVariance exactly.")
                End Using
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
    Public Async Function DecimalColumns_AreDeclaredAtTheRequiredScale() As Task

        Await AssertColumnTypeAsync("stockcountlines", "countedquantity", "decimal(19,3)")
        Await AssertColumnTypeAsync("stockcountlines", "systemquantity", "decimal(19,3)")
        Await AssertColumnTypeAsync("stockcountlines", "variance", "decimal(19,3)")
        Await AssertColumnTypeAsync("stockadjustments", "quantityvariance", "decimal(19,3)")

        For Each timestampColumn As String In New String() {"countedatutc", "approvedatutc", "createdatutc", "updatedatutc"}
            Await AssertColumnTypeAsync("stockcounts", timestampColumn, "datetime(6)")
        Next

        For Each tableName As String In New String() {"stockcounts", "stockcountlines", "stockadjustments"}
            Dim collation As String = Await ScalarStringAsync(
                "SELECT TABLE_COLLATION FROM information_schema.TABLES " &
                $"WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{tableName}';")
            Assert.AreEqual("utf8mb4_unicode_ci", collation,
                $"{tableName} must state utf8mb4_unicode_ci, not inherit the server's general_ci default.")
        Next

    End Function

    ' =========================================================================
    ' Done-when box 5 - grants 0012, and only what it grants.
    ' =========================================================================

    ''' <summary>merch_api can INSERT and UPDATE stockcounts and stockadjustments; INSERT only on stockcountlines.</summary>
    <TestMethod>
    Public Async Function MerchApi_CanInsertAndUpdateHeaders_AndInsertOnlyLines() As Task

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Await ExecuteAsync(connection,
                "INSERT INTO StockCounts (Status, CountedByUserId, CountedAtUtc, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                $"VALUES ('Open', {_countedByUserId}, UTC_TIMESTAMP(6), 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));")
            Dim countId As Integer = Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

            Await ExecuteAsync(connection,
                "INSERT INTO StockCountLines (StockCountId, ProductId, CountedQuantity, SystemQuantity, Variance, CreatedAtUtc) " &
                $"VALUES ({countId}, {_productId}, 8.000, 10.000, -2.000, UTC_TIMESTAMP(6));")

            Await ExecuteAsync(connection,
                $"UPDATE StockCounts SET Status = 'Closed', RowVersion = RowVersion + 1 WHERE Id = {countId};")

            Assert.AreEqual("Closed", Await ScalarStringAsync($"SELECT Status FROM StockCounts WHERE Id = {countId};"))

            Await ExecuteAsync(connection,
                "INSERT INTO StockAdjustments (ProductId, QuantityVariance, Reason, RequestedByUserId, ExceedsThreshold, " &
                "Status, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                $"VALUES ({_productId}, -2.000, 'Count variance', {_requesterUserId}, 1, 'Pending', 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));")
            Dim adjustmentId As Integer = Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

            Await ExecuteAsync(connection,
                $"UPDATE StockAdjustments SET Status = 'Approved', ApprovedByUserId = {_approverUserId}, " &
                $"RowVersion = RowVersion + 1 WHERE Id = {adjustmentId};")

            Assert.AreEqual("Approved", Await ScalarStringAsync($"SELECT Status FROM StockAdjustments WHERE Id = {adjustmentId};"))

        End Using

    End Function

    ''' <summary>DELETE is denied ERROR 1142 on all three new tables.</summary>
    <TestMethod>
    Public Async Function MerchApi_HasNoDeleteGrant_OnAnyOfTheThreeTables() As Task

        Dim countId As Integer = Await InsertCountAsMigratorAsync("NODEL")
        Dim lineId As Integer = Await InsertCountLineAsMigratorAsync(countId, _productId, 1D, 1D, 0D)
        Dim adjustmentId As Integer = Await InsertAdjustmentAsMigratorAsync("NODEL")

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Dim lineDelete As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM StockCountLines WHERE Id = {lineId};"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, lineDelete.Number, "DELETE on StockCountLines must be denied ERROR 1142.")

            Dim countDelete As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM StockCounts WHERE Id = {countId};"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, countDelete.Number, "DELETE on StockCounts must be denied ERROR 1142.")

            Dim adjustmentDelete As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM StockAdjustments WHERE Id = {adjustmentId};"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, adjustmentDelete.Number, "DELETE on StockAdjustments must be denied ERROR 1142.")

        End Using

    End Function

    ''' <summary>merch_api cannot UPDATE StockCountLines - only the two headers are mutable.</summary>
    <TestMethod>
    Public Async Function MerchApi_HasNoUpdateGrant_OnStockCountLines() As Task

        Dim countId As Integer = Await InsertCountAsMigratorAsync("NOUPD")
        Dim lineId As Integer = Await InsertCountLineAsMigratorAsync(countId, _productId, 1D, 1D, 0D)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Dim lineUpdate As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"UPDATE StockCountLines SET Variance = 0 WHERE Id = {lineId};"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, lineUpdate.Number, "UPDATE on StockCountLines must be denied ERROR 1142.")

        End Using

    End Function

    ''' <summary>merch_api holds no DDL on the new tables either - ADR-013's split is not weakened by adding a table to it.</summary>
    <TestMethod>
    Public Async Function MerchApi_CannotAlterOrDropTheNewTables() As Task

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Dim alterEx As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, "ALTER TABLE StockCounts ADD COLUMN Sneaked INT NULL;"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, alterEx.Number, "ALTER must be denied ERROR 1142.")

            Dim dropEx As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, "DROP TABLE StockAdjustments;"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, dropEx.Number, "DROP must be denied ERROR 1142.")

        End Using

    End Function

    ' =========================================================================
    ' Fixtures and helpers
    ' =========================================================================

    Private Function MakeTag(suffixTag As String) As String
        Return $"P4-03-{_suffix}-{suffixTag}"
    End Function

    Private Shared Function LoadMigratorOptions() As DatabaseOptions
        Dim migratorConfigPath As String =
            Path.Combine(Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName)
        Return DatabaseOptionsLoader.Load(migratorConfigPath)
    End Function

    ''' <summary>
    ''' Creates the product and three users this run's counts and adjustments
    ''' point at. As merch_migrator, so TearDown can remove them again -
    ''' merch_api holds no DELETE on any of the three.
    ''' </summary>
    Private Async Function CreateFixturesAsync() As Task

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Await ExecuteAsync(connection,
                $"INSERT INTO Products (Sku, Name, Price, Cost, ReorderLevel, IsActive, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                $"VALUES ('p4_03_{_suffix}_sku', 'P4-03 Fixture Product', 10.0000, 5.0000, 0.000, 1, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));")
            _productId = Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

            _countedByUserId = Await CreateFixtureUserAsync(connection, "countedby")
            _approverUserId = Await CreateFixtureUserAsync(connection, "approver")
            _requesterUserId = Await CreateFixtureUserAsync(connection, "requester")

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
            $"VALUES ('p4_03_{_suffix}_{role}', 'not-a-usable-hash', 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));")

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

            If Await TableExistsAsync(connection, "stockadjustments") Then
                Await ExecuteAsync(connection,
                    "DELETE FROM StockAdjustments WHERE RequestedByUserId IN " &
                    $"(SELECT Id FROM (SELECT Id FROM Users WHERE Username LIKE 'p4_03_{_suffix}_%') AS u);")
            End If

            If Await TableExistsAsync(connection, "stockcountlines") Then
                Await ExecuteAsync(connection,
                    "DELETE FROM StockCountLines WHERE StockCountId IN " &
                    "(SELECT Id FROM (SELECT sc.Id FROM StockCounts sc " &
                    $"WHERE sc.CountedByUserId IN (SELECT Id FROM Users WHERE Username LIKE 'p4_03_{_suffix}_%')) AS x);")
            End If

            If Await TableExistsAsync(connection, "stockcounts") Then
                Await ExecuteAsync(connection,
                    "DELETE FROM StockCounts WHERE CountedByUserId IN " &
                    $"(SELECT Id FROM (SELECT Id FROM Users WHERE Username LIKE 'p4_03_{_suffix}_%') AS u);")
            End If

            Await ExecuteAsync(connection, $"DELETE FROM Products WHERE Sku      = 'p4_03_{_suffix}_sku';")
            Await ExecuteAsync(connection, $"DELETE FROM Users    WHERE Username LIKE 'p4_03_{_suffix}_%';")

        End Using

    End Function

    Private Async Function InsertCountAsMigratorAsync(tag As String,
                                                       Optional status As String = "Open") As Task(Of Integer)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO StockCounts (Status, CountedByUserId, CountedAtUtc, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@status, @countedBy, UTC_TIMESTAMP(6), 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@status", status)
                command.Parameters.AddWithValue("@countedBy", _countedByUserId)
                Await command.ExecuteNonQueryAsync()
            End Using

            Return Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

        End Using

    End Function

    Private Async Function InsertCountLineAsMigratorAsync(countId As Integer,
                                                          productId As Integer,
                                                          countedQuantity As Decimal,
                                                          systemQuantity As Decimal,
                                                          variance As Decimal) As Task(Of Integer)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO StockCountLines (StockCountId, ProductId, CountedQuantity, SystemQuantity, Variance, CreatedAtUtc) " &
                    "VALUES (@countId, @productId, @counted, @system, @variance, UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@countId", countId)
                command.Parameters.AddWithValue("@productId", productId)
                command.Parameters.AddWithValue("@counted", countedQuantity)
                command.Parameters.AddWithValue("@system", systemQuantity)
                command.Parameters.AddWithValue("@variance", variance)
                Await command.ExecuteNonQueryAsync()
            End Using

            Return Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

        End Using

    End Function

    Private Async Function InsertAdjustmentAsMigratorAsync(tag As String,
                                                            Optional status As String = "Pending",
                                                            Optional quantityVariance As Decimal = -1D) As Task(Of Integer)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO StockAdjustments (ProductId, QuantityVariance, Reason, RequestedByUserId, " &
                    "ExceedsThreshold, Status, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@productId, @variance, @reason, @requestedBy, 1, @status, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@productId", _productId)
                command.Parameters.AddWithValue("@variance", quantityVariance)
                command.Parameters.AddWithValue("@reason", MakeTag(tag))
                command.Parameters.AddWithValue("@requestedBy", _requesterUserId)
                command.Parameters.AddWithValue("@status", status)
                Await command.ExecuteNonQueryAsync()
            End Using

            Return Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

        End Using

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
