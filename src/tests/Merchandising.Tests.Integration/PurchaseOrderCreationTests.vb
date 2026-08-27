' Merchandising.Tests.Integration.PurchaseOrderCreationTests
'
' P3-03: POST /api/v1/purchase-orders and the two reads that go with it,
' proven over real HTTP through MerchandisingApiFactory against the real
' pinned MariaDB.
'
' Role gating itself (PurchaseOrders.Create / PurchaseOrders.Track resolve to
' the roles PolicyRegistry says they do) is AuthorizationMatrixTests' job -
' this file proves the BEHAVIOUR of the endpoints:
'
'   box 1  a created order starts in Draft, server-assigned, and a
'          client-supplied status is refused rather than silently dropped
'   box 2  lines reference ACTIVE products; an inactive product - and, by the
'          same argument, an inactive supplier - is refused with a stable
'          error code
'   box 3  money and quantity scale validated at the API boundary BEFORE
'          binding (ADR-004.1), asserted by proving NO row was written, since
'          a correctly-scaled stored value would prove nothing
'   box 4  a repeated idempotency key returns the original committed order,
'          never a second one (ADR-007), including under real concurrency
'   box 5  pagination, max page size, sort and filter (spec section 13)
'   box 6  creation audited through the P2-04 pipeline
'
' Fixture users, suppliers and products are real, permanent rows - merch_api
' holds no DELETE on any of them (ADR-013) - so nothing here is cleaned up in
' TearDown. Purchase orders accumulate across runs by design; every test that
' counts rows either scopes its count to a supplier it created itself in that
' test, or takes a before/after delta.

