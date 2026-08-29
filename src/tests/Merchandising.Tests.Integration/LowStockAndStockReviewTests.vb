' Merchandising.Tests.Integration.LowStockAndStockReviewTests
'
' P4-11: GET /api/v1/inventory/stock, GET /api/v1/inventory/low-stock, and
' GET /api/v1/inventory/stock/movements - proven over real HTTP through
' MerchandisingApiFactory against the real pinned MariaDB.
'
' Role gating itself is AuthorizationMatrixTests' job (StockRead_/
' LowStockReview_/StockReviewMovements_MatrixMatchesPolicyRegistry) - this
' file proves the BEHAVIOUR:
'
'   box 1  the movement ledger for a product sums to its current balance -
'          the P4-01 invariant, now exposed through the API
'   box 2  low-stock threshold is per product (ReorderLevel), and only
'          ACTIVE products at or below it are listed
'   box 3  pagination/max page size/sort match P3-03's contract
'   box 4  date boundaries on the movement ledger are store-local
'          (Asia/Manila), same as P3-06
'
' EVERY BALANCE CHANGE GOES THROUGH A REAL COMMAND (adjustment or decrement),
' never a raw SQL UPDATE of StockBalances - P4-01's ledger reconciliation
' scans every product in the database after this whole assembly runs
' (LedgerReconciliationTests' AssemblyCleanup), so a fixture balance with no
' matching StockMovements row would fail that check for every OTHER test
' file too. A freshly-created product's StockBalances starts at 0.000 with
' no movement row (0 sums to 0), the same convention every other fixture in
' this assembly already uses.

