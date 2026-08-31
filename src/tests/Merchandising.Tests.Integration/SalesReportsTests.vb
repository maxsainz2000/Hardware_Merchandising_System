' Merchandising.Tests.Integration.SalesReportsTests
'
' P6-02: spec section 14's first four reports (daily sales summary, sales by
' product, sales by cashier, payment-method summary), proven over real HTTP
' through MerchandisingApiFactory against the real pinned MariaDB.
'
' Role gating itself (Reports.View resolves to the roles PolicyRegistry says
' it does) is AuthorizationMatrixTests' job - this file proves the
' BEHAVIOUR, the same split PurchaseOrderHistoryTests uses:
'
'   box 1  all four reports reconcile against GET /api/v1/sales - the
'          detail endpoint this card adds (ADR-023 point 6) - through
'          ReportReconciliationHarness.AssertReconciles, two independent
'          code paths per ADR-023 point 3
'   box 2  cost basis reads captured SaleLines.Cost, never a current
'          Products.Cost re-read (docs/report-specification.md section 5)
'   box 4  every report states its applied date range and returns treatment
'
' RECONCILIATION IS ROBUST TO OTHER TESTS' CONCURRENT DATA. The daily
' summary and payment-method summary aggregate EVERY completed sale for the
' store-local day under test, which in a shared real database includes rows
' other integration tests wrote. That is not a defect in this design: both
' sides of every reconciliation below (the report's own total, and the sum
' computed from GET /api/v1/sales for the SAME date filter) read the SAME
' underlying rows, so pollution from another test appears identically on
' both sides and cancels out - proving exactly what ADR-023 requires, that
' the report agrees with the detail screen, never that either equals a
' known absolute number. Sales-by-product/by-cashier tests go further and
' isolate a single fixture product/cashier's own row, so even that shared-
' total reasoning is not load-bearing there.

Imports System.Collections.Generic
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Net.Http.Json
Imports System.Threading.Tasks
Imports Merchandising.Api.Sales
Imports Merchandising.Contracts.Auth
Imports Merchandising.Contracts.Reporting
Imports Merchandising.Contracts.Sales
Imports Merchandising.Domain
Imports Merchandising.Domain.Sales
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class SalesReportsTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const FixturePassword As String = "P6-02 Fixture Passw0rd!"
    Private Const AdminUsername As String = "p6_02_fixture_admin"
    Private Const CashierUsername As String = "p6_02_fixture_cashier"
    Private Const FixtureProductSkuPrefix As String = "p6_02_fixture_sku_"

    Private _factory As MerchandisingApiFactory
    Private _connectionFactory As ConnectionFactory
    Private _saleService As SaleService
    Private _cashierSessionService As CashierSessionService
    Private _cashierUserId As Integer
    Private _todayLocal As DateOnly
    Private _todayText As String

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _factory = New MerchandisingApiFactory()
        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        _saleService = New SaleService(_connectionFactory)
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

    ''' <summary>Daily sales summary reconciles against GET /api/v1/sales for the same store-local day.</summary>
    <TestMethod>
    Public Async Function DailySalesSummary_Reconciles_AgainstSalesDetailEndpoint() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync(price:=10.0000D, cost:=6.0000D)
        Await SeedStockDirectlyAsync(productId, 50.000D)
        Await CompleteSaleAsync(productId, 2.000D)
        Await CompleteSaleAsync(productId, 3.000D)

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, AdminUsername)

            Dim summary As DailySalesSummaryResponse =
                Await GetJsonAsync(Of DailySalesSummaryResponse)(client, token, $"/api/v1/reports/sales/daily-summary?date={_todayText}")

            Dim detail As IReadOnlyList(Of SaleResponse) =
                Await FetchAllSalesAsync(client, token, _todayText, _todayText, cashierUserId:=Nothing)

            Dim detailTotal As Decimal = detail.Sum(Function(s) s.Total)

            ReportReconciliationHarness.AssertReconciles(
                summary.CompletedSalesTotal, detailTotal, "Daily sales summary", "Sales detail sum",
                $"date={_todayText}")

            Assert.AreEqual(detail.Count, summary.CompletedSalesCount, "Transaction count must match the detail endpoint's own row count for the same day.")

        End Using

    End Function

    ''' <summary>Sales by product reconciles the fixture product's own row against the same product's lines from GET /api/v1/sales.</summary>
    <TestMethod>
    Public Async Function SalesByProduct_Reconciles_AgainstSalesDetailEndpoint() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync(price:=12.5000D, cost:=7.0000D)
        Await SeedStockDirectlyAsync(productId, 50.000D)
        Await CompleteSaleAsync(productId, 4.000D)
        Await CompleteSaleAsync(productId, 1.000D)

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, AdminUsername)

            Dim byProduct As SalesByProductResponse =
                Await GetJsonAsync(Of SalesByProductResponse)(
                    client, token, $"/api/v1/reports/sales/by-product?fromDate={_todayText}&toDate={_todayText}&pageSize=100")

            Dim item As SalesByProductItemResponse = byProduct.Items.Single(Function(i) i.ProductId = productId)

            Dim detail As IReadOnlyList(Of SaleResponse) =
                Await FetchAllSalesAsync(client, token, _todayText, _todayText, cashierUserId:=Nothing)

            Dim productLines = detail.SelectMany(Function(s) s.Lines).Where(Function(l) l.ProductId = productId).ToList()
            Dim detailQuantity As Decimal = productLines.Sum(Function(l) l.Quantity)
            Dim detailGrossValue As Decimal = productLines.Sum(Function(l) l.LineTotal)

            ReportReconciliationHarness.AssertReconciles(
                item.GrossSalesValue, detailGrossValue, "Sales by product (gross)", "Sales detail line sum",
                $"productId={productId}, date={_todayText}")

            Assert.AreEqual(detailQuantity, item.QuantitySold, "Quantity sold must match the detail endpoint's own line sum.")

        End Using

    End Function

    ''' <summary>Sales by cashier reconciles the fixture cashier's own row against GET /api/v1/sales filtered to that cashier.</summary>
    <TestMethod>
    Public Async Function SalesByCashier_Reconciles_AgainstSalesDetailEndpoint() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync(price:=8.0000D, cost:=3.0000D)
        Await SeedStockDirectlyAsync(productId, 50.000D)
        Await CompleteSaleAsync(productId, 1.000D)
        Await CompleteSaleAsync(productId, 2.000D)

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, AdminUsername)

            Dim byCashier As SalesByCashierResponse =
                Await GetJsonAsync(Of SalesByCashierResponse)(
                    client, token, $"/api/v1/reports/sales/by-cashier?fromDate={_todayText}&toDate={_todayText}&pageSize=100")

            Dim item As SalesByCashierItemResponse = byCashier.Items.Single(Function(i) i.CashierUserId = _cashierUserId)

            Dim detail As IReadOnlyList(Of SaleResponse) =
                Await FetchAllSalesAsync(client, token, _todayText, _todayText, cashierUserId:=_cashierUserId)

            Dim detailTotal As Decimal = detail.Sum(Function(s) s.Total)

            ReportReconciliationHarness.AssertReconciles(
                item.CompletedSalesTotal, detailTotal, "Sales by cashier", "Sales detail sum (cashier-filtered)",
                $"cashierUserId={_cashierUserId}, date={_todayText}")

            Assert.AreEqual(detail.Count, item.CompletedSalesCount)

        End Using

    End Function

    ''' <summary>Payment-method summary reconciles against SalePayments summed from GET /api/v1/sales for the same day.</summary>
    <TestMethod>
    Public Async Function PaymentMethodSummary_Reconciles_AgainstSalesDetailEndpoint() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync(price:=6.0000D, cost:=2.0000D)
        Await SeedStockDirectlyAsync(productId, 50.000D)
        Await CompleteSaleAsync(productId, 1.000D)

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, AdminUsername)

            Dim summary As PaymentMethodSummaryResponse =
                Await GetJsonAsync(Of PaymentMethodSummaryResponse)(
                    client, token, $"/api/v1/reports/sales/payment-methods?fromDate={_todayText}&toDate={_todayText}")

            Dim detail As IReadOnlyList(Of SaleResponse) =
                Await FetchAllSalesAsync(client, token, _todayText, _todayText, cashierUserId:=Nothing)

            Dim detailCashTotal As Decimal =
                detail.Where(Function(s) s.Payment IsNot Nothing AndAlso String.Equals(s.Payment.Method, "Cash", StringComparison.Ordinal)).
                    Sum(Function(s) s.Payment.Amount)

            Dim reportCashTotal As Decimal =
                summary.Totals.Single(Function(t) String.Equals(t.Method, "Cash", StringComparison.Ordinal)).Amount

            ReportReconciliationHarness.AssertReconciles(
                reportCashTotal, detailCashTotal, "Payment-method summary (Cash)", "Sales detail payment sum",
                $"date={_todayText}")

        End Using

    End Function

    ' ------------------------------------------------------------------ box 2: captured cost basis

    ''' <summary>A cost change AFTER the sale must never move an already-reported captured cost basis - docs/report-specification.md section 5.</summary>
    <TestMethod>
    Public Async Function SalesByProduct_CostChangedAfterSale_CapturedCostBasisUnmoved() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync(price:=20.0000D, cost:=9.0000D)
        Await SeedStockDirectlyAsync(productId, 10.000D)
        Await CompleteSaleAsync(productId, 2.000D)

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, AdminUsername)

            Dim before As SalesByProductResponse =
                Await GetJsonAsync(Of SalesByProductResponse)(
                    client, token, $"/api/v1/reports/sales/by-product?fromDate={_todayText}&toDate={_todayText}&pageSize=100")
            Dim itemBefore As SalesByProductItemResponse = before.Items.Single(Function(i) i.ProductId = productId)
            Assert.AreEqual(18.0000D, itemBefore.CapturedCostBasis, "2 * 9.0000, the cost captured at sale time.")

            Await ChangeCostDirectlyAsync(productId, newCost:=50.0000D)

            Dim after As SalesByProductResponse =
                Await GetJsonAsync(Of SalesByProductResponse)(
                    client, token, $"/api/v1/reports/sales/by-product?fromDate={_todayText}&toDate={_todayText}&pageSize=100")
            Dim itemAfter As SalesByProductItemResponse = after.Items.Single(Function(i) i.ProductId = productId)

            Assert.AreEqual(
                18.0000D, itemAfter.CapturedCostBasis,
                "A product cost change after the sale must never move the report's captured cost basis - a join to Products.Cost would have shown 100.0000 here.")

        End Using

    End Function

    ' ------------------------------------------------------------------ box 4: range and returns treatment

    ''' <summary>Every one of the four reports states its applied date range, time zone and returns treatment - spec section 14's universal requirement.</summary>
    <TestMethod>
    Public Async Function EveryReport_StatesDateRangeAndReturnsTreatment() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, AdminUsername)

            Dim daily As DailySalesSummaryResponse =
                Await GetJsonAsync(Of DailySalesSummaryResponse)(client, token, $"/api/v1/reports/sales/daily-summary?date={_todayText}")
            Assert.AreEqual(_todayText, daily.Range.FromDate)
            Assert.AreEqual(_todayText, daily.Range.ToDate)
            Assert.AreEqual("Asia/Manila", daily.Range.TimeZone)
            Assert.AreEqual("Included", daily.Range.ReturnsTreatment)

            Dim byProduct As SalesByProductResponse =
                Await GetJsonAsync(Of SalesByProductResponse)(client, token, $"/api/v1/reports/sales/by-product?fromDate={_todayText}&toDate={_todayText}")
            Assert.AreEqual(_todayText, byProduct.Range.FromDate)
            Assert.AreEqual("Included", byProduct.Range.ReturnsTreatment)

            Dim byCashier As SalesByCashierResponse =
                Await GetJsonAsync(Of SalesByCashierResponse)(client, token, $"/api/v1/reports/sales/by-cashier?fromDate={_todayText}&toDate={_todayText}")
            Assert.AreEqual(_todayText, byCashier.Range.FromDate)
            Assert.AreEqual("Included", byCashier.Range.ReturnsTreatment)

            Dim paymentMethods As PaymentMethodSummaryResponse =
                Await GetJsonAsync(Of PaymentMethodSummaryResponse)(client, token, $"/api/v1/reports/sales/payment-methods?fromDate={_todayText}&toDate={_todayText}")
            Assert.AreEqual(_todayText, paymentMethods.Range.FromDate)
            Assert.AreEqual("Excluded", paymentMethods.Range.ReturnsTreatment,
                            "Payment-method summary nets no returns - docs/report-specification.md section 4 row 4.")

        End Using

    End Function

    ''' <summary>G-24: the payment-method summary's own wording, asserted on the live response, not merely on the source file (PaymentWordingTests' job).</summary>
    <TestMethod>
    Public Async Function PaymentMethodSummary_Note_StatesRecordedNotAuthorised() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, AdminUsername)

            Dim summary As PaymentMethodSummaryResponse =
                Await GetJsonAsync(Of PaymentMethodSummaryResponse)(client, token, $"/api/v1/reports/sales/payment-methods?fromDate={_todayText}&toDate={_todayText}")

            Assert.AreEqual(PaymentMethodSummaryResponse.RecordedNotAuthorisedNote, summary.Note)
            StringAssert.Contains(summary.Note, "not authorised")
            StringAssert.Contains(summary.Note, "recorded")

        End Using

    End Function

    ' ------------------------------------------------------------------ validation

    <TestMethod>
    Public Async Function GetDailySalesSummary_MissingDate_Refused400() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, AdminUsername)

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Get, "/api/v1/reports/sales/daily-summary", token, Nothing)

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)

            End Using

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
                    "VALUES (@sku, NULL, 'P6-02 Fixture Product', @price, @cost, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@sku", sku)
                command.Parameters.AddWithValue("@price", price)
                command.Parameters.AddWithValue("@cost", cost)
                Await command.ExecuteNonQueryAsync()
                Return CInt(command.LastInsertedId)
            End Using
        End Using

    End Function

    ''' <summary>Seeds a known StockBalances quantity plus a matching StockMovements row - SaleServiceTests' identical fixture shape, needed so LedgerReconciliationTests' whole-suite check stays satisfied.</summary>
    Private Async Function SeedStockDirectlyAsync(productId As Integer, quantity As Decimal) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Dim transaction As MySqlTransaction = Await connection.BeginTransactionAsync()

            Dim stockResult = Await StockRepository.IncrementAsync(connection, transaction, productId, quantity)

            Using movementCommand As MySqlCommand = connection.CreateCommand()
                movementCommand.Transaction = transaction
                movementCommand.CommandText =
                    "INSERT INTO StockMovements (ProductId, Delta, QuantityBefore, QuantityAfter, Reason, ActorUserId, CorrelationId, CreatedAtUtc) " &
                    "VALUES (@productId, @delta, @before, @after, 'P6-02 test fixture stock seed', @actorUserId, @correlationId, UTC_TIMESTAMP(6));"
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

    Private Async Function ChangeCostDirectlyAsync(productId As Integer, newCost As Decimal) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "UPDATE Products SET Cost = @cost, UpdatedAtUtc = UTC_TIMESTAMP(6) WHERE Id = @productId;"
                command.Parameters.AddWithValue("@cost", newCost)
                command.Parameters.AddWithValue("@productId", productId)
                Await command.ExecuteNonQueryAsync()
            End Using
        End Using

    End Function

    ''' <summary>Pages through GET /api/v1/sales until every row for the filter is fetched - the daily/payment-method totals can exceed one page on a shared database.</summary>
    Private Async Function FetchAllSalesAsync(
        client As HttpClient, token As String, fromDate As String, toDate As String, cashierUserId As Integer?) As Task(Of IReadOnlyList(Of SaleResponse))

        Dim items As New List(Of SaleResponse)
        Dim page As Integer = 1

        Do

            Dim cashierFilter As String = If(cashierUserId.HasValue, $"&cashierUserId={cashierUserId.Value}", String.Empty)
            Dim result As SaleSearchResponse =
                Await GetJsonAsync(Of SaleSearchResponse)(
                    client, token, $"/api/v1/sales?fromDate={fromDate}&toDate={toDate}{cashierFilter}&page={page}&pageSize=100")

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
