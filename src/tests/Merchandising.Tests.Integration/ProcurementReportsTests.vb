' Merchandising.Tests.Integration.ProcurementReportsTests
'
' P6-04: spec section 14 rows 6-7 (purchase-order history, goods-receiving
' history), proven over real HTTP through MerchandisingApiFactory against the
' real pinned MariaDB.
'
'   box 1  the purchase-order history report reconciles, order-by-order,
'          against GET /api/v1/purchase-orders/history (existing, P3-06) -
'          docs/report-specification.md section 7's named detail endpoint
'          for both of this card's reports
'   box 2  OutstandingQuantity is computed from committed ReceiptLines rows,
'          never PurchaseOrderLines.ReceivedQuantity - proven by receiving
'          against the SAME order across TWO SEPARATE receipts and showing
'          this report's own accumulation matches P3-06's accumulator
'          column, i.e. "P4-06's accumulation rule, read back"
'   box 3  the goods-receiving history report reconciles its own summed
'          ReceivedQuantity (grouped by purchase order) against the
'          purchase-order history report's ReceivedQuantity for the same
'          order - a THIRD independently-written query agreeing with the
'          other two
'   box 4  the purchase-order status machine is consumed, not reimplemented -
'          a Cancelled order appears in the report, labelled, never excluded
'          or hard-coded around
'   box 5  both reports state their date range and ReturnsTreatment =
'          Excluded (docs/report-specification.md section 4 rows 6-7)
'
' RECEIPTS AND ORDERS ARE BUILT THROUGH THE PRODUCTION SERVICE LAYER
' (PurchaseOrderService, ReceivingService) rather than raw SQL or HTTP -
' the same fixture style ReceivingTests.vb already establishes for proving
' behaviour against the real atomic receiving path, not a shortcut around it.
'
' TWO ADMIN FIXTURES for the same ADR-017 section 6 self-approval reason
' ReceivingTests.vb/PurchaseOrderApprovalTests.vb need them: approving an
' order requires an actor who did not request it.
'
' RECONCILIATION IS ROBUST TO OTHER TESTS' CONCURRENT DATA - every
' assertion below isolates its own fixture order (by Id) or fixture product
' (by ProductId) out of a possibly much larger shared-database result set,
' paged in full rather than assumed to fit on page one (the same "page
' until TotalCount is reached" fix P6-03's own evidence file documents was
' needed against this same shared database).

