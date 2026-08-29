' Merchandising.Tests.Integration.PurchaseReturnTests
'
' P4-08: POST /api/v1/receipts/{receiptId}/returns and
' PurchaseReturnService.RecordAsync, proven against the real pinned MariaDB -
' spec section 10.1's "The API rejects a return quantity that exceeds the
' received quantity less prior returns", and CLAUDE.md section 5's atomicity
' rule applied to a purchase return's own stock-out effect.
'
' Role gating (PurchaseReturns.Manage resolves to the roles PolicyRegistry
' says it does) is AuthorizationMatrixTests' job. This file proves the
' BEHAVIOUR:
'
'   box 1   the bound is computed server-side from committed ReceiptLines/
'           PurchaseReturnLines rows - Cost and ProductId on the response
'           come from the locked receipt line, never a client value (the
'           request contract has no such fields to begin with)
'   box 2   over-returning is refused with a stable error code, including
'           the case where two PRIOR partial returns together already
'           exhaust the bound
'   box 3   each return writes its own StockMovements row; StockMovements
'           stays append-only (no UPDATE/DELETE grant reaches it)
'   box 4   concurrent returns against the SAME receipt line cannot oversell
'           the bound - proven under real concurrent load, the P1-13 shape
'   box 5   the P4-01 ledger reconciliation passes after every return
'
' Fixture users, supplier and products are real, permanent rows - nothing
' here is torn down (ADR-013). Two Admin fixtures are needed to create and
' approve the purchase order a receipt is built from (ADR-017 section 6);
' a separate ProcurementOfficer fixture and Inventory fixture supply the
' PurchaseReturns.Manage and Receiving.Confirm actors respectively - two
' DIFFERENT policies, so one actor per policy keeps each test's assertions
' about role gating irrelevant to this file (that is AuthorizationMatrixTests'
' job) and isolates which service is actually being exercised.

