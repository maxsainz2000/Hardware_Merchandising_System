' Merchandising.Api.Controllers.InventoryReportsController
'
' P6-05: spec section 14 rows 8-11, the last four of Track B's twelve
' reports - current stock, low-stock, stock movement, stock-adjustment.
' Gated by Reports.View (Admin-and-above, PolicyRegistry, pre-registered at
' P2-02), the same policy every other Track B report uses.
'
'   GET /api/v1/reports/inventory/current-stock    - spec section 14 row 8.
'                     No date parameters - a point-in-time snapshot
'                     (CurrentStockReportResponse's header). includeInactive
'                     defaults False, the same convention GET /api/v1/inventory/stock
'                     (P4-11) uses.
'   GET /api/v1/reports/inventory/low-stock         - spec section 14 row 9.
'                     No date parameters either. Delegates straight to
'                     StockRepository.SearchLowStockAsync through
'                     ReportService.GetLowStockReportAsync - P4-11's own
'                     threshold comparison, never restated here.
'   GET /api/v1/reports/inventory/stock-movements   - spec section 14 row 10.
'                     productId is OPTIONAL (unlike GET /api/v1/inventory/stock/movements,
'                     which requires it) - see ReportRepository.GetStockMovementReportAsync's
'                     header for why this card's own Done-when box needs that.
'   GET /api/v1/reports/inventory/stock-adjustments - spec section 14 row 11.
'                     The first GET route StockAdjustments has ever had.
'
' WHAT THIS CONTROLLER OWNS, AND WHAT IT DELIBERATELY DOES NOT - the same
' division every other Track B controller's header states. It owns HTTP
' shape only; every join, every derived StockStatus/StockEffectApplied
' value lives one layer down (ReportRepository/ReportService).

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
    <Route("api/v1/reports/inventory")>
    <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.ReportsView)>
    Public Class InventoryReportsController
        Inherits ControllerBase

        Private Const DefaultPageSize As Integer = 25
        Private Const MaxPageSize As Integer = 100

        ''' <summary>P6-06: a CSV export returns every matching row, never just the on-screen page - ReportsController.ExportPageSize's own reasoning.</summary>
        Private Const ExportPageSize As Integer = Integer.MaxValue

        Private ReadOnly _reportService As ReportService

        Public Sub New(reportService As ReportService)
            _reportService = reportService
        End Sub

        ''' <summary>Spec section 14 row 8. No date parameters - a point-in-time snapshot.</summary>
        <HttpGet("current-stock")>
        Public Async Function GetCurrentStockReport(
            <FromQuery> Optional includeInactive As Boolean = False,
            <FromQuery> Optional sort As String = Nothing,
            <FromQuery> Optional page As Integer = 1,
            <FromQuery> Optional pageSize As Integer = DefaultPageSize) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()

            Dim sortField As CurrentStockReportSortField = CurrentStockReportSortField.ProductName
            Dim sortDescending As Boolean = False

            If Not String.IsNullOrWhiteSpace(sort) Then
                If Not TryParseCurrentStockSort(sort, sortField, sortDescending) Then
                    Return ValidationFailed(
                        "sort", "Unsupported sort. Use one of productName, quantity, reorderLevel, optionally suffixed with ':asc' or ':desc'.",
                        correlationId)
                End If
            End If

            Dim effectivePage As Integer = If(page < 1, 1, page)
            Dim effectivePageSize As Integer = If(pageSize < 1, DefaultPageSize, Math.Min(pageSize, MaxPageSize))

            Dim result =
                Await _reportService.GetCurrentStockReportAsync(
                    includeInactive, sortField, sortDescending, effectivePage, effectivePageSize, HttpContext.RequestAborted)

            Return Ok(New CurrentStockReportResponse With {
                .Range = New ReportRangeEnvelope With {
                    .FromDate = Nothing, .ToDate = Nothing, .TimeZone = StoreTimeZone.IanaId,
                    .ReturnsTreatment = NameOf(Merchandising.Domain.Reporting.ReturnsTreatment.Excluded)
                },
                .Items = result.Items,
                .TotalCount = result.TotalCount,
                .Page = effectivePage,
                .PageSize = effectivePageSize,
                .MaxPageSize = MaxPageSize,
                .Sort = CanonicalCurrentStockSort(sortField, sortDescending)
            })

        End Function

        ''' <summary>P6-06: same route/validation as <see cref="GetCurrentStockReport"/>, exported as CSV - every matching row. includeInactive is carried into the export filename since it is the only parameter this point-in-time report has (CsvExporter.BuildFileName's extraSuffix).</summary>
        <HttpGet("current-stock/csv")>
        Public Async Function GetCurrentStockReportCsv(
            <FromQuery> Optional includeInactive As Boolean = False,
            <FromQuery> Optional sort As String = Nothing) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()

            Dim sortField As CurrentStockReportSortField = CurrentStockReportSortField.ProductName
            Dim sortDescending As Boolean = False

            If Not String.IsNullOrWhiteSpace(sort) Then
                If Not TryParseCurrentStockSort(sort, sortField, sortDescending) Then
                    Return ValidationFailed(
                        "sort", "Unsupported sort. Use one of productName, quantity, reorderLevel, optionally suffixed with ':asc' or ':desc'.",
                        correlationId)
                End If
            End If

            Dim result =
                Await _reportService.GetCurrentStockReportAsync(
                    includeInactive, sortField, sortDescending, 1, ExportPageSize, HttpContext.RequestAborted)

            Dim rows As IReadOnlyList(Of String)() =
                result.Items.Select(Function(item) ReportCsvFormatters.CurrentStockRow(item)).ToArray()

            Dim bytes As Byte() = CsvExporter.BuildFile(ReportCsvFormatters.CurrentStockHeaders(), rows)
            Dim fileName As String = CsvExporter.BuildFileName(
                "current-stock", Nothing, Nothing, If(includeInactive, "including-inactive", "active-only"))

            Return File(bytes, CsvExporter.ContentType, fileName)

        End Function

        ''' <summary>Spec section 14 row 9. No date parameters. Reuses P4-11's own StockSortField whitelist and sort parsing.</summary>
        <HttpGet("low-stock")>
        Public Async Function GetLowStockReport(
            <FromQuery> Optional sort As String = Nothing,
            <FromQuery> Optional page As Integer = 1,
            <FromQuery> Optional pageSize As Integer = DefaultPageSize) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()

            Dim sortField As StockSortField = StockSortField.Quantity
            Dim sortDescending As Boolean = False

            If Not String.IsNullOrWhiteSpace(sort) Then
                If Not TryParseStockSort(sort, sortField, sortDescending) Then
                    Return ValidationFailed(
                        "sort", "Unsupported sort. Use one of productName, quantity, reorderLevel, optionally suffixed with ':asc' or ':desc'.",
                        correlationId)
                End If
            End If

            Dim effectivePage As Integer = If(page < 1, 1, page)
            Dim effectivePageSize As Integer = If(pageSize < 1, DefaultPageSize, Math.Min(pageSize, MaxPageSize))

            Dim result =
                Await _reportService.GetLowStockReportAsync(
                    sortField, sortDescending, effectivePage, effectivePageSize, HttpContext.RequestAborted)

            Return Ok(New LowStockReportResponse With {
                .Range = New ReportRangeEnvelope With {
                    .FromDate = Nothing, .ToDate = Nothing, .TimeZone = StoreTimeZone.IanaId,
                    .ReturnsTreatment = NameOf(Merchandising.Domain.Reporting.ReturnsTreatment.Excluded)
                },
                .Items = result.Items,
                .TotalCount = result.TotalCount,
                .Page = effectivePage,
                .PageSize = effectivePageSize,
                .MaxPageSize = MaxPageSize,
                .Sort = CanonicalStockSort(sortField, sortDescending)
            })

        End Function

        ''' <summary>P6-06: same route/validation as <see cref="GetLowStockReport"/>, exported as CSV - every matching row.</summary>
        <HttpGet("low-stock/csv")>
        Public Async Function GetLowStockReportCsv(<FromQuery> Optional sort As String = Nothing) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()

            Dim sortField As StockSortField = StockSortField.Quantity
            Dim sortDescending As Boolean = False

            If Not String.IsNullOrWhiteSpace(sort) Then
                If Not TryParseStockSort(sort, sortField, sortDescending) Then
                    Return ValidationFailed(
                        "sort", "Unsupported sort. Use one of productName, quantity, reorderLevel, optionally suffixed with ':asc' or ':desc'.",
                        correlationId)
                End If
            End If

            Dim result =
                Await _reportService.GetLowStockReportAsync(sortField, sortDescending, 1, ExportPageSize, HttpContext.RequestAborted)

            Dim rows As IReadOnlyList(Of String)() =
                result.Items.Select(Function(item) ReportCsvFormatters.LowStockRow(item)).ToArray()

            Dim bytes As Byte() = CsvExporter.BuildFile(ReportCsvFormatters.LowStockHeaders(), rows)
            Dim fileName As String = CsvExporter.BuildFileName("low-stock", Nothing, Nothing)

            Return File(bytes, CsvExporter.ContentType, fileName)

        End Function

        ''' <summary>Spec section 14 row 10. productId is optional - Nothing means every product.</summary>
        <HttpGet("stock-movements")>
        Public Async Function GetStockMovementReport(
            <FromQuery> Optional productId As Integer? = Nothing,
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
                If Not TryParseCreatedAtSort(sort, sortDescending) Then
                    fieldErrors("sort") = {"Unsupported sort. Use createdAt, optionally suffixed with ':asc' or ':desc'."}
                End If
            End If

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim effectivePage As Integer = If(page < 1, 1, page)
            Dim effectivePageSize As Integer = If(pageSize < 1, DefaultPageSize, Math.Min(pageSize, MaxPageSize))

            Dim result =
                Await _reportService.GetStockMovementReportAsync(
                    productId, range.FromUtc, range.ToUtcExclusive, sortDescending, effectivePage, effectivePageSize, HttpContext.RequestAborted)

            Return Ok(New StockMovementReportResponse With {
                .Range = New ReportRangeEnvelope With {
                    .FromDate = range.FromDateText, .ToDate = range.ToDateText, .TimeZone = StoreTimeZone.IanaId,
                    .ReturnsTreatment = NameOf(Merchandising.Domain.Reporting.ReturnsTreatment.Included)
                },
                .Items = result.Items,
                .TotalCount = result.TotalCount,
                .Page = effectivePage,
                .PageSize = effectivePageSize,
                .MaxPageSize = MaxPageSize,
                .Sort = "createdAt" & If(sortDescending, ":desc", ":asc")
            })

        End Function

        ''' <summary>P6-06: same route/validation as <see cref="GetStockMovementReport"/>, exported as CSV - every matching row. A filtered productId is carried into the filename (CsvExporter.BuildFileName's extraSuffix).</summary>
        <HttpGet("stock-movements/csv")>
        Public Async Function GetStockMovementReportCsv(
            <FromQuery> Optional productId As Integer? = Nothing,
            <FromQuery> Optional fromDate As String = Nothing,
            <FromQuery> Optional toDate As String = Nothing,
            <FromQuery> Optional sort As String = Nothing) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            Dim range As ParsedDateRange = ParseDateRange(fromDate, toDate, fieldErrors)

            Dim sortDescending As Boolean = True

            If Not String.IsNullOrWhiteSpace(sort) Then
                If Not TryParseCreatedAtSort(sort, sortDescending) Then
                    fieldErrors("sort") = {"Unsupported sort. Use createdAt, optionally suffixed with ':asc' or ':desc'."}
                End If
            End If

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim result =
                Await _reportService.GetStockMovementReportAsync(
                    productId, range.FromUtc, range.ToUtcExclusive, sortDescending, 1, ExportPageSize, HttpContext.RequestAborted)

            Dim rows As IReadOnlyList(Of String)() =
                result.Items.Select(Function(item) ReportCsvFormatters.StockMovementRow(item)).ToArray()

            Dim bytes As Byte() = CsvExporter.BuildFile(ReportCsvFormatters.StockMovementHeaders(), rows)
            Dim fileName As String = CsvExporter.BuildFileName(
                "stock-movements", range.FromDateText, range.ToDateText,
                If(productId.HasValue, "product-" & productId.Value.ToString(CultureInfo.InvariantCulture), Nothing))

            Return File(bytes, CsvExporter.ContentType, fileName)

        End Function

        ''' <summary>Spec section 14 row 11. The first GET route StockAdjustments has ever had.</summary>
        <HttpGet("stock-adjustments")>
        Public Async Function GetStockAdjustmentReport(
            <FromQuery> Optional productId As Integer? = Nothing,
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
                If Not TryParseCreatedAtSort(sort, sortDescending) Then
                    fieldErrors("sort") = {"Unsupported sort. Use createdAt, optionally suffixed with ':asc' or ':desc'."}
                End If
            End If

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim effectivePage As Integer = If(page < 1, 1, page)
            Dim effectivePageSize As Integer = If(pageSize < 1, DefaultPageSize, Math.Min(pageSize, MaxPageSize))

            Dim result =
                Await _reportService.GetStockAdjustmentReportAsync(
                    productId, range.FromUtc, range.ToUtcExclusive, sortDescending, effectivePage, effectivePageSize, HttpContext.RequestAborted)

            Return Ok(New StockAdjustmentReportResponse With {
                .Range = New ReportRangeEnvelope With {
                    .FromDate = range.FromDateText, .ToDate = range.ToDateText, .TimeZone = StoreTimeZone.IanaId,
                    .ReturnsTreatment = NameOf(Merchandising.Domain.Reporting.ReturnsTreatment.Excluded)
                },
                .Items = result.Items,
                .TotalCount = result.TotalCount,
                .Page = effectivePage,
                .PageSize = effectivePageSize,
                .MaxPageSize = MaxPageSize,
                .Sort = "createdAt" & If(sortDescending, ":desc", ":asc")
            })

        End Function

        ''' <summary>P6-06: same route/validation as <see cref="GetStockAdjustmentReport"/>, exported as CSV - every matching row.</summary>
        <HttpGet("stock-adjustments/csv")>
        Public Async Function GetStockAdjustmentReportCsv(
            <FromQuery> Optional productId As Integer? = Nothing,
            <FromQuery> Optional fromDate As String = Nothing,
            <FromQuery> Optional toDate As String = Nothing,
            <FromQuery> Optional sort As String = Nothing) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            Dim range As ParsedDateRange = ParseDateRange(fromDate, toDate, fieldErrors)

            Dim sortDescending As Boolean = True

            If Not String.IsNullOrWhiteSpace(sort) Then
                If Not TryParseCreatedAtSort(sort, sortDescending) Then
                    fieldErrors("sort") = {"Unsupported sort. Use createdAt, optionally suffixed with ':asc' or ':desc'."}
                End If
            End If

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim result =
                Await _reportService.GetStockAdjustmentReportAsync(
                    productId, range.FromUtc, range.ToUtcExclusive, sortDescending, 1, ExportPageSize, HttpContext.RequestAborted)

            Dim rows As IReadOnlyList(Of String)() =
                result.Items.Select(Function(item) ReportCsvFormatters.StockAdjustmentRow(item)).ToArray()

            Dim bytes As Byte() = CsvExporter.BuildFile(ReportCsvFormatters.StockAdjustmentHeaders(), rows)
            Dim fileName As String = CsvExporter.BuildFileName(
                "stock-adjustments", range.FromDateText, range.ToDateText,
                If(productId.HasValue, "product-" & productId.Value.ToString(CultureInfo.InvariantCulture), Nothing))

            Return File(bytes, CsvExporter.ContentType, fileName)

        End Function

        ' --------------------------------------------------------- date range

        Private Structure ParsedDateRange
            Public FromDateText As String
            Public ToDateText As String
            Public FromUtc As DateTime?
            Public ToUtcExclusive As DateTime?
        End Structure

        ''' <summary>The same duplicated-per-controller shape every other Track B controller uses (ProcurementReportsController's own comment).</summary>
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

        Private Shared Function TryParseCurrentStockSort(
            value As String, ByRef sortField As CurrentStockReportSortField, ByRef sortDescending As Boolean) As Boolean

            Dim fieldName As String = Nothing
            Dim descending As Boolean = False

            If Not TrySplitSort(value, fieldName, descending) Then
                Return False
            End If

            If String.Equals(fieldName, "productName", StringComparison.OrdinalIgnoreCase) Then
                sortField = CurrentStockReportSortField.ProductName
            ElseIf String.Equals(fieldName, "quantity", StringComparison.OrdinalIgnoreCase) Then
                sortField = CurrentStockReportSortField.Quantity
            ElseIf String.Equals(fieldName, "reorderLevel", StringComparison.OrdinalIgnoreCase) Then
                sortField = CurrentStockReportSortField.ReorderLevel
            Else
                Return False
            End If

            sortDescending = descending
            Return True

        End Function

        Private Shared Function CanonicalCurrentStockSort(sortField As CurrentStockReportSortField, sortDescending As Boolean) As String

            Dim fieldName As String

            Select Case sortField
                Case CurrentStockReportSortField.Quantity
                    fieldName = "quantity"
                Case CurrentStockReportSortField.ReorderLevel
                    fieldName = "reorderLevel"
                Case Else
                    fieldName = "productName"
            End Select

            Return fieldName & If(sortDescending, ":desc", ":asc")

        End Function

        ''' <summary>The exact same field names/whitelist InventoryController.TryParseStockSort uses for GET /api/v1/inventory/stock and /low-stock.</summary>
        Private Shared Function TryParseStockSort(
            value As String, ByRef sortField As StockSortField, ByRef sortDescending As Boolean) As Boolean

            Dim fieldName As String = Nothing
            Dim descending As Boolean = False

            If Not TrySplitSort(value, fieldName, descending) Then
                Return False
            End If

            If String.Equals(fieldName, "productName", StringComparison.OrdinalIgnoreCase) Then
                sortField = StockSortField.ProductName
            ElseIf String.Equals(fieldName, "quantity", StringComparison.OrdinalIgnoreCase) Then
                sortField = StockSortField.Quantity
            ElseIf String.Equals(fieldName, "reorderLevel", StringComparison.OrdinalIgnoreCase) Then
                sortField = StockSortField.ReorderLevel
            Else
                Return False
            End If

            sortDescending = descending
            Return True

        End Function

        Private Shared Function CanonicalStockSort(sortField As StockSortField, sortDescending As Boolean) As String

            Dim fieldName As String

            Select Case sortField
                Case StockSortField.Quantity
                    fieldName = "quantity"
                Case StockSortField.ReorderLevel
                    fieldName = "reorderLevel"
                Case Else
                    fieldName = "productName"
            End Select

            Return fieldName & If(sortDescending, ":desc", ":asc")

        End Function

        Private Shared Function TryParseCreatedAtSort(value As String, ByRef sortDescending As Boolean) As Boolean

            Dim fieldName As String = Nothing
            Dim descending As Boolean = False

            If Not TrySplitSort(value, fieldName, descending) Then
                Return False
            End If

            If Not String.Equals(fieldName, "createdAt", StringComparison.OrdinalIgnoreCase) Then
                Return False
            End If

            sortDescending = descending
            Return True

        End Function

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

        ' ------------------------------------------------------------ results

        Private Function ValidationFailed(field As String, message As String, correlationId As String) As IActionResult
            Return ValidationFailed(New Dictionary(Of String, String()) From {{field, New String() {message}}}, correlationId)
        End Function

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
