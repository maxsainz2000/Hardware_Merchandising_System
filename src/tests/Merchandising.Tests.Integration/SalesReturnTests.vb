' Merchandising.Tests.Integration.SalesReturnTests
'
' P5-11: SalesReturnService.{RecordAsync,ApproveExceptionalAsync,
' RejectExceptionalAsync} and the SalesReturnsController routes, proven
' against the real pinned MariaDB - spec section 10.3's "A completed-sale
' return must identify the original sale line, cannot exceed the quantity
' sold minus prior returns, and records whether the returned item is
' eligible to re-enter stock. Payment reversal is recorded operationally."
'
' Role gating (SalesReturns.Create/SalesReturns.ApproveExceptional resolve
' to the roles PolicyRegistry says they do) is AuthorizationMatrixTests'
' job. This file proves the BEHAVIOUR:
'
'   box 1   the bound is computed server-side from committed SaleLines/
'           SalesReturnLines rows, including the case where two PRIOR
'           partial returns together already exhaust the bound
'   box 2   RestocksItem=True writes a stock-in movement and moves the
'           balance; RestocksItem=False writes NO movement at all, and the
'           bound still accounts for the quantity either way
'   box 3   concurrent returns against the SAME sale line cannot exceed the
'           bound - proven under real concurrent load, the P1-13/P4-08 shape
'   box 4   the refund record never claims an external reversal (G-24)
'   box 5   below the configured threshold a return completes immediately,
'           with RefundMethod/RefundAmount set and no approver; at or above
'           it, it lands PendingApproval with NEITHER a stock NOR a refund
'           effect until SalesReturns.ApproveExceptional/RejectExceptional
'           decide it
'   box 6   self-approval is refused 403 with two real users (ADR-017
'           section 6, reusing AdjustmentsController's exact mechanism); a
'           DIFFERENT authorized user approving the same return succeeds
'           and applies the deferred stock effect; rejecting leaves no
'           stock or refund effect at all
'   box 7   the P4-01 ledger reconciliation passes after every return
'
' Fixture users, and every product this file creates, are real, permanent
' rows (ADR-013). A fresh product per test that needs to control its own
' sale/return bound exactly - the same "create fresh, never delete" shape
' AdjustmentTests/PurchaseReturnTests use. Sales are completed through the
' real SaleService/CashierSessionService (never seeded directly) so a
' return's SaleLines row is exactly what a real sale would produce.

