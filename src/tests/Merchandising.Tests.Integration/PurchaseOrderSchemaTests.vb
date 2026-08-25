' Merchandising.Tests.Integration.PurchaseOrderSchemaTests
'
' P3-02: migration 0008 (PurchaseOrders, PurchaseOrderLines) and
' db/grants/0010, proven against the real pinned MariaDB 10.4.32 instance
' (ADR-000, ADR-002, ADR-009) - never a substitute.
'
' WHY THIS IS A TEST FILE AND NOT ONLY A CAPTURED TRANSCRIPT. P2-06 proved
' migration 0006 with a mysql-CLI transcript under evidence/. That was
' adequate for a one-off shape check, but three of this card's done-when
' boxes ("proven with ERROR 1451 rather than asserted by inspection", the
' concurrent double-insert in the P2-07 shape, the decimal round trip) are
' claims that must keep being true on every future run, on a classmate's
' machine, after a restore. A transcript proves them once. This file proves
' them every time run-tests.ps1 runs, and the transcript is rendered from
' it rather than typed.
'
' THERE IS NO API HERE YET. P3-03 builds the first purchase-order endpoint.
' Every statement below is raw SQL on a raw connection on purpose - the
' uniqueness box specifically demands a race that "bypasses any API check",
' and at this card there is no API to bypass, only a database to trust.
'
' TWO IDENTITIES, DELIBERATELY (ADR-013). Statements that must succeed as
' the application run as merch_api. Fixture setup and teardown - and the two
' FK-restriction proofs, which have to attempt a DELETE that merch_api is
' not granted at all - run as merch_migrator. Using merch_api for the FK
' proofs would fail with ERROR 1142 (no privilege) and never reach 1451
' (referenced row), proving the grant model instead of the constraint.
'
' Fixtures are named p3_02_<suffix> with a fresh GUID per run and removed in
' TearDown as merch_migrator, the same per-run isolation MigrationRunnerTests
' uses. Nothing here drops or truncates a real table.

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks
Imports Merchandising.Domain.Procurement
Imports Merchandising.Infrastructure.Data
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class PurchaseOrderSchemaTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const MigrationId As String = "0008_purchase-orders"

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

    Private _supplierId As Integer
    Private _productId As Integer
    Private _requesterUserId As Integer
    Private _approverUserId As Integer

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
    ' Done-when box 5 - the migration applies, and applies exactly once.
    ' =========================================================================

    ''' <summary>
    ''' The runner recorded 0008 once, with a checksum, on a database that
    ''' already carried 0001-0007. A second row - or a Succeeded = 0 row -
    ''' would mean the runner reapplied or half-applied it.
    ''' </summary>
    <TestMethod>
    Public Async Function Migration0008_IsRecordedExactlyOnce_AfterMigrations0001To0007() As Task

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
            Assert.IsTrue(rows(0).Succeeded, "The recorded application of 0008 must have succeeded.")
            Assert.AreEqual(64, rows(0).Checksum.Length, "A SHA-256 checksum must be recorded for 0008.")

            ' The predecessors are still recorded - 0008 added to the chain
            ' rather than replacing it.
            Dim applied As Integer = Await ScalarIntAsync(
                connection,
                "SELECT COUNT(*) FROM SchemaMigrations WHERE MigrationId IN " &
                "('0001_foundation','0002_authentication','0003_backup','0004_maintenance'," &
                "'0005_identity','0006_product-master','0007_suppliers') AND Succeeded = 1;")

            Assert.AreEqual(7, applied, "0008 must apply on top of 0001-0007, not in place of them.")

        End Using

    End Function

    ' =========================================================================
    ' Done-when box 1 - the foreign keys PREVENT deletion, proven by 1451.
    ' =========================================================================

    ''' <summary>
    ''' Spec section 12: "Foreign-key behavior must prevent accidental
    ''' deletion of records referenced by sales, receipts, returns, movements,
    ''' or audit events." Attempted as merch_migrator, the one identity that
    ''' actually holds DELETE - so the refusal is the constraint talking, not
    ''' the grant.
    ''' </summary>
    <TestMethod>
    Public Async Function DeletingASupplierReferencedByAnOrder_IsRefusedWith1451() As Task

        Await InsertOrderAsMigratorAsync(OrderNumber("FK-SUP"))

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM Suppliers WHERE Id = {_supplierId};"))

            Console.WriteLine($"P3-02 supplier delete -> ERROR {ex.Number}: {ex.Message}")

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A supplier referenced by a purchase order must not be deletable.")

        End Using

    End Function

    ''' <summary>Same proof for the line's product FK.</summary>
    <TestMethod>
    Public Async Function DeletingAProductReferencedByAnOrderLine_IsRefusedWith1451() As Task

        Dim orderId As Integer = Await InsertOrderAsMigratorAsync(OrderNumber("FK-PROD"))
        Await InsertLineAsMigratorAsync(orderId, lineNumber:=1, orderedQuantity:=5D, purchaseCost:=12.5D)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM Products WHERE Id = {_productId};"))

            Console.WriteLine($"P3-02 product delete -> ERROR {ex.Number}: {ex.Message}")

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A product referenced by a purchase-order line must not be deletable.")

        End Using

    End Function

    ''' <summary>An order header referenced by a line cannot be deleted either.</summary>
    <TestMethod>
    Public Async Function DeletingAnOrderReferencedByALine_IsRefusedWith1451() As Task

        Dim orderId As Integer = Await InsertOrderAsMigratorAsync(OrderNumber("FK-HDR"))
        Await InsertLineAsMigratorAsync(orderId, lineNumber:=1, orderedQuantity:=1D, purchaseCost:=1D)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM PurchaseOrders WHERE Id = {orderId};"))

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "An order header with lines must not be deletable.")

        End Using

    End Function

    ' =========================================================================
    ' Done-when box 2 - RequestedBy and ApprovedBy are two columns.
    ' =========================================================================

    ''' <summary>
    ''' ADR-017 section 6's self-approval veto compares the requester with the
    ''' approver at P3-04. It cannot if they are one field. Asserted against
    ''' information_schema rather than by reading the migration, because the
    ''' migration file is not what the server is running.
    ''' </summary>
    <TestMethod>
    Public Async Function RequestedByAndApprovedBy_AreTwoDistinctUserColumns() As Task

        Dim columns As IReadOnlyDictionary(Of String, (DataType As String, IsNullable As Boolean)) =
            Await DescribeColumnsAsync("purchaseorders")

        Assert.IsTrue(columns.ContainsKey("requestedbyuserid"), "PurchaseOrders must carry RequestedByUserId.")
        Assert.IsTrue(columns.ContainsKey("approvedbyuserid"), "PurchaseOrders must carry ApprovedByUserId.")

        Assert.IsFalse(columns("requestedbyuserid").IsNullable,
            "Every order has a requester from the moment it exists.")
        Assert.IsTrue(columns("approvedbyuserid").IsNullable,
            "A Draft order has no approver yet - ApprovedByUserId must be nullable.")

        ' Both point at Users, and they are separate constraints.
        Dim fks As IReadOnlyList(Of (Column As String, RefTable As String)) =
            Await DescribeForeignKeysAsync("purchaseorders")

        ' Enumerable.Count(predicate) written as Where(...).Count() on purpose:
        ' IReadOnlyList exposes a Count PROPERTY, and VB resolves the property
        ' first, so fks.Count(Function(f) ...) is a compile error (BC32016).
        Assert.AreEqual(1, fks.Where(Function(f) f.Column = "requestedbyuserid" AndAlso f.RefTable = "users").Count(),
            "RequestedByUserId must be a foreign key to Users.")
        Assert.AreEqual(1, fks.Where(Function(f) f.Column = "approvedbyuserid" AndAlso f.RefTable = "users").Count(),
            "ApprovedByUserId must be a separate foreign key to Users.")

    End Function

    ' =========================================================================
    ' Done-when box 3 - order number unique at the DATABASE, under a race.
    ' =========================================================================

    ''' <summary>
    ''' The P2-07 shape: two inserts fired without awaiting between them, on
    ''' two separate connections, sharing one order number. No API check
    ''' exists to bypass at this card, which is the point - uniqueness must
    ''' be a fact about the server before any endpoint relies on it.
    ''' </summary>
    <TestMethod>
    Public Async Function TwoConcurrentInsertsSameOrderNumber_ExactlyOneSucceeds() As Task

        Dim number As String = OrderNumber("RACE")

        Dim first As Task(Of (Succeeded As Boolean, ErrorNumber As Integer)) = InsertOrderRaceAsync(number)
        Dim second As Task(Of (Succeeded As Boolean, ErrorNumber As Integer)) = InsertOrderRaceAsync(number)

        Dim results = Await Task.WhenAll(first, second)

        Console.WriteLine($"P3-02 concurrent order-number insert -> " &
                          $"{DescribeOutcome(results(0))} | {DescribeOutcome(results(1))}")

        Assert.AreEqual(1, results.Count(Function(r) r.Succeeded),
            "Exactly one of two simultaneous inserts sharing an order number must succeed.")
        Assert.AreEqual(1, results.Count(Function(r) Not r.Succeeded AndAlso r.ErrorNumber = DuplicateKeyErrorNumber),
            "The other must be refused ERROR 1062 by the unique key, not by anything else.")

    End Function

    ' =========================================================================
    ' Done-when box 4 - status is a stable identifier, not an ordinal.
    ' =========================================================================

    ''' <summary>
    ''' ADR-020 section 5: enum NAMES are what P3-02 stores. This test walks
    ''' the Domain enum itself rather than a hand-typed list, so a status
    ''' added to <see cref="PurchaseOrderStatus"/> without a matching
    ''' migration fails here instead of at runtime.
    ''' </summary>
    <TestMethod>
    Public Async Function StatusColumn_AcceptsEveryDomainStatusName() As Task

        Dim names As String() = [Enum].GetNames(GetType(PurchaseOrderStatus))

        Assert.HasCount(7, names, "Spec section 10.1 states seven statuses.")

        Dim index As Integer = 0

        For Each statusName As String In names

            index += 1
            Dim orderId As Integer = Await InsertOrderAsMigratorAsync(
                OrderNumber($"ST{index}"), status:=statusName)

            Dim stored As String = Await ScalarStringAsync(
                $"SELECT Status FROM PurchaseOrders WHERE Id = {orderId};")

            Assert.AreEqual(statusName, stored,
                $"'{statusName}' must round-trip verbatim - stored as its name, never as an ordinal.")

        Next

    End Function

    ''' <summary>
    ''' The other half of the same box: a value that is not one of the seven
    ''' is refused by the server. Includes an ordinal, which is exactly the
    ''' representation ADR-020 rejects - if '3' were storable, a renumbered
    ''' enum would silently rewrite history.
    ''' </summary>
    <TestMethod>
    Public Async Function StatusColumn_RefusesAnOrdinalAndAnUnknownName() As Task

        For Each bad As String In New String() {"3", "Approvedd", "draft", ""}

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() InsertOrderAsMigratorAsync(OrderNumber("BAD" & Guid.NewGuid().ToString("N").Substring(0, 4)), status:=bad))

            Console.WriteLine($"P3-02 status '{bad}' -> ERROR {ex.Number}")

            Assert.AreEqual(CheckConstraintFailedErrorNumber, ex.Number,
                $"Status '{bad}' must be refused by CK_PurchaseOrders_Status.")

        Next

    End Function

    ' =========================================================================
    ' Done-when box 7 - decimal round trip, exact.
    ' =========================================================================

    ''' <summary>
    ''' The two values the card names, through the new columns: 0.001 is the
    ''' smallest representable quantity at DECIMAL(19,3) and 12345678901234.5678
    ''' exercises all 18 significant digits of DECIMAL(19,4). Compared as
    ''' Decimal, never as Double - CLAUDE.md section 5.
    ''' </summary>
    <TestMethod>
    Public Async Function DecimalColumns_RoundTripBoundaryValuesExactly() As Task

        Const smallestQuantity As Decimal = 0.001D
        Const largestMoney As Decimal = 12345678901234.5678D

        Dim orderId As Integer = Await InsertOrderAsMigratorAsync(OrderNumber("DEC"))

        Await InsertLineAsMigratorAsync(orderId, lineNumber:=1,
                                        orderedQuantity:=smallestQuantity,
                                        purchaseCost:=largestMoney)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT OrderedQuantity, PurchaseCost, ReceivedQuantity FROM PurchaseOrderLines " &
                    "WHERE PurchaseOrderId = @orderId;"
                command.Parameters.AddWithValue("@orderId", orderId)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync()

                    Assert.IsTrue(Await reader.ReadAsync(), "The line must be readable.")

                    Dim quantity As Decimal = reader.GetDecimal(0)
                    Dim cost As Decimal = reader.GetDecimal(1)
                    Dim received As Decimal = reader.GetDecimal(2)

                    Console.WriteLine($"P3-02 round trip -> OrderedQuantity={quantity} " &
                                      $"PurchaseCost={cost} ReceivedQuantity={received}")

                    Assert.AreEqual(smallestQuantity, quantity, "0.001 must survive DECIMAL(19,3) exactly.")
                    Assert.AreEqual(largestMoney, cost, "12345678901234.5678 must survive DECIMAL(19,4) exactly.")
                    Assert.AreEqual(0D, received, "ReceivedQuantity must default to zero, not NULL.")

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
    Public Async Function MoneyAndQuantityColumns_AreDeclaredAtTheRequiredPrecision() As Task

        Await AssertColumnTypeAsync("purchaseorderlines", "orderedquantity", "decimal(19,3)")
        Await AssertColumnTypeAsync("purchaseorderlines", "receivedquantity", "decimal(19,3)")
        Await AssertColumnTypeAsync("purchaseorderlines", "purchasecost", "decimal(19,4)")

        For Each timestampColumn As String In New String() {"createdatutc", "updatedatutc", "submittedatutc", "approvedatutc"}
            Await AssertColumnTypeAsync("purchaseorders", timestampColumn, "datetime(6)")
        Next

        For Each tableName As String In New String() {"purchaseorders", "purchaseorderlines"}
            Dim collation As String = Await ScalarStringAsync(
                "SELECT TABLE_COLLATION FROM information_schema.TABLES " &
                $"WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{tableName}';")
            Assert.AreEqual("utf8mb4_unicode_ci", collation,
                $"{tableName} must state utf8mb4_unicode_ci, not inherit the server's general_ci default.")
        Next

    End Function

    ' =========================================================================
    ' Spec section 12 - received quantity bounded by ordered quantity.
    ' =========================================================================

    ''' <summary>
    ''' Spec section 12 lists "receipt quantity bounded by ordered quantity by
    ''' default" as a core Procurement integrity requirement, and spec section
    ''' 10.1's MVP default is to reject over-receiving. Phase 4 enforces it at
    ''' the API; this proves the server refuses it regardless.
    ''' </summary>
    <TestMethod>
    Public Async Function ReceivedQuantityAboveOrdered_IsRefusedByTheDatabase() As Task

        Dim orderId As Integer = Await InsertOrderAsMigratorAsync(OrderNumber("OVER"))
        Await InsertLineAsMigratorAsync(orderId, lineNumber:=1, orderedQuantity:=10D, purchaseCost:=1D)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection,
                        $"UPDATE PurchaseOrderLines SET ReceivedQuantity = 10.001 WHERE PurchaseOrderId = {orderId};"))

            Console.WriteLine($"P3-02 over-receive -> ERROR {ex.Number}")

            Assert.AreEqual(CheckConstraintFailedErrorNumber, ex.Number,
                "Receiving more than was ordered must be refused by CK_PurchaseOrderLines_ReceivedQuantity.")

            ' The bound is inclusive: receiving exactly what was ordered is legal.
            Await ExecuteAsync(connection,
                $"UPDATE PurchaseOrderLines SET ReceivedQuantity = 10.000 WHERE PurchaseOrderId = {orderId};")

        End Using

    End Function

    ' =========================================================================
    ' Done-when box 6 - grants 0010, and only what it grants.
    ' =========================================================================

    ''' <summary>
    ''' merch_api can INSERT and UPDATE both tables. A Draft order is
    ''' legitimately editable, so UPDATE here is deliberate rather than the
    ''' ADR-013 append-only default - see the header of
    ''' db/grants/0010_purchase-order-grants.sql.
    ''' </summary>
    <TestMethod>
    Public Async Function MerchApi_CanInsertAndUpdate_BothTables() As Task

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Dim number As String = OrderNumber("API")

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO PurchaseOrders (OrderNumber, SupplierId, Status, RequestedByUserId, " &
                    "RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@number, @supplierId, 'Draft', @requestedBy, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@number", number)
                command.Parameters.AddWithValue("@supplierId", _supplierId)
                command.Parameters.AddWithValue("@requestedBy", _requesterUserId)
                Await command.ExecuteNonQueryAsync()
            End Using

            Dim orderId As Integer = Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

            Await ExecuteAsync(connection,
                $"UPDATE PurchaseOrders SET Status = 'Submitted', RowVersion = RowVersion + 1 WHERE Id = {orderId};")

            Await ExecuteAsync(connection,
                "INSERT INTO PurchaseOrderLines (PurchaseOrderId, LineNumber, ProductId, OrderedQuantity, " &
                $"PurchaseCost, ReceivedQuantity, RowVersion, CreatedAtUtc, UpdatedAtUtc) VALUES ({orderId}, 1, " &
                $"{_productId}, 3.000, 4.5000, 0.000, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));")

            Await ExecuteAsync(connection,
                $"UPDATE PurchaseOrderLines SET OrderedQuantity = 4.000 WHERE PurchaseOrderId = {orderId};")

            Assert.AreEqual("Submitted", Await ScalarStringAsync($"SELECT Status FROM PurchaseOrders WHERE Id = {orderId};"))

        End Using

    End Function

    ''' <summary>
    ''' And nothing beyond that. DELETE is not granted on either table: spec
    ''' section 12's "transactional records are never physically deleted"
    ''' covers a purchase order, and P3-05 keeps a cancelled order readable
    ''' rather than removing it.
    ''' </summary>
    <TestMethod>
    Public Async Function MerchApi_HasNoDeleteGrant_OnEitherTable() As Task

        Dim orderId As Integer = Await InsertOrderAsMigratorAsync(OrderNumber("NODEL"))
        Await InsertLineAsMigratorAsync(orderId, lineNumber:=1, orderedQuantity:=1D, purchaseCost:=1D)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Dim lineDelete As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM PurchaseOrderLines WHERE PurchaseOrderId = {orderId};"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, lineDelete.Number,
                "DELETE on PurchaseOrderLines must be denied ERROR 1142.")

            Dim orderDelete As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM PurchaseOrders WHERE Id = {orderId};"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, orderDelete.Number,
                "DELETE on PurchaseOrders must be denied ERROR 1142.")

        End Using

    End Function

    ''' <summary>
    ''' merch_api holds no DDL on the new tables either - ADR-013's split is
    ''' not weakened by adding a table to it.
    ''' </summary>
    <TestMethod>
    Public Async Function MerchApi_CannotAlterOrDropTheNewTables() As Task

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Dim alterEx As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, "ALTER TABLE PurchaseOrders ADD COLUMN Sneaked INT NULL;"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, alterEx.Number, "ALTER must be denied ERROR 1142.")

            Dim dropEx As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, "DROP TABLE PurchaseOrderLines;"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, dropEx.Number, "DROP must be denied ERROR 1142.")

        End Using

    End Function

    ' =========================================================================
    ' Fixtures and helpers
    ' =========================================================================

    Private Function OrderNumber(tag As String) As String
        Return $"P3-02-{_suffix}-{tag}"
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
    ''' Creates the supplier, product and two users this run's orders point
    ''' at. As merch_migrator, so TearDown can remove them again - merch_api
    ''' holds no DELETE on any of the three (0002, 0008, 0009 grants).
    ''' </summary>
    Private Async Function CreateFixturesAsync() As Task

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Await ExecuteAsync(connection,
                $"INSERT INTO Suppliers (Name, IsActive, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                $"VALUES ('p3_02_{_suffix}_supplier', 1, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));")
            _supplierId = Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

            Await ExecuteAsync(connection,
                $"INSERT INTO Products (Sku, Name, Price, Cost, ReorderLevel, IsActive, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                $"VALUES ('p3_02_{_suffix}_sku', 'P3-02 Fixture Product', 10.0000, 5.0000, 0.000, 1, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));")
            _productId = Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

            _requesterUserId = Await CreateFixtureUserAsync(connection, "requester")
            _approverUserId = Await CreateFixtureUserAsync(connection, "approver")

        End Using

    End Function

    ''' <summary>
    ''' A row in Users, not an account anyone can log in as - the password
    ''' hash is a placeholder. This card needs a valid foreign key target and
    ''' nothing more; P3-04 is where a real approver authenticates.
    ''' </summary>
    Private Async Function CreateFixtureUserAsync(connection As MySqlConnection, role As String) As Task(Of Integer)

        Await ExecuteAsync(connection,
            $"INSERT INTO Users (Username, PasswordHash, IsActive, CreatedAtUtc, UpdatedAtUtc) " &
            $"VALUES ('p3_02_{_suffix}_{role}', 'not-a-usable-hash', 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));")

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

            If Await TableExistsAsync(connection, "purchaseorderlines") Then
                Await ExecuteAsync(connection,
                    "DELETE FROM PurchaseOrderLines WHERE PurchaseOrderId IN " &
                    $"(SELECT Id FROM PurchaseOrders WHERE OrderNumber LIKE 'P3-02-{_suffix}-%');")
            End If

            If Await TableExistsAsync(connection, "purchaseorders") Then
                Await ExecuteAsync(connection,
                    $"DELETE FROM PurchaseOrders WHERE OrderNumber LIKE 'P3-02-{_suffix}-%';")
            End If

            Await ExecuteAsync(connection, $"DELETE FROM Products  WHERE Sku      = 'p3_02_{_suffix}_sku';")
            Await ExecuteAsync(connection, $"DELETE FROM Suppliers WHERE Name     = 'p3_02_{_suffix}_supplier';")
            Await ExecuteAsync(connection, $"DELETE FROM Users     WHERE Username LIKE 'p3_02_{_suffix}_%';")

        End Using

    End Function

    Private Async Function InsertOrderAsMigratorAsync(number As String,
                                                      Optional status As String = "Draft") As Task(Of Integer)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO PurchaseOrders (OrderNumber, SupplierId, Status, RequestedByUserId, " &
                    "RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@number, @supplierId, @status, @requestedBy, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@number", number)
                command.Parameters.AddWithValue("@supplierId", _supplierId)
                command.Parameters.AddWithValue("@status", status)
                command.Parameters.AddWithValue("@requestedBy", _requesterUserId)
                Await command.ExecuteNonQueryAsync()
            End Using

            Return Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

        End Using

    End Function

    Private Async Function InsertLineAsMigratorAsync(orderId As Integer,
                                                     lineNumber As Integer,
                                                     orderedQuantity As Decimal,
                                                     purchaseCost As Decimal) As Task

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO PurchaseOrderLines (PurchaseOrderId, LineNumber, ProductId, OrderedQuantity, " &
                    "PurchaseCost, ReceivedQuantity, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@orderId, @lineNumber, @productId, @quantity, @cost, 0, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@orderId", orderId)
                command.Parameters.AddWithValue("@lineNumber", lineNumber)
                command.Parameters.AddWithValue("@productId", _productId)
                command.Parameters.AddWithValue("@quantity", orderedQuantity)
                command.Parameters.AddWithValue("@cost", purchaseCost)
                Await command.ExecuteNonQueryAsync()
            End Using

        End Using

    End Function

    ''' <summary>
    ''' One racer. Opens its own connection and reports the outcome instead of
    ''' throwing, so the caller can fire two and inspect both - a throwing
    ''' racer would fail the test rather than being one of its two results.
    ''' </summary>
    Private Async Function InsertOrderRaceAsync(number As String) As Task(Of (Succeeded As Boolean, ErrorNumber As Integer))

        Try
            Await InsertOrderAsMigratorAsync(number)
            Return (True, 0)
        Catch ex As MySqlException
            Return (False, ex.Number)
        End Try

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
