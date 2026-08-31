' Merchandising.Tests.Integration.ReturnsAndPerformanceReportsTests
'
' P6-03: spec section 14 rows 5 and 12 (returns and cancellations, product
' performance summary), proven over real HTTP through MerchandisingApiFactory
' against the real pinned MariaDB - the same split SalesReportsTests (P6-02)
' uses:
'
'   box 1  both reports reconcile against GET /api/v1/sales/returns - the
'          detail endpoint this card adds (docs/report-specification.md
'          section 7), through ReportReconciliationHarness.AssertReconciles,
'          two independent code paths, the same ADR-023 point 3 requirement
'          P6-02 already proved for the first four reports
'   box    the margin estimate is labelled informational wherever it appears
'   box    the returns report distinguishes a return that restocked from one
'          that did not (RestocksItem, P5-11)
'   box    product performance's current stock position reads StockBalances
'          live while its sales figures respect the selected period - "the
'          mixed-temporality trap"
'
' RECONCILIATION IS ROBUST TO OTHER TESTS' CONCURRENT DATA - the same
' reasoning SalesReportsTests' own header gives: both sides of every
' assertion below read the SAME underlying rows for the SAME filter, and
' every reconciliation here additionally isolates its own fixture product's
' row, so shared-database pollution cancels out identically on both sides.

