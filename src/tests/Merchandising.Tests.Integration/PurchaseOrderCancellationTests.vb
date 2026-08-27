' Merchandising.Tests.Integration.PurchaseOrderCancellationTests
'
' P3-05: POST /{id}/cancel and POST /{id}/close, both routed through P3-01's
' CanTransition (ADR-020) - spec section 10.1's "A cancelled order cannot
' receive goods" and the closure half of the same status machine.
'
' Role gating itself (PurchaseOrders.Cancel / PurchaseOrders.Close resolve to
' the roles PolicyRegistry says they do) is AuthorizationMatrixTests' job -
' this file proves the BEHAVIOUR:
'
'   box 1  a cancelled order refuses every subsequent action, enumerated -
'          not spot-checked - with the stable PURCHASE_ORDER_CANCELLED code
'   box 2  closure rules match P3-01's table exactly: Close succeeds only
'          from PartiallyReceived/FullyReceived, and is refused (with the
'          same enumeration) from every other state, including a closed
'          order refusing everything again
'   box 3  a cancelled or closed order is never deleted and stays fully
'          readable - GET still returns it, 200, after the transition
'   box 4  both transitions are audited with actor, reason and correlation ID
'
' PartiallyReceived/FullyReceived are unreachable through any live Phase 3
' endpoint - Phase 4 owns receiving. Every test that needs one of those
' states reaches it with a direct SQL UPDATE (merch_api holds UPDATE on
' purchaseorders, db/grants/0010) purely to arrange fixture state, the same
' shape AuthorizationMatrixTests.ForcePurchaseOrderStatusDirectlyAsync uses
' for its own Close matrix cell.
'
' This card's done-when boxes do not ask for a forced-failure/rollback proof
' (contrast P3-04's explicit box) - CancelAsync/CloseAsync share
' PurchaseOrderService.TransitionWithReasonAsync, the identical transaction
' shape ApproveAsync already proved atomic
' (ApproveAsync_FaultInjectedBeforeCommit_RollsBackStatusAndAuditRows,
' PurchaseOrderApprovalTests.vb) - a second proof of the same mechanism is
' not repeated here.
'
' Fixture users, the supplier and product are real, permanent rows - nothing
' here is torn down (ADR-013: no DELETE grant on Users, Suppliers or
' Products, and none on PurchaseOrders either - box 3's own claim).

