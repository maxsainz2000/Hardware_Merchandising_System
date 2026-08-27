' Merchandising.Tests.Integration.PurchaseOrderHistoryTests
'
' P3-06: GET /api/v1/purchase-orders/history, spec section 14's Purchase-order
' history report shape - proven over real HTTP through MerchandisingApiFactory
' against the real pinned MariaDB.
'
' Role gating itself (PurchaseOrders.Track resolves to the roles
' PolicyRegistry says it does, on BOTH the plain list and this route) is
' AuthorizationMatrixTests' job - this file proves the BEHAVIOUR:
'
'   box 1  ordered/received/outstanding quantity and value are computed
'          server-side, from PurchaseOrderLines, never trusted from a client
'   box 2  a cancelled order appears in history, labelled, not excluded
'   box 3  date-range filters are store-local (Asia/Manila) and the applied
'          range is echoed back in the response
'   box 4  pagination/max page size match P3-03's contract

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
Imports Merchandising.Domain
Imports Merchandising.Domain.Entities
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class PurchaseOrderHistoryTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const FixturePassword As String = "P3-06 Fixture Passw0rd!"
    Private Const ProcurementFixtureUsername As String = "p3_06_fixture_procurement"
    Private Const FixtureProductSku As String = "p3_06_fixture_sku"

    Private _factory As MerchandisingApiFactory
    Private _connectionFactory As ConnectionFactory
    Private _productId As Integer

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _factory = New MerchandisingApiFactory()
        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        Await EnsureFixtureUserAsync(ProcurementFixtureUsername, "ProcurementOfficer")
        _productId = Await EnsureFixtureProductAsync()

    End Function

    <TestCleanup>
    Public Sub TearDown()
        _factory?.Dispose()
    End Sub

    ' ------------------------------------------------------------- box 1

    ''' <summary>Ordered quantity/value are the server's own SUM over the order's lines, not anything the client sent as a total.</summary>
    <TestMethod>
    Public Async Function GetPurchaseOrderHistory_OrderedQuantityAndValue_ComputedServerSide() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)
            Dim supplierId As Integer = Await CreateActiveSupplierAsync()

            Dim created As PurchaseOrderResponse = Await CreateOrderAsync(
                client, token, supplierId,
                New CreatePurchaseOrderLineRequest With {.ProductId = _productId, .OrderedQuantity = 3.000D, .PurchaseCost = 10.0000D},
                New CreatePurchaseOrderLineRequest With {.ProductId = _productId, .OrderedQuantity = 5.000D, .PurchaseCost = 4.5000D})

            Dim history As PurchaseOrderHistoryResponse =
                Await GetHistoryAsync(client, token, $"?supplierId={supplierId}")

            Assert.HasCount(1, history.Items)
            Dim item As PurchaseOrderHistoryItemResponse = history.Items(0)

            Assert.AreEqual(created.Id, item.Id)
            Assert.AreEqual(8.000D, item.OrderedQuantity, "3.000 + 5.000")
            Assert.AreEqual(52.5000D, item.OrderedValue, "3*10.0000 + 5*4.5000 = 30.0000 + 22.5000")
            Assert.AreEqual(0D, item.ReceivedQuantity, "Nothing received yet - Phase 4 owns receiving.")
            Assert.AreEqual(0D, item.ReceivedValue)
            Assert.AreEqual(8.000D, item.OutstandingQuantity, "Ordered minus received: 8.000 - 0.")

        End Using

    End Function

    ''' <summary>ReceivedQuantity/Value and OutstandingQuantity react to what has actually been received, once something has - forced directly, standing in for Phase 4's receiving endpoint.</summary>
    <TestMethod>
    Public Async Function GetPurchaseOrderHistory_PartiallyReceivedLine_ReflectsReceivedAndOutstanding() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)
            Dim supplierId As Integer = Await CreateActiveSupplierAsync()

            Dim created As PurchaseOrderResponse = Await CreateOrderAsync(
                client, token, supplierId,
                New CreatePurchaseOrderLineRequest With {.ProductId = _productId, .OrderedQuantity = 10.000D, .PurchaseCost = 2.0000D})

            Await ForceLineReceivedQuantityDirectlyAsync(created.Lines(0).Id, 4.000D)

            Dim history As PurchaseOrderHistoryResponse =
                Await GetHistoryAsync(client, token, $"?supplierId={supplierId}")

            Dim item As PurchaseOrderHistoryItemResponse = history.Items(0)
            Assert.AreEqual(10.000D, item.OrderedQuantity)
            Assert.AreEqual(20.0000D, item.OrderedValue)
            Assert.AreEqual(4.000D, item.ReceivedQuantity)
            Assert.AreEqual(8.0000D, item.ReceivedValue, "4 * 2.0000, the captured order-time cost.")
            Assert.AreEqual(6.000D, item.OutstandingQuantity, "10.000 - 4.000")

        End Using

    End Function

    ' ------------------------------------------------------------- box 2

    ''' <summary>A cancelled order stays in history, Status = "Cancelled" - never dropped from an unfiltered read.</summary>
    <TestMethod>
    Public Async Function GetPurchaseOrderHistory_CancelledOrder_AppearsLabelledNotExcluded() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)
            Dim supplierId As Integer = Await CreateActiveSupplierAsync()

            Dim created As PurchaseOrderResponse = Await CreateOrderAsync(
                client, token, supplierId,
                New CreatePurchaseOrderLineRequest With {.ProductId = _productId, .OrderedQuantity = 1.000D, .PurchaseCost = 1.0000D})

            Using cancelResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, $"/api/v1/purchase-orders/{created.Id}/cancel", token,
                                 New CancelPurchaseOrderRequest With {.Reason = "P3-06 fixture"})
                Assert.AreEqual(HttpStatusCode.OK, cancelResponse.StatusCode)
            End Using

            Dim history As PurchaseOrderHistoryResponse =
                Await GetHistoryAsync(client, token, $"?supplierId={supplierId}")

            Assert.HasCount(1, history.Items, "The cancelled order must still be present, not vanished.")
            Assert.AreEqual("Cancelled", history.Items(0).Status)

        End Using

    End Function

    ' ------------------------------------------------------------- box 3

    ''' <summary>An order created "today" (store-local) is included by a fromDate/toDate of today, and excluded once the window moves entirely to another day.</summary>
    <TestMethod>
    Public Async Function GetPurchaseOrderHistory_DateRangeFilter_StoreLocalBoundaries() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)
            Dim supplierId As Integer = Await CreateActiveSupplierAsync()

            Await CreateOrderAsync(
                client, token, supplierId,
                New CreatePurchaseOrderLineRequest With {.ProductId = _productId, .OrderedQuantity = 1.000D, .PurchaseCost = 1.0000D})

            Dim todayLocal As DateOnly = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, StoreTimeZone.Zone))
            Dim tomorrowLocal As DateOnly = todayLocal.AddDays(1)
            Dim yesterdayLocal As DateOnly = todayLocal.AddDays(-1)

            Dim todayText As String = todayLocal.ToString("yyyy-MM-dd")
            Dim tomorrowText As String = tomorrowLocal.ToString("yyyy-MM-dd")
            Dim yesterdayText As String = yesterdayLocal.ToString("yyyy-MM-dd")

            Dim includedByToday As PurchaseOrderHistoryResponse =
                Await GetHistoryAsync(client, token, $"?supplierId={supplierId}&fromDate={todayText}&toDate={todayText}")
            Assert.HasCount(1, includedByToday.Items, "An order created today must be included by a today-to-today window.")
            Assert.AreEqual(todayText, includedByToday.FromDate)
            Assert.AreEqual(todayText, includedByToday.ToDate)
            Assert.AreEqual("Asia/Manila", includedByToday.TimeZone)

            Dim excludedByTomorrow As PurchaseOrderHistoryResponse =
                Await GetHistoryAsync(client, token, $"?supplierId={supplierId}&fromDate={tomorrowText}")
            Assert.IsEmpty(excludedByTomorrow.Items, "A window starting tomorrow must exclude an order created today.")

            Dim excludedByYesterday As PurchaseOrderHistoryResponse =
                Await GetHistoryAsync(client, token, $"?supplierId={supplierId}&toDate={yesterdayText}")
            Assert.IsEmpty(excludedByYesterday.Items, "A window ending yesterday must exclude an order created today.")

        End Using

    End Function

    ''' <summary>An unbounded query states that plainly rather than leaving the caller to guess - both fields null.</summary>
    <TestMethod>
    Public Async Function GetPurchaseOrderHistory_NoDateFilters_EchoesUnboundedRange() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)
            Dim history As PurchaseOrderHistoryResponse = Await GetHistoryAsync(client, token, "?pageSize=1")

            Assert.IsNull(history.FromDate)
            Assert.IsNull(history.ToDate)
            Assert.AreEqual("Asia/Manila", history.TimeZone, "The zone is always stated, even when the range is unbounded.")

        End Using

    End Function

    <TestMethod>
    Public Async Function GetPurchaseOrderHistory_MalformedFromDate_Refused400() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Get, "/api/v1/purchase-orders/history?fromDate=2026%2F08%2F01", token, Nothing)

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("VALIDATION_FAILED", body.ErrorCode)
                Assert.IsTrue(body.Errors.ContainsKey("fromDate"))

            End Using

        End Using

    End Function

    <TestMethod>
    Public Async Function GetPurchaseOrderHistory_FromDateAfterToDate_Refused400() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Get, "/api/v1/purchase-orders/history?fromDate=2026-08-27&toDate=2026-08-01", token, Nothing)

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("VALIDATION_FAILED", body.ErrorCode)
                Assert.IsTrue(body.Errors.ContainsKey("toDate"))

            End Using

        End Using

    End Function

    ' ------------------------------------------------------------- box 4

    ''' <summary>Same pagination contract as P3-03's plain list: defaults, clamping, and no overlap between pages.</summary>
    <TestMethod>
    Public Async Function GetPurchaseOrderHistory_PaginationMatchesP3_03Contract() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)
            Dim supplierId As Integer = Await CreateActiveSupplierAsync()

            For i As Integer = 1 To 3
                Await CreateOrderAsync(
                    client, token, supplierId,
                    New CreatePurchaseOrderLineRequest With {.ProductId = _productId, .OrderedQuantity = 1.000D, .PurchaseCost = 1.0000D})
            Next

            Dim defaulted As PurchaseOrderHistoryResponse = Await GetHistoryAsync(client, token, $"?supplierId={supplierId}")
            Assert.AreEqual(25, defaulted.PageSize, "Page size defaults to 25, same as the plain list.")
            Assert.AreEqual(100, defaulted.MaxPageSize)
            Assert.AreEqual(3, defaulted.TotalCount)

            Dim oversized As PurchaseOrderHistoryResponse = Await GetHistoryAsync(client, token, $"?supplierId={supplierId}&pageSize=5000")
            Assert.AreEqual(100, oversized.PageSize, "An oversized page size is clamped, not honoured.")

            Dim pageOne As PurchaseOrderHistoryResponse =
                Await GetHistoryAsync(client, token, $"?supplierId={supplierId}&sort=orderNumber:asc&page=1&pageSize=2")
            Dim pageTwo As PurchaseOrderHistoryResponse =
                Await GetHistoryAsync(client, token, $"?supplierId={supplierId}&sort=orderNumber:asc&page=2&pageSize=2")

            Assert.HasCount(2, pageOne.Items)
            Assert.HasCount(1, pageTwo.Items)

            Dim allIds As New List(Of Integer)
            allIds.AddRange(pageOne.Items.Select(Function(i) i.Id))
            allIds.AddRange(pageTwo.Items.Select(Function(i) i.Id))
            Assert.HasCount(3, New HashSet(Of Integer)(allIds), "Pages must not overlap.")

        End Using

    End Function

    ' --------------------------------------------------------------- helpers

    Private Async Function CreateOrderAsync(
        client As HttpClient, token As String, supplierId As Integer,
        ParamArray lines As CreatePurchaseOrderLineRequest()) As Task(Of PurchaseOrderResponse)

        Dim request As New CreatePurchaseOrderRequest With {
            .SupplierId = supplierId,
            .IdempotencyKey = Guid.NewGuid().ToString("d"),
            .Lines = lines.ToList()
        }

        Using response As HttpResponseMessage =
            Await SendAsync(client, HttpMethod.Post, "/api/v1/purchase-orders", token, request)

            Assert.AreEqual(
                HttpStatusCode.Created, response.StatusCode,
                "Fixture purchase-order creation must succeed. Body: " & Await response.Content.ReadAsStringAsync())
            Return Await response.Content.ReadFromJsonAsync(Of PurchaseOrderResponse)()

        End Using

    End Function

    Private Async Function GetHistoryAsync(client As HttpClient, token As String, queryString As String) As Task(Of PurchaseOrderHistoryResponse)

        Using response As HttpResponseMessage =
            Await SendAsync(client, HttpMethod.Get, "/api/v1/purchase-orders/history" & queryString, token, Nothing)

            Assert.AreEqual(
                HttpStatusCode.OK, response.StatusCode,
                $"GET /api/v1/purchase-orders/history{queryString} failed. Body: " & Await response.Content.ReadAsStringAsync())
            Return Await response.Content.ReadFromJsonAsync(Of PurchaseOrderHistoryResponse)()

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

    Private Async Function CreateActiveSupplierAsync() As Task(Of Integer)

        Dim name As String = "P3-06 Supplier " & Guid.NewGuid().ToString("N").Substring(0, 12)

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
                    "VALUES (@sku, NULL, 'P3-06 Fixture Product', 1.0000, 0.5000, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                insertCommand.Parameters.AddWithValue("@sku", FixtureProductSku)
                Await insertCommand.ExecuteNonQueryAsync()
                Return CInt(insertCommand.LastInsertedId)
            End Using

        End Using

    End Function

    ''' <summary>Fixture setup only, standing in for Phase 4's receiving endpoint (which does not exist yet) - merch_api holds UPDATE on purchaseorderlines (db/grants/0010).</summary>
    Private Async Function ForceLineReceivedQuantityDirectlyAsync(lineId As Integer, receivedQuantity As Decimal) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "UPDATE PurchaseOrderLines SET ReceivedQuantity = @receivedQuantity, UpdatedAtUtc = UTC_TIMESTAMP(6) WHERE Id = @id;"
                command.Parameters.AddWithValue("@receivedQuantity", receivedQuantity)
                command.Parameters.AddWithValue("@id", lineId)
                Await command.ExecuteNonQueryAsync()
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
