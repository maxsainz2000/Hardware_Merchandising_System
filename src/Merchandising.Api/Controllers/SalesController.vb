' Merchandising.Api.Controllers.SalesController
'
' P5-07: spec section 13's `POST /api/v1/sales` - "the card the phase exists
' for." Gated by Sales.Create (PolicyRegistry's CashierAndAbove policy,
' pre-registered since P2-02 with no live route until this card).
'
' WHAT THIS CONTROLLER OWNS, AND WHAT IT DELIBERATELY DOES NOT - the same
' division ReceivingController's header states. It owns HTTP shape only:
' parsing, refusing a malformed request with field-level detail (ADR-014),
' and mapping a service outcome onto a status code. Every business rule -
' the open-session requirement, product activity, current price, available
' stock, payment validity, and all seven effects committing together - is
' decided inside SaleService, in one transaction. There is no courtesy
' pre-check read here (contrast CreateProduct's duplicate-SKU pre-check):
' SaleService's own outcomes already carry everything this controller needs
' to build a response, the same simpler shape ReceivingController uses for
' ReceiveGoods.

Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Security.Claims
Imports System.Threading.Tasks
Imports Merchandising.Api.Middleware
Imports Merchandising.Api.Sales
Imports Merchandising.Api.Security
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Sales
Imports Merchandising.Domain
Imports Merchandising.Domain.Sales
Imports Merchandising.Domain.Security
Imports Merchandising.Infrastructure.Data
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Mvc

