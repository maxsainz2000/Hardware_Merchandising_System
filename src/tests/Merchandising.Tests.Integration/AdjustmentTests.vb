' Merchandising.Tests.Integration.AdjustmentTests
'
' P4-10: AdjustmentService.{RequestAsync,ApproveAsync,RejectAsync} and the
' AdjustmentsController routes, proven against the real pinned MariaDB -
' spec section 10.2's "An adjustment applies a variance to stock. Below the
' configured threshold it applies directly; at or above it requires a
' second person's approval."
'
' Role gating (Adjustments.Request/Adjustments.Approve resolve to the roles
' PolicyRegistry says they do) is AuthorizationMatrixTests' job. This file
' proves the BEHAVIOUR:
'
'   box 1   the threshold is read from SystemSettings, not a constant - a
'           CHANGED setting changes the routing outcome for the identical
'           variance magnitude, asserted directly rather than assumed
'   box 2   self-approval is refused 403 with two real users (ADR-017
'           section 6, reusing PurchaseOrdersController's exact mechanism);
'           a DIFFERENT authorized user approving the same adjustment
'           succeeds
'   box 3   movement + balance + audit commit together or not at all, on
'           BOTH apply paths (below-threshold immediate apply, and
'           Approve) - the P1-12/P2-08/P3-04-shaped forced-failure rollback
'   box 4   a Pending or Rejected adjustment changes no stock - asserted on
'           the balance and the ledger, both directly after Reject and
'           after an Approve refused for insufficient stock
'   box 5   the P4-01 ledger reconciliation passes after every applied
'           adjustment, whichever path applied it
'
' Fixture users are real, permanent rows (ADR-013: no DELETE grant on
' Users). A fresh product is created per test that needs to control its own
' starting balance exactly - the same "create fresh, never delete" shape
' StockCountTests/ReceivingTests use.

