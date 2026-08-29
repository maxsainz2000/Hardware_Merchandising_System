' Merchandising.Tests.Integration.ReceiveAndAdjustConcurrencyTests
'
' P4-15: the Phase 4 exit criterion "concurrent receive-and-adjust on the
' same product is safe" (plan.md section 7), which the Phase 4 closure pack
' found had no test firing it.
'
' evidence/phase-4/INDEX.md section 4.1 recorded the gap precisely: the exit
' gate cited p4-05-receiving-atomic.txt and p4-10-adjustments.txt, and
' NEITHER contains a scenario where a receive and an adjustment contend for
' the same StockBalances row at the same time. Each proved its own command
' atomic in isolation; the criterion claims something about the two of them
' together. That was an argument by analogy from P1-13 (decrement vs
' decrement) and P4-08 (return vs return), not an executed proof - and
' tasks.md line 20's own rule for this phase is that a sentence in an
' evidence file is not a check.
'
' This file is that check. Two properties, both fired under real concurrent
' load in the P1-13 / P4-08 "launch every task, then await" shape:
'
'   box 1   NO LOST UPDATE. N receives and N adjustments against ONE product,
'           all in flight together, settle to the exact arithmetic sum of
'           their deltas. A lost update - the classic read-then-write defect
'           ADR-006's conditional write exists to make impossible - shows up
'           here as a final balance short of the expected figure, and as
'           ledger drift.
'
'   box 2   NO NEGATIVE BALANCE, EITHER ORDERING. A receive of +Q and an
'           adjustment of -Q race on a product holding 0. Both orderings are
'           legal and this test accepts either, but only two end states are:
'           the adjustment saw the stock (Applied, balance 0) or it did not
'           (InsufficientStock, balance Q). Anything else - a negative
'           balance, an unexpected exception, an Applied adjustment sitting
'           on a balance that never covered it - is a defect. Repeated over
'           several rounds so both orderings are actually observed rather
'           than one being assumed.
'
' Both boxes assert the P4-01 ledger reconciliation for the contended
' product afterwards, because the invariant that catches a mis-ordered write
' is exactly SUM(movements) = balance.
'
' Fixture users, supplier and products are real, permanent rows - nothing
' here is torn down (ADR-013). The balance seed writes a matching movement
' rather than setting StockBalances directly: seeding without a movement is
' precisely the drift P4-01 found in the Phase 1/2 fixtures.

Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports System.Linq
Imports System.Text
Imports System.Threading.Tasks
Imports Merchandising.Api.Inventory
Imports Merchandising.Api.Procurement
Imports Merchandising.Api.Receiving
Imports Merchandising.Contracts.Procurement
Imports Merchandising.Contracts.Receiving
Imports Merchandising.Domain.Configuration
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class ReceiveAndAdjustConcurrencyTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const FixturePassword As String = "P4-15 Fixture Passw0rd!"
    Private Const AdminAUsername As String = "p4_15_fixture_admin_a"
    Private Const AdminBUsername As String = "p4_15_fixture_admin_b"
    Private Const InventoryUsername As String = "p4_15_fixture_inventory"
    Private Const FixtureProductSkuPrefix As String = "p4_15_fixture_sku_"

    ''' <summary>High enough that every adjustment in this file applies immediately - the threshold routing itself is P4-10's subject, not this file's.</summary>
    Private Const ImmediateApplyThreshold As Decimal = 100000.000D

    ''' <summary>SystemSettingRegistry's own declared default for inventory.adjustmentThreshold - what TearDownAsync restores.</summary>
    Private Const RegistryDefaultThreshold As Decimal = 10.000D

    Private _connectionFactory As ConnectionFactory
    Private _purchaseOrderService As PurchaseOrderService
    Private _receivingService As ReceivingService
    Private _adjustmentService As AdjustmentService
    Private _supplierId As Integer
    Private _adminAUserId As Integer
    Private _adminBUserId As Integer
    Private _inventoryUserId As Integer

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        _purchaseOrderService = New PurchaseOrderService(_connectionFactory)
        _receivingService = New ReceivingService(_connectionFactory)
        _adjustmentService = New AdjustmentService(_connectionFactory)

        _adminAUserId = Await EnsureFixtureUserAsync(AdminAUsername, "Admin")
        _adminBUserId = Await EnsureFixtureUserAsync(AdminBUsername, "Admin")
        _inventoryUserId = Await EnsureFixtureUserAsync(InventoryUsername, "InventoryClerk")
        _supplierId = Await CreateActiveSupplierAsync()

        Await SetThresholdAsync(ImmediateApplyThreshold)

    End Function

    ''' <summary>
    ''' Puts the shared threshold back to SystemSettingRegistry's own default.
    ''' inventory.adjustmentThreshold is one row for the whole installation,
    ''' so a suite that raises it and walks away leaves the DEVELOPMENT
    ''' database - the one a demo runs against - configured to auto-apply an
    ''' adjustment of any size. Restoring it is this class's own mess to
    ''' clean up, and it cannot be done through the API from here: writing a
    ''' setting is SuperAdmin-only.
    ''' </summary>
    <TestCleanup>
    Public Async Function TearDownAsync() As Task

        Await SetThresholdAsync(RegistryDefaultThreshold)

    End Function

    ' ------------------------------------------------------------------ box 1

    ''' <summary>
    ''' Box 1: four receives (+10.000 each, against four separately approved
    ''' orders) and four adjustments (-1.000 each) against ONE product, every
    ''' one launched before any is awaited. All eight must commit, and the
    ''' balance must land on the exact arithmetic total. The opening balance
    ''' is seeded high enough that no adjustment can be refused for
    ''' insufficient stock, so a shortfall can only mean a lost update.
    ''' </summary>
    <TestMethod>
    Public Async Function ReceiveAndAdjust_ConcurrentSameProduct_NoLostUpdate() As Task

        Const ReceiptCount As Integer = 4
        Const AdjustmentCount As Integer = 4
        Const OpeningBalance As Decimal = 100.000D
        Const ReceiveQuantity As Decimal = 10.000D
        Const AdjustVariance As Decimal = -1.000D

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SeedBalanceAsync(productId, OpeningBalance)

        ' Orders are prepared BEFORE the race - approving a purchase order is
        ' not what is under test here, and doing it inside the race would
        ' contend on PurchaseOrders instead of on StockBalances.
        Dim orderIds(ReceiptCount - 1) As Integer
        Dim orderLineIds(ReceiptCount - 1) As Integer
        For i = 0 To ReceiptCount - 1
            Dim prepared = Await CreateApprovedOrderAsync(productId, ReceiveQuantity)
            orderIds(i) = prepared.OrderId
            orderLineIds(i) = prepared.OrderLineId
        Next

        Dim tasks As New List(Of Task(Of AttemptResult))(ReceiptCount + AdjustmentCount)

        For i = 0 To ReceiptCount - 1
            tasks.Add(AttemptReceiveAsync(orderIds(i), orderLineIds(i), ReceiveQuantity))
        Next
        For i = 0 To AdjustmentCount - 1
            tasks.Add(AttemptAdjustAsync(productId, AdjustVariance))
        Next

        Dim results As AttemptResult() = Await Task.WhenAll(tasks)

        Dim distribution As New StringBuilder()
        Dim receiveSuccesses As Integer = 0
        Dim adjustSuccesses As Integer = 0
        Dim exceptions As Integer = 0

        For Each result As AttemptResult In results
            If distribution.Length > 0 Then distribution.Append(" | ")
            distribution.Append($"{result.Operation}:{result.Kind}")
            If String.Equals(result.Kind, "Exception", StringComparison.Ordinal) Then
                exceptions += 1
            ElseIf String.Equals(result.Operation, "receive", StringComparison.Ordinal) AndAlso
                   String.Equals(result.Kind, NameOf(ReceivingOutcomeKind.Created), StringComparison.Ordinal) Then
                receiveSuccesses += 1
            ElseIf String.Equals(result.Operation, "adjust", StringComparison.Ordinal) AndAlso
                   String.Equals(result.Kind, NameOf(AdjustmentOutcomeKind.Created), StringComparison.Ordinal) Then
                adjustSuccesses += 1
            End If
        Next

        Console.WriteLine($"P4-15 box 1 raw distribution -> {distribution}")
        Console.WriteLine($"P4-15 box 1 detail -> {DescribeFailures(results)}")

        Assert.AreEqual(0, exceptions, "No attempt may fail with an unexpected exception - every outcome must be a controlled one.")
        Assert.AreEqual(ReceiptCount, receiveSuccesses, "Every receive against its own approved order must commit.")
        Assert.AreEqual(AdjustmentCount, adjustSuccesses, "Every adjustment must commit - the opening balance covers all of them.")

        Dim expected As Decimal =
            OpeningBalance + (ReceiveQuantity * ReceiptCount) + (AdjustVariance * AdjustmentCount)

        Dim actual As Decimal = Await ReadBalanceAsync(productId)
        Console.WriteLine($"P4-15 box 1 balance -> expected {expected:0.000}, actual {actual:0.000}")

        Assert.AreEqual(
            expected, actual,
            $"Concurrent receives and adjustments lost an update: expected {expected:0.000}, found {actual:0.000}.")

        Await AssertLedgerReconcilesAsync(productId, "after concurrent receive-and-adjust")
        Await AssertNoMovementWentNegativeAsync(productId, "the concurrent batch")

    End Function

    ' ------------------------------------------------------------------ box 2

    ''' <summary>
    ''' Box 2: a receive of +5.000 and an adjustment of -5.000 fired together
    ''' at a product that already holds 2.000 - enough to have a real
    ''' StockBalances row, not enough to cover the adjustment on its own.
    ''' That opening balance is deliberate and load-bearing: against a
    ''' never-received product there is NO balance row at all, so the
    ''' conditional write's row-existence alone refuses the adjustment and
    ''' the "Quantity >= @qty" guard is never the thing being tested. With
    ''' 2.000 present the guard is the only thing standing between the
    ''' adjustment and a negative balance.
    '''
    ''' Whichever side wins the row lock, only two end states are consistent:
    ''' the adjustment saw the received stock (Applied, 2.000 + 5.000 -
    ''' 5.000 = 2.000) or it did not (InsufficientStock, 2.000 + 5.000 =
    ''' 7.000). Several rounds, each on a fresh product, so the assertion is
    ''' not passing on one lucky ordering; the observed distribution is
    ''' printed so the evidence file can state which orderings occurred.
    ''' </summary>
    <TestMethod>
    Public Async Function ReceiveAndAdjust_ConcurrentSameProduct_ExactlyOneOrderingWins() As Task

        Const Rounds As Integer = 6
        Const Quantity As Decimal = 5.000D
        Const OpeningBalance As Decimal = 2.000D

        Dim adjustmentApplied As Integer = 0
        Dim adjustmentRefused As Integer = 0

        For round = 1 To Rounds

            Dim productId As Integer = Await CreateFixtureProductAsync()
            Await SeedBalanceAsync(productId, OpeningBalance)
            Dim prepared = Await CreateApprovedOrderAsync(productId, Quantity)

            Dim receiveTask As Task(Of AttemptResult) =
                AttemptReceiveAsync(prepared.OrderId, prepared.OrderLineId, Quantity)
            Dim adjustTask As Task(Of AttemptResult) =
                AttemptAdjustAsync(productId, -Quantity)

            Dim results As AttemptResult() = Await Task.WhenAll(receiveTask, adjustTask)

            Dim receiveResult As AttemptResult = results(0)
            Dim adjustResult As AttemptResult = results(1)
            Dim balance As Decimal = Await ReadBalanceAsync(productId)

            Console.WriteLine(
                $"P4-15 box 2 round {round} -> receive:{receiveResult.Kind} adjust:{adjustResult.Kind} balance:{balance:0.000}")

            Assert.AreNotEqual("Exception", receiveResult.Kind, $"Round {round}: receive threw - {receiveResult.Detail}")
            Assert.AreNotEqual("Exception", adjustResult.Kind, $"Round {round}: adjust threw - {adjustResult.Detail}")

            Assert.AreEqual(
                NameOf(ReceivingOutcomeKind.Created), receiveResult.Kind,
                $"Round {round}: the receive has nothing to contend for - it adds stock and must always commit.")

            Assert.IsGreaterThanOrEqualTo(
                0D, balance,
                $"Round {round}: balance went negative ({balance:0.000}) - the guarantee this criterion exists for.")

            ' The final balance alone cannot see a TRANSIENT negative: an
            ' adjustment that took the balance to -5.000 before the receive
            ' put 5.000 back leaves 0.000 behind, indistinguishable from the
            ' safe ordering. StockMovements is append-only, so the ledger
            ' still carries the QuantityAfter that was actually written -
            ' that is where a momentary negative is provable, and it is what
            ' makes this assertion falsifiable rather than decorative.
            Await AssertNoMovementWentNegativeAsync(productId, $"round {round}")

            Select Case adjustResult.Kind

                Case NameOf(AdjustmentOutcomeKind.Created)
                    adjustmentApplied += 1
                    Assert.AreEqual(
                        OpeningBalance, balance,
                        $"Round {round}: the adjustment applied, so it saw the received stock - {OpeningBalance:0.000} + {Quantity:0.000} - {Quantity:0.000}.")

                Case NameOf(AdjustmentOutcomeKind.InsufficientStock)
                    adjustmentRefused += 1
                    Assert.AreEqual(
                        OpeningBalance + Quantity, balance,
                        $"Round {round}: the adjustment was refused, so only the receive landed - balance must be exactly {OpeningBalance + Quantity:0.000}.")

                Case Else
                    Assert.Fail($"Round {round}: '{adjustResult.Kind}' is not a consistent outcome for this race - {adjustResult.Detail}")

            End Select

            Await AssertLedgerReconcilesAsync(productId, $"after round {round}")

        Next

        Console.WriteLine(
            $"P4-15 box 2 ordering distribution over {Rounds} rounds -> adjustment applied: {adjustmentApplied}, adjustment refused for insufficient stock: {adjustmentRefused}")

        Assert.AreEqual(
            Rounds, adjustmentApplied + adjustmentRefused,
            "Every round must land on one of the two consistent end states.")

    End Function

    ' --------------------------------------------------------------- attempts

    ''' <summary>One attempt's outcome, flattened to strings so a receive and an adjustment can share one result array.</summary>
    Private Structure AttemptResult
        Public Property Operation As String
        Public Property Kind As String
        Public Property Detail As String
    End Structure

    Private Async Function AttemptReceiveAsync(
        purchaseOrderId As Integer, purchaseOrderLineId As Integer, quantity As Decimal) As Task(Of AttemptResult)

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

            Return New AttemptResult With {
                .Operation = "receive", .Kind = outcome.Kind.ToString(), .Detail = outcome.Kind.ToString()}

        Catch ex As Exception
            Return New AttemptResult With {
                .Operation = "receive", .Kind = "Exception", .Detail = ex.GetType().Name & ": " & ex.Message}
        End Try

    End Function

    Private Async Function AttemptAdjustAsync(productId As Integer, variance As Decimal) As Task(Of AttemptResult)

        Try
            Dim outcome As AdjustmentOutcome =
                Await _adjustmentService.RequestAsync(
                    productId,
                    variance,
                    "P4-15 concurrency probe",
                    _inventoryUserId,
                    Guid.NewGuid().ToString(),
                    Guid.NewGuid().ToString())

            Return New AttemptResult With {
                .Operation = "adjust",
                .Kind = outcome.Kind.ToString(),
                .Detail = If(outcome.Response Is Nothing, outcome.Kind.ToString(), outcome.Response.Status)}

        Catch ex As Exception
            Return New AttemptResult With {
                .Operation = "adjust", .Kind = "Exception", .Detail = ex.GetType().Name & ": " & ex.Message}
        End Try

    End Function

    Private Shared Function DescribeFailures(results As IEnumerable(Of AttemptResult)) As String

        Dim detail As String =
            String.Join("; ", results.
                Where(Function(r) Not String.Equals(r.Kind, NameOf(ReceivingOutcomeKind.Created), StringComparison.Ordinal)).
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
    ''' zero. Read from the append-only ledger rather than from StockBalances,
    ''' so a negative that a later receive papered over is still visible.
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
        Return "P4-15-GRN-" & Guid.NewGuid().ToString("N").Substring(0, 20)
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

        Dim name As String = "P4-15 Supplier " & Guid.NewGuid().ToString("N").Substring(0, 12)

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
                    "VALUES (@sku, NULL, 'P4-15 Fixture Product', 9.0000, 4.0000, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@sku", sku)
                Await command.ExecuteNonQueryAsync()
                Return CInt(command.LastInsertedId)
            End Using
        End Using

    End Function

    ''' <summary>
    ''' Seeds an opening balance WITH a matching movement row. Writing
    ''' StockBalances directly is exactly the fixture defect P4-01 found and
    ''' had to heal with a compensating correction - never repeat it.
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
                    "P4-15 fixture: opening balance seed", _inventoryUserId, Guid.NewGuid().ToString())

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

    Private Shared Function LoadMigratorOptions() As DatabaseOptions
        Dim migratorConfigPath As String =
            IO.Path.Combine(IO.Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName)
        Return DatabaseOptionsLoader.Load(migratorConfigPath)
    End Function

    Private Async Function ReadBalanceAsync(productId As Integer) As Task(Of Decimal)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COALESCE(Quantity, 0.000) FROM StockBalances WHERE ProductId = @productId;"
                command.Parameters.AddWithValue("@productId", productId)
                Dim scalar As Object = Await command.ExecuteScalarAsync()
                Return If(scalar Is Nothing OrElse scalar Is DBNull.Value, 0.000D, CDec(scalar))
            End Using
        End Using

    End Function

End Class
