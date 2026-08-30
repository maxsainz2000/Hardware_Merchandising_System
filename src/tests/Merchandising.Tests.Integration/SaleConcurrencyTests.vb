' Merchandising.Tests.Integration.SaleConcurrencyTests
'
' P5-08: the Phase 5 exit criterion "negative stock impossible under
' concurrent load," fired rather than argued - the same rule tasks.md has
' held since Phase 3: a sentence in an evidence file is not a check.
'
' P4-15's own closing section named exactly what it did NOT claim: "Sale-vs-
' receive and sale-vs-adjust are Phase 5's transaction and Phase 5's to
' prove." This file is that proof, in the P1-13 / P4-08 / P4-15 "launch every
' task, then await" mould, applied to SaleService.CompleteAsync - the one
' command among the three that ALSO requires an Open CashierSession
' (spec section 10.3), so its fixture needs a POOL of cashiers, each holding
' its own session, rather than the single actor every earlier concurrency
' file could get away with: two sales from the SAME user would serialize on
' CashierSessionRepository.GetOpenForUpdateByUserAsync's row lock, which
' would be testing session-row contention, not the StockBalances race this
' criterion is actually about.
'
'   box 1   N SIMULTANEOUS SALES OF THE LAST REMAINING UNIT. A product
'           holding exactly 1.000 unit, bought by N distinct cashiers at
'           once. Exactly one Created, every other InsufficientStock, never
'           an unexpected exception. Run at N=2 and N=10 (P1-13's own two
'           rounds).
'
'   box 2   SALE RACING RECEIVE. A receive of +5.000 and a sale of 5.000 (an
'           EWallet sale - no cash-tender arithmetic to complicate the race)
'           fire together at a product holding 2.000 - a real balance row,
'           so the conditional decrement's guard is what is actually being
'           raced, not row-existence (P4-15 section 2.1(a)'s own finding).
'           Both orderings are legal: the sale sees the received stock
'           (Created, balance 2.000) or it does not (InsufficientStock,
'           balance 7.000, the receive still lands). Six rounds.
'
'   box 3   SALE RACING ADJUSTMENT. Both operations are DECREMENTS here (a
'           negative adjustment), so unlike box 2 there is no "extra
'           capacity" arriving mid-race - a product holding exactly 5.000
'           races a sale for 5.000 against an adjustment variance of
'           -5.000. Exactly one of the two wins (Created); the other gets
'           InsufficientStock; the balance is 0.000 either way. The
'           "ordering" this box's distribution reports is WHICH COMMAND won,
'           not the balance - six rounds, both winners must actually appear
'           or the race is not a real one.
'
' Every box also asserts, over the APPEND-ONLY StockMovements ledger, that no
' row for the contended product ever recorded a QuantityAfter below zero
' (P4-15 section 2.1(b): the final balance alone cannot see a transient
' negative a later movement papered over) and that the P4-01 ledger
' reconciliation still holds afterward.
'
' Fixture users, supplier and products are real, permanent rows - nothing
' here is torn down (ADR-013). Balances are always seeded WITH a matching
' StockMovements row (SeedBalanceAsync) - seeding a bare balance is exactly
' the drift P4-01 found in the Phase 1/2 fixtures.

Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports System.Linq
Imports System.Text
Imports System.Threading.Tasks
Imports Merchandising.Api.Inventory
Imports Merchandising.Api.Procurement
Imports Merchandising.Api.Receiving
Imports Merchandising.Api.Sales
Imports Merchandising.Contracts.Procurement
Imports Merchandising.Contracts.Receiving
Imports Merchandising.Contracts.Sales
Imports Merchandising.Domain.Configuration
Imports Merchandising.Domain.Sales
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class SaleConcurrencyTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const FixturePassword As String = "P5-08 Fixture Passw0rd!"
    Private Const AdminAUsername As String = "p5_08_fixture_admin_a"
    Private Const AdminBUsername As String = "p5_08_fixture_admin_b"
    Private Const InventoryUsername As String = "p5_08_fixture_inventory"
    Private Const CashierUsernamePrefix As String = "p5_08_fixture_cashier_"
    Private Const FixtureProductSkuPrefix As String = "p5_08_fixture_sku_"

    ''' <summary>Large enough to cover box 1's N=10 round - one distinct, pre-opened session per concurrent sale attempt.</summary>
    Private Const CashierPoolSize As Integer = 10

    ''' <summary>High enough that every adjustment in this file applies immediately - threshold routing itself is P4-10's subject, not this file's (P4-15's identical reasoning).</summary>
    Private Const ImmediateApplyThreshold As Decimal = 100000.000D

    ''' <summary>SystemSettingRegistry's own declared default for inventory.adjustmentThreshold - what TearDownAsync restores.</summary>
    Private Const RegistryDefaultThreshold As Decimal = 10.000D

    Private _connectionFactory As ConnectionFactory
    Private _saleService As SaleService
    Private _cashierSessionService As CashierSessionService
    Private _purchaseOrderService As PurchaseOrderService
    Private _receivingService As ReceivingService
    Private _adjustmentService As AdjustmentService
    Private _supplierId As Integer
    Private _adminAUserId As Integer
    Private _adminBUserId As Integer
    Private _inventoryUserId As Integer
    Private _cashierUserIds As Integer()

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        _saleService = New SaleService(_connectionFactory)
        _cashierSessionService = New CashierSessionService(_connectionFactory)
        _purchaseOrderService = New PurchaseOrderService(_connectionFactory)
        _receivingService = New ReceivingService(_connectionFactory)
        _adjustmentService = New AdjustmentService(_connectionFactory)

        _adminAUserId = Await EnsureFixtureUserAsync(AdminAUsername, "Admin")
        _adminBUserId = Await EnsureFixtureUserAsync(AdminBUsername, "Admin")
        _inventoryUserId = Await EnsureFixtureUserAsync(InventoryUsername, "InventoryClerk")
        _supplierId = Await CreateActiveSupplierAsync()

        _cashierUserIds = New Integer(CashierPoolSize - 1) {}
        For i = 0 To CashierPoolSize - 1
            _cashierUserIds(i) = Await EnsureCashierWithOpenSessionAsync(CashierUsernamePrefix & i.ToString(CultureInfo.InvariantCulture))
        Next

        Await SetThresholdAsync(ImmediateApplyThreshold)

    End Function

    ''' <summary>
    ''' Puts the shared threshold back to SystemSettingRegistry's own default -
    ''' inventory.adjustmentThreshold is one row for the whole installation,
    ''' and this class's own mess to clean up (P4-15's identical reasoning
    ''' and identical constraint: cannot be done through the API from here,
    ''' since writing a setting is SuperAdmin-only).
    ''' </summary>
    <TestCleanup>
    Public Async Function TearDownAsync() As Task

        Await SetThresholdAsync(RegistryDefaultThreshold)

    End Function

    ' ------------------------------------------------------------------ box 1

    ''' <summary>
    ''' Box 1: a product holding exactly one unit, bought by N distinct
    ''' cashiers at once. Exactly one must succeed; every other must receive
    ''' the controlled InsufficientStock outcome, never an unexpected
    ''' exception. Run at N=2 and N=10, resetting the balance to 1.000
    ''' between rounds - the same two-round shape StockDecrementTests'
    ''' P1-13 proof uses.
    ''' </summary>
    <TestMethod>
    Public Async Function Sale_ConcurrentSameProduct_LastUnitExactlyOneSucceeds() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()

        Await RunLastUnitRoundAsync(productId, requestCount:=2)
        Await RunLastUnitRoundAsync(productId, requestCount:=10)

    End Function

    Private Async Function RunLastUnitRoundAsync(productId As Integer, requestCount As Integer) As Task

        Await ResetBalanceAsync(productId, 1.000D)

        Dim tasks(requestCount - 1) As Task(Of AttemptResult)
        For i = 0 To requestCount - 1
            tasks(i) = AttemptSaleAsync(_cashierUserIds(i), productId, 1.000D)
        Next

        Dim results As AttemptResult() = Await Task.WhenAll(tasks)

        Dim distribution As New StringBuilder()
        Dim successCount As Integer = 0
        Dim insufficientCount As Integer = 0
        Dim exceptionCount As Integer = 0

        For Each result As AttemptResult In results
            If distribution.Length > 0 Then distribution.Append(" | ")
            distribution.Append($"sale:{result.Kind}")
            Select Case result.Kind
                Case NameOf(SaleOutcomeKind.Created)
                    successCount += 1
                Case NameOf(SaleOutcomeKind.InsufficientStock)
                    insufficientCount += 1
                Case Else
                    exceptionCount += 1
            End Select
        Next

        Console.WriteLine($"P5-08 box 1 x{requestCount} raw distribution -> {distribution}")

        Assert.AreEqual(0, exceptionCount, $"No attempt may fail with an unexpected exception - only Created or InsufficientStock are controlled outcomes. Detail: {DescribeFailures(results)}")
        Assert.AreEqual(1, successCount, $"Exactly one of {requestCount} simultaneous sales for the last unit must succeed.")
        Assert.AreEqual(requestCount - 1, insufficientCount, "Every non-winning sale must receive the controlled insufficient-stock outcome.")

        Dim balance As Decimal = Await ReadBalanceAsync(productId)
        Assert.AreEqual(0.000D, balance, "Final balance must be exactly zero after the last unit is sold - never negative.")

        Await AssertLedgerReconcilesAsync(productId, $"after box 1 x{requestCount}")
        Await AssertNoMovementWentNegativeAsync(productId, $"box 1 x{requestCount}")

    End Function

    ' ------------------------------------------------------------------ box 2

    ''' <summary>
    ''' Box 2: a receive of +5.000 races a sale of 5.000 at a product holding
    ''' 2.000 - the P4-15 box-2 numbers, with the sale standing in for the
    ''' adjustment. 2.000 is load-bearing for the same reason P4-15 section
    ''' 2.1(a) recorded: a never-received product has no StockBalances row
    ''' at all, so the conditional decrement's row-existence check refuses it
    ''' before the "Quantity >= @qty" guard this criterion exists to test is
    ''' ever reached.
    ''' </summary>
    <TestMethod>
    Public Async Function Sale_RacingReceive_SameProduct_BothOrderingsObserved() As Task

        Const Rounds As Integer = 6
        Const Quantity As Decimal = 5.000D
        Const OpeningBalance As Decimal = 2.000D

        Dim saleWon As Integer = 0
        Dim receiveWon As Integer = 0

        For round = 1 To Rounds

            Dim productId As Integer = Await CreateFixtureProductAsync()
            Await SeedBalanceAsync(productId, OpeningBalance)
            Dim prepared = Await CreateApprovedOrderAsync(productId, Quantity)

            Dim receiveTask As Task(Of AttemptResult) = AttemptReceiveAsync(prepared.OrderId, prepared.OrderLineId, Quantity)
            Dim saleTask As Task(Of AttemptResult) = AttemptSaleAsync(_cashierUserIds(0), productId, Quantity)

            Dim results As AttemptResult() = Await Task.WhenAll(receiveTask, saleTask)
            Dim receiveResult As AttemptResult = results(0)
            Dim saleResult As AttemptResult = results(1)
            Dim balance As Decimal = Await ReadBalanceAsync(productId)

            Console.WriteLine($"P5-08 box 2 round {round} -> receive:{receiveResult.Kind} sale:{saleResult.Kind} balance:{balance:0.000}")

            Assert.AreNotEqual("Exception", receiveResult.Kind, $"Round {round}: receive threw - {receiveResult.Detail}")
            Assert.AreNotEqual("Exception", saleResult.Kind, $"Round {round}: sale threw - {saleResult.Detail}")

            Assert.AreEqual(
                NameOf(ReceivingOutcomeKind.Created), receiveResult.Kind,
                $"Round {round}: the receive has nothing to contend for - it adds stock and must always commit.")

            Assert.IsGreaterThanOrEqualTo(
                0D, balance,
                $"Round {round}: balance went negative ({balance:0.000}) - the guarantee this criterion exists for.")

            Await AssertNoMovementWentNegativeAsync(productId, $"round {round}")

            Select Case saleResult.Kind

                Case NameOf(SaleOutcomeKind.Created)
                    saleWon += 1
                    Assert.AreEqual(
                        OpeningBalance, balance,
                        $"Round {round}: the sale succeeded, so it saw the received stock - {OpeningBalance:0.000} + {Quantity:0.000} - {Quantity:0.000}.")

                Case NameOf(SaleOutcomeKind.InsufficientStock)
                    receiveWon += 1
                    Assert.AreEqual(
                        OpeningBalance + Quantity, balance,
                        $"Round {round}: the sale was refused, so only the receive landed - balance must be exactly {OpeningBalance + Quantity:0.000}.")

                Case Else
                    Assert.Fail($"Round {round}: '{saleResult.Kind}' is not a consistent outcome for this race - {saleResult.Detail}")

            End Select

            Await AssertLedgerReconcilesAsync(productId, $"after round {round}")

        Next

        Console.WriteLine($"P5-08 box 2 ordering distribution over {Rounds} rounds -> sale won: {saleWon}, receive won (sale refused): {receiveWon}")

        Assert.AreEqual(Rounds, saleWon + receiveWon, "Every round must land on one of the two consistent end states.")

    End Function

    ' ------------------------------------------------------------------ box 3

    ''' <summary>
    ''' Box 3: a sale of 5.000 races an adjustment of -5.000 at a product
    ''' holding exactly 5.000. Unlike box 2, BOTH sides are decrements - there
    ''' is no ordering that lets both commit, so exactly one wins and the
    ''' other must be refused InsufficientStock, and the balance lands on
    ''' 0.000 either way. What this box's distribution tracks is WHICH
    ''' command won - a test that only ever sees the sale win (or only ever
    ''' sees the adjustment win) is asserting one code path while claiming a
    ''' race exists between two.
    ''' </summary>
    <TestMethod>
    Public Async Function Sale_RacingAdjustment_SameProduct_ExactlyOneWins() As Task

        Const Rounds As Integer = 8
        Const Quantity As Decimal = 5.000D

        Dim saleWon As Integer = 0
        Dim adjustmentWon As Integer = 0

        For round = 1 To Rounds

            Dim productId As Integer = Await CreateFixtureProductAsync()
            Await SeedBalanceAsync(productId, Quantity)

            Dim saleTask As Task(Of AttemptResult) = AttemptSaleAsync(_cashierUserIds(0), productId, Quantity)
            Dim adjustTask As Task(Of AttemptResult) = AttemptAdjustAsync(productId, -Quantity)

            Dim results As AttemptResult() = Await Task.WhenAll(saleTask, adjustTask)
            Dim saleResult As AttemptResult = results(0)
            Dim adjustResult As AttemptResult = results(1)
            Dim balance As Decimal = Await ReadBalanceAsync(productId)

            Console.WriteLine($"P5-08 box 3 round {round} -> sale:{saleResult.Kind} adjust:{adjustResult.Kind} balance:{balance:0.000}")

            Assert.AreNotEqual("Exception", saleResult.Kind, $"Round {round}: sale threw - {saleResult.Detail}")
            Assert.AreNotEqual("Exception", adjustResult.Kind, $"Round {round}: adjust threw - {adjustResult.Detail}")

            Assert.IsGreaterThanOrEqualTo(
                0D, balance,
                $"Round {round}: balance went negative ({balance:0.000}) - the guarantee this criterion exists for.")

            Await AssertNoMovementWentNegativeAsync(productId, $"round {round}")

            Dim saleCreated As Boolean = String.Equals(saleResult.Kind, NameOf(SaleOutcomeKind.Created), StringComparison.Ordinal)
            Dim adjustCreated As Boolean = String.Equals(adjustResult.Kind, NameOf(AdjustmentOutcomeKind.Created), StringComparison.Ordinal)

            Assert.AreNotEqual(saleCreated, adjustCreated, $"Round {round}: exactly one of the sale/adjustment pair must win - sale:{saleResult.Kind} adjust:{adjustResult.Kind}.")

            If saleCreated Then
                saleWon += 1
                Assert.AreEqual(
                    NameOf(AdjustmentOutcomeKind.InsufficientStock), adjustResult.Kind,
                    $"Round {round}: the sale won, so the adjustment must have been refused for insufficient stock.")
            Else
                adjustmentWon += 1
                Assert.AreEqual(
                    NameOf(SaleOutcomeKind.InsufficientStock), saleResult.Kind,
                    $"Round {round}: the adjustment won, so the sale must have been refused for insufficient stock.")
            End If

            Assert.AreEqual(0.000D, balance, $"Round {round}: whichever side won, the balance must land on exactly zero.")

            Await AssertLedgerReconcilesAsync(productId, $"after round {round}")

        Next

        Console.WriteLine($"P5-08 box 3 ordering distribution over {Rounds} rounds -> sale won: {saleWon}, adjustment won: {adjustmentWon}")

        Assert.AreEqual(Rounds, saleWon + adjustmentWon, "Every round must land on exactly one winner.")
        Assert.IsGreaterThan(0, saleWon, $"Across {Rounds} rounds the sale never won once - this is not a genuine race, only one code path was ever exercised.")
        Assert.IsGreaterThan(0, adjustmentWon, $"Across {Rounds} rounds the adjustment never won once - this is not a genuine race, only one code path was ever exercised.")

    End Function

    ' --------------------------------------------------------------- attempts

    ''' <summary>One attempt's outcome, flattened to strings so a sale, receive, and adjustment can share one result shape.</summary>
    Private Structure AttemptResult
        Public Property Operation As String
        Public Property Kind As String
        Public Property Detail As String
    End Structure

    Private Async Function AttemptSaleAsync(cashierUserId As Integer, productId As Integer, quantity As Decimal) As Task(Of AttemptResult)

        Try
            Dim outcome As SaleOutcome =
                Await _saleService.CompleteAsync(
                    New List(Of CreateSaleLineRequest) From {
                        New CreateSaleLineRequest With {.ProductId = productId, .Quantity = quantity}},
                    PaymentMethod.EWallet,
                    Nothing,
                    cashierUserId,
                    Guid.NewGuid().ToString(),
                    Guid.NewGuid().ToString())

            Return New AttemptResult With {.Operation = "sale", .Kind = outcome.Kind.ToString(), .Detail = outcome.Kind.ToString()}

        Catch ex As Exception
            Return New AttemptResult With {.Operation = "sale", .Kind = "Exception", .Detail = $"{ex.GetType().Name}: {ex.Message}"}
        End Try

    End Function

    Private Async Function AttemptReceiveAsync(purchaseOrderId As Integer, purchaseOrderLineId As Integer, quantity As Decimal) As Task(Of AttemptResult)

        Try
            Dim outcome As ReceivingOutcome =
                Await _receivingService.ReceiveAsync(
                    purchaseOrderId,
                    NewReferenceNumber(),
                    New List(Of ReceiveGoodsLineRequest) From {
                        New ReceiveGoodsLineRequest With {
                            .PurchaseOrderLineId = purchaseOrderLineId, .QuantityReceived = quantity, .Cost = 2.0000D}},
                    _inventoryUserId,
                    Guid.NewGuid().ToString(),
                    Guid.NewGuid().ToString())

            Return New AttemptResult With {.Operation = "receive", .Kind = outcome.Kind.ToString(), .Detail = outcome.Kind.ToString()}

        Catch ex As Exception
            Return New AttemptResult With {.Operation = "receive", .Kind = "Exception", .Detail = $"{ex.GetType().Name}: {ex.Message}"}
        End Try

    End Function

    Private Async Function AttemptAdjustAsync(productId As Integer, variance As Decimal) As Task(Of AttemptResult)

        Try
            Dim outcome As AdjustmentOutcome =
                Await _adjustmentService.RequestAsync(
                    productId,
                    variance,
                    "P5-08 concurrency probe",
                    _inventoryUserId,
                    Guid.NewGuid().ToString(),
                    Guid.NewGuid().ToString())

            Return New AttemptResult With {
                .Operation = "adjust",
                .Kind = outcome.Kind.ToString(),
                .Detail = If(outcome.Response Is Nothing, outcome.Kind.ToString(), outcome.Response.Status)}

        Catch ex As Exception
            Return New AttemptResult With {.Operation = "adjust", .Kind = "Exception", .Detail = $"{ex.GetType().Name}: {ex.Message}"}
        End Try

    End Function

    Private Shared Function DescribeFailures(results As IEnumerable(Of AttemptResult)) As String

        Dim detail As String =
            String.Join("; ", results.
                Where(Function(r) Not String.Equals(r.Kind, NameOf(SaleOutcomeKind.Created), StringComparison.Ordinal)).
                Select(Function(r) $"{r.Operation}:{r.Kind}:{r.Detail}"))

        Return If(String.IsNullOrEmpty(detail), "(every attempt Created)", detail)

    End Function

    ' --------------------------------------------------------------- assertions

    Private Async Function AssertLedgerReconcilesAsync(productId As Integer, context As String) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            Dim discrepancies = Await LedgerReconciliation.FindDiscrepanciesAsync(connection)
            Dim found = discrepancies.FirstOrDefault(Function(d) d.ProductId = productId)

            Dim detail As String =
                If(found Is Nothing, String.Empty,
                   $"expected {found.ExpectedQuantity}, actual {found.ActualQuantity}, delta {found.Delta}")

            Assert.IsNull(found, $"Product {productId} must reconcile {context}: {detail}")

        End Using

    End Function

    ''' <summary>
    ''' No movement this product ever recorded may leave the balance below
    ''' zero. Read from the append-only ledger rather than StockBalances, so
    ''' a negative a later movement papered over is still visible (P4-15
    ''' section 2.1(b)).
    ''' </summary>
    Private Async Function AssertNoMovementWentNegativeAsync(productId As Integer, context As String) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()

                command.CommandText =
                    "SELECT COALESCE(MIN(QuantityAfter), 0.000) FROM StockMovements WHERE ProductId = @productId;"
                command.Parameters.AddWithValue("@productId", productId)

                Dim lowest As Decimal = CDec(Await command.ExecuteScalarAsync())

                Assert.IsGreaterThanOrEqualTo(
                    0D, lowest,
                    $"Product {productId}: the ledger records a balance of {lowest:0.000} at some point during {context} - " &
                    "stock was allowed below zero, even if a later movement hid it.")

            End Using
        End Using

    End Function

    ' --------------------------------------------------------------- fixtures

    Private Shared Function NewReferenceNumber() As String
        Return "P5-08-GRN-" & Guid.NewGuid().ToString("N").Substring(0, 20)
    End Function

    ''' <summary>Creates, submits and approves a single-line order for <paramref name="productId"/>, ready to be received against.</summary>
    Private Async Function CreateApprovedOrderAsync(
        productId As Integer, orderedQuantity As Decimal) As Task(Of (OrderId As Integer, OrderLineId As Integer))

        Dim createOutcome As PurchaseOrderCreationOutcome =
            Await _purchaseOrderService.CreateAsync(
                _supplierId,
                New List(Of CreatePurchaseOrderLineRequest) From {
                    New CreatePurchaseOrderLineRequest With {
                        .ProductId = productId, .OrderedQuantity = orderedQuantity, .PurchaseCost = 2.0000D}},
                _adminAUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(PurchaseOrderCreationOutcomeKind.Created, createOutcome.Kind, "Fixture order creation must succeed.")

        Dim orderId As Integer = createOutcome.Response.Id
        Dim orderLineId As Integer = createOutcome.Response.Lines(0).Id

        Dim submitOutcome = Await _purchaseOrderService.SubmitAsync(orderId, _adminAUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Success, submitOutcome.Kind, "Fixture submit must succeed.")

        Dim approveOutcome = Await _purchaseOrderService.ApproveAsync(orderId, _adminBUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Success, approveOutcome.Kind, "Fixture approve must succeed.")

        Return (OrderId:=orderId, OrderLineId:=orderLineId)

    End Function

    Private Async Function CreateActiveSupplierAsync() As Task(Of Integer)

        Dim name As String = "P5-08 Supplier " & Guid.NewGuid().ToString("N").Substring(0, 12)

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
                    "VALUES (@sku, NULL, 'P5-08 Fixture Product', 9.0000, 4.0000, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@sku", sku)
                Await command.ExecuteNonQueryAsync()
                Return CInt(command.LastInsertedId)
            End Using
        End Using

    End Function

    ''' <summary>
    ''' Seeds an opening balance WITH a matching movement row - seeding
    ''' StockBalances directly is exactly the fixture defect P4-01 found and
    ''' had to heal with a compensating correction (P4-15's identical
    ''' reasoning). Used for a fresh product that has never held a balance.
    ''' </summary>
    Private Async Function SeedBalanceAsync(productId As Integer, quantity As Decimal) As Task

        If quantity = 0D Then
            Return
        End If

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using transaction As MySqlTransaction = Await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted)

                Dim stockResult = Await StockRepository.IncrementAsync(connection, transaction, productId, quantity)

                Await StockMovementWriter.WriteAsync(
                    connection, transaction, productId, quantity,
                    stockResult.QuantityBefore, stockResult.QuantityAfter,
                    "P5-08 fixture: opening balance seed", _inventoryUserId, Guid.NewGuid().ToString())

                Await transaction.CommitAsync()

            End Using
        End Using

    End Function

    ''' <summary>
    ''' Resets a fixture product's balance to <paramref name="quantity"/>,
    ''' through StockRepository's own upsert/conditional-decrement (never a
    ''' bare UPDATE) - a product that has never held a balance has NO
    ''' StockBalances row at all (ProductRepository.InsertAsync does not seed
    ''' one, the same fact P4-15 section 2.1(a) found), and a bare UPDATE
    ''' against a missing row affects zero rows silently, leaving the ledger
    ''' recording a movement that never actually reached StockBalances. Used
    ''' by box 1, which reuses ONE product across two rounds rather than
    ''' creating a fresh one each time.
    ''' </summary>
    Private Async Function ResetBalanceAsync(productId As Integer, quantity As Decimal) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using transaction As MySqlTransaction = Await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted)

                Dim current As Decimal = Await ReadBalanceAsync(connection, productId, transaction)
                Dim delta As Decimal = quantity - current

                If delta > 0D Then

                    Dim incrementResult = Await StockRepository.IncrementAsync(connection, transaction, productId, delta)
                    Await StockMovementWriter.WriteAsync(
                        connection, transaction, productId, delta, incrementResult.QuantityBefore, incrementResult.QuantityAfter,
                        "P5-08 fixture: balance reset between rounds", _inventoryUserId, Guid.NewGuid().ToString())

                ElseIf delta < 0D Then

                    Dim decrementResult = Await StockRepository.TryDecrementAsync(connection, transaction, productId, -delta)
                    Assert.IsTrue(decrementResult.Succeeded, $"Fixture reset could not decrement product {productId} from {current:0.000} to {quantity:0.000}.")
                    Await StockMovementWriter.WriteAsync(
                        connection, transaction, productId, delta, decrementResult.QuantityBefore, decrementResult.QuantityAfter,
                        "P5-08 fixture: balance reset between rounds", _inventoryUserId, Guid.NewGuid().ToString())

                End If

                Await transaction.CommitAsync()

            End Using
        End Using

    End Function

    Private Async Function SetThresholdAsync(threshold As Decimal) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using transaction As MySqlTransaction = Await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted)

                Await SystemSettingsRepository.UpsertAsync(
                    connection, transaction, SystemSettingRegistry.Keys.AdjustmentApprovalThreshold,
                    threshold.ToString("0.000", CultureInfo.InvariantCulture), _inventoryUserId)

                Await transaction.CommitAsync()

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

    ''' <summary>
    ''' Ensures the named cashier exists and holds an Open session, calling
    ''' OpenAsync and accepting either Created (first run) or AlreadyOpen (a
    ''' prior run's session, still open - these fixture rows are never torn
    ''' down, ADR-013) as success. SaleService looks up the open session by
    ''' actor user id alone, so the session's own id is never needed here.
    ''' </summary>
    Private Async Function EnsureCashierWithOpenSessionAsync(username As String) As Task(Of Integer)

        Dim userId As Integer = Await EnsureFixtureUserAsync(username, "Cashier")

        Dim outcome As CashierSessionOutcome =
            Await _cashierSessionService.OpenAsync(userId, 0D, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.IsTrue(
            outcome.Kind = CashierSessionOutcomeKind.Created OrElse outcome.Kind = CashierSessionOutcomeKind.AlreadyOpen,
            $"Fixture cashier '{username}' could not be given an open session - unexpected outcome {outcome.Kind}.")

        Return userId

    End Function

    Private Shared Function LoadMigratorOptions() As DatabaseOptions
        Dim migratorConfigPath As String =
            IO.Path.Combine(IO.Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName)
        Return DatabaseOptionsLoader.Load(migratorConfigPath)
    End Function

    Private Async Function ReadBalanceAsync(productId As Integer) As Task(Of Decimal)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Return Await ReadBalanceAsync(connection, productId, Nothing)
        End Using

    End Function

    Private Shared Async Function ReadBalanceAsync(connection As MySqlConnection, productId As Integer, transaction As MySqlTransaction) As Task(Of Decimal)

        Using command As MySqlCommand = connection.CreateCommand()
            command.Transaction = transaction
            command.CommandText = "SELECT COALESCE(Quantity, 0.000) FROM StockBalances WHERE ProductId = @productId;"
            command.Parameters.AddWithValue("@productId", productId)
            Dim scalar As Object = Await command.ExecuteScalarAsync()
            Return If(scalar Is Nothing OrElse scalar Is DBNull.Value, 0.000D, CDec(scalar))
        End Using

    End Function

End Class