Imports System.Collections.Generic
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Net.Http.Json
Imports System.Threading.Tasks
Imports Merchandising.Contracts.Auth
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Inventory
Imports Merchandising.Domain
Imports Merchandising.Domain.Configuration
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class LowStockAndStockReviewTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const FixturePassword As String = "P4-11 Fixture Passw0rd!"
    Private Const InventoryFixtureUsername As String = "p4_11_fixture_inventory"

    Private _factory As MerchandisingApiFactory
    Private _connectionFactory As ConnectionFactory
    Private _skuSuffix As String

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _factory = New MerchandisingApiFactory()
        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        _skuSuffix = Guid.NewGuid().ToString("N").Substring(0, 10)
        Await EnsureFixtureUserAsync(InventoryFixtureUsername, "InventoryClerk")

        ' AuthorizationMatrixTests fixes this SAME shared SystemSettings row to
        ' a small value (1.000) for its own approve-cell test and never resets
        ' it - SystemSettings is not test-isolated. Fixed here to something
        ' comfortably above every variance this file ever sends, so a fixture
        ' adjustment auto-applies regardless of assembly test order (the
        ' card's own Done-when box 1 wording, "asserted rather than assumed",
        ' applied to this file's own fixtures too).
        Await SetAdjustmentThresholdDirectlyAsync(100.000D)

    End Function

    <TestCleanup>
    Public Sub TearDown()
        _factory?.Dispose()
    End Sub

    ' ------------------------------------------------------------- box 1

    ''' <summary>Summing every returned movement's Delta for a product equals CurrentBalance - P4-01's invariant, exposed here rather than only asserted internally.</summary>
    <TestMethod>
    Public Async Function GetStockMovements_SumsToCurrentBalance() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, InventoryFixtureUsername)
            Dim productId As Integer = Await CreateProductDirectlyAsync("p4_11_sum_" & _skuSuffix, reorderLevel:=0D, isActive:=True)

            Await ApplyAdjustmentAsync(client, token, productId, 5.000D)
            Await ApplyAdjustmentAsync(client, token, productId, 3.000D)
            Await DecrementAsync(client, token, productId, 2.000D)

            Dim movements As StockMovementSearchResponse =
                Await GetMovementsAsync(client, token, $"?productId={productId}&pageSize=100&sort=createdAt:asc")

            Assert.HasCount(3, movements.Items)
            Assert.AreEqual(productId, movements.ProductId)
            Assert.AreEqual(6.000D, movements.CurrentBalance, "5.000 + 3.000 - 2.000")

            Dim summedDelta As Decimal = movements.Items.Sum(Function(m) m.Delta)
            Assert.AreEqual(movements.CurrentBalance, summedDelta, "The ledger for this product must sum to its current balance.")

            Dim directBalance As Decimal = Await ReadBalanceDirectlyAsync(productId)
            Assert.AreEqual(directBalance, movements.CurrentBalance, "The API's CurrentBalance must match StockBalances.Quantity directly.")

        End Using

    End Function

    <TestMethod>
    Public Async Function GetStockMovements_MissingProductId_Refused400() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, InventoryFixtureUsername)

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Get, "/api/v1/inventory/stock/movements", token, Nothing)

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("VALIDATION_FAILED", body.ErrorCode)
                Assert.IsTrue(body.Errors.ContainsKey("productId"))

            End Using

        End Using

    End Function

    <TestMethod>
    Public Async Function GetStockMovements_UnknownSort_Refused400() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, InventoryFixtureUsername)
            Dim productId As Integer = Await CreateProductDirectlyAsync("p4_11_badsort_" & _skuSuffix, reorderLevel:=0D, isActive:=True)

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Get, $"/api/v1/inventory/stock/movements?productId={productId}&sort=quantity:asc", token, Nothing)

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("VALIDATION_FAILED", body.ErrorCode)
                Assert.IsTrue(body.Errors.ContainsKey("sort"))

            End Using

        End Using

    End Function

    ' ------------------------------------------------------------- box 2

    ''' <summary>A product below its own reorder level is listed; one above its own reorder level, and an active-looking product that is actually deactivated, are not - proving the threshold is per product, never a global constant.</summary>
    <TestMethod>
    Public Async Function GetLowStock_IncludesAtOrBelowReorderLevel_ExcludesAboveAndInactive() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, InventoryFixtureUsername)

            Dim belowSku As String = "p4_11_below_" & _skuSuffix
            Dim aboveSku As String = "p4_11_above_" & _skuSuffix
            Dim inactiveSku As String = "p4_11_inactive_" & _skuSuffix

            ' ReorderLevel 10.000, raised to 5.000 - at or below its own reorder level.
            Dim belowProductId As Integer = Await CreateProductDirectlyAsync(belowSku, reorderLevel:=10.000D, isActive:=True)
            Await ApplyAdjustmentAsync(client, token, belowProductId, 5.000D)

            ' ReorderLevel 2.000, raised to the SAME 5.000 - above its own reorder level.
            Dim aboveProductId As Integer = Await CreateProductDirectlyAsync(aboveSku, reorderLevel:=2.000D, isActive:=True)
            Await ApplyAdjustmentAsync(client, token, aboveProductId, 5.000D)

            ' ReorderLevel 10.000, balance stays 0 (well below) - but deactivated.
            Dim inactiveProductId As Integer = Await CreateProductDirectlyAsync(inactiveSku, reorderLevel:=10.000D, isActive:=False)

            Assert.IsTrue(Await LowStockContainsSkuAsync(client, token, belowSku),
                          "A product at or below its own reorder level must be listed.")
            Assert.IsFalse(Await LowStockContainsSkuAsync(client, token, aboveSku),
                           "A product above its own reorder level must not be listed.")
            Assert.IsFalse(Await LowStockContainsSkuAsync(client, token, inactiveSku),
                           "A deactivated product must not be listed, however low its balance.")

        End Using

    End Function

    ' ------------------------------------------------------------- box 3

    <TestMethod>
    Public Async Function GetStock_PaginationMatchesP3_03Contract() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, InventoryFixtureUsername)

            ' Guarantees at least four products exist, independent of whatever
            ' earlier phases' fixtures already left behind.
            For i As Integer = 1 To 4
                Await CreateProductDirectlyAsync($"p4_11_page_{i}_{_skuSuffix}", reorderLevel:=0D, isActive:=True)
            Next

            Dim defaulted As StockSearchResponse = Await GetStockAsync(client, token, "")
            Assert.AreEqual(25, defaulted.PageSize, "Page size defaults to 25, same as P3-03's list.")
            Assert.AreEqual(100, defaulted.MaxPageSize)

            Dim oversized As StockSearchResponse = Await GetStockAsync(client, token, "?pageSize=5000")
            Assert.AreEqual(100, oversized.PageSize, "An oversized page size is clamped, not honoured.")

            Dim pageOne As StockSearchResponse = Await GetStockAsync(client, token, "?sort=productName:asc&page=1&pageSize=2")
            Dim pageTwo As StockSearchResponse = Await GetStockAsync(client, token, "?sort=productName:asc&page=2&pageSize=2")

            Assert.HasCount(2, pageOne.Items)
            Assert.HasCount(2, pageTwo.Items)

            Dim allIds As New List(Of Integer)
            allIds.AddRange(pageOne.Items.Select(Function(i) i.ProductId))
            allIds.AddRange(pageTwo.Items.Select(Function(i) i.ProductId))
            Assert.HasCount(4, New HashSet(Of Integer)(allIds), "Pages must not overlap.")

        End Using

    End Function

    <TestMethod>
    Public Async Function GetStock_UnknownSort_Refused400() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, InventoryFixtureUsername)

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Get, "/api/v1/inventory/stock?sort=bogus", token, Nothing)

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("VALIDATION_FAILED", body.ErrorCode)
                Assert.IsTrue(body.Errors.ContainsKey("sort"))

            End Using

        End Using

    End Function

    ' ------------------------------------------------------------- box 4

    ''' <summary>A movement recorded "today" (store-local) is included by a fromDate/toDate of today, and excluded once the window moves entirely to another day.</summary>
    <TestMethod>
    Public Async Function GetStockMovements_DateRangeFilter_StoreLocalBoundaries() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, InventoryFixtureUsername)
            Dim productId As Integer = Await CreateProductDirectlyAsync("p4_11_daterange_" & _skuSuffix, reorderLevel:=0D, isActive:=True)

            Await ApplyAdjustmentAsync(client, token, productId, 1.000D)

            Dim todayLocal As DateOnly = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, StoreTimeZone.Zone))
            Dim tomorrowLocal As DateOnly = todayLocal.AddDays(1)
            Dim yesterdayLocal As DateOnly = todayLocal.AddDays(-1)

            Dim todayText As String = todayLocal.ToString("yyyy-MM-dd")
            Dim tomorrowText As String = tomorrowLocal.ToString("yyyy-MM-dd")
            Dim yesterdayText As String = yesterdayLocal.ToString("yyyy-MM-dd")

            Dim includedByToday As StockMovementSearchResponse =
                Await GetMovementsAsync(client, token, $"?productId={productId}&fromDate={todayText}&toDate={todayText}")
            Assert.HasCount(1, includedByToday.Items, "A movement recorded today must be included by a today-to-today window.")
            Assert.AreEqual(todayText, includedByToday.FromDate)
            Assert.AreEqual(todayText, includedByToday.ToDate)
            Assert.AreEqual("Asia/Manila", includedByToday.TimeZone)

            Dim excludedByTomorrow As StockMovementSearchResponse =
                Await GetMovementsAsync(client, token, $"?productId={productId}&fromDate={tomorrowText}")
            Assert.IsEmpty(excludedByTomorrow.Items, "A window starting tomorrow must exclude a movement recorded today.")

            Dim excludedByYesterday As StockMovementSearchResponse =
                Await GetMovementsAsync(client, token, $"?productId={productId}&toDate={yesterdayText}")
            Assert.IsEmpty(excludedByYesterday.Items, "A window ending yesterday must exclude a movement recorded today.")

        End Using

    End Function

    <TestMethod>
    Public Async Function GetStockMovements_NoDateFilters_EchoesUnboundedRange() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, InventoryFixtureUsername)
            Dim productId As Integer = Await CreateProductDirectlyAsync("p4_11_unbounded_" & _skuSuffix, reorderLevel:=0D, isActive:=True)

            Dim movements As StockMovementSearchResponse =
                Await GetMovementsAsync(client, token, $"?productId={productId}")

            Assert.IsNull(movements.FromDate)
            Assert.IsNull(movements.ToDate)
            Assert.AreEqual("Asia/Manila", movements.TimeZone)

        End Using

    End Function

    ' --------------------------------------------------------------- helpers

    Private Async Function ApplyAdjustmentAsync(client As HttpClient, token As String, productId As Integer, variance As Decimal) As Task

        Dim body As New RequestAdjustmentRequest With {
            .ProductId = productId,
            .QuantityVariance = variance,
            .Reason = "P4-11 fixture adjustment",
            .IdempotencyKey = Guid.NewGuid().ToString("d")
        }

        Using response As HttpResponseMessage = Await SendAsync(client, HttpMethod.Post, "/api/v1/adjustments", token, body)

            Assert.AreEqual(
                HttpStatusCode.Created, response.StatusCode,
                "Fixture adjustment must succeed. Body: " & Await response.Content.ReadAsStringAsync())

            Dim created As AdjustmentResponse = Await response.Content.ReadFromJsonAsync(Of AdjustmentResponse)()
            Assert.AreEqual(
                "Applied", created.Status,
                "Fixture variance must stay below the default adjustment threshold and auto-apply.")

        End Using

    End Function

    Private Async Function DecrementAsync(client As HttpClient, token As String, productId As Integer, quantity As Decimal) As Task

        Dim body As New StockDecrementRequest With {
            .ProductId = productId,
            .Quantity = quantity,
            .Reason = "P4-11 fixture decrement",
            .IdempotencyKey = Guid.NewGuid().ToString("d")
        }

        Using response As HttpResponseMessage = Await SendAsync(client, HttpMethod.Post, "/api/v1/inventory/stock/decrement", token, body)
            Assert.AreEqual(
                HttpStatusCode.OK, response.StatusCode,
                "Fixture decrement must succeed. Body: " & Await response.Content.ReadAsStringAsync())
        End Using

    End Function

    Private Async Function GetStockAsync(client As HttpClient, token As String, queryString As String) As Task(Of StockSearchResponse)

        Using response As HttpResponseMessage =
            Await SendAsync(client, HttpMethod.Get, "/api/v1/inventory/stock" & queryString, token, Nothing)

            Assert.AreEqual(
                HttpStatusCode.OK, response.StatusCode,
                $"GET /api/v1/inventory/stock{queryString} failed. Body: " & Await response.Content.ReadAsStringAsync())
            Return Await response.Content.ReadFromJsonAsync(Of StockSearchResponse)()

        End Using

    End Function

    Private Async Function GetLowStockAsync(client As HttpClient, token As String, queryString As String) As Task(Of LowStockSearchResponse)

        Using response As HttpResponseMessage =
            Await SendAsync(client, HttpMethod.Get, "/api/v1/inventory/low-stock" & queryString, token, Nothing)

            Assert.AreEqual(
                HttpStatusCode.OK, response.StatusCode,
                $"GET /api/v1/inventory/low-stock{queryString} failed. Body: " & Await response.Content.ReadAsStringAsync())
            Return Await response.Content.ReadFromJsonAsync(Of LowStockSearchResponse)()

        End Using

    End Function

    ''' <summary>Walks pages of the low-stock list, up to a generous cap, looking for one SKU - other integration test files leave permanent fixture products behind (no DELETE grant), so this list is never scoped to only this file's own fixtures.</summary>
    Private Async Function LowStockContainsSkuAsync(client As HttpClient, token As String, sku As String) As Task(Of Boolean)

        Const pageSize As Integer = 100
        Const maxPagesToScan As Integer = 20

        For page As Integer = 1 To maxPagesToScan

            Dim result As LowStockSearchResponse = Await GetLowStockAsync(client, token, $"?page={page}&pageSize={pageSize}")

            If result.Items.Any(Function(i) String.Equals(i.Sku, sku, StringComparison.Ordinal)) Then
                Return True
            End If

            If result.Items.Count < pageSize Then
                Return False
            End If

        Next

        Return False

    End Function

    Private Async Function GetMovementsAsync(client As HttpClient, token As String, queryString As String) As Task(Of StockMovementSearchResponse)

        Using response As HttpResponseMessage =
            Await SendAsync(client, HttpMethod.Get, "/api/v1/inventory/stock/movements" & queryString, token, Nothing)

            Assert.AreEqual(
                HttpStatusCode.OK, response.StatusCode,
                $"GET /api/v1/inventory/stock/movements{queryString} failed. Body: " & Await response.Content.ReadAsStringAsync())
            Return Await response.Content.ReadFromJsonAsync(Of StockMovementSearchResponse)()

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

    ' --------------------------------------------------------------- fixtures

    ''' <summary>
    ''' A fresh, permanent product (Products has no DELETE grant for merch_api,
    ''' the same reason every other integration suite here never deletes its
    ''' own fixtures) with StockBalances seeded at 0.000 and NO movement row -
    ''' 0 sums to nothing, so this never breaks P4-01's whole-database
    ''' reconciliation. Any later balance change must go through a real
    ''' command (ApplyAdjustmentAsync/DecrementAsync), never a raw UPDATE.
    ''' </summary>
    Private Async Function CreateProductDirectlyAsync(sku As String, reorderLevel As Decimal, isActive As Boolean) As Task(Of Integer)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            Dim productId As Integer

            Using insertCommand As MySqlCommand = connection.CreateCommand()
                insertCommand.CommandText =
                    "INSERT INTO Products (Sku, Barcode, Name, Price, Cost, ReorderLevel, IsActive, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@sku, NULL, @name, 1.0000, 0.5000, @reorderLevel, @isActive, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                insertCommand.Parameters.AddWithValue("@sku", sku)
                insertCommand.Parameters.AddWithValue("@name", "P4-11 Fixture " & sku)
                insertCommand.Parameters.AddWithValue("@reorderLevel", reorderLevel)
                insertCommand.Parameters.AddWithValue("@isActive", isActive)
                Await insertCommand.ExecuteNonQueryAsync()
                productId = CInt(insertCommand.LastInsertedId)
            End Using

            Using balanceCommand As MySqlCommand = connection.CreateCommand()
                balanceCommand.CommandText =
                    "INSERT INTO StockBalances (ProductId, Quantity, RowVersion, UpdatedAtUtc) " &
                    "VALUES (@productId, 0.000, 0, UTC_TIMESTAMP(6));"
                balanceCommand.Parameters.AddWithValue("@productId", productId)
                Await balanceCommand.ExecuteNonQueryAsync()
            End Using

            Return productId

        End Using

    End Function

    Private Async Function ReadBalanceDirectlyAsync(productId As Integer) As Task(Of Decimal)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT Quantity FROM StockBalances WHERE ProductId = @productId;"
                command.Parameters.AddWithValue("@productId", productId)
                Return CDec(Await command.ExecuteScalarAsync())
            End Using
        End Using

    End Function

    ''' <summary>Same mechanism as AuthorizationMatrixTests' own SetAdjustmentThresholdDirectlyAsync - SystemSettingsRepository.UpsertAsync directly, so this file never depends on SystemSettingRegistry's own default or on what an earlier test class left behind.</summary>
    Private Async Function SetAdjustmentThresholdDirectlyAsync(threshold As Decimal) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using transaction As MySqlTransaction = Await connection.BeginTransactionAsync()

                Dim actorUserId As Integer
                Using actorCommand As MySqlCommand = connection.CreateCommand()
                    actorCommand.Transaction = transaction
                    actorCommand.CommandText = "SELECT Id FROM Users WHERE Username = @username;"
                    actorCommand.Parameters.AddWithValue("@username", InventoryFixtureUsername)
                    actorUserId = CInt(Await actorCommand.ExecuteScalarAsync())
                End Using

                Await SystemSettingsRepository.UpsertAsync(
                    connection, transaction, SystemSettingRegistry.Keys.AdjustmentApprovalThreshold,
                    threshold.ToString("0.000", Globalization.CultureInfo.InvariantCulture), actorUserId)

                Await transaction.CommitAsync()

            End Using
        End Using

    End Function

    Private Async Function EnsureFixtureUserAsync(username As String, roleName As String) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Dim existing As Merchandising.Domain.Entities.User = Await UserRepository.FindByUsernameAsync(connection, username)
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