Imports System.Collections.Generic
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Net.Http.Json
Imports System.Threading.Tasks
Imports Merchandising.Api.Inventory
Imports Merchandising.Contracts.Auth
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Inventory
Imports Merchandising.Domain.Configuration
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class AdjustmentTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const FixturePassword As String = "P4-10 Fixture Passw0rd!"
    Private Const AdminAUsername As String = "p4_10_fixture_admin_a"
    Private Const AdminBUsername As String = "p4_10_fixture_admin_b"
    Private Const InventoryUsername As String = "p4_10_fixture_inventory"
    Private Const FixtureProductSkuPrefix As String = "p4_10_fixture_sku_"

    Private _factory As MerchandisingApiFactory
    Private _connectionFactory As ConnectionFactory
    Private _adjustmentService As AdjustmentService
    Private _inventoryUserId As Integer
    Private _adminAUserId As Integer
    Private _adminBUserId As Integer

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _factory = New MerchandisingApiFactory()
        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        _adjustmentService = New AdjustmentService(_connectionFactory)

        _inventoryUserId = Await EnsureFixtureUserAsync(InventoryUsername, "InventoryClerk")
        _adminAUserId = Await EnsureFixtureUserAsync(AdminAUsername, "Admin")
        _adminBUserId = Await EnsureFixtureUserAsync(AdminBUsername, "Admin")

    End Function

    <TestCleanup>
    Public Sub TearDown()
        _factory?.Dispose()
    End Sub

    ' -------------------------------------------------------------- box 1

    ''' <summary>Below the threshold, a request applies immediately - Applied, no approver, movement written.</summary>
    <TestMethod>
    Public Async Function RequestAsync_BelowThreshold_AppliesImmediately() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SetBalanceAsync(productId, 20.000D)
        Await SetThresholdAsync(10.000D)

        Dim outcome As AdjustmentOutcome =
            Await _adjustmentService.RequestAsync(
                productId, 5.000D, "Found extra stock", _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(AdjustmentOutcomeKind.Created, outcome.Kind)
        Assert.AreEqual("Applied", outcome.Response.Status)
        Assert.IsFalse(outcome.Response.ExceedsThreshold)
        Assert.IsNull(outcome.Response.ApprovedByUserId, "Nobody approved an auto-applied adjustment.")
        Assert.IsNotNull(outcome.Response.MovementId)
        Assert.AreEqual(25.000D, Await ReadBalanceAsync(productId), "20 + 5 = 25.")

    End Function

    ''' <summary>At or above the threshold, a request is recorded Pending with no stock effect at all.</summary>
    <TestMethod>
    Public Async Function RequestAsync_AtOrAboveThreshold_RecordsPendingNoStockEffect() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SetBalanceAsync(productId, 20.000D)
        Await SetThresholdAsync(10.000D)

        Dim outcome As AdjustmentOutcome =
            Await _adjustmentService.RequestAsync(
                productId, -10.000D, "Found less than recorded", _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(AdjustmentOutcomeKind.Created, outcome.Kind)
        Assert.AreEqual("Pending", outcome.Response.Status)
        Assert.IsTrue(outcome.Response.ExceedsThreshold, "|-10| >= 10 - at the threshold counts as exceeding it (card's own 'at or above' wording).")
        Assert.IsNull(outcome.Response.ApprovedByUserId)
        Assert.IsNull(outcome.Response.MovementId)
        Assert.AreEqual(20.000D, Await ReadBalanceAsync(productId), "A Pending adjustment must not touch the balance.")

    End Function

    ''' <summary>
    ''' Box 1's own wording: "a changed setting changes the outcome, asserted
    ''' rather than assumed." The SAME variance magnitude (5.000) is
    ''' requested twice, against two fresh products so neither balance nor
    ''' idempotency can explain a difference - only the threshold change can.
    ''' </summary>
    <TestMethod>
    Public Async Function RequestAsync_ChangedThreshold_ChangesOutcome() As Task

        Dim productA As Integer = Await CreateFixtureProductAsync()
        Dim productB As Integer = Await CreateFixtureProductAsync()
        Await SetBalanceAsync(productA, 20.000D)
        Await SetBalanceAsync(productB, 20.000D)

        Await SetThresholdAsync(10.000D)
        Dim belowOutcome As AdjustmentOutcome =
            Await _adjustmentService.RequestAsync(
                productA, 5.000D, "Below a 10 threshold", _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual("Applied", belowOutcome.Response.Status, "5.000 must be BELOW a threshold of 10.000.")

        Await SetThresholdAsync(1.000D)
        Dim aboveOutcome As AdjustmentOutcome =
            Await _adjustmentService.RequestAsync(
                productB, 5.000D, "At/above a 1 threshold", _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual("Pending", aboveOutcome.Response.Status, "The IDENTICAL 5.000 variance must be AT/ABOVE a threshold of 1.000 - the threshold change is what moved the outcome.")

    End Function

    ''' <summary>A below-threshold decrease that would take the balance negative rolls back entirely - no adjustment row at all, not even Pending.</summary>
    <TestMethod>
    Public Async Function RequestAsync_BelowThresholdInsufficientStock_RefusesAndWritesNothing() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SetBalanceAsync(productId, 2.000D)
        Await SetThresholdAsync(10.000D)

        Dim outcome As AdjustmentOutcome =
            Await _adjustmentService.RequestAsync(
                productId, -5.000D, "More than is on hand", _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(AdjustmentOutcomeKind.InsufficientStock, outcome.Kind)
        Assert.AreEqual(2.000D, Await ReadBalanceAsync(productId), "The balance must be untouched.")
        Assert.AreEqual(0L, Await CountAdjustmentRowsAsync(productId), "No StockAdjustments row at all - the whole transaction, including the insert, must have rolled back.")

    End Function

    ''' <summary>An unknown ProductId is refused before any row is written.</summary>
    <TestMethod>
    Public Async Function RequestAsync_UnknownProduct_ReturnsProductNotFound() As Task

        Dim outcome As AdjustmentOutcome =
            Await _adjustmentService.RequestAsync(
                999999999, 5.000D, "No such product", _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(AdjustmentOutcomeKind.ProductNotFound, outcome.Kind)

    End Function

    ' -------------------------------------------------------- box 3 (apply)

    ''' <summary>Box 3: fault injected on the below-threshold immediate-apply path rolls back the insert, the stock effect and the audit row together.</summary>
    <TestMethod>
    Public Async Function RequestAsync_FaultInjectedBeforeCommit_RollsBackEverything() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SetBalanceAsync(productId, 20.000D)
        Await SetThresholdAsync(10.000D)
        Dim correlationId As String = Guid.NewGuid().ToString()

        Dim faultInjected As Boolean = False

        Await Assert.ThrowsExactlyAsync(Of InvalidOperationException)(
            Function() _adjustmentService.RequestAsync(
                productId, 5.000D, "P4-10 forced failure (request)", _inventoryUserId, correlationId, Guid.NewGuid().ToString(),
                testOnlyFaultAfterAuditInsert:=Sub()
                                                   faultInjected = True
                                                   Throw New InvalidOperationException("P4-10 forced failure: after audit insert, before commit.")
                                               End Sub))

        Assert.IsTrue(faultInjected, "The fault-injection delegate must actually have fired for this proof to mean anything.")
        Assert.AreEqual(20.000D, Await ReadBalanceAsync(productId), "A rolled-back request must leave the balance untouched.")
        Assert.AreEqual(0L, Await CountAdjustmentRowsAsync(productId), "A rolled-back request must leave no StockAdjustments row.")
        Assert.AreEqual(0L, Await CountMovementRowsAsync(correlationId), "A rolled-back request must leave no StockMovements row.")
        Assert.AreEqual(0L, Await CountAuditRowsAsync(correlationId, "AdjustmentApplied"), "A rolled-back request must leave no AuditLogs row.")

    End Function

    ''' <summary>Box 3: fault injected on Approve rolls back the stock effect, the status transition and the audit row together - status stays Pending.</summary>
    <TestMethod>
    Public Async Function ApproveAsync_FaultInjectedBeforeCommit_RollsBackEverything() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SetBalanceAsync(productId, 20.000D)
        Dim adjustmentId As Integer = Await RequestPendingDirectlyAsync(productId, 15.000D)
        Dim correlationId As String = Guid.NewGuid().ToString()

        Dim faultInjected As Boolean = False

        Await Assert.ThrowsExactlyAsync(Of InvalidOperationException)(
            Function() _adjustmentService.ApproveAsync(
                adjustmentId, _adminAUserId, correlationId,
                testOnlyFaultAfterAuditInsert:=Sub()
                                                   faultInjected = True
                                                   Throw New InvalidOperationException("P4-10 forced failure: after audit insert, before commit.")
                                               End Sub))

        Assert.IsTrue(faultInjected, "The fault-injection delegate must actually have fired for this proof to mean anything.")
        Assert.AreEqual(20.000D, Await ReadBalanceAsync(productId), "A rolled-back approval must leave the balance untouched.")
        Assert.AreEqual("Pending", Await ReadAdjustmentStatusAsync(adjustmentId), "A rolled-back approval must leave Status untouched.")
        Assert.AreEqual(0L, Await CountMovementRowsAsync(correlationId), "A rolled-back approval must leave no StockMovements row.")
        Assert.AreEqual(0L, Await CountAuditRowsAsync(correlationId, "AdjustmentApplied"), "A rolled-back approval must leave no AuditLogs row.")

    End Function

    ' -------------------------------------------------------------- box 4

    ''' <summary>Box 4: a rejected adjustment changes no stock - asserted on the balance and by confirming zero movement rows exist.</summary>
    <TestMethod>
    Public Async Function RejectAsync_PendingAdjustment_ChangesNoStock() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SetBalanceAsync(productId, 20.000D)
        Dim adjustmentId As Integer = Await RequestPendingDirectlyAsync(productId, -15.000D)

        Dim outcome As AdjustmentOutcome =
            Await _adjustmentService.RejectAsync(adjustmentId, _adminAUserId, Guid.NewGuid().ToString())

        Assert.AreEqual(AdjustmentOutcomeKind.Created, outcome.Kind)
        Assert.AreEqual("Rejected", outcome.Response.Status)
        Assert.AreEqual(20.000D, Await ReadBalanceAsync(productId), "A rejected adjustment must not touch the balance.")
        Assert.AreEqual(0L, Await CountMovementRowsForProductAsync(productId), "A rejected adjustment must write no StockMovements row.")

    End Function

    <TestMethod>
    Public Async Function RejectAsync_NotPending_Refused() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SetBalanceAsync(productId, 20.000D)
        Dim adjustmentId As Integer = Await RequestPendingDirectlyAsync(productId, -15.000D)

        Dim first As AdjustmentOutcome = Await _adjustmentService.RejectAsync(adjustmentId, _adminAUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(AdjustmentOutcomeKind.Created, first.Kind)

        Dim second As AdjustmentOutcome = Await _adjustmentService.RejectAsync(adjustmentId, _adminAUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(AdjustmentOutcomeKind.NotPending, second.Kind)

    End Function

    <TestMethod>
    Public Async Function RejectAsync_UnknownId_ReturnsNotFound() As Task

        Dim outcome As AdjustmentOutcome = Await _adjustmentService.RejectAsync(999999999, _adminAUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(AdjustmentOutcomeKind.NotFound, outcome.Kind)

    End Function

    ''' <summary>Box 4: an Approve refused for insufficient stock leaves the adjustment Pending and the balance untouched.</summary>
    <TestMethod>
    Public Async Function ApproveAsync_InsufficientStock_LeavesStatusPendingAndBalanceUntouched() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SetBalanceAsync(productId, 20.000D)
        Dim adjustmentId As Integer = Await RequestPendingDirectlyAsync(productId, -15.000D)

        ' Stock consumed by something else between request and approval.
        Await SetBalanceAsync(productId, 5.000D)

        Dim outcome As AdjustmentOutcome = Await _adjustmentService.ApproveAsync(adjustmentId, _adminAUserId, Guid.NewGuid().ToString())

        Assert.AreEqual(AdjustmentOutcomeKind.InsufficientStock, outcome.Kind)
        Assert.AreEqual("Pending", Await ReadAdjustmentStatusAsync(adjustmentId), "A refused approval must leave the adjustment Pending, not Applied.")
        Assert.AreEqual(5.000D, Await ReadBalanceAsync(productId), "A refused approval must not touch the balance.")

    End Function

    <TestMethod>
    Public Async Function ApproveAsync_NotPending_Refused() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SetBalanceAsync(productId, 20.000D)
        Dim adjustmentId As Integer = Await RequestPendingDirectlyAsync(productId, 15.000D)

        Dim first As AdjustmentOutcome = Await _adjustmentService.ApproveAsync(adjustmentId, _adminAUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(AdjustmentOutcomeKind.Created, first.Kind)

        Dim second As AdjustmentOutcome = Await _adjustmentService.ApproveAsync(adjustmentId, _adminBUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(AdjustmentOutcomeKind.NotPending, second.Kind)

    End Function

    <TestMethod>
    Public Async Function ApproveAsync_UnknownId_ReturnsNotFound() As Task

        Dim outcome As AdjustmentOutcome = Await _adjustmentService.ApproveAsync(999999999, _adminAUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(AdjustmentOutcomeKind.NotFound, outcome.Kind)

    End Function

    ''' <summary>Approving a Pending adjustment applies the stock effect and records the approver - a positive variance path.</summary>
    <TestMethod>
    Public Async Function ApproveAsync_PendingAdjustment_AppliesAndRecordsApprover() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SetBalanceAsync(productId, 20.000D)
        Dim adjustmentId As Integer = Await RequestPendingDirectlyAsync(productId, 15.000D)

        Dim outcome As AdjustmentOutcome = Await _adjustmentService.ApproveAsync(adjustmentId, _adminAUserId, Guid.NewGuid().ToString())

        Assert.AreEqual(AdjustmentOutcomeKind.Created, outcome.Kind)
        Assert.AreEqual("Applied", outcome.Response.Status)
        Assert.AreEqual(_adminAUserId, outcome.Response.ApprovedByUserId)
        Assert.IsNotNull(outcome.Response.MovementId)
        Assert.AreEqual(35.000D, Await ReadBalanceAsync(productId), "20 + 15 = 35.")

    End Function

    ' -------------------------------------------------------------- box 5

    ''' <summary>Box 5: the ledger reconciles after an adjustment applied directly (below threshold).</summary>
    <TestMethod>
    Public Async Function RequestAsync_AppliedBelowThreshold_LedgerReconciles() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SetBalanceAsync(productId, 20.000D)
        Await SetThresholdAsync(10.000D)

        Dim outcome As AdjustmentOutcome =
            Await _adjustmentService.RequestAsync(
                productId, 5.000D, "Reconciliation check", _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(AdjustmentOutcomeKind.Created, outcome.Kind)

        Await AssertLedgerReconcilesAsync(productId)

    End Function

    ''' <summary>Box 5: the ledger reconciles after an adjustment applied via Approve.</summary>
    <TestMethod>
    Public Async Function ApproveAsync_Applied_LedgerReconciles() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SetBalanceAsync(productId, 20.000D)
        Dim adjustmentId As Integer = Await RequestPendingDirectlyAsync(productId, -15.000D)

        Dim outcome As AdjustmentOutcome = Await _adjustmentService.ApproveAsync(adjustmentId, _adminAUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(AdjustmentOutcomeKind.Created, outcome.Kind)

        Await AssertLedgerReconcilesAsync(productId)

    End Function

    ' -------------------------------------------------------------- box 2 (HTTP)

    ''' <summary>Box 2: the SAME user who requested a Pending adjustment is refused 403 approving it, and the refusal is audited.</summary>
    <TestMethod>
    Public Async Function ApproveAdjustment_SameUserAsRequester_Refused403AndAudited() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SetBalanceAsync(productId, 20.000D)

        Using client As HttpClient = _factory.CreateClient()

            Dim adminAToken As String = Await LoginAsync(client, AdminAUsername)
            Dim adjustmentId As Integer = Await RequestPendingOverHttpAsync(client, adminAToken, productId, 15.000D)
            Dim correlationId As String = Guid.NewGuid().ToString()

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, $"/api/v1/adjustments/{adjustmentId}/approve", adminAToken, Nothing, correlationId)

                Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, "Self-approval must be refused 403.")
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("FORBIDDEN", body.ErrorCode)

            End Using

            Assert.AreEqual("Pending", Await ReadAdjustmentStatusAsync(adjustmentId), "A refused self-approval must leave the adjustment Pending.")
            Assert.AreEqual(20.000D, Await ReadBalanceAsync(productId))

            Dim deniedAuditCount As Long = Await CountAuditRowsAsync(correlationId, "AdjustmentApprovalDenied")
            Assert.AreEqual(1L, deniedAuditCount, "The self-approval refusal itself must be audited (spec section 9).")

        End Using

    End Function

    ''' <summary>Box 2: a DIFFERENT authorized user approving the same adjustment succeeds and is fully attributable.</summary>
    <TestMethod>
    Public Async Function ApproveAdjustment_DifferentAuthorizedUser_SucceedsAndIsAttributable() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SetBalanceAsync(productId, 20.000D)

        Using client As HttpClient = _factory.CreateClient()

            Dim adminAToken As String = Await LoginAsync(client, AdminAUsername)
            Dim adminBToken As String = Await LoginAsync(client, AdminBUsername)
            Dim adjustmentId As Integer = Await RequestPendingOverHttpAsync(client, adminAToken, productId, 15.000D)

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, $"/api/v1/adjustments/{adjustmentId}/approve", adminBToken, Nothing)

                Assert.AreEqual(
                    HttpStatusCode.OK, response.StatusCode,
                    "A different authorized user must be able to approve. Body: " & Await response.Content.ReadAsStringAsync())

                Dim approved As AdjustmentResponse = Await response.Content.ReadFromJsonAsync(Of AdjustmentResponse)()
                Assert.AreEqual("Applied", approved.Status)
                Assert.AreEqual(_adminAUserId, approved.RequestedByUserId, "RequestedByUserId must stay the original requester.")
                Assert.AreEqual(_adminBUserId, approved.ApprovedByUserId, "ApprovedByUserId must be the actual approver, not the requester.")

            End Using

            Assert.AreEqual("Applied", Await ReadAdjustmentStatusAsync(adjustmentId))
            Assert.AreEqual(35.000D, Await ReadBalanceAsync(productId))

        End Using

    End Function

    ''' <summary>A role without Adjustments.Approve is refused for a ROLE reason even when not the requester - the two denial reasons are distinct.</summary>
    <TestMethod>
    Public Async Function ApproveAdjustment_RoleNotAllowed_StillRefused403() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Await SetBalanceAsync(productId, 20.000D)

        Using client As HttpClient = _factory.CreateClient()

            Dim adminAToken As String = Await LoginAsync(client, AdminAUsername)
            Dim inventoryToken As String = Await LoginAsync(client, InventoryUsername)
            Dim adjustmentId As Integer = Await RequestPendingOverHttpAsync(client, adminAToken, productId, 15.000D)

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, $"/api/v1/adjustments/{adjustmentId}/approve", inventoryToken, Nothing)

                Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("FORBIDDEN", body.ErrorCode)

            End Using

        End Using

    End Function

    ' -------------------------------------------------------------- HTTP shape

    ''' <summary>ADR-004.1: an over-scale variance is refused 400 before any connection is opened.</summary>
    <TestMethod>
    Public Async Function RequestAdjustment_OverScaleQuantity_Returns400AndWritesNothing() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, InventoryUsername)

            Dim request As New RequestAdjustmentRequest With {
                .ProductId = productId, .QuantityVariance = 1.9999D, .Reason = "Over-scale", .IdempotencyKey = Guid.NewGuid().ToString("d")}

            Using response As HttpResponseMessage = Await SendAsync(client, HttpMethod.Post, "/api/v1/adjustments", token, request)

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("VALIDATION_FAILED", body.ErrorCode)
                Assert.IsTrue(body.Errors.ContainsKey("quantityVariance"))

            End Using

        End Using

        Assert.AreEqual(0L, Await CountAdjustmentRowsAsync(productId))

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

    ''' <summary>A fresh Pending adjustment over real HTTP - |variance| must be requested well above whatever this test class's own default threshold row currently holds, so callers should set it explicitly first via SetThresholdAsync where the exact boundary matters.</summary>
    Private Async Function RequestPendingOverHttpAsync(
        client As HttpClient, requesterToken As String, productId As Integer, absVariance As Decimal) As Task(Of Integer)

        Await SetThresholdAsync(1.000D)

        Dim body As New RequestAdjustmentRequest With {
            .ProductId = productId, .QuantityVariance = absVariance, .Reason = "P4-10 fixture", .IdempotencyKey = Guid.NewGuid().ToString("d")}

        Using response As HttpResponseMessage = Await SendAsync(client, HttpMethod.Post, "/api/v1/adjustments", requesterToken, body)

            Assert.AreEqual(
                HttpStatusCode.Created, response.StatusCode,
                "Fixture adjustment request must succeed. Body: " & Await response.Content.ReadAsStringAsync())

            Dim created As AdjustmentResponse = Await response.Content.ReadFromJsonAsync(Of AdjustmentResponse)()
            Assert.AreEqual("Pending", created.Status, "Fixture adjustment must be Pending.")
            Return created.Id

        End Using

    End Function

    ' --------------------------------------------------------------- helpers (direct service / raw)

    ''' <summary>A fresh Pending adjustment, requested directly through the service (threshold fixed to 1.000 first, so any |variance| >= 1 is Pending).</summary>
    Private Async Function RequestPendingDirectlyAsync(productId As Integer, variance As Decimal) As Task(Of Integer)

        Await SetThresholdAsync(1.000D)

        Dim outcome As AdjustmentOutcome =
            Await _adjustmentService.RequestAsync(
                productId, variance, "P4-10 fixture", _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(AdjustmentOutcomeKind.Created, outcome.Kind, "Fixture request must succeed.")
        Assert.AreEqual("Pending", outcome.Response.Status, "Fixture request must be Pending.")
        Return outcome.Response.Id

    End Function

    Private Async Function CreateFixtureProductAsync() As Task(Of Integer)

        Dim sku As String = FixtureProductSkuPrefix & Guid.NewGuid().ToString("N").Substring(0, 16)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO Products (Sku, Barcode, Name, Price, Cost, IsActive, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@sku, NULL, 'P4-10 Fixture Product', 9.0000, 4.0000, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@sku", sku)
                Await command.ExecuteNonQueryAsync()
                Return CInt(command.LastInsertedId)
            End Using
        End Using

    End Function

    ''' <summary>Sets a fresh product's balance via StockRepository.IncrementAsync's own upsert plus a matching compensating StockMovements row, so P4-01's ledger reconciliation stays satisfied for this fixture.</summary>
    Private Async Function SetBalanceAsync(productId As Integer, quantity As Decimal) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using transaction As MySqlTransaction = Await connection.BeginTransactionAsync()

                Dim currentQuantity As Decimal = Await ReadBalanceForUpdateAsync(connection, transaction, productId)
                Dim delta As Decimal = quantity - currentQuantity

                If delta <> 0D Then

                    Dim stockResult = Await StockRepository.IncrementAsync(connection, transaction, productId, delta)

                    Using movementCommand As MySqlCommand = connection.CreateCommand()
                        movementCommand.Transaction = transaction
                        movementCommand.CommandText =
                            "INSERT INTO StockMovements (ProductId, Delta, QuantityBefore, QuantityAfter, Reason, ActorUserId, CorrelationId, CreatedAtUtc) " &
                            "VALUES (@productId, @delta, @before, @after, 'Test fixture balance seed (AdjustmentTests)', @actorUserId, @correlationId, UTC_TIMESTAMP(6));"
                        movementCommand.Parameters.AddWithValue("@productId", productId)
                        movementCommand.Parameters.AddWithValue("@delta", delta)
                        movementCommand.Parameters.AddWithValue("@before", stockResult.QuantityBefore)
                        movementCommand.Parameters.AddWithValue("@after", stockResult.QuantityAfter)
                        movementCommand.Parameters.AddWithValue("@actorUserId", _inventoryUserId)
                        movementCommand.Parameters.AddWithValue("@correlationId", Guid.NewGuid().ToString())
                        Await movementCommand.ExecuteNonQueryAsync()
                    End Using

                End If

                Await transaction.CommitAsync()

            End Using
        End Using

    End Function

    Private Shared Async Function ReadBalanceForUpdateAsync(connection As MySqlConnection, transaction As MySqlTransaction, productId As Integer) As Task(Of Decimal)

        Using command As MySqlCommand = connection.CreateCommand()
            command.Transaction = transaction
            command.CommandText = "SELECT Quantity FROM StockBalances WHERE ProductId = @productId FOR UPDATE;"
            command.Parameters.AddWithValue("@productId", productId)
            Dim result As Object = Await command.ExecuteScalarAsync()
            Return If(result Is Nothing, 0D, CDec(result))
        End Using

    End Function

    ''' <summary>Sets the adjustment approval threshold via SystemSettingsRepository.UpsertAsync directly - the same mechanism AdjustmentService itself reads from, never a shortcut around it.</summary>
    Private Async Function SetThresholdAsync(threshold As Decimal) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using transaction As MySqlTransaction = Await connection.BeginTransactionAsync()

                Await SystemSettingsRepository.UpsertAsync(
                    connection, transaction, SystemSettingRegistry.Keys.AdjustmentApprovalThreshold,
                    threshold.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture), _inventoryUserId)

                Await transaction.CommitAsync()

            End Using
        End Using

    End Function

    Private Async Function ReadBalanceAsync(productId As Integer) As Task(Of Decimal)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT Quantity FROM StockBalances WHERE ProductId = @productId;"
                command.Parameters.AddWithValue("@productId", productId)
                Return CDec(Await command.ExecuteScalarAsync())
            End Using
        End Using

    End Function

    Private Async Function ReadAdjustmentStatusAsync(adjustmentId As Integer) As Task(Of String)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT Status FROM StockAdjustments WHERE Id = @id;"
                command.Parameters.AddWithValue("@id", adjustmentId)
                Return CStr(Await command.ExecuteScalarAsync())
            End Using
        End Using

    End Function

    Private Async Function CountAdjustmentRowsAsync(productId As Integer) As Task(Of Long)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM StockAdjustments WHERE ProductId = @productId;"
                command.Parameters.AddWithValue("@productId", productId)
                Return CLng(Await command.ExecuteScalarAsync())
            End Using
        End Using

    End Function

    Private Async Function CountMovementRowsAsync(correlationId As String) As Task(Of Long)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM StockMovements WHERE CorrelationId = @correlationId;"
                command.Parameters.AddWithValue("@correlationId", correlationId)
                Return CLng(Await command.ExecuteScalarAsync())
            End Using
        End Using

    End Function

    Private Async Function CountMovementRowsForProductAsync(productId As Integer) As Task(Of Long)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM StockMovements WHERE ProductId = @productId AND Reason = 'StockAdjustment';"
                command.Parameters.AddWithValue("@productId", productId)
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

    Private Async Function AssertLedgerReconcilesAsync(productId As Integer) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Dim discrepancies = Await LedgerReconciliation.FindDiscrepanciesAsync(connection)
            Dim found = discrepancies.FirstOrDefault(Function(d) d.ProductId = productId)
            Dim detail As String =
                If(found Is Nothing, String.Empty,
                   $"expected {found.ExpectedQuantity}, actual {found.ActualQuantity}, delta {found.Delta}")
            Assert.IsNull(found, $"Product {productId} must reconcile after a committed adjustment: {detail}")
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

End Class