Namespace Controllers

    <ApiController>
    <Route("api/v1/sales")>
    Public Class SalesController
        Inherits ControllerBase

        Private Const DefaultPageSize As Integer = 25
        Private Const MaxPageSize As Integer = 100

        Private ReadOnly _saleService As SaleService

        Public Sub New(saleService As SaleService)
            _saleService = saleService
        End Sub

        ''' <summary>
        ''' Completes a sale against the calling cashier's own open session.
        ''' One transaction commits the sale header, its lines, the payment
        ''' record, the stock effects and the audit event, or none of them
        ''' (spec section 11).
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.SalesCreate)>
        <AuditRequired>
        <HttpPost>
        Public Async Function CreateSale(<FromBody> request As CreateSaleRequest) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            Dim paymentMethod As PaymentMethod = PaymentMethod.Cash
            ValidateRequestShape(request, fieldErrors, paymentMethod)

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            ' paymentMethod was set ByRef inside ValidateRequestShape ->
            ' ValidatePayment once payment.method parsed successfully - a
            ' precondition guaranteed by fieldErrors being empty here.
            Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

            Dim outcome As SaleOutcome =
                Await _saleService.CompleteAsync(
                    request.Lines, paymentMethod, request.Payment.TenderedAmount, actorUserId, correlationId,
                    request.IdempotencyKey, cancellationToken:=HttpContext.RequestAborted)

            Select Case outcome.Kind

                Case SaleOutcomeKind.Created
                    Return Created($"/api/v1/sales/{outcome.Response.Id}", outcome.Response)

                Case SaleOutcomeKind.Replayed
                    ' ADR-007: the ORIGINAL committed result, byte for byte -
                    ' ReceivingService's identical reasoning. 200, not 201 -
                    ' this request sold nothing.
                    Return Content(outcome.ReplayPayload, "application/json")

                Case SaleOutcomeKind.NoOpenSession
                    Return Conflict(New ApiErrorResponse With {
                        .ErrorCode = SaleOutcome.NoOpenSessionErrorCode,
                        .Message = "A sale requires an open cashier session - open one before completing a sale.",
                        .CorrelationId = correlationId
                    })

                Case SaleOutcomeKind.ProductNotFound
                    Return ValidationFailed(
                        New Dictionary(Of String, String()) From {
                            {LineFieldFor(request, outcome.OffendingProductId), New String() {"No such product exists."}}},
                        correlationId)

                Case SaleOutcomeKind.ProductInactive
                    Return Conflict(New ApiErrorResponse With {
                        .ErrorCode = SaleOutcome.ProductInactiveErrorCode,
                        .Message = $"The product named by {LineFieldFor(request, outcome.OffendingProductId)} is inactive and cannot be sold.",
                        .CorrelationId = correlationId
                    })

                Case SaleOutcomeKind.InsufficientStock
                    Return Conflict(New ApiErrorResponse With {
                        .ErrorCode = SaleOutcome.InsufficientStockErrorCode,
                        .Message = $"There is not enough available stock for {LineFieldFor(request, outcome.OffendingProductId)}.",
                        .CorrelationId = correlationId
                    })

                Case SaleOutcomeKind.IdempotencyKeyReused
                    ' P5-09/ADR-007.1: this idempotency key was already used
                    ' for a DIFFERENT request body - a client bug, refused
                    ' rather than replayed (never look like success).
                    Return Conflict(New ApiErrorResponse With {
                        .ErrorCode = SaleOutcome.IdempotencyKeyReusedErrorCode,
                        .Message = "This idempotency key was already used for a different request. Generate a new key for a new sale.",
                        .CorrelationId = correlationId
                    })

                Case Else ' CashTenderInsufficient
                    Return BadRequest(New ApiErrorResponse With {
                        .ErrorCode = SaleOutcome.CashTenderInsufficientErrorCode,
                        .Message = $"The tendered amount is short by {outcome.ShortfallAmount:0.0000}.",
                        .CorrelationId = correlationId,
                        .Errors = New Dictionary(Of String, String()) From {
                            {"payment.tenderedAmount", New String() {"Tendered amount must be at least the sale total."}}}
                    })

            End Select

        End Function

        ''' <summary>
        ''' P6-02: GET /api/v1/sales - the detail endpoint ADR-023 point 6
        ''' flagged as owed to this card. No GET existed for Sales before
        ''' this card. Gated by Reports.View, not Sales.Create - this
        ''' endpoint exists to serve report reconciliation, never as a
        ''' cashier-facing sales-history feature (confirmed with the user at
        ''' P6-02; SaleSearchResponse's own header carries the same note).
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.ReportsView)>
        <HttpGet>
        Public Async Function SearchSales(
            <FromQuery> Optional fromDate As String = Nothing,
            <FromQuery> Optional toDate As String = Nothing,
            <FromQuery> Optional cashierUserId As Integer? = Nothing,
            <FromQuery> Optional sort As String = Nothing,
            <FromQuery> Optional page As Integer = 1,
            <FromQuery> Optional pageSize As Integer = DefaultPageSize) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

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

            Dim sortField As SaleSortField = SaleSortField.CreatedAt
            Dim sortDescending As Boolean = True

            If Not String.IsNullOrWhiteSpace(sort) Then
                If Not TryParseSaleSort(sort, sortField, sortDescending) Then
                    fieldErrors("sort") = {"Unsupported sort. Use one of createdAt, total, optionally suffixed with ':asc' or ':desc'."}
                End If
            End If

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim effectivePage As Integer = If(page < 1, 1, page)
            Dim effectivePageSize As Integer = If(pageSize < 1, DefaultPageSize, Math.Min(pageSize, MaxPageSize))

            Dim fromUtc As DateTime? =
                If(fromLocalDate.HasValue, CType(StoreTimeZone.StartOfDayUtc(fromLocalDate.Value), DateTime?), Nothing)
            Dim toUtcExclusive As DateTime? =
                If(toLocalDate.HasValue, CType(StoreTimeZone.EndOfDayUtcExclusive(toLocalDate.Value), DateTime?), Nothing)

            Dim result =
                Await _saleService.SearchAsync(
                    fromUtc, toUtcExclusive, cashierUserId, sortField, sortDescending,
                    effectivePage, effectivePageSize, HttpContext.RequestAborted)

            Return Ok(New SaleSearchResponse With {
                .Items = result.Items,
                .TotalCount = result.TotalCount,
                .Page = effectivePage,
                .PageSize = effectivePageSize,
                .MaxPageSize = MaxPageSize,
                .Sort = CanonicalSaleSort(sortField, sortDescending),
                .FromDate = If(fromLocalDate.HasValue, fromLocalDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Nothing),
                .ToDate = If(toLocalDate.HasValue, toLocalDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Nothing),
                .TimeZone = StoreTimeZone.IanaId
            })

        End Function

        ' --------------------------------------------------------- validation

        ''' <summary>
        ''' Everything about the request that can be judged without touching
        ''' the database - runs first so a malformed request is refused
        ''' before a connection is opened and before any idempotency key is
        ''' claimed (ADR-004.1, ReceivingController's identical box-3 rule).
        ''' <paramref name="parsedMethod"/> is populated when
        ''' <c>payment.method</c> is one of the three recognised names - callers
        ''' must re-check <paramref name="fieldErrors"/> before trusting it.
        ''' </summary>
        Private Shared Sub ValidateRequestShape(
            request As CreateSaleRequest, fieldErrors As Dictionary(Of String, String()), ByRef parsedMethod As PaymentMethod)

            If request Is Nothing Then
                fieldErrors("request") = {"A request body is required."}
                Return
            End If

            If String.IsNullOrWhiteSpace(request.IdempotencyKey) Then
                fieldErrors("idempotencyKey") = {"Idempotency key is required."}
            ElseIf Not IsWellFormedIdempotencyKey(request.IdempotencyKey) Then
                fieldErrors("idempotencyKey") = {
                    "Idempotency key must be a UUID in the canonical 36-character form, for example " &
                    "3f2504e0-4f89-41d3-9a0c-0305e82c3301."}
            End If

            If request.Lines Is Nothing OrElse request.Lines.Count = 0 Then
                fieldErrors("lines") = {"A sale must have at least one line."}
            Else

                For index As Integer = 0 To request.Lines.Count - 1

                    Dim line As CreateSaleLineRequest = request.Lines(index)

                    If line Is Nothing Then
                        fieldErrors($"lines[{index}]") = {"A line is required."}
                        Continue For
                    End If

                    If line.ProductId <= 0 Then
                        fieldErrors($"lines[{index}].productId") = {"A product is required."}
                    End If

                    ValidateQuantity(line.Quantity, $"lines[{index}].quantity", fieldErrors)

                Next

            End If

            ValidatePayment(request.Payment, fieldErrors, parsedMethod)

        End Sub

        ''' <summary>Quantity must be positive (CK_SaleLines_Quantity) and already at DECIMAL(19,3) scale.</summary>
        Private Shared Sub ValidateQuantity(
            value As Decimal, field As String, fieldErrors As Dictionary(Of String, String()))

            If value <= 0D Then
                fieldErrors(field) = {"Quantity must be greater than zero."}
            ElseIf Not DecimalScaleGuard.IsAtQuantityScale(value) Then
                fieldErrors(field) = {$"Quantity must have no more than {DecimalScaleGuard.QuantityScale} decimal places."}
            End If

        End Sub

        ''' <summary>
        ''' Method must be one of the three recognised names; TenderedAmount
        ''' must be present, non-negative and at money scale for Cash, and
        ''' absent for Card/EWallet (CreateSalePaymentRequest's own header,
        ''' the API-boundary mirror of CK_SalePayments_CashTenderPairing).
        ''' </summary>
        Private Shared Sub ValidatePayment(
            payment As CreateSalePaymentRequest, fieldErrors As Dictionary(Of String, String()), ByRef parsedMethod As PaymentMethod)

            If payment Is Nothing Then
                fieldErrors("payment") = {"A payment is required."}
                Return
            End If

            Dim method As PaymentMethod = PaymentMethod.Cash

            If Not TryParsePaymentMethod(payment.Method, method) Then
                fieldErrors("payment.method") = {
                    $"Payment method must be one of {String.Join(", ", [Enum].GetNames(GetType(PaymentMethod)))}."}
                Return
            End If

            parsedMethod = method

            If method = PaymentMethod.Cash Then

                If Not payment.TenderedAmount.HasValue Then
                    fieldErrors("payment.tenderedAmount") = {"Tendered amount is required for a Cash payment."}
                ElseIf payment.TenderedAmount.Value < 0D Then
                    fieldErrors("payment.tenderedAmount") = {"Tendered amount cannot be negative."}
                ElseIf Not DecimalScaleGuard.IsAtMoneyScale(payment.TenderedAmount.Value) Then
                    fieldErrors("payment.tenderedAmount") = {
                        $"Tendered amount must have no more than {DecimalScaleGuard.MoneyScale} decimal places."}
                End If

            ElseIf payment.TenderedAmount.HasValue Then

                fieldErrors("payment.tenderedAmount") = {"Tendered amount must not be supplied for a Card or EWallet payment."}

            End If

        End Sub

        Private Shared Function TryParsePaymentMethod(value As String, ByRef method As PaymentMethod) As Boolean

            For Each candidate As String In [Enum].GetNames(GetType(PaymentMethod))
                If String.Equals(candidate, value, StringComparison.Ordinal) Then
                    method = CType([Enum].Parse(GetType(PaymentMethod), candidate), PaymentMethod)
                    Return True
                End If
            Next

            Return False

        End Function

        Private Shared Function IsWellFormedIdempotencyKey(value As String) As Boolean

            Dim parsed As Guid = Guid.Empty
            Return Guid.TryParseExact(value, "D", parsed) AndAlso parsed <> Guid.Empty

        End Function

        ''' <summary>Parses "field" or "field:direction" against SearchSales' own whitelist - the same per-controller-whitelist precedent PurchaseOrdersController.TryParseSort/ReportsController.TrySplitSort establish.</summary>
        Private Shared Function TryParseSaleSort(value As String, ByRef sortField As SaleSortField, ByRef sortDescending As Boolean) As Boolean

            Dim parts As String() = value.Split(":"c)

            If parts.Length > 2 Then
                Return False
            End If

            Dim fieldName As String = parts(0).Trim()
            Dim descending As Boolean = False

            If parts.Length = 2 Then

                Dim direction As String = parts(1).Trim()

                If String.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase) Then
                    descending = True
                ElseIf Not String.Equals(direction, "asc", StringComparison.OrdinalIgnoreCase) Then
                    Return False
                End If

            End If

            If String.Equals(fieldName, "createdAt", StringComparison.OrdinalIgnoreCase) Then
                sortField = SaleSortField.CreatedAt
            ElseIf String.Equals(fieldName, "total", StringComparison.OrdinalIgnoreCase) Then
                sortField = SaleSortField.Total
            Else
                Return False
            End If

            sortDescending = descending
            Return True

        End Function

        Private Shared Function CanonicalSaleSort(sortField As SaleSortField, sortDescending As Boolean) As String

            Dim fieldName As String = If(sortField = SaleSortField.Total, "total", "createdAt")
            Return fieldName & If(sortDescending, ":desc", ":asc")

        End Function

        ''' <summary>The field name for a line the SERVICE rejected. The service reports a ProductId, not a position; this maps it back to the line the caller wrote - ReceivingController's identical LineFieldFor.</summary>
        Private Shared Function LineFieldFor(request As CreateSaleRequest, productId As Integer) As String

            For index As Integer = 0 To request.Lines.Count - 1
                If request.Lines(index).ProductId = productId Then
                    Return $"lines[{index}].productId"
                End If
            Next

            Return "lines"

        End Function

        ' ------------------------------------------------------------ results

        Private Function ValidationFailed(
            fieldErrors As Dictionary(Of String, String()), correlationId As String) As IActionResult

            Return BadRequest(New ApiErrorResponse With {
                .ErrorCode = "VALIDATION_FAILED",
                .Message = "The sale could not be processed because of a validation failure.",
                .CorrelationId = correlationId,
                .Errors = fieldErrors
            })

        End Function

    End Class

End Namespace
