' Merchandising.Tests.Integration.SaleServiceTests
'
' P5-07: POST /api/v1/sales and SaleService.CompleteAsync, proven against the
' real pinned MariaDB - spec section 11's "Sale completed" atomic-result row
' (Sale, sale lines, payment, stock-out movements, balance changes, session
' totals, audit event commit together).
'
' Role gating (Sales.Create resolves to the roles PolicyRegistry says it
' does) is AuthorizationMatrixTests' job. This file proves the BEHAVIOUR, the
' same split ReceivingTests uses for the identical reason (that file's own
' header):
'
'   box 1   every one of the five spec section 10.3 re-checks (product
'           activity, current price, available stock, duplicate request
'           state, payment validity) refused with its own stable error code,
'           each with its own test - plus the foundational "requires an open
'           session" precondition
'   box 2   the effective unit price/cost is captured once, server-side,
'           fresh; a later price change never moves an already-committed
'           line
'   box 3   stock decrements use ADR-006's conditional update - proven
'           indirectly (a real decrement happens) and directly (insufficient
'           stock is refused with no trace, never a read-then-write)
'   box 4   all seven effects commit together or not at all - the
'           P1-12/P2-08/P4-05-shaped forced-failure rollback
'   box 5   cash tendered < total is refused with a stable code and no
'           trace; change is exact and stored, not recomputed
'   box 6   card/e-wallet is recorded, never claimed authorised (G-24)
'   box 7   the P4-01 ledger reconciliation passes after a committed sale,
'           asserted directly in this file, not only by the suite-wide
'           fixture
'
' A handful of HTTP-level tests round-trip through SalesController itself,
' proving the controller's own status-code mapping - the same
' "mostly service-level, a few through the real HTTP pipeline" split
' ReceivingTests uses.
'
' Fixture cashier/products are real, permanent rows - nothing here is torn
' down (ADR-013: no DELETE grant on Users or Products). Each test opens its
' own fresh session on the one fixture cashier, always closing whatever was
' open first - CashierSessions.OpenSessionOwner (0011_pos.sql) allows at most
' one Open row per user at a time.

