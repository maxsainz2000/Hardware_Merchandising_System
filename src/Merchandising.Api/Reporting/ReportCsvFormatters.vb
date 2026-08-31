' Merchandising.Api.Reporting.ReportCsvFormatters
'
' P6-06: the report-specific half of the CSV export feature - one header
' array and one row-building function per spec section 14 report, so every
' controller's own Csv action does nothing but call ReportService, hand the
' result to the matching pair here, then hand THAT to CsvExporter.BuildFile.
' No escaping, no encoding, no filename logic lives in this file - that is
' entirely CsvExporter's job, one file over.
'
' COLUMN ORDER MIRRORS EACH RESPONSE CONTRACT'S OWN JSON PROPERTY ORDER,
' EXCEPT THE Range/PAGING ENVELOPE - a CSV export is the flat row data only;
' Range.FromDate/ToDate/TimeZone/ReturnsTreatment is carried in the export's
' FILENAME (CsvExporter.BuildFileName) and TotalCount/Page/PageSize/Sort
' have no meaning once export has bypassed pagination entirely (P6-06's own
' "export all matching rows" scope decision). PaymentMethodTotalResponse's
' three-row list is flattened into three named columns (CashTotal/
' CardTotal/EWalletTotal) per report, keyed by
' Merchandising.Domain.Sales.PaymentMethod's own enum names rather than
' assumed list order - GetPaymentTotalsAsync's own "always one row per
' method name, COALESCEd to zero" guarantee is what makes the lookup safe.
'
' Friend, not Public - CsvExporterTests.vb (Merchandising.Tests.Unit) reaches
' these through the InternalsVisibleTo grant Merchandising.Api.vbproj
' carries for that assembly (the same seam Program.vb's own header
' describes for Merchandising.Tests.Integration), so every header/row pair
' is unit-tested directly against a hand-built response object - no database,
' no HTTP pipeline, the same "no database needed" shape
' ReportSpecificationDocumentationTests already uses for this phase.

Imports System.Collections.Generic
Imports System.Linq
Imports Merchandising.Contracts.Inventory
Imports Merchandising.Contracts.Reporting
Imports Merchandising.Domain.Sales

