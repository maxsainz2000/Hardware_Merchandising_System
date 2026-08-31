' Merchandising.Api.Controllers.ProcurementReportsController
'
' P6-04: spec section 14 rows 6-7, the procurement half of Track B. Gated by
' Reports.View (Admin-and-above, PolicyRegistry, pre-registered at P2-02),
' the same policy every other Track B report uses.
'
'   GET /api/v1/reports/procurement/purchase-orders - spec section 14 row 6.
'                     ReceivedQuantity/Value/OutstandingQuantity are computed
'                     from committed ReceiptLines rows, never the
'                     PurchaseOrderLines.ReceivedQuantity accumulator P3-06's
'                     GET /api/v1/purchase-orders/history reads - see
'                     ReportRepository.GetPurchaseOrderHistoryReportAsync's
'                     header for why. Date filter: PurchaseOrders.CreatedAtUtc,
'                     the same column P3-06's endpoint filters on, so the two
'                     reconcile order-by-order under an identical filter.
'   GET /api/v1/reports/procurement/goods-receiving - spec section 14 row 7.
'                     A flat per-receipt-line listing. Date filter:
'                     Receipts.ReceivedAtUtc, this report's own natural date
'                     dimension - deliberately not the same column the
'                     purchase-orders report above filters on (that
'                     column belongs to the ORDER, not the receipt).
'
' WHAT THIS CONTROLLER OWNS, AND WHAT IT DELIBERATELY DOES NOT - the same
' division ReportsController's header states. It owns HTTP shape only; every
' sum, every join, every "read from committed rows, not the accumulator"
' decision lives in ReportRepository, one layer down. It owns NO purchase-
' order status rule - Status is read and echoed verbatim from
' PurchaseOrders.Status (ADR-020's enum name), never re-decided here.

