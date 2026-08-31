' Merchandising.Tests.Integration.InventoryReportsTests
'
' P6-05: spec section 14 rows 8-11 (current stock, low stock, stock
' movement, stock adjustment), proven over real HTTP through
' MerchandisingApiFactory against the real pinned MariaDB.
'
'   box 1  all four reconcile through ReportReconciliationHarness -
'          current-stock and low-stock against GET /api/v1/inventory/stock
'          (existing, P4-11); stock-movements against GET /api/v1/inventory/
'          stock/movements (existing, P4-11); stock-adjustments against
'          stock-movements (this card's own new report, reconciling a
'          StockAdjustments-sourced figure against a StockMovements-sourced
'          one - two different tables, never the same query run twice)
'   box 2  the stock movement report carries CorrelationId, asserted
'          against the exact value the fixture command supplied
'   box 3  current stock and the movement report agree with each other for
'          every product - P4-01's ledger invariant, asked as a report
'          question
'   box 4  the low-stock report calls StockRepository.SearchLowStockAsync
'          DIRECTLY (ReportService.GetLowStockReportAsync's own header) -
'          never a second WHERE clause
'
' FIXTURES ARE BUILT THROUGH THE PRODUCTION SERVICE LAYER (AdjustmentService,
' StockService) rather than raw SQL or HTTP - the same fixture style
' ProcurementReportsTests.vb established at P6-04, for the identical reason:
' proving behaviour against the real atomic stock-effect path, not a
' shortcut around it.
'
' RECONCILIATION IS ROBUST TO OTHER TESTS' CONCURRENT DATA - every
' assertion below isolates its own fixture product (by ProductId) or its
' own fixture correlation id out of a possibly much larger shared-database
' result set, paged in full (P6-03/P6-04's "page until TotalCount is
' reached" precedent), never assumed to fit on page one.