Namespace Reporting

    Friend NotInheritable Class ReportCsvFormatters

        Private Sub New()
        End Sub

        ' ------------------------------------------------------- daily sales summary

        Friend Shared Function DailySalesSummaryHeaders() As String()
            Return {"date", "completedSalesCount", "completedSalesTotal", "completedReturnsCount",
                    "completedReturnsTotal", "netSalesTotal", "cashTotal", "cardTotal", "eWalletTotal"}
        End Function

        Friend Shared Function DailySalesSummaryRow(response As DailySalesSummaryResponse) As String()

            Dim totals As IReadOnlyDictionary(Of String, Decimal) = IndexPaymentTotals(response.PaymentTotals)

            Return {
                response.Range.FromDate,
                CsvExporter.FormatInteger(response.CompletedSalesCount),
                CsvExporter.FormatDecimal(response.CompletedSalesTotal),
                CsvExporter.FormatInteger(response.CompletedReturnsCount),
                CsvExporter.FormatDecimal(response.CompletedReturnsTotal),
                CsvExporter.FormatDecimal(response.NetSalesTotal),
                CsvExporter.FormatDecimal(AmountFor(totals, PaymentMethod.Cash)),
                CsvExporter.FormatDecimal(AmountFor(totals, PaymentMethod.Card)),
                CsvExporter.FormatDecimal(AmountFor(totals, PaymentMethod.EWallet))
            }

        End Function

        ' ------------------------------------------------------- payment method summary

        Friend Shared Function PaymentMethodSummaryHeaders() As String()
            Return {"fromDate", "toDate", "cashTotal", "cardTotal", "eWalletTotal", "note"}
        End Function

        Friend Shared Function PaymentMethodSummaryRow(response As PaymentMethodSummaryResponse) As String()

            Dim totals As IReadOnlyDictionary(Of String, Decimal) = IndexPaymentTotals(response.Totals)

            Return {
                response.Range.FromDate,
                response.Range.ToDate,
                CsvExporter.FormatDecimal(AmountFor(totals, PaymentMethod.Cash)),
                CsvExporter.FormatDecimal(AmountFor(totals, PaymentMethod.Card)),
                CsvExporter.FormatDecimal(AmountFor(totals, PaymentMethod.EWallet)),
                response.Note
            }

        End Function

        ' ------------------------------------------------------- sales by product

        Friend Shared Function SalesByProductHeaders() As String()
            Return {"productId", "productSku", "productName", "quantitySold", "quantityReturned",
                    "netQuantity", "grossSalesValue", "capturedCostBasis"}
        End Function

        Friend Shared Function SalesByProductRow(item As SalesByProductItemResponse) As String()
            Return {
                CsvExporter.FormatInteger(item.ProductId),
                item.ProductSku,
                item.ProductName,
                CsvExporter.FormatDecimal(item.QuantitySold),
                CsvExporter.FormatDecimal(item.QuantityReturned),
                CsvExporter.FormatDecimal(item.NetQuantity),
                CsvExporter.FormatDecimal(item.GrossSalesValue),
                CsvExporter.FormatDecimal(item.CapturedCostBasis)
            }
        End Function

        ' ------------------------------------------------------- sales by cashier

        Friend Shared Function SalesByCashierHeaders() As String()
            Return {"cashierUserId", "cashierUsername", "completedSalesCount", "completedSalesTotal",
                    "returnsCount", "returnsTotal", "netValue", "cashTotal", "cardTotal", "eWalletTotal"}
        End Function

        Friend Shared Function SalesByCashierRow(item As SalesByCashierItemResponse) As String()

            Dim totals As IReadOnlyDictionary(Of String, Decimal) = IndexPaymentTotals(item.PaymentTotals)

            Return {
                CsvExporter.FormatInteger(item.CashierUserId),
                item.CashierUsername,
                CsvExporter.FormatInteger(item.CompletedSalesCount),
                CsvExporter.FormatDecimal(item.CompletedSalesTotal),
                CsvExporter.FormatInteger(item.ReturnsCount),
                CsvExporter.FormatDecimal(item.ReturnsTotal),
                CsvExporter.FormatDecimal(item.NetValue),
                CsvExporter.FormatDecimal(AmountFor(totals, PaymentMethod.Cash)),
                CsvExporter.FormatDecimal(AmountFor(totals, PaymentMethod.Card)),
                CsvExporter.FormatDecimal(AmountFor(totals, PaymentMethod.EWallet))
            }

        End Function

        ' ------------------------------------------------------- returns and cancellations

        Friend Shared Function ReturnsAndCancellationsHeaders() As String()
            Return {"salesReturnId", "salesReturnLineId", "saleId", "productId", "productSku", "productName",
                    "quantityReturned", "reason", "returnedByUserId", "returnedByUsername", "approvedByUserId",
                    "approvedByUsername", "status", "restocksItem", "returnedAtUtc"}
        End Function

        Friend Shared Function ReturnsAndCancellationsRow(item As ReturnsAndCancellationsItemResponse) As String()
            Return {
                CsvExporter.FormatInteger(item.SalesReturnId),
                CsvExporter.FormatInteger(item.SalesReturnLineId),
                CsvExporter.FormatInteger(item.SaleId),
                CsvExporter.FormatInteger(item.ProductId),
                item.ProductSku,
                item.ProductName,
                CsvExporter.FormatDecimal(item.QuantityReturned),
                item.Reason,
                CsvExporter.FormatInteger(item.ReturnedByUserId),
                item.ReturnedByUsername,
                CsvExporter.FormatNullableInteger(item.ApprovedByUserId),
                item.ApprovedByUsername,
                item.Status,
                CsvExporter.FormatBoolean(item.RestocksItem),
                CsvExporter.FormatUtcDateTime(item.ReturnedAtUtc)
            }
        End Function

        ' ------------------------------------------------------- product performance

        Friend Shared Function ProductPerformanceHeaders() As String()
            Return {"productId", "productSku", "productName", "quantitySold", "quantityReturned", "netQuantity",
                    "netSalesValue", "recordedCostEstimate", "marginEstimate", "marginEstimateLabel", "currentStockQuantity"}
        End Function

        Friend Shared Function ProductPerformanceRow(item As ProductPerformanceItemResponse) As String()
            Return {
                CsvExporter.FormatInteger(item.ProductId),
                item.ProductSku,
                item.ProductName,
                CsvExporter.FormatDecimal(item.QuantitySold),
                CsvExporter.FormatDecimal(item.QuantityReturned),
                CsvExporter.FormatDecimal(item.NetQuantity),
                CsvExporter.FormatDecimal(item.NetSalesValue),
                CsvExporter.FormatDecimal(item.RecordedCostEstimate),
                CsvExporter.FormatDecimal(item.MarginEstimate),
                item.MarginEstimateLabelText,
                CsvExporter.FormatDecimal(item.CurrentStockQuantity)
            }
        End Function

        ' ------------------------------------------------------- purchase-order history

        Friend Shared Function PurchaseOrderHistoryHeaders() As String()
            Return {"id", "orderNumber", "supplierId", "supplierName", "status", "createdAtUtc",
                    "orderedQuantity", "orderedValue", "receivedQuantity", "receivedValue", "outstandingQuantity"}
        End Function

        Friend Shared Function PurchaseOrderHistoryRow(item As PurchaseOrderHistoryReportItemResponse) As String()
            Return {
                CsvExporter.FormatInteger(item.Id),
                item.OrderNumber,
                CsvExporter.FormatInteger(item.SupplierId),
                item.SupplierName,
                item.Status,
                CsvExporter.FormatUtcDateTime(item.CreatedAtUtc),
                CsvExporter.FormatDecimal(item.OrderedQuantity),
                CsvExporter.FormatDecimal(item.OrderedValue),
                CsvExporter.FormatDecimal(item.ReceivedQuantity),
                CsvExporter.FormatDecimal(item.ReceivedValue),
                CsvExporter.FormatDecimal(item.OutstandingQuantity)
            }
        End Function

        ' ------------------------------------------------------- goods-receiving history

        Friend Shared Function GoodsReceivingHistoryHeaders() As String()
            Return {"receiptId", "receiptLineId", "referenceNumber", "purchaseOrderId", "orderNumber", "supplierId",
                    "supplierName", "receivedAtUtc", "productId", "productSku", "productName", "orderedQuantity",
                    "receivedQuantity", "receivedByUserId", "receivedByUsername"}
        End Function

        Friend Shared Function GoodsReceivingHistoryRow(item As GoodsReceivingHistoryItemResponse) As String()
            Return {
                CsvExporter.FormatInteger(item.ReceiptId),
                CsvExporter.FormatInteger(item.ReceiptLineId),
                item.ReferenceNumber,
                CsvExporter.FormatInteger(item.PurchaseOrderId),
                item.OrderNumber,
                CsvExporter.FormatInteger(item.SupplierId),
                item.SupplierName,
                CsvExporter.FormatUtcDateTime(item.ReceivedAtUtc),
                CsvExporter.FormatInteger(item.ProductId),
                item.ProductSku,
                item.ProductName,
                CsvExporter.FormatDecimal(item.OrderedQuantity),
                CsvExporter.FormatDecimal(item.ReceivedQuantity),
                CsvExporter.FormatInteger(item.ReceivedByUserId),
                item.ReceivedByUsername
            }
        End Function

        ' ------------------------------------------------------- current stock

        Friend Shared Function CurrentStockHeaders() As String()
            Return {"productId", "sku", "name", "categoryId", "categoryName", "isActive",
                    "quantity", "reorderLevel", "stockStatus"}
        End Function

        Friend Shared Function CurrentStockRow(item As CurrentStockReportItemResponse) As String()
            Return {
                CsvExporter.FormatInteger(item.ProductId),
                item.Sku,
                item.Name,
                CsvExporter.FormatNullableInteger(item.CategoryId),
                item.CategoryName,
                CsvExporter.FormatBoolean(item.IsActive),
                CsvExporter.FormatDecimal(item.Quantity),
                CsvExporter.FormatDecimal(item.ReorderLevel),
                item.StockStatus
            }
        End Function

        ' ------------------------------------------------------- low stock

        Friend Shared Function LowStockHeaders() As String()
            Return {"productId", "sku", "name", "quantity", "reorderLevel"}
        End Function

        Friend Shared Function LowStockRow(item As LowStockItemResponse) As String()
            Return {
                CsvExporter.FormatInteger(item.ProductId),
                item.Sku,
                item.Name,
                CsvExporter.FormatDecimal(item.Quantity),
                CsvExporter.FormatDecimal(item.ReorderLevel)
            }
        End Function

        ' ------------------------------------------------------- stock movements

        Friend Shared Function StockMovementHeaders() As String()
            Return {"id", "productId", "productSku", "productName", "delta", "quantityBefore", "quantityAfter",
                    "reason", "actorUserId", "actorUsername", "correlationId", "createdAtUtc"}
        End Function

        Friend Shared Function StockMovementRow(item As StockMovementReportItemResponse) As String()
            Return {
                CsvExporter.FormatInteger(item.Id),
                CsvExporter.FormatInteger(item.ProductId),
                item.ProductSku,
                item.ProductName,
                CsvExporter.FormatDecimal(item.Delta),
                CsvExporter.FormatDecimal(item.QuantityBefore),
                CsvExporter.FormatDecimal(item.QuantityAfter),
                item.Reason,
                CsvExporter.FormatInteger(item.ActorUserId),
                item.ActorUsername,
                item.CorrelationId,
                CsvExporter.FormatUtcDateTime(item.CreatedAtUtc)
            }
        End Function

        ' ------------------------------------------------------- stock adjustments

        Friend Shared Function StockAdjustmentHeaders() As String()
            Return {"id", "productId", "productSku", "productName", "quantityVariance", "reason",
                    "requestedByUserId", "requestedByUsername", "approvedByUserId", "approvedByUsername",
                    "exceedsThreshold", "status", "stockEffectApplied", "createdAtUtc", "updatedAtUtc"}
        End Function

        Friend Shared Function StockAdjustmentRow(item As StockAdjustmentReportItemResponse) As String()
            Return {
                CsvExporter.FormatInteger(item.Id),
                CsvExporter.FormatInteger(item.ProductId),
                item.ProductSku,
                item.ProductName,
                CsvExporter.FormatDecimal(item.QuantityVariance),
                item.Reason,
                CsvExporter.FormatInteger(item.RequestedByUserId),
                item.RequestedByUsername,
                CsvExporter.FormatNullableInteger(item.ApprovedByUserId),
                item.ApprovedByUsername,
                CsvExporter.FormatBoolean(item.ExceedsThreshold),
                item.Status,
                CsvExporter.FormatBoolean(item.StockEffectApplied),
                CsvExporter.FormatUtcDateTime(item.CreatedAtUtc),
                CsvExporter.FormatUtcDateTime(item.UpdatedAtUtc)
            }
        End Function

        ' ------------------------------------------------------------- shared

        ''' <summary>
        ''' GetPaymentTotalsAsync's own guarantee - always exactly one row
        ''' per <see cref="PaymentMethod"/> name, COALESCEd to zero - is what
        ''' makes this a safe direct index rather than a defensive lookup.
        ''' </summary>
        Private Shared Function IndexPaymentTotals(
            totals As IReadOnlyList(Of PaymentMethodTotalResponse)) As IReadOnlyDictionary(Of String, Decimal)

            Return totals.ToDictionary(Function(t) t.Method, Function(t) t.Amount, StringComparer.Ordinal)

        End Function

        Private Shared Function AmountFor(
            totals As IReadOnlyDictionary(Of String, Decimal), method As PaymentMethod) As Decimal

            Dim amount As Decimal = 0D
            totals.TryGetValue(method.ToString(), amount)
            Return amount

        End Function

    End Class

End Namespace
