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

        Private Shared Function ToPaymentTotalResponses(
            totals As IReadOnlyList(Of (Method As String, Amount As Decimal))) As IReadOnlyList(Of PaymentMethodTotalResponse)

            Return totals.Select(Function(t) New PaymentMethodTotalResponse With {.Method = t.Method, .Amount = t.Amount}).ToList()

        End Function

    End Class

End Namespace