Imports System.Collections.Generic
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Net.Http.Json
Imports System.Threading.Tasks
Imports Merchandising.Api.Inventory
Imports Merchandising.Contracts.Auth
Imports Merchandising.Contracts.Inventory
Imports Merchandising.Contracts.Reporting
Imports Merchandising.Domain
Imports Merchandising.Domain.Configuration
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class InventoryReportsTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const FixturePassword As String = "P6-05 Fixture Passw0rd!"
    Private Const AdminAUsername As String = "p6_05_fixture_admin_a"
    Private Const AdminBUsername As String = "p6_05_fixture_admin_b"
    Private Const InventoryUsername As String = "p6_05_fixture_inventory"
    Private Const FixtureProductSkuPrefix As String = "p6_05_fixture_sku_"

    ''' <summary>The threshold this class pins for every test method - see SetUpAsync's own comment for why.</summary>
    Private Const FixtureThreshold As Decimal = 10.000D

    Private _factory As MerchandisingApiFactory
    Private _connectionFactory As ConnectionFactory
    Private _adjustmentService As AdjustmentService
    Private _stockService As StockService
    Private _adminAUserId As Integer
    Private _adminBUserId As Integer
    Private _inventoryUserId As Integer

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _factory = New MerchandisingApiFactory()
        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        _adjustmentService = New AdjustmentService(_connectionFactory)
        _stockService = New StockService(_connectionFactory)

        _adminAUserId = Await EnsureFixtureUserAsync(AdminAUsername, "Admin")
        _adminBUserId = Await EnsureFixtureUserAsync(AdminBUsername, "Admin")
        _inventoryUserId = Await EnsureFixtureUserAsync(InventoryUsername, "InventoryClerk")

        ' PINS THE THRESHOLD, DELIBERATELY. inventory.adjustmentThreshold is
        ' shared, mutable SystemSettings state - AdjustmentTests.vb's own
        ' SetThresholdAsync (same mechanism, reused here) already proves other
        ' test classes in this suite change it and never restore it. The
        ' assembly carries <DoNotParallelize> (AdjustmentTests.vb's own
        ' header, ADR-009.2) specifically so no OTHER class's tests can be
        ' running concurrently and observe this - it is safe to pin here for
        ' every test method in this class to rely on a known value.
        Await SetAdjustmentThresholdAsync(FixtureThreshold)

    End Function

    <TestCleanup>
    Public Sub TearDown()
        _factory?.Dispose()
    End Sub

    ' ---------------------------------------------------------------- box 1a

    ''' <summary>
    ''' Current stock reconciles Quantity/ReorderLevel against GET
    ''' /api/v1/inventory/stock for three fixture products spanning all
    ''' three StockStatus labels, and shows a joined category name (or null,
    ''' for a product with none).
    ''' </summary>
    <TestMethod>
    Public Async Function CurrentStockReport_Reconciles_AgainstStockEndpoint_AndDerivesStockStatus() As Task

        Dim categoryId As Integer = Await CreateCategoryAsync()

        Dim normalProductId As Integer = Await CreateFixtureProductAsync(reorderLevel:=5.000D, categoryId:=categoryId)
        Dim lowProductId As Integer = Await CreateFixtureProductAsync(reorderLevel:=5.000D)
        Dim outOfStockProductId As Integer = Await CreateFixtureProductAsync(reorderLevel:=5.000D)

        Await RequestAppliedAdjustmentAsync(normalProductId, 8.000D)
        Await RequestAppliedAdjustmentAsync(lowProductId, 3.000D)
        ' outOfStockProductId is left untouched - zero stock, no StockBalances row at all.

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, AdminAUsername)

            Dim detail As IReadOnlyList(Of StockBalanceResponse) = Await FetchAllStockAsync(client, token)
            Dim report As IReadOnlyList(Of CurrentStockReportItemResponse) = Await FetchAllCurrentStockReportAsync(client, token)

            For Each productId As Integer In {normalProductId, lowProductId, outOfStockProductId}

                Dim detailItem As StockBalanceResponse = detail.Single(Function(i) i.ProductId = productId)
                Dim reportItem As CurrentStockReportItemResponse = report.Single(Function(i) i.ProductId = productId)

                ReportReconciliationHarness.AssertReconciles(
                    reportItem.Quantity, detailItem.Quantity,
                    "Current stock report", "Stock endpoint (quantity)", $"productId={productId}")
                ReportReconciliationHarness.AssertReconciles(
                    reportItem.ReorderLevel, detailItem.ReorderLevel,
                    "Current stock report", "Stock endpoint (reorder level)", $"productId={productId}")

            Next

            Dim normalItem As CurrentStockReportItemResponse = report.Single(Function(i) i.ProductId = normalProductId)
            Assert.AreEqual(8.000D, normalItem.Quantity)
            Assert.AreEqual("Normal", normalItem.StockStatus)
            Assert.AreEqual(categoryId, normalItem.CategoryId)
            Assert.IsNotNull(normalItem.CategoryName)

            Dim lowItem As CurrentStockReportItemResponse = report.Single(Function(i) i.ProductId = lowProductId)
            Assert.AreEqual(3.000D, lowItem.Quantity, "3.000 <= reorder level 5.000.")
            Assert.AreEqual("Low", lowItem.StockStatus)
            Assert.IsNull(lowItem.CategoryId, "This fixture product was created with no category.")
            Assert.IsNull(lowItem.CategoryName)

            Dim outOfStockItem As CurrentStockReportItemResponse = report.Single(Function(i) i.ProductId = outOfStockProductId)
            Assert.AreEqual(0.000D, outOfStockItem.Quantity, "Never touched by a stock-changing command.")
            Assert.AreEqual("OutOfStock", outOfStockItem.StockStatus)

        End Using

    End Function

    ' ---------------------------------------------------------------- box 1b, box 4

    ''' <summary>
    ''' Low-stock's own row set reconciles against GET /api/v1/inventory/stock
    ''' filtered client-side by the SAME threshold comparison
    ''' (quantity &lt;= reorderLevel) - never against GET /api/v1/inventory/
    ''' low-stock again, which (per ReportService.GetLowStockReportAsync's
    ''' header) is the exact same query this report already calls, and would
    ''' merely prove the query is deterministic.
    ''' </summary>
    <TestMethod>
    Public Async Function LowStockReport_Reconciles_AgainstStockEndpoint_ByThresholdFilter() As Task

        Dim belowThresholdProductId As Integer = Await CreateFixtureProductAsync(reorderLevel:=5.000D)
        Dim aboveThresholdProductId As Integer = Await CreateFixtureProductAsync(reorderLevel:=5.000D)
        Dim knownProductIds As New HashSet(Of Integer) From {belowThresholdProductId, aboveThresholdProductId}

        Await RequestAppliedAdjustmentAsync(belowThresholdProductId, 2.000D)  ' 2.000 <= 5.000 - at or below.
        Await RequestAppliedAdjustmentAsync(aboveThresholdProductId, 9.000D) ' 9.000 > 5.000 - above.

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, AdminAUsername)

            Dim detail As IReadOnlyList(Of StockBalanceResponse) = Await FetchAllStockAsync(client, token)
            Dim expectedLowIds As HashSet(Of Integer) =
                detail.Where(Function(i) knownProductIds.Contains(i.ProductId) AndAlso i.Quantity <= i.ReorderLevel) _
                      .Select(Function(i) i.ProductId).ToHashSet()

            Dim report As IReadOnlyList(Of LowStockItemResponse) = Await FetchAllLowStockReportAsync(client, token)
            Dim actualLowIds As HashSet(Of Integer) =
                report.Where(Function(i) knownProductIds.Contains(i.ProductId)).Select(Function(i) i.ProductId).ToHashSet()

            ReportReconciliationHarness.AssertReconciles(
                CDec(actualLowIds.Count), CDec(expectedLowIds.Count),
                "Low-stock report", "Stock endpoint (threshold-filtered)", $"productIds={String.Join(",", knownProductIds)}")

            Assert.IsTrue(actualLowIds.SetEquals(expectedLowIds))
            Assert.Contains(belowThresholdProductId, actualLowIds)
            Assert.DoesNotContain(aboveThresholdProductId, actualLowIds)

        End Using

    End Function

    ' ---------------------------------------------------------------- box 1c, box 2

    ''' <summary>
    ''' The stock movement report reconciles row-for-row against GET
    ''' /api/v1/inventory/stock/movements for one product, and every row
    ''' carries the exact CorrelationId the fixture command supplied - the
    ''' card's own named Done-when box.
    ''' </summary>
    <TestMethod>
    Public Async Function StockMovementReport_Reconciles_AgainstStockMovementsEndpoint_AndCarriesCorrelationId() As Task

        Dim productId As Integer = Await CreateFixtureProductAsync(reorderLevel:=0D)

        Dim increaseCorrelationId As String = Guid.NewGuid().ToString()
        Dim increaseOutcome As AdjustmentOutcome =
            Await _adjustmentService.RequestAsync(
                productId, 6.000D, "P6-05 fixture increase", _inventoryUserId, increaseCorrelationId, Guid.NewGuid().ToString())
        Assert.AreEqual(AdjustmentOutcomeKind.Created, increaseOutcome.Kind)
        Assert.AreEqual("Applied", increaseOutcome.Response.Status)

        Dim decreaseCorrelationId As String = Guid.NewGuid().ToString()
        Dim decrementOutcome As StockDecrementOutcome =
            Await _stockService.DecrementAsync(
                productId, 2.000D, "P6-05 fixture decrement", _inventoryUserId, decreaseCorrelationId, Guid.NewGuid().ToString())
        Assert.AreEqual(StockDecrementOutcomeKind.Success, decrementOutcome.Kind)

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, AdminAUsername)

            Dim detail As IReadOnlyList(Of StockMovementItemResponse) = Await FetchAllStockMovementsAsync(client, token, productId)
            Dim report As IReadOnlyList(Of StockMovementReportItemResponse) = Await FetchAllStockMovementReportAsync(client, token, productId, Nothing, Nothing)

            ReportReconciliationHarness.AssertReconciles(
                CDec(report.Count), CDec(detail.Count),
                "Stock movement report", "Stock movements endpoint (row count)", $"productId={productId}")
            ReportReconciliationHarness.AssertReconciles(
                report.Sum(Function(i) i.Delta), detail.Sum(Function(i) i.Delta),
                "Stock movement report", "Stock movements endpoint (delta sum)", $"productId={productId}")

            Dim increaseReportItem As StockMovementReportItemResponse = report.Single(Function(i) i.CorrelationId = increaseCorrelationId)
            Assert.AreEqual(6.000D, increaseReportItem.Delta)
            Assert.AreEqual("StockAdjustment", increaseReportItem.Reason)
            Assert.AreEqual(_inventoryUserId, increaseReportItem.ActorUserId)
            Assert.AreEqual(InventoryUsername, increaseReportItem.ActorUsername)

            Dim decreaseReportItem As StockMovementReportItemResponse = report.Single(Function(i) i.CorrelationId = decreaseCorrelationId)
            Assert.AreEqual(-2.000D, decreaseReportItem.Delta)
            Assert.AreEqual("P6-05 fixture decrement", decreaseReportItem.Reason)

        End Using

    End Function

    ' ---------------------------------------------------------------- box 3

    ''' <summary>
    ''' Current stock and the movement report agree with each other for
    ''' every product - P4-01's standing ledger assertion, asked as a report
    ''' question (tasks.md P6-05's own Done-when box).
    ''' </summary>
    <TestMethod>
    Public Async Function CurrentStockReport_And_StockMovementReport_AgreeForEveryFixtureProduct() As Task

        Dim productA As Integer = Await CreateFixtureProductAsync()
        Dim productB As Integer = Await CreateFixtureProductAsync()

        Await RequestAppliedAdjustmentAsync(productA, 9.000D)
        Await _stockService.DecrementAsync(productA, 4.000D, "P6-05 fixture decrement", _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Await RequestAppliedAdjustmentAsync(productB, 3.000D)
        Await RequestAppliedAdjustmentAsync(productB, 2.000D)

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, AdminAUsername)

            Dim currentStock As IReadOnlyList(Of CurrentStockReportItemResponse) = Await FetchAllCurrentStockReportAsync(client, token)

            For Each productId As Integer In {productA, productB}

                Dim currentStockItem As CurrentStockReportItemResponse = currentStock.Single(Function(i) i.ProductId = productId)
                Dim movements As IReadOnlyList(Of StockMovementReportItemResponse) =
                    Await FetchAllStockMovementReportAsync(client, token, productId, Nothing, Nothing)

                ReportReconciliationHarness.AssertReconciles(
                    currentStockItem.Quantity, movements.Sum(Function(m) m.Delta),
                    "Current stock report", "Stock movement report (summed delta)", $"productId={productId}")

            Next

            Dim productAStock As CurrentStockReportItemResponse = currentStock.Single(Function(i) i.ProductId = productA)
            Assert.AreEqual(5.000D, productAStock.Quantity, "9.000 - 4.000")

            Dim productBStock As CurrentStockReportItemResponse = currentStock.Single(Function(i) i.ProductId = productB)
            Assert.AreEqual(5.000D, productBStock.Quantity, "3.000 + 2.000")

        End Using

    End Function

    ' ---------------------------------------------------------------- box 1d

    ''' <summary>
    ''' Stock-adjustment report reconciles an Applied adjustment's
    ''' QuantityVariance against the exact StockMovements row it produced
    ''' (matched by CorrelationId, via the stock movement report) - two
    ''' different source tables, never the same query twice. A Pending
    ''' adjustment shows no stock effect at all, and produces no matching
    ''' movement row.
    ''' </summary>
    <TestMethod>
    Public Async Function StockAdjustmentReport_Reconciles_AppliedEffect_AndPendingHasNoStockEffect() As Task

        Dim belowVariance As Decimal = FixtureThreshold - 5.000D
        Dim aboveVariance As Decimal = FixtureThreshold + 5.000D

        Dim appliedProductId As Integer = Await CreateFixtureProductAsync()
        Dim pendingProductId As Integer = Await CreateFixtureProductAsync()

        Dim appliedCorrelationId As String = Guid.NewGuid().ToString()
        Dim appliedOutcome As AdjustmentOutcome =
            Await _adjustmentService.RequestAsync(
                appliedProductId, belowVariance, "P6-05 fixture applied adjustment", _inventoryUserId, appliedCorrelationId, Guid.NewGuid().ToString())
        Assert.AreEqual(AdjustmentOutcomeKind.Created, appliedOutcome.Kind)
        Assert.AreEqual("Applied", appliedOutcome.Response.Status, "Below threshold - applied immediately.")

        Dim pendingCorrelationId As String = Guid.NewGuid().ToString()
        Dim pendingOutcome As AdjustmentOutcome =
            Await _adjustmentService.RequestAsync(
                pendingProductId, aboveVariance, "P6-05 fixture pending adjustment", _inventoryUserId, pendingCorrelationId, Guid.NewGuid().ToString())
        Assert.AreEqual(AdjustmentOutcomeKind.Created, pendingOutcome.Kind)
        Assert.AreEqual("Pending", pendingOutcome.Response.Status, "At or above threshold - awaits approval.")

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, AdminAUsername)

            ' -------------------------------------------------- applied side
            Dim appliedReport As IReadOnlyList(Of StockAdjustmentReportItemResponse) =
                Await FetchAllStockAdjustmentReportAsync(client, token, appliedProductId)
            Dim appliedReportItem As StockAdjustmentReportItemResponse = appliedReport.Single(Function(i) i.Id = appliedOutcome.Response.Id)

            Assert.AreEqual("Applied", appliedReportItem.Status)
            Assert.IsTrue(appliedReportItem.StockEffectApplied)
            Assert.AreEqual(belowVariance, appliedReportItem.QuantityVariance)
            Assert.AreEqual(_inventoryUserId, appliedReportItem.RequestedByUserId)
            Assert.AreEqual(InventoryUsername, appliedReportItem.RequestedByUsername)
            Assert.IsNull(appliedReportItem.ApprovedByUserId, "Applied directly below threshold - nobody approved it.")

            Dim appliedMovements As IReadOnlyList(Of StockMovementReportItemResponse) =
                Await FetchAllStockMovementReportAsync(client, token, appliedProductId, Nothing, Nothing)
            Dim appliedMovement As StockMovementReportItemResponse = appliedMovements.Single(Function(m) m.CorrelationId = appliedCorrelationId)

            ReportReconciliationHarness.AssertReconciles(
                appliedReportItem.QuantityVariance, appliedMovement.Delta,
                "Stock-adjustment report (Applied)", "Stock movement report (matching correlation id)", $"adjustmentId={appliedOutcome.Response.Id}")
            Assert.AreEqual("StockAdjustment", appliedMovement.Reason)

            ' -------------------------------------------------- pending side
            Dim pendingReport As IReadOnlyList(Of StockAdjustmentReportItemResponse) =
                Await FetchAllStockAdjustmentReportAsync(client, token, pendingProductId)
            Dim pendingReportItem As StockAdjustmentReportItemResponse = pendingReport.Single(Function(i) i.Id = pendingOutcome.Response.Id)

            Assert.AreEqual("Pending", pendingReportItem.Status)
            Assert.IsFalse(pendingReportItem.StockEffectApplied)
            Assert.AreEqual(aboveVariance, pendingReportItem.QuantityVariance)

            Dim pendingMovements As IReadOnlyList(Of StockMovementReportItemResponse) =
                Await FetchAllStockMovementReportAsync(client, token, pendingProductId, Nothing, Nothing)
            Assert.IsFalse(
                pendingMovements.Any(Function(m) m.CorrelationId = pendingCorrelationId),
                "A Pending adjustment has never touched stock - AdjustmentService's own header.")

            ' ------------------------------------------- approve, then re-check
            Dim approveOutcome As AdjustmentOutcome =
                Await _adjustmentService.ApproveAsync(pendingOutcome.Response.Id, _adminBUserId, Guid.NewGuid().ToString())
            Assert.AreEqual(AdjustmentOutcomeKind.Created, approveOutcome.Kind)
            Assert.AreEqual("Applied", approveOutcome.Response.Status)

            Dim afterApproveReport As IReadOnlyList(Of StockAdjustmentReportItemResponse) =
                Await FetchAllStockAdjustmentReportAsync(client, token, pendingProductId)
            Dim afterApproveItem As StockAdjustmentReportItemResponse = afterApproveReport.Single(Function(i) i.Id = pendingOutcome.Response.Id)

            Assert.AreEqual("Applied", afterApproveItem.Status)
            Assert.IsTrue(afterApproveItem.StockEffectApplied)
            Assert.AreEqual(_adminBUserId, afterApproveItem.ApprovedByUserId)
            Assert.AreEqual(AdminBUsername, afterApproveItem.ApprovedByUsername)

        End Using

    End Function

    ' ---------------------------------------------------------------- range/treatment

    ''' <summary>
    ''' Current-stock and low-stock echo an unbounded range (no date
    ''' dimension) and ReturnsTreatment = Excluded; stock-movements echoes
    ''' the applied range and Included; stock-adjustments echoes the applied
    ''' range and Excluded - all per docs/report-specification.md section 4
    ''' rows 8-11.
    ''' </summary>
    <TestMethod>
    Public Async Function AllFourReports_StateReturnsTreatmentPerSpec() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, AdminAUsername)

            Dim currentStock As CurrentStockReportResponse =
                Await GetJsonAsync(Of CurrentStockReportResponse)(client, token, "/api/v1/reports/inventory/current-stock?pageSize=1")
            Assert.IsNull(currentStock.Range.FromDate)
            Assert.IsNull(currentStock.Range.ToDate)
            Assert.AreEqual("Excluded", currentStock.Range.ReturnsTreatment)

            Dim lowStock As LowStockReportResponse =
                Await GetJsonAsync(Of LowStockReportResponse)(client, token, "/api/v1/reports/inventory/low-stock?pageSize=1")
            Assert.IsNull(lowStock.Range.FromDate)
            Assert.IsNull(lowStock.Range.ToDate)
            Assert.AreEqual("Excluded", lowStock.Range.ReturnsTreatment)

            Dim todayText As String = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, StoreTimeZone.Zone)).ToString("yyyy-MM-dd")

            Dim movements As StockMovementReportResponse =
                Await GetJsonAsync(Of StockMovementReportResponse)(
                    client, token, $"/api/v1/reports/inventory/stock-movements?fromDate={todayText}&toDate={todayText}&pageSize=1")
            Assert.AreEqual(todayText, movements.Range.FromDate)
            Assert.AreEqual(todayText, movements.Range.ToDate)
            Assert.AreEqual("Asia/Manila", movements.Range.TimeZone)
            Assert.AreEqual("Included", movements.Range.ReturnsTreatment)

            Dim adjustments As StockAdjustmentReportResponse =
                Await GetJsonAsync(Of StockAdjustmentReportResponse)(
                    client, token, $"/api/v1/reports/inventory/stock-adjustments?fromDate={todayText}&toDate={todayText}&pageSize=1")
            Assert.AreEqual(todayText, adjustments.Range.FromDate)
            Assert.AreEqual(todayText, adjustments.Range.ToDate)
            Assert.AreEqual("Excluded", adjustments.Range.ReturnsTreatment)

        End Using

    End Function

    ' --------------------------------------------------------------- fixtures

    ''' <summary>An adjustment expected to apply immediately (below the configured threshold) - fails the test loudly if it does not.</summary>
    Private Async Function RequestAppliedAdjustmentAsync(productId As Integer, variance As Decimal) As Task(Of AdjustmentResponse)

        Dim outcome As AdjustmentOutcome =
            Await _adjustmentService.RequestAsync(
                productId, variance, "P6-05 fixture adjustment", _inventoryUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(AdjustmentOutcomeKind.Created, outcome.Kind, "Fixture adjustment must succeed.")
        Assert.AreEqual("Applied", outcome.Response.Status, "Fixture variance must stay below the configured threshold.")

        Return outcome.Response

    End Function

    ''' <summary>Sets the adjustment approval threshold via SystemSettingsRepository.UpsertAsync directly - the same mechanism AdjustmentTests.vb's own SetThresholdAsync uses, never a shortcut around it.</summary>
    Private Async Function SetAdjustmentThresholdAsync(threshold As Decimal) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using transaction As MySqlTransaction = Await connection.BeginTransactionAsync()

                Await SystemSettingsRepository.UpsertAsync(
                    connection, transaction, SystemSettingRegistry.Keys.AdjustmentApprovalThreshold,
                    threshold.ToString("0.000", Globalization.CultureInfo.InvariantCulture), _inventoryUserId)

                Await transaction.CommitAsync()

            End Using
        End Using

    End Function

    Private Async Function CreateCategoryAsync() As Task(Of Integer)

        Dim name As String = "P6-05 Category " & Guid.NewGuid().ToString("N").Substring(0, 12)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO Categories (Name, CreatedAtUtc, UpdatedAtUtc) VALUES (@name, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@name", name)
                Await command.ExecuteNonQueryAsync()
                Return CInt(command.LastInsertedId)
            End Using
        End Using

    End Function

    Private Async Function CreateFixtureProductAsync(
        Optional reorderLevel As Decimal = 0D, Optional categoryId As Integer? = Nothing) As Task(Of Integer)

        Dim sku As String = FixtureProductSkuPrefix & Guid.NewGuid().ToString("N").Substring(0, 16)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO Products (Sku, Barcode, Name, Price, Cost, ReorderLevel, CategoryId, IsActive, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@sku, NULL, 'P6-05 Fixture Product', 9.0000, 4.0000, @reorderLevel, @categoryId, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@sku", sku)
                command.Parameters.AddWithValue("@reorderLevel", reorderLevel)
                command.Parameters.AddWithValue("@categoryId", If(CObj(categoryId), DBNull.Value))
                Await command.ExecuteNonQueryAsync()
                Return CInt(command.LastInsertedId)
            End Using
        End Using

    End Function

    ' --------------------------------------------------------------- paging

    Private Async Function FetchAllStockAsync(client As HttpClient, token As String) As Task(Of IReadOnlyList(Of StockBalanceResponse))

        Dim items As New List(Of StockBalanceResponse)
        Dim page As Integer = 1

        Do
            Dim result As StockSearchResponse =
                Await GetJsonAsync(Of StockSearchResponse)(client, token, $"/api/v1/inventory/stock?includeInactive=true&page={page}&pageSize=100")

            items.AddRange(result.Items)
            If items.Count >= result.TotalCount OrElse result.Items.Count = 0 Then
                Exit Do
            End If
            page += 1
        Loop

        Return items

    End Function

    Private Async Function FetchAllLowStockReportAsync(client As HttpClient, token As String) As Task(Of IReadOnlyList(Of LowStockItemResponse))

        Dim items As New List(Of LowStockItemResponse)
        Dim page As Integer = 1

        Do
            Dim result As LowStockReportResponse =
                Await GetJsonAsync(Of LowStockReportResponse)(client, token, $"/api/v1/reports/inventory/low-stock?page={page}&pageSize=100")

            items.AddRange(result.Items)
            If items.Count >= result.TotalCount OrElse result.Items.Count = 0 Then
                Exit Do
            End If
            page += 1
        Loop

        Return items

    End Function

    Private Async Function FetchAllCurrentStockReportAsync(client As HttpClient, token As String) As Task(Of IReadOnlyList(Of CurrentStockReportItemResponse))

        Dim items As New List(Of CurrentStockReportItemResponse)
        Dim page As Integer = 1

        Do
            Dim result As CurrentStockReportResponse =
                Await GetJsonAsync(Of CurrentStockReportResponse)(
                    client, token, $"/api/v1/reports/inventory/current-stock?includeInactive=true&page={page}&pageSize=100")

            items.AddRange(result.Items)
            If items.Count >= result.TotalCount OrElse result.Items.Count = 0 Then
                Exit Do
            End If
            page += 1
        Loop

        Return items

    End Function

    Private Async Function FetchAllStockMovementsAsync(
        client As HttpClient, token As String, productId As Integer) As Task(Of IReadOnlyList(Of StockMovementItemResponse))

        Dim items As New List(Of StockMovementItemResponse)
        Dim page As Integer = 1

        Do
            Dim result As StockMovementSearchResponse =
                Await GetJsonAsync(Of StockMovementSearchResponse)(
                    client, token, $"/api/v1/inventory/stock/movements?productId={productId}&page={page}&pageSize=100")

            items.AddRange(result.Items)
            If items.Count >= result.TotalCount OrElse result.Items.Count = 0 Then
                Exit Do
            End If
            page += 1
        Loop

        Return items

    End Function

    Private Async Function FetchAllStockMovementReportAsync(
        client As HttpClient, token As String, productId As Integer?, fromDate As String, toDate As String) As Task(Of IReadOnlyList(Of StockMovementReportItemResponse))

        Dim query As String = MovementQuery(productId, fromDate, toDate)
        Dim items As New List(Of StockMovementReportItemResponse)
        Dim page As Integer = 1

        Do
            Dim result As StockMovementReportResponse =
                Await GetJsonAsync(Of StockMovementReportResponse)(
                    client, token, $"/api/v1/reports/inventory/stock-movements?{query}page={page}&pageSize=100")

            items.AddRange(result.Items)
            If items.Count >= result.TotalCount OrElse result.Items.Count = 0 Then
                Exit Do
            End If
            page += 1
        Loop

        Return items

    End Function

    Private Async Function FetchAllStockAdjustmentReportAsync(
        client As HttpClient, token As String, productId As Integer?) As Task(Of IReadOnlyList(Of StockAdjustmentReportItemResponse))

        Dim query As String = MovementQuery(productId, Nothing, Nothing)
        Dim items As New List(Of StockAdjustmentReportItemResponse)
        Dim page As Integer = 1

        Do
            Dim result As StockAdjustmentReportResponse =
                Await GetJsonAsync(Of StockAdjustmentReportResponse)(
                    client, token, $"/api/v1/reports/inventory/stock-adjustments?{query}page={page}&pageSize=100")

            items.AddRange(result.Items)
            If items.Count >= result.TotalCount OrElse result.Items.Count = 0 Then
                Exit Do
            End If
            page += 1
        Loop

        Return items

    End Function

    Private Shared Function MovementQuery(productId As Integer?, fromDate As String, toDate As String) As String

        Dim parts As New List(Of String)
        If productId.HasValue Then
            parts.Add($"productId={productId.Value}")
        End If
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