Imports System.Collections.Generic
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Net.Http.Json
Imports System.Threading.Tasks
Imports Merchandising.Api.Sales
Imports Merchandising.Contracts.Auth
Imports Merchandising.Contracts.Inventory
Imports Merchandising.Contracts.Reporting
Imports Merchandising.Contracts.Sales
Imports Merchandising.Domain
Imports Merchandising.Domain.Sales
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class ReturnsAndPerformanceReportsTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const FixturePassword As String = "P6-03 Fixture Passw0rd!"
    Private Const AdminUsername As String = "p6_03_fixture_admin"
    Private Const CashierUsername As String = "p6_03_fixture_cashier"
    Private Const FixtureProductSkuPrefix As String = "p6_03_fixture_sku_"

    Private _factory As MerchandisingApiFactory
    Private _connectionFactory As ConnectionFactory
    Private _saleService As SaleService
    Private _salesReturnService As SalesReturnService
    Private _cashierSessionService As CashierSessionService
    Private _cashierUserId As Integer
    Private _todayLocal As DateOnly
    Private _todayText As String

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _factory = New MerchandisingApiFactory()
        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        _saleService = New SaleService(_connectionFactory)
        _salesReturnService = New SalesReturnService(_connectionFactory)
        _cashierSessionService = New CashierSessionService(_connectionFactory)

        Await EnsureFixtureUserAsync(AdminUsername, "Admin")
        _cashierUserId = Await EnsureFixtureUserAsync(CashierUsername, "Cashier")
        Await CloseOpenSessionDirectlyAsync(_cashierUserId)

        _todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, StoreTimeZone.Zone))
        _todayText = _todayLocal.ToString("yyyy-MM-dd")

    End Function

    <TestCleanup>
    Public Sub TearDown()
        _factory?.Dispose()
    End Sub

    ' ------------------------------------------------------------------ box 1: reconciliation

    ''' <summary>Returns and cancellations reconciles the fixture product's own returned quantity against GET /api/v1/sales/returns.</summary>
    <TestMethod>
    Public Async Function ReturnsAndCancellations_Reconciles_AgainstSalesReturnsDetailEndpoint() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync(price:=10.0000D, cost:=6.0000D)
        Await SeedStockDirectlyAsync(productId, 50.000D)
        Dim sale As SaleResponse = Await CompleteSaleAsync(productId, 5.000D)

        Dim returnLine As New CreateSalesReturnLineRequest With {
            .SaleLineId = sale.Lines(0).Id, .QuantityReturned = 2.000D, .RestocksItem = True}
        Await CreateReturnAsync(sale.Id, {returnLine})

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, AdminUsername)

            Dim report As IReadOnlyList(Of ReturnsAndCancellationsItemResponse) =
                Await FetchAllReturnsAndCancellationsAsync(client, token, _todayText, _todayText)

            Dim reportQuantity As Decimal =
                report.Where(Function(i) i.ProductId = productId).Sum(Function(i) i.QuantityReturned)

            Dim detail As IReadOnlyList(Of SalesReturnResponse) =
                Await FetchAllReturnsAsync(client, token, _todayText, _todayText)

            Dim detailQuantity As Decimal =
                detail.SelectMany(Function(r) r.Lines).Where(Function(l) l.ProductId = productId).Sum(Function(l) l.QuantityReturned)

            ReportReconciliationHarness.AssertReconciles(
                reportQuantity, detailQuantity, "Returns and cancellations", "Sales returns detail line sum",
                $"productId={productId}, date={_todayText}")

        End Using

    End Function

    ''' <summary>Product performance reconciles the fixture product's net quantity and net sales value against GET /api/v1/sales and GET /api/v1/sales/returns for the same window.</summary>
    <TestMethod>
    Public Async Function ProductPerformance_Reconciles_AgainstSalesAndReturnsDetailEndpoints() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync(price:=15.0000D, cost:=9.0000D)
        Await SeedStockDirectlyAsync(productId, 50.000D)
        Dim sale As SaleResponse = Await CompleteSaleAsync(productId, 6.000D)

        Dim returnLine As New CreateSalesReturnLineRequest With {
            .SaleLineId = sale.Lines(0).Id, .QuantityReturned = 2.000D, .RestocksItem = True}
        Await CreateReturnAsync(sale.Id, {returnLine})

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, AdminUsername)

            Dim report As IReadOnlyList(Of ProductPerformanceItemResponse) =
                Await FetchAllProductPerformanceAsync(client, token, _todayText, _todayText)

            Dim item As ProductPerformanceItemResponse = report.Single(Function(i) i.ProductId = productId)

            Dim soldDetail As IReadOnlyList(Of SaleResponse) =
                Await FetchAllSalesAsync(client, token, _todayText, _todayText)
            Dim soldLines = soldDetail.SelectMany(Function(s) s.Lines).Where(Function(l) l.ProductId = productId).ToList()
            Dim soldQuantity As Decimal = soldLines.Sum(Function(l) l.Quantity)
            Dim soldValue As Decimal = soldLines.Sum(Function(l) l.LineTotal)

            Dim returnDetail As IReadOnlyList(Of SalesReturnResponse) =
                Await FetchAllReturnsAsync(client, token, _todayText, _todayText)
            Dim returnedLines =
                returnDetail.Where(Function(r) String.Equals(r.Status, "Completed", StringComparison.Ordinal)).
                    SelectMany(Function(r) r.Lines).Where(Function(l) l.ProductId = productId).ToList()
            Dim returnedQuantity As Decimal = returnedLines.Sum(Function(l) l.QuantityReturned)
            Dim returnedValue As Decimal = returnedLines.Sum(Function(l) l.QuantityReturned * l.UnitPrice)

            ReportReconciliationHarness.AssertReconciles(
                item.NetQuantity, soldQuantity - returnedQuantity, "Product performance (net quantity)",
                "Sales/Sales-returns detail net", $"productId={productId}, date={_todayText}")

            ReportReconciliationHarness.AssertReconciles(
                item.NetSalesValue, soldValue - returnedValue, "Product performance (net sales value)",
                "Sales/Sales-returns detail net", $"productId={productId}, date={_todayText}")

        End Using

    End Function

    ' ------------------------------------------------------------------ margin label

    ''' <summary>Spec section 14: the margin estimate must be labelled informational wherever it appears - asserted on the live response, not merely on the source file.</summary>
    <TestMethod>
    Public Async Function ProductPerformance_MarginEstimate_IsLabelledInformational() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync(price:=10.0000D, cost:=4.0000D)
        Await SeedStockDirectlyAsync(productId, 50.000D)
        Await CompleteSaleAsync(productId, 1.000D)

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, AdminUsername)

            Dim report As IReadOnlyList(Of ProductPerformanceItemResponse) =
                Await FetchAllProductPerformanceAsync(client, token, _todayText, _todayText)

            Dim item As ProductPerformanceItemResponse = report.Single(Function(i) i.ProductId = productId)

            Assert.AreEqual(ProductPerformanceItemResponse.MarginEstimateLabel, item.MarginEstimateLabelText)
            StringAssert.Contains(item.MarginEstimateLabelText, "Informational")
            Assert.AreEqual(6.0000D, item.MarginEstimate, "1 * (10.0000 - 4.0000), rounded once through DecimalScaleGuard.RoundMoney.")

        End Using

    End Function

    ' ------------------------------------------------------------------ restocked vs not

    ''' <summary>A return line eligible to re-enter stock and one that is not must both be distinguishable in the report, not collapsed into one shape.</summary>
    <TestMethod>
    Public Async Function ReturnsAndCancellations_DistinguishesRestockingFromNonRestocking() As Task

        Dim restockedProductId As Integer = Await CreateFixtureProductAsync(price:=8.0000D, cost:=3.0000D)
        Dim nonRestockedProductId As Integer = Await CreateFixtureProductAsync(price:=8.0000D, cost:=3.0000D)
        Await SeedStockDirectlyAsync(restockedProductId, 20.000D)
        Await SeedStockDirectlyAsync(nonRestockedProductId, 20.000D)

        Dim sale As SaleResponse = Await CompleteTwoLineSaleAsync(restockedProductId, nonRestockedProductId)

        Dim restockedLine As New CreateSalesReturnLineRequest With {
            .SaleLineId = sale.Lines(0).Id, .QuantityReturned = 1.000D, .RestocksItem = True}
        Dim nonRestockedLine As New CreateSalesReturnLineRequest With {
            .SaleLineId = sale.Lines(1).Id, .QuantityReturned = 1.000D, .RestocksItem = False}

        Dim created As SalesReturnResponse = Await CreateReturnAsync(sale.Id, {restockedLine, nonRestockedLine})

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, AdminUsername)

            Dim report As IReadOnlyList(Of ReturnsAndCancellationsItemResponse) =
                Await FetchAllReturnsAndCancellationsAsync(client, token, _todayText, _todayText)

            Dim rowsForThisReturn = report.Where(Function(i) i.SalesReturnId = created.Id).ToList()

            Dim restockedRow = rowsForThisReturn.Single(Function(i) i.ProductId = restockedProductId)
            Dim nonRestockedRow = rowsForThisReturn.Single(Function(i) i.ProductId = nonRestockedProductId)

            Assert.IsTrue(restockedRow.RestocksItem, "The line marked eligible to re-enter stock must report RestocksItem = True.")
            Assert.IsFalse(nonRestockedRow.RestocksItem, "The line recorded operationally with no stock effect must report RestocksItem = False.")

        End Using

    End Function

    ' ------------------------------------------------------------------ mixed temporality

    ''' <summary>Current stock position reads StockBalances LIVE, never derived from the report's own period arithmetic - docs/report-specification.md, "the mixed-temporality trap."</summary>
    <TestMethod>
    Public Async Function ProductPerformance_CurrentStockReadsLiveWhileSalesRespectPeriod() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync(price:=5.0000D, cost:=2.0000D)
        Await SeedStockDirectlyAsync(productId, 20.000D)
        Await CompleteSaleAsync(productId, 5.000D)

        ' A stock change AFTER the sale, unrelated to this report's own
        ' period arithmetic (a receiving, simulated directly) - if the
        ' report ever computed "current stock" from period sales instead of
        ' reading StockBalances live, this later movement would never show.
        Await SeedStockDirectlyAsync(productId, 3.000D)

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, AdminUsername)

            Dim report As IReadOnlyList(Of ProductPerformanceItemResponse) =
                Await FetchAllProductPerformanceAsync(client, token, _todayText, _todayText)

            Dim item As ProductPerformanceItemResponse = report.Single(Function(i) i.ProductId = productId)

            Dim stock As IReadOnlyList(Of StockBalanceResponse) = Await FetchAllStockAsync(client, token)
            Dim liveQuantity As Decimal = stock.Single(Function(s) s.ProductId = productId).Quantity

            Assert.AreEqual(18.0000D, liveQuantity, "20 seeded - 5 sold + 3 seeded again, the live balance.")
            ReportReconciliationHarness.AssertReconciles(
                item.CurrentStockQuantity, liveQuantity, "Product performance (current stock)", "Stock detail",
                $"productId={productId}")
            Assert.AreNotEqual(
                15.0000D, item.CurrentStockQuantity,
                "15 is what naive period-only arithmetic (20 seeded - 5 sold) would show - the report must read the live balance, not derive one.")

        End Using

    End Function

    ' ------------------------------------------------------------------ range and returns treatment

    ''' <summary>Both new reports state their applied date range, time zone and returns treatment - spec section 14's universal requirement.</summary>
    <TestMethod>
    Public Async Function BothReports_StateDateRangeAndReturnsTreatment() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, AdminUsername)

            Dim returns As ReturnsAndCancellationsResponse =
                Await GetJsonAsync(Of ReturnsAndCancellationsResponse)(
                    client, token, $"/api/v1/reports/sales/returns?fromDate={_todayText}&toDate={_todayText}")
            Assert.AreEqual(_todayText, returns.Range.FromDate)
            Assert.AreEqual("Asia/Manila", returns.Range.TimeZone)
            Assert.AreEqual("Included", returns.Range.ReturnsTreatment)

            Dim performance As ProductPerformanceResponse =
                Await GetJsonAsync(Of ProductPerformanceResponse)(
                    client, token, $"/api/v1/reports/sales/product-performance?fromDate={_todayText}&toDate={_todayText}")
            Assert.AreEqual(_todayText, performance.Range.FromDate)
            Assert.AreEqual("Included", performance.Range.ReturnsTreatment)

        End Using

    End Function

    ' --------------------------------------------------------------- helpers

    Private Async Function CompleteSaleAsync(productId As Integer, quantity As Decimal) As Task(Of SaleResponse)

        Await EnsureOpenSessionAsync()

        Dim lines As New List(Of CreateSaleLineRequest) From {
            New CreateSaleLineRequest With {.ProductId = productId, .Quantity = quantity}
        }

        Dim outcome As SaleOutcome =
            Await _saleService.CompleteAsync(
                lines, PaymentMethod.Cash, 100000.0000D, _cashierUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(SaleOutcomeKind.Created, outcome.Kind, "Fixture sale could not be completed.")
        Return outcome.Response

    End Function

    Private Async Function CompleteTwoLineSaleAsync(productAId As Integer, productBId As Integer) As Task(Of SaleResponse)

        Await EnsureOpenSessionAsync()

        Dim lines As New List(Of CreateSaleLineRequest) From {
            New CreateSaleLineRequest With {.ProductId = productAId, .Quantity = 1.000D},
            New CreateSaleLineRequest With {.ProductId = productBId, .Quantity = 1.000D}
        }

        Dim outcome As SaleOutcome =
            Await _saleService.CompleteAsync(
                lines, PaymentMethod.Cash, 100000.0000D, _cashierUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(SaleOutcomeKind.Created, outcome.Kind, "Fixture sale could not be completed.")
        Return outcome.Response

    End Function

    Private Async Function CreateReturnAsync(
        saleId As Integer, lines As IReadOnlyList(Of CreateSalesReturnLineRequest)) As Task(Of SalesReturnResponse)

        Dim outcome As SalesReturnOutcome =
            Await _salesReturnService.RecordAsync(
                saleId, lines, "P6-03 fixture return", PaymentMethod.Cash.ToString(), _cashierUserId,
                Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(SalesReturnOutcomeKind.Created, outcome.Kind, "Fixture return could not be recorded.")
        Return outcome.Response

    End Function

    Private Async Function EnsureOpenSessionAsync() As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT 1 FROM CashierSessions WHERE OpenedByUserId = @userId AND Status = 'Open';"
                command.Parameters.AddWithValue("@userId", _cashierUserId)
                If Await command.ExecuteScalarAsync() IsNot Nothing Then
                    Return
                End If
            End Using
        End Using

        Dim outcome As CashierSessionOutcome =
            Await _cashierSessionService.OpenAsync(_cashierUserId, 0D, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(CashierSessionOutcomeKind.Created, outcome.Kind, "Fixture session could not be opened.")

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

    Private Async Function CreateFixtureProductAsync(Optional price As Decimal = 1.0000D, Optional cost As Decimal = 0.5000D) As Task(Of Integer)

        Dim sku As String = FixtureProductSkuPrefix & Guid.NewGuid().ToString("N").Substring(0, 16)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO Products (Sku, Barcode, Name, Price, Cost, IsActive, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@sku, NULL, 'P6-03 Fixture Product', @price, @cost, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@sku", sku)
                command.Parameters.AddWithValue("@price", price)
                command.Parameters.AddWithValue("@cost", cost)
                Await command.ExecuteNonQueryAsync()
                Return CInt(command.LastInsertedId)
            End Using
        End Using

    End Function

    ''' <summary>Seeds an additional StockBalances quantity plus a matching StockMovements row - SalesReportsTests' identical fixture shape (P6-02), needed so LedgerReconciliationTests' whole-suite check stays satisfied. Callable more than once per product - the mixed-temporality test relies on that.</summary>
    Private Async Function SeedStockDirectlyAsync(productId As Integer, quantity As Decimal) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Dim transaction As MySqlTransaction = Await connection.BeginTransactionAsync()

            Dim stockResult = Await StockRepository.IncrementAsync(connection, transaction, productId, quantity)

            Using movementCommand As MySqlCommand = connection.CreateCommand()
                movementCommand.Transaction = transaction
                movementCommand.CommandText =
                    "INSERT INTO StockMovements (ProductId, Delta, QuantityBefore, QuantityAfter, Reason, ActorUserId, CorrelationId, CreatedAtUtc) " &
                    "VALUES (@productId, @delta, @before, @after, 'P6-03 test fixture stock seed', @actorUserId, @correlationId, UTC_TIMESTAMP(6));"
                movementCommand.Parameters.AddWithValue("@productId", productId)
                movementCommand.Parameters.AddWithValue("@delta", quantity)
                movementCommand.Parameters.AddWithValue("@before", stockResult.QuantityBefore)
                movementCommand.Parameters.AddWithValue("@after", stockResult.QuantityAfter)
                movementCommand.Parameters.AddWithValue("@actorUserId", _cashierUserId)
                movementCommand.Parameters.AddWithValue("@correlationId", Guid.NewGuid().ToString())
                Await movementCommand.ExecuteNonQueryAsync()
            End Using

            Await transaction.CommitAsync()

        End Using

    End Function

    ''' <summary>Pages through GET /api/v1/sales until every row for the filter is fetched.</summary>
    Private Async Function FetchAllSalesAsync(
        client As HttpClient, token As String, fromDate As String, toDate As String) As Task(Of IReadOnlyList(Of SaleResponse))

        Dim items As New List(Of SaleResponse)
        Dim page As Integer = 1

        Do

            Dim result As SaleSearchResponse =
                Await GetJsonAsync(Of SaleSearchResponse)(
                    client, token, $"/api/v1/sales?fromDate={fromDate}&toDate={toDate}&page={page}&pageSize=100")

            items.AddRange(result.Items)

            If items.Count >= result.TotalCount OrElse result.Items.Count = 0 Then
                Exit Do
            End If

            page += 1

        Loop

        Return items

    End Function

    ''' <summary>Pages through GET /api/v1/sales/returns until every row for the filter is fetched.</summary>
    Private Async Function FetchAllReturnsAsync(
        client As HttpClient, token As String, fromDate As String, toDate As String) As Task(Of IReadOnlyList(Of SalesReturnResponse))

        Dim items As New List(Of SalesReturnResponse)
        Dim page As Integer = 1

        Do

            Dim result As SalesReturnSearchResponse =
                Await GetJsonAsync(Of SalesReturnSearchResponse)(
                    client, token, $"/api/v1/sales/returns?fromDate={fromDate}&toDate={toDate}&page={page}&pageSize=100")

            items.AddRange(result.Items)

            If items.Count >= result.TotalCount OrElse result.Items.Count = 0 Then
                Exit Do
            End If

            page += 1

        Loop

        Return items

    End Function

    ''' <summary>
    ''' Pages through GET /api/v1/reports/sales/returns until every row for
    ''' the filter is fetched - the shared database already carries well
    ''' over 100 return lines for "today" across this session's repeated
    ''' test runs, so a single pageSize=100 fetch is not guaranteed to
    ''' contain this test's own fixture rows.
    ''' </summary>
    Private Async Function FetchAllReturnsAndCancellationsAsync(
        client As HttpClient, token As String, fromDate As String, toDate As String) As Task(Of IReadOnlyList(Of ReturnsAndCancellationsItemResponse))

        Dim items As New List(Of ReturnsAndCancellationsItemResponse)
        Dim page As Integer = 1

        Do

            Dim result As ReturnsAndCancellationsResponse =
                Await GetJsonAsync(Of ReturnsAndCancellationsResponse)(
                    client, token, $"/api/v1/reports/sales/returns?fromDate={fromDate}&toDate={toDate}&page={page}&pageSize=100")

            items.AddRange(result.Items)

            If items.Count >= result.TotalCount OrElse result.Items.Count = 0 Then
                Exit Do
            End If

            page += 1

        Loop

        Return items

    End Function

    ''' <summary>
    ''' Pages through GET /api/v1/reports/sales/product-performance until
    ''' every row for the filter is fetched - the same reason
    ''' SalesReportsTests.FetchAllByProductAsync exists (P6-02's own by-
    ''' product report): every fixture product here gets a brand new random
    ''' SKU, so the "sold today" set is genuinely unbounded across this
    ''' session's repeated test runs, and a single pageSize=100 fetch is not
    ''' guaranteed to contain this test's own fixture row.
    ''' </summary>
    Private Async Function FetchAllProductPerformanceAsync(
        client As HttpClient, token As String, fromDate As String, toDate As String) As Task(Of IReadOnlyList(Of ProductPerformanceItemResponse))

        Dim items As New List(Of ProductPerformanceItemResponse)
        Dim page As Integer = 1

        Do

            Dim result As ProductPerformanceResponse =
                Await GetJsonAsync(Of ProductPerformanceResponse)(
                    client, token, $"/api/v1/reports/sales/product-performance?fromDate={fromDate}&toDate={toDate}&page={page}&pageSize=100")

            items.AddRange(result.Items)

            If items.Count >= result.TotalCount OrElse result.Items.Count = 0 Then
                Exit Do
            End If

            page += 1

        Loop

        Return items

    End Function

    ''' <summary>Pages through GET /api/v1/inventory/stock until every row is fetched - the fixture product's alphabetical position is not guaranteed to fall on page 1 of a shared, ever-growing Products table.</summary>
    Private Async Function FetchAllStockAsync(client As HttpClient, token As String) As Task(Of IReadOnlyList(Of StockBalanceResponse))

        Dim items As New List(Of StockBalanceResponse)
        Dim page As Integer = 1

        Do

            Dim result As StockSearchResponse =
                Await GetJsonAsync(Of StockSearchResponse)(
                    client, token, $"/api/v1/inventory/stock?includeInactive=true&page={page}&pageSize=100")

            items.AddRange(result.Items)

            If items.Count >= result.TotalCount OrElse result.Items.Count = 0 Then
                Exit Do
            End If

            page += 1

        Loop

        Return items

    End Function

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