Imports System.Collections.Generic
Imports System.Globalization
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
    <Route("api/v1/reports/procurement")>
    <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.ReportsView)>
    Public Class ProcurementReportsController
        Inherits ControllerBase

        Private Const DefaultPageSize As Integer = 25
        Private Const MaxPageSize As Integer = 100

        Private ReadOnly _reportService As ReportService

        Public Sub New(reportService As ReportService)
            _reportService = reportService
        End Sub

        ''' <summary>Spec section 14 row 6.</summary>
        <HttpGet("purchase-orders")>
        Public Async Function GetPurchaseOrderHistoryReport(
            <FromQuery> Optional fromDate As String = Nothing,
            <FromQuery> Optional toDate As String = Nothing,
            <FromQuery> Optional sort As String = Nothing,
            <FromQuery> Optional page As Integer = 1,
            <FromQuery> Optional pageSize As Integer = DefaultPageSize) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            Dim range As ParsedDateRange = ParseDateRange(fromDate, toDate, fieldErrors)

            Dim sortField As PurchaseOrderHistoryReportSortField = PurchaseOrderHistoryReportSortField.CreatedAt
            Dim sortDescending As Boolean = True

            If Not String.IsNullOrWhiteSpace(sort) Then
                If Not TryParsePurchaseOrderHistorySort(sort, sortField, sortDescending) Then
                    fieldErrors("sort") = {"Unsupported sort. Use one of createdAt, orderNumber, status, optionally suffixed with ':asc' or ':desc'."}
                End If
            End If

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim effectivePage As Integer = If(page < 1, 1, page)
            Dim effectivePageSize As Integer = If(pageSize < 1, DefaultPageSize, Math.Min(pageSize, MaxPageSize))

            Dim result =
                Await _reportService.GetPurchaseOrderHistoryReportAsync(
                    range.FromDateText, range.ToDateText, range.FromUtc, range.ToUtcExclusive,
                    sortField, sortDescending, effectivePage, effectivePageSize, HttpContext.RequestAborted)

            Return Ok(New PurchaseOrderHistoryReportResponse With {
                .Range = New ReportRangeEnvelope With {
                    .FromDate = range.FromDateText, .ToDate = range.ToDateText, .TimeZone = StoreTimeZone.IanaId,
                    .ReturnsTreatment = NameOf(Merchandising.Domain.Reporting.ReturnsTreatment.Excluded)
                },
                .Items = result.Items,
                .TotalCount = result.TotalCount,
                .Page = effectivePage,
                .PageSize = effectivePageSize,
                .MaxPageSize = MaxPageSize,
                .Sort = CanonicalPurchaseOrderHistorySort(sortField, sortDescending)
            })

        End Function

        ''' <summary>Spec section 14 row 7.</summary>
        <HttpGet("goods-receiving")>
        Public Async Function GetGoodsReceivingHistory(
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
                If Not TryParseReceivedAtSort(sort, sortDescending) Then
                    fieldErrors("sort") = {"Unsupported sort. Use receivedAt, optionally suffixed with ':asc' or ':desc'."}
                End If
            End If

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim effectivePage As Integer = If(page < 1, 1, page)
            Dim effectivePageSize As Integer = If(pageSize < 1, DefaultPageSize, Math.Min(pageSize, MaxPageSize))

            Dim result =
                Await _reportService.GetGoodsReceivingHistoryAsync(
                    range.FromDateText, range.ToDateText, range.FromUtc, range.ToUtcExclusive,
                    sortDescending, effectivePage, effectivePageSize, HttpContext.RequestAborted)

            Return Ok(New GoodsReceivingHistoryResponse With {
                .Range = New ReportRangeEnvelope With {
                    .FromDate = range.FromDateText, .ToDate = range.ToDateText, .TimeZone = StoreTimeZone.IanaId,
                    .ReturnsTreatment = NameOf(Merchandising.Domain.Reporting.ReturnsTreatment.Excluded)
                },
                .Items = result.Items,
                .TotalCount = result.TotalCount,
                .Page = effectivePage,
                .PageSize = effectivePageSize,
                .MaxPageSize = MaxPageSize,
                .Sort = "receivedAt" & If(sortDescending, ":desc", ":asc")
            })

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
        ''' (docs/report-specification.md section 3). toDate before fromDate
        ''' is refused. The same duplicated-per-controller shape
        ''' ReportsController.ParseDateRange already uses (that class's own
        ''' comment: "each controller owns its own whitelist").
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

        Private Shared Function TryParsePurchaseOrderHistorySort(
            value As String, ByRef sortField As PurchaseOrderHistoryReportSortField, ByRef sortDescending As Boolean) As Boolean

            Dim fieldName As String = Nothing
            Dim descending As Boolean = False

            If Not TrySplitSort(value, fieldName, descending) Then
                Return False
            End If

            If String.Equals(fieldName, "createdAt", StringComparison.OrdinalIgnoreCase) Then
                sortField = PurchaseOrderHistoryReportSortField.CreatedAt
            ElseIf String.Equals(fieldName, "orderNumber", StringComparison.OrdinalIgnoreCase) Then
                sortField = PurchaseOrderHistoryReportSortField.OrderNumber
            ElseIf String.Equals(fieldName, "status", StringComparison.OrdinalIgnoreCase) Then
                sortField = PurchaseOrderHistoryReportSortField.Status
            Else
                Return False
            End If

            sortDescending = descending
            Return True

        End Function

        ''' <summary>Single-field whitelist for the goods-receiving history report - the same shape ReportsController.TryParseReturnedAtSort uses for the returns report.</summary>
        Private Shared Function TryParseReceivedAtSort(value As String, ByRef sortDescending As Boolean) As Boolean

            Dim fieldName As String = Nothing
            Dim descending As Boolean = False

            If Not TrySplitSort(value, fieldName, descending) Then
                Return False
            End If

            If Not String.Equals(fieldName, "receivedAt", StringComparison.OrdinalIgnoreCase) Then
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

        Private Shared Function CanonicalPurchaseOrderHistorySort(sortField As PurchaseOrderHistoryReportSortField, sortDescending As Boolean) As String

            Dim fieldName As String

            Select Case sortField
                Case PurchaseOrderHistoryReportSortField.OrderNumber
                    fieldName = "orderNumber"
                Case PurchaseOrderHistoryReportSortField.Status
                    fieldName = "status"
                Case Else
                    fieldName = "createdAt"
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