Imports System.Collections.Generic
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Net.Http.Json
Imports System.Threading.Tasks
Imports Merchandising.Api.Procurement
Imports Merchandising.Contracts.Auth
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Procurement
Imports Merchandising.Domain.Entities
Imports Merchandising.Domain.Procurement
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class PurchaseOrderCancellationTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const FixturePassword As String = "P3-05 Fixture Passw0rd!"
    Private Const ProcurementFixtureUsername As String = "p3_05_fixture_procurement"
    Private Const FixtureProductSku As String = "p3_05_fixture_sku"

    Private _factory As MerchandisingApiFactory
    Private _connectionFactory As ConnectionFactory
    Private _purchaseOrderService As PurchaseOrderService
    Private _supplierId As Integer
    Private _productId As Integer
    Private _requesterUserId As Integer

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _factory = New MerchandisingApiFactory()
        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        _purchaseOrderService = New PurchaseOrderService(_connectionFactory)

        _requesterUserId = Await EnsureFixtureUserAsync(ProcurementFixtureUsername, "ProcurementOfficer")
        _supplierId = Await CreateActiveSupplierAsync()
        _productId = Await EnsureFixtureProductAsync()

    End Function

    <TestCleanup>
    Public Sub TearDown()
        _factory?.Dispose()
    End Sub

    ' ------------------------------------------------------------- box 2 (legal transitions)

    <TestMethod>
    Public Async Function CancelAsync_FromDraft_Succeeds() As Task
        Await AssertCancelSucceedsAsync(fromStatus:=Nothing)
    End Function

    <TestMethod>
    Public Async Function CancelAsync_FromSubmitted_Succeeds() As Task
        Await AssertCancelSucceedsAsync(fromStatus:="Submitted")
    End Function

    <TestMethod>
    Public Async Function CancelAsync_FromApproved_Succeeds() As Task
        Await AssertCancelSucceedsAsync(fromStatus:="Approved")
    End Function

    Private Async Function AssertCancelSucceedsAsync(fromStatus As String) As Task

        Dim orderId As Integer = Await CreateDraftDirectlyAsync()
        If fromStatus IsNot Nothing Then
            Await ForceStatusDirectlyAsync(orderId, fromStatus)
        End If

        Dim outcome As PurchaseOrderTransitionOutcome =
            Await _purchaseOrderService.CancelAsync(orderId, _requesterUserId, "changed my mind", Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Success, outcome.Kind)
        Assert.AreEqual("Cancelled", outcome.Response.Status)

    End Function

    <TestMethod>
    Public Async Function CloseAsync_FromPartiallyReceived_Succeeds() As Task
        Await AssertCloseSucceedsAsync("PartiallyReceived")
    End Function

    <TestMethod>
    Public Async Function CloseAsync_FromFullyReceived_Succeeds() As Task
        Await AssertCloseSucceedsAsync("FullyReceived")
    End Function

    Private Async Function AssertCloseSucceedsAsync(fromStatus As String) As Task

        Dim orderId As Integer = Await CreateDraftDirectlyAsync()
        Await ForceStatusDirectlyAsync(orderId, fromStatus)

        Dim outcome As PurchaseOrderTransitionOutcome =
            Await _purchaseOrderService.CloseAsync(orderId, _requesterUserId, "receiving finished", Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Success, outcome.Kind)
        Assert.AreEqual("Closed", outcome.Response.Status)

    End Function

    ''' <summary>Cancel is deliberately NOT legal once anything has been received - PurchaseOrderTransitions.vb's own header explains why (a receipt already wrote append-only StockMovements). Close is the only route once receiving has started.</summary>
    <TestMethod>
    Public Async Function CancelAsync_FromPartiallyReceived_RefusedInvalidTransition() As Task

        Dim orderId As Integer = Await CreateDraftDirectlyAsync()
        Await ForceStatusDirectlyAsync(orderId, "PartiallyReceived")

        Dim outcome As PurchaseOrderTransitionOutcome =
            Await _purchaseOrderService.CancelAsync(orderId, _requesterUserId, "attempted mid-receipt", Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Refused, outcome.Kind)
        Assert.AreEqual(PurchaseOrderTransitionErrors.InvalidTransition, outcome.ErrorCode)

    End Function

    ''' <summary>Close is refused before any goods have been received - "closure rules match P3-01's table exactly", never a rule this controller invents.</summary>
    <TestMethod>
    Public Async Function CloseAsync_FromDraft_RefusedInvalidTransition() As Task
        Await AssertCloseRefusedInvalidTransitionAsync(fromStatus:=Nothing)
    End Function

    <TestMethod>
    Public Async Function CloseAsync_FromSubmitted_RefusedInvalidTransition() As Task
        Await AssertCloseRefusedInvalidTransitionAsync(fromStatus:="Submitted")
    End Function

    <TestMethod>
    Public Async Function CloseAsync_FromApproved_RefusedInvalidTransition() As Task
        Await AssertCloseRefusedInvalidTransitionAsync(fromStatus:="Approved")
    End Function

    Private Async Function AssertCloseRefusedInvalidTransitionAsync(fromStatus As String) As Task

        Dim orderId As Integer = Await CreateDraftDirectlyAsync()
        If fromStatus IsNot Nothing Then
            Await ForceStatusDirectlyAsync(orderId, fromStatus)
        End If

        Dim outcome As PurchaseOrderTransitionOutcome =
            Await _purchaseOrderService.CloseAsync(orderId, _requesterUserId, "too early", Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Refused, outcome.Kind)
        Assert.AreEqual(PurchaseOrderTransitionErrors.InvalidTransition, outcome.ErrorCode)

    End Function

    ' ------------------------------------------------------------- box 1/2 (terminal states refuse everything)

    ''' <summary>
    ''' Box 1: a Cancelled order refuses every one of the six actions,
    ''' enumerated - not spot-checked. Submit/Approve/Cancel/Close go through
    ''' the real service methods (the ones with live Phase 3 endpoints);
    ''' ReceivePartially/ReceiveFully have none yet (Phase 4), so they are
    ''' asserted directly against CanTransition - the same table
    ''' PurchaseOrderTransitionTests already proves exhaustively at P3-01,
    ''' consulted here to confirm this SPECIFIC row rather than re-deriving it.
    ''' </summary>
    <TestMethod>
    Public Async Function CancelledOrder_RefusesEveryAction_Enumerated() As Task

        Dim orderId As Integer = Await CreateDraftDirectlyAsync()
        Await ForceStatusDirectlyAsync(orderId, "Cancelled")

        Await AssertAllSixActionsRefusedAsync(orderId, PurchaseOrderStatus.Cancelled, PurchaseOrderTransitionErrors.Cancelled)

    End Function

    ''' <summary>Box 2's other half: a Closed order is equally terminal - refuses everything, the same way Cancelled does.</summary>
    <TestMethod>
    Public Async Function ClosedOrder_RefusesEveryAction_Enumerated() As Task

        Dim orderId As Integer = Await CreateDraftDirectlyAsync()
        Await ForceStatusDirectlyAsync(orderId, "Closed")

        Await AssertAllSixActionsRefusedAsync(orderId, PurchaseOrderStatus.Closed, PurchaseOrderTransitionErrors.Closed)

    End Function

    Private Async Function AssertAllSixActionsRefusedAsync(
        orderId As Integer, fromStatus As PurchaseOrderStatus, expectedErrorCode As String) As Task

        Dim submitOutcome As PurchaseOrderTransitionOutcome =
            Await _purchaseOrderService.SubmitAsync(orderId, _requesterUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Refused, submitOutcome.Kind, "Submit")
        Assert.AreEqual(expectedErrorCode, submitOutcome.ErrorCode, "Submit")

        Dim approveOutcome As PurchaseOrderTransitionOutcome =
            Await _purchaseOrderService.ApproveAsync(orderId, _requesterUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Refused, approveOutcome.Kind, "Approve")
        Assert.AreEqual(expectedErrorCode, approveOutcome.ErrorCode, "Approve")

        Dim cancelOutcome As PurchaseOrderTransitionOutcome =
            Await _purchaseOrderService.CancelAsync(orderId, _requesterUserId, "already terminal", Guid.NewGuid().ToString())
        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Refused, cancelOutcome.Kind, "Cancel")
        Assert.AreEqual(expectedErrorCode, cancelOutcome.ErrorCode, "Cancel")

        Dim closeOutcome As PurchaseOrderTransitionOutcome =
            Await _purchaseOrderService.CloseAsync(orderId, _requesterUserId, "already terminal", Guid.NewGuid().ToString())
        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Refused, closeOutcome.Kind, "Close")
        Assert.AreEqual(expectedErrorCode, closeOutcome.ErrorCode, "Close")

        ' ReceivePartially/ReceiveFully have no live endpoint (Phase 4) - the
        ' pure domain decision is the only thing to assert against.
        Dim receivePartiallyResult As PurchaseOrderTransitionResult =
            PurchaseOrderTransitions.CanTransition(fromStatus, PurchaseOrderAction.ReceivePartially)
        Assert.IsFalse(receivePartiallyResult.IsAllowed, "ReceivePartially")
        Assert.AreEqual(expectedErrorCode, receivePartiallyResult.ErrorCode, "ReceivePartially")

        Dim receiveFullyResult As PurchaseOrderTransitionResult =
            PurchaseOrderTransitions.CanTransition(fromStatus, PurchaseOrderAction.ReceiveFully)
        Assert.IsFalse(receiveFullyResult.IsAllowed, "ReceiveFully")
        Assert.AreEqual(expectedErrorCode, receiveFullyResult.ErrorCode, "ReceiveFully")

    End Function

    ' ------------------------------------------------------------- box 3 (never deleted, stays readable)

    ''' <summary>A cancelled order is never physically deleted (spec section 12) and remains fully readable over HTTP.</summary>
    <TestMethod>
    Public Async Function CancelPurchaseOrder_OverHttp_OrderRemainsReadableAfterCancellation() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)
            Dim orderId As Integer = Await CreateDraftOrderAsync(client, token)

            Using cancelResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, $"/api/v1/purchase-orders/{orderId}/cancel", token,
                                 New CancelPurchaseOrderRequest With {.Reason = "no longer needed"})
                Assert.AreEqual(HttpStatusCode.OK, cancelResponse.StatusCode)
            End Using

            Using getResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Get, $"/api/v1/purchase-orders/{orderId}", token, Nothing)

                Assert.AreEqual(
                    HttpStatusCode.OK, getResponse.StatusCode,
                    "A cancelled order must remain fully readable, never a 404 - it was refused, not deleted.")
                Dim fetched As PurchaseOrderResponse = Await getResponse.Content.ReadFromJsonAsync(Of PurchaseOrderResponse)()
                Assert.AreEqual("Cancelled", fetched.Status)

            End Using

        End Using

    End Function

    ' ------------------------------------------------------------- box 4 (audited: actor, reason, correlation ID)

    <TestMethod>
    Public Async Function CancelPurchaseOrder_Success_AuditedWithActorReasonAndCorrelationId() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)
            Dim orderId As Integer = Await CreateDraftOrderAsync(client, token)
            Dim correlationId As String = Guid.NewGuid().ToString()
            Const reason As String = "supplier discontinued the item"

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, $"/api/v1/purchase-orders/{orderId}/cancel", token,
                                 New CancelPurchaseOrderRequest With {.Reason = reason}, correlationId)
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode)
            End Using

            Dim row = Await ReadAuditRowAsync(correlationId, "PurchaseOrderCancelled")
            Assert.AreEqual(1L, row.Count, "Exactly one audit row for a successful cancel.")
            Assert.AreEqual(_requesterUserId, row.ActorUserId)
            Assert.AreEqual(reason, row.Detail, "The reason must be captured in the audit trail, not just accepted and discarded.")
            Assert.AreEqual(correlationId, row.CorrelationId)

        End Using

    End Function

    <TestMethod>
    Public Async Function ClosePurchaseOrder_Success_AuditedWithActorReasonAndCorrelationId() As Task

        Dim orderId As Integer = Await CreateDraftDirectlyAsync()
        Await ForceStatusDirectlyAsync(orderId, "FullyReceived")
        Dim correlationId As String = Guid.NewGuid().ToString()
        Const reason As String = "all lines received, nothing outstanding"

        Dim outcome As PurchaseOrderTransitionOutcome =
            Await _purchaseOrderService.CloseAsync(orderId, _requesterUserId, reason, correlationId)
        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Success, outcome.Kind)

        Dim row = Await ReadAuditRowAsync(correlationId, "PurchaseOrderClosed")
        Assert.AreEqual(1L, row.Count)
        Assert.AreEqual(_requesterUserId, row.ActorUserId)
        Assert.AreEqual(reason, row.Detail)
        Assert.AreEqual(correlationId, row.CorrelationId)

    End Function

    ''' <summary>A missing reason is refused 400, field-level - the same shape MaintenanceController.Enter uses for its own required reason.</summary>
    <TestMethod>
    Public Async Function CancelPurchaseOrder_MissingReason_Refused400() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)
            Dim orderId As Integer = Await CreateDraftOrderAsync(client, token)

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, $"/api/v1/purchase-orders/{orderId}/cancel", token,
                                 New CancelPurchaseOrderRequest With {.Reason = ""})

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("VALIDATION_FAILED", body.ErrorCode)
                Assert.IsTrue(body.Errors.ContainsKey("reason"))

            End Using

        End Using

    End Function

    ''' <summary>An unknown order id is a controlled NotFound outcome, never an exception.</summary>
    <TestMethod>
    Public Async Function CancelAsync_UnknownOrder_ReturnsNotFound() As Task

        Dim outcome As PurchaseOrderTransitionOutcome =
            Await _purchaseOrderService.CancelAsync(999999999, _requesterUserId, "n/a", Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.NotFound, outcome.Kind)

    End Function

    ' --------------------------------------------------------------- helpers (HTTP)

    Private Async Function CreateDraftOrderAsync(client As HttpClient, token As String) As Task(Of Integer)

        Dim request As New CreatePurchaseOrderRequest With {
            .SupplierId = _supplierId,
            .IdempotencyKey = Guid.NewGuid().ToString("d"),
            .Lines = New List(Of CreatePurchaseOrderLineRequest) From {
                New CreatePurchaseOrderLineRequest With {
                    .ProductId = _productId, .OrderedQuantity = 1.000D, .PurchaseCost = 1.0000D}
            }
        }

        Using response As HttpResponseMessage =
            Await SendAsync(client, HttpMethod.Post, "/api/v1/purchase-orders", token, request)

            Assert.AreEqual(
                HttpStatusCode.Created, response.StatusCode,
                "Fixture purchase-order creation must succeed. Body: " & Await response.Content.ReadAsStringAsync())
            Return (Await response.Content.ReadFromJsonAsync(Of PurchaseOrderResponse)()).Id

        End Using

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

    ' --------------------------------------------------------------- helpers (direct service)

    Private Async Function CreateDraftDirectlyAsync() As Task(Of Integer)

        Dim lines As New List(Of CreatePurchaseOrderLineRequest) From {
            New CreatePurchaseOrderLineRequest With {
                .ProductId = _productId, .OrderedQuantity = 1.000D, .PurchaseCost = 1.0000D}
        }

        Dim outcome As PurchaseOrderCreationOutcome =
            Await _purchaseOrderService.CreateAsync(
                _supplierId, lines, _requesterUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseOrderCreationOutcomeKind.Created, outcome.Kind, "Fixture creation must succeed.")
        Return outcome.Response.Id

    End Function

    ''' <summary>Fixture setup only - forces a status no live Phase 3 endpoint can reach (PartiallyReceived/FullyReceived), or fast-forwards past Submit/Approve when only the FROM-state matters to the test.</summary>
    Private Async Function ForceStatusDirectlyAsync(orderId As Integer, status As String) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "UPDATE PurchaseOrders SET Status = @status, UpdatedAtUtc = UTC_TIMESTAMP(6) WHERE Id = @id;"
                command.Parameters.AddWithValue("@status", status)
                command.Parameters.AddWithValue("@id", orderId)
                Await command.ExecuteNonQueryAsync()
            End Using
        End Using

    End Function

    Private Async Function ReadAuditRowAsync(correlationId As String, action As String) As Task(Of (Count As Long, ActorUserId As Integer?, Detail As String, CorrelationId As String))

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT COUNT(*), MIN(ActorUserId), MIN(Detail), MIN(CorrelationId) " &
                    "FROM AuditLogs WHERE CorrelationId = @correlationId AND Action = @action;"
                command.Parameters.AddWithValue("@correlationId", correlationId)
                command.Parameters.AddWithValue("@action", action)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync()
                    Await reader.ReadAsync()

                    If reader.IsDBNull(1) Then
                        Return (Count:=CLng(reader.GetInt64(0)), ActorUserId:=CType(Nothing, Integer?), Detail:=Nothing, CorrelationId:=Nothing)
                    End If

                    Return (
                        Count:=CLng(reader.GetInt64(0)),
                        ActorUserId:=reader.GetInt32(1),
                        Detail:=reader.GetString(2),
                        CorrelationId:=reader.GetValue(3).ToString())

                End Using
            End Using
        End Using

    End Function

    ' --------------------------------------------------------------- fixtures

    Private Async Function CreateActiveSupplierAsync() As Task(Of Integer)

        Dim name As String = "P3-05 Supplier " & Guid.NewGuid().ToString("N").Substring(0, 12)

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

    Private Async Function EnsureFixtureProductAsync() As Task(Of Integer)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            Using selectCommand As MySqlCommand = connection.CreateCommand()
                selectCommand.CommandText = "SELECT Id FROM Products WHERE Sku = @sku;"
                selectCommand.Parameters.AddWithValue("@sku", FixtureProductSku)
                Dim existing As Object = Await selectCommand.ExecuteScalarAsync()
                If existing IsNot Nothing Then
                    Return CInt(existing)
                End If
            End Using

            Using insertCommand As MySqlCommand = connection.CreateCommand()
                insertCommand.CommandText =
                    "INSERT INTO Products (Sku, Barcode, Name, Price, Cost, IsActive, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@sku, NULL, 'P3-05 Fixture Product', 1.0000, 0.5000, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                insertCommand.Parameters.AddWithValue("@sku", FixtureProductSku)
                Await insertCommand.ExecuteNonQueryAsync()
                Return CInt(insertCommand.LastInsertedId)
            End Using

        End Using

    End Function

    ''' <summary>Returns the fixture user's Id, creating the account (via merch_migrator) only the first time.</summary>
    Private Async Function EnsureFixtureUserAsync(username As String, roleName As String) As Task(Of Integer)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Dim existing As User = Await UserRepository.FindByUsernameAsync(connection, username)
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
