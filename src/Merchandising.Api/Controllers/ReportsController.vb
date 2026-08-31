' Merchandising.Api.Controllers.ReportsController
'
' P6-02: spec section 14's first four reports - the ones that carry money.
' Gated by Reports.View (Admin-and-above, PolicyRegistry, pre-registered at
' P2-02 with no live route until this card - docs/report-specification.md
' section 8).
'
'   GET /api/v1/reports/sales/daily-summary  - spec section 14 row 1. Takes
'                     a single REQUIRED `date` (not a fromDate/toDate range) -
'                     spec's own wording is "for the selected store-local
'                     day", singular, unlike every other report in this file.
'   GET /api/v1/reports/sales/by-product     - spec section 14 row 2.
'   GET /api/v1/reports/sales/by-cashier     - spec section 14 row 3.
'   GET /api/v1/reports/sales/payment-methods - spec section 14 row 4, G-24.
'   GET /api/v1/reports/sales/returns        - P6-03: spec section 14 row 5,
'                     the returns-and-cancellations report. Every status
'                     appears (ReturnsAndCancellationsItemResponse's header).
'   GET /api/v1/reports/sales/product-performance - P6-03: spec section 14
'                     row 12. Its CurrentStockQuantity is live, never scoped
'                     to fromDate/toDate - ProductPerformanceItemResponse's
'                     own header names this "the mixed-temporality trap."
'
' WHAT THIS CONTROLLER OWNS, AND WHAT IT DELIBERATELY DOES NOT - the same
' division PurchaseOrdersController's header states. It owns HTTP shape:
' parsing/clamping query parameters, refusing a malformed request with
' field-level detail (ADR-014), and converting a parsed store-local date to
' the UTC window ReportService needs (Merchandising.Domain.StoreTimeZone,
' docs/report-specification.md section 2). It owns NO report arithmetic -
' every sum, every returns-netting decision, every captured-cost read lives
' in ReportRepository, one layer down.

Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Threading.Tasks
Imports Merchandising.Api.Middleware
Imports Merchandising.Api.Reporting
Imports Merchandising.Api.Security
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Reporting
Imports Merchandising.Domain
Imports Merchandising.Domain.Security
Imports Merchandising.Infrastructure.Data
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Mvc