Imports System.Collections.Generic
Imports System.IO
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
Imports Merchandising.Domain.Configuration
Imports Merchandising.Domain.Sales
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class SalesReturnTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const FixturePassword As String = "P5-11 Fixture Passw0rd!"
    Private Const CashierUsername As String = "p5_11_fixture_cashier"
    Private Const AdminAUsername As String = "p5_11_fixture_admin_a"
    Private Const AdminBUsername As String = "p5_11_fixture_admin_b"
    Private Const InventoryUsername As String = "p5_11_fixture_inventory"
    Private Const FixtureProductSkuPrefix As String = "p5_11_fixture_sku_"

    ''' <summary>High enough that every return in this file completes immediately unless a test lowers it explicitly - threshold routing itself is covered by its own dedicated tests, not incidental to every other one (P4-15/SaleConcurrencyTests' identical reasoning).</summary>
    Private Const ImmediateCompleteThreshold As Decimal = 100000.0000D

    Private _factory As MerchandisingApiFactory
    Private _connectionFactory As ConnectionFactory
    Private _saleService As SaleService
    Private _cashierSessionService As CashierSessionService
    Private _salesReturnService As SalesReturnService
    Private _cashierUserId As Integer
    Private _adminAUserId As Integer
    Private _adminBUserId As Integer
    Private _inventoryUserId As Integer

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _factory = New MerchandisingApiFactory()
        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        _saleService = New SaleService(_connectionFactory)
        _cashierSessionService = New CashierSessionService(_connectionFactory)
        _salesReturnService = New SalesReturnService(_connectionFactory)

        _cashierUserId = Await EnsureFixtureUserAsync(CashierUsername, "Cashier")
        _adminAUserId = Await EnsureFixtureUserAsync(AdminAUsername, "Admin")
        _adminBUserId = Await EnsureFixtureUserAsync(AdminBUsername, "Admin")
        _inventoryUserId = Await EnsureFixtureUserAsync(InventoryUsername, "InventoryClerk")

        Await SetThresholdAsync(ImmediateCompleteThreshold)
        Await CloseAnyOpenFixtureCashierSessionDirectlyAsync()

    End Function

    <TestCleanup>
    Public Sub TearDown()
        _factory?.Dispose()
    End Sub

    ' -------------------------------------------------------------- box 1

    ''' <summary>Returning more than sold is refused, and nothing is written.</summary>
    <TestMethod>
    Public Async Function RecordAsync_OverReturned_Refused() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim sale = Await CompleteFixtureSaleAsync(productId, 5.000D)

        Dim outcome As SalesReturnOutcome = Await RecordReturnAsync(sale.SaleId, sale.SaleLineId, 6.000D, restocksItem:=True)

        Assert.AreEqual(SalesReturnOutcomeKind.OverReturned, outcome.Kind)
        Assert.AreEqual(0L, Await CountSalesReturnRowsAsync(sale.SaleId))

    End Function

    ''' <summary>Two prior partial returns that together exhaust the bound must refuse a third - the aggregate case a single-row CHECK cannot see.</summary>
    <TestMethod>
    Public Async Function RecordAsync_TwoPriorPartialReturnsExhaustBound_ThirdRefused() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim sale = Await CompleteFixtureSaleAsync(productId, 5.000D)

        Dim first As SalesReturnOutcome = Await RecordReturnAsync(sale.SaleId, sale.SaleLineId, 2.000D, restocksItem:=True)
        Assert.AreEqual(SalesReturnOutcomeKind.Created, first.Kind)

        Dim second As SalesReturnOutcome = Await RecordReturnAsync(sale.SaleId, sale.SaleLineId, 3.000D, restocksItem:=True)
        Assert.AreEqual(SalesReturnOutcomeKind.Created, second.Kind)

        Dim third As SalesReturnOutcome = Await RecordReturnAsync(sale.SaleId, sale.SaleLineId, 0.001D, restocksItem:=True)
        Assert.AreEqual(SalesReturnOutcomeKind.OverReturned, third.Kind, "5.000 sold, 5.000 already returned across two prior returns - nothing remains.")

        Await AssertLedgerReconcilesAsync(productId)

    End Function

    ''' <summary>A SaleLineId naming a different sale entirely is refused as LineNotFound, never treated as belonging to the route's sale.</summary>
    <TestMethod>
    Public Async Function RecordAsync_SaleLineBelongsToDifferentSale_Refused() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim saleA = Await CompleteFixtureSaleAsync(productId, 2.000D)
        Dim saleB = Await CompleteFixtureSaleAsync(productId, 2.000D)

        Dim outcome As SalesReturnOutcome = Await RecordReturnAsync(saleA.SaleId, saleB.SaleLineId, 1.000D, restocksItem:=True)

        Assert.AreEqual(SalesReturnOutcomeKind.LineNotFound, outcome.Kind)

    End Function

    ' -------------------------------------------------------------- box 2

    ''' <summary>RestocksItem=True writes exactly one stock-in movement and moves the balance by the returned quantity.</summary>
    <TestMethod>
    Public Async Function RecordAsync_RestocksItemTrue_WritesMovementAndUpdatesBalance() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim sale = Await CompleteFixtureSaleAsync(productId, 5.000D)
        Dim balanceAfterSale As Decimal = Await ReadBalanceAsync(productId)

        Dim outcome As SalesReturnOutcome = Await RecordReturnAsync(sale.SaleId, sale.SaleLineId, 2.000D, restocksItem:=True)

        Assert.AreEqual(SalesReturnOutcomeKind.Created, outcome.Kind)
        Assert.AreEqual("Completed", outcome.Response.Status)
        Assert.AreEqual(balanceAfterSale + 2.000D, Await ReadBalanceAsync(productId))
        Assert.AreEqual(1L, Await CountMovementRowsAsync(productId, "SalesReturn"))

        Await AssertLedgerReconcilesAsync(productId)

    End Function

    ''' <summary>RestocksItem=False writes NO movement and has no stock effect at all, but the bound still accounts for the returned quantity.</summary>
    <TestMethod>
    Public Async Function RecordAsync_RestocksItemFalse_WritesNoMovementButBoundStillAccounts() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim sale = Await CompleteFixtureSaleAsync(productId, 5.000D)
        Dim balanceAfterSale As Decimal = Await ReadBalanceAsync(productId)

        Dim outcome As SalesReturnOutcome = Await RecordReturnAsync(sale.SaleId, sale.SaleLineId, 3.000D, restocksItem:=False)

        Assert.AreEqual(SalesReturnOutcomeKind.Created, outcome.Kind)
        Assert.AreEqual(balanceAfterSale, Await ReadBalanceAsync(productId), "RestocksItem=False must have no stock effect.")
        Assert.AreEqual(0L, Await CountMovementRowsAsync(productId, "SalesReturn"))

        ' Only 2.000 remains returnable (5.000 sold - 3.000 already returned, restocked or not).
        Dim tooMuch As SalesReturnOutcome = Await RecordReturnAsync(sale.SaleId, sale.SaleLineId, 2.001D, restocksItem:=True)
        Assert.AreEqual(SalesReturnOutcomeKind.OverReturned, tooMuch.Kind)

        Dim justRight As SalesReturnOutcome = Await RecordReturnAsync(sale.SaleId, sale.SaleLineId, 2.000D, restocksItem:=True)
        Assert.AreEqual(SalesReturnOutcomeKind.Created, justRight.Kind)

        Await AssertLedgerReconcilesAsync(productId)

    End Function

    ' -------------------------------------------------------------- box 3

    ''' <summary>N simultaneous returns of 2.000 each against a sale line holding exactly 5.000 sold - exactly 2 can commit (4.000) before the bound refuses the rest, never more, and never an unexpected exception (the P1-13/P4-08 shape).</summary>
    <TestMethod>
    Public Async Function RecordAsync_ConcurrentReturnsAgainstSameLine_CannotExceedBound() As Task

        Const RequestCount As Integer = 5
        Const QuantityPerAttempt As Decimal = 2.000D

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim sale = Await CompleteFixtureSaleAsync(productId, 5.000D)

        Dim tasks(RequestCount - 1) As Task(Of SalesReturnOutcome)
        For i = 0 To RequestCount - 1
            tasks(i) = RecordReturnAsync(sale.SaleId, sale.SaleLineId, QuantityPerAttempt, restocksItem:=True)
        Next

        Dim results As SalesReturnOutcome() = Await Task.WhenAll(tasks)

        Dim createdCount As Integer = results.Count(Function(r) r.Kind = SalesReturnOutcomeKind.Created)
        Dim overReturnedCount As Integer = results.Count(Function(r) r.Kind = SalesReturnOutcomeKind.OverReturned)

        Console.WriteLine($"P5-11 concurrency distribution -> created:{createdCount} overReturned:{overReturnedCount}")

        Assert.AreEqual(RequestCount, createdCount + overReturnedCount, "Every attempt must land on a controlled outcome, never an unexpected exception.")
        Assert.AreEqual(2, createdCount, "5.000 sold / 2.000 per attempt: exactly 2 can commit without exceeding the bound.")

        Dim totalReturned As Decimal = Await SumReturnedQuantityAsync(sale.SaleLineId)
        Assert.IsLessThanOrEqualTo(5.000D, totalReturned, "The sum of committed returns must never exceed what was sold.")
        Assert.AreEqual(createdCount * QuantityPerAttempt, totalReturned)

        Await AssertLedgerReconcilesAsync(productId)

    End Function

    ' -------------------------------------------------------------- box 4

    ''' <summary>G-24: the committed response never claims an external bank/terminal reversal - it is recorded, not reversed.</summary>
    <TestMethod>
    Public Async Function RecordAsync_RefundRecord_NeverClaimsExternalReversal() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim sale = Await CompleteFixtureSaleAsync(productId, 2.000D)

        Dim outcome As SalesReturnOutcome = Await RecordReturnAsync(sale.SaleId, sale.SaleLineId, 2.000D, restocksItem:=True)
        Assert.AreEqual(SalesReturnOutcomeKind.Created, outcome.Kind)

        Dim serialized As String = JsonSerializer.Serialize(outcome.Response)
        Dim forbiddenFragments As String() = {"Reversed", "Bank", "Terminal", "Authorized", "Authorised"}

        For Each fragment As String In forbiddenFragments
            Assert.IsFalse(
                serialized.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                $"Response leaked '{fragment}' - the refund is recorded operationally, never claimed as an external reversal (G-24). Body: {serialized}")
        Next

    End Function

    ' -------------------------------------------------------------- box 5

    ''' <summary>Below the configured threshold, a return completes immediately - RefundMethod/RefundAmount set, no approver, stock effect applied in the same transaction.</summary>
    <TestMethod>
    Public Async Function RecordAsync_BelowThreshold_CompletesImmediately() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim sale = Await CompleteFixtureSaleAsync(productId, 2.000D)

        Dim outcome As SalesReturnOutcome = Await RecordReturnAsync(sale.SaleId, sale.SaleLineId, 2.000D, restocksItem:=True)

        Assert.AreEqual(SalesReturnOutcomeKind.Created, outcome.Kind)
        Assert.AreEqual("Completed", outcome.Response.Status)
        Assert.IsFalse(outcome.Response.ExceedsThreshold)
        Assert.IsNotNull(outcome.Response.RefundAmount)
        Assert.AreEqual("Cash", outcome.Response.RefundMethod)
        Assert.IsNull(outcome.Response.ApprovedByUserId, "Nobody approved a within-scope return.")

    End Function

    ''' <summary>At or above the configured threshold, a return lands PendingApproval with NEITHER a stock NOR a refund effect - CLAUDE.md section 5's atomicity rule applied to the deferred half.</summary>
    <TestMethod>
    Public Async Function RecordAsync_AtOrAboveThreshold_LandsPendingApprovalWithNoStockOrRefundEffect() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SetThresholdAsync(0.0100D)
        Dim sale = Await CompleteFixtureSaleAsync(productId, 2.000D)
        Dim balanceAfterSale As Decimal = Await ReadBalanceAsync(productId)

        Dim outcome As SalesReturnOutcome = Await RecordReturnAsync(sale.SaleId, sale.SaleLineId, 2.000D, restocksItem:=True)

        Assert.AreEqual(SalesReturnOutcomeKind.Created, outcome.Kind)
        Assert.AreEqual("PendingApproval", outcome.Response.Status)
        Assert.IsTrue(outcome.Response.ExceedsThreshold)
        Assert.IsNull(outcome.Response.RefundAmount)
        Assert.IsNull(outcome.Response.RefundMethod)
        Assert.IsNull(outcome.Response.ApprovedByUserId)

        Assert.AreEqual(balanceAfterSale, Await ReadBalanceAsync(productId), "PendingApproval must have no stock effect yet.")
        Assert.AreEqual(0L, Await CountMovementRowsAsync(productId, "SalesReturn"))

    End Function

    ' -------------------------------------------------------------- box 6 (HTTP)

    ''' <summary>Box 6: the SAME user who requested a PendingApproval return is refused 403 approving it, and the refusal is audited.</summary>
    <TestMethod>
    Public Async Function ApproveExceptional_SameUserAsRequester_Refused403AndAudited() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SetThresholdAsync(0.0100D)

        Using client As HttpClient = _factory.CreateClient()

            Dim adminAToken As String = Await LoginAsync(client, AdminAUsername)
            Dim sale = Await CompleteFixtureSaleAsync(productId, 2.000D)
            Dim salesReturnId As Integer = Await RequestPendingOverHttpAsync(client, adminAToken, sale.SaleId, sale.SaleLineId, 2.000D)
            Dim correlationId As String = Guid.NewGuid().ToString()

            Using response As HttpResponseMessage =
                Await SendAsync(
                    client, HttpMethod.Post, $"/api/v1/sales/returns/{salesReturnId}/approve-exceptional", adminAToken,
                    New ApproveExceptionalSalesReturnRequest With {.RefundMethod = "Cash"}, correlationId)

                Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, "Self-approval must be refused 403.")
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("FORBIDDEN", body.ErrorCode)

            End Using

            Assert.AreEqual("PendingApproval", Await ReadSalesReturnStatusAsync(salesReturnId), "A refused self-approval must leave the return PendingApproval.")

            Dim deniedAuditCount As Long = Await CountAuditRowsAsync(correlationId, "SalesReturnApprovalDenied")
            Assert.AreEqual(1L, deniedAuditCount, "The self-approval refusal itself must be audited (spec section 9).")

        End Using

    End Function

    ''' <summary>Box 6: a DIFFERENT authorized user approving the same return succeeds, applies the deferred stock effect, and is fully attributable.</summary>
    <TestMethod>
    Public Async Function ApproveExceptional_DifferentAuthorizedUser_SucceedsAndAppliesDeferredStockEffect() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SetThresholdAsync(0.0100D)

        Using client As HttpClient = _factory.CreateClient()

            Dim adminAToken As String = Await LoginAsync(client, AdminAUsername)
            Dim adminBToken As String = Await LoginAsync(client, AdminBUsername)
            Dim sale = Await CompleteFixtureSaleAsync(productId, 2.000D)
            Dim salesReturnId As Integer = Await RequestPendingOverHttpAsync(client, adminAToken, sale.SaleId, sale.SaleLineId, 2.000D)
            Dim balanceBeforeApprove As Decimal = Await ReadBalanceAsync(productId)

            Using response As HttpResponseMessage =
                Await SendAsync(
                    client, HttpMethod.Post, $"/api/v1/sales/returns/{salesReturnId}/approve-exceptional", adminBToken,
                    New ApproveExceptionalSalesReturnRequest With {.RefundMethod = "Cash"})

                Assert.AreEqual(
                    HttpStatusCode.OK, response.StatusCode,
                    "A different authorized user must be able to approve. Body: " & Await response.Content.ReadAsStringAsync())

                Dim approved As SalesReturnResponse = Await response.Content.ReadFromJsonAsync(Of SalesReturnResponse)()
                Assert.AreEqual("Completed", approved.Status)
                Assert.AreEqual(_adminAUserId, approved.ReturnedByUserId, "ReturnedByUserId must stay the original requester.")
                Assert.AreEqual(_adminBUserId, approved.ApprovedByUserId, "ApprovedByUserId must be the actual approver, not the requester.")
                Assert.IsNotNull(approved.RefundAmount)
                Assert.AreEqual("Cash", approved.RefundMethod)

            End Using

            Assert.AreEqual("Completed", Await ReadSalesReturnStatusAsync(salesReturnId))
            Assert.AreEqual(balanceBeforeApprove + 2.000D, Await ReadBalanceAsync(productId))
            Assert.AreEqual(1L, Await CountMovementRowsAsync(productId, "SalesReturn"))

            Await AssertLedgerReconcilesAsync(productId)

        End Using

    End Function

    ''' <summary>A role without SalesReturns.ApproveExceptional is refused for a ROLE reason even when not the requester - the two denial reasons are distinct.</summary>
    <TestMethod>
    Public Async Function ApproveExceptional_RoleNotAllowed_StillRefused403() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SetThresholdAsync(0.0100D)

        Using client As HttpClient = _factory.CreateClient()

            Dim adminAToken As String = Await LoginAsync(client, AdminAUsername)
            Dim inventoryToken As String = Await LoginAsync(client, InventoryUsername)
            Dim sale = Await CompleteFixtureSaleAsync(productId, 2.000D)
            Dim salesReturnId As Integer = Await RequestPendingOverHttpAsync(client, adminAToken, sale.SaleId, sale.SaleLineId, 2.000D)

            Using response As HttpResponseMessage =
                Await SendAsync(
                    client, HttpMethod.Post, $"/api/v1/sales/returns/{salesReturnId}/approve-exceptional", inventoryToken,
                    New ApproveExceptionalSalesReturnRequest With {.RefundMethod = "Cash"})

                Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("FORBIDDEN", body.ErrorCode)

            End Using

        End Using

    End Function

    ''' <summary>Rejecting a PendingApproval return leaves no stock or refund effect at all - the "or none of them" half of the same atomicity rule.</summary>
    <TestMethod>
    Public Async Function RejectExceptional_PendingReturn_RejectsWithNoStockOrRefundEffect() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SetThresholdAsync(0.0100D)

        Using client As HttpClient = _factory.CreateClient()

            Dim adminAToken As String = Await LoginAsync(client, AdminAUsername)
            Dim adminBToken As String = Await LoginAsync(client, AdminBUsername)
            Dim sale = Await CompleteFixtureSaleAsync(productId, 2.000D)
            Dim salesReturnId As Integer = Await RequestPendingOverHttpAsync(client, adminAToken, sale.SaleId, sale.SaleLineId, 2.000D)
            Dim balanceBeforeReject As Decimal = Await ReadBalanceAsync(productId)

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, $"/api/v1/sales/returns/{salesReturnId}/reject-exceptional", adminBToken, requestBody:=Nothing)

                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, "Body: " & Await response.Content.ReadAsStringAsync())
                Dim rejected As SalesReturnResponse = Await response.Content.ReadFromJsonAsync(Of SalesReturnResponse)()
                Assert.AreEqual("Rejected", rejected.Status)
                Assert.IsNull(rejected.RefundAmount)
                Assert.IsNull(rejected.RefundMethod)
                Assert.AreEqual(_adminBUserId, rejected.ApprovedByUserId)

            End Using

            Assert.AreEqual(balanceBeforeReject, Await ReadBalanceAsync(productId))
            Assert.AreEqual(0L, Await CountMovementRowsAsync(productId, "SalesReturn"))

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
        client As HttpClient, method As HttpMethod, path As String, token As String, requestBody As Object,
        Optional correlationId As String = Nothing) As Task(Of HttpResponseMessage)

        Dim request As New HttpRequestMessage(method, path)

        If token IsNot Nothing Then
            request.Headers.Authorization = New AuthenticationHeaderValue("Bearer", token)
        End If

        If correlationId IsNot Nothing Then
            request.Headers.Add("X-Correlation-Id", correlationId)
        End If

        If requestBody IsNot Nothing Then
            request.Content = JsonContent.Create(requestBody)
        End If

        Return Await client.SendAsync(request)

    End Function

    ''' <summary>A fresh PendingApproval return over real HTTP - the caller must have already fixed the threshold low enough via SetThresholdAsync.</summary>
    Private Async Function RequestPendingOverHttpAsync(
        client As HttpClient, requesterToken As String, saleId As Integer, saleLineId As Integer, quantity As Decimal) As Task(Of Integer)

        Dim body As New CreateSalesReturnRequest With {
            .Reason = "P5-11 fixture return",
            .RefundMethod = "Cash",
            .IdempotencyKey = Guid.NewGuid().ToString("d"),
            .Lines = New List(Of CreateSalesReturnLineRequest) From {
                New CreateSalesReturnLineRequest With {.SaleLineId = saleLineId, .QuantityReturned = quantity, .RestocksItem = True}}
        }

        Using response As HttpResponseMessage = Await SendAsync(client, HttpMethod.Post, $"/api/v1/sales/{saleId}/returns", requesterToken, body)

            Assert.AreEqual(
                HttpStatusCode.Created, response.StatusCode,
                "Fixture sales return request must succeed. Body: " & Await response.Content.ReadAsStringAsync())

            Dim created As SalesReturnResponse = Await response.Content.ReadFromJsonAsync(Of SalesReturnResponse)()
            Assert.AreEqual("PendingApproval", created.Status, "Fixture sales return must be PendingApproval - the threshold fix must have taken effect.")
            Return created.Id

        End Using

    End Function

    ' --------------------------------------------------------------- helpers (direct service)

    ''' <summary>
    ''' Completes a fresh sale through the real SaleService/CashierSessionService
    ''' - never seeded directly, so the return's SaleLines row is exactly
    ''' what a real sale would produce. The BALANCE is seeded first (a
    ''' freshly created product has no StockBalances row at all -
    ''' ProductRepository.InsertAsync does not create one, the same fact
    ''' P4-15 section 2.1(a) found), always through
    ''' StockRepository.IncrementAsync plus a matching StockMovements row -
    ''' never a bare balance UPDATE, so P4-01's ledger reconciliation stays
    ''' satisfied for every fixture this file creates.
    ''' </summary>
    Private Async Function CompleteFixtureSaleAsync(
        productId As Integer, quantity As Decimal) As Task(Of (SaleId As Integer, SaleLineId As Integer, UnitPrice As Decimal))

        Await SeedBalanceAsync(productId, quantity)

        Dim openOutcome As CashierSessionOutcome =
            Await _cashierSessionService.OpenAsync(_cashierUserId, 0D, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(CashierSessionOutcomeKind.Created, openOutcome.Kind, "Fixture cashier session must open.")
        Dim sessionId As Integer = openOutcome.Session.Id

        Dim saleOutcome As SaleOutcome =
            Await _saleService.CompleteAsync(
                New List(Of CreateSaleLineRequest) From {New CreateSaleLineRequest With {.ProductId = productId, .Quantity = quantity}},
                PaymentMethod.EWallet, Nothing, _cashierUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(SaleOutcomeKind.Created, saleOutcome.Kind, "Fixture sale must complete.")

        Dim closeOutcome As CashierSessionOutcome =
            Await _cashierSessionService.CloseAsync(sessionId, _cashierUserId, 0D, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(CashierSessionOutcomeKind.Created, closeOutcome.Kind, "Fixture cashier session must close.")

        Dim line As SaleLineResponse = saleOutcome.Response.Lines(0)
        Return (SaleId:=saleOutcome.Response.Id, SaleLineId:=line.Id, UnitPrice:=line.UnitPrice)

    End Function

    ''' <summary>Records a return directly through SalesReturnService, defaulting Reason/RefundMethod so every box-1/2/3 test only names what it is actually varying.</summary>
    Private Async Function RecordReturnAsync(
        saleId As Integer, saleLineId As Integer, quantityReturned As Decimal, restocksItem As Boolean) As Task(Of SalesReturnOutcome)

        Return Await _salesReturnService.RecordAsync(
            saleId,
            New List(Of CreateSalesReturnLineRequest) From {
                New CreateSalesReturnLineRequest With {.SaleLineId = saleLineId, .QuantityReturned = quantityReturned, .RestocksItem = restocksItem}},
            "P5-11 fixture return", "Cash", _cashierUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

    End Function

    Private Async Function CreateFixtureProductAsync() As Task(Of Integer)

        Dim sku As String = FixtureProductSkuPrefix & Guid.NewGuid().ToString("N").Substring(0, 16)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO Products (Sku, Barcode, Name, Price, Cost, IsActive, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@sku, NULL, 'P5-11 Fixture Product', 9.0000, 4.0000, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@sku", sku)
                Await command.ExecuteNonQueryAsync()
                Return CInt(command.LastInsertedId)
            End Using
        End Using

    End Function

    ''' <summary>Sets the sales-return approval threshold via SystemSettingsRepository.UpsertAsync directly - the same mechanism SalesReturnService itself reads from, never a shortcut around it.</summary>
    Private Async Function SetThresholdAsync(threshold As Decimal) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using transaction As MySqlTransaction = Await connection.BeginTransactionAsync()

                Await SystemSettingsRepository.UpsertAsync(
                    connection, transaction, SystemSettingRegistry.Keys.SalesReturnApprovalThreshold,
                    threshold.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture), _inventoryUserId)

                Await transaction.CommitAsync()

            End Using
        End Using

    End Function

    Private Async Function ReadBalanceAsync(productId As Integer) As Task(Of Decimal)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT Quantity FROM StockBalances WHERE ProductId = @productId;"
                command.Parameters.AddWithValue("@productId", productId)
                Dim result As Object = Await command.ExecuteScalarAsync()
                Return If(result Is Nothing, 0D, CDec(result))
            End Using
        End Using

    End Function

    Private Async Function ReadSalesReturnStatusAsync(salesReturnId As Integer) As Task(Of String)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT Status FROM SalesReturns WHERE Id = @id;"
                command.Parameters.AddWithValue("@id", salesReturnId)
                Return CStr(Await command.ExecuteScalarAsync())
            End Using
        End Using

    End Function

    Private Async Function CountSalesReturnRowsAsync(saleId As Integer) As Task(Of Long)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM SalesReturns WHERE SaleId = @saleId;"
                command.Parameters.AddWithValue("@saleId", saleId)
                Return CLng(Await command.ExecuteScalarAsync())
            End Using
        End Using

    End Function

    Private Async Function CountMovementRowsAsync(productId As Integer, reason As String) As Task(Of Long)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM StockMovements WHERE ProductId = @productId AND Reason = @reason;"
                command.Parameters.AddWithValue("@productId", productId)
                command.Parameters.AddWithValue("@reason", reason)
                Return CLng(Await command.ExecuteScalarAsync())
            End Using
        End Using

    End Function

    Private Async Function CountAuditRowsAsync(correlationId As String, action As String) As Task(Of Long)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM AuditLogs WHERE CorrelationId = @correlationId AND Action = @action;"
                command.Parameters.AddWithValue("@correlationId", correlationId)
                command.Parameters.AddWithValue("@action", action)
                Return CLng(Await command.ExecuteScalarAsync())
            End Using
        End Using

    End Function

    Private Async Function SumReturnedQuantityAsync(saleLineId As Integer) As Task(Of Decimal)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COALESCE(SUM(QuantityReturned), 0.000) FROM SalesReturnLines WHERE SaleLineId = @saleLineId;"
                command.Parameters.AddWithValue("@saleLineId", saleLineId)
                Return CDec(Await command.ExecuteScalarAsync())
            End Using
        End Using

    End Function

    Private Async Function AssertLedgerReconcilesAsync(productId As Integer) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            Dim discrepancies = Await LedgerReconciliation.FindDiscrepanciesAsync(connection)
            Dim found = discrepancies.FirstOrDefault(Function(d) d.ProductId = productId)

            Dim detail As String =
                If(found Is Nothing, String.Empty,
                   $"expected {found.ExpectedQuantity}, actual {found.ActualQuantity}, delta {found.Delta}")

            Assert.IsNull(found, $"Product {productId} must reconcile: {detail}")

        End Using

    End Function

    ''' <summary>
    ''' Seeds an opening balance WITH a matching movement row - seeding
    ''' StockBalances directly is exactly the fixture defect P4-01 found and
    ''' had to heal with a compensating correction (SaleConcurrencyTests'
    ''' identical reasoning). ADDS <paramref name="atLeastQuantity"/> to
    ''' whatever the product already holds, rather than resetting it, so
    ''' repeated calls for the same product across a test's several
    ''' CompleteFixtureSaleAsync calls never race each other's own bound
    ''' arithmetic.
    ''' </summary>
    Private Async Function SeedBalanceAsync(productId As Integer, atLeastQuantity As Decimal) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using transaction As MySqlTransaction = Await connection.BeginTransactionAsync()

                Dim stockResult = Await StockRepository.IncrementAsync(connection, transaction, productId, atLeastQuantity)

                Await StockMovementWriter.WriteAsync(
                    connection, transaction, productId, atLeastQuantity,
                    stockResult.QuantityBefore, stockResult.QuantityAfter,
                    "P5-11 fixture: sale-enabling balance seed", _inventoryUserId, Guid.NewGuid().ToString())

                Await transaction.CommitAsync()

            End Using
        End Using

    End Function

    ''' <summary>
    ''' Force-closes any Open session left behind by this file's own fixture
    ''' Cashier from an earlier interrupted run, via UPDATE as merch_api
    ''' (db/grants/0013 - the same identity CashierSessionService itself
    ''' writes through) - AuthorizationMatrixTests' identical
    ''' CloseAnyOpenFixtureCashierSessionsDirectlyAsync reasoning. Without
    ''' this, a rerun after any assertion failure inside
    ''' CompleteFixtureSaleAsync (between Open and Close) would find the
    ''' fixture Cashier still holding an Open session and get AlreadyOpen
    ''' for every subsequent test.
    ''' </summary>
    Private Async Function CloseAnyOpenFixtureCashierSessionDirectlyAsync() As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "UPDATE CashierSessions " &
                    "   SET Status = 'Closed', ClosedByUserId = OpenedByUserId, ClosedAtUtc = UTC_TIMESTAMP(6), " &
                    "       RowVersion = RowVersion + 1, UpdatedAtUtc = UTC_TIMESTAMP(6) " &
                    " WHERE Status = 'Open' AND OpenedByUserId = @cashierUserId;"
                command.Parameters.AddWithValue("@cashierUserId", _cashierUserId)
                Await command.ExecuteNonQueryAsync()
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
            Path.Combine(Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName)
        Return DatabaseOptionsLoader.Load(migratorConfigPath)
    End Function

End Class