Imports System.Collections.Generic
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Net.Http.Json
Imports System.Text.Json
Imports System.Threading.Tasks
Imports Merchandising.Api.Procurement
Imports Merchandising.Api.Receiving
Imports Merchandising.Contracts.Auth
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Procurement
Imports Merchandising.Contracts.Receiving
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class PurchaseReturnTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const FixturePassword As String = "P4-08 Fixture Passw0rd!"
    Private Const AdminAUsername As String = "p4_08_fixture_admin_a"
    Private Const AdminBUsername As String = "p4_08_fixture_admin_b"
    Private Const ProcurementUsername As String = "p4_08_fixture_procurement"
    Private Const InventoryUsername As String = "p4_08_fixture_inventory"
    Private Const FixtureProductSkuPrefix As String = "p4_08_fixture_sku_"

    ''' <summary>ERROR 1142: the command is denied to this user for this table - proves StockMovements has no UPDATE/DELETE grant reaching it, the same constant PurchaseOrderSchemaTests defines.</summary>
    Private Const TableAccessDeniedErrorNumber As Integer = 1142

    Private _factory As MerchandisingApiFactory
    Private _connectionFactory As ConnectionFactory
    Private _purchaseOrderService As PurchaseOrderService
    Private _receivingService As ReceivingService
    Private _purchaseReturnService As PurchaseReturnService
    Private _supplierId As Integer
    Private _adminAUserId As Integer
    Private _adminBUserId As Integer
    Private _procurementUserId As Integer
    Private _inventoryUserId As Integer

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _factory = New MerchandisingApiFactory()
        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        _purchaseOrderService = New PurchaseOrderService(_connectionFactory)
        _receivingService = New ReceivingService(_connectionFactory)
        _purchaseReturnService = New PurchaseReturnService(_connectionFactory)

        _adminAUserId = Await EnsureFixtureUserAsync(AdminAUsername, "Admin")
        _adminBUserId = Await EnsureFixtureUserAsync(AdminBUsername, "Admin")
        _procurementUserId = Await EnsureFixtureUserAsync(ProcurementUsername, "ProcurementOfficer")
        _inventoryUserId = Await EnsureFixtureUserAsync(InventoryUsername, "InventoryClerk")
        _supplierId = Await CreateActiveSupplierAsync()

    End Function

    <TestCleanup>
    Public Sub TearDown()
        _factory?.Dispose()
    End Sub

    ' ------------------------------------------------------------------ box 1

    ''' <summary>Box 1: the committed return line's Cost and ProductId come from the LOCKED ReceiptLines row, never a client-supplied value - RecordPurchaseReturnLineRequest has no such fields, so this also proves the whole response is server-derived.</summary>
    <TestMethod>
    Public Async Function RecordAsync_ValidReturn_CostAndProductComeFromTheReceiptLine() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim received = Await ReceiveFullyAsync(productId, 10.000D, 3.7500D)

        Dim outcome As PurchaseReturnOutcome =
            Await _purchaseReturnService.RecordAsync(
                received.ReceiptId, NewReferenceNumber(), New List(Of RecordPurchaseReturnLineRequest) From {
                    New RecordPurchaseReturnLineRequest With {
                        .ReceiptLineId = received.ReceiptLineId, .QuantityReturned = 4.000D, .Reason = "Wrong item shipped", .RemovesStock = True}
                },
                _procurementUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseReturnOutcomeKind.Created, outcome.Kind)
        Assert.AreEqual("Approved", outcome.Response.Status)
        Assert.AreEqual(_procurementUserId, outcome.Response.RequestedByUserId)
        Assert.AreEqual(_procurementUserId, outcome.Response.ApprovedByUserId)
        Assert.AreEqual(1, outcome.Response.Lines.Count)

        Dim line = outcome.Response.Lines(0)
        Assert.AreEqual(productId, line.ProductId)
        Assert.AreEqual(3.7500D, line.Cost, "Cost must be copied from the receipt line, not any client value (the request has no cost field).")
        Assert.IsNotNull(line.MovementId)
        Assert.AreEqual(6.000D, Await ReadBalanceAsync(productId), "10 received - 4 returned = 6.")

    End Function

    ''' <summary>RemovesStock = False records the return with no stock effect at all - no movement, no balance change, but the bound still accounts for it.</summary>
    <TestMethod>
    Public Async Function RecordAsync_RemovesStockFalse_NoStockEffectButBoundStillAccountsForIt() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim received = Await ReceiveFullyAsync(productId, 10.000D, 1.0000D)

        Dim outcome As PurchaseReturnOutcome =
            Await _purchaseReturnService.RecordAsync(
                received.ReceiptId, NewReferenceNumber(), New List(Of RecordPurchaseReturnLineRequest) From {
                    New RecordPurchaseReturnLineRequest With {
                        .ReceiptLineId = received.ReceiptLineId, .QuantityReturned = 10.000D, .Reason = "Defective, already written off", .RemovesStock = False}
                },
                _procurementUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseReturnOutcomeKind.Created, outcome.Kind)
        Assert.IsNull(outcome.Response.Lines(0).MovementId, "RemovesStock=False must write no StockMovements row.")
        Assert.AreEqual(10.000D, Await ReadBalanceAsync(productId), "Balance must be untouched.")

        ' The bound is now exhausted (10 of 10 returned) even though no
        ' stock moved - the next attempt must be refused.
        Dim secondOutcome As PurchaseReturnOutcome =
            Await _purchaseReturnService.RecordAsync(
                received.ReceiptId, NewReferenceNumber(), New List(Of RecordPurchaseReturnLineRequest) From {
                    New RecordPurchaseReturnLineRequest With {
                        .ReceiptLineId = received.ReceiptLineId, .QuantityReturned = 0.001D, .Reason = "Should be refused", .RemovesStock = False}
                },
                _procurementUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseReturnOutcomeKind.OverReturned, secondOutcome.Kind)

    End Function

    ' ------------------------------------------------------------------ box 2

    ''' <summary>Box 2: a single return exceeding the receipt line's QuantityReceived is refused.</summary>
    <TestMethod>
    Public Async Function RecordAsync_QuantityExceedsReceived_RefusedWithStableCode() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim received = Await ReceiveFullyAsync(productId, 10.000D, 1.0000D)

        Dim outcome As PurchaseReturnOutcome =
            Await _purchaseReturnService.RecordAsync(
                received.ReceiptId, NewReferenceNumber(), New List(Of RecordPurchaseReturnLineRequest) From {
                    New RecordPurchaseReturnLineRequest With {
                        .ReceiptLineId = received.ReceiptLineId, .QuantityReturned = 10.001D, .Reason = "Too many", .RemovesStock = True}
                },
                _procurementUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseReturnOutcomeKind.OverReturned, outcome.Kind)
        Assert.AreEqual(PurchaseReturnOutcome.OverReturnedErrorCode, outcome.ErrorCode)
        Assert.AreEqual(received.ReceiptLineId, outcome.OffendingReceiptLineId)
        Assert.AreEqual(10.000D, Await ReadBalanceAsync(productId), "A refused return must leave the balance untouched.")

    End Function

    ''' <summary>Box 2: two PRIOR partial returns together exhaust the bound (6 + 4 = 10), and a THIRD is refused with the same code - not inferred, computed from the sum of committed rows.</summary>
    <TestMethod>
    Public Async Function RecordAsync_TwoPriorPartialReturnsExhaustBound_ThirdRefused() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim received = Await ReceiveFullyAsync(productId, 10.000D, 1.0000D)

        Dim firstOutcome As PurchaseReturnOutcome =
            Await _purchaseReturnService.RecordAsync(
                received.ReceiptId, NewReferenceNumber(), New List(Of RecordPurchaseReturnLineRequest) From {
                    New RecordPurchaseReturnLineRequest With {.ReceiptLineId = received.ReceiptLineId, .QuantityReturned = 6.000D, .Reason = "Partial 1", .RemovesStock = True}
                },
                _procurementUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(PurchaseReturnOutcomeKind.Created, firstOutcome.Kind)

        Dim secondOutcome As PurchaseReturnOutcome =
            Await _purchaseReturnService.RecordAsync(
                received.ReceiptId, NewReferenceNumber(), New List(Of RecordPurchaseReturnLineRequest) From {
                    New RecordPurchaseReturnLineRequest With {.ReceiptLineId = received.ReceiptLineId, .QuantityReturned = 4.000D, .Reason = "Partial 2, exactly closes it", .RemovesStock = True}
                },
                _procurementUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(PurchaseReturnOutcomeKind.Created, secondOutcome.Kind)
        Assert.AreEqual(0.000D, Await ReadBalanceAsync(productId), "10 received, 10 returned (6+4).")

        Dim thirdOutcome As PurchaseReturnOutcome =
            Await _purchaseReturnService.RecordAsync(
                received.ReceiptId, NewReferenceNumber(), New List(Of RecordPurchaseReturnLineRequest) From {
                    New RecordPurchaseReturnLineRequest With {.ReceiptLineId = received.ReceiptLineId, .QuantityReturned = 0.001D, .Reason = "Must be refused", .RemovesStock = True}
                },
                _procurementUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseReturnOutcomeKind.OverReturned, thirdOutcome.Kind)
        Assert.AreEqual(PurchaseReturnOutcome.OverReturnedErrorCode, thirdOutcome.ErrorCode)

    End Function

    ' ------------------------------------------------------------------ box 3

    ''' <summary>Box 3: two returns against different lines write two DISTINCT StockMovements rows (never one shared/edited row).</summary>
    <TestMethod>
    Public Async Function RecordAsync_TwoReturns_EachWritesItsOwnMovementRow() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim received = Await ReceiveFullyAsync(productId, 10.000D, 1.0000D)

        Dim firstCorrelationId As String = Guid.NewGuid().ToString()
        Await _purchaseReturnService.RecordAsync(
            received.ReceiptId, NewReferenceNumber(), New List(Of RecordPurchaseReturnLineRequest) From {
                New RecordPurchaseReturnLineRequest With {.ReceiptLineId = received.ReceiptLineId, .QuantityReturned = 3.000D, .Reason = "First", .RemovesStock = True}
            },
            _procurementUserId, firstCorrelationId, Guid.NewGuid().ToString())

        Dim secondCorrelationId As String = Guid.NewGuid().ToString()
        Await _purchaseReturnService.RecordAsync(
            received.ReceiptId, NewReferenceNumber(), New List(Of RecordPurchaseReturnLineRequest) From {
                New RecordPurchaseReturnLineRequest With {.ReceiptLineId = received.ReceiptLineId, .QuantityReturned = 2.000D, .Reason = "Second", .RemovesStock = True}
            },
            _procurementUserId, secondCorrelationId, Guid.NewGuid().ToString())

        Assert.AreEqual(1L, Await CountMovementsAsync(firstCorrelationId, productId))
        Assert.AreEqual(1L, Await CountMovementsAsync(secondCorrelationId, productId))
        Assert.AreEqual(5.000D, Await ReadBalanceAsync(productId), "10 received - 3 - 2 = 5.")

    End Function

    ''' <summary>Box 3: StockMovements has no UPDATE or DELETE grant reaching it, proven fresh against a row this card's own code just wrote - corrections can only ever be a new compensating row, never an edit (CLAUDE.md section 5).</summary>
    <TestMethod>
    Public Async Function StockMovements_FromAPurchaseReturn_HasNoUpdateOrDeleteGrant() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim received = Await ReceiveFullyAsync(productId, 10.000D, 1.0000D)
        Dim correlationId As String = Guid.NewGuid().ToString()

        Dim outcome As PurchaseReturnOutcome =
            Await _purchaseReturnService.RecordAsync(
                received.ReceiptId, NewReferenceNumber(), New List(Of RecordPurchaseReturnLineRequest) From {
                    New RecordPurchaseReturnLineRequest With {.ReceiptLineId = received.ReceiptLineId, .QuantityReturned = 5.000D, .Reason = "For the grant proof", .RemovesStock = True}
                },
                _procurementUserId, correlationId, Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseReturnOutcomeKind.Created, outcome.Kind)
        Dim movementId As Integer = outcome.Response.Lines(0).MovementId.Value

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            Dim updateEx As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"UPDATE StockMovements SET Reason = 'tampered' WHERE Id = {movementId};"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, updateEx.Number, "UPDATE on stockmovements must be denied at the grant level.")

            Dim deleteEx As MySqlException =
                Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                    Function() ExecuteAsync(connection, $"DELETE FROM StockMovements WHERE Id = {movementId};"))
            Assert.AreEqual(TableAccessDeniedErrorNumber, deleteEx.Number, "DELETE on stockmovements must be denied at the grant level.")

        End Using

    End Function

    ' ------------------------------------------------------------------ box 4

    ''' <summary>
    ''' Box 4: N concurrent return attempts against the SAME receipt line,
    ''' each requesting the FULL received quantity, launched together and
    ''' awaited only after all are underway (the same "fire without
    ''' awaiting" shape StockDecrementTests.RunConcurrencyRoundAsync uses
    ''' for P1-13). Exactly one may succeed; every other must receive the
    ''' controlled OverReturned outcome, never an unexpected exception -
    ''' proving PurchaseReturnRepository.GetReceiptLineForUpdateAsync's row
    ''' lock, not two sequential calls that could pass by accident.
    ''' </summary>
    <TestMethod>
    Public Async Function RecordAsync_ConcurrentReturnsAgainstSameLine_CannotOversellTheBound() As Task

        Const RequestCount As Integer = 8

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim received = Await ReceiveFullyAsync(productId, 5.000D, 1.0000D)

        Dim tasks(RequestCount - 1) As Task(Of (Kind As String, Detail As String))
        For i = 0 To RequestCount - 1
            tasks(i) = AttemptReturnAsync(received.ReceiptId, received.ReceiptLineId, 5.000D)
        Next

        Dim results = Await Task.WhenAll(tasks)

        Dim successCount As Integer = 0
        Dim overReturnedCount As Integer = 0
        Dim exceptionCount As Integer = 0
        Dim distribution As New System.Text.StringBuilder()

        For i = 0 To RequestCount - 1
            Select Case results(i).Kind
                Case NameOf(PurchaseReturnOutcomeKind.Created)
                    successCount += 1
                Case NameOf(PurchaseReturnOutcomeKind.OverReturned)
                    overReturnedCount += 1
                Case Else
                    exceptionCount += 1
            End Select
            If i > 0 Then distribution.Append(" | ")
            distribution.Append($"{results(i).Kind}: {results(i).Detail}")
        Next

        Console.WriteLine($"P4-08 x{RequestCount} raw distribution -> {distribution}")
        Console.WriteLine($"P4-08 x{RequestCount} summary -> Created:{successCount} OverReturned:{overReturnedCount} Exception:{exceptionCount}")

        Assert.AreEqual(1, successCount, $"Exactly one of {RequestCount} simultaneous returns against the same line must succeed.")
        Assert.AreEqual(0, exceptionCount, "No attempt may fail with an unexpected exception - only Created or OverReturned are controlled outcomes.")
        Assert.AreEqual(RequestCount - 1, overReturnedCount, "Every non-winning request must receive the controlled OverReturned outcome.")

        Assert.AreEqual(0.000D, Await ReadBalanceAsync(productId), "Final balance must be exactly zero - never negative, never oversold.")
        Assert.AreEqual(5.000D, Await SumReturnedQuantityAsync(received.ReceiptLineId), "Exactly one 5.000 return may be recorded across all attempts.")

    End Function

    Private Async Function AttemptReturnAsync(receiptId As Integer, receiptLineId As Integer, quantity As Decimal) As Task(Of (Kind As String, Detail As String))

        Try
            Dim outcome As PurchaseReturnOutcome =
                Await _purchaseReturnService.RecordAsync(
                    receiptId:=receiptId,
                    referenceNumber:=NewReferenceNumber(),
                    lines:=New List(Of RecordPurchaseReturnLineRequest) From {
                        New RecordPurchaseReturnLineRequest With {.ReceiptLineId = receiptLineId, .QuantityReturned = quantity, .Reason = "Concurrency probe", .RemovesStock = True}},
                    actorUserId:=_procurementUserId,
                    correlationId:=Guid.NewGuid().ToString(),
                    idempotencyKey:=Guid.NewGuid().ToString())

            Return (Kind:=outcome.Kind.ToString(), Detail:=outcome.Kind.ToString())

        Catch ex As Exception
            Return (Kind:="Exception", Detail:=ex.GetType().Name & ": " & ex.Message)
        End Try

    End Function

    ' ------------------------------------------------------------------ box 5

    ''' <summary>Box 5: P4-01's ledger reconciliation passes after every return, asserted directly here.</summary>
    <TestMethod>
    Public Async Function RecordAsync_Committed_LedgerReconciles() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim received = Await ReceiveFullyAsync(productId, 8.000D, 2.0000D)

        Dim outcome As PurchaseReturnOutcome =
            Await _purchaseReturnService.RecordAsync(
                received.ReceiptId, NewReferenceNumber(), New List(Of RecordPurchaseReturnLineRequest) From {
                    New RecordPurchaseReturnLineRequest With {.ReceiptLineId = received.ReceiptLineId, .QuantityReturned = 3.000D, .Reason = "For reconciliation", .RemovesStock = True}
                },
                _procurementUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseReturnOutcomeKind.Created, outcome.Kind)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Dim discrepancies = Await LedgerReconciliation.FindDiscrepanciesAsync(connection)
            Dim found = discrepancies.FirstOrDefault(Function(d) d.ProductId = productId)
            Dim detail As String =
                If(found Is Nothing, String.Empty,
                   $"expected {found.ExpectedQuantity}, actual {found.ActualQuantity}, delta {found.Delta}")
            Assert.IsNull(found, $"Product {productId} must reconcile after a committed return: {detail}")
        End Using

    End Function

    ' ------------------------------------------------------------------ other outcomes

    <TestMethod>
    Public Async Function RecordAsync_UnknownReceipt_ReturnsReceiptNotFound() As Task

        Dim outcome As PurchaseReturnOutcome =
            Await _purchaseReturnService.RecordAsync(
                999999999, NewReferenceNumber(), New List(Of RecordPurchaseReturnLineRequest) From {
                    New RecordPurchaseReturnLineRequest With {.ReceiptLineId = 1, .QuantityReturned = 1.000D, .Reason = "n/a", .RemovesStock = True}
                },
                _procurementUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseReturnOutcomeKind.ReceiptNotFound, outcome.Kind)

    End Function

    <TestMethod>
    Public Async Function RecordAsync_UnknownLine_ReturnsLineNotFound() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim received = Await ReceiveFullyAsync(productId, 5.000D, 1.0000D)

        Dim outcome As PurchaseReturnOutcome =
            Await _purchaseReturnService.RecordAsync(
                received.ReceiptId, NewReferenceNumber(), New List(Of RecordPurchaseReturnLineRequest) From {
                    New RecordPurchaseReturnLineRequest With {.ReceiptLineId = 999999999, .QuantityReturned = 1.000D, .Reason = "n/a", .RemovesStock = True}
                },
                _procurementUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseReturnOutcomeKind.LineNotFound, outcome.Kind)
        Assert.AreEqual(999999999, outcome.OffendingReceiptLineId)

    End Function

    ''' <summary>A receipt line that genuinely exists, but on a DIFFERENT receipt, must be refused the same as one that does not exist - line identity is scoped to the receipt named in the route.</summary>
    <TestMethod>
    Public Async Function RecordAsync_LineBelongsToDifferentReceipt_ReturnsLineNotFound() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim receiptOne = Await ReceiveFullyAsync(productId, 5.000D, 1.0000D)
        Dim receiptTwo = Await ReceiveFullyAsync(Await CreateFixtureProductAsync(), 5.000D, 1.0000D)

        Dim outcome As PurchaseReturnOutcome =
            Await _purchaseReturnService.RecordAsync(
                receiptOne.ReceiptId, NewReferenceNumber(), New List(Of RecordPurchaseReturnLineRequest) From {
                    New RecordPurchaseReturnLineRequest With {.ReceiptLineId = receiptTwo.ReceiptLineId, .QuantityReturned = 1.000D, .Reason = "n/a", .RemovesStock = True}
                },
                _procurementUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseReturnOutcomeKind.LineNotFound, outcome.Kind)
        Assert.AreEqual(receiptTwo.ReceiptLineId, outcome.OffendingReceiptLineId)

    End Function

    <TestMethod>
    Public Async Function RecordAsync_DuplicateReferenceNumber_Refused() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim received = Await ReceiveFullyAsync(productId, 10.000D, 1.0000D)
        Dim referenceNumber As String = NewReferenceNumber()

        Dim firstOutcome As PurchaseReturnOutcome =
            Await _purchaseReturnService.RecordAsync(
                received.ReceiptId, referenceNumber, New List(Of RecordPurchaseReturnLineRequest) From {
                    New RecordPurchaseReturnLineRequest With {.ReceiptLineId = received.ReceiptLineId, .QuantityReturned = 3.000D, .Reason = "First", .RemovesStock = True}
                },
                _procurementUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(PurchaseReturnOutcomeKind.Created, firstOutcome.Kind)

        Dim secondOutcome As PurchaseReturnOutcome =
            Await _purchaseReturnService.RecordAsync(
                received.ReceiptId, referenceNumber, New List(Of RecordPurchaseReturnLineRequest) From {
                    New RecordPurchaseReturnLineRequest With {.ReceiptLineId = received.ReceiptLineId, .QuantityReturned = 2.000D, .Reason = "Second", .RemovesStock = True}
                },
                _procurementUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseReturnOutcomeKind.DuplicateReferenceNumber, secondOutcome.Kind)
        Assert.AreEqual(7.000D, Await ReadBalanceAsync(productId), "The refused duplicate must not have applied its own stock effect.")

    End Function

    ''' <summary>Stock already consumed by something else since receiving (a sale, an adjustment) is a distinct failure mode from the received-minus-prior-returns bound - reuses the same INSUFFICIENT_STOCK code P1-11 already established.</summary>
    <TestMethod>
    Public Async Function RecordAsync_StockAlreadyConsumedElsewhere_ReturnsInsufficientStock() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim received = Await ReceiveFullyAsync(productId, 10.000D, 1.0000D)

        ' Simulate 9 of the 10 received units already sold - the RECEIVING
        ' bound (10) is untouched, but only 1 unit remains physically on hand.
        Await SimulateStockConsumedAsync(productId, 9.000D)
        Assert.AreEqual(1.000D, Await ReadBalanceAsync(productId))

        Dim outcome As PurchaseReturnOutcome =
            Await _purchaseReturnService.RecordAsync(
                received.ReceiptId, NewReferenceNumber(), New List(Of RecordPurchaseReturnLineRequest) From {
                    New RecordPurchaseReturnLineRequest With {.ReceiptLineId = received.ReceiptLineId, .QuantityReturned = 5.000D, .Reason = "Within the receiving bound, but not on hand", .RemovesStock = True}
                },
                _procurementUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseReturnOutcomeKind.InsufficientStock, outcome.Kind)
        Assert.AreEqual(PurchaseReturnOutcome.InsufficientStockErrorCode, outcome.ErrorCode)
        Assert.AreEqual(1.000D, Await ReadBalanceAsync(productId), "A refused return must leave the balance untouched.")

    End Function

    ''' <summary>A repeated idempotency key replays the ORIGINAL committed return - never a second stock-out.</summary>
    <TestMethod>
    Public Async Function RecordAsync_RepeatedIdempotencyKey_ReplaysWithoutASecondStockEffect() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim received = Await ReceiveFullyAsync(productId, 6.000D, 1.5000D)
        Dim idempotencyKey As String = Guid.NewGuid().ToString()
        Dim referenceNumber As String = NewReferenceNumber()

        Dim lines As New List(Of RecordPurchaseReturnLineRequest) From {
            New RecordPurchaseReturnLineRequest With {.ReceiptLineId = received.ReceiptLineId, .QuantityReturned = 6.000D, .Reason = "Idempotency probe", .RemovesStock = True}
        }

        Dim firstOutcome As PurchaseReturnOutcome =
            Await _purchaseReturnService.RecordAsync(
                received.ReceiptId, referenceNumber, lines, _procurementUserId, Guid.NewGuid().ToString(), idempotencyKey)
        Assert.AreEqual(PurchaseReturnOutcomeKind.Created, firstOutcome.Kind)

        Dim secondOutcome As PurchaseReturnOutcome =
            Await _purchaseReturnService.RecordAsync(
                received.ReceiptId, referenceNumber, lines, _procurementUserId, Guid.NewGuid().ToString(), idempotencyKey)

        Assert.AreEqual(PurchaseReturnOutcomeKind.Replayed, secondOutcome.Kind)

        Dim replayed As PurchaseReturnResponse = JsonSerializer.Deserialize(Of PurchaseReturnResponse)(secondOutcome.ReplayPayload)
        Assert.AreEqual(firstOutcome.Response.Id, replayed.Id)
        Assert.AreEqual(0.000D, Await ReadBalanceAsync(productId), "A replayed request must never remove stock a second time.")

    End Function

    ''' <summary>
    ''' Fault injected after the audit insert and idempotency completion
    ''' write, still inside the open transaction, immediately before commit
    ''' - the P1-12/P2-08 shape. Every effect must roll back: the return
    ''' header, its line, the movement, the balance, and the audit row.
    ''' </summary>
    <TestMethod>
    Public Async Function RecordAsync_FaultInjectedBeforeCommit_RollsBackEveryEffect() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim received = Await ReceiveFullyAsync(productId, 7.000D, 2.0000D)
        Dim correlationId As String = Guid.NewGuid().ToString()
        Dim referenceNumber As String = NewReferenceNumber()

        Dim balanceBefore As Decimal = Await ReadBalanceAsync(productId)
        Dim auditCountBefore As Long = Await CountAuditRowsAsync(correlationId, "PurchaseReturnRecorded", "Success")

        Dim faultInjected As Boolean = False

        Await Assert.ThrowsExactlyAsync(Of InvalidOperationException)(
            Function() _purchaseReturnService.RecordAsync(
                received.ReceiptId, referenceNumber,
                New List(Of RecordPurchaseReturnLineRequest) From {
                    New RecordPurchaseReturnLineRequest With {.ReceiptLineId = received.ReceiptLineId, .QuantityReturned = 7.000D, .Reason = "Forced failure", .RemovesStock = True}
                },
                _procurementUserId, correlationId, Guid.NewGuid().ToString(),
                testOnlyFaultAfterAuditInsert:=Sub()
                                                   faultInjected = True
                                                   Throw New InvalidOperationException("P4-08 forced failure: after audit insert, before commit.")
                                               End Sub))

        Assert.IsTrue(faultInjected, "The fault-injection delegate must actually have fired for this proof to mean anything.")

        Assert.AreEqual(balanceBefore, Await ReadBalanceAsync(productId), "A rolled-back return must leave StockBalances untouched.")
        Assert.AreEqual(0L, Await CountMovementsAsync(correlationId, productId), "A rolled-back return must leave zero StockMovements rows.")
        Assert.AreEqual(0L, Await CountPurchaseReturnsAsync(referenceNumber), "A rolled-back return must leave zero PurchaseReturns rows - the reference number must be free for a real retry.")
        Assert.AreEqual(auditCountBefore, Await CountAuditRowsAsync(correlationId, "PurchaseReturnRecorded", "Success"), "A rolled-back return must leave zero new AuditLogs rows.")

    End Function

    ' -------------------------------------------------------------- HTTP shape

    ''' <summary>End-to-end smoke test: a real HTTP request through the PurchaseReturns.Manage-gated route produces the same committed effect the direct-service tests above prove.</summary>
    <TestMethod>
    Public Async Function RecordPurchaseReturn_ValidRequestOverHttp_Returns201AndCommits() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim received = Await ReceiveFullyAsync(productId, 5.000D, 1.5000D)

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementUsername)

            Dim request As New RecordPurchaseReturnRequest With {
                .ReferenceNumber = NewReferenceNumber(),
                .IdempotencyKey = Guid.NewGuid().ToString("d"),
                .Lines = New List(Of RecordPurchaseReturnLineRequest) From {
                    New RecordPurchaseReturnLineRequest With {.ReceiptLineId = received.ReceiptLineId, .QuantityReturned = 2.000D, .Reason = "HTTP smoke test", .RemovesStock = True}
                }
            }

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, $"/api/v1/receipts/{received.ReceiptId}/returns", token, request)

                Assert.AreEqual(
                    HttpStatusCode.Created, response.StatusCode,
                    "Body: " & Await response.Content.ReadAsStringAsync())

                Dim body As PurchaseReturnResponse = Await response.Content.ReadFromJsonAsync(Of PurchaseReturnResponse)()
                Assert.AreEqual("Approved", body.Status)
                Assert.AreEqual(1, body.Lines.Count)
                Assert.AreEqual(productId, body.Lines(0).ProductId)

            End Using

        End Using

        Assert.AreEqual(3.000D, Await ReadBalanceAsync(productId))

    End Function

    ''' <summary>ADR-014: a request naming the same ReceiptLineId twice is a shape error the controller refuses.</summary>
    <TestMethod>
    Public Async Function RecordPurchaseReturn_DuplicateReceiptLineIdInRequest_Returns400() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim received = Await ReceiveFullyAsync(productId, 10.000D, 1.0000D)

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementUsername)

            Dim request As New RecordPurchaseReturnRequest With {
                .ReferenceNumber = NewReferenceNumber(),
                .IdempotencyKey = Guid.NewGuid().ToString("d"),
                .Lines = New List(Of RecordPurchaseReturnLineRequest) From {
                    New RecordPurchaseReturnLineRequest With {.ReceiptLineId = received.ReceiptLineId, .QuantityReturned = 3.000D, .Reason = "First", .RemovesStock = True},
                    New RecordPurchaseReturnLineRequest With {.ReceiptLineId = received.ReceiptLineId, .QuantityReturned = 2.000D, .Reason = "Duplicate", .RemovesStock = True}
                }
            }

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, $"/api/v1/receipts/{received.ReceiptId}/returns", token, request)

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("VALIDATION_FAILED", body.ErrorCode)

            End Using

        End Using

    End Function

    ''' <summary>A missing reason is refused 400 before any connection is opened - PurchaseReturnLines.Reason is NOT NULL (0009).</summary>
    <TestMethod>
    Public Async Function RecordPurchaseReturn_MissingReason_Returns400() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim received = Await ReceiveFullyAsync(productId, 10.000D, 1.0000D)

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementUsername)

            Dim request As New RecordPurchaseReturnRequest With {
                .ReferenceNumber = NewReferenceNumber(),
                .IdempotencyKey = Guid.NewGuid().ToString("d"),
                .Lines = New List(Of RecordPurchaseReturnLineRequest) From {
                    New RecordPurchaseReturnLineRequest With {.ReceiptLineId = received.ReceiptLineId, .QuantityReturned = 3.000D, .Reason = "", .RemovesStock = True}
                }
            }

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, $"/api/v1/receipts/{received.ReceiptId}/returns", token, request)

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("VALIDATION_FAILED", body.ErrorCode)
                Assert.IsTrue(body.Errors.ContainsKey("lines[0].reason"))

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

    ' --------------------------------------------------------------- helpers (direct service)

    ''' <summary>Creates, submits, approves and fully receives a single-line order for <paramref name="productId"/>. Returns the committed receipt's Id and the one line's Id, ready for a purchase return.</summary>
    Private Async Function ReceiveFullyAsync(
        productId As Integer, orderedQuantity As Decimal, purchaseCost As Decimal) As Task(Of (ReceiptId As Integer, ReceiptLineId As Integer))

        Dim createOutcome As PurchaseOrderCreationOutcome =
            Await _purchaseOrderService.CreateAsync(
                _supplierId,
                New List(Of CreatePurchaseOrderLineRequest) From {
                    New CreatePurchaseOrderLineRequest With {.ProductId = productId, .OrderedQuantity = orderedQuantity, .PurchaseCost = purchaseCost}},
                _adminAUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(PurchaseOrderCreationOutcomeKind.Created, createOutcome.Kind, "Fixture creation must succeed.")

        Dim orderId As Integer = createOutcome.Response.Id
        Dim purchaseOrderLineId As Integer = createOutcome.Response.Lines(0).Id

        Dim submitOutcome = Await _purchaseOrderService.SubmitAsync(orderId, _adminAUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Success, submitOutcome.Kind, "Fixture submit must succeed.")

        Dim approveOutcome = Await _purchaseOrderService.ApproveAsync(orderId, _adminBUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Success, approveOutcome.Kind, "Fixture approve must succeed.")

        Dim receiveOutcome As ReceivingOutcome =
            Await _receivingService.ReceiveAsync(
                orderId, NewReferenceNumber(), New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = purchaseOrderLineId, .QuantityReceived = orderedQuantity, .Cost = purchaseCost}
                },
                _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(ReceivingOutcomeKind.Created, receiveOutcome.Kind, "Fixture receiving must succeed.")

        Return (ReceiptId:=receiveOutcome.Response.Id, ReceiptLineId:=receiveOutcome.Response.Lines(0).Id)

    End Function

    ''' <summary>Simulates stock consumed by something other than a return (a sale, an adjustment) between receiving and a later return attempt - a plain StockRepository decrement + movement, the same primitive P1-11 built.</summary>
    Private Async Function SimulateStockConsumedAsync(productId As Integer, quantity As Decimal) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using transaction As MySqlTransaction = Await connection.BeginTransactionAsync()

                Dim decrementResult = Await StockRepository.TryDecrementAsync(connection, transaction, productId, quantity)
                Assert.IsTrue(decrementResult.Succeeded, "Fixture stock-consumption simulation must succeed.")

                Await StockMovementWriter.WriteAsync(
                    connection, transaction, productId, -quantity,
                    decrementResult.QuantityBefore, decrementResult.QuantityAfter,
                    "P4-08 fixture: simulated sale", _adminAUserId, Guid.NewGuid().ToString())

                Await transaction.CommitAsync()

            End Using
        End Using

    End Function

    Private Async Function ExecuteAsync(connection As MySqlConnection, sql As String) As Task
        Using command As MySqlCommand = connection.CreateCommand()
            command.CommandText = sql
            Await command.ExecuteNonQueryAsync()
        End Using
    End Function

    ' --------------------------------------------------------------- fixtures

    Private Shared Function NewReferenceNumber() As String
        Return "P4-08-RET-" & Guid.NewGuid().ToString("N").Substring(0, 20)
    End Function

    Private Async Function CreateActiveSupplierAsync() As Task(Of Integer)

        Dim name As String = "P4-08 Supplier " & Guid.NewGuid().ToString("N").Substring(0, 12)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO Suppliers (Name, IsActive, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@name, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@name", name)
                Await command.ExecuteNonQueryAsync()
                Return CInt(command.LastInsertedId)
            End Using
        End Using

    End Function

    Private Async Function CreateFixtureProductAsync() As Task(Of Integer)

        Dim sku As String = FixtureProductSkuPrefix & Guid.NewGuid().ToString("N").Substring(0, 16)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO Products (Sku, Barcode, Name, Price, Cost, IsActive, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@sku, NULL, 'P4-08 Fixture Product', 9.0000, 4.0000, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@sku", sku)
                Await command.ExecuteNonQueryAsync()
                Return CInt(command.LastInsertedId)
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

    ' --------------------------------------------------------------- helpers (raw reads)

    Private Async Function ReadBalanceAsync(productId As Integer) As Task(Of Decimal)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT Quantity FROM StockBalances WHERE ProductId = @productId;"
                command.Parameters.AddWithValue("@productId", productId)
                Return CDec(Await command.ExecuteScalarAsync())
            End Using
        End Using

    End Function

    Private Async Function CountMovementsAsync(correlationId As String, productId As Integer) As Task(Of Long)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT COUNT(*) FROM StockMovements WHERE CorrelationId = @correlationId AND ProductId = @productId;"
                command.Parameters.AddWithValue("@correlationId", correlationId)
                command.Parameters.AddWithValue("@productId", productId)
                Return CLng(Await command.ExecuteScalarAsync())
            End Using
        End Using

    End Function

    Private Async Function CountPurchaseReturnsAsync(referenceNumber As String) As Task(Of Long)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM PurchaseReturns WHERE ReferenceNumber = @referenceNumber;"
                command.Parameters.AddWithValue("@referenceNumber", referenceNumber)
                Return CLng(Await command.ExecuteScalarAsync())
            End Using
        End Using

    End Function

    Private Async Function SumReturnedQuantityAsync(receiptLineId As Integer) As Task(Of Decimal)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT COALESCE(SUM(QuantityReturned), 0.000) FROM PurchaseReturnLines WHERE ReceiptLineId = @receiptLineId;"
                command.Parameters.AddWithValue("@receiptLineId", receiptLineId)
                Return CDec(Await command.ExecuteScalarAsync())
            End Using
        End Using

    End Function

    Private Async Function CountAuditRowsAsync(correlationId As String, action As String, result As String) As Task(Of Long)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT COUNT(*) FROM AuditLogs WHERE CorrelationId = @correlationId AND Action = @action AND Result = @result;"
                command.Parameters.AddWithValue("@correlationId", correlationId)
                command.Parameters.AddWithValue("@action", action)
                command.Parameters.AddWithValue("@result", result)
                Return CLng(Await command.ExecuteScalarAsync())
            End Using
        End Using

    End Function

End Class
