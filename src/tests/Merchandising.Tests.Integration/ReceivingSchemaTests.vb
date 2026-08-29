' Merchandising.Tests.Integration.ReceivingSchemaTests
'
' P4-02: migration 0009 (Receipts, ReceiptLines, PurchaseReturns,
' PurchaseReturnLines) and db/grants/0011, proven against the real pinned
' MariaDB 10.4.32 instance (ADR-000, ADR-002, ADR-009) - never a substitute.
' Structured the same way P3-02's PurchaseOrderSchemaTests proves migration
' 0008: every "done when" claim that must keep being true on every future
' run is a test here, not a one-off transcript.
'
' THERE IS NO RECEIVING API HERE YET. P4-05 builds the first receiving
' endpoint and P4-08 the first purchase-return endpoint. Every statement
' below is raw SQL on a raw connection on purpose - the reference-uniqueness
' box specifically demands a race that bypasses any API check, and at this
' card there is no API to bypass, only a database to trust.
'
' TWO IDENTITIES, DELIBERATELY (ADR-013). Statements that must succeed as the
' application run as merch_api. Fixture setup/teardown and the FK-restriction
' proofs - which have to attempt a DELETE that merch_api is not granted at
' all - run as merch_migrator. Using merch_api for the FK proofs would fail
' with ERROR 1142 (no privilege) and never reach 1451 (referenced row),
' proving the grant model instead of the constraint.
'
' Fixtures are named p4_02_<suffix> with a fresh GUID per run and removed in
' TearDown as merch_migrator, the same per-run isolation
' PurchaseOrderSchemaTests uses. Nothing here drops or truncates a real table.

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks
Imports Merchandising.Infrastructure.Data
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class ReceivingSchemaTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const MigrationId As String = "0009_receiving"

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
    Private _receivedByUserId As Integer
    Private _returnRequesterUserId As Integer

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
    ' Done-when box 3 - the migration applies, and applies exactly once.
    ' =========================================================================

    ''' <summary>
    ''' The runner recorded 0009 once, with a checksum, on a database that
    ''' already carried 0001-0008. A second row - or a Succeeded = 0 row -
    ''' would mean the runner reapplied or half-applied it.
    ''' </summary>
    <TestMethod>
    Public Async Function Migration0009_IsRecordedExactlyOnce_AfterMigrations0001To0008() As Task

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
            Assert.IsTrue(rows(0).Succeeded, "The recorded application of 0009 must have succeeded.")
            Assert.AreEqual(64, rows(0).Checksum.Length, "A SHA-256 checksum must be recorded for 0009.")

            Dim applied As Integer = Await ScalarIntAsync(
                connection,
                "SELECT COUNT(*) FROM SchemaMigrations WHERE MigrationId IN " &
                "('0001_foundation','0002_authentication','0003_backup','0004_maintenance'," &
                "'0005_identity','0006_product-master','0007_suppliers','0008_purchase-orders') AND Succeeded = 1;")

            Assert.AreEqual(8, applied, "0009 must apply on top of 0001-0008, not in place of them.")

        End Using

    End Function

    ' =========================================================================
    ' Done-when box 1 - all four tables created, FKs prevent deletion (1451).
    ' =========================================================================

    ''' <summary>PurchaseOrders is a protected parent of Receipts.</summary>
    <TestMethod>
    Public Async Function DeletingAPurchaseOrderReferencedByAReceipt_IsRefusedWith1451() As Task

        Dim orderId As Integer = Await InsertOrderAsMigratorAsync("FK-PO")
        Await InsertReceiptAsMigratorAsync(orderId, _receivedByUserId, "FK-PO")

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM PurchaseOrders WHERE Id = {orderId};"))

            Console.WriteLine($"P4-02 order delete (receipt exists) -> ERROR {ex.Number}: {ex.Message}")

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A purchase order referenced by a receipt must not be deletable.")

        End Using

    End Function

    ''' <summary>Users is a protected parent of Receipts (ReceivedByUserId).</summary>
    <TestMethod>
    Public Async Function DeletingAUserReferencedByAReceipt_IsRefusedWith1451() As Task

        Dim orderId As Integer = Await InsertOrderAsMigratorAsync("FK-USR")
        Await InsertReceiptAsMigratorAsync(orderId, _receivedByUserId, "FK-USR")

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM Users WHERE Id = {_receivedByUserId};"))

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A user referenced as a receipt's ReceivedByUserId must not be deletable.")

        End Using

    End Function

    ''' <summary>PurchaseOrderLines is a protected parent of ReceiptLines.</summary>
    <TestMethod>
    Public Async Function DeletingAPurchaseOrderLineReferencedByAReceiptLine_IsRefusedWith1451() As Task

        Dim orderId As Integer = Await InsertOrderAsMigratorAsync("FK-POL")
        Dim lineId As Integer = Await InsertLineAsMigratorAsync(orderId, lineNumber:=1, orderedQuantity:=100D, purchaseCost:=5D)
        Dim receiptId As Integer = Await InsertReceiptAsMigratorAsync(orderId, _receivedByUserId, "FK-POL")
        Await InsertReceiptLineAsMigratorAsync(receiptId, lineId, _productId, quantityReceived:=10D, cost:=5D)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM PurchaseOrderLines WHERE Id = {lineId};"))

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A purchase-order line referenced by a receipt line must not be deletable.")

        End Using

    End Function

    ''' <summary>Products is a protected parent of ReceiptLines.</summary>
    <TestMethod>
    Public Async Function DeletingAProductReferencedByAReceiptLine_IsRefusedWith1451() As Task

        Dim orderId As Integer = Await InsertOrderAsMigratorAsync("FK-PRD")
        Dim lineId As Integer = Await InsertLineAsMigratorAsync(orderId, lineNumber:=1, orderedQuantity:=100D, purchaseCost:=5D)
        Dim receiptId As Integer = Await InsertReceiptAsMigratorAsync(orderId, _receivedByUserId, "FK-PRD")
        Await InsertReceiptLineAsMigratorAsync(receiptId, lineId, _productId, quantityReceived:=10D, cost:=5D)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM Products WHERE Id = {_productId};"))

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A product referenced by a receipt line must not be deletable.")

        End Using

    End Function

    ''' <summary>Receipts is a protected parent of ReceiptLines.</summary>
    <TestMethod>
    Public Async Function DeletingAReceiptReferencedByAReceiptLine_IsRefusedWith1451() As Task

        Dim orderId As Integer = Await InsertOrderAsMigratorAsync("FK-RCT")
        Dim lineId As Integer = Await InsertLineAsMigratorAsync(orderId, lineNumber:=1, orderedQuantity:=100D, purchaseCost:=5D)
        Dim receiptId As Integer = Await InsertReceiptAsMigratorAsync(orderId, _receivedByUserId, "FK-RCT")
        Await InsertReceiptLineAsMigratorAsync(receiptId, lineId, _productId, quantityReceived:=10D, cost:=5D)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM Receipts WHERE Id = {receiptId};"))

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A receipt header with lines must not be deletable.")

        End Using

    End Function

    ''' <summary>Receipts is also a protected parent of PurchaseReturns.</summary>
    <TestMethod>
    Public Async Function DeletingAReceiptReferencedByAPurchaseReturn_IsRefusedWith1451() As Task

        Dim orderId As Integer = Await InsertOrderAsMigratorAsync("FK-RTR")
        Dim receiptId As Integer = Await InsertReceiptAsMigratorAsync(orderId, _receivedByUserId, "FK-RTR")
        Await InsertPurchaseReturnAsMigratorAsync(receiptId, _returnRequesterUserId, "FK-RTR")

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM Receipts WHERE Id = {receiptId};"))

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A receipt referenced by a purchase return must not be deletable.")

        End Using

    End Function

    ''' <summary>PurchaseReturns is a protected parent of PurchaseReturnLines.</summary>
    <TestMethod>
    Public Async Function DeletingAPurchaseReturnReferencedByALine_IsRefusedWith1451() As Task

        Dim orderId As Integer = Await InsertOrderAsMigratorAsync("FK-PRL")
        Dim lineId As Integer = Await InsertLineAsMigratorAsync(orderId, lineNumber:=1, orderedQuantity:=100D, purchaseCost:=5D)
        Dim receiptId As Integer = Await InsertReceiptAsMigratorAsync(orderId, _receivedByUserId, "FK-PRL")
        Dim receiptLineId As Integer = Await InsertReceiptLineAsMigratorAsync(receiptId, lineId, _productId, quantityReceived:=10D, cost:=5D)
        Dim returnId As Integer = Await InsertPurchaseReturnAsMigratorAsync(receiptId, _returnRequesterUserId, "FK-PRL")
        Await InsertPurchaseReturnLineAsMigratorAsync(returnId, receiptLineId, _productId, quantityReturned:=1D, cost:=5D)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM PurchaseReturns WHERE Id = {returnId};"))

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A purchase return header with lines must not be deletable.")

        End Using

    End Function

    ''' <summary>ReceiptLines is a protected parent of PurchaseReturnLines.</summary>
    <TestMethod>
    Public Async Function DeletingAReceiptLineReferencedByAReturnLine_IsRefusedWith1451() As Task

        Dim orderId As Integer = Await InsertOrderAsMigratorAsync("FK-RCL")
        Dim lineId As Integer = Await InsertLineAsMigratorAsync(orderId, lineNumber:=1, orderedQuantity:=100D, purchaseCost:=5D)
        Dim receiptId As Integer = Await InsertReceiptAsMigratorAsync(orderId, _receivedByUserId, "FK-RCL")
        Dim receiptLineId As Integer = Await InsertReceiptLineAsMigratorAsync(receiptId, lineId, _productId, quantityReceived:=10D, cost:=5D)
        Dim returnId As Integer = Await InsertPurchaseReturnAsMigratorAsync(receiptId, _returnRequesterUserId, "FK-RCL")
        Await InsertPurchaseReturnLineAsMigratorAsync(returnId, receiptLineId, _productId, quantityReturned:=1D, cost:=5D)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM ReceiptLines WHERE Id = {receiptLineId};"))

            Assert.AreEqual(RowIsReferencedErrorNumber, ex.Number,
                "A receipt line referenced by a return line must not be deletable.")

        End Using

    End Function

    ' =========================================================================
    ' Done-when box 2 - receipt reference unique at the DATABASE, under a race.
    ' =========================================================================

    ''' <summary>
    ''' The P2-07 / P3-02 shape: two inserts fired without awaiting between
    ''' them, on two separate connections, sharing one reference number. No
    ''' API check exists to bypass at this card, which is the point -
    ''' uniqueness must be a fact about the server before any endpoint relies
    ''' on it.
    ''' </summary>
    <TestMethod>
    Public Async Function TwoConcurrentInsertsSameReceiptReference_ExactlyOneSucceeds() As Task

        Dim orderId As Integer = Await InsertOrderAsMigratorAsync("RACE")
        Dim reference As String = MakeReference("RACE")

        Dim first As Task(Of (Succeeded As Boolean, ErrorNumber As Integer)) =
            InsertReceiptRaceAsync(orderId, reference)
        Dim second As Task(Of (Succeeded As Boolean, ErrorNumber As Integer)) =
            InsertReceiptRaceAsync(orderId, reference)

        Dim results = Await Task.WhenAll(first, second)

        Console.WriteLine($"P4-02 concurrent receipt-reference insert -> " &
                          $"{DescribeOutcome(results(0))} | {DescribeOutcome(results(1))}")

        Assert.AreEqual(1, results.Count(Function(r) r.Succeeded),
            "Exactly one of two simultaneous inserts sharing a receipt reference must succeed.")
        Assert.AreEqual(1, results.Count(Function(r) Not r.Succeeded AndAlso r.ErrorNumber = DuplicateKeyErrorNumber),
            "The other must be refused ERROR 1062 by the unique key, not by anything else.")

    End Function

    ' =========================================================================
    ' Done-when box 5 - decimal round trip, exact, and declared types checked.
    ' =========================================================================

    ''' <summary>
    ''' The two values the card names, through every new decimal column:
    ''' 0.001 is the smallest representable quantity at DECIMAL(19,3) and
    ''' 12345678901234.5678 exercises all 18 significant digits of
    ''' DECIMAL(19,4). Compared as Decimal, never as Double - CLAUDE.md
    ''' section 5.
    ''' </summary>
    <TestMethod>
    Public Async Function DecimalColumns_RoundTripBoundaryValuesExactly() As Task

        Const smallestQuantity As Decimal = 0.001D
        Const largestMoney As Decimal = 12345678901234.5678D

        Dim orderId As Integer = Await InsertOrderAsMigratorAsync("DEC")
        Dim lineId As Integer = Await InsertLineAsMigratorAsync(orderId, lineNumber:=1, orderedQuantity:=1000000D, purchaseCost:=1D)
        Dim receiptId As Integer = Await InsertReceiptAsMigratorAsync(orderId, _receivedByUserId, "DEC")
        Dim receiptLineId As Integer =
            Await InsertReceiptLineAsMigratorAsync(receiptId, lineId, _productId, smallestQuantity, largestMoney)
        Dim returnId As Integer = Await InsertPurchaseReturnAsMigratorAsync(receiptId, _returnRequesterUserId, "DEC")
        Await InsertPurchaseReturnLineAsMigratorAsync(returnId, receiptLineId, _productId, smallestQuantity, largestMoney)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT QuantityReceived, Cost FROM ReceiptLines WHERE Id = @id;"
                command.Parameters.AddWithValue("@id", receiptLineId)
                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync()
                    Assert.IsTrue(Await reader.ReadAsync(), "The receipt line must be readable.")
                    Dim quantity As Decimal = reader.GetDecimal(0)
                    Dim cost As Decimal = reader.GetDecimal(1)
                    Assert.AreEqual(smallestQuantity, quantity, "0.001 must survive ReceiptLines.QuantityReceived (DECIMAL(19,3)) exactly.")
                    Assert.AreEqual(largestMoney, cost, "12345678901234.5678 must survive ReceiptLines.Cost (DECIMAL(19,4)) exactly.")
                End Using
            End Using

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT QuantityReturned, Cost FROM PurchaseReturnLines WHERE PurchaseReturnId = @id;"
                command.Parameters.AddWithValue("@id", returnId)
                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync()
                    Assert.IsTrue(Await reader.ReadAsync(), "The return line must be readable.")
                    Dim quantity As Decimal = reader.GetDecimal(0)
                    Dim cost As Decimal = reader.GetDecimal(1)
                    Assert.AreEqual(smallestQuantity, quantity, "0.001 must survive PurchaseReturnLines.QuantityReturned (DECIMAL(19,3)) exactly.")
                    Assert.AreEqual(largestMoney, cost, "12345678901234.5678 must survive PurchaseReturnLines.Cost (DECIMAL(19,4)) exactly.")
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

        Await AssertColumnTypeAsync("receiptlines", "quantityreceived", "decimal(19,3)")
        Await AssertColumnTypeAsync("receiptlines", "cost", "decimal(19,4)")
        Await AssertColumnTypeAsync("purchasereturnlines", "quantityreturned", "decimal(19,3)")
        Await AssertColumnTypeAsync("purchasereturnlines", "cost", "decimal(19,4)")

        For Each timestampColumn As String In New String() {"receivedatutc", "createdatutc"}
            Await AssertColumnTypeAsync("receipts", timestampColumn, "datetime(6)")
        Next

        For Each timestampColumn As String In New String() {"returnedatutc", "approvedatutc", "createdatutc", "updatedatutc"}
            Await AssertColumnTypeAsync("purchasereturns", timestampColumn, "datetime(6)")
        Next

        For Each tableName As String In New String() {"receipts", "receiptlines", "purchasereturns", "purchasereturnlines"}
            Dim collation As String = Await ScalarStringAsync(
                "SELECT TABLE_COLLATION FROM information_schema.TABLES " &
                $"WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{tableName}';")
            Assert.AreEqual("utf8mb4_unicode_ci", collation,
                $"{tableName} must state utf8mb4_unicode_ci, not inherit the server's general_ci default.")
        Next

    End Function

    ' =========================================================================
    ' Approval-state column: a stable name, refused if unknown.
    ' =========================================================================

    ''' <summary>Each of the three names the CHECK declares round-trips verbatim.</summary>
    <TestMethod>
    Public Async Function StatusColumn_AcceptsEveryDeclaredName() As Task

        Dim orderId As Integer = Await InsertOrderAsMigratorAsync("ST")
        Dim receiptId As Integer = Await InsertReceiptAsMigratorAsync(orderId, _receivedByUserId, "ST")

        Dim index As Integer = 0
        For Each statusName As String In New String() {"Requested", "Approved", "Rejected"}

            index += 1
            Dim returnId As Integer =
                Await InsertPurchaseReturnAsMigratorAsync(receiptId, _returnRequesterUserId, $"ST{index}", status:=statusName)

            Dim stored As String = Await ScalarStringAsync($"SELECT Status FROM PurchaseReturns WHERE Id = {returnId};")

            Assert.AreEqual(statusName, stored,
                $"'{statusName}' must round-trip verbatim - stored as its name, never as an ordinal.")

        Next

    End Function

    ''' <summary>An unknown name, and a lower-cased one, are both refused (utf8mb4_bin scoping).</summary>
    <TestMethod>
    Public Async Function StatusColumn_RefusesAnUnknownOrMiscasedName() As Task

        Dim orderId As Integer = Await InsertOrderAsMigratorAsync("BAD")
        Dim receiptId As Integer = Await InsertReceiptAsMigratorAsync(orderId, _receivedByUserId, "BAD")

        For Each bad As String In New String() {"requested", "Pending", ""}

            Dim ex As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() InsertPurchaseReturnAsMigratorAsync(
                        receiptId, _returnRequesterUserId, "BAD" & Guid.NewGuid().ToString("N").Substring(0, 4), status:=bad))

            Console.WriteLine($"P4-02 return status '{bad}' -> ERROR {ex.Number}")

            Assert.AreEqual(CheckConstraintFailedErrorNumber, ex.Number,
                $"Status '{bad}' must be refused by CK_PurchaseReturns_Status.")

        Next

    End Function

    ' =========================================================================
    ' Done-when box 4 - grants 0011, and only what it grants.
    ' =========================================================================

    ''' <summary>
    ''' merch_api can INSERT receipts and receipt lines, and INSERT+UPDATE
    ''' purchase returns - the one table this migration's header argues is
    ''' mutable because its approval state changes in place.
    ''' </summary>
    <TestMethod>
    Public Async Function MerchApi_CanInsertReceiving_AndUpdatePurchaseReturnStatus() As Task

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Dim orderId As Integer = Await InsertOrderAsMigratorAsync("API")
            Dim lineId As Integer = Await InsertLineAsMigratorAsync(orderId, lineNumber:=1, orderedQuantity:=100D, purchaseCost:=5D)

            Dim reference As String = MakeReference("API")
            Await ExecuteAsync(connection,
                "INSERT INTO Receipts (PurchaseOrderId, ReceivedByUserId, ReceivedAtUtc, ReferenceNumber, CreatedAtUtc) " &
                $"VALUES ({orderId}, {_receivedByUserId}, UTC_TIMESTAMP(6), '{reference}', UTC_TIMESTAMP(6));")
            Dim receiptId As Integer = Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

            Await ExecuteAsync(connection,
                "INSERT INTO ReceiptLines (ReceiptId, PurchaseOrderLineId, ProductId, QuantityReceived, Cost, CreatedAtUtc) " &
                $"VALUES ({receiptId}, {lineId}, {_productId}, 10.000, 5.0000, UTC_TIMESTAMP(6));")
            Dim receiptLineId As Integer = Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

            Dim returnReference As String = MakeReference("APIR")
            Await ExecuteAsync(connection,
                "INSERT INTO PurchaseReturns (ReceiptId, RequestedByUserId, Status, ReturnedAtUtc, ReferenceNumber, " &
                "RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                $"VALUES ({receiptId}, {_returnRequesterUserId}, 'Requested', UTC_TIMESTAMP(6), '{returnReference}', " &
                "0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));")
            Dim returnId As Integer = Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

            Await ExecuteAsync(connection,
                "INSERT INTO PurchaseReturnLines (PurchaseReturnId, ReceiptLineId, ProductId, QuantityReturned, Cost, " &
                "Reason, RemovesStock, CreatedAtUtc) " &
                $"VALUES ({returnId}, {receiptLineId}, {_productId}, 1.000, 5.0000, 'API test', 1, UTC_TIMESTAMP(6));")

            Await ExecuteAsync(connection,
                $"UPDATE PurchaseReturns SET Status = 'Approved', ApprovedByUserId = {_approverUserId}, " &
                $"ApprovedAtUtc = UTC_TIMESTAMP(6), RowVersion = RowVersion + 1 WHERE Id = {returnId};")

            Assert.AreEqual("Approved", Await ScalarStringAsync($"SELECT Status FROM PurchaseReturns WHERE Id = {returnId};"))

        End Using

    End Function

    ''' <summary>DELETE is denied ERROR 1142 on all four new tables.</summary>
    <TestMethod>
    Public Async Function MerchApi_HasNoDeleteGrant_OnAnyOfTheFourTables() As Task

        Dim orderId As Integer = Await InsertOrderAsMigratorAsync("NODEL")
        Dim lineId As Integer = Await InsertLineAsMigratorAsync(orderId, lineNumber:=1, orderedQuantity:=1D, purchaseCost:=1D)
        Dim receiptId As Integer = Await InsertReceiptAsMigratorAsync(orderId, _receivedByUserId, "NODEL")
        Dim receiptLineId As Integer = Await InsertReceiptLineAsMigratorAsync(receiptId, lineId, _productId, 1D, 1D)
        Dim returnId As Integer = Await InsertPurchaseReturnAsMigratorAsync(receiptId, _returnRequesterUserId, "NODEL")
        Dim returnLineId As Integer = Await InsertPurchaseReturnLineAsMigratorAsync(returnId, receiptLineId, _productId, 1D, 1D)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Dim returnLineDelete As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM PurchaseReturnLines WHERE Id = {returnLineId};"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, returnLineDelete.Number, "DELETE on PurchaseReturnLines must be denied ERROR 1142.")

            Dim returnDelete As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM PurchaseReturns WHERE Id = {returnId};"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, returnDelete.Number, "DELETE on PurchaseReturns must be denied ERROR 1142.")

            Dim receiptLineDelete As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM ReceiptLines WHERE Id = {receiptLineId};"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, receiptLineDelete.Number, "DELETE on ReceiptLines must be denied ERROR 1142.")

            Dim receiptDelete As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM Receipts WHERE Id = {receiptId};"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, receiptDelete.Number, "DELETE on Receipts must be denied ERROR 1142.")

        End Using

    End Function

    ''' <summary>merch_api cannot UPDATE Receipts, ReceiptLines or PurchaseReturnLines - only PurchaseReturns is mutable.</summary>
    <TestMethod>
    Public Async Function MerchApi_HasNoUpdateGrant_OnTheThreeImmutableTables() As Task

        Dim orderId As Integer = Await InsertOrderAsMigratorAsync("NOUPD")
        Dim lineId As Integer = Await InsertLineAsMigratorAsync(orderId, lineNumber:=1, orderedQuantity:=1D, purchaseCost:=1D)
        Dim receiptId As Integer = Await InsertReceiptAsMigratorAsync(orderId, _receivedByUserId, "NOUPD")
        Dim receiptLineId As Integer = Await InsertReceiptLineAsMigratorAsync(receiptId, lineId, _productId, 1D, 1D)
        Dim returnId As Integer = Await InsertPurchaseReturnAsMigratorAsync(receiptId, _returnRequesterUserId, "NOUPD")
        Dim returnLineId As Integer = Await InsertPurchaseReturnLineAsMigratorAsync(returnId, receiptLineId, _productId, 1D, 1D)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Dim receiptUpdate As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"UPDATE Receipts SET ReferenceNumber = 'x' WHERE Id = {receiptId};"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, receiptUpdate.Number, "UPDATE on Receipts must be denied ERROR 1142.")

            Dim receiptLineUpdate As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"UPDATE ReceiptLines SET Cost = 0 WHERE Id = {receiptLineId};"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, receiptLineUpdate.Number, "UPDATE on ReceiptLines must be denied ERROR 1142.")

            Dim returnLineUpdate As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"UPDATE PurchaseReturnLines SET Cost = 0 WHERE Id = {returnLineId};"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, returnLineUpdate.Number, "UPDATE on PurchaseReturnLines must be denied ERROR 1142.")

        End Using

    End Function

    ''' <summary>merch_api holds no DDL on the new tables either - ADR-013's split is not weakened by adding a table to it.</summary>
    <TestMethod>
    Public Async Function MerchApi_CannotAlterOrDropTheNewTables() As Task

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Dim alterEx As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, "ALTER TABLE Receipts ADD COLUMN Sneaked INT NULL;"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, alterEx.Number, "ALTER must be denied ERROR 1142.")

            Dim dropEx As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, "DROP TABLE PurchaseReturnLines;"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, dropEx.Number, "DROP must be denied ERROR 1142.")

        End Using

    End Function

    ' =========================================================================
    ' Fixtures and helpers
    ' =========================================================================

    Private Function MakeReference(tag As String) As String
        Return $"P4-02-{_suffix}-{tag}"
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
    ''' Creates the supplier, product and four users this run's orders,
    ''' receipts and returns point at. As merch_migrator, so TearDown can
    ''' remove them again - merch_api holds no DELETE on any of the three.
    ''' </summary>
    Private Async Function CreateFixturesAsync() As Task

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Await ExecuteAsync(connection,
                $"INSERT INTO Suppliers (Name, IsActive, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                $"VALUES ('p4_02_{_suffix}_supplier', 1, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));")
            _supplierId = Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

            Await ExecuteAsync(connection,
                $"INSERT INTO Products (Sku, Name, Price, Cost, ReorderLevel, IsActive, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                $"VALUES ('p4_02_{_suffix}_sku', 'P4-02 Fixture Product', 10.0000, 5.0000, 0.000, 1, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));")
            _productId = Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

            _requesterUserId = Await CreateFixtureUserAsync(connection, "requester")
            _approverUserId = Await CreateFixtureUserAsync(connection, "approver")
            _receivedByUserId = Await CreateFixtureUserAsync(connection, "receivedby")
            _returnRequesterUserId = Await CreateFixtureUserAsync(connection, "returnrequester")

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
            $"VALUES ('p4_02_{_suffix}_{role}', 'not-a-usable-hash', 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));")

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

            If Await TableExistsAsync(connection, "purchasereturnlines") Then
                Await ExecuteAsync(connection,
                    "DELETE FROM PurchaseReturnLines WHERE PurchaseReturnId IN " &
                    "(SELECT Id FROM (SELECT Id FROM PurchaseReturns WHERE ReferenceNumber LIKE " &
                    $"'P4-02-{_suffix}-%') AS pr);")
            End If

            If Await TableExistsAsync(connection, "purchasereturns") Then
                Await ExecuteAsync(connection,
                    $"DELETE FROM PurchaseReturns WHERE ReferenceNumber LIKE 'P4-02-{_suffix}-%';")
            End If

            If Await TableExistsAsync(connection, "receiptlines") Then
                Await ExecuteAsync(connection,
                    "DELETE FROM ReceiptLines WHERE ReceiptId IN " &
                    "(SELECT Id FROM (SELECT Id FROM Receipts WHERE ReferenceNumber LIKE " &
                    $"'P4-02-{_suffix}-%') AS r);")
            End If

            If Await TableExistsAsync(connection, "receipts") Then
                Await ExecuteAsync(connection,
                    $"DELETE FROM Receipts WHERE ReferenceNumber LIKE 'P4-02-{_suffix}-%';")
            End If

            If Await TableExistsAsync(connection, "purchaseorderlines") Then
                Await ExecuteAsync(connection,
                    "DELETE FROM PurchaseOrderLines WHERE PurchaseOrderId IN " &
                    "(SELECT Id FROM (SELECT Id FROM PurchaseOrders WHERE OrderNumber LIKE " &
                    $"'P4-02-{_suffix}-%') AS po);")
            End If

            If Await TableExistsAsync(connection, "purchaseorders") Then
                Await ExecuteAsync(connection,
                    $"DELETE FROM PurchaseOrders WHERE OrderNumber LIKE 'P4-02-{_suffix}-%';")
            End If

            Await ExecuteAsync(connection, $"DELETE FROM Products  WHERE Sku      = 'p4_02_{_suffix}_sku';")
            Await ExecuteAsync(connection, $"DELETE FROM Suppliers WHERE Name     = 'p4_02_{_suffix}_supplier';")
            Await ExecuteAsync(connection, $"DELETE FROM Users     WHERE Username LIKE 'p4_02_{_suffix}_%';")

        End Using

    End Function

    Private Async Function InsertOrderAsMigratorAsync(tag As String) As Task(Of Integer)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO PurchaseOrders (OrderNumber, SupplierId, Status, RequestedByUserId, " &
                    "ApprovedByUserId, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@number, @supplierId, 'Approved', @requestedBy, @approvedBy, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@number", MakeReference(tag))
                command.Parameters.AddWithValue("@supplierId", _supplierId)
                command.Parameters.AddWithValue("@requestedBy", _requesterUserId)
                command.Parameters.AddWithValue("@approvedBy", _approverUserId)
                Await command.ExecuteNonQueryAsync()
            End Using

            Return Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

        End Using

    End Function

    Private Async Function InsertLineAsMigratorAsync(orderId As Integer,
                                                     lineNumber As Integer,
                                                     orderedQuantity As Decimal,
                                                     purchaseCost As Decimal) As Task(Of Integer)

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

            Return Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

        End Using

    End Function

    Private Async Function InsertReceiptAsMigratorAsync(purchaseOrderId As Integer,
                                                        receivedByUserId As Integer,
                                                        tag As String) As Task(Of Integer)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO Receipts (PurchaseOrderId, ReceivedByUserId, ReceivedAtUtc, ReferenceNumber, CreatedAtUtc) " &
                    "VALUES (@orderId, @receivedBy, UTC_TIMESTAMP(6), @reference, UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@orderId", purchaseOrderId)
                command.Parameters.AddWithValue("@receivedBy", receivedByUserId)
                command.Parameters.AddWithValue("@reference", MakeReference(tag))
                Await command.ExecuteNonQueryAsync()
            End Using

            Return Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

        End Using

    End Function

    Private Async Function InsertReceiptLineAsMigratorAsync(receiptId As Integer,
                                                            purchaseOrderLineId As Integer,
                                                            productId As Integer,
                                                            quantityReceived As Decimal,
                                                            cost As Decimal) As Task(Of Integer)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO ReceiptLines (ReceiptId, PurchaseOrderLineId, ProductId, QuantityReceived, Cost, CreatedAtUtc) " &
                    "VALUES (@receiptId, @lineId, @productId, @quantity, @cost, UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@receiptId", receiptId)
                command.Parameters.AddWithValue("@lineId", purchaseOrderLineId)
                command.Parameters.AddWithValue("@productId", productId)
                command.Parameters.AddWithValue("@quantity", quantityReceived)
                command.Parameters.AddWithValue("@cost", cost)
                Await command.ExecuteNonQueryAsync()
            End Using

            Return Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

        End Using

    End Function

    Private Async Function InsertPurchaseReturnAsMigratorAsync(receiptId As Integer,
                                                               requestedByUserId As Integer,
                                                               tag As String,
                                                               Optional status As String = "Requested") As Task(Of Integer)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO PurchaseReturns (ReceiptId, RequestedByUserId, Status, ReturnedAtUtc, ReferenceNumber, " &
                    "RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@receiptId, @requestedBy, @status, UTC_TIMESTAMP(6), @reference, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@receiptId", receiptId)
                command.Parameters.AddWithValue("@requestedBy", requestedByUserId)
                command.Parameters.AddWithValue("@status", status)
                command.Parameters.AddWithValue("@reference", MakeReference(tag))
                Await command.ExecuteNonQueryAsync()
            End Using

            Return Await ScalarIntAsync(connection, "SELECT LAST_INSERT_ID();")

        End Using

    End Function

    Private Async Function InsertPurchaseReturnLineAsMigratorAsync(purchaseReturnId As Integer,
                                                                   receiptLineId As Integer,
                                                                   productId As Integer,
                                                                   quantityReturned As Decimal,
                                                                   cost As Decimal) As Task(Of Integer)

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO PurchaseReturnLines (PurchaseReturnId, ReceiptLineId, ProductId, QuantityReturned, " &
                    "Cost, Reason, RemovesStock, CreatedAtUtc) " &
                    "VALUES (@returnId, @receiptLineId, @productId, @quantity, @cost, 'Test reason', 1, UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@returnId", purchaseReturnId)
                command.Parameters.AddWithValue("@receiptLineId", receiptLineId)
                command.Parameters.AddWithValue("@productId", productId)
                command.Parameters.AddWithValue("@quantity", quantityReturned)
                command.Parameters.AddWithValue("@cost", cost)
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
    Private Async Function InsertReceiptRaceAsync(orderId As Integer, reference As String) As Task(Of (Succeeded As Boolean, ErrorNumber As Integer))

        Try
            Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()
                Using command As MySqlCommand = connection.CreateCommand()
                    command.CommandText =
                        "INSERT INTO Receipts (PurchaseOrderId, ReceivedByUserId, ReceivedAtUtc, ReferenceNumber, CreatedAtUtc) " &
                        "VALUES (@orderId, @receivedBy, UTC_TIMESTAMP(6), @reference, UTC_TIMESTAMP(6));"
                    command.Parameters.AddWithValue("@orderId", orderId)
                    command.Parameters.AddWithValue("@receivedBy", _receivedByUserId)
                    command.Parameters.AddWithValue("@reference", reference)
                    Await command.ExecuteNonQueryAsync()
                End Using
            End Using
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

    Private Async Function AssertColumnTypeAsync(tableName As String, columnName As String, expectedType As String) As Task

        Dim columns As IReadOnlyDictionary(Of String, (DataType As String, IsNullable As Boolean)) =
            Await DescribeColumnsAsync(tableName)

        Assert.IsTrue(columns.ContainsKey(columnName), $"{tableName} must carry a {columnName} column.")
        Assert.AreEqual(expectedType, columns(columnName).DataType,
            $"{tableName}.{columnName} must be declared {expectedType}.")

    End Function

End Class
