' Merchandising.Tests.Integration.PurchaseOrderApprovalTests
'
' P3-04: POST /{id}/submit and POST /{id}/approve, spec section 9's "cannot
' approve their own purchase order" proven end-to-end over real HTTP through
' MerchandisingApiFactory against the real pinned MariaDB, and the
' status-change/audit atomicity spec section 11 requires.
'
' Role gating itself (PurchaseOrders.Submit / PurchaseOrders.Approve resolve
' to the roles PolicyRegistry says they do) is AuthorizationMatrixTests' job -
' this file proves the BEHAVIOUR:
'
'   box 1/2  self-approval is refused 403 with two real users; a DIFFERENT
'            authorized user approving the same order succeeds
'   box 3    the refusal is attributable and audited, and so is the success -
'            ApprovedByUserId / ApprovedAtUtc persisted, both paths audited
'   box 4    every illegal transition into Submitted/Approved is refused with
'            PurchaseOrderTransitions' stable error code
'   box 5    status change and audit row commit together or not at all - the
'            P1-12/P2-08-shaped forced-failure rollback, driven directly
'            against PurchaseOrderService since the fault-injection hook is
'            #If DEBUG-only and has no HTTP surface
'
' Fixture users, the supplier and product are real, permanent rows - nothing
' here is torn down (ADR-013: no DELETE grant on Users, Suppliers or
' Products). Two DISTINCT Admin fixtures are needed (not one Admin plus the
' ProcurementOfficer fixture other Phase 3 suites use) because
' PurchaseOrders.Approve is AdminAndAbove only (PolicyRegistry) - a
' ProcurementOfficer is refused for a ROLE reason before self-approval is
' ever reached, which would prove the wrong thing here.

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
Public Class PurchaseOrderApprovalTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const FixturePassword As String = "P3-04 Fixture Passw0rd!"
    Private Const AdminAUsername As String = "p3_04_fixture_admin_a"
    Private Const AdminBUsername As String = "p3_04_fixture_admin_b"
    Private Const ProcurementFixtureUsername As String = "p3_04_fixture_procurement"
    Private Const FixtureProductSku As String = "p3_04_fixture_sku"

    Private _factory As MerchandisingApiFactory
    Private _connectionFactory As ConnectionFactory
    Private _purchaseOrderService As PurchaseOrderService
    Private _supplierId As Integer
    Private _productId As Integer
    Private _adminAUserId As Integer
    Private _adminBUserId As Integer
    Private _procurementUserId As Integer

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _factory = New MerchandisingApiFactory()
        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        _purchaseOrderService = New PurchaseOrderService(_connectionFactory)

        _adminAUserId = Await EnsureFixtureUserAsync(AdminAUsername, "Admin")
        _adminBUserId = Await EnsureFixtureUserAsync(AdminBUsername, "Admin")
        _procurementUserId = Await EnsureFixtureUserAsync(ProcurementFixtureUsername, "ProcurementOfficer")
        _supplierId = Await CreateActiveSupplierAsync()
        _productId = Await EnsureFixtureProductAsync()

    End Function

    <TestCleanup>
    Public Sub TearDown()
        _factory?.Dispose()
    End Sub

    ' ------------------------------------------------------------- boxes 1-3

    ''' <summary>
    ''' Box 1/3: the SAME user who created and submitted an order is refused
    ''' 403 approving it, and the refusal is audited - the order's status,
    ''' ApprovedByUserId and ApprovedAtUtc are all untouched.
    ''' </summary>
    <TestMethod>
    Public Async Function ApprovePurchaseOrder_SameUserAsRequester_Refused403AndAudited() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim adminAToken As String = Await LoginAsync(client, AdminAUsername)
            Dim orderId As Integer = Await CreateAndSubmitOrderAsync(client, adminAToken)
            Dim correlationId As String = Guid.NewGuid().ToString()

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, $"/api/v1/purchase-orders/{orderId}/approve", adminAToken, Nothing, correlationId)

                Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, "Self-approval must be refused 403.")
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("FORBIDDEN", body.ErrorCode)
                Assert.IsFalse(String.IsNullOrWhiteSpace(body.CorrelationId))

            End Using

            Dim stored = Await ReadOrderAsync(orderId)
            Assert.AreEqual("Submitted", stored.Status, "A refused self-approval must leave the order Submitted, not Approved.")
            Assert.IsNull(stored.ApprovedByUserId)
            Assert.IsNull(stored.ApprovedAtUtc)

            Dim deniedAuditCount As Long =
                Await CountAuditRowsAsync(correlationId, "PurchaseOrderApprovalDenied", "Denied")
            Assert.AreEqual(1L, deniedAuditCount, "The self-approval refusal itself must be audited (spec section 9's privileged-action control).")

        End Using

    End Function

    ''' <summary>
    ''' Box 1/2/3: a DIFFERENT authorized user (also Admin, so the ROLE cell
    ''' is already satisfied - see AuthorizationMatrixTests for that
    ''' dimension) approving the same order succeeds, and the result is fully
    ''' attributable and audited.
    ''' </summary>
    <TestMethod>
    Public Async Function ApprovePurchaseOrder_DifferentAuthorizedUser_SucceedsAndIsAttributable() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim adminAToken As String = Await LoginAsync(client, AdminAUsername)
            Dim adminBToken As String = Await LoginAsync(client, AdminBUsername)
            Dim orderId As Integer = Await CreateAndSubmitOrderAsync(client, adminAToken)
            Dim correlationId As String = Guid.NewGuid().ToString()

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, $"/api/v1/purchase-orders/{orderId}/approve", adminBToken, Nothing, correlationId)

                Assert.AreEqual(
                    HttpStatusCode.OK, response.StatusCode,
                    "A different authorized user must be able to approve. Body: " & Await response.Content.ReadAsStringAsync())

                Dim approved As PurchaseOrderResponse = Await response.Content.ReadFromJsonAsync(Of PurchaseOrderResponse)()
                Assert.AreEqual("Approved", approved.Status)
                Assert.AreEqual(_adminAUserId, approved.RequestedByUserId, "RequestedByUserId must stay the original requester.")
                Assert.AreEqual(_adminBUserId, approved.ApprovedByUserId, "ApprovedByUserId must be the actual approver, not the requester.")
                Assert.IsNotNull(approved.ApprovedAtUtc)
                Assert.IsTrue(
                    (DateTime.UtcNow - approved.ApprovedAtUtc.Value).Duration() < TimeSpan.FromMinutes(1),
                    "ApprovedAtUtc must be a real, current UTC timestamp.")

            End Using

            Dim stored = Await ReadOrderAsync(orderId)
            Assert.AreEqual("Approved", stored.Status)
            Assert.AreEqual(_adminBUserId, stored.ApprovedByUserId)
            Assert.IsNotNull(stored.ApprovedAtUtc)

            Dim approvedAuditCount As Long =
                Await CountAuditRowsAsync(correlationId, "PurchaseOrderApproved", "Success")
            Assert.AreEqual(1L, approvedAuditCount, "Exactly one audit row for the successful approval, carrying the caller's correlation ID.")

        End Using

    End Function

    ''' <summary>
    ''' A ProcurementOfficer is refused for a ROLE reason (PurchaseOrders.Approve
    ''' is AdminAndAbove) even when they are NOT the requester - the two
    ''' denial reasons are distinct, and this proves the role gate fires
    ''' independently of ownership. AuthorizationMatrixTests' Approve matrix
    ''' test covers the full role dimension; this is the one cross-check that
    ''' belongs with the self-approval tests because it shares this class's
    ''' fixtures.
    ''' </summary>
    <TestMethod>
    Public Async Function ApprovePurchaseOrder_ProcurementOfficerNotTheRequester_StillRefused403() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim adminAToken As String = Await LoginAsync(client, AdminAUsername)
            Dim procurementToken As String = Await LoginAsync(client, ProcurementFixtureUsername)
            Dim orderId As Integer = Await CreateAndSubmitOrderAsync(client, adminAToken)

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, $"/api/v1/purchase-orders/{orderId}/approve", procurementToken, Nothing)

                Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("FORBIDDEN", body.ErrorCode)

            End Using

        End Using

    End Function

    ' ------------------------------------------------------------- box 4

    ''' <summary>Approving a Draft order (never submitted) is refused - P3-01's table has no (Draft, Approve) row.</summary>
    <TestMethod>
    Public Async Function ApproveAsync_OrderStillDraft_RefusedInvalidTransition() As Task

        Dim createOutcome As PurchaseOrderCreationOutcome = Await CreateDraftDirectlyAsync(_adminAUserId)
        Dim orderId As Integer = createOutcome.Response.Id

        Dim outcome As PurchaseOrderTransitionOutcome =
            Await _purchaseOrderService.ApproveAsync(orderId, _adminBUserId, Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Refused, outcome.Kind)
        Assert.AreEqual(PurchaseOrderTransitionErrors.InvalidTransition, outcome.ErrorCode)

    End Function

    ''' <summary>Submitting an already-Submitted order is refused - not a second no-op success.</summary>
    <TestMethod>
    Public Async Function SubmitAsync_OrderAlreadySubmitted_RefusedInvalidTransition() As Task

        Dim orderId As Integer = Await CreateAndSubmitDirectlyAsync(_adminAUserId)

        Dim outcome As PurchaseOrderTransitionOutcome =
            Await _purchaseOrderService.SubmitAsync(orderId, _adminAUserId, Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Refused, outcome.Kind)
        Assert.AreEqual(PurchaseOrderTransitionErrors.InvalidTransition, outcome.ErrorCode)

    End Function

    ''' <summary>Submitting an Approved order is refused - Submit is only legal from Draft (P3-01's table).</summary>
    <TestMethod>
    Public Async Function SubmitAsync_OrderAlreadyApproved_RefusedInvalidTransition() As Task

        Dim orderId As Integer = Await CreateAndSubmitDirectlyAsync(_adminAUserId)
        Dim approveOutcome As PurchaseOrderTransitionOutcome =
            Await _purchaseOrderService.ApproveAsync(orderId, _adminBUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Success, approveOutcome.Kind)

        Dim outcome As PurchaseOrderTransitionOutcome =
            Await _purchaseOrderService.SubmitAsync(orderId, _adminAUserId, Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Refused, outcome.Kind)
        Assert.AreEqual(PurchaseOrderTransitionErrors.InvalidTransition, outcome.ErrorCode)

    End Function

    ''' <summary>Approving an already-Approved order (double approval) is refused, not a silent second success.</summary>
    <TestMethod>
    Public Async Function ApproveAsync_OrderAlreadyApproved_RefusedInvalidTransition() As Task

        Dim orderId As Integer = Await CreateAndSubmitDirectlyAsync(_adminAUserId)
        Dim firstApproval As PurchaseOrderTransitionOutcome =
            Await _purchaseOrderService.ApproveAsync(orderId, _adminBUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Success, firstApproval.Kind)

        Dim outcome As PurchaseOrderTransitionOutcome =
            Await _purchaseOrderService.ApproveAsync(orderId, _adminBUserId, Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Refused, outcome.Kind)
        Assert.AreEqual(PurchaseOrderTransitionErrors.InvalidTransition, outcome.ErrorCode)

    End Function

    ''' <summary>An unknown order id is a controlled NotFound outcome, never an exception.</summary>
    <TestMethod>
    Public Async Function ApproveAsync_UnknownOrder_ReturnsNotFound() As Task

        Dim outcome As PurchaseOrderTransitionOutcome =
            Await _purchaseOrderService.ApproveAsync(999999999, _adminBUserId, Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.NotFound, outcome.Kind)

    End Function

    ' ------------------------------------------------------------- box 5

    ''' <summary>
    ''' Done-when box 5: fault injected after the audit insert, still inside
    ''' the open transaction, immediately before commit - the same shape
    ''' PriceChangeTests.ChangePriceAsync_FaultInjectedBeforeCommit_...
    ''' uses for P2-08/P1-12. The unhandled exception propagating out of
    ''' ApproveAsync is what proves the rollback: PurchaseOrders.Status,
    ''' ApprovedByUserId, ApprovedAtUtc and the audit row must all still be
    ''' exactly where they were before this call.
    ''' </summary>
    <TestMethod>
    Public Async Function ApproveAsync_FaultInjectedBeforeCommit_RollsBackStatusAndAuditRows() As Task

        Dim orderId As Integer = Await CreateAndSubmitDirectlyAsync(_adminAUserId)
        Dim correlationId As String = Guid.NewGuid().ToString()

        Dim beforeStatus As String = (Await ReadOrderAsync(orderId)).Status
        Dim auditCountBefore As Long = Await CountAuditRowsAsync(correlationId, "PurchaseOrderApproved", "Success")

        Assert.AreEqual("Submitted", beforeStatus, "Fixture must start Submitted, not already Approved.")
        Assert.AreEqual(0L, auditCountBefore, "A fresh correlation Id must start with no AuditLogs rows.")

        Dim faultInjected As Boolean = False

        Await Assert.ThrowsExactlyAsync(Of InvalidOperationException)(
            Function() _purchaseOrderService.ApproveAsync(
                orderId, _adminBUserId, correlationId,
                testOnlyFaultAfterAuditInsert:=Sub()
                                                   faultInjected = True
                                                   Throw New InvalidOperationException("P3-04 forced failure: after audit insert, before commit.")
                                               End Sub))

        Assert.IsTrue(faultInjected, "The fault-injection delegate must actually have fired for this proof to mean anything.")

        Dim stored = Await ReadOrderAsync(orderId)
        Assert.AreEqual("Submitted", stored.Status, "A rolled-back approval must leave Status untouched.")
        Assert.IsNull(stored.ApprovedByUserId, "A rolled-back approval must leave ApprovedByUserId untouched.")
        Assert.IsNull(stored.ApprovedAtUtc, "A rolled-back approval must leave ApprovedAtUtc untouched.")

        Dim auditCountAfter As Long = Await CountAuditRowsAsync(correlationId, "PurchaseOrderApproved", "Success")
        Assert.AreEqual(0L, auditCountAfter, "A rolled-back approval must leave zero AuditLogs rows.")

    End Function

    ' --------------------------------------------------------------- helpers (HTTP)

    Private Async Function CreateAndSubmitOrderAsync(client As HttpClient, requesterToken As String) As Task(Of Integer)

        Dim createRequest As New CreatePurchaseOrderRequest With {
            .SupplierId = _supplierId,
            .IdempotencyKey = Guid.NewGuid().ToString("d"),
            .Lines = New List(Of CreatePurchaseOrderLineRequest) From {
                New CreatePurchaseOrderLineRequest With {
                    .ProductId = _productId, .OrderedQuantity = 1.000D, .PurchaseCost = 1.0000D}
            }
        }

        Dim orderId As Integer

        Using createResponse As HttpResponseMessage =
            Await SendAsync(client, HttpMethod.Post, "/api/v1/purchase-orders", requesterToken, createRequest)

            Assert.AreEqual(
                HttpStatusCode.Created, createResponse.StatusCode,
                "Fixture purchase-order creation must succeed. Body: " & Await createResponse.Content.ReadAsStringAsync())
            orderId = (Await createResponse.Content.ReadFromJsonAsync(Of PurchaseOrderResponse)()).Id

        End Using

        Using submitResponse As HttpResponseMessage =
            Await SendAsync(client, HttpMethod.Post, $"/api/v1/purchase-orders/{orderId}/submit", requesterToken, Nothing)

            Assert.AreEqual(
                HttpStatusCode.OK, submitResponse.StatusCode,
                "Fixture purchase-order submit must succeed. Body: " & Await submitResponse.Content.ReadAsStringAsync())

        End Using

        Return orderId

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

    Private Async Function CreateDraftDirectlyAsync(requesterUserId As Integer) As Task(Of PurchaseOrderCreationOutcome)

        Dim lines As New List(Of CreatePurchaseOrderLineRequest) From {
            New CreatePurchaseOrderLineRequest With {
                .ProductId = _productId, .OrderedQuantity = 1.000D, .PurchaseCost = 1.0000D}
        }

        Dim outcome As PurchaseOrderCreationOutcome =
            Await _purchaseOrderService.CreateAsync(
                _supplierId, lines, requesterUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseOrderCreationOutcomeKind.Created, outcome.Kind, "Fixture creation must succeed.")
        Return outcome

    End Function

    Private Async Function CreateAndSubmitDirectlyAsync(requesterUserId As Integer) As Task(Of Integer)

        Dim createOutcome As PurchaseOrderCreationOutcome = Await CreateDraftDirectlyAsync(requesterUserId)
        Dim orderId As Integer = createOutcome.Response.Id

        Dim submitOutcome As PurchaseOrderTransitionOutcome =
            Await _purchaseOrderService.SubmitAsync(orderId, requesterUserId, Guid.NewGuid().ToString())

        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Success, submitOutcome.Kind, "Fixture submit must succeed.")
        Return orderId

    End Function

    ' --------------------------------------------------------------- fixtures

    ''' <summary>A brand-new active supplier per test run, so no test's assertions depend on another test's leftover rows.</summary>
    Private Async Function CreateActiveSupplierAsync() As Task(Of Integer)

        Dim name As String = "P3-04 Supplier " & Guid.NewGuid().ToString("N").Substring(0, 12)

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

    ''' <summary>One permanent active fixture product. Reused across runs - Products has no DELETE grant.</summary>
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
                    "VALUES (@sku, NULL, 'P3-04 Fixture Product', 1.0000, 0.5000, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                insertCommand.Parameters.AddWithValue("@sku", FixtureProductSku)
                Await insertCommand.ExecuteNonQueryAsync()
                Return CInt(insertCommand.LastInsertedId)
            End Using

        End Using

    End Function

    Private Async Function ReadOrderAsync(orderId As Integer) As Task(Of (Status As String, ApprovedByUserId As Integer?, ApprovedAtUtc As DateTime?))

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT Status, ApprovedByUserId, ApprovedAtUtc FROM PurchaseOrders WHERE Id = @id;"
                command.Parameters.AddWithValue("@id", orderId)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync()
                    Await reader.ReadAsync()

                    Dim approvedByOrdinal As Integer = reader.GetOrdinal("ApprovedByUserId")
                    Dim approvedAtOrdinal As Integer = reader.GetOrdinal("ApprovedAtUtc")

                    Return (
                        Status:=reader.GetString(reader.GetOrdinal("Status")),
                        ApprovedByUserId:=If(reader.IsDBNull(approvedByOrdinal), CType(Nothing, Integer?), reader.GetInt32(approvedByOrdinal)),
                        ApprovedAtUtc:=If(reader.IsDBNull(approvedAtOrdinal), CType(Nothing, DateTime?), reader.GetDateTime(approvedAtOrdinal)))

                End Using
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
