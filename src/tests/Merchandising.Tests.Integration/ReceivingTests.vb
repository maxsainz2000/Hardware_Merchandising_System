' Merchandising.Tests.Integration.ReceivingTests
'
' P4-05: POST /api/v1/receipts and ReceivingService.ReceiveAsync, proven
' against the real pinned MariaDB - spec section 11's "Goods received"
' atomic-result row (Receipt, receipt lines, stock-in movements, balance
' changes, audit event commit together).
'
' P4-06: partial receiving accumulating across SEPARATE receipts (distinct
' calls, distinct reference numbers/idempotency keys - never a replay).
' ReceivingService.ReceiveAsync needed no new production code for this - it
' already re-reads PurchaseOrderLines.ReceivedQuantity fresh, locked, on
' every call (GetLinesForUpdateAsync) and projects the incoming receipt onto
' that live value, so a second call naturally sees the first's accumulation.
' Box 7 below proves that is actually true, against the real database,
' rather than merely arguing it from the P4-05 design.
'
' Role gating (Receiving.Confirm resolves to the roles PolicyRegistry says
' it does) is AuthorizationMatrixTests' job. This file proves the BEHAVIOUR:
'
'   box 1   exactly one atomic stock increase per line, conditional update,
'           never read-then-write
'   box 2   status moves through CanTransition only - full quantity ->
'           FullyReceived, partial -> PartiallyReceived, mixed lines
'           independently
'   box 3   every non-receivable status is refused with the stable error
'           code PurchaseOrderTransitions names, asserted over Draft,
'           Submitted, Cancelled, Closed and FullyReceived - not spot-checked
'   box 4   a repeated idempotency key replays the original receipt, never a
'           second stock increase
'   box 5   all five effects commit together or not at all - the
'           P1-12/P2-08-shaped forced-failure rollback
'   box 6   the P4-01 ledger reconciliation passes after a committed receipt,
'           asserted directly in this file, not only by the suite-wide
'           fixture
'   box 7   P4-06: two, then three, separate receipts accumulate correctly
'           on PurchaseOrderLines.ReceivedQuantity; each writes its own
'           movement row and the ledger reconciles after every one, not only
'           the last; mixed lines stay independent across receipts too
'   box 8   P4-07: over-receiving is refused 409 with a stable code and no
'           partial trace, whether attempted in one shot or as a further
'           receipt against an already-closed line while the ORDER overall
'           is still receivable; and CK_PurchaseOrderLines_ReceivedQuantity
'           (not the API's own guard) is proven to be what actually stops
'           it, by calling the production repository method directly
'
' Fixture users, supplier and products are real, permanent rows - nothing
' here is torn down (ADR-013: no DELETE grant on Users, Suppliers or
' Products). Two distinct Admin fixtures are needed for the same reason
' PurchaseOrderApprovalTests needs them: approving an order requires an
' actor who did not request it (ADR-017 section 6).

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
Imports Merchandising.Domain.Procurement
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class ReceivingTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const FixturePassword As String = "P4-05 Fixture Passw0rd!"
    Private Const AdminAUsername As String = "p4_05_fixture_admin_a"
    Private Const AdminBUsername As String = "p4_05_fixture_admin_b"
    Private Const InventoryUsername As String = "p4_05_fixture_inventory"
    Private Const FixtureProductSkuPrefix As String = "p4_05_fixture_sku_"

    ''' <summary>ERROR 4025: CONSTRAINT ... failed - MariaDB 10.4's CHECK violation. Same constant PurchaseOrderSchemaTests defines for the schema-level proof at P3-02; this file re-proves it against the production repository method (P4-07).</summary>
    Private Const CheckConstraintFailedErrorNumber As Integer = 4025

    Private _factory As MerchandisingApiFactory
    Private _connectionFactory As ConnectionFactory
    Private _purchaseOrderService As PurchaseOrderService
    Private _receivingService As ReceivingService
    Private _supplierId As Integer
    Private _adminAUserId As Integer
    Private _adminBUserId As Integer
    Private _inventoryUserId As Integer

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _factory = New MerchandisingApiFactory()
        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        _purchaseOrderService = New PurchaseOrderService(_connectionFactory)
        _receivingService = New ReceivingService(_connectionFactory)

        _adminAUserId = Await EnsureFixtureUserAsync(AdminAUsername, "Admin")
        _adminBUserId = Await EnsureFixtureUserAsync(AdminBUsername, "Admin")
        _inventoryUserId = Await EnsureFixtureUserAsync(InventoryUsername, "InventoryClerk")
        _supplierId = Await CreateActiveSupplierAsync()

    End Function

    <TestCleanup>
    Public Sub TearDown()
        _factory?.Dispose()
    End Sub

    ' ------------------------------------------------------------------ box 1

    ''' <summary>
    ''' Box 1: a single-line receipt against a product with NO prior
    ''' StockBalances row (StockRepository.IncrementAsync's own upsert case)
    ''' produces exactly one StockMovements row and a matching balance,
    ''' verified by reading the raw stored rows rather than trusting the
    ''' response.
    ''' </summary>
    <TestMethod>
    Public Async Function ReceiveAsync_FreshProductNoPriorBalance_WritesOneMovementAndMatchingBalance() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim order As PurchaseOrderResponse = Await CreateApprovedOrderAsync({(productId, 10.000D, 2.5000D)})
        Dim correlationId As String = Guid.NewGuid().ToString()

        Dim outcome As ReceivingOutcome =
            Await _receivingService.ReceiveAsync(
                order.Id, NewReferenceNumber(), New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {
                        .PurchaseOrderLineId = order.Lines(0).Id, .QuantityReceived = 10.000D, .Cost = 2.6000D}
                },
                _inventoryUserId, correlationId, Guid.NewGuid().ToString())

        Assert.AreEqual(ReceivingOutcomeKind.Created, outcome.Kind)
        Assert.AreEqual("FullyReceived", outcome.Response.PurchaseOrderStatus)

        Dim balance As Decimal = Await ReadBalanceAsync(productId)
        Assert.AreEqual(10.000D, balance, "Balance must equal exactly what was received, from a standing start of no row at all.")

        Dim movementCount As Long = Await CountMovementsAsync(correlationId, productId)
        Assert.AreEqual(1L, movementCount, "Exactly one StockMovements row for this product/correlation, never more.")

        Dim movement = Await ReadSingleMovementAsync(correlationId, productId)
        Assert.AreEqual(10.000D, movement.Delta)
        Assert.AreEqual(0.000D, movement.QuantityBefore)
        Assert.AreEqual(10.000D, movement.QuantityAfter)

    End Function

    ''' <summary>Box 1: two lines in one receipt each write their own movement, against their own product's own balance.</summary>
    <TestMethod>
    Public Async Function ReceiveAsync_TwoLines_EachWritesItsOwnMovementAndBalance() As Task

        Dim productA As Integer = Await CreateFixtureProductAsync()
        Dim productB As Integer = Await CreateFixtureProductAsync()

        Dim order As PurchaseOrderResponse =
            Await CreateApprovedOrderAsync({(productA, 5.000D, 1.0000D), (productB, 8.000D, 2.0000D)})

        Dim correlationId As String = Guid.NewGuid().ToString()

        Dim outcome As ReceivingOutcome =
            Await _receivingService.ReceiveAsync(
                order.Id, NewReferenceNumber(),
                New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = order.Lines(0).Id, .QuantityReceived = 5.000D, .Cost = 1.1000D},
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = order.Lines(1).Id, .QuantityReceived = 8.000D, .Cost = 2.1000D}
                },
                _inventoryUserId, correlationId, Guid.NewGuid().ToString())

        Assert.AreEqual(ReceivingOutcomeKind.Created, outcome.Kind)
        Assert.AreEqual("FullyReceived", outcome.Response.PurchaseOrderStatus)
        Assert.HasCount(2, outcome.Response.Lines)

        Assert.AreEqual(5.000D, Await ReadBalanceAsync(productA))
        Assert.AreEqual(8.000D, Await ReadBalanceAsync(productB))
        Assert.AreEqual(1L, Await CountMovementsAsync(correlationId, productA))
        Assert.AreEqual(1L, Await CountMovementsAsync(correlationId, productB))

    End Function

    ' ------------------------------------------------------------------ box 2

    <TestMethod>
    Public Async Function ReceiveAsync_PartialQuantity_MovesOrderToPartiallyReceived() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim order As PurchaseOrderResponse = Await CreateApprovedOrderAsync({(productId, 10.000D, 2.5000D)})

        Dim outcome As ReceivingOutcome =
            Await _receivingService.ReceiveAsync(
                order.Id, NewReferenceNumber(), New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {
                        .PurchaseOrderLineId = order.Lines(0).Id, .QuantityReceived = 4.000D, .Cost = 2.5000D}
                },
                _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(ReceivingOutcomeKind.Created, outcome.Kind)
        Assert.AreEqual("PartiallyReceived", outcome.Response.PurchaseOrderStatus)

        Dim storedStatus As String = Await ReadOrderStatusAsync(order.Id)
        Assert.AreEqual("PartiallyReceived", storedStatus)

        Dim receivedQuantity As Decimal = Await ReadLineReceivedQuantityAsync(order.Lines(0).Id)
        Assert.AreEqual(4.000D, receivedQuantity)

    End Function

    ''' <summary>
    ''' Box 2: one line fully received and another partially in the SAME
    ''' receipt leaves the order PartiallyReceived - the projection in
    ''' ReceivingService only calls the order FullyReceived when EVERY line
    ''' is exactly at its ordered quantity.
    ''' </summary>
    <TestMethod>
    Public Async Function ReceiveAsync_MixedLinesOneFullOnePartial_StaysPartiallyReceived() As Task

        Dim productA As Integer = Await CreateFixtureProductAsync()
        Dim productB As Integer = Await CreateFixtureProductAsync()

        Dim order As PurchaseOrderResponse =
            Await CreateApprovedOrderAsync({(productA, 5.000D, 1.0000D), (productB, 8.000D, 2.0000D)})

        Dim outcome As ReceivingOutcome =
            Await _receivingService.ReceiveAsync(
                order.Id, NewReferenceNumber(),
                New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = order.Lines(0).Id, .QuantityReceived = 5.000D, .Cost = 1.0000D},
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = order.Lines(1).Id, .QuantityReceived = 3.000D, .Cost = 2.0000D}
                },
                _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(ReceivingOutcomeKind.Created, outcome.Kind)
        Assert.AreEqual("PartiallyReceived", outcome.Response.PurchaseOrderStatus, "One line still short must keep the order PartiallyReceived even though the other line closed exactly.")

    End Function

    ' ------------------------------------------------------------------ box 3

    ''' <summary>Box 3: Draft, Submitted, Cancelled, Closed and FullyReceived are all refused, each with the stable code PurchaseOrderTransitions names - not spot-checked.</summary>
    <TestMethod>
    Public Async Function ReceiveAsync_EveryNonReceivableStatus_RefusedWithStableCode() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()

        ' Draft
        Dim draftOutcome As PurchaseOrderCreationOutcome = Await CreateDraftDirectlyAsync(productId, 5.000D, 1.0000D)
        Await AssertRefusedAsync(draftOutcome.Response.Id, draftOutcome.Response.Lines(0).Id, PurchaseOrderTransitionErrors.InvalidTransition)

        ' Submitted
        Dim submittedOutcome As PurchaseOrderCreationOutcome = Await CreateDraftDirectlyAsync(productId, 5.000D, 1.0000D)
        Await _purchaseOrderService.SubmitAsync(submittedOutcome.Response.Id, _adminAUserId, Guid.NewGuid().ToString())
        Await AssertRefusedAsync(submittedOutcome.Response.Id, submittedOutcome.Response.Lines(0).Id, PurchaseOrderTransitionErrors.InvalidTransition)

        ' Cancelled
        Dim cancelledOutcome As PurchaseOrderCreationOutcome = Await CreateDraftDirectlyAsync(productId, 5.000D, 1.0000D)
        Await _purchaseOrderService.CancelAsync(cancelledOutcome.Response.Id, _adminAUserId, "P4-05 fixture", Guid.NewGuid().ToString())
        Await AssertRefusedAsync(cancelledOutcome.Response.Id, cancelledOutcome.Response.Lines(0).Id, PurchaseOrderTransitionErrors.Cancelled)

        ' Closed (short-closed after a partial receipt)
        Dim closedOrder As PurchaseOrderResponse = Await CreateApprovedOrderAsync({(productId, 5.000D, 1.0000D)})
        Await _receivingService.ReceiveAsync(
            closedOrder.Id, NewReferenceNumber(), New List(Of ReceiveGoodsLineRequest) From {
                New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = closedOrder.Lines(0).Id, .QuantityReceived = 2.000D, .Cost = 1.0000D}
            },
            _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Await _purchaseOrderService.CloseAsync(closedOrder.Id, _adminAUserId, "P4-05 fixture short-close", Guid.NewGuid().ToString())
        Await AssertRefusedAsync(closedOrder.Id, closedOrder.Lines(0).Id, PurchaseOrderTransitionErrors.Closed)

        ' FullyReceived
        Dim fullyReceivedOrder As PurchaseOrderResponse = Await CreateApprovedOrderAsync({(productId, 3.000D, 1.0000D)})
        Dim firstReceive As ReceivingOutcome =
            Await _receivingService.ReceiveAsync(
                fullyReceivedOrder.Id, NewReferenceNumber(), New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = fullyReceivedOrder.Lines(0).Id, .QuantityReceived = 3.000D, .Cost = 1.0000D}
                },
                _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual("FullyReceived", firstReceive.Response.PurchaseOrderStatus, "Fixture must actually reach FullyReceived for this sub-case to mean anything.")
        Await AssertRefusedAsync(fullyReceivedOrder.Id, fullyReceivedOrder.Lines(0).Id, PurchaseOrderTransitionErrors.FullyReceived)

    End Function

    Private Async Function AssertRefusedAsync(purchaseOrderId As Integer, purchaseOrderLineId As Integer, expectedCode As String) As Task

        Dim outcome As ReceivingOutcome =
            Await _receivingService.ReceiveAsync(
                purchaseOrderId, NewReferenceNumber(), New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = purchaseOrderLineId, .QuantityReceived = 1.000D, .Cost = 1.0000D}
                },
                _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(ReceivingOutcomeKind.Refused, outcome.Kind, $"Expected a refusal for purchase order {purchaseOrderId}.")
        Assert.AreEqual(expectedCode, outcome.ErrorCode)

    End Function

    ' ------------------------------------------------------------------ box 4

    ''' <summary>Box 4: a repeated idempotency key replays the ORIGINAL committed receipt - never a second stock increase.</summary>
    <TestMethod>
    Public Async Function ReceiveAsync_RepeatedIdempotencyKey_ReplaysWithoutASecondStockIncrease() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim order As PurchaseOrderResponse = Await CreateApprovedOrderAsync({(productId, 6.000D, 1.5000D)})
        Dim idempotencyKey As String = Guid.NewGuid().ToString()
        Dim referenceNumber As String = NewReferenceNumber()

        Dim lines As New List(Of ReceiveGoodsLineRequest) From {
            New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = order.Lines(0).Id, .QuantityReceived = 6.000D, .Cost = 1.6000D}
        }

        Dim firstOutcome As ReceivingOutcome =
            Await _receivingService.ReceiveAsync(
                order.Id, referenceNumber, lines, _inventoryUserId, Guid.NewGuid().ToString(), idempotencyKey)
        Assert.AreEqual(ReceivingOutcomeKind.Created, firstOutcome.Kind)

        Dim secondOutcome As ReceivingOutcome =
            Await _receivingService.ReceiveAsync(
                order.Id, referenceNumber, lines, _inventoryUserId, Guid.NewGuid().ToString(), idempotencyKey)

        Assert.AreEqual(ReceivingOutcomeKind.Replayed, secondOutcome.Kind)

        Dim replayed As ReceiptResponse = JsonSerializer.Deserialize(Of ReceiptResponse)(secondOutcome.ReplayPayload)
        Assert.AreEqual(firstOutcome.Response.Id, replayed.Id, "The replay must be the SAME receipt, byte for byte.")

        Assert.AreEqual(6.000D, Await ReadBalanceAsync(productId), "A replayed request must never increment stock a second time.")

        Dim receiptCount As Long = Await CountReceiptsAsync(referenceNumber)
        Assert.AreEqual(1L, receiptCount, "Exactly one Receipts row must exist for this reference number, ever.")

    End Function

    ' ------------------------------------------------------------------ box 5

    ''' <summary>
    ''' Box 5: fault injected after the audit insert and idempotency
    ''' completion write, still inside the open transaction, immediately
    ''' before commit - the same shape
    ''' PurchaseOrderApprovalTests.ApproveAsync_FaultInjectedBeforeCommit_...
    ''' uses for P1-12/P2-08. The unhandled exception propagating out of
    ''' ReceiveAsync is what proves the rollback: the receipt, its line, the
    ''' movement, the balance, the order line's ReceivedQuantity, the order's
    ''' Status and the audit row must ALL still be exactly where they were
    ''' before this call.
    ''' </summary>
    <TestMethod>
    Public Async Function ReceiveAsync_FaultInjectedBeforeCommit_RollsBackEveryEffect() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim order As PurchaseOrderResponse = Await CreateApprovedOrderAsync({(productId, 7.000D, 3.0000D)})
        Dim correlationId As String = Guid.NewGuid().ToString()
        Dim referenceNumber As String = NewReferenceNumber()

        Dim balanceBefore As Decimal = Await ReadBalanceAsync(productId)
        Dim auditCountBefore As Long = Await CountAuditRowsAsync(correlationId, "GoodsReceived", "Success")

        Dim faultInjected As Boolean = False

        Await Assert.ThrowsExactlyAsync(Of InvalidOperationException)(
            Function() _receivingService.ReceiveAsync(
                order.Id, referenceNumber,
                New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = order.Lines(0).Id, .QuantityReceived = 7.000D, .Cost = 3.1000D}
                },
                _inventoryUserId, correlationId, Guid.NewGuid().ToString(),
                testOnlyFaultAfterAuditInsert:=Sub()
                                                   faultInjected = True
                                                   Throw New InvalidOperationException("P4-05 forced failure: after audit insert, before commit.")
                                               End Sub))

        Assert.IsTrue(faultInjected, "The fault-injection delegate must actually have fired for this proof to mean anything.")

        Assert.AreEqual(balanceBefore, Await ReadBalanceAsync(productId), "A rolled-back receipt must leave StockBalances untouched.")
        Assert.AreEqual(0L, Await CountMovementsAsync(correlationId, productId), "A rolled-back receipt must leave zero StockMovements rows.")
        Assert.AreEqual(0L, Await CountReceiptsAsync(referenceNumber), "A rolled-back receipt must leave zero Receipts rows - the reference number must be free for a real retry.")
        Assert.AreEqual(0.000D, Await ReadLineReceivedQuantityAsync(order.Lines(0).Id), "A rolled-back receipt must leave PurchaseOrderLines.ReceivedQuantity untouched.")
        Assert.AreEqual("Approved", Await ReadOrderStatusAsync(order.Id), "A rolled-back receipt must leave the order's Status untouched.")
        Assert.AreEqual(auditCountBefore, Await CountAuditRowsAsync(correlationId, "GoodsReceived", "Success"), "A rolled-back receipt must leave zero new AuditLogs rows.")

    End Function

    ' ------------------------------------------------------------------ box 6

    ''' <summary>Box 6: P4-01's ledger reconciliation passes after a committed receipt, asserted directly here, not only by the suite-wide fixture.</summary>
    <TestMethod>
    Public Async Function ReceiveAsync_Committed_LedgerReconciles() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim order As PurchaseOrderResponse = Await CreateApprovedOrderAsync({(productId, 9.000D, 4.0000D)})

        Dim outcome As ReceivingOutcome =
            Await _receivingService.ReceiveAsync(
                order.Id, NewReferenceNumber(), New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = order.Lines(0).Id, .QuantityReceived = 9.000D, .Cost = 4.1000D}
                },
                _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(ReceivingOutcomeKind.Created, outcome.Kind)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Dim discrepancies = Await LedgerReconciliation.FindDiscrepanciesAsync(connection)
            Dim found = discrepancies.FirstOrDefault(Function(d) d.ProductId = productId)
            Dim detail As String =
                If(found Is Nothing, String.Empty,
                   $"expected {found.ExpectedQuantity}, actual {found.ActualQuantity}, delta {found.Delta}")
            Assert.IsNull(found, $"Product {productId} must reconcile after a committed receipt: {detail}")
        End Using

    End Function

    ' ------------------------------------------------------------------ box 7 (P4-06)

    ''' <summary>
    ''' P4-06 box 1: two SEPARATE POST-equivalent calls (distinct reference
    ''' numbers, idempotency keys and correlation ids - genuinely two
    ''' receipts, not a replay) summing exactly to the ordered quantity move
    ''' the order to FullyReceived, asserted on the STORED ReceivedQuantity,
    ''' not inferred from the status. No production code exists purely for
    ''' this - ReceivingService.ReceiveAsync already re-reads
    ''' PurchaseOrderLines.ReceivedQuantity fresh (GetLinesForUpdateAsync,
    ''' locked) on every call and projects onto that live value, so a second
    ''' receipt naturally sees the first's accumulation. This test proves
    ''' that is actually true against the real database, not merely argued.
    ''' </summary>
    <TestMethod>
    Public Async Function ReceiveAsync_TwoSequentialPartials_SumToOrderedAndMovesToFullyReceived() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim order As PurchaseOrderResponse = Await CreateApprovedOrderAsync({(productId, 10.000D, 2.0000D)})
        Dim lineId As Integer = order.Lines(0).Id

        Dim firstOutcome As ReceivingOutcome =
            Await _receivingService.ReceiveAsync(
                order.Id, NewReferenceNumber(), New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = lineId, .QuantityReceived = 6.000D, .Cost = 2.0000D}
                },
                _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(ReceivingOutcomeKind.Created, firstOutcome.Kind)
        Assert.AreEqual("PartiallyReceived", firstOutcome.Response.PurchaseOrderStatus)
        Assert.AreEqual(6.000D, Await ReadLineReceivedQuantityAsync(lineId), "After the first receipt, ReceivedQuantity must be exactly what THAT receipt sent.")

        Dim secondOutcome As ReceivingOutcome =
            Await _receivingService.ReceiveAsync(
                order.Id, NewReferenceNumber(), New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = lineId, .QuantityReceived = 4.000D, .Cost = 2.0000D}
                },
                _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(ReceivingOutcomeKind.Created, secondOutcome.Kind)
        Assert.AreEqual("FullyReceived", secondOutcome.Response.PurchaseOrderStatus)
        Assert.AreEqual(10.000D, Await ReadLineReceivedQuantityAsync(lineId), "The two receipts together must sum to exactly the ordered quantity, read from the stored column, not the status.")
        Assert.AreEqual("FullyReceived", Await ReadOrderStatusAsync(order.Id))

    End Function

    ''' <summary>P4-06 box 2: three receipts accumulate correctly, including a final one that exactly closes the line.</summary>
    <TestMethod>
    Public Async Function ReceiveAsync_ThreeReceipts_AccumulateCorrectlyIncludingAnExactClose() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim order As PurchaseOrderResponse = Await CreateApprovedOrderAsync({(productId, 15.000D, 1.0000D)})
        Dim lineId As Integer = order.Lines(0).Id

        Dim quantities As Decimal() = {5.000D, 4.000D, 6.000D}
        Dim runningTotal As Decimal = 0D

        For index As Integer = 0 To quantities.Length - 1

            Dim outcome As ReceivingOutcome =
                Await _receivingService.ReceiveAsync(
                    order.Id, NewReferenceNumber(), New List(Of ReceiveGoodsLineRequest) From {
                        New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = lineId, .QuantityReceived = quantities(index), .Cost = 1.0000D}
                    },
                    _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

            runningTotal += quantities(index)

            Assert.AreEqual(ReceivingOutcomeKind.Created, outcome.Kind, $"Receipt {index + 1} must succeed.")
            Assert.AreEqual(runningTotal, Await ReadLineReceivedQuantityAsync(lineId), $"After receipt {index + 1}, ReceivedQuantity must be the running total, not merely the last delta.")

            Dim expectedStatus As String = If(runningTotal = 15.000D, "FullyReceived", "PartiallyReceived")
            Assert.AreEqual(expectedStatus, outcome.Response.PurchaseOrderStatus, $"Receipt {index + 1} of 3.")

        Next

        Assert.AreEqual("FullyReceived", Await ReadOrderStatusAsync(order.Id), "The third receipt exactly closes the line at 5+4+6=15.")

    End Function

    ''' <summary>P4-06 box 3: each receipt writes its OWN StockMovements row (never one row updated in place - StockMovements is append-only, ADR-013), and the ledger reconciles after EVERY receipt, not only the last.</summary>
    <TestMethod>
    Public Async Function ReceiveAsync_TwoReceipts_EachWritesItsOwnMovementAndLedgerReconcilesAfterEach() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim order As PurchaseOrderResponse = Await CreateApprovedOrderAsync({(productId, 10.000D, 1.5000D)})
        Dim lineId As Integer = order.Lines(0).Id

        Dim firstCorrelationId As String = Guid.NewGuid().ToString()
        Await _receivingService.ReceiveAsync(
            order.Id, NewReferenceNumber(), New List(Of ReceiveGoodsLineRequest) From {
                New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = lineId, .QuantityReceived = 3.000D, .Cost = 1.5000D}
            },
            _inventoryUserId, firstCorrelationId, Guid.NewGuid().ToString())

        Assert.AreEqual(1L, Await CountMovementsAsync(firstCorrelationId, productId), "The first receipt's own movement row.")
        Await AssertReconcilesAsync(productId, "after the first receipt")

        Dim secondCorrelationId As String = Guid.NewGuid().ToString()
        Await _receivingService.ReceiveAsync(
            order.Id, NewReferenceNumber(), New List(Of ReceiveGoodsLineRequest) From {
                New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = lineId, .QuantityReceived = 7.000D, .Cost = 1.5000D}
            },
            _inventoryUserId, secondCorrelationId, Guid.NewGuid().ToString())

        Assert.AreEqual(1L, Await CountMovementsAsync(secondCorrelationId, productId), "The second receipt's own movement row, distinct from the first.")
        Assert.AreEqual(1L, Await CountMovementsAsync(firstCorrelationId, productId), "The first receipt's movement row must still be there, unedited - StockMovements is append-only.")
        Await AssertReconcilesAsync(productId, "after the second receipt")

        Assert.AreEqual(10.000D, Await ReadBalanceAsync(productId), "Balance must be the SUM of both movements.")

    End Function

    ''' <summary>P4-06 box 4: one line closed on the FIRST of two receipts and the other only closed on the second - the order stays PartiallyReceived until the second receipt, and the already-closed line's ReceivedQuantity is untouched by the second receipt.</summary>
    <TestMethod>
    Public Async Function ReceiveAsync_MixedLinesAcrossTwoReceipts_StaysPartiallyReceivedUntilBothClose() As Task

        Dim productA As Integer = Await CreateFixtureProductAsync()
        Dim productB As Integer = Await CreateFixtureProductAsync()

        Dim order As PurchaseOrderResponse =
            Await CreateApprovedOrderAsync({(productA, 5.000D, 1.0000D), (productB, 8.000D, 2.0000D)})
        Dim lineA As Integer = order.Lines(0).Id
        Dim lineB As Integer = order.Lines(1).Id

        ' Receipt 1: line A closes completely, line B only partially.
        Dim firstOutcome As ReceivingOutcome =
            Await _receivingService.ReceiveAsync(
                order.Id, NewReferenceNumber(),
                New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = lineA, .QuantityReceived = 5.000D, .Cost = 1.0000D},
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = lineB, .QuantityReceived = 3.000D, .Cost = 2.0000D}
                },
                _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual("PartiallyReceived", firstOutcome.Response.PurchaseOrderStatus, "Line B is still short, so the order must not be FullyReceived even though line A closed exactly.")
        Assert.AreEqual(5.000D, Await ReadLineReceivedQuantityAsync(lineA))
        Assert.AreEqual(3.000D, Await ReadLineReceivedQuantityAsync(lineB))

        ' Receipt 2: only line B, closing the remaining 5.
        Dim secondOutcome As ReceivingOutcome =
            Await _receivingService.ReceiveAsync(
                order.Id, NewReferenceNumber(),
                New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = lineB, .QuantityReceived = 5.000D, .Cost = 2.0000D}
                },
                _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(ReceivingOutcomeKind.Created, secondOutcome.Kind)
        Assert.AreEqual("FullyReceived", secondOutcome.Response.PurchaseOrderStatus)
        Assert.AreEqual(5.000D, Await ReadLineReceivedQuantityAsync(lineA), "Line A must be untouched by a receipt that names only line B.")
        Assert.AreEqual(8.000D, Await ReadLineReceivedQuantityAsync(lineB))

    End Function

    Private Async Function AssertReconcilesAsync(productId As Integer, whenLabel As String) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Dim discrepancies = Await LedgerReconciliation.FindDiscrepanciesAsync(connection)
            Dim found = discrepancies.FirstOrDefault(Function(d) d.ProductId = productId)
            Dim detail As String =
                If(found Is Nothing, String.Empty,
                   $"expected {found.ExpectedQuantity}, actual {found.ActualQuantity}, delta {found.Delta}")
            Assert.IsNull(found, $"Product {productId} must reconcile {whenLabel}: {detail}")
        End Using

    End Function

    ' ------------------------------------------------------------------ box 8 (P4-07)

    ''' <summary>P4-07 box 1: a single receipt exceeding the ordered quantity is refused 409 with the stable RECEIPT_QUANTITY_EXCEEDS_ORDERED code, and leaves no trace at all - checked before the receipt header is ever inserted.</summary>
    <TestMethod>
    Public Async Function ReceiveAsync_QuantityExceedsOrdered_Refused409WithNoPartialTrace() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim order As PurchaseOrderResponse = Await CreateApprovedOrderAsync({(productId, 10.000D, 1.0000D)})
        Dim lineId As Integer = order.Lines(0).Id
        Dim correlationId As String = Guid.NewGuid().ToString()
        Dim referenceNumber As String = NewReferenceNumber()

        Dim outcome As ReceivingOutcome =
            Await _receivingService.ReceiveAsync(
                order.Id, referenceNumber, New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = lineId, .QuantityReceived = 10.001D, .Cost = 1.0000D}
                },
                _inventoryUserId, correlationId, Guid.NewGuid().ToString())

        Assert.AreEqual(ReceivingOutcomeKind.OverReceived, outcome.Kind)
        Assert.AreEqual(lineId, outcome.OffendingPurchaseOrderLineId)
        Assert.AreEqual(ReceivingOutcome.OverReceivedErrorCode, outcome.ErrorCode)

        Assert.AreEqual(0L, Await CountReceiptsAsync(referenceNumber), "No Receipts row.")
        Assert.AreEqual(0L, Await CountMovementsAsync(correlationId, productId), "No StockMovements row.")
        Assert.AreEqual(0D, Await ReadBalanceOrZeroAsync(productId), "No balance change.")
        Assert.AreEqual(0.000D, Await ReadLineReceivedQuantityAsync(lineId), "ReceivedQuantity untouched.")
        Assert.AreEqual("Approved", Await ReadOrderStatusAsync(order.Id), "Order status untouched.")

    End Function

    ''' <summary>
    ''' P4-07 box 2: a THIRD receipt against a line that is already fully
    ''' received is refused with the SAME OverReceivedErrorCode - even
    ''' though the ORDER as a whole is still PartiallyReceived (a second
    ''' line is deliberately left far from done), so PurchaseOrderTransitions.
    ''' CanTransition would happily allow a ReceivePartially action here.
    ''' This is exactly the case a status-level refusal cannot catch, and
    ''' the reason OverReceived is a distinct kind from Refused.
    ''' </summary>
    <TestMethod>
    Public Async Function ReceiveAsync_ThirdReceiptOnFullyReceivedLine_RefusedWithSameCode() As Task

        Dim closingProductId As Integer = Await CreateFixtureProductAsync()
        Dim openProductId As Integer = Await CreateFixtureProductAsync()

        Dim order As PurchaseOrderResponse =
            Await CreateApprovedOrderAsync({(closingProductId, 5.000D, 1.0000D), (openProductId, 100.000D, 1.0000D)})
        Dim closingLineId As Integer = order.Lines(0).Id
        Dim openLineId As Integer = order.Lines(1).Id

        ' Receipt 1: partial on both lines.
        Dim firstOutcome As ReceivingOutcome =
            Await _receivingService.ReceiveAsync(
                order.Id, NewReferenceNumber(),
                New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = closingLineId, .QuantityReceived = 3.000D, .Cost = 1.0000D},
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = openLineId, .QuantityReceived = 1.000D, .Cost = 1.0000D}
                },
                _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual("PartiallyReceived", firstOutcome.Response.PurchaseOrderStatus)

        ' Receipt 2: closes the closing line exactly (3 + 2 = 5); the open
        ' line is nowhere near its ordered 100, so the order stays Partial.
        Dim secondOutcome As ReceivingOutcome =
            Await _receivingService.ReceiveAsync(
                order.Id, NewReferenceNumber(), New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = closingLineId, .QuantityReceived = 2.000D, .Cost = 1.0000D}
                },
                _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual("PartiallyReceived", secondOutcome.Response.PurchaseOrderStatus, "The order overall must still be PartiallyReceived - the OPEN line is nowhere near done.")
        Assert.AreEqual(5.000D, Await ReadLineReceivedQuantityAsync(closingLineId))

        ' Receipt 3: any further quantity on the now-fully-received closing
        ' line must be refused - the order is receivable (CanTransition
        ' would allow it), so only the line-level check can catch this.
        Dim thirdCorrelationId As String = Guid.NewGuid().ToString()
        Dim thirdReferenceNumber As String = NewReferenceNumber()

        Dim thirdOutcome As ReceivingOutcome =
            Await _receivingService.ReceiveAsync(
                order.Id, thirdReferenceNumber, New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = closingLineId, .QuantityReceived = 1.000D, .Cost = 1.0000D}
                },
                _inventoryUserId, thirdCorrelationId, Guid.NewGuid().ToString())

        Assert.AreEqual(ReceivingOutcomeKind.OverReceived, thirdOutcome.Kind)
        Assert.AreEqual(ReceivingOutcome.OverReceivedErrorCode, thirdOutcome.ErrorCode, "Same stable code as a single-receipt over-receive.")
        Assert.AreEqual(closingLineId, thirdOutcome.OffendingPurchaseOrderLineId)
        Assert.AreEqual(5.000D, Await ReadLineReceivedQuantityAsync(closingLineId), "The third, refused receipt must not have moved ReceivedQuantity at all.")
        Assert.AreEqual(0L, Await CountReceiptsAsync(thirdReferenceNumber))
        Assert.AreEqual(0L, Await CountMovementsAsync(thirdCorrelationId, closingProductId))

    End Function

    ''' <summary>
    ''' P4-07 box 3: THE DATABASE, NOT THE API GUARD, IS WHAT ACTUALLY STOPS
    ''' AN OVER-RECEIPT. Calls PurchaseOrderRepository.IncrementReceivedQuantityAsync
    ''' DIRECTLY - the exact method ReceivingService's step 6 calls - with an
    ''' over-limit quantity, bypassing ReceivingService.ReceiveAsync's step 4
    ''' guard entirely. If this test failed to throw, the API-only guard
    ''' would be "one deployment away from useless" (the card's own words):
    ''' proof that CK_PurchaseOrderLines_ReceivedQuantity (P3-02) is a real,
    ''' independent backstop, not merely documented as one.
    ''' </summary>
    <TestMethod>
    Public Async Function IncrementReceivedQuantityAsync_OverLimit_RefusedByTheDatabase() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim order As PurchaseOrderResponse = Await CreateApprovedOrderAsync({(productId, 10.000D, 1.0000D)})
        Dim lineId As Integer = order.Lines(0).Id

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using transaction As MySqlTransaction = Await connection.BeginTransactionAsync()

                Dim ex As MySqlException =
                    Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                        Function() PurchaseOrderRepository.IncrementReceivedQuantityAsync(
                            connection, transaction, lineId, 10.001D))

                Console.WriteLine($"P4-07 over-receive via the production repository method -> ERROR {ex.Number}")
                Assert.AreEqual(CheckConstraintFailedErrorNumber, ex.Number,
                    "CK_PurchaseOrderLines_ReceivedQuantity, not the API guard, must be what refuses this.")

                Await transaction.RollbackAsync()

            End Using
        End Using

        Assert.AreEqual(0.000D, Await ReadLineReceivedQuantityAsync(lineId), "The rolled-back over-limit attempt must leave ReceivedQuantity untouched.")

    End Function

    ' ------------------------------------------------------------------ other refusals

    <TestMethod>
    Public Async Function ReceiveAsync_UnknownOrder_ReturnsPurchaseOrderNotFound() As Task

        Dim outcome As ReceivingOutcome =
            Await _receivingService.ReceiveAsync(
                999999999, NewReferenceNumber(), New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = 1, .QuantityReceived = 1.000D, .Cost = 1.0000D}
                },
                _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(ReceivingOutcomeKind.PurchaseOrderNotFound, outcome.Kind)

    End Function

    <TestMethod>
    Public Async Function ReceiveAsync_UnknownLine_ReturnsLineNotFound() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim order As PurchaseOrderResponse = Await CreateApprovedOrderAsync({(productId, 5.000D, 1.0000D)})

        Dim outcome As ReceivingOutcome =
            Await _receivingService.ReceiveAsync(
                order.Id, NewReferenceNumber(), New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = 999999999, .QuantityReceived = 1.000D, .Cost = 1.0000D}
                },
                _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(ReceivingOutcomeKind.LineNotFound, outcome.Kind)
        Assert.AreEqual(999999999, outcome.OffendingPurchaseOrderLineId)

    End Function

    ''' <summary>A line that genuinely exists, but on a DIFFERENT order, must be refused the same as a line that does not exist at all - identity is scoped to the order being received against.</summary>
    <TestMethod>
    Public Async Function ReceiveAsync_LineBelongsToDifferentOrder_ReturnsLineNotFound() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim orderOne As PurchaseOrderResponse = Await CreateApprovedOrderAsync({(productId, 5.000D, 1.0000D)})
        Dim orderTwo As PurchaseOrderResponse = Await CreateApprovedOrderAsync({(productId, 5.000D, 1.0000D)})

        Dim outcome As ReceivingOutcome =
            Await _receivingService.ReceiveAsync(
                orderOne.Id, NewReferenceNumber(), New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = orderTwo.Lines(0).Id, .QuantityReceived = 1.000D, .Cost = 1.0000D}
                },
                _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(ReceivingOutcomeKind.LineNotFound, outcome.Kind)
        Assert.AreEqual(orderTwo.Lines(0).Id, outcome.OffendingPurchaseOrderLineId)

    End Function

    <TestMethod>
    Public Async Function ReceiveAsync_DuplicateReferenceNumber_Refused() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim orderOne As PurchaseOrderResponse = Await CreateApprovedOrderAsync({(productId, 5.000D, 1.0000D)})
        Dim orderTwo As PurchaseOrderResponse = Await CreateApprovedOrderAsync({(productId, 5.000D, 1.0000D)})
        Dim referenceNumber As String = NewReferenceNumber()

        Dim firstOutcome As ReceivingOutcome =
            Await _receivingService.ReceiveAsync(
                orderOne.Id, referenceNumber, New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = orderOne.Lines(0).Id, .QuantityReceived = 5.000D, .Cost = 1.0000D}
                },
                _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(ReceivingOutcomeKind.Created, firstOutcome.Kind)

        Dim secondOutcome As ReceivingOutcome =
            Await _receivingService.ReceiveAsync(
                orderTwo.Id, referenceNumber, New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = orderTwo.Lines(0).Id, .QuantityReceived = 5.000D, .Cost = 1.0000D}
                },
                _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(ReceivingOutcomeKind.DuplicateReferenceNumber, secondOutcome.Kind)
        Assert.AreEqual("Approved", Await ReadOrderStatusAsync(orderTwo.Id), "A refused duplicate-reference receipt must leave the second order's status untouched.")

    End Function

    ' -------------------------------------------------------------- HTTP shape

    ''' <summary>End-to-end smoke test: a real HTTP request through the Receiving.Confirm-gated route produces the same committed effect the direct-service tests above prove.</summary>
    <TestMethod>
    Public Async Function ReceiveGoods_ValidRequestOverHttp_Returns201AndCommits() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim order As PurchaseOrderResponse = Await CreateApprovedOrderAsync({(productId, 4.000D, 1.2500D)})

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, InventoryUsername)

            Dim request As New ReceiveGoodsRequest With {
                .PurchaseOrderId = order.Id,
                .ReferenceNumber = NewReferenceNumber(),
                .IdempotencyKey = Guid.NewGuid().ToString("d"),
                .Lines = New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = order.Lines(0).Id, .QuantityReceived = 4.000D, .Cost = 1.3000D}
                }
            }

            Using response As HttpResponseMessage = Await SendAsync(client, HttpMethod.Post, "/api/v1/receipts", token, request)

                Assert.AreEqual(
                    HttpStatusCode.Created, response.StatusCode,
                    "Body: " & Await response.Content.ReadAsStringAsync())

                Dim body As ReceiptResponse = Await response.Content.ReadFromJsonAsync(Of ReceiptResponse)()
                Assert.AreEqual("FullyReceived", body.PurchaseOrderStatus)
                Assert.HasCount(1, body.Lines)
                Assert.AreEqual(productId, body.Lines(0).ProductId)

            End Using

        End Using

        Assert.AreEqual(4.000D, Await ReadBalanceAsync(productId))

    End Function

    ''' <summary>P4-06, over the live authorized HTTP path rather than the direct service: two separate POST /api/v1/receipts calls from the SAME Receiving.Confirm-authorized session accumulate to FullyReceived, proving accumulation is not an artifact of calling ReceiveAsync directly.</summary>
    <TestMethod>
    Public Async Function ReceiveGoods_TwoSequentialReceiptsOverHttp_AccumulateToFullyReceived() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim order As PurchaseOrderResponse = Await CreateApprovedOrderAsync({(productId, 10.000D, 1.0000D)})

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, InventoryUsername)

            Dim firstRequest As New ReceiveGoodsRequest With {
                .PurchaseOrderId = order.Id,
                .ReferenceNumber = NewReferenceNumber(),
                .IdempotencyKey = Guid.NewGuid().ToString("d"),
                .Lines = New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = order.Lines(0).Id, .QuantityReceived = 6.000D, .Cost = 1.0000D}
                }
            }

            Using firstResponse As HttpResponseMessage = Await SendAsync(client, HttpMethod.Post, "/api/v1/receipts", token, firstRequest)
                Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode, "Body: " & Await firstResponse.Content.ReadAsStringAsync())
                Dim firstBody As ReceiptResponse = Await firstResponse.Content.ReadFromJsonAsync(Of ReceiptResponse)()
                Assert.AreEqual("PartiallyReceived", firstBody.PurchaseOrderStatus)
            End Using

            Dim secondRequest As New ReceiveGoodsRequest With {
                .PurchaseOrderId = order.Id,
                .ReferenceNumber = NewReferenceNumber(),
                .IdempotencyKey = Guid.NewGuid().ToString("d"),
                .Lines = New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = order.Lines(0).Id, .QuantityReceived = 4.000D, .Cost = 1.0000D}
                }
            }

            Using secondResponse As HttpResponseMessage = Await SendAsync(client, HttpMethod.Post, "/api/v1/receipts", token, secondRequest)
                Assert.AreEqual(HttpStatusCode.Created, secondResponse.StatusCode, "Body: " & Await secondResponse.Content.ReadAsStringAsync())
                Dim secondBody As ReceiptResponse = Await secondResponse.Content.ReadFromJsonAsync(Of ReceiptResponse)()
                Assert.AreEqual("FullyReceived", secondBody.PurchaseOrderStatus)
            End Using

        End Using

        Assert.AreEqual(10.000D, Await ReadBalanceAsync(productId))
        Assert.AreEqual(10.000D, Await ReadLineReceivedQuantityAsync(order.Lines(0).Id))

    End Function

    ''' <summary>ADR-014: a request naming the same PurchaseOrderLineId twice is a shape error the controller refuses, never something the transaction has to sort out.</summary>
    <TestMethod>
    Public Async Function ReceiveGoods_DuplicatePurchaseOrderLineIdInRequest_Returns400() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim order As PurchaseOrderResponse = Await CreateApprovedOrderAsync({(productId, 10.000D, 1.0000D)})

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, InventoryUsername)

            Dim request As New ReceiveGoodsRequest With {
                .PurchaseOrderId = order.Id,
                .ReferenceNumber = NewReferenceNumber(),
                .IdempotencyKey = Guid.NewGuid().ToString("d"),
                .Lines = New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = order.Lines(0).Id, .QuantityReceived = 3.000D, .Cost = 1.0000D},
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = order.Lines(0).Id, .QuantityReceived = 2.000D, .Cost = 1.0000D}
                }
            }

            Using response As HttpResponseMessage = Await SendAsync(client, HttpMethod.Post, "/api/v1/receipts", token, request)

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("VALIDATION_FAILED", body.ErrorCode)

            End Using

        End Using

    End Function

    ''' <summary>ADR-004.1: an over-scale quantity is refused 400 before any connection is opened - never silently rounded.</summary>
    <TestMethod>
    Public Async Function ReceiveGoods_OverScaleQuantity_Returns400() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim order As PurchaseOrderResponse = Await CreateApprovedOrderAsync({(productId, 10.000D, 1.0000D)})

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, InventoryUsername)

            Dim request As New ReceiveGoodsRequest With {
                .PurchaseOrderId = order.Id,
                .ReferenceNumber = NewReferenceNumber(),
                .IdempotencyKey = Guid.NewGuid().ToString("d"),
                .Lines = New List(Of ReceiveGoodsLineRequest) From {
                    New ReceiveGoodsLineRequest With {.PurchaseOrderLineId = order.Lines(0).Id, .QuantityReceived = 1.9999D, .Cost = 1.0000D}
                }
            }

            Using response As HttpResponseMessage = Await SendAsync(client, HttpMethod.Post, "/api/v1/receipts", token, request)

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("VALIDATION_FAILED", body.ErrorCode)
                Assert.IsTrue(body.Errors.ContainsKey("lines[0].quantityReceived"))

            End Using

        End Using

        Assert.AreEqual(0.000D, Await ReadBalanceOrZeroAsync(productId), "A refused request must never reach a bound SQL parameter, let alone change the balance.")

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

    ''' <summary>Creates, submits and approves (by a DIFFERENT admin than the requester) a multi-line order. Returns the committed order, whose Lines carry the real, server-assigned PurchaseOrderLineIds this card's receiving calls address.</summary>
    Private Async Function CreateApprovedOrderAsync(
        lineSpecs As IEnumerable(Of (ProductId As Integer, OrderedQuantity As Decimal, PurchaseCost As Decimal))) As Task(Of PurchaseOrderResponse)

        Dim createOutcome As PurchaseOrderCreationOutcome =
            Await CreateDraftDirectlyAsync(lineSpecs)

        Dim orderId As Integer = createOutcome.Response.Id

        Dim submitOutcome As PurchaseOrderTransitionOutcome =
            Await _purchaseOrderService.SubmitAsync(orderId, _adminAUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Success, submitOutcome.Kind, "Fixture submit must succeed.")

        Dim approveOutcome As PurchaseOrderTransitionOutcome =
            Await _purchaseOrderService.ApproveAsync(orderId, _adminBUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Success, approveOutcome.Kind, "Fixture approve must succeed.")

        Return approveOutcome.Response

    End Function

    Private Async Function CreateDraftDirectlyAsync(
        lineSpecs As IEnumerable(Of (ProductId As Integer, OrderedQuantity As Decimal, PurchaseCost As Decimal))) As Task(Of PurchaseOrderCreationOutcome)

        Dim lines As New List(Of CreatePurchaseOrderLineRequest)
        For Each spec In lineSpecs
            lines.Add(New CreatePurchaseOrderLineRequest With {
                .ProductId = spec.ProductId, .OrderedQuantity = spec.OrderedQuantity, .PurchaseCost = spec.PurchaseCost})
        Next

        Dim outcome As PurchaseOrderCreationOutcome =
            Await _purchaseOrderService.CreateAsync(
                _supplierId, lines, _adminAUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseOrderCreationOutcomeKind.Created, outcome.Kind, "Fixture creation must succeed.")
        Return outcome

    End Function

    ''' <summary>Single-line convenience overload for the "every non-receivable status" fixtures.</summary>
    Private Async Function CreateDraftDirectlyAsync(
        productId As Integer, orderedQuantity As Decimal, purchaseCost As Decimal) As Task(Of PurchaseOrderCreationOutcome)

        Return Await CreateDraftDirectlyAsync({(productId, orderedQuantity, purchaseCost)})

    End Function

    ' --------------------------------------------------------------- fixtures

    Private Shared Function NewReferenceNumber() As String
        Return "P4-05-GRN-" & Guid.NewGuid().ToString("N").Substring(0, 20)
    End Function

    Private Async Function CreateActiveSupplierAsync() As Task(Of Integer)

        Dim name As String = "P4-05 Supplier " & Guid.NewGuid().ToString("N").Substring(0, 12)

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

    ''' <summary>
    ''' A brand-new active product per call, with deliberately NO StockBalances
    ''' row seeded - the "fresh product, never yet balanced" case
    ''' StockRepository.IncrementAsync's upsert exists for. A product with no
    ''' movements and no balance row already reconciles (0 = 0), so this
    ''' needs no compensating-movement dance the way a nonzero-baseline
    ''' fixture would.
    ''' </summary>
    Private Async Function CreateFixtureProductAsync() As Task(Of Integer)

        Dim sku As String = FixtureProductSkuPrefix & Guid.NewGuid().ToString("N").Substring(0, 16)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO Products (Sku, Barcode, Name, Price, Cost, IsActive, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@sku, NULL, 'P4-05 Fixture Product', 9.0000, 4.0000, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
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

    ''' <summary>Same as ReadBalanceAsync, but 0 rather than an error when no StockBalances row exists at all - a refused request may never have created one.</summary>
    Private Async Function ReadBalanceOrZeroAsync(productId As Integer) As Task(Of Decimal)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT Quantity FROM StockBalances WHERE ProductId = @productId;"
                command.Parameters.AddWithValue("@productId", productId)
                Dim result As Object = Await command.ExecuteScalarAsync()
                Return If(result Is Nothing, 0D, CDec(result))
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

    Private Async Function ReadSingleMovementAsync(correlationId As String, productId As Integer) As Task(Of (Delta As Decimal, QuantityBefore As Decimal, QuantityAfter As Decimal))

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT Delta, QuantityBefore, QuantityAfter FROM StockMovements " &
                    " WHERE CorrelationId = @correlationId AND ProductId = @productId;"
                command.Parameters.AddWithValue("@correlationId", correlationId)
                command.Parameters.AddWithValue("@productId", productId)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync()
                    Await reader.ReadAsync()
                    Return (Delta:=reader.GetDecimal(0), QuantityBefore:=reader.GetDecimal(1), QuantityAfter:=reader.GetDecimal(2))
                End Using
            End Using
        End Using

    End Function

    Private Async Function CountReceiptsAsync(referenceNumber As String) As Task(Of Long)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM Receipts WHERE ReferenceNumber = @referenceNumber;"
                command.Parameters.AddWithValue("@referenceNumber", referenceNumber)
                Return CLng(Await command.ExecuteScalarAsync())
            End Using
        End Using

    End Function

    Private Async Function ReadOrderStatusAsync(orderId As Integer) As Task(Of String)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT Status FROM PurchaseOrders WHERE Id = @id;"
                command.Parameters.AddWithValue("@id", orderId)
                Return CStr(Await command.ExecuteScalarAsync())
            End Using
        End Using

    End Function

    Private Async Function ReadLineReceivedQuantityAsync(purchaseOrderLineId As Integer) As Task(Of Decimal)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT ReceivedQuantity FROM PurchaseOrderLines WHERE Id = @id;"
                command.Parameters.AddWithValue("@id", purchaseOrderLineId)
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
