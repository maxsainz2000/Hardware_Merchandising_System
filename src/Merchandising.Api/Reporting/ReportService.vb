' Merchandising.Api.Reporting.ReportService
'
' P6-02: the read-only orchestration layer between ReportsController and
' ReportRepository - the same division PurchaseOrderService.SearchAsync/
' GetAsync already establish for a read-only surface: the controller owns
' HTTP shape (parsing, clamping, refusing a malformed request), this owns
' opening a connection and mapping repository rows onto response contracts.
' No transaction anywhere in this class - every method here is a plain read,
' and merch_api's database-level SELECT (ADR-013) is everything it needs.
'
' EVERY DATE PARAMETER IS ALREADY A UTC INSTANT BY THE TIME IT REACHES HERE -
' ReportsController performs the store-local-to-UTC conversion through
' Merchandising.Domain.StoreTimeZone, per docs/report-specification.md
' section 2 and ADR-023 point 1. This class never parses a date string and
' never computes a day boundary of its own.

Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Merchandising.Contracts.Inventory
Imports Merchandising.Contracts.Reporting
Imports Merchandising.Domain
Imports Merchandising.Domain.Reporting
Imports Merchandising.Infrastructure.Data
Imports MySqlConnector

Namespace Reporting

    Public NotInheritable Class ReportService

        Private ReadOnly _connectionFactory As ConnectionFactory

        Public Sub New(connectionFactory As ConnectionFactory)
            _connectionFactory = connectionFactory
        End Sub

        ''' <summary>Spec section 14 row 1 - single store-local day.</summary>
        Public Async Function GetDailySalesSummaryAsync(
            storeLocalDate As String, fromUtc As DateTime, toUtcExclusive As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of DailySalesSummaryResponse)

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim result =
                    Await ReportRepository.GetDailySalesSummaryAsync(connection, fromUtc, toUtcExclusive, cancellationToken).ConfigureAwait(False)

                Return New DailySalesSummaryResponse With {
                    .Range = New ReportRangeEnvelope With {
                        .FromDate = storeLocalDate,
                        .ToDate = storeLocalDate,
                        .TimeZone = StoreTimeZone.IanaId,
                        .ReturnsTreatment = NameOf(ReturnsTreatment.Included)
                    },
                    .CompletedSalesCount = result.CompletedSalesCount,
                    .CompletedSalesTotal = result.CompletedSalesTotal,
                    .CompletedReturnsCount = result.CompletedReturnsCount,
                    .CompletedReturnsTotal = result.CompletedReturnsTotal,
                    .NetSalesTotal = result.CompletedSalesTotal - result.CompletedReturnsTotal,
                    .PaymentTotals = ToPaymentTotalResponses(result.PaymentTotals)
                }

            End Using

        End Function

        ''' <summary>Spec section 14 row 4 - the payment-method summary. Returns treatment: Excluded (docs/report-specification.md section 4 row 4).</summary>
        Public Async Function GetPaymentMethodSummaryAsync(
            fromDate As String, toDate As String, fromUtc As DateTime?, toUtcExclusive As DateTime?,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of PaymentMethodSummaryResponse)

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim totals =
                    Await ReportRepository.GetPaymentMethodSummaryAsync(connection, fromUtc, toUtcExclusive, cancellationToken).ConfigureAwait(False)

                Return New PaymentMethodSummaryResponse With {
                    .Range = New ReportRangeEnvelope With {
                        .FromDate = fromDate,
                        .ToDate = toDate,
                        .TimeZone = StoreTimeZone.IanaId,
                        .ReturnsTreatment = NameOf(ReturnsTreatment.Excluded)
                    },
                    .Totals = ToPaymentTotalResponses(totals)
                }

            End Using

        End Function

        ''' <summary>Spec section 14 row 2. Returns treatment: Included.</summary>
        Public Async Function GetSalesByProductAsync(
            fromDate As String, toDate As String, fromUtc As DateTime?, toUtcExclusive As DateTime?,
            sortField As SalesByProductSortField, sortDescending As Boolean, page As Integer, pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Items As IReadOnlyList(Of SalesByProductItemResponse), TotalCount As Integer))

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim result =
                    Await ReportRepository.GetSalesByProductAsync(
                        connection, fromUtc, toUtcExclusive, sortField, sortDescending, page, pageSize, cancellationToken).ConfigureAwait(False)

                Dim items = result.Items.Select(
                    Function(r) New SalesByProductItemResponse With {
                        .ProductId = r.ProductId,
                        .ProductSku = r.ProductSku,
                        .ProductName = r.ProductName,
                        .QuantitySold = r.QuantitySold,
                        .QuantityReturned = r.QuantityReturned,
                        .NetQuantity = r.QuantitySold - r.QuantityReturned,
                        .GrossSalesValue = r.GrossSalesValue,
                        .CapturedCostBasis = r.CapturedCostBasis
                    }).ToList()

                Return (Items:=CType(items, IReadOnlyList(Of SalesByProductItemResponse)), TotalCount:=result.TotalCount)

            End Using

        End Function

        ''' <summary>Spec section 14 row 3. Returns treatment: Included.</summary>
        Public Async Function GetSalesByCashierAsync(
            fromDate As String, toDate As String, fromUtc As DateTime?, toUtcExclusive As DateTime?,
            sortField As SalesByCashierSortField, sortDescending As Boolean, page As Integer, pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Items As IReadOnlyList(Of SalesByCashierItemResponse), TotalCount As Integer))

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim result =
                    Await ReportRepository.GetSalesByCashierAsync(
                        connection, fromUtc, toUtcExclusive, sortField, sortDescending, page, pageSize, cancellationToken).ConfigureAwait(False)

                Dim items = result.Items.Select(
                    Function(r) New SalesByCashierItemResponse With {
                        .CashierUserId = r.CashierUserId,
                        .CashierUsername = r.CashierUsername,
                        .CompletedSalesCount = r.CompletedSalesCount,
                        .CompletedSalesTotal = r.CompletedSalesTotal,
                        .ReturnsCount = r.ReturnsCount,
                        .ReturnsTotal = r.ReturnsTotal,
                        .NetValue = r.CompletedSalesTotal - r.ReturnsTotal,
                        .PaymentTotals = ToPaymentTotalResponses(r.PaymentTotals)
                    }).ToList()

                Return (Items:=CType(items, IReadOnlyList(Of SalesByCashierItemResponse)), TotalCount:=result.TotalCount)

            End Using

        End Function

        ''' <summary>Spec section 14 row 5. Returns treatment: Included (the report's whole subject is return rows - docs/report-specification.md section 4 row 5).</summary>
        Public Async Function GetReturnsAndCancellationsAsync(
            fromDate As String, toDate As String, fromUtc As DateTime?, toUtcExclusive As DateTime?,
            sortDescending As Boolean, page As Integer, pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Items As IReadOnlyList(Of ReturnsAndCancellationsItemResponse), TotalCount As Integer))

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim result =
                    Await ReportRepository.GetReturnsAndCancellationsAsync(
                        connection, fromUtc, toUtcExclusive, sortDescending, page, pageSize, cancellationToken).ConfigureAwait(False)

                Dim items = result.Items.Select(
                    Function(r) New ReturnsAndCancellationsItemResponse With {
                        .SalesReturnId = r.SalesReturnId,
                        .SalesReturnLineId = r.SalesReturnLineId,
                        .SaleId = r.SaleId,
                        .ProductId = r.ProductId,
                        .ProductSku = r.ProductSku,
                        .ProductName = r.ProductName,
                        .QuantityReturned = r.QuantityReturned,
                        .Reason = r.Reason,
                        .ReturnedByUserId = r.ReturnedByUserId,
                        .ReturnedByUsername = r.ReturnedByUsername,
                        .ApprovedByUserId = r.ApprovedByUserId,
                        .ApprovedByUsername = r.ApprovedByUsername,
                        .Status = r.Status,
                        .RestocksItem = r.RestocksItem,
                        .ReturnedAtUtc = r.ReturnedAtUtc
                    }).ToList()

                Return (Items:=CType(items, IReadOnlyList(Of ReturnsAndCancellationsItemResponse)), TotalCount:=result.TotalCount)

            End Using

        End Function

        ''' <summary>
        ''' Spec section 14 row 12. Returns treatment: Included.
        ''' NetQuantity/NetSalesValue/RecordedCostEstimate respect
        ''' fromUtc/toUtcExclusive; CurrentStockQuantity never does - it is
        ''' read live by ReportRepository regardless of the window
        ''' (ProductPerformanceItemResponse's own header). MarginEstimate is
        ''' the one NEW derived figure this phase computes (docs/report-
        ''' specification.md section 6) - rounded once, here, through
        ''' DecimalScaleGuard.RoundMoney.
        ''' </summary>
        Public Async Function GetProductPerformanceAsync(
            fromDate As String, toDate As String, fromUtc As DateTime?, toUtcExclusive As DateTime?,
            sortField As ProductPerformanceSortField, sortDescending As Boolean, page As Integer, pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Items As IReadOnlyList(Of ProductPerformanceItemResponse), TotalCount As Integer))

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim result =
                    Await ReportRepository.GetProductPerformanceAsync(
                        connection, fromUtc, toUtcExclusive, sortField, sortDescending, page, pageSize, cancellationToken).ConfigureAwait(False)

                Dim items = result.Items.Select(
                    Function(r)
                        Dim netQuantity As Decimal = r.QuantitySold - r.QuantityReturned
                        Dim netSalesValue As Decimal = r.GrossSalesValue - r.ReturnedValue
                        Dim recordedCostEstimate As Decimal = r.CapturedCostBasis - r.ReturnedCostBasis
                        Dim marginEstimate As Decimal = DecimalScaleGuard.RoundMoney(netSalesValue - recordedCostEstimate)

                        Return New ProductPerformanceItemResponse With {
                            .ProductId = r.ProductId,
                            .ProductSku = r.ProductSku,
                            .ProductName = r.ProductName,
                            .QuantitySold = r.QuantitySold,
                            .QuantityReturned = r.QuantityReturned,
                            .NetQuantity = netQuantity,
                            .NetSalesValue = netSalesValue,
                            .RecordedCostEstimate = recordedCostEstimate,
                            .MarginEstimate = marginEstimate,
                            .CurrentStockQuantity = r.CurrentStockQuantity
                        }
                    End Function).ToList()

                Return (Items:=CType(items, IReadOnlyList(Of ProductPerformanceItemResponse)), TotalCount:=result.TotalCount)

            End Using

        End Function

        ''' <summary>Spec section 14 row 6. Returns treatment: Excluded (docs/report-specification.md section 4 row 6). See ReportRepository.GetPurchaseOrderHistoryReportAsync's header for why ReceivedQuantity/Value/OutstandingQuantity are computed from committed ReceiptLines rather than the PurchaseOrderLines accumulator.</summary>
        Public Async Function GetPurchaseOrderHistoryReportAsync(
            fromDate As String, toDate As String, fromUtc As DateTime?, toUtcExclusive As DateTime?,
            sortField As PurchaseOrderHistoryReportSortField, sortDescending As Boolean, page As Integer, pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Items As IReadOnlyList(Of PurchaseOrderHistoryReportItemResponse), TotalCount As Integer))

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim result =
                    Await ReportRepository.GetPurchaseOrderHistoryReportAsync(
                        connection, fromUtc, toUtcExclusive, sortField, sortDescending, page, pageSize, cancellationToken).ConfigureAwait(False)

                Dim items = result.Items.Select(
                    Function(r) New PurchaseOrderHistoryReportItemResponse With {
                        .Id = r.Id,
                        .OrderNumber = r.OrderNumber,
                        .SupplierId = r.SupplierId,
                        .SupplierName = r.SupplierName,
                        .Status = r.Status,
                        .CreatedAtUtc = r.CreatedAtUtc,
                        .OrderedQuantity = r.OrderedQuantity,
                        .OrderedValue = r.OrderedValue,
                        .ReceivedQuantity = r.ReceivedQuantity,
                        .ReceivedValue = r.ReceivedValue,
                        .OutstandingQuantity = r.OutstandingQuantity
                    }).ToList()

                Return (Items:=CType(items, IReadOnlyList(Of PurchaseOrderHistoryReportItemResponse)), TotalCount:=result.TotalCount)

            End Using

        End Function

        ''' <summary>Spec section 14 row 7. Returns treatment: Excluded (docs/report-specification.md section 4 row 7). OrderedQuantity is the receipt line's own purchase-order line total (GoodsReceivingHistoryItemResponse's header).</summary>
        Public Async Function GetGoodsReceivingHistoryAsync(
            fromDate As String, toDate As String, fromUtc As DateTime?, toUtcExclusive As DateTime?,
            sortDescending As Boolean, page As Integer, pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Items As IReadOnlyList(Of GoodsReceivingHistoryItemResponse), TotalCount As Integer))

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim result =
                    Await ReportRepository.GetGoodsReceivingHistoryAsync(
                        connection, fromUtc, toUtcExclusive, sortDescending, page, pageSize, cancellationToken).ConfigureAwait(False)

                Dim items = result.Items.Select(
                    Function(r) New GoodsReceivingHistoryItemResponse With {
                        .ReceiptId = r.ReceiptId,
                        .ReceiptLineId = r.ReceiptLineId,
                        .ReferenceNumber = r.ReferenceNumber,
                        .PurchaseOrderId = r.PurchaseOrderId,
                        .OrderNumber = r.OrderNumber,
                        .SupplierId = r.SupplierId,
                        .SupplierName = r.SupplierName,
                        .ReceivedAtUtc = r.ReceivedAtUtc,
                        .ProductId = r.ProductId,
                        .ProductSku = r.ProductSku,
                        .ProductName = r.ProductName,
                        .OrderedQuantity = r.OrderedQuantity,
                        .ReceivedQuantity = r.ReceivedQuantity,
                        .ReceivedByUserId = r.ReceivedByUserId,
                        .ReceivedByUsername = r.ReceivedByUsername
                    }).ToList()

                Return (Items:=CType(items, IReadOnlyList(Of GoodsReceivingHistoryItemResponse)), TotalCount:=result.TotalCount)

            End Using

        End Function

        ''' <summary>Spec section 14 row 8. Returns treatment: Excluded (docs/report-specification.md section 4 row 8). StockStatus is derived here, not stored - see CurrentStockReportItemResponse's header for the rule.</summary>
        Public Async Function GetCurrentStockReportAsync(
            includeInactive As Boolean,
            sortField As CurrentStockReportSortField, sortDescending As Boolean, page As Integer, pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Items As IReadOnlyList(Of CurrentStockReportItemResponse), TotalCount As Integer))

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim result =
                    Await ReportRepository.GetCurrentStockReportAsync(
                        connection, includeInactive, sortField, sortDescending, page, pageSize, cancellationToken).ConfigureAwait(False)

                Dim items = result.Items.Select(
                    Function(r) New CurrentStockReportItemResponse With {
                        .ProductId = r.ProductId,
                        .Sku = r.Sku,
                        .Name = r.Name,
                        .CategoryId = r.CategoryId,
                        .CategoryName = r.CategoryName,
                        .IsActive = r.IsActive,
                        .Quantity = r.Quantity,
                        .ReorderLevel = r.ReorderLevel,
                        .StockStatus = DeriveStockStatus(r.Quantity, r.ReorderLevel)
                    }).ToList()

                Return (Items:=CType(items, IReadOnlyList(Of CurrentStockReportItemResponse)), TotalCount:=result.TotalCount)

            End Using

        End Function

        ''' <summary>
        ''' Spec section 14 row 9. Returns treatment: Excluded (docs/report-specification.md
        ''' section 4 row 9). CALLS StockRepository.SearchLowStockAsync
        ''' DIRECTLY - P4-11's own threshold comparison, never restated here
        ''' (tasks.md P6-05's own Done-when box). LowStockItemResponse is
        ''' returned as-is, the same reuse LowStockReportResponse's header
        ''' explains.
        ''' </summary>
        Public Async Function GetLowStockReportAsync(
            sortField As StockSortField, sortDescending As Boolean, page As Integer, pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Items As IReadOnlyList(Of LowStockItemResponse), TotalCount As Integer))

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim result =
                    Await StockRepository.SearchLowStockAsync(
                        connection, sortField, sortDescending, page, pageSize, cancellationToken).ConfigureAwait(False)

                Dim items = result.Items.Select(
                    Function(r) New LowStockItemResponse With {
                        .ProductId = r.ProductId,
                        .Sku = r.Sku,
                        .Name = r.Name,
                        .Quantity = r.Quantity,
                        .ReorderLevel = r.ReorderLevel
                    }).ToList()

                Return (Items:=CType(items, IReadOnlyList(Of LowStockItemResponse)), TotalCount:=result.TotalCount)

            End Using

        End Function

        ''' <summary>Spec section 14 row 10. Returns treatment: Included (docs/report-specification.md section 4 row 10). <paramref name="productId"/> Nothing means every product.</summary>
        Public Async Function GetStockMovementReportAsync(
            productId As Integer?, fromUtc As DateTime?, toUtcExclusive As DateTime?,
            sortDescending As Boolean, page As Integer, pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Items As IReadOnlyList(Of StockMovementReportItemResponse), TotalCount As Integer))

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim result =
                    Await ReportRepository.GetStockMovementReportAsync(
                        connection, productId, fromUtc, toUtcExclusive, sortDescending, page, pageSize, cancellationToken).ConfigureAwait(False)

                Dim items = result.Items.Select(
                    Function(r) New StockMovementReportItemResponse With {
                        .Id = r.Id,
                        .ProductId = r.ProductId,
                        .ProductSku = r.ProductSku,
                        .ProductName = r.ProductName,
                        .Delta = r.Delta,
                        .QuantityBefore = r.QuantityBefore,
                        .QuantityAfter = r.QuantityAfter,
                        .Reason = r.Reason,
                        .ActorUserId = r.ActorUserId,
                        .ActorUsername = r.ActorUsername,
                        .CorrelationId = r.CorrelationId,
                        .CreatedAtUtc = r.CreatedAtUtc
                    }).ToList()

                Return (Items:=CType(items, IReadOnlyList(Of StockMovementReportItemResponse)), TotalCount:=result.TotalCount)

            End Using

        End Function

        ''' <summary>Spec section 14 row 11. Returns treatment: Excluded (docs/report-specification.md section 4 row 11). StockEffectApplied is derived here - True only when Status = "Applied" (StockAdjustmentReportItemResponse's header).</summary>
        Public Async Function GetStockAdjustmentReportAsync(
            productId As Integer?, fromUtc As DateTime?, toUtcExclusive As DateTime?,
            sortDescending As Boolean, page As Integer, pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Items As IReadOnlyList(Of StockAdjustmentReportItemResponse), TotalCount As Integer))

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim result =
                    Await ReportRepository.GetStockAdjustmentReportAsync(
                        connection, productId, fromUtc, toUtcExclusive, sortDescending, page, pageSize, cancellationToken).ConfigureAwait(False)

                Dim items = result.Items.Select(
                    Function(r) New StockAdjustmentReportItemResponse With {
                        .Id = r.Id,
                        .ProductId = r.ProductId,
                        .ProductSku = r.ProductSku,
                        .ProductName = r.ProductName,
                        .QuantityVariance = r.QuantityVariance,
                        .Reason = r.Reason,
                        .RequestedByUserId = r.RequestedByUserId,
                        .RequestedByUsername = r.RequestedByUsername,
                        .ApprovedByUserId = r.ApprovedByUserId,
                        .ApprovedByUsername = r.ApprovedByUsername,
                        .ExceedsThreshold = r.ExceedsThreshold,
                        .Status = r.Status,
                        .StockEffectApplied = String.Equals(r.Status, "Applied", StringComparison.Ordinal),
                        .CreatedAtUtc = r.CreatedAtUtc,
                        .UpdatedAtUtc = r.UpdatedAtUtc
                    }).ToList()

                Return (Items:=CType(items, IReadOnlyList(Of StockAdjustmentReportItemResponse)), TotalCount:=result.TotalCount)

            End Using

        End Function

        ''' <summary>"OutOfStock" (Quantity &lt;= 0), "Low" (Quantity &lt;= ReorderLevel - the exact threshold StockRepository.SearchLowStockAsync's own WHERE clause uses), else "Normal".</summary>
        Private Shared Function DeriveStockStatus(quantity As Decimal, reorderLevel As Decimal) As String

            If quantity <= 0D Then
                Return "OutOfStock"
            ElseIf quantity <= reorderLevel Then
                Return "Low"
            Else
                Return "Normal"
            End If

        End Function

        Private Shared Function ToPaymentTotalResponses(
            totals As IReadOnlyList(Of (Method As String, Amount As Decimal))) As IReadOnlyList(Of PaymentMethodTotalResponse)

            Return totals.Select(Function(t) New PaymentMethodTotalResponse With {.Method = t.Method, .Amount = t.Amount}).ToList()

        End Function

    End Class

End Namespace