Imports System.Collections.Generic
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Net.Http.Json
Imports System.Threading.Tasks
Imports Merchandising.Api.Procurement
Imports Merchandising.Api.Receiving
Imports Merchandising.Contracts.Auth
Imports Merchandising.Contracts.Procurement
Imports Merchandising.Contracts.Receiving
Imports Merchandising.Contracts.Reporting
Imports Merchandising.Domain
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class ProcurementReportsTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const FixturePassword As String = "P6-04 Fixture Passw0rd!"
    Private Const AdminAUsername As String = "p6_04_fixture_admin_a"
    Private Const AdminBUsername As String = "p6_04_fixture_admin_b"
    Private Const InventoryUsername As String = "p6_04_fixture_inventory"
    Private Const FixtureProductSkuPrefix As String = "p6_04_fixture_sku_"

    Private _factory As MerchandisingApiFactory
    Private _connectionFactory As ConnectionFactory
    Private _purchaseOrderService As PurchaseOrderService
    Private _receivingService As ReceivingService
    Private _adminAUserId As Integer
    Private _adminBUserId As Integer
    Private _inventoryUserId As Integer
    Private _todayText As String

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _factory = New MerchandisingApiFactory()
        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        _purchaseOrderService = New PurchaseOrderService(_connectionFactory)
        _receivingService = New ReceivingService(_connectionFactory)

        _adminAUserId = Await EnsureFixtureUserAsync(AdminAUsername, "Admin")
        _adminBUserId = Await EnsureFixtureUserAsync(AdminBUsername, "Admin")
        _inventoryUserId = Await EnsureFixtureUserAsync(InventoryUsername, "InventoryClerk")

        Dim todayLocal As DateOnly = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, StoreTimeZone.Zone))
        _todayText = todayLocal.ToString("yyyy-MM-dd")

    End Function

    <TestCleanup>
    Public Sub TearDown()
        _factory?.Dispose()
    End Sub

    ' -------------------------------------------------- box 1 and box 2

    ''' <summary>
    ''' Two lines, two separate receipts (one against each line, one line
    ''' receiving twice) - the purchase-order history report's Ordered/
    ''' Received/Outstanding figures, computed here from committed
    ''' ReceiptLines rows, must agree exactly with P3-06's accumulator-column
    ''' figures for the SAME order under the SAME date filter.
    ''' </summary>
    <TestMethod>
    Public Async Function PurchaseOrderHistoryReport_Reconciles_AgainstPurchaseOrdersHistoryEndpoint_AcrossMultipleReceipts() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim supplierId As Integer = Await CreateActiveSupplierAsync()

        Dim order As PurchaseOrderResponse = Await CreateApprovedOrderAsync(
            supplierId,
            {(productId, 10.000D, 2.5000D), (productId, 5.000D, 3.0000D)})

        ' Line 0 (ordered 10.000 @ 2.5000): received across TWO separate
        ' receipts - 4.000 then 3.000, summing to 7.000. Line 1 (ordered
        ' 5.000 @ 3.0000): received once, 2.000. The receipt's own Cost is
        ' deliberately different from the order's PurchaseCost on every line
        ' - ReceivedValue must price at the ORDER's captured cost, never the
        ' receipt's.
        Await ReceiveAsync(order.Id, {(order.Lines(0).Id, 4.000D, 2.6000D), (order.Lines(1).Id, 2.000D, 3.2000D)})
        Await ReceiveAsync(order.Id, {(order.Lines(0).Id, 3.000D, 2.7000D)})

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, AdminAUsername)

            Dim report As IReadOnlyList(Of PurchaseOrderHistoryReportItemResponse) =
                Await FetchAllPurchaseOrderHistoryReportAsync(client, token, _todayText, _todayText)
            Dim reportItem As PurchaseOrderHistoryReportItemResponse = report.Single(Function(i) i.Id = order.Id)

            Dim detail As IReadOnlyList(Of PurchaseOrderHistoryItemResponse) =
                Await FetchAllPurchaseOrdersHistoryAsync(client, token, _todayText, _todayText)
            Dim detailItem As PurchaseOrderHistoryItemResponse = detail.Single(Function(i) i.Id = order.Id)

            ReportReconciliationHarness.AssertReconciles(
                reportItem.OrderedQuantity, detailItem.OrderedQuantity,
                "Purchase-order history report", "Purchase-orders history endpoint (ordered)", $"purchaseOrderId={order.Id}")
            ReportReconciliationHarness.AssertReconciles(
                reportItem.OrderedValue, detailItem.OrderedValue,
                "Purchase-order history report", "Purchase-orders history endpoint (ordered value)", $"purchaseOrderId={order.Id}")
            ReportReconciliationHarness.AssertReconciles(
                reportItem.ReceivedQuantity, detailItem.ReceivedQuantity,
                "Purchase-order history report", "Purchase-orders history endpoint (received)", $"purchaseOrderId={order.Id}")
            ReportReconciliationHarness.AssertReconciles(
                reportItem.ReceivedValue, detailItem.ReceivedValue,
                "Purchase-order history report", "Purchase-orders history endpoint (received value)", $"purchaseOrderId={order.Id}")
            ReportReconciliationHarness.AssertReconciles(
                reportItem.OutstandingQuantity, detailItem.OutstandingQuantity,
                "Purchase-order history report", "Purchase-orders history endpoint (outstanding)", $"purchaseOrderId={order.Id}")

            ' Not merely equal to each other - genuinely the right numbers,
            ' so a defect shared by both queries could not hide behind
            ' agreement alone.
            Assert.AreEqual(15.000D, reportItem.OrderedQuantity, "10.000 + 5.000")
            Assert.AreEqual(40.0000D, reportItem.OrderedValue, "10*2.5000 + 5*3.0000 = 25.0000 + 15.0000")
            Assert.AreEqual(9.000D, reportItem.ReceivedQuantity, "4.000 + 3.000 (line 0, two receipts) + 2.000 (line 1)")
            Assert.AreEqual(23.5000D, reportItem.ReceivedValue, "7*2.5000 + 2*3.0000 = 17.5000 + 6.0000 - the ORDER's cost, not the receipt's")
            Assert.AreEqual(6.000D, reportItem.OutstandingQuantity, "15.000 - 9.000")

        End Using

    End Function

    ' -------------------------------------------------------------- box 3

    ''' <summary>
    ''' Goods-receiving history's own rows, grouped by purchase order and
    ''' summed, reconcile against the purchase-order history report's
    ''' ReceivedQuantity for that same order - a third independently-written
    ''' query. Fetched UNBOUNDED on both sides deliberately: this report
    ''' filters on Receipts.ReceivedAtUtc, the purchase-order report filters
    ''' on PurchaseOrders.CreatedAtUtc - two different date dimensions that a
    ''' shared bounded filter would not guarantee cover the same rows
    ''' (ReportRepository.GetGoodsReceivingHistoryAsync's own header).
    ''' </summary>
    <TestMethod>
    Public Async Function GoodsReceivingHistory_Reconciles_ReceivedQuantityAgainstPurchaseOrderHistoryReport() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim supplierId As Integer = Await CreateActiveSupplierAsync()

        Dim order As PurchaseOrderResponse = Await CreateApprovedOrderAsync(
            supplierId, {(productId, 10.000D, 2.5000D)})

        Dim firstReferenceNumber As String = NewReferenceNumber()
        Dim secondReferenceNumber As String = NewReferenceNumber()

        Await ReceiveAsync(order.Id, {(order.Lines(0).Id, 4.000D, 2.6000D)}, firstReferenceNumber)
        Await ReceiveAsync(order.Id, {(order.Lines(0).Id, 3.000D, 2.7000D)}, secondReferenceNumber)

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, AdminAUsername)

            Dim goodsReceiving As IReadOnlyList(Of GoodsReceivingHistoryItemResponse) =
                Await FetchAllGoodsReceivingHistoryAsync(client, token, Nothing, Nothing)
            Dim rowsForOrder As IReadOnlyList(Of GoodsReceivingHistoryItemResponse) =
                goodsReceiving.Where(Function(r) r.PurchaseOrderId = order.Id).ToList()

            Assert.HasCount(2, rowsForOrder, "One row per receipt against this order.")

            Dim ordersReport As IReadOnlyList(Of PurchaseOrderHistoryReportItemResponse) =
                Await FetchAllPurchaseOrderHistoryReportAsync(client, token, Nothing, Nothing)
            Dim orderItem As PurchaseOrderHistoryReportItemResponse = ordersReport.Single(Function(i) i.Id = order.Id)

            ReportReconciliationHarness.AssertReconciles(
                rowsForOrder.Sum(Function(r) r.ReceivedQuantity), orderItem.ReceivedQuantity,
                "Goods-receiving history (summed by order)", "Purchase-order history report ReceivedQuantity",
                $"purchaseOrderId={order.Id}")

            ' Content correctness, not merely the summed total - spec section
            ' 14's named columns: receipt, supplier, date, product, ordered
            ' quantity, received quantity, responsible user.
            Dim firstRow As GoodsReceivingHistoryItemResponse =
                rowsForOrder.Single(Function(r) r.ReferenceNumber = firstReferenceNumber)
            Assert.AreEqual(order.Id, firstRow.PurchaseOrderId)
            Assert.AreEqual(order.OrderNumber, firstRow.OrderNumber)
            Assert.AreEqual(supplierId, firstRow.SupplierId)
            Assert.AreEqual(productId, firstRow.ProductId)
            Assert.AreEqual(10.000D, firstRow.OrderedQuantity, "The order LINE's own ordered quantity, not any order-level total.")
            Assert.AreEqual(4.000D, firstRow.ReceivedQuantity)
            Assert.AreEqual(_inventoryUserId, firstRow.ReceivedByUserId)
            Assert.AreEqual(InventoryUsername, firstRow.ReceivedByUsername)

            Dim secondRow As GoodsReceivingHistoryItemResponse =
                rowsForOrder.Single(Function(r) r.ReferenceNumber = secondReferenceNumber)
            Assert.AreEqual(3.000D, secondRow.ReceivedQuantity)

        End Using

    End Function

    ' -------------------------------------------------------------- box 4

    ''' <summary>A cancelled order stays in the report, Status = "Cancelled" - the status machine is consumed (ADR-020), never re-decided or filtered around here.</summary>
    <TestMethod>
    Public Async Function PurchaseOrderHistoryReport_CancelledOrder_AppearsLabelledNotExcluded() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync()
        Dim supplierId As Integer = Await CreateActiveSupplierAsync()

        Dim createOutcome As PurchaseOrderCreationOutcome =
            Await _purchaseOrderService.CreateAsync(
                supplierId,
                New List(Of Merchandising.Contracts.Procurement.CreatePurchaseOrderLineRequest) From {
                    New Merchandising.Contracts.Procurement.CreatePurchaseOrderLineRequest With {
                        .ProductId = productId, .OrderedQuantity = 1.000D, .PurchaseCost = 1.0000D}},
                _adminAUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(PurchaseOrderCreationOutcomeKind.Created, createOutcome.Kind)

        Dim orderId As Integer = createOutcome.Response.Id

        Dim submitOutcome As PurchaseOrderTransitionOutcome =
            Await _purchaseOrderService.SubmitAsync(orderId, _adminAUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Success, submitOutcome.Kind)

        Dim cancelOutcome As PurchaseOrderTransitionOutcome =
            Await _purchaseOrderService.CancelAsync(orderId, _adminAUserId, "P6-04 fixture cancellation", Guid.NewGuid().ToString())
        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Success, cancelOutcome.Kind)

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, AdminAUsername)

            Dim report As IReadOnlyList(Of PurchaseOrderHistoryReportItemResponse) =
                Await FetchAllPurchaseOrderHistoryReportAsync(client, token, _todayText, _todayText)
            Dim item As PurchaseOrderHistoryReportItemResponse = report.Single(Function(i) i.Id = orderId)

            Assert.AreEqual("Cancelled", item.Status)

        End Using

    End Function

    ' -------------------------------------------------------------- box 5

    ''' <summary>Both reports echo an unbounded range as null/null and state ReturnsTreatment = Excluded (docs/report-specification.md section 4 rows 6-7) - neither report carries a sales-returns dimension.</summary>
    <TestMethod>
    Public Async Function BothReports_StateUnboundedRangeAndReturnsTreatmentExcluded() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, AdminAUsername)

            Dim orders As PurchaseOrderHistoryReportResponse =
                Await GetJsonAsync(Of PurchaseOrderHistoryReportResponse)(client, token, "/api/v1/reports/procurement/purchase-orders?pageSize=1")

            Assert.IsNull(orders.Range.FromDate)
            Assert.IsNull(orders.Range.ToDate)
            Assert.AreEqual("Asia/Manila", orders.Range.TimeZone)
            Assert.AreEqual("Excluded", orders.Range.ReturnsTreatment)

            Dim receiving As GoodsReceivingHistoryResponse =
                Await GetJsonAsync(Of GoodsReceivingHistoryResponse)(client, token, "/api/v1/reports/procurement/goods-receiving?pageSize=1")

            Assert.IsNull(receiving.Range.FromDate)
            Assert.IsNull(receiving.Range.ToDate)
            Assert.AreEqual("Asia/Manila", receiving.Range.TimeZone)
            Assert.AreEqual("Excluded", receiving.Range.ReturnsTreatment)

        End Using

    End Function

    ' --------------------------------------------------------------- fixtures

    Private Async Function CreateApprovedOrderAsync(
        supplierId As Integer,
        lineSpecs As IEnumerable(Of (ProductId As Integer, OrderedQuantity As Decimal, PurchaseCost As Decimal))) As Task(Of PurchaseOrderResponse)

        Dim lines As New List(Of Merchandising.Contracts.Procurement.CreatePurchaseOrderLineRequest)
        For Each spec In lineSpecs
            lines.Add(New Merchandising.Contracts.Procurement.CreatePurchaseOrderLineRequest With {
                .ProductId = spec.ProductId, .OrderedQuantity = spec.OrderedQuantity, .PurchaseCost = spec.PurchaseCost})
        Next

        Dim createOutcome As PurchaseOrderCreationOutcome =
            Await _purchaseOrderService.CreateAsync(
                supplierId, lines, _adminAUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(PurchaseOrderCreationOutcomeKind.Created, createOutcome.Kind, "Fixture order creation must succeed.")

        Dim orderId As Integer = createOutcome.Response.Id

        Dim submitOutcome As PurchaseOrderTransitionOutcome =
            Await _purchaseOrderService.SubmitAsync(orderId, _adminAUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Success, submitOutcome.Kind, "Fixture submit must succeed.")

        Dim approveOutcome As PurchaseOrderTransitionOutcome =
            Await _purchaseOrderService.ApproveAsync(orderId, _adminBUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(PurchaseOrderTransitionOutcomeKind.Success, approveOutcome.Kind, "Fixture approve must succeed.")

        Return approveOutcome.Response

    End Function

    Private Async Function ReceiveAsync(
        purchaseOrderId As Integer,
        lineSpecs As IEnumerable(Of (PurchaseOrderLineId As Integer, QuantityReceived As Decimal, Cost As Decimal)),
        Optional referenceNumber As String = Nothing) As Task(Of ReceiptResponse)

        Dim lines As New List(Of ReceiveGoodsLineRequest)
        For Each spec In lineSpecs
            lines.Add(New ReceiveGoodsLineRequest With {
                .PurchaseOrderLineId = spec.PurchaseOrderLineId, .QuantityReceived = spec.QuantityReceived, .Cost = spec.Cost})
        Next

        Dim outcome As ReceivingOutcome =
            Await _receivingService.ReceiveAsync(
                purchaseOrderId, If(referenceNumber, NewReferenceNumber()), lines,
                _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(ReceivingOutcomeKind.Created, outcome.Kind, "Fixture receiving must succeed.")
        Return outcome.Response

    End Function

    Private Shared Function NewReferenceNumber() As String
        Return "P6-04-GRN-" & Guid.NewGuid().ToString("N").Substring(0, 20)
    End Function

    Private Async Function CreateActiveSupplierAsync() As Task(Of Integer)

        Dim name As String = "P6-04 Supplier " & Guid.NewGuid().ToString("N").Substring(0, 12)

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
                    "VALUES (@sku, NULL, 'P6-04 Fixture Product', 9.0000, 4.0000, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@sku", sku)
                Await command.ExecuteNonQueryAsync()
                Return CInt(command.LastInsertedId)
            End Using
        End Using

    End Function

    ' --------------------------------------------------------------- paging

    Private Async Function FetchAllPurchaseOrderHistoryReportAsync(
        client As HttpClient, token As String, fromDate As String, toDate As String) As Task(Of IReadOnlyList(Of PurchaseOrderHistoryReportItemResponse))

        Dim query As String = DateQuery(fromDate, toDate)
        Dim items As New List(Of PurchaseOrderHistoryReportItemResponse)
        Dim page As Integer = 1

        Do
            Dim result As PurchaseOrderHistoryReportResponse =
                Await GetJsonAsync(Of PurchaseOrderHistoryReportResponse)(
                    client, token, $"/api/v1/reports/procurement/purchase-orders?{query}page={page}&pageSize=100")

            items.AddRange(result.Items)

            If items.Count >= result.TotalCount OrElse result.Items.Count = 0 Then
                Exit Do
            End If

            page += 1
        Loop

        Return items

    End Function

    Private Async Function FetchAllGoodsReceivingHistoryAsync(
        client As HttpClient, token As String, fromDate As String, toDate As String) As Task(Of IReadOnlyList(Of GoodsReceivingHistoryItemResponse))

        Dim query As String = DateQuery(fromDate, toDate)
        Dim items As New List(Of GoodsReceivingHistoryItemResponse)
        Dim page As Integer = 1

        Do
            Dim result As GoodsReceivingHistoryResponse =
                Await GetJsonAsync(Of GoodsReceivingHistoryResponse)(
                    client, token, $"/api/v1/reports/procurement/goods-receiving?{query}page={page}&pageSize=100")

            items.AddRange(result.Items)

            If items.Count >= result.TotalCount OrElse result.Items.Count = 0 Then
                Exit Do
            End If

            page += 1
        Loop

        Return items

    End Function

    ''' <summary>P3-06's own existing detail endpoint - GET /api/v1/purchase-orders/history.</summary>
    Private Async Function FetchAllPurchaseOrdersHistoryAsync(
        client As HttpClient, token As String, fromDate As String, toDate As String) As Task(Of IReadOnlyList(Of PurchaseOrderHistoryItemResponse))

        Dim query As String = DateQuery(fromDate, toDate)
        Dim items As New List(Of PurchaseOrderHistoryItemResponse)
        Dim page As Integer = 1

        Do
            Dim result As PurchaseOrderHistoryResponse =
                Await GetJsonAsync(Of PurchaseOrderHistoryResponse)(
                    client, token, $"/api/v1/purchase-orders/history?{query}page={page}&pageSize=100")

            items.AddRange(result.Items)

            If items.Count >= result.TotalCount OrElse result.Items.Count = 0 Then
                Exit Do
            End If

            page += 1
        Loop

        Return items

    End Function

    Private Shared Function DateQuery(fromDate As String, toDate As String) As String

        Dim parts As New List(Of String)
        If Not String.IsNullOrEmpty(fromDate) Then
            parts.Add($"fromDate={fromDate}")
        End If
        If Not String.IsNullOrEmpty(toDate) Then
            parts.Add($"toDate={toDate}")
        End If

        Return If(parts.Count = 0, String.Empty, String.Join("&", parts) & "&")

    End Function

    ' --------------------------------------------------------------- http

    Private Async Function GetJsonAsync(Of T)(client As HttpClient, token As String, path As String) As Task(Of T)

        Using response As HttpResponseMessage =
            Await SendAsync(client, HttpMethod.Get, path, token, Nothing)

            Assert.AreEqual(
                HttpStatusCode.OK, response.StatusCode,
                $"GET {path} failed. Body: " & Await response.Content.ReadAsStringAsync())
            Return Await response.Content.ReadFromJsonAsync(Of T)()

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