Imports System.Collections.Generic
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Net.Http.Json
Imports System.Threading.Tasks
Imports Merchandising.Contracts.Auth
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Procurement
Imports Merchandising.Domain.Entities
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class PurchaseOrderCreationTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const FixturePassword As String = "P3-03 Fixture Passw0rd!"
    Private Const ProcurementFixtureUsername As String = "p3_03_fixture_procurement"

    Private Const ActiveProductSku As String = "p3_03_fixture_active_sku"
    Private Const InactiveProductSku As String = "p3_03_fixture_inactive_sku"
    Private Const InactiveSupplierName As String = "P3-03 Fixture Inactive Supplier"

    Private _factory As MerchandisingApiFactory
    Private _connectionFactory As ConnectionFactory

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _factory = New MerchandisingApiFactory()
        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        Await EnsureFixtureUserAsync(ProcurementFixtureUsername, "ProcurementOfficer")

    End Function

    <TestCleanup>
    Public Sub TearDown()
        _factory?.Dispose()
    End Sub

    ' ------------------------------------------------------------- box 1

    ''' <summary>
    ''' Box 1: the created order is Draft, and every identity on it -
    ''' OrderNumber, LineNumber, RequestedByUserId - is assigned by the
    ''' server, not taken from the request.
    ''' </summary>
    <TestMethod>
    Public Async Function CreatePurchaseOrder_StartsInDraft_WithServerAssignedIdentities() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)
            Dim supplierId As Integer = Await CreateActiveSupplierAsync()
            Dim productId As Integer = Await EnsureProductAsync(ActiveProductSku, isActive:=True)

            Dim created As PurchaseOrderResponse = Await CreateOrderAsync(
                client, token,
                NewOrderRequest(supplierId, New CreatePurchaseOrderLineRequest With {
                    .ProductId = productId, .OrderedQuantity = 12.500D, .PurchaseCost = 34.5600D
                }))

            Assert.AreEqual("Draft", created.Status, "A newly created purchase order must be Draft (spec section 10.1).")
            Assert.IsGreaterThan(0, created.Id)
            Assert.IsFalse(String.IsNullOrWhiteSpace(created.OrderNumber), "The server must assign an order number.")
            Assert.IsGreaterThan(0, created.RequestedByUserId, "RequestedByUserId must come from the authenticated caller.")
            Assert.IsNull(created.ApprovedByUserId, "A Draft order has no approver.")
            Assert.IsNull(created.SubmittedAtUtc)
            Assert.IsNull(created.ApprovedAtUtc)

            Assert.HasCount(1, created.Lines)
            Assert.AreEqual(1, created.Lines(0).LineNumber, "Line numbers are server-assigned, starting at 1.")
            Assert.AreEqual(12.500D, created.Lines(0).OrderedQuantity)
            Assert.AreEqual(34.5600D, created.Lines(0).PurchaseCost)
            Assert.AreEqual(0D, created.Lines(0).ReceivedQuantity, "Nothing has been received against a new order.")

            ' Read it back: what was returned is what was stored.
            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Get, $"/api/v1/purchase-orders/{created.Id}", token, Nothing)

                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode)
                Dim fetched As PurchaseOrderResponse = Await response.Content.ReadFromJsonAsync(Of PurchaseOrderResponse)()
                Assert.AreEqual(created.OrderNumber, fetched.OrderNumber)
                Assert.AreEqual("Draft", fetched.Status)
                Assert.AreEqual(supplierId, fetched.SupplierId)
                Assert.HasCount(1, fetched.Lines)
                Assert.AreEqual(productId, fetched.Lines(0).ProductId)

            End Using

        End Using

    End Function

    ''' <summary>
    ''' Box 1, the half that is easy to fake: a status in the request body is
    ''' REFUSED, not quietly ignored. A silently-dropped status would leave a
    ''' caller believing it had created an Approved order.
    ''' </summary>
    <TestMethod>
    Public Async Function CreatePurchaseOrder_ClientSuppliedStatus_Refused400() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)
            Dim supplierId As Integer = Await CreateActiveSupplierAsync()
            Dim productId As Integer = Await EnsureProductAsync(ActiveProductSku, isActive:=True)

            Dim request As CreatePurchaseOrderRequest = NewOrderRequest(
                supplierId, New CreatePurchaseOrderLineRequest With {
                    .ProductId = productId, .OrderedQuantity = 1.000D, .PurchaseCost = 1.0000D})
            request.Status = "Approved"

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/purchase-orders", token, request)

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("VALIDATION_FAILED", body.ErrorCode)
                Assert.IsTrue(
                    body.Errors IsNot Nothing AndAlso body.Errors.ContainsKey("status"),
                    "The refusal must name the offending field. Body: " & Await response.Content.ReadAsStringAsync())

            End Using

        End Using

    End Function

    ' ------------------------------------------------------------- box 2

    ''' <summary>Box 2: an inactive product cannot be newly ordered (spec section 10.2), with a stable error code.</summary>
    <TestMethod>
    Public Async Function CreatePurchaseOrder_InactiveProductLine_RefusedWithStableErrorCode() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)
            Dim supplierId As Integer = Await CreateActiveSupplierAsync()
            Dim inactiveProductId As Integer = Await EnsureProductAsync(InactiveProductSku, isActive:=False)

            Using response As HttpResponseMessage =
                Await SendAsync(
                    client, HttpMethod.Post, "/api/v1/purchase-orders", token,
                    NewOrderRequest(supplierId, New CreatePurchaseOrderLineRequest With {
                        .ProductId = inactiveProductId, .OrderedQuantity = 1.000D, .PurchaseCost = 1.0000D}))

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("PRODUCT_INACTIVE", body.ErrorCode)
                Assert.IsFalse(String.IsNullOrWhiteSpace(body.CorrelationId))

            End Using

        End Using

    End Function

    ''' <summary>Box 2, by the same argument: an order cannot be raised against a supplier that has been deactivated (P2-10 lifecycle).</summary>
    <TestMethod>
    Public Async Function CreatePurchaseOrder_InactiveSupplier_RefusedWithStableErrorCode() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)
            Dim inactiveSupplierId As Integer = Await EnsureInactiveSupplierAsync()
            Dim productId As Integer = Await EnsureProductAsync(ActiveProductSku, isActive:=True)

            Using response As HttpResponseMessage =
                Await SendAsync(
                    client, HttpMethod.Post, "/api/v1/purchase-orders", token,
                    NewOrderRequest(inactiveSupplierId, New CreatePurchaseOrderLineRequest With {
                        .ProductId = productId, .OrderedQuantity = 1.000D, .PurchaseCost = 1.0000D}))

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("SUPPLIER_INACTIVE", body.ErrorCode)

            End Using

        End Using

    End Function

    ''' <summary>An unknown supplier or product is a field-level validation failure, not a 500 and not an FK error surfacing from the database.</summary>
    <TestMethod>
    Public Async Function CreatePurchaseOrder_UnknownSupplierAndProduct_RefusedWithFieldDetail() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)

            Using response As HttpResponseMessage =
                Await SendAsync(
                    client, HttpMethod.Post, "/api/v1/purchase-orders", token,
                    NewOrderRequest(999999999, New CreatePurchaseOrderLineRequest With {
                        .ProductId = 999999999, .OrderedQuantity = 1.000D, .PurchaseCost = 1.0000D}))

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("VALIDATION_FAILED", body.ErrorCode)
                Assert.IsTrue(body.Errors.ContainsKey("supplierId"))
                Assert.IsTrue(body.Errors.Keys.Any(Function(k) k.Contains("productId")))

            End Using

        End Using

    End Function

    ''' <summary>An order with no lines is not an order. Refused before anything is written.</summary>
    <TestMethod>
    Public Async Function CreatePurchaseOrder_NoLines_Refused400() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)
            Dim supplierId As Integer = Await CreateActiveSupplierAsync()

            Dim request As New CreatePurchaseOrderRequest With {
                .SupplierId = supplierId,
                .Lines = Array.Empty(Of CreatePurchaseOrderLineRequest)(),
                .IdempotencyKey = Guid.NewGuid().ToString("d")
            }

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/purchase-orders", token, request)

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("VALIDATION_FAILED", body.ErrorCode)
                Assert.IsTrue(body.Errors.ContainsKey("lines"))

            End Using

        End Using

    End Function

    ' ------------------------------------------------------------- box 3

    ''' <summary>
    ''' Box 3 / ADR-004.1: an over-scale QUANTITY (4 dp into DECIMAL(19,3))
    ''' is refused at the boundary. Asserted by proving no PurchaseOrders row
    ''' and no IdempotencyKeys row were written - a stored 12.346 would look
    ''' correct whether the guard ran or whether MariaDB silently rounded it
    ''' (Note 1265), so reading the value back proves nothing.
    ''' </summary>
    <TestMethod>
    Public Async Function CreatePurchaseOrder_OverScaleQuantity_RefusedBeforeAnyRowIsWritten() As Task

        Await AssertOverScaleRefusedWithoutWritingAsync(
            quantity:=12.3456D, cost:=1.0000D, expectedField:="lines[0].orderedQuantity")

    End Function

    ''' <summary>Box 3 / ADR-004.1, the money half: 5 dp into DECIMAL(19,4).</summary>
    <TestMethod>
    Public Async Function CreatePurchaseOrder_OverScaleCost_RefusedBeforeAnyRowIsWritten() As Task

        Await AssertOverScaleRefusedWithoutWritingAsync(
            quantity:=1.000D, cost:=1.99999D, expectedField:="lines[0].purchaseCost")

    End Function

    Private Async Function AssertOverScaleRefusedWithoutWritingAsync(
        quantity As Decimal, cost As Decimal, expectedField As String) As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)
            Dim supplierId As Integer = Await CreateActiveSupplierAsync()
            Dim productId As Integer = Await EnsureProductAsync(ActiveProductSku, isActive:=True)

            Dim ordersBefore As Long = Await CountRowsAsync("SELECT COUNT(*) FROM PurchaseOrders;")
            Dim keysBefore As Long = Await CountRowsAsync("SELECT COUNT(*) FROM IdempotencyKeys;")

            Using response As HttpResponseMessage =
                Await SendAsync(
                    client, HttpMethod.Post, "/api/v1/purchase-orders", token,
                    NewOrderRequest(supplierId, New CreatePurchaseOrderLineRequest With {
                        .ProductId = productId, .OrderedQuantity = quantity, .PurchaseCost = cost}))

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("VALIDATION_FAILED", body.ErrorCode)
                Assert.IsTrue(
                    body.Errors.ContainsKey(expectedField),
                    $"Expected a field error on '{expectedField}'. Got: {String.Join(", ", body.Errors.Keys)}")

            End Using

            Assert.AreEqual(ordersBefore, Await CountRowsAsync("SELECT COUNT(*) FROM PurchaseOrders;"),
                            "An over-scale value must be refused before any PurchaseOrders row is written.")
            Assert.AreEqual(keysBefore, Await CountRowsAsync("SELECT COUNT(*) FROM IdempotencyKeys;"),
                            "An over-scale value must be refused before the idempotency key is even claimed.")

        End Using

    End Function

    ' ------------------------------------------------------------- box 4

    ''' <summary>Box 4 / ADR-007: the same key twice returns the SAME order, and creates exactly one.</summary>
    <TestMethod>
    Public Async Function CreatePurchaseOrder_RepeatedIdempotencyKey_ReplaysTheOriginalOrder() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)
            Dim supplierId As Integer = Await CreateActiveSupplierAsync()
            Dim productId As Integer = Await EnsureProductAsync(ActiveProductSku, isActive:=True)

            Dim request As CreatePurchaseOrderRequest = NewOrderRequest(
                supplierId, New CreatePurchaseOrderLineRequest With {
                    .ProductId = productId, .OrderedQuantity = 3.000D, .PurchaseCost = 2.5000D})

            Dim first As PurchaseOrderResponse = Await CreateOrderAsync(client, token, request)

            ' Byte-for-byte the same request, including the key.
            Using replay As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/purchase-orders", token, request)

                Assert.IsTrue(
                    CInt(replay.StatusCode) >= 200 AndAlso CInt(replay.StatusCode) < 300,
                    $"A replayed idempotency key must succeed, not error. Got {CInt(replay.StatusCode)}.")

                Dim replayed As PurchaseOrderResponse = Await replay.Content.ReadFromJsonAsync(Of PurchaseOrderResponse)()
                Assert.AreEqual(first.Id, replayed.Id, "A repeated key must return the ORIGINAL order.")
                Assert.AreEqual(first.OrderNumber, replayed.OrderNumber)

            End Using

            Assert.AreEqual(
                1L, Await CountOrdersForSupplierAsync(supplierId),
                "A repeated idempotency key must never create a second order.")

        End Using

    End Function

    ''' <summary>
    ''' Box 4 under real concurrency, the P1-14 shape: ten simultaneous
    ''' requests sharing one key. All ten report the same order id and
    ''' exactly one row exists.
    ''' </summary>
    <TestMethod>
    Public Async Function CreatePurchaseOrder_TenConcurrentRequestsSameKey_CreateExactlyOneOrder() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)
            Dim supplierId As Integer = Await CreateActiveSupplierAsync()
            Dim productId As Integer = Await EnsureProductAsync(ActiveProductSku, isActive:=True)

            Dim request As CreatePurchaseOrderRequest = NewOrderRequest(
                supplierId, New CreatePurchaseOrderLineRequest With {
                    .ProductId = productId, .OrderedQuantity = 1.000D, .PurchaseCost = 1.0000D})

            Dim attempts As New List(Of Task(Of HttpResponseMessage))
            For i As Integer = 1 To 10
                attempts.Add(SendAsync(client, HttpMethod.Post, "/api/v1/purchase-orders", token, request))
            Next

            Dim responses As HttpResponseMessage() = Await Task.WhenAll(attempts)

            Dim reportedIds As New List(Of Integer)
            For Each response As HttpResponseMessage In responses
                Assert.IsTrue(
                    CInt(response.StatusCode) >= 200 AndAlso CInt(response.StatusCode) < 300,
                    $"Every concurrent attempt on one key must succeed. Got {CInt(response.StatusCode)}: " &
                    Await response.Content.ReadAsStringAsync())
                reportedIds.Add((Await response.Content.ReadFromJsonAsync(Of PurchaseOrderResponse)()).Id)
                response.Dispose()
            Next

            Console.WriteLine("P3-03 ten concurrent same-key creates -> ids " & String.Join(", ", reportedIds))

            Assert.AreEqual(1, reportedIds.Distinct().Count(), "All ten attempts must report the same order id.")
            Assert.AreEqual(1L, Await CountOrdersForSupplierAsync(supplierId), "Exactly one order row may exist.")

        End Using

    End Function

    ''' <summary>
    ''' The other side of the concurrency coin, and the one that tests the
    ''' order-number generator rather than the idempotency store: ten
    ''' simultaneous requests with DISTINCT keys must all succeed with ten
    ''' distinct order numbers. UQ_PurchaseOrders_OrderNumber turns a lost
    ''' race into ERROR 1062, which the generator retries; if it did not,
    ''' this test would surface it as a 500.
    ''' </summary>
    <TestMethod>
    Public Async Function CreatePurchaseOrder_TenConcurrentRequestsDistinctKeys_ProduceTenDistinctOrderNumbers() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)
            Dim supplierId As Integer = Await CreateActiveSupplierAsync()
            Dim productId As Integer = Await EnsureProductAsync(ActiveProductSku, isActive:=True)

            Dim attempts As New List(Of Task(Of HttpResponseMessage))
            For i As Integer = 1 To 10
                attempts.Add(SendAsync(
                    client, HttpMethod.Post, "/api/v1/purchase-orders", token,
                    NewOrderRequest(supplierId, New CreatePurchaseOrderLineRequest With {
                        .ProductId = productId, .OrderedQuantity = 1.000D, .PurchaseCost = 1.0000D})))
            Next

            Dim responses As HttpResponseMessage() = Await Task.WhenAll(attempts)

            Dim orderNumbers As New List(Of String)
            For Each response As HttpResponseMessage In responses
                Assert.AreEqual(
                    HttpStatusCode.Created, response.StatusCode,
                    "A concurrent create with its own key must succeed. Body: " & Await response.Content.ReadAsStringAsync())
                orderNumbers.Add((Await response.Content.ReadFromJsonAsync(Of PurchaseOrderResponse)()).OrderNumber)
                response.Dispose()
            Next

            Console.WriteLine("P3-03 ten concurrent distinct-key creates -> " & String.Join(", ", orderNumbers))

            Assert.AreEqual(10, orderNumbers.Distinct().Count(), "Ten orders must carry ten distinct order numbers.")
            Assert.AreEqual(10L, Await CountOrdersForSupplierAsync(supplierId))

        End Using

    End Function

    ''' <summary>A missing or malformed idempotency key is refused - ADR-007 makes it required on every write command.</summary>
    <TestMethod>
    Public Async Function CreatePurchaseOrder_MissingIdempotencyKey_Refused400() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)
            Dim supplierId As Integer = Await CreateActiveSupplierAsync()
            Dim productId As Integer = Await EnsureProductAsync(ActiveProductSku, isActive:=True)

            Dim request As CreatePurchaseOrderRequest = NewOrderRequest(
                supplierId, New CreatePurchaseOrderLineRequest With {
                    .ProductId = productId, .OrderedQuantity = 1.000D, .PurchaseCost = 1.0000D})
            request.IdempotencyKey = Nothing

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/purchase-orders", token, request)

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("VALIDATION_FAILED", body.ErrorCode)
                Assert.IsTrue(body.Errors.ContainsKey("idempotencyKey"))

            End Using

        End Using

    End Function

    ' ------------------------------------------------------------- box 5

    ''' <summary>
    ''' Box 5: filtering by supplier and by status, both applied server-side.
    ''' Scoped to a supplier this test created, so the expected count is
    ''' exact rather than "at least".
    ''' </summary>
    <TestMethod>
    Public Async Function ListPurchaseOrders_FiltersBySupplierAndStatus() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)
            Dim supplierId As Integer = Await CreateActiveSupplierAsync()
            Dim productId As Integer = Await EnsureProductAsync(ActiveProductSku, isActive:=True)

            For i As Integer = 1 To 3
                Await CreateOrderAsync(
                    client, token,
                    NewOrderRequest(supplierId, New CreatePurchaseOrderLineRequest With {
                        .ProductId = productId, .OrderedQuantity = 1.000D, .PurchaseCost = 1.0000D}))
            Next

            Dim bySupplier As PurchaseOrderSearchResponse =
                Await ListAsync(client, token, $"?supplierId={supplierId}")
            Assert.AreEqual(3, bySupplier.TotalCount, "The supplier filter must return exactly this test's three orders.")
            Assert.IsTrue(bySupplier.Items.All(Function(o) o.SupplierId = supplierId))
            Assert.IsTrue(bySupplier.Items.All(Function(o) o.LineCount = 1), "A list row reports its line count.")

            Dim draftOnly As PurchaseOrderSearchResponse =
                Await ListAsync(client, token, $"?supplierId={supplierId}&status=Draft")
            Assert.AreEqual(3, draftOnly.TotalCount, "All three are Draft.")

            Dim approvedOnly As PurchaseOrderSearchResponse =
                Await ListAsync(client, token, $"?supplierId={supplierId}&status=Approved")
            Assert.AreEqual(0, approvedOnly.TotalCount, "None of them are Approved - nothing in Phase 3 can approve one yet.")

        End Using

    End Function

    ''' <summary>Box 5: an unknown status filter is refused, not silently treated as "no filter".</summary>
    <TestMethod>
    Public Async Function ListPurchaseOrders_UnknownStatusFilter_Refused400() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Get, "/api/v1/purchase-orders?status=Shipped", token, Nothing)

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("VALIDATION_FAILED", body.ErrorCode)
                Assert.IsTrue(body.Errors.ContainsKey("status"))

            End Using

        End Using

    End Function

    ''' <summary>Box 5: pageSize is clamped to the documented maximum, and the response says what was applied.</summary>
    <TestMethod>
    Public Async Function ListPurchaseOrders_PageSizeClampedToMaximum_AndEchoedBack() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)

            Dim defaulted As PurchaseOrderSearchResponse = Await ListAsync(client, token, "")
            Assert.AreEqual(1, defaulted.Page, "Page defaults to 1.")
            Assert.AreEqual(25, defaulted.PageSize, "Page size defaults to 25.")
            Assert.AreEqual(100, defaulted.MaxPageSize)
            Assert.AreEqual("createdAt:desc", defaulted.Sort, "Newest first is the default sort.")

            Dim oversized As PurchaseOrderSearchResponse = Await ListAsync(client, token, "?pageSize=5000")
            Assert.AreEqual(100, oversized.PageSize, "An oversized page size is clamped to the maximum, not honoured.")
            Assert.IsLessThanOrEqualTo(100, oversized.Items.Count)

            Dim negative As PurchaseOrderSearchResponse = Await ListAsync(client, token, "?page=-4&pageSize=0")
            Assert.AreEqual(1, negative.Page)
            Assert.AreEqual(25, negative.PageSize)

        End Using

    End Function

    ''' <summary>Box 5: sorting is a whitelist. A field nobody registered is refused rather than concatenated into SQL.</summary>
    <TestMethod>
    Public Async Function ListPurchaseOrders_SortIsWhitelisted() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)
            Dim supplierId As Integer = Await CreateActiveSupplierAsync()
            Dim productId As Integer = Await EnsureProductAsync(ActiveProductSku, isActive:=True)

            For i As Integer = 1 To 3
                Await CreateOrderAsync(
                    client, token,
                    NewOrderRequest(supplierId, New CreatePurchaseOrderLineRequest With {
                        .ProductId = productId, .OrderedQuantity = 1.000D, .PurchaseCost = 1.0000D}))
            Next

            Dim ascending As PurchaseOrderSearchResponse =
                Await ListAsync(client, token, $"?supplierId={supplierId}&sort=orderNumber:asc")
            Assert.AreEqual("orderNumber:asc", ascending.Sort)
            Dim numbers As List(Of String) = ascending.Items.Select(Function(o) o.OrderNumber).ToList()
            CollectionAssert.AreEqual(
                numbers.OrderBy(Function(n) n, StringComparer.Ordinal).ToList(), numbers,
                "sort=orderNumber:asc must return ascending order numbers.")

            Dim descending As PurchaseOrderSearchResponse =
                Await ListAsync(client, token, $"?supplierId={supplierId}&sort=orderNumber:desc")
            Assert.AreEqual("orderNumber:desc", descending.Sort)
            CollectionAssert.AreEqual(
                numbers.OrderByDescending(Function(n) n, StringComparer.Ordinal).ToList(),
                descending.Items.Select(Function(o) o.OrderNumber).ToList())

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Get, "/api/v1/purchase-orders?sort=rowVersion:asc", token, Nothing)

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("VALIDATION_FAILED", body.ErrorCode)
                Assert.IsTrue(body.Errors.ContainsKey("sort"))

            End Using

        End Using

    End Function

    ''' <summary>Box 5: paging actually pages - page 2 is a different, non-overlapping set.</summary>
    <TestMethod>
    Public Async Function ListPurchaseOrders_SecondPageDoesNotRepeatTheFirst() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)
            Dim supplierId As Integer = Await CreateActiveSupplierAsync()
            Dim productId As Integer = Await EnsureProductAsync(ActiveProductSku, isActive:=True)

            For i As Integer = 1 To 5
                Await CreateOrderAsync(
                    client, token,
                    NewOrderRequest(supplierId, New CreatePurchaseOrderLineRequest With {
                        .ProductId = productId, .OrderedQuantity = 1.000D, .PurchaseCost = 1.0000D}))
            Next

            Dim pageOne As PurchaseOrderSearchResponse =
                Await ListAsync(client, token, $"?supplierId={supplierId}&sort=orderNumber:asc&page=1&pageSize=2")
            Dim pageTwo As PurchaseOrderSearchResponse =
                Await ListAsync(client, token, $"?supplierId={supplierId}&sort=orderNumber:asc&page=2&pageSize=2")
            Dim pageThree As PurchaseOrderSearchResponse =
                Await ListAsync(client, token, $"?supplierId={supplierId}&sort=orderNumber:asc&page=3&pageSize=2")

            Assert.AreEqual(5, pageOne.TotalCount, "TotalCount is the whole result set, not the page.")
            Assert.HasCount(2, pageOne.Items)
            Assert.HasCount(2, pageTwo.Items)
            Assert.HasCount(1, pageThree.Items, "The last page carries the remainder.")

            Dim allIds As List(Of Integer) =
                pageOne.Items.Concat(pageTwo.Items).Concat(pageThree.Items).Select(Function(o) o.Id).ToList()
            Assert.AreEqual(5, allIds.Distinct().Count(), "Pages must not overlap.")

        End Using

    End Function

    ' ------------------------------------------------------------- box 6

    ''' <summary>Box 6: a successful create writes exactly one AuditLogs row, carrying the caller's correlation ID.</summary>
    <TestMethod>
    Public Async Function CreatePurchaseOrder_Success_WritesExactlyOneAuditRow() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)
            Dim supplierId As Integer = Await CreateActiveSupplierAsync()
            Dim productId As Integer = Await EnsureProductAsync(ActiveProductSku, isActive:=True)
            Dim correlationId As String = Guid.NewGuid().ToString()

            Using response As HttpResponseMessage =
                Await SendAsync(
                    client, HttpMethod.Post, "/api/v1/purchase-orders", token,
                    NewOrderRequest(supplierId, New CreatePurchaseOrderLineRequest With {
                        .ProductId = productId, .OrderedQuantity = 1.000D, .PurchaseCost = 1.0000D}),
                    correlationId)

                Assert.AreEqual(HttpStatusCode.Created, response.StatusCode)

            End Using

            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
                Using command As MySqlCommand = connection.CreateCommand()
                    command.CommandText =
                        "SELECT COUNT(*) FROM AuditLogs WHERE CorrelationId = @correlationId AND Action = @action;"
                    command.Parameters.AddWithValue("@correlationId", correlationId)
                    command.Parameters.AddWithValue("@action", "PurchaseOrderCreated")
                    Assert.AreEqual(
                        1L, CLng(Await command.ExecuteScalarAsync()),
                        "Exactly one AuditLogs row must be written for a successful create.")
                End Using
            End Using

        End Using

    End Function

    ''' <summary>An unknown order id is a controlled 404 with a stable code, never a leak (ADR-014).</summary>
    <TestMethod>
    Public Async Function GetPurchaseOrder_UnknownId_ReturnsControlled404() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Get, "/api/v1/purchase-orders/999999999", token, Nothing)

                Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("PURCHASE_ORDER_NOT_FOUND", body.ErrorCode)
                Assert.IsFalse(String.IsNullOrWhiteSpace(body.CorrelationId))

            End Using

        End Using

    End Function

    ' --------------------------------------------------------------- helpers

    Private Shared Function NewOrderRequest(
        supplierId As Integer, ParamArray lines As CreatePurchaseOrderLineRequest()) As CreatePurchaseOrderRequest

        Return New CreatePurchaseOrderRequest With {
            .SupplierId = supplierId,
            .Lines = lines.ToList(),
            .IdempotencyKey = Guid.NewGuid().ToString("d")
        }

    End Function

    Private Async Function CreateOrderAsync(
        client As HttpClient, token As String, request As CreatePurchaseOrderRequest) As Task(Of PurchaseOrderResponse)

        Using response As HttpResponseMessage =
            Await SendAsync(client, HttpMethod.Post, "/api/v1/purchase-orders", token, request)

            Assert.AreEqual(
                HttpStatusCode.Created, response.StatusCode,
                "Purchase-order creation failed. Body: " & Await response.Content.ReadAsStringAsync())
            Return Await response.Content.ReadFromJsonAsync(Of PurchaseOrderResponse)()

        End Using

    End Function

    Private Async Function ListAsync(client As HttpClient, token As String, queryString As String) As Task(Of PurchaseOrderSearchResponse)

        Using response As HttpResponseMessage =
            Await SendAsync(client, HttpMethod.Get, "/api/v1/purchase-orders" & queryString, token, Nothing)

            Assert.AreEqual(
                HttpStatusCode.OK, response.StatusCode,
                $"GET /api/v1/purchase-orders{queryString} failed. Body: " & Await response.Content.ReadAsStringAsync())
            Return Await response.Content.ReadFromJsonAsync(Of PurchaseOrderSearchResponse)()

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

    ' --------------------------------------------------------------- fixtures

    ''' <summary>A brand-new active supplier per call, so a test that counts orders by supplier counts only its own.</summary>
    Private Async Function CreateActiveSupplierAsync() As Task(Of Integer)

        Dim name As String = "P3-03 Supplier " & Guid.NewGuid().ToString("N").Substring(0, 12)

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

    ''' <summary>The one permanently-inactive supplier this suite needs. Reused across runs - Suppliers has no DELETE grant.</summary>
    Private Async Function EnsureInactiveSupplierAsync() As Task(Of Integer)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            Dim existing As Supplier = Await SupplierRepository.FindByNameAsync(connection, InactiveSupplierName)
            If existing IsNot Nothing Then
                Return existing.Id
            End If

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO Suppliers (Name, IsActive, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@name, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@name", InactiveSupplierName)
                Await command.ExecuteNonQueryAsync()
                Return CInt(command.LastInsertedId)
            End Using

        End Using

    End Function

    Private Async Function EnsureProductAsync(sku As String, isActive As Boolean) As Task(Of Integer)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            Using selectCommand As MySqlCommand = connection.CreateCommand()
                selectCommand.CommandText = "SELECT Id FROM Products WHERE Sku = @sku;"
                selectCommand.Parameters.AddWithValue("@sku", sku)
                Dim existing As Object = Await selectCommand.ExecuteScalarAsync()
                If existing IsNot Nothing Then
                    Return CInt(existing)
                End If
            End Using

            Using insertCommand As MySqlCommand = connection.CreateCommand()
                insertCommand.CommandText =
                    "INSERT INTO Products (Sku, Barcode, Name, Price, Cost, IsActive, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@sku, NULL, @name, 1.0000, 0.5000, @isActive, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                insertCommand.Parameters.AddWithValue("@sku", sku)
                insertCommand.Parameters.AddWithValue("@name", "P3-03 Fixture " & sku)
                insertCommand.Parameters.AddWithValue("@isActive", If(isActive, 1, 0))
                Await insertCommand.ExecuteNonQueryAsync()
                Return CInt(insertCommand.LastInsertedId)
            End Using

        End Using

    End Function

    Private Async Function CountOrdersForSupplierAsync(supplierId As Integer) As Task(Of Long)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM PurchaseOrders WHERE SupplierId = @supplierId;"
                command.Parameters.AddWithValue("@supplierId", supplierId)
                Return CLng(Await command.ExecuteScalarAsync())
            End Using
        End Using

    End Function

    Private Async Function CountRowsAsync(sql As String) As Task(Of Long)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = sql
                Return CLng(Await command.ExecuteScalarAsync())
            End Using
        End Using

    End Function

    Private Async Function EnsureFixtureUserAsync(username As String, roleName As String) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Dim existing As User = Await UserRepository.FindByUsernameAsync(connection, username)
            If existing IsNot Nothing Then
                Return
            End If
        End Using

        Dim migratorFactory As New ConnectionFactory(LoadMigratorOptions())
        Await CreateUserCommand.RunAsync(migratorFactory, username, FixturePassword, roleName)

    End Function

    Private Shared Function LoadMigratorOptions() As DatabaseOptions
        Dim migratorConfigPath As String =
            IO.Path.Combine(IO.Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName)
        Return DatabaseOptionsLoader.Load(migratorConfigPath)
    End Function

End Class