Namespace Controllers

    <ApiController>
    <Route("api/v1/reports/sales")>
    <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.ReportsView)>
    Public Class ReportsController
        Inherits ControllerBase

        Private Const DefaultPageSize As Integer = 25
        Private Const MaxPageSize As Integer = 100

        ''' <summary>P6-06: a CSV export returns every row matching the caller's filters, never just the on-screen page - the whole reason an export exists. Page 1 at this size never overflows the repository's (page - 1) * pageSize offset arithmetic, because page is always 1 here.</summary>
        Private Const ExportPageSize As Integer = Integer.MaxValue

        Private ReadOnly _reportService As ReportService

        Public Sub New(reportService As ReportService)
            _reportService = reportService
        End Sub

        ''' <summary>Spec section 14 row 1 - a single store-local day, required.</summary>
        <HttpGet("daily-summary")>
        Public Async Function GetDailySalesSummary(<FromQuery(Name:="date")> Optional date_ As String = Nothing) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            Dim parsedDate As DateOnly = Nothing

            If String.IsNullOrWhiteSpace(date_) Then
                fieldErrors("date") = {"A date is required, in yyyy-MM-dd form, interpreted in the store time zone."}
            ElseIf Not DateOnly.TryParseExact(date_, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, parsedDate) Then
                fieldErrors("date") = {"date must be a calendar date in yyyy-MM-dd form, interpreted in the store time zone."}
            End If

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim fromUtc As DateTime = StoreTimeZone.StartOfDayUtc(parsedDate)
            Dim toUtcExclusive As DateTime = StoreTimeZone.EndOfDayUtcExclusive(parsedDate)

            Dim response As DailySalesSummaryResponse =
                Await _reportService.GetDailySalesSummaryAsync(
                    parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), fromUtc, toUtcExclusive,
                    HttpContext.RequestAborted)

            Return Ok(response)

        End Function

        ''' <summary>P6-06: same route/validation as <see cref="GetDailySalesSummary"/>, exported as a single-row CSV. Reports.View gates this route through the same class-level &lt;Authorize&gt; every action in this controller carries - export permission equals view permission by construction (docs/report-specification.md section 8, ADR-024).</summary>
        <HttpGet("daily-summary/csv")>
        Public Async Function GetDailySalesSummaryCsv(<FromQuery(Name:="date")> Optional date_ As String = Nothing) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            Dim parsedDate As DateOnly = Nothing

            If String.IsNullOrWhiteSpace(date_) Then
                fieldErrors("date") = {"A date is required, in yyyy-MM-dd form, interpreted in the store time zone."}
            ElseIf Not DateOnly.TryParseExact(date_, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, parsedDate) Then
                fieldErrors("date") = {"date must be a calendar date in yyyy-MM-dd form, interpreted in the store time zone."}
            End If

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim fromUtc As DateTime = StoreTimeZone.StartOfDayUtc(parsedDate)
            Dim toUtcExclusive As DateTime = StoreTimeZone.EndOfDayUtcExclusive(parsedDate)

            Dim response As DailySalesSummaryResponse =
                Await _reportService.GetDailySalesSummaryAsync(
                    parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), fromUtc, toUtcExclusive,
                    HttpContext.RequestAborted)

            Dim bytes As Byte() = CsvExporter.BuildFile(
                ReportCsvFormatters.DailySalesSummaryHeaders(),
                New IReadOnlyList(Of String)() {ReportCsvFormatters.DailySalesSummaryRow(response)})

            Dim fileName As String = CsvExporter.BuildFileName("daily-sales-summary", response.Range.FromDate, response.Range.ToDate)

            Return File(bytes, CsvExporter.ContentType, fileName)

        End Function

        ''' <summary>Spec section 14 row 4 - G-24's own report row.</summary>
        <HttpGet("payment-methods")>
        Public Async Function GetPaymentMethodSummary(
            <FromQuery> Optional fromDate As String = Nothing,
            <FromQuery> Optional toDate As String = Nothing) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            Dim range As ParsedDateRange = ParseDateRange(fromDate, toDate, fieldErrors)

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim response As PaymentMethodSummaryResponse =
                Await _reportService.GetPaymentMethodSummaryAsync(
                    range.FromDateText, range.ToDateText, range.FromUtc, range.ToUtcExclusive, HttpContext.RequestAborted)

            Return Ok(response)

        End Function

        ''' <summary>P6-06: same route/validation as <see cref="GetPaymentMethodSummary"/>, exported as a single-row CSV.</summary>
        <HttpGet("payment-methods/csv")>
        Public Async Function GetPaymentMethodSummaryCsv(
            <FromQuery> Optional fromDate As String = Nothing,
            <FromQuery> Optional toDate As String = Nothing) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            Dim range As ParsedDateRange = ParseDateRange(fromDate, toDate, fieldErrors)

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim response As PaymentMethodSummaryResponse =
                Await _reportService.GetPaymentMethodSummaryAsync(
                    range.FromDateText, range.ToDateText, range.FromUtc, range.ToUtcExclusive, HttpContext.RequestAborted)

            Dim bytes As Byte() = CsvExporter.BuildFile(
                ReportCsvFormatters.PaymentMethodSummaryHeaders(),
                New IReadOnlyList(Of String)() {ReportCsvFormatters.PaymentMethodSummaryRow(response)})

            Dim fileName As String = CsvExporter.BuildFileName("payment-method-summary", range.FromDateText, range.ToDateText)

            Return File(bytes, CsvExporter.ContentType, fileName)

        End Function

        ''' <summary>Spec section 14 row 2.</summary>
        <HttpGet("by-product")>
        Public Async Function GetSalesByProduct(
            <FromQuery> Optional fromDate As String = Nothing,
            <FromQuery> Optional toDate As String = Nothing,
            <FromQuery> Optional sort As String = Nothing,
            <FromQuery> Optional page As Integer = 1,
            <FromQuery> Optional pageSize As Integer = DefaultPageSize) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            Dim range As ParsedDateRange = ParseDateRange(fromDate, toDate, fieldErrors)

            Dim sortField As SalesByProductSortField = SalesByProductSortField.ProductName
            Dim sortDescending As Boolean = False

            If Not String.IsNullOrWhiteSpace(sort) Then
                If Not TryParseSalesByProductSort(sort, sortField, sortDescending) Then
                    fieldErrors("sort") = {"Unsupported sort. Use one of productName, quantitySold, grossSalesValue, optionally suffixed with ':asc' or ':desc'."}
                End If
            End If

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim effectivePage As Integer = If(page < 1, 1, page)
            Dim effectivePageSize As Integer = If(pageSize < 1, DefaultPageSize, Math.Min(pageSize, MaxPageSize))

            Dim result =
                Await _reportService.GetSalesByProductAsync(
                    range.FromDateText, range.ToDateText, range.FromUtc, range.ToUtcExclusive,
                    sortField, sortDescending, effectivePage, effectivePageSize, HttpContext.RequestAborted)

            Return Ok(New SalesByProductResponse With {
                .Range = New ReportRangeEnvelope With {
                    .FromDate = range.FromDateText, .ToDate = range.ToDateText, .TimeZone = StoreTimeZone.IanaId,
                    .ReturnsTreatment = NameOf(Merchandising.Domain.Reporting.ReturnsTreatment.Included)
                },
                .Items = result.Items,
                .TotalCount = result.TotalCount,
                .Page = effectivePage,
                .PageSize = effectivePageSize,
                .MaxPageSize = MaxPageSize,
                .Sort = CanonicalSalesByProductSort(sortField, sortDescending)
            })

        End Function

        ''' <summary>P6-06: same route/validation as <see cref="GetSalesByProduct"/>, exported as CSV - every matching row, not just page 1's on-screen slice.</summary>
        <HttpGet("by-product/csv")>
        Public Async Function GetSalesByProductCsv(
            <FromQuery> Optional fromDate As String = Nothing,
            <FromQuery> Optional toDate As String = Nothing,
            <FromQuery> Optional sort As String = Nothing) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            Dim range As ParsedDateRange = ParseDateRange(fromDate, toDate, fieldErrors)

            Dim sortField As SalesByProductSortField = SalesByProductSortField.ProductName
            Dim sortDescending As Boolean = False

            If Not String.IsNullOrWhiteSpace(sort) Then
                If Not TryParseSalesByProductSort(sort, sortField, sortDescending) Then
                    fieldErrors("sort") = {"Unsupported sort. Use one of productName, quantitySold, grossSalesValue, optionally suffixed with ':asc' or ':desc'."}
                End If
            End If

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim result =
                Await _reportService.GetSalesByProductAsync(
                    range.FromDateText, range.ToDateText, range.FromUtc, range.ToUtcExclusive,
                    sortField, sortDescending, 1, ExportPageSize, HttpContext.RequestAborted)

            Dim rows As IReadOnlyList(Of String)() =
                result.Items.Select(Function(item) ReportCsvFormatters.SalesByProductRow(item)).ToArray()

            Dim bytes As Byte() = CsvExporter.BuildFile(ReportCsvFormatters.SalesByProductHeaders(), rows)
            Dim fileName As String = CsvExporter.BuildFileName("sales-by-product", range.FromDateText, range.ToDateText)

            Return File(bytes, CsvExporter.ContentType, fileName)

        End Function

        ''' <summary>Spec section 14 row 3.</summary>
        <HttpGet("by-cashier")>
        Public Async Function GetSalesByCashier(
            <FromQuery> Optional fromDate As String = Nothing,
            <FromQuery> Optional toDate As String = Nothing,
            <FromQuery> Optional sort As String = Nothing,
            <FromQuery> Optional page As Integer = 1,
            <FromQuery> Optional pageSize As Integer = DefaultPageSize) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            Dim range As ParsedDateRange = ParseDateRange(fromDate, toDate, fieldErrors)

            Dim sortField As SalesByCashierSortField = SalesByCashierSortField.CashierUsername
            Dim sortDescending As Boolean = False

            If Not String.IsNullOrWhiteSpace(sort) Then
                If Not TryParseSalesByCashierSort(sort, sortField, sortDescending) Then
                    fieldErrors("sort") = {"Unsupported sort. Use one of cashierUsername, completedSalesTotal, optionally suffixed with ':asc' or ':desc'."}
                End If
            End If

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim effectivePage As Integer = If(page < 1, 1, page)
            Dim effectivePageSize As Integer = If(pageSize < 1, DefaultPageSize, Math.Min(pageSize, MaxPageSize))

            Dim result =
                Await _reportService.GetSalesByCashierAsync(
                    range.FromDateText, range.ToDateText, range.FromUtc, range.ToUtcExclusive,
                    sortField, sortDescending, effectivePage, effectivePageSize, HttpContext.RequestAborted)

            Return Ok(New SalesByCashierResponse With {
                .Range = New ReportRangeEnvelope With {
                    .FromDate = range.FromDateText, .ToDate = range.ToDateText, .TimeZone = StoreTimeZone.IanaId,
                    .ReturnsTreatment = NameOf(Merchandising.Domain.Reporting.ReturnsTreatment.Included)
                },
                .Items = result.Items,
                .TotalCount = result.TotalCount,
                .Page = effectivePage,
                .PageSize = effectivePageSize,
                .MaxPageSize = MaxPageSize,
                .Sort = CanonicalSalesByCashierSort(sortField, sortDescending)
            })

        End Function

        ''' <summary>P6-06: same route/validation as <see cref="GetSalesByCashier"/>, exported as CSV - every matching row.</summary>
        <HttpGet("by-cashier/csv")>
        Public Async Function GetSalesByCashierCsv(
            <FromQuery> Optional fromDate As String = Nothing,
            <FromQuery> Optional toDate As String = Nothing,
            <FromQuery> Optional sort As String = Nothing) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            Dim range As ParsedDateRange = ParseDateRange(fromDate, toDate, fieldErrors)

            Dim sortField As SalesByCashierSortField = SalesByCashierSortField.CashierUsername
            Dim sortDescending As Boolean = False

            If Not String.IsNullOrWhiteSpace(sort) Then
                If Not TryParseSalesByCashierSort(sort, sortField, sortDescending) Then
                    fieldErrors("sort") = {"Unsupported sort. Use one of cashierUsername, completedSalesTotal, optionally suffixed with ':asc' or ':desc'."}
                End If
            End If

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim result =
                Await _reportService.GetSalesByCashierAsync(
                    range.FromDateText, range.ToDateText, range.FromUtc, range.ToUtcExclusive,
                    sortField, sortDescending, 1, ExportPageSize, HttpContext.RequestAborted)

            Dim rows As IReadOnlyList(Of String)() =
                result.Items.Select(Function(item) ReportCsvFormatters.SalesByCashierRow(item)).ToArray()

            Dim bytes As Byte() = CsvExporter.BuildFile(ReportCsvFormatters.SalesByCashierHeaders(), rows)
            Dim fileName As String = CsvExporter.BuildFileName("sales-by-cashier", range.FromDateText, range.ToDateText)

            Return File(bytes, CsvExporter.ContentType, fileName)

        End Function

        ''' <summary>Spec section 14 row 5.</summary>
        <HttpGet("returns")>
        Public Async Function GetReturnsAndCancellations(
            <FromQuery> Optional fromDate As String = Nothing,
            <FromQuery> Optional toDate As String = Nothing,
            <FromQuery> Optional sort As String = Nothing,
            <FromQuery> Optional page As Integer = 1,
            <FromQuery> Optional pageSize As Integer = DefaultPageSize) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            Dim range As ParsedDateRange = ParseDateRange(fromDate, toDate, fieldErrors)

            Dim sortDescending As Boolean = True

            If Not String.IsNullOrWhiteSpace(sort) Then
                If Not TryParseReturnedAtSort(sort, sortDescending) Then
                    fieldErrors("sort") = {"Unsupported sort. Use returnedAt, optionally suffixed with ':asc' or ':desc'."}
                End If
            End If

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim effectivePage As Integer = If(page < 1, 1, page)
            Dim effectivePageSize As Integer = If(pageSize < 1, DefaultPageSize, Math.Min(pageSize, MaxPageSize))

            Dim result =
                Await _reportService.GetReturnsAndCancellationsAsync(
                    range.FromDateText, range.ToDateText, range.FromUtc, range.ToUtcExclusive,
                    sortDescending, effectivePage, effectivePageSize, HttpContext.RequestAborted)

            Return Ok(New ReturnsAndCancellationsResponse With {
                .Range = New ReportRangeEnvelope With {
                    .FromDate = range.FromDateText, .ToDate = range.ToDateText, .TimeZone = StoreTimeZone.IanaId,
                    .ReturnsTreatment = NameOf(Merchandising.Domain.Reporting.ReturnsTreatment.Included)
                },
                .Items = result.Items,
                .TotalCount = result.TotalCount,
                .Page = effectivePage,
                .PageSize = effectivePageSize,
                .MaxPageSize = MaxPageSize,
                .Sort = "returnedAt" & If(sortDescending, ":desc", ":asc")
            })

        End Function

        ''' <summary>P6-06: same route/validation as <see cref="GetReturnsAndCancellations"/>, exported as CSV - every matching row.</summary>
        <HttpGet("returns/csv")>
        Public Async Function GetReturnsAndCancellationsCsv(
            <FromQuery> Optional fromDate As String = Nothing,
            <FromQuery> Optional toDate As String = Nothing,
            <FromQuery> Optional sort As String = Nothing) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            Dim range As ParsedDateRange = ParseDateRange(fromDate, toDate, fieldErrors)

            Dim sortDescending As Boolean = True

            If Not String.IsNullOrWhiteSpace(sort) Then
                If Not TryParseReturnedAtSort(sort, sortDescending) Then
                    fieldErrors("sort") = {"Unsupported sort. Use returnedAt, optionally suffixed with ':asc' or ':desc'."}
                End If
            End If

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim result =
                Await _reportService.GetReturnsAndCancellationsAsync(
                    range.FromDateText, range.ToDateText, range.FromUtc, range.ToUtcExclusive,
                    sortDescending, 1, ExportPageSize, HttpContext.RequestAborted)

            Dim rows As IReadOnlyList(Of String)() =
                result.Items.Select(Function(item) ReportCsvFormatters.ReturnsAndCancellationsRow(item)).ToArray()

            Dim bytes As Byte() = CsvExporter.BuildFile(ReportCsvFormatters.ReturnsAndCancellationsHeaders(), rows)
            Dim fileName As String = CsvExporter.BuildFileName("returns-and-cancellations", range.FromDateText, range.ToDateText)

            Return File(bytes, CsvExporter.ContentType, fileName)

        End Function

        ''' <summary>Spec section 14 row 12. CurrentStockQuantity in every item is live - never scoped to fromDate/toDate (ProductPerformanceItemResponse's own header).</summary>
        <HttpGet("product-performance")>
        Public Async Function GetProductPerformance(
            <FromQuery> Optional fromDate As String = Nothing,
            <FromQuery> Optional toDate As String = Nothing,
            <FromQuery> Optional sort As String = Nothing,
            <FromQuery> Optional page As Integer = 1,
            <FromQuery> Optional pageSize As Integer = DefaultPageSize) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            Dim range As ParsedDateRange = ParseDateRange(fromDate, toDate, fieldErrors)

            Dim sortField As ProductPerformanceSortField = ProductPerformanceSortField.ProductName
            Dim sortDescending As Boolean = False

            If Not String.IsNullOrWhiteSpace(sort) Then
                If Not TryParseProductPerformanceSort(sort, sortField, sortDescending) Then
                    fieldErrors("sort") = {"Unsupported sort. Use one of productName, netSalesValue, netQuantity, optionally suffixed with ':asc' or ':desc'."}
                End If
            End If

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim effectivePage As Integer = If(page < 1, 1, page)
            Dim effectivePageSize As Integer = If(pageSize < 1, DefaultPageSize, Math.Min(pageSize, MaxPageSize))

            Dim result =
                Await _reportService.GetProductPerformanceAsync(
                    range.FromDateText, range.ToDateText, range.FromUtc, range.ToUtcExclusive,
                    sortField, sortDescending, effectivePage, effectivePageSize, HttpContext.RequestAborted)

            Return Ok(New ProductPerformanceResponse With {
                .Range = New ReportRangeEnvelope With {
                    .FromDate = range.FromDateText, .ToDate = range.ToDateText, .TimeZone = StoreTimeZone.IanaId,
                    .ReturnsTreatment = NameOf(Merchandising.Domain.Reporting.ReturnsTreatment.Included)
                },
                .Items = result.Items,
                .TotalCount = result.TotalCount,
                .Page = effectivePage,
                .PageSize = effectivePageSize,
                .MaxPageSize = MaxPageSize,
                .Sort = CanonicalProductPerformanceSort(sortField, sortDescending)
            })

        End Function

        ''' <summary>P6-06: same route/validation as <see cref="GetProductPerformance"/>, exported as CSV - every matching row. CurrentStockQuantity is still live, same as the JSON route.</summary>
        <HttpGet("product-performance/csv")>
        Public Async Function GetProductPerformanceCsv(
            <FromQuery> Optional fromDate As String = Nothing,
            <FromQuery> Optional toDate As String = Nothing,
            <FromQuery> Optional sort As String = Nothing) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            Dim range As ParsedDateRange = ParseDateRange(fromDate, toDate, fieldErrors)

            Dim sortField As ProductPerformanceSortField = ProductPerformanceSortField.ProductName
            Dim sortDescending As Boolean = False

            If Not String.IsNullOrWhiteSpace(sort) Then
                If Not TryParseProductPerformanceSort(sort, sortField, sortDescending) Then
                    fieldErrors("sort") = {"Unsupported sort. Use one of productName, netSalesValue, netQuantity, optionally suffixed with ':asc' or ':desc'."}
                End If
            End If

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim result =
                Await _reportService.GetProductPerformanceAsync(
                    range.FromDateText, range.ToDateText, range.FromUtc, range.ToUtcExclusive,
                    sortField, sortDescending, 1, ExportPageSize, HttpContext.RequestAborted)

            Dim rows As IReadOnlyList(Of String)() =
                result.Items.Select(Function(item) ReportCsvFormatters.ProductPerformanceRow(item)).ToArray()

            Dim bytes As Byte() = CsvExporter.BuildFile(ReportCsvFormatters.ProductPerformanceHeaders(), rows)
            Dim fileName As String = CsvExporter.BuildFileName("product-performance", range.FromDateText, range.ToDateText)

            Return File(bytes, CsvExporter.ContentType, fileName)

        End Function

        ' --------------------------------------------------------- date range

        Private Structure ParsedDateRange
            Public FromDateText As String
            Public ToDateText As String
            Public FromUtc As DateTime?
            Public ToUtcExclusive As DateTime?
        End Structure

        ''' <summary>
        ''' fromDate/toDate: each optional and independent - omitting one
        ''' leaves that side unbounded, never defaulting to "today"
        ''' (docs/report-specification.md section 3, inherited from P3-06's
        ''' PurchaseOrderHistoryResponse contract). toDate before fromDate is
        ''' refused.
        ''' </summary>
        Private Shared Function ParseDateRange(
            fromDate As String, toDate As String, fieldErrors As Dictionary(Of String, String())) As ParsedDateRange

            Dim fromLocalDate As DateOnly? = Nothing

            If Not String.IsNullOrWhiteSpace(fromDate) Then
                Dim parsedFromDate As DateOnly
                If DateOnly.TryParseExact(fromDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, parsedFromDate) Then
                    fromLocalDate = parsedFromDate
                Else
                    fieldErrors("fromDate") = {"fromDate must be a calendar date in yyyy-MM-dd form, interpreted in the store time zone."}
                End If
            End If

            Dim toLocalDate As DateOnly? = Nothing

            If Not String.IsNullOrWhiteSpace(toDate) Then
                Dim parsedToDate As DateOnly
                If DateOnly.TryParseExact(toDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, parsedToDate) Then
                    toLocalDate = parsedToDate
                Else
                    fieldErrors("toDate") = {"toDate must be a calendar date in yyyy-MM-dd form, interpreted in the store time zone."}
                End If
            End If

            If fromLocalDate.HasValue AndAlso toLocalDate.HasValue AndAlso fromLocalDate.Value > toLocalDate.Value Then
                fieldErrors("toDate") = {"toDate cannot be before fromDate."}
            End If

            Return New ParsedDateRange With {
                .FromDateText = If(fromLocalDate.HasValue, fromLocalDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Nothing),
                .ToDateText = If(toLocalDate.HasValue, toLocalDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Nothing),
                .FromUtc = If(fromLocalDate.HasValue, CType(StoreTimeZone.StartOfDayUtc(fromLocalDate.Value), DateTime?), Nothing),
                .ToUtcExclusive = If(toLocalDate.HasValue, CType(StoreTimeZone.EndOfDayUtcExclusive(toLocalDate.Value), DateTime?), Nothing)
            }

        End Function

        ' ------------------------------------------------------------ sorting

        Private Shared Function TryParseSalesByProductSort(
            value As String, ByRef sortField As SalesByProductSortField, ByRef sortDescending As Boolean) As Boolean

            Dim fieldName As String = Nothing
            Dim descending As Boolean = False

            If Not TrySplitSort(value, fieldName, descending) Then
                Return False
            End If

            If String.Equals(fieldName, "productName", StringComparison.OrdinalIgnoreCase) Then
                sortField = SalesByProductSortField.ProductName
            ElseIf String.Equals(fieldName, "quantitySold", StringComparison.OrdinalIgnoreCase) Then
                sortField = SalesByProductSortField.QuantitySold
            ElseIf String.Equals(fieldName, "grossSalesValue", StringComparison.OrdinalIgnoreCase) Then
                sortField = SalesByProductSortField.GrossSalesValue
            Else
                Return False
            End If

            sortDescending = descending
            Return True

        End Function

        Private Shared Function TryParseSalesByCashierSort(
            value As String, ByRef sortField As SalesByCashierSortField, ByRef sortDescending As Boolean) As Boolean

            Dim fieldName As String = Nothing
            Dim descending As Boolean = False

            If Not TrySplitSort(value, fieldName, descending) Then
                Return False
            End If

            If String.Equals(fieldName, "cashierUsername", StringComparison.OrdinalIgnoreCase) Then
                sortField = SalesByCashierSortField.CashierUsername
            ElseIf String.Equals(fieldName, "completedSalesTotal", StringComparison.OrdinalIgnoreCase) Then
                sortField = SalesByCashierSortField.CompletedSalesTotal
            Else
                Return False
            End If

            sortDescending = descending
            Return True

        End Function

        ''' <summary>Single-field whitelist for the returns-and-cancellations report - the same shape SalesReturnsController.TryParseReturnedAtSort uses for GET /api/v1/sales/returns.</summary>
        Private Shared Function TryParseReturnedAtSort(value As String, ByRef sortDescending As Boolean) As Boolean

            Dim fieldName As String = Nothing
            Dim descending As Boolean = False

            If Not TrySplitSort(value, fieldName, descending) Then
                Return False
            End If

            If Not String.Equals(fieldName, "returnedAt", StringComparison.OrdinalIgnoreCase) Then
                Return False
            End If

            sortDescending = descending
            Return True

        End Function

        Private Shared Function TryParseProductPerformanceSort(
            value As String, ByRef sortField As ProductPerformanceSortField, ByRef sortDescending As Boolean) As Boolean

            Dim fieldName As String = Nothing
            Dim descending As Boolean = False

            If Not TrySplitSort(value, fieldName, descending) Then
                Return False
            End If

            If String.Equals(fieldName, "productName", StringComparison.OrdinalIgnoreCase) Then
                sortField = ProductPerformanceSortField.ProductName
            ElseIf String.Equals(fieldName, "netSalesValue", StringComparison.OrdinalIgnoreCase) Then
                sortField = ProductPerformanceSortField.NetSalesValue
            ElseIf String.Equals(fieldName, "netQuantity", StringComparison.OrdinalIgnoreCase) Then
                sortField = ProductPerformanceSortField.NetQuantity
            Else
                Return False
            End If

            sortDescending = descending
            Return True

        End Function

        ''' <summary>Parses "field" or "field:direction" - the shared half PurchaseOrdersController.TryParseSort also implements, duplicated rather than shared across controllers (this codebase's existing precedent: each controller owns its own whitelist).</summary>
        Private Shared Function TrySplitSort(value As String, ByRef fieldName As String, ByRef descending As Boolean) As Boolean

            Dim parts As String() = value.Split(":"c)

            If parts.Length > 2 Then
                Return False
            End If

            fieldName = parts(0).Trim()
            descending = False

            If parts.Length = 2 Then

                Dim direction As String = parts(1).Trim()

                If String.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase) Then
                    descending = True
                ElseIf Not String.Equals(direction, "asc", StringComparison.OrdinalIgnoreCase) Then
                    Return False
                End If

            End If

            Return True

        End Function

        Private Shared Function CanonicalSalesByProductSort(sortField As SalesByProductSortField, sortDescending As Boolean) As String

            Dim fieldName As String

            Select Case sortField
                Case SalesByProductSortField.QuantitySold
                    fieldName = "quantitySold"
                Case SalesByProductSortField.GrossSalesValue
                    fieldName = "grossSalesValue"
                Case Else
                    fieldName = "productName"
            End Select

            Return fieldName & If(sortDescending, ":desc", ":asc")

        End Function

        Private Shared Function CanonicalSalesByCashierSort(sortField As SalesByCashierSortField, sortDescending As Boolean) As String

            Dim fieldName As String =
                If(sortField = SalesByCashierSortField.CompletedSalesTotal, "completedSalesTotal", "cashierUsername")

            Return fieldName & If(sortDescending, ":desc", ":asc")

        End Function

        Private Shared Function CanonicalProductPerformanceSort(sortField As ProductPerformanceSortField, sortDescending As Boolean) As String

            Dim fieldName As String

            Select Case sortField
                Case ProductPerformanceSortField.NetSalesValue
                    fieldName = "netSalesValue"
                Case ProductPerformanceSortField.NetQuantity
                    fieldName = "netQuantity"
                Case Else
                    fieldName = "productName"
            End Select

            Return fieldName & If(sortDescending, ":desc", ":asc")

        End Function

        ' ------------------------------------------------------------ results

        Private Function ValidationFailed(
            fieldErrors As Dictionary(Of String, String()), correlationId As String) As IActionResult

            Return BadRequest(New ApiErrorResponse With {
                .ErrorCode = "VALIDATION_FAILED",
                .Message = "The report could not be produced because of a validation failure.",
                .CorrelationId = correlationId,
                .Errors = fieldErrors
            })

        End Function

    End Class

End Namespace