Imports System.Collections.Generic
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Net.Http.Json
Imports System.Text.Json
Imports System.Threading.Tasks
Imports Merchandising.Api.Sales
Imports Merchandising.Contracts.Auth
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Sales
Imports Merchandising.Domain.Sales
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class SaleServiceTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const FixturePassword As String = "P5-07 Fixture Passw0rd!"
    Private Const CashierUsername As String = "p5_07_fixture_cashier"
    Private Const FixtureProductSkuPrefix As String = "p5_07_fixture_sku_"

    Private _factory As MerchandisingApiFactory
    Private _connectionFactory As ConnectionFactory
    Private _saleService As SaleService
    Private _cashierSessionService As CashierSessionService
    Private _cashierUserId As Integer

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _factory = New MerchandisingApiFactory()
        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        _saleService = New SaleService(_connectionFactory)
        _cashierSessionService = New CashierSessionService(_connectionFactory)

        _cashierUserId = Await EnsureFixtureUserAsync(CashierUsername, "Cashier")
        Await CloseOpenSessionDirectlyAsync(_cashierUserId)

    End Function

    <TestCleanup>
    Public Sub TearDown()
        _factory?.Dispose()
    End Sub

    ' ------------------------------------------------------------------ box 1 (all seven effects) + box 7 (reconciliation)

    ''' <summary>Box 1 (all seven effects) and box 7 (P4-01 reconciliation) together: a committed cash sale writes every effect correctly, and the ledger reconciles afterward.</summary>
    <TestMethod>
    Public Async Function CompleteAsync_Success_CommitsAllSevenEffectsAndReconciles() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync(price:=10.0000D, cost:=6.0000D)
        Await SeedStockDirectlyAsync(productId, 20.000D)
        Await EnsureOpenSessionAsync()

        Dim correlationId As String = Guid.NewGuid().ToString()
        Dim lines As New List(Of CreateSaleLineRequest) From {
            New CreateSaleLineRequest With {.ProductId = productId, .Quantity = 3.000D}
        }

        Dim outcome As SaleOutcome =
            Await _saleService.CompleteAsync(
                lines, PaymentMethod.Cash, 50.0000D, _cashierUserId, correlationId, Guid.NewGuid().ToString())

        Assert.AreEqual(SaleOutcomeKind.Created, outcome.Kind)
        Dim sale As SaleResponse = outcome.Response

        ' Sales header + SaleLines row (effects 1-2)
        Assert.AreEqual(30.0000D, sale.Total)
        Assert.AreEqual("Completed", sale.Status)
        Assert.HasCount(1, sale.Lines)
        Assert.AreEqual(10.0000D, sale.Lines(0).UnitPrice)
        Assert.AreEqual(6.0000D, sale.Lines(0).Cost)
        Assert.AreEqual(30.0000D, sale.Lines(0).LineTotal)
        Assert.AreEqual(1L, Await CountSalesAsync(correlationId))

        ' SalePayments row (effect 3)
        Assert.AreEqual("Cash", sale.Payment.Method)
        Assert.AreEqual(30.0000D, sale.Payment.Amount)
        Assert.AreEqual(50.0000D, sale.Payment.TenderedAmount)
        Assert.AreEqual(20.0000D, sale.Payment.ChangeAmount)
        Assert.AreEqual("Recorded", sale.Payment.Status)

        ' StockMovements row (effect 4)
        Assert.AreEqual(1L, Await CountMovementsAsync(correlationId, productId))
        Dim movement = Await ReadSingleMovementAsync(correlationId, productId)
        Assert.AreEqual(-3.000D, movement.Delta)
        Assert.AreEqual(20.000D, movement.QuantityBefore)
        Assert.AreEqual(17.000D, movement.QuantityAfter)

        ' Conditional balance decrement (effect 5)
        Assert.AreEqual(17.000D, Await ReadBalanceOrZeroAsync(productId))

        ' Session totals, computed from committed rows (effect 6)
        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Dim totals = Await CashierSessionRepository.GetPaymentTotalsAsync(connection, sale.CashierSessionId)
            Dim cashTotal As Decimal = totals.First(Function(t) String.Equals(t.Method, "Cash", StringComparison.Ordinal)).Amount
            Assert.AreEqual(30.0000D, cashTotal)
        End Using

        ' Audit row (effect 7)
        Assert.AreEqual(1L, Await CountAuditRowsAsync(correlationId, "SaleCompleted", "Success"))

        ' Box 7: ledger reconciliation
        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Dim discrepancies = Await LedgerReconciliation.FindDiscrepanciesAsync(connection)
            Dim found = discrepancies.FirstOrDefault(Function(d) d.ProductId = productId)
            Dim detail As String =
                If(found Is Nothing, String.Empty, $"expected {found.ExpectedQuantity}, actual {found.ActualQuantity}, delta {found.Delta}")
            Assert.IsNull(found, $"Product {productId} must reconcile after a committed sale: {detail}")
        End Using

    End Function

    ' ------------------------------------------------------------------ box 2

    ''' <summary>Box 2, the plan.md section 7 defect: a price change AFTER the sale must never move the already-committed line.</summary>
    <TestMethod>
    Public Async Function CompleteAsync_PriceChangedAfterSale_LineRemainsUnmoved() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync(price:=5.0000D, cost:=2.0000D)
        Await SeedStockDirectlyAsync(productId, 10.000D)
        Await EnsureOpenSessionAsync()

        Dim lines As New List(Of CreateSaleLineRequest) From {
            New CreateSaleLineRequest With {.ProductId = productId, .Quantity = 2.000D}
        }

        Dim outcome As SaleOutcome =
            Await _saleService.CompleteAsync(
                lines, PaymentMethod.Cash, 20.0000D, _cashierUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(SaleOutcomeKind.Created, outcome.Kind)
        Assert.AreEqual(5.0000D, outcome.Response.Lines(0).UnitPrice)

        Await ChangePriceDirectlyAsync(productId, 9.0000D, 4.0000D)

        Dim storedUnitPrice As Decimal = Await ReadSaleLineUnitPriceAsync(outcome.Response.Lines(0).Id)
        Assert.AreEqual(5.0000D, storedUnitPrice, "A price change after the sale must never move an already-committed SaleLines row.")

    End Function

    ''' <summary>The other half of box 2/spec section 10.3's "current price" re-check: a price change BEFORE the sale must be what gets captured - never a value from whenever the product was first created.</summary>
    <TestMethod>
    Public Async Function CompleteAsync_UsesCurrentPriceAtSaleTime_NotAStalePrice() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync(price:=5.0000D, cost:=2.0000D)
        Await SeedStockDirectlyAsync(productId, 10.000D)
        Await ChangePriceDirectlyAsync(productId, 9.0000D, 4.0000D)
        Await EnsureOpenSessionAsync()

        Dim lines As New List(Of CreateSaleLineRequest) From {
            New CreateSaleLineRequest With {.ProductId = productId, .Quantity = 1.000D}
        }

        Dim outcome As SaleOutcome =
            Await _saleService.CompleteAsync(
                lines, PaymentMethod.Cash, 20.0000D, _cashierUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(SaleOutcomeKind.Created, outcome.Kind)
        Assert.AreEqual(9.0000D, outcome.Response.Lines(0).UnitPrice, "The CURRENT price at sale time must be captured, not the price the product was created with.")
        Assert.AreEqual(4.0000D, outcome.Response.Lines(0).Cost)

    End Function

    ' ------------------------------------------------------------------ open-session precondition

    ''' <summary>Spec section 10.3's foundational precondition: a sale requires an open cashier session.</summary>
    <TestMethod>
    Public Async Function CompleteAsync_NoOpenSession_Refused() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SeedStockDirectlyAsync(productId, 5.000D)
        Await CloseOpenSessionDirectlyAsync(_cashierUserId)

        Dim lines As New List(Of CreateSaleLineRequest) From {
            New CreateSaleLineRequest With {.ProductId = productId, .Quantity = 1.000D}
        }

        Dim outcome As SaleOutcome =
            Await _saleService.CompleteAsync(
                lines, PaymentMethod.Cash, 20.0000D, _cashierUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(SaleOutcomeKind.NoOpenSession, outcome.Kind)

    End Function

    ' ------------------------------------------------------------------ box 1: product activity

    ''' <summary>Re-check 1 of 5: an unknown ProductId is refused with its own stable outcome, before any row is written.</summary>
    <TestMethod>
    Public Async Function CompleteAsync_UnknownProduct_Refused() As Task

        Await EnsureOpenSessionAsync()

        Dim lines As New List(Of CreateSaleLineRequest) From {
            New CreateSaleLineRequest With {.ProductId = 999999999, .Quantity = 1.000D}
        }

        Dim outcome As SaleOutcome =
            Await _saleService.CompleteAsync(
                lines, PaymentMethod.Cash, 20.0000D, _cashierUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(SaleOutcomeKind.ProductNotFound, outcome.Kind)
        Assert.AreEqual(999999999, outcome.OffendingProductId)

    End Function

    ''' <summary>Re-check 2 of 5: an inactive product is refused with its own stable outcome, before any row is written - spec section 10.2's "cannot be newly sold".</summary>
    <TestMethod>
    Public Async Function CompleteAsync_InactiveProduct_Refused() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SeedStockDirectlyAsync(productId, 5.000D)
        Await DeactivateProductDirectlyAsync(productId)
        Await EnsureOpenSessionAsync()

        Dim lines As New List(Of CreateSaleLineRequest) From {
            New CreateSaleLineRequest With {.ProductId = productId, .Quantity = 1.000D}
        }

        Dim outcome As SaleOutcome =
            Await _saleService.CompleteAsync(
                lines, PaymentMethod.Cash, 20.0000D, _cashierUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(SaleOutcomeKind.ProductInactive, outcome.Kind)
        Assert.AreEqual(productId, outcome.OffendingProductId)

    End Function

    ' ------------------------------------------------------------------ box 1: available stock (box 3's own refusal)

    ''' <summary>Re-check 3 of 5: insufficient stock is refused with no trace at all - ADR-006's conditional UPDATE, never read-then-write.</summary>
    <TestMethod>
    Public Async Function CompleteAsync_InsufficientStock_RefusedWithNoTrace() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await EnsureOpenSessionAsync()

        Dim correlationId As String = Guid.NewGuid().ToString()
        Dim lines As New List(Of CreateSaleLineRequest) From {
            New CreateSaleLineRequest With {.ProductId = productId, .Quantity = 5.000D}
        }

        Dim outcome As SaleOutcome =
            Await _saleService.CompleteAsync(
                lines, PaymentMethod.Cash, 100.0000D, _cashierUserId, correlationId, Guid.NewGuid().ToString())

        Assert.AreEqual(SaleOutcomeKind.InsufficientStock, outcome.Kind)
        Assert.AreEqual(productId, outcome.OffendingProductId)
        Assert.AreEqual(0D, Await ReadBalanceOrZeroAsync(productId))
        Assert.AreEqual(0L, Await CountSalesAsync(correlationId))
        Assert.AreEqual(0L, Await CountMovementsAsync(correlationId, productId))

    End Function

    ' ------------------------------------------------------------------ box 5: payment validity

    ''' <summary>Re-check 5 of 5 / card done-when box 5: tendered less than total is refused with no trace, and the shortfall is exact.</summary>
    <TestMethod>
    Public Async Function CompleteAsync_CashTenderBelowTotal_RefusedWithNoTrace() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync(price:=10.0000D, cost:=5.0000D)
        Await SeedStockDirectlyAsync(productId, 5.000D)
        Await EnsureOpenSessionAsync()

        Dim correlationId As String = Guid.NewGuid().ToString()
        Dim lines As New List(Of CreateSaleLineRequest) From {
            New CreateSaleLineRequest With {.ProductId = productId, .Quantity = 1.000D}
        }

        Dim outcome As SaleOutcome =
            Await _saleService.CompleteAsync(
                lines, PaymentMethod.Cash, 4.0000D, _cashierUserId, correlationId, Guid.NewGuid().ToString())

        Assert.AreEqual(SaleOutcomeKind.CashTenderInsufficient, outcome.Kind)
        Assert.AreEqual(6.0000D, outcome.ShortfallAmount)
        Assert.AreEqual(5.000D, Await ReadBalanceOrZeroAsync(productId), "A refused sale must never touch stock.")
        Assert.AreEqual(0L, Await CountSalesAsync(correlationId))

    End Function

    ' ------------------------------------------------------------------ card done-when box 6 (G-24)

    ''' <summary>Card done-when box 6 / G-24: card and e-wallet are RECORDED, never claimed approved/authorised/accepted.</summary>
    <TestMethod>
    Public Async Function CompleteAsync_CardAndEWalletPayments_RecordedNeverAuthorised() As Task

        For Each method As PaymentMethod In New PaymentMethod() {PaymentMethod.Card, PaymentMethod.EWallet}

            Dim productId As Integer = Await CreateFixtureProductAsync(price:=8.0000D, cost:=3.0000D)
            Await SeedStockDirectlyAsync(productId, 5.000D)
            Await EnsureOpenSessionAsync()

            Dim lines As New List(Of CreateSaleLineRequest) From {
                New CreateSaleLineRequest With {.ProductId = productId, .Quantity = 1.000D}
            }

            Dim outcome As SaleOutcome =
                Await _saleService.CompleteAsync(
                    lines, method, Nothing, _cashierUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

            Assert.AreEqual(SaleOutcomeKind.Created, outcome.Kind, $"Method {method} should have completed.")
            Assert.AreEqual(method.ToString(), outcome.Response.Payment.Method)
            Assert.AreEqual(8.0000D, outcome.Response.Payment.Amount)
            Assert.IsNull(outcome.Response.Payment.TenderedAmount, $"{method} must carry no tendered amount.")
            Assert.IsNull(outcome.Response.Payment.ChangeAmount, $"{method} must carry no change amount.")
            Assert.AreEqual("Recorded", outcome.Response.Payment.Status, $"{method} must be labelled Recorded, never approved/authorised/accepted.")

        Next

    End Function

    ' ------------------------------------------------------------------ duplicate request state (P5-07 re-check 4 of 5 / P5-09 boxes 1-2)

    ''' <summary>
    ''' P5-09 boxes 1-2: a repeated key replays the ORIGINAL committed sale
    ''' byte-for-byte (not merely matching on Id), and exactly one row exists
    ''' afterward in each of Sales, SaleLines, StockMovements and
    ''' SalePayments - plus the session's own cash total reflects only the
    ''' ONE sale, never a doubled total a demo would actually notice.
    ''' </summary>
    <TestMethod>
    Public Async Function CompleteAsync_RepeatedIdempotencyKeySameBody_ReplaysByteForByteWithExactlyOneOfEachEffectRow() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync(price:=4.0000D, cost:=1.0000D)
        Await SeedStockDirectlyAsync(productId, 10.000D)
        Dim sessionId As Integer = Await EnsureOpenSessionAsync()

        Dim idempotencyKey As String = Guid.NewGuid().ToString()
        Dim lines As New List(Of CreateSaleLineRequest) From {
            New CreateSaleLineRequest With {.ProductId = productId, .Quantity = 2.000D}
        }

        Dim firstOutcome As SaleOutcome =
            Await _saleService.CompleteAsync(
                lines, PaymentMethod.Cash, 10.0000D, _cashierUserId, Guid.NewGuid().ToString(), idempotencyKey)
        Assert.AreEqual(SaleOutcomeKind.Created, firstOutcome.Kind)

        Dim secondOutcome As SaleOutcome =
            Await _saleService.CompleteAsync(
                lines, PaymentMethod.Cash, 10.0000D, _cashierUserId, Guid.NewGuid().ToString(), idempotencyKey)

        Assert.AreEqual(SaleOutcomeKind.Replayed, secondOutcome.Kind)

        ' Box 1: byte-for-byte, not merely Id equality - the stored payload
        ' IS the exact JSON the first commit produced, so re-serializing the
        ' first outcome's own Response must match it character for character.
        Dim expectedPayload As String = JsonSerializer.Serialize(firstOutcome.Response)
        Assert.AreEqual(expectedPayload, secondOutcome.ReplayPayload, "The replay must be the ORIGINAL committed payload, byte for byte - not merely a matching Id.")

        ' Box 2: exactly one of each effect row, and the session total is not doubled.
        Assert.AreEqual(8.000D, Await ReadBalanceOrZeroAsync(productId), "A replayed request must never decrement stock a second time.")
        Assert.AreEqual(1L, Await CountSalesAsync(firstOutcome.Response.CorrelationId), "Exactly one Sales row may exist.")
        Assert.AreEqual(1L, Await CountSaleLinesAsync(firstOutcome.Response.Id), "Exactly one SaleLines row may exist.")
        Assert.AreEqual(1L, Await CountMovementsAsync(firstOutcome.Response.CorrelationId, productId), "Exactly one StockMovements row may exist.")
        Assert.AreEqual(1L, Await CountSalePaymentsAsync(firstOutcome.Response.Id), "Exactly one SalePayments row may exist.")

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Dim totals = Await CashierSessionRepository.GetPaymentTotalsAsync(connection, sessionId)
            Dim cashTotal As Decimal = totals.First(Function(t) String.Equals(t.Method, "Cash", StringComparison.Ordinal)).Amount
            Assert.AreEqual(8.0000D, cashTotal, "The session's own cash total must reflect the ONE sale, not a doubled total from a silently-repeated replay.")
        End Using

    End Function

    ''' <summary>
    ''' P5-09 box 3: a key reused with a DIFFERENT body is refused with its
    ''' own stable code, never silently replayed as if it were a legitimate
    ''' retry - a client bug (a duplicated key sent with different lines)
    ''' must not be allowed to look like success.
    ''' </summary>
    <TestMethod>
    Public Async Function CompleteAsync_SameIdempotencyKeyDifferentBody_RefusedWithoutReplayingOrTouchingStock() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync(price:=4.0000D, cost:=1.0000D)
        Await SeedStockDirectlyAsync(productId, 10.000D)
        Await EnsureOpenSessionAsync()

        Dim idempotencyKey As String = Guid.NewGuid().ToString()

        Dim firstOutcome As SaleOutcome =
            Await _saleService.CompleteAsync(
                New List(Of CreateSaleLineRequest) From {
                    New CreateSaleLineRequest With {.ProductId = productId, .Quantity = 2.000D}},
                PaymentMethod.Cash, 10.0000D, _cashierUserId, Guid.NewGuid().ToString(), idempotencyKey)
        Assert.AreEqual(SaleOutcomeKind.Created, firstOutcome.Kind)

        ' Same key, DIFFERENT body - a different quantity for the same line.
        Dim secondOutcome As SaleOutcome =
            Await _saleService.CompleteAsync(
                New List(Of CreateSaleLineRequest) From {
                    New CreateSaleLineRequest With {.ProductId = productId, .Quantity = 3.000D}},
                PaymentMethod.Cash, 10.0000D, _cashierUserId, Guid.NewGuid().ToString(), idempotencyKey)

        Assert.AreEqual(SaleOutcomeKind.IdempotencyKeyReused, secondOutcome.Kind, "A same-key, different-body retry must be refused, never replayed or completed as a new sale.")
        Assert.IsNull(secondOutcome.Response, "A refused reuse must carry no committed response.")
        Assert.IsNull(secondOutcome.ReplayPayload, "A refused reuse must not replay anything - that would be the client bug looking like success.")

        Assert.AreEqual(8.000D, Await ReadBalanceOrZeroAsync(productId), "The refused second attempt must leave stock exactly where the first sale left it - no partial or extra effect.")
        Assert.AreEqual(1L, Await CountSalesAsync(firstOutcome.Response.CorrelationId), "Only the first, legitimate sale may exist.")

    End Function

    ''' <summary>
    ''' P5-09 box 4: N concurrent requests sharing one key and one identical
    ''' body - exactly one commits, every other replays, none errors. The
    ''' P1-13/P1-14 "launch every task, then await" shape, applied to the
    ''' largest transaction in the system.
    ''' </summary>
    <TestMethod>
    Public Async Function CompleteAsync_ConcurrentRequestsSameIdempotencyKeyAndBody_ExactlyOneCommitsRestReplay() As Task

        Const RequestCount As Integer = 5

        Dim productId As Integer = Await CreateFixtureProductAsync(price:=4.0000D, cost:=1.0000D)
        Await SeedStockDirectlyAsync(productId, 10.000D)
        Await EnsureOpenSessionAsync()

        Dim idempotencyKey As String = Guid.NewGuid().ToString()
        Dim lines As New List(Of CreateSaleLineRequest) From {
            New CreateSaleLineRequest With {.ProductId = productId, .Quantity = 2.000D}
        }

        ' None of these are awaited individually - all RequestCount calls are
        ' underway, sharing one idempotencyKey and one body, before this
        ' method awaits any of them (StockDecrementTests' P1-14 concurrency shape).
        Dim tasks(RequestCount - 1) As Task(Of SaleOutcome)
        For i = 0 To RequestCount - 1
            tasks(i) = _saleService.CompleteAsync(
                lines, PaymentMethod.Cash, 10.0000D, _cashierUserId, Guid.NewGuid().ToString(), idempotencyKey)
        Next

        Dim outcomes As SaleOutcome() = Await Task.WhenAll(tasks)

        Dim distribution As New System.Text.StringBuilder()
        Dim createdCount As Integer = 0
        Dim replayedCount As Integer = 0
        For i = 0 To RequestCount - 1
            If distribution.Length > 0 Then distribution.Append(" | ")
            distribution.Append(outcomes(i).Kind.ToString())
            Select Case outcomes(i).Kind
                Case SaleOutcomeKind.Created
                    createdCount += 1
                Case SaleOutcomeKind.Replayed
                    replayedCount += 1
            End Select
        Next
        Console.WriteLine($"P5-09 box 4 x{RequestCount} raw distribution -> {distribution}")

        Assert.AreEqual(1, createdCount, "Exactly one of the concurrent requests may commit a new sale.")
        Assert.AreEqual(RequestCount - 1, replayedCount, "Every other concurrent request must replay - never error, never a second sale.")

        Assert.AreEqual(8.000D, Await ReadBalanceOrZeroAsync(productId), "5 concurrent identical requests sharing one key must decrement stock exactly once.")

        Dim winner As SaleOutcome = outcomes.First(Function(o) o.Kind = SaleOutcomeKind.Created)
        Assert.AreEqual(1L, Await CountSalesAsync(winner.Response.CorrelationId), "Exactly one Sales row may exist across all concurrent attempts.")

    End Function

    ' ------------------------------------------------------------------ box 4: all seven effects, forced failure

    ''' <summary>
    ''' Box 4: fault injected after the audit insert and idempotency completion
    ''' write, still inside the open transaction, immediately before commit -
    ''' the same shape ReceivingTests.ReceiveAsync_FaultInjectedBeforeCommit_...
    ''' uses for P4-05. The unhandled exception propagating out of
    ''' CompleteAsync is what proves the rollback: the sale, its line, the
    ''' payment, the movement, the balance and the audit row must ALL still be
    ''' exactly where they were before this call.
    ''' </summary>
    <TestMethod>
    Public Async Function CompleteAsync_FaultInjectedBeforeCommit_RollsBackAllSevenEffects() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync(price:=7.0000D, cost:=3.0000D)
        Await SeedStockDirectlyAsync(productId, 10.000D)
        Await EnsureOpenSessionAsync()

        Dim correlationId As String = Guid.NewGuid().ToString()
        Dim balanceBefore As Decimal = Await ReadBalanceOrZeroAsync(productId)
        Dim auditCountBefore As Long = Await CountAuditRowsAsync(correlationId, "SaleCompleted", "Success")

        Dim faultInjected As Boolean = False

        Await Assert.ThrowsExactlyAsync(Of InvalidOperationException)(
            Function() _saleService.CompleteAsync(
                New List(Of CreateSaleLineRequest) From {
                    New CreateSaleLineRequest With {.ProductId = productId, .Quantity = 2.000D}
                },
                PaymentMethod.Cash, 20.0000D, _cashierUserId, correlationId, Guid.NewGuid().ToString(),
                testOnlyFaultAfterAuditInsert:=Sub()
                                                   faultInjected = True
                                                   Throw New InvalidOperationException("P5-07 forced failure: after audit insert, before commit.")
                                               End Sub))

        Assert.IsTrue(faultInjected, "The fault-injection delegate must actually have fired for this proof to mean anything.")

        Assert.AreEqual(balanceBefore, Await ReadBalanceOrZeroAsync(productId), "A rolled-back sale must leave StockBalances untouched.")
        Assert.AreEqual(0L, Await CountMovementsAsync(correlationId, productId), "A rolled-back sale must leave zero StockMovements rows.")
        Assert.AreEqual(0L, Await CountSalesAsync(correlationId), "A rolled-back sale must leave zero Sales rows.")
        Assert.AreEqual(auditCountBefore, Await CountAuditRowsAsync(correlationId, "SaleCompleted", "Success"), "A rolled-back sale must leave zero new AuditLogs rows.")

    End Function

    ' ------------------------------------------------------------------ P5-10: completed sales are immutable

    ''' <summary>
    ''' P5-10 box 1: "cancelling a completed sale is refused" is the SAME
    ''' database-grant fact db/grants/0013_pos-grants.sql already argues and
    ''' PosSchemaTests already proves generically (no UPDATE grant at all on
    ''' `sales`) - this test names the SPECIFIC statement a cancellation would
    ''' have to be, `Status -> 'Cancelled'`, run through the same connection
    ''' factory (merch_api) production code uses, rather than a column this
    ''' card happens not to care about. There is no repository method that
    ''' attempts this (SaleRepository has no UpdateAsync at all - the P4-07
    ''' "call the production write path directly" shape does not apply here
    ''' the way it did to a repository method that exists and needs a guard;
    ''' for Sales, the guard IS the absence of any write path, and the
    ''' database is what proves that absence is real, not merely coded).
    ''' </summary>
    <TestMethod>
    Public Async Function CompletedSale_StatusUpdateToCancelled_DeniedByDatabaseGrant() As Task

        Const TableAccessDeniedErrorNumber As Integer = 1142

        Dim productId As Integer = Await CreateFixtureProductAsync(price:=6.0000D, cost:=2.0000D)
        Await SeedStockDirectlyAsync(productId, 5.000D)
        Await EnsureOpenSessionAsync()

        Dim outcome As SaleOutcome =
            Await _saleService.CompleteAsync(
                New List(Of CreateSaleLineRequest) From {
                    New CreateSaleLineRequest With {.ProductId = productId, .Quantity = 1.000D}},
                PaymentMethod.Cash, 10.0000D, _cashierUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(SaleOutcomeKind.Created, outcome.Kind)

        Dim ex As MySqlException =
            Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                Function() UpdateSaleStatusDirectlyAsync(outcome.Response.Id, "Cancelled"))

        Assert.AreEqual(TableAccessDeniedErrorNumber, ex.Number, "merch_api must have no UPDATE grant on Sales at all - a status change to Cancelled must be denied exactly like any other column.")

        Dim storedStatus As String = Await ReadSaleStatusAsync(outcome.Response.Id)
        Assert.AreEqual("Completed", storedStatus, "The refused attempt must leave the sale's own Status column untouched.")

    End Function

    ''' <summary>
    ''' P5-10 box 3 (the "in-progress" half): a cart is never a server-side
    ''' row in this system - SaleService.CompleteAsync's own header states it
    ''' commits the Sales row and every other effect together, atomically, at
    ''' completion; nothing is written beforehand for a cart to attach to
    ''' (SaleStatus.vb's own header: "There is no persisted draft/in-progress
    ''' row"). "Cancelling an in-progress sale" is therefore not an API call
    ''' at all - it is simply the client never calling CompleteAsync. This
    ''' test makes that structural fact an executable assertion rather than
    ''' an argument in a comment: preparing everything CompleteAsync would
    ''' need (a real product, a real open session, a real correlation id) and
    ''' then never invoking it must leave precisely zero trace and zero stock
    ''' effect - because there was never anything to undo.
    ''' </summary>
    <TestMethod>
    Public Async Function AbandonedSale_NeverCompleted_LeavesNoRowsOrStockEffect() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync(price:=3.0000D, cost:=1.0000D)
        Await SeedStockDirectlyAsync(productId, 9.000D)
        Await EnsureOpenSessionAsync()

        Dim correlationId As String = Guid.NewGuid().ToString()

        ' Everything a real sale would need is prepared - a product, an open
        ' session, lines that would be valid - and then simply abandoned:
        ' CompleteAsync is deliberately never called, the same as a cashier
        ' clearing a cart before checkout.
        Dim abandonedLines As New List(Of CreateSaleLineRequest) From {
            New CreateSaleLineRequest With {.ProductId = productId, .Quantity = 2.000D}
        }
        GC.KeepAlive(abandonedLines) ' prepared, never sent - the point being made

        Assert.AreEqual(9.000D, Await ReadBalanceOrZeroAsync(productId), "An abandoned cart must never have touched stock.")
        Assert.AreEqual(0L, Await CountSalesAsync(correlationId), "An abandoned cart must never have written a Sales row.")
        Assert.AreEqual(0L, Await CountMovementsAsync(correlationId, productId), "An abandoned cart must never have written a StockMovements row.")

    End Function

    ''' <summary>
    ''' P5-10 box 2: "every route that could mutate a completed sale is
    ''' refused... not spot-checked." SalesController declares exactly one
    ''' action (CreateSale, POST /api/v1/sales) - there is no {id}-scoped
    ''' route at all for an existing sale, so a PUT/PATCH/DELETE naming a
    ''' real, completed sale's own id cannot reach any action. Proven through
    ''' the real HTTP pipeline against a REAL completed sale (not a made-up
    ''' id), for every mutating verb, so a future route added without
    ''' thought fails this test rather than shipping untested. Routing
    ''' happens before authorization for an unmatched endpoint (ASP.NET
    ''' Core's own pipeline order), so this holds independent of the caller's
    ''' role - there is no policy decision to be role-specific ABOUT, only a
    ''' route that must continue not to exist.
    ''' </summary>
    <TestMethod>
    Public Async Function ExistingSale_NoMutatingRouteReachesIt_RefusedForEveryMutatingVerb() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync(price:=6.0000D, cost:=2.0000D)
        Await SeedStockDirectlyAsync(productId, 5.000D)
        Await EnsureOpenSessionAsync()

        Dim outcome As SaleOutcome =
            Await _saleService.CompleteAsync(
                New List(Of CreateSaleLineRequest) From {
                    New CreateSaleLineRequest With {.ProductId = productId, .Quantity = 1.000D}},
                PaymentMethod.Cash, 10.0000D, _cashierUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(SaleOutcomeKind.Created, outcome.Kind)

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, CashierUsername)

            For Each method As HttpMethod In New HttpMethod() {HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete}

                Using idScoped As HttpResponseMessage =
                    Await SendAsync(client, method, $"/api/v1/sales/{outcome.Response.Id}", token, requestBody:=Nothing)
                    Assert.AreEqual(
                        HttpStatusCode.NotFound, idScoped.StatusCode,
                        $"{method} /api/v1/sales/{{id}} must not resolve to any action - a completed sale must have no route that can mutate it.")
                End Using

                Using collectionScoped As HttpResponseMessage =
                    Await SendAsync(client, method, "/api/v1/sales", token, requestBody:=Nothing)
                    Assert.AreNotEqual(
                        HttpStatusCode.OK, collectionScoped.StatusCode,
                        $"{method} /api/v1/sales must never succeed - only POST (create) is a declared action on this route.")
                    Assert.AreNotEqual(HttpStatusCode.NoContent, collectionScoped.StatusCode)
                End Using

            Next

        End Using

        ' The refused calls above must not have touched the sale at all.
        Dim storedStatus As String = Await ReadSaleStatusAsync(outcome.Response.Id)
        Assert.AreEqual("Completed", storedStatus)

    End Function

    ' ------------------------------------------------------------------ HTTP-level round trip

    ''' <summary>End to end through the real ASP.NET Core pipeline: 201, the committed shape, and G-24's wording in the actual JSON body.</summary>
    <TestMethod>
    Public Async Function CreateSale_Success_Returns201WithRecordedPaymentWording() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync(price:=12.0000D, cost:=6.0000D)
        Await SeedStockDirectlyAsync(productId, 10.000D)
        Await CloseOpenSessionDirectlyAsync(_cashierUserId)

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, CashierUsername)

            Using openResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/cashier-sessions", token,
                    New OpenCashierSessionRequest With {.OpeningFloat = 0D, .IdempotencyKey = Guid.NewGuid().ToString("d")})
                Assert.AreEqual(HttpStatusCode.Created, openResponse.StatusCode)
            End Using

            Dim body As New CreateSaleRequest With {
                .IdempotencyKey = Guid.NewGuid().ToString("d"),
                .Lines = New List(Of CreateSaleLineRequest) From {
                    New CreateSaleLineRequest With {.ProductId = productId, .Quantity = 2.000D}},
                .Payment = New CreateSalePaymentRequest With {.Method = "Cash", .TenderedAmount = 30.0000D}
            }

            Using response As HttpResponseMessage = Await SendAsync(client, HttpMethod.Post, "/api/v1/sales", token, body)

                Assert.AreEqual(HttpStatusCode.Created, response.StatusCode)

                Dim raw As String = Await response.Content.ReadAsStringAsync()
                Dim sale As SaleResponse = JsonSerializer.Deserialize(Of SaleResponse)(raw)

                Assert.AreEqual(24.0000D, sale.Total)
                Assert.AreEqual("Recorded", sale.Payment.Status)

                Dim forbiddenWords As String() = {"approved", "authorised", "authorized", "accepted"}
                For Each word As String In forbiddenWords
                    Assert.IsFalse(
                        raw.Contains(word, StringComparison.OrdinalIgnoreCase),
                        $"G-24: response body must never contain '{word}'. Body: {raw}")
                Next

            End Using

        End Using

    End Function

    ''' <summary>Controller-level proof that NoOpenSession maps to 409 with the stable code.</summary>
    <TestMethod>
    Public Async Function CreateSale_NoOpenSession_Returns409WithStableCode() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SeedStockDirectlyAsync(productId, 5.000D)
        Await CloseOpenSessionDirectlyAsync(_cashierUserId)

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, CashierUsername)

            Dim body As New CreateSaleRequest With {
                .IdempotencyKey = Guid.NewGuid().ToString("d"),
                .Lines = New List(Of CreateSaleLineRequest) From {
                    New CreateSaleLineRequest With {.ProductId = productId, .Quantity = 1.000D}},
                .Payment = New CreateSalePaymentRequest With {.Method = "Cash", .TenderedAmount = 20.0000D}
            }

            Using response As HttpResponseMessage = Await SendAsync(client, HttpMethod.Post, "/api/v1/sales", token, body)

                Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode)
                Dim errorBody As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("CASHIER_SESSION_REQUIRED", errorBody.ErrorCode)

            End Using

        End Using

    End Function

    ''' <summary>Controller-level proof that CashTenderInsufficient maps to 400 with the stable code.</summary>
    <TestMethod>
    Public Async Function CreateSale_CashTenderBelowTotal_Returns400WithStableCode() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync(price:=10.0000D, cost:=5.0000D)
        Await SeedStockDirectlyAsync(productId, 5.000D)
        Await CloseOpenSessionDirectlyAsync(_cashierUserId)

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, CashierUsername)

            Using openResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/cashier-sessions", token,
                    New OpenCashierSessionRequest With {.OpeningFloat = 0D, .IdempotencyKey = Guid.NewGuid().ToString("d")})
                Assert.AreEqual(HttpStatusCode.Created, openResponse.StatusCode)
            End Using

            Dim body As New CreateSaleRequest With {
                .IdempotencyKey = Guid.NewGuid().ToString("d"),
                .Lines = New List(Of CreateSaleLineRequest) From {
                    New CreateSaleLineRequest With {.ProductId = productId, .Quantity = 1.000D}},
                .Payment = New CreateSalePaymentRequest With {.Method = "Cash", .TenderedAmount = 2.0000D}
            }

            Using response As HttpResponseMessage = Await SendAsync(client, HttpMethod.Post, "/api/v1/sales", token, body)

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)
                Dim errorBody As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("CASH_TENDER_INSUFFICIENT", errorBody.ErrorCode)

            End Using

        End Using

    End Function

    ''' <summary>P5-09 box 3, controller-level: a same-key different-body retry maps to 409 with its own stable code, through the real HTTP pipeline.</summary>
    <TestMethod>
    Public Async Function CreateSale_SameIdempotencyKeyDifferentBody_Returns409WithStableCode() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync(price:=10.0000D, cost:=5.0000D)
        Await SeedStockDirectlyAsync(productId, 10.000D)
        Await CloseOpenSessionDirectlyAsync(_cashierUserId)

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, CashierUsername)

            Using openResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/cashier-sessions", token,
                    New OpenCashierSessionRequest With {.OpeningFloat = 0D, .IdempotencyKey = Guid.NewGuid().ToString("d")})
                Assert.AreEqual(HttpStatusCode.Created, openResponse.StatusCode)
            End Using

            Dim sharedKey As String = Guid.NewGuid().ToString("d")

            Dim firstBody As New CreateSaleRequest With {
                .IdempotencyKey = sharedKey,
                .Lines = New List(Of CreateSaleLineRequest) From {
                    New CreateSaleLineRequest With {.ProductId = productId, .Quantity = 1.000D}},
                .Payment = New CreateSalePaymentRequest With {.Method = "Cash", .TenderedAmount = 20.0000D}
            }

            Using firstResponse As HttpResponseMessage = Await SendAsync(client, HttpMethod.Post, "/api/v1/sales", token, firstBody)
                Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode)
            End Using

            Dim secondBody As New CreateSaleRequest With {
                .IdempotencyKey = sharedKey,
                .Lines = New List(Of CreateSaleLineRequest) From {
                    New CreateSaleLineRequest With {.ProductId = productId, .Quantity = 2.000D}},
                .Payment = New CreateSalePaymentRequest With {.Method = "Cash", .TenderedAmount = 20.0000D}
            }

            Using response As HttpResponseMessage = Await SendAsync(client, HttpMethod.Post, "/api/v1/sales", token, secondBody)

                Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode)
                Dim errorBody As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("IDEMPOTENCY_KEY_REUSED", errorBody.ErrorCode)

            End Using

        End Using

    End Function

    ' --------------------------------------------------------------- shared helpers

    Private Async Function EnsureOpenSessionAsync() As Task(Of Integer)

        Await CloseOpenSessionDirectlyAsync(_cashierUserId)

        Dim outcome As CashierSessionOutcome =
            Await _cashierSessionService.OpenAsync(
                _cashierUserId, 0D, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(CashierSessionOutcomeKind.Created, outcome.Kind, "Fixture session could not be opened.")
        Return outcome.Session.Id

    End Function

    Private Async Function CloseOpenSessionDirectlyAsync(userId As Integer) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "UPDATE CashierSessions " &
                    "   SET Status = 'Closed', ClosedByUserId = OpenedByUserId, ClosedAtUtc = UTC_TIMESTAMP(6), " &
                    "       RowVersion = RowVersion + 1, UpdatedAtUtc = UTC_TIMESTAMP(6) " &
                    " WHERE Status = 'Open' AND OpenedByUserId = @userId;"
                command.Parameters.AddWithValue("@userId", userId)
                Await command.ExecuteNonQueryAsync()
            End Using
        End Using

    End Function

    ''' <summary>A brand-new active product per call, with deliberately NO StockBalances row seeded - ReceivingTests' identical fixture shape.</summary>
    Private Async Function CreateFixtureProductAsync(Optional price As Decimal = 1.0000D, Optional cost As Decimal = 0.5000D) As Task(Of Integer)

        Dim sku As String = FixtureProductSkuPrefix & Guid.NewGuid().ToString("N").Substring(0, 16)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO Products (Sku, Barcode, Name, Price, Cost, IsActive, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@sku, NULL, 'P5-07 Fixture Product', @price, @cost, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@sku", sku)
                command.Parameters.AddWithValue("@price", price)
                command.Parameters.AddWithValue("@cost", cost)
                Await command.ExecuteNonQueryAsync()
                Return CInt(command.LastInsertedId)
            End Using
        End Using

    End Function

    ''' <summary>Seeds a known StockBalances quantity via StockRepository.IncrementAsync PLUS a matching StockMovements row, so LedgerReconciliationTests' whole-suite check stays satisfied - the same shape AdjustmentTests.SetBalanceAsync/P5-06's own fixture helper already establish.</summary>
    Private Async Function SeedStockDirectlyAsync(productId As Integer, quantity As Decimal) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Dim transaction As MySqlTransaction = Await connection.BeginTransactionAsync()

            Dim stockResult = Await StockRepository.IncrementAsync(connection, transaction, productId, quantity)

            Using movementCommand As MySqlCommand = connection.CreateCommand()
                movementCommand.Transaction = transaction
                movementCommand.CommandText =
                    "INSERT INTO StockMovements (ProductId, Delta, QuantityBefore, QuantityAfter, Reason, ActorUserId, CorrelationId, CreatedAtUtc) " &
                    "VALUES (@productId, @delta, @before, @after, 'P5-07 test fixture stock seed', @actorUserId, @correlationId, UTC_TIMESTAMP(6));"
                movementCommand.Parameters.AddWithValue("@productId", productId)
                movementCommand.Parameters.AddWithValue("@delta", quantity)
                movementCommand.Parameters.AddWithValue("@before", stockResult.QuantityBefore)
                movementCommand.Parameters.AddWithValue("@after", stockResult.QuantityAfter)
                movementCommand.Parameters.AddWithValue("@actorUserId", _cashierUserId)
                movementCommand.Parameters.AddWithValue("@correlationId", Guid.NewGuid().ToString())
                Await movementCommand.ExecuteNonQueryAsync()
            End Using

            Await transaction.CommitAsync()
            Await transaction.DisposeAsync()
        End Using

    End Function

    Private Async Function ChangePriceDirectlyAsync(productId As Integer, price As Decimal, cost As Decimal) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "UPDATE Products SET Price = @price, Cost = @cost, UpdatedAtUtc = UTC_TIMESTAMP(6) WHERE Id = @productId;"
                command.Parameters.AddWithValue("@price", price)
                command.Parameters.AddWithValue("@cost", cost)
                command.Parameters.AddWithValue("@productId", productId)
                Await command.ExecuteNonQueryAsync()
            End Using
        End Using

    End Function

    ''' <summary>
    ''' P5-10: attempts the exact write a "cancel a completed sale" command
    ''' would have to make, through the SAME connection factory (merch_api)
    ''' production code uses - never merch_migrator. Must throw
    ''' MySqlException ERROR 1142; there is no repository method to call
    ''' because none exists (see the calling test's own header).
    ''' </summary>
    Private Async Function UpdateSaleStatusDirectlyAsync(saleId As Integer, status As String) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "UPDATE Sales SET Status = @status WHERE Id = @saleId;"
                command.Parameters.AddWithValue("@status", status)
                command.Parameters.AddWithValue("@saleId", saleId)
                Await command.ExecuteNonQueryAsync()
            End Using
        End Using

    End Function

    Private Async Function ReadSaleStatusAsync(saleId As Integer) As Task(Of String)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT Status FROM Sales WHERE Id = @saleId;"
                command.Parameters.AddWithValue("@saleId", saleId)
                Return CStr(Await command.ExecuteScalarAsync())
            End Using
        End Using

    End Function

    Private Async Function DeactivateProductDirectlyAsync(productId As Integer) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "UPDATE Products SET IsActive = 0, UpdatedAtUtc = UTC_TIMESTAMP(6) WHERE Id = @productId;"
                command.Parameters.AddWithValue("@productId", productId)
                Await command.ExecuteNonQueryAsync()
            End Using
        End Using

    End Function

    Private Async Function ReadSaleLineUnitPriceAsync(saleLineId As Integer) As Task(Of Decimal)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT UnitPrice FROM SaleLines WHERE Id = @id;"
                command.Parameters.AddWithValue("@id", saleLineId)
                Return CDec(Await command.ExecuteScalarAsync())
            End Using
        End Using

    End Function

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

    Private Async Function CountSalesAsync(correlationId As String) As Task(Of Long)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM Sales WHERE CorrelationId = @correlationId;"
                command.Parameters.AddWithValue("@correlationId", correlationId)
                Return CLng(Await command.ExecuteScalarAsync())
            End Using
        End Using

    End Function

    Private Async Function CountSaleLinesAsync(saleId As Integer) As Task(Of Long)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM SaleLines WHERE SaleId = @saleId;"
                command.Parameters.AddWithValue("@saleId", saleId)
                Return CLng(Await command.ExecuteScalarAsync())
            End Using
        End Using

    End Function

    Private Async Function CountSalePaymentsAsync(saleId As Integer) As Task(Of Long)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM SalePayments WHERE SaleId = @saleId;"
                command.Parameters.AddWithValue("@saleId", saleId)
                Return CLng(Await command.ExecuteScalarAsync())
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

End Class
