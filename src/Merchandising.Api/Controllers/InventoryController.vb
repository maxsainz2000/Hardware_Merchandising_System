' Merchandising.Api.Controllers.InventoryController
'
' POST /api/v1/inventory/stock/decrement - spec section 24 Foundation POC
' item 7 ("execute a stock decrement that creates a movement and audit
' record in one transaction"). Gated to the roles spec section 9 assigns
' adjustment responsibility to: Inventory Clerk raises adjustments, Admin
' approves them, Super Admin has blanket access. Cashier decrements stock
' only through a completed sale, which is a Phase 2+ endpoint this task does
' not build (CLAUDE.md's Phase 1 scope discipline).
'
' Field validation - including the ADR-004.1 storage-scale check - happens
' here, at the API boundary a client-supplied value crosses, exactly like
' AuthController's inline validation of username/password. By the time
' StockService.DecrementAsync is called, quantity is already known valid.
'
' P4-11 ADDS THREE READ ROUTES - spec section 13's "representative endpoint"
' Inventory row, and section 14's Current stock / Low-stock report
' definitions:
'
'   GET stock            - Stock.Read: every product's current position.
'   GET stock/movements  - Stock.ReviewMovements: one product's ledger, plus
'                          its current balance in the same response.
'   GET low-stock        - LowStock.Review: active products at or below
'                          their own reorder level.
'
' <Authorize> MOVES FROM THE CLASS TO EACH ACTION HERE, because these three
' routes are gated by three DIFFERENT policies (Stock.Read is
' EveryOperationalRole, the other two are InventoryAndAbove) while
' DecrementStock keeps Adjustments.Request - a single class-level policy
' could no longer describe every action, the same reason PurchaseOrdersController
' and AdjustmentsController put <Authorize(Policy:=...)> on each action rather
' than the class.
'
' PAGE/PAGE SIZE/SORT FOLLOW P3-03's CONTRACT EXACTLY (that controller's own
' header): page defaults to 1, clamped up to 1; pageSize defaults to 25,
' clamped down to MaxPageSize; sort is "field[:asc|desc]" against a
' whitelist, refused rather than silently ignored if unrecognised; the
' applied values are always echoed back, never left for the caller to infer.

Imports System.Globalization
Imports Merchandising.Api.Inventory
Imports Merchandising.Api.Middleware
Imports Merchandising.Api.Security
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Inventory
Imports Merchandising.Domain
Imports Merchandising.Domain.Security
Imports Merchandising.Infrastructure.Data
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Mvc
Imports System.Security.Claims

Namespace Controllers

    <ApiController>
    <Route("api/v1/inventory")>
    Public Class InventoryController
        Inherits ControllerBase

        Private Const MaxReasonLength As Integer = 255
        Private Const DefaultPageSize As Integer = 25
        Private Const MaxPageSize As Integer = 100

        Private ReadOnly _stockService As StockService

        Public Sub New(stockService As StockService)
            _stockService = stockService
        End Sub

        ''' <summary>
        ''' Decrements one product's stock balance. Returns 200 with the
        ''' before/after balance on success, 400 for a validation failure
        ''' (including an over-scale quantity, ADR-004.1), 409 when the
        ''' current balance cannot satisfy the request (ADR-006) - the same
        ''' response for "no such product" and "not enough stock", since the
        ''' conditional UPDATE's WHERE clause cannot and need not tell them
        ''' apart.
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.AdjustmentsRequest)>
        <AuditRequired>
        <HttpPost("stock/decrement")>
        Public Async Function DecrementStock(<FromBody> request As StockDecrementRequest) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            If request Is Nothing OrElse request.ProductId <= 0 Then
                fieldErrors("productId") = {"Product Id is required and must be a positive integer."}
            End If

            If request Is Nothing OrElse request.Quantity <= 0D Then
                fieldErrors("quantity") = {"Quantity is required and must be greater than zero."}
            ElseIf Not fieldErrors.ContainsKey("quantity") Then
                Try
                    DecimalScaleGuard.EnsureQuantityScale(request.Quantity)
                Catch ex As ArgumentException
                    fieldErrors("quantity") = {ex.Message}
                End Try
            End If

            If request Is Nothing OrElse String.IsNullOrWhiteSpace(request.Reason) Then
                fieldErrors("reason") = {"Reason is required."}
            ElseIf request.Reason.Length > MaxReasonLength Then
                fieldErrors("reason") = {$"Reason must be {MaxReasonLength} characters or fewer."}
            End If

            ' P1-14 / ADR-007: required, and validated to the same canonical
            ' 36-character GUID shape CorrelationIdMiddleware enforces on the
            ' header - IdempotencyKeys.KeyValue is CHAR(36) NOT NULL under
            ' STRICT_TRANS_TABLES, so an over-length value must be rejected
            ' here rather than surfacing as an ERROR 1406 from the database.
            If request Is Nothing OrElse String.IsNullOrWhiteSpace(request.IdempotencyKey) Then
                fieldErrors("idempotencyKey") = {"Idempotency key is required."}
            ElseIf Not IsWellFormedIdempotencyKey(request.IdempotencyKey) Then
                fieldErrors("idempotencyKey") = {
                    "Idempotency key must be a UUID in the canonical 36-character form, for example " &
                    "3f2504e0-4f89-41d3-9a0c-0305e82c3301."}
            End If

            If fieldErrors.Count > 0 Then
                Return BadRequest(New ApiErrorResponse With {
                    .ErrorCode = "VALIDATION_FAILED",
                    .Message = "The stock decrement request failed validation.",
                    .CorrelationId = correlationId,
                    .Errors = fieldErrors
                })
            End If

            Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

            Dim outcome As StockDecrementOutcome =
                Await _stockService.DecrementAsync(
                    request.ProductId, request.Quantity, request.Reason, actorUserId, correlationId, request.IdempotencyKey)

            Select Case outcome.Kind

                Case StockDecrementOutcomeKind.Success
                    Return Ok(outcome.Response)

                Case Else
                    Return Conflict(New ApiErrorResponse With {
                        .ErrorCode = "INSUFFICIENT_STOCK",
                        .Message = "The requested quantity is not available for this product.",
                        .CorrelationId = correlationId
                    })

            End Select

        End Function

        ''' <summary>
        ''' Paginated, sorted list of every product's current stock position -
        ''' Stock.Read.
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.StockRead)>
        <HttpGet("stock")>
        Public Async Function GetStock(
            <FromQuery> Optional includeInactive As Boolean = False,
            <FromQuery> Optional sort As String = Nothing,
            <FromQuery> Optional page As Integer = 1,
            <FromQuery> Optional pageSize As Integer = DefaultPageSize) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()

            Dim sortField As StockSortField = StockSortField.ProductName
            Dim sortDescending As Boolean = False

            If Not String.IsNullOrWhiteSpace(sort) Then
                If Not TryParseStockSort(sort, sortField, sortDescending) Then
                    Return ValidationFailed(
                        "sort", "Unsupported sort. Use one of " & String.Join(", ", StockSortFieldNames()) &
                        ", optionally suffixed with ':asc' or ':desc'.", correlationId)
                End If
            End If

            Dim effectivePage As Integer = If(page < 1, 1, page)
            Dim effectivePageSize As Integer = If(pageSize < 1, DefaultPageSize, Math.Min(pageSize, MaxPageSize))

            Dim result = Await _stockService.SearchStockAsync(
                includeInactive, sortField, sortDescending, effectivePage, effectivePageSize, HttpContext.RequestAborted)

            Return Ok(New StockSearchResponse With {
                .Items = result.Items,
                .TotalCount = result.TotalCount,
                .Page = effectivePage,
                .PageSize = effectivePageSize,
                .MaxPageSize = MaxPageSize,
                .Sort = CanonicalStockSort(sortField, sortDescending)
            })

        End Function

        ''' <summary>
        ''' Paginated, sorted list of ACTIVE products at or below their own
        ''' reorder level - LowStock.Review (spec section 14).
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.LowStockReview)>
        <HttpGet("low-stock")>
        Public Async Function GetLowStock(
            <FromQuery> Optional sort As String = Nothing,
            <FromQuery> Optional page As Integer = 1,
            <FromQuery> Optional pageSize As Integer = DefaultPageSize) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()

            Dim sortField As StockSortField = StockSortField.Quantity
            Dim sortDescending As Boolean = False

            If Not String.IsNullOrWhiteSpace(sort) Then
                If Not TryParseStockSort(sort, sortField, sortDescending) Then
                    Return ValidationFailed(
                        "sort", "Unsupported sort. Use one of " & String.Join(", ", StockSortFieldNames()) &
                        ", optionally suffixed with ':asc' or ':desc'.", correlationId)
                End If
            End If

            Dim effectivePage As Integer = If(page < 1, 1, page)
            Dim effectivePageSize As Integer = If(pageSize < 1, DefaultPageSize, Math.Min(pageSize, MaxPageSize))

            Dim result = Await _stockService.SearchLowStockAsync(
                sortField, sortDescending, effectivePage, effectivePageSize, HttpContext.RequestAborted)

            Return Ok(New LowStockSearchResponse With {
                .Items = result.Items,
                .TotalCount = result.TotalCount,
                .Page = effectivePage,
                .PageSize = effectivePageSize,
                .MaxPageSize = MaxPageSize,
                .Sort = CanonicalStockSort(sortField, sortDescending)
            })

        End Function

        ''' <summary>
        ''' Paginated, date-bounded movement ledger for ONE product, with its
        ''' current balance echoed alongside - Stock.ReviewMovements. Date
        ''' boundaries follow P3-06's exact contract: store-local
        ''' (Asia/Manila) yyyy-MM-dd, converted to a UTC instant range here,
        ''' the applied range and zone always echoed back.
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.StockReviewMovements)>
        <HttpGet("stock/movements")>
        Public Async Function GetStockMovements(
            <FromQuery> Optional productId As Integer? = Nothing,
            <FromQuery> Optional fromDate As String = Nothing,
            <FromQuery> Optional toDate As String = Nothing,
            <FromQuery> Optional sort As String = Nothing,
            <FromQuery> Optional page As Integer = 1,
            <FromQuery> Optional pageSize As Integer = DefaultPageSize) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            If Not productId.HasValue OrElse productId.Value <= 0 Then
                fieldErrors("productId") = {"A product id is required."}
            End If

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

            Dim sortField As StockMovementSortField = StockMovementSortField.CreatedAt
            Dim sortDescending As Boolean = True

            If Not String.IsNullOrWhiteSpace(sort) Then
                If Not TryParseMovementSort(sort, sortField, sortDescending) Then
                    fieldErrors("sort") = {"Unsupported sort. Use 'createdAt', optionally suffixed with ':asc' or ':desc'."}
                End If
            End If

            If fieldErrors.Count > 0 Then
                Return BadRequest(New ApiErrorResponse With {
                    .ErrorCode = "VALIDATION_FAILED",
                    .Message = "The stock-movement query failed validation.",
                    .CorrelationId = correlationId,
                    .Errors = fieldErrors
                })
            End If

            Dim effectivePage As Integer = If(page < 1, 1, page)
            Dim effectivePageSize As Integer = If(pageSize < 1, DefaultPageSize, Math.Min(pageSize, MaxPageSize))

            Dim fromUtc As DateTime? =
                If(fromLocalDate.HasValue, CType(StoreTimeZone.StartOfDayUtc(fromLocalDate.Value), DateTime?), Nothing)
            Dim toUtcExclusive As DateTime? =
                If(toLocalDate.HasValue, CType(StoreTimeZone.EndOfDayUtcExclusive(toLocalDate.Value), DateTime?), Nothing)

            Dim result = Await _stockService.SearchMovementsAsync(
                productId.Value, fromUtc, toUtcExclusive, sortField, sortDescending,
                effectivePage, effectivePageSize, HttpContext.RequestAborted)

            Return Ok(New StockMovementSearchResponse With {
                .Items = result.Items,
                .TotalCount = result.TotalCount,
                .Page = effectivePage,
                .PageSize = effectivePageSize,
                .MaxPageSize = MaxPageSize,
                .Sort = CanonicalMovementSort(sortField, sortDescending),
                .ProductId = productId.Value,
                .CurrentBalance = result.CurrentBalance,
                .FromDate = If(fromLocalDate.HasValue, fromLocalDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Nothing),
                .ToDate = If(toLocalDate.HasValue, toLocalDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Nothing),
                .TimeZone = StoreTimeZone.IanaId
            })

        End Function

        ' ------------------------------------------------------------ parsing

        Private Shared Function StockSortFieldNames() As String()
            Return {"productName", "quantity", "reorderLevel"}
        End Function

        Private Shared Function TryParseStockSort(
            value As String, ByRef sortField As StockSortField, ByRef sortDescending As Boolean) As Boolean

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

        Private Shared Function TryParseMovementSort(
            value As String, ByRef sortField As StockMovementSortField, ByRef sortDescending As Boolean) As Boolean

            Dim parts As String() = value.Split(":"c)

            If parts.Length > 2 Then
                Return False
            End If

            Dim fieldName As String = parts(0).Trim()
            Dim descending As Boolean = True

            If Not String.Equals(fieldName, "createdAt", StringComparison.OrdinalIgnoreCase) Then
                Return False
            End If

            If parts.Length = 2 Then

                Dim direction As String = parts(1).Trim()

                If String.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase) Then
                    descending = True
                ElseIf String.Equals(direction, "asc", StringComparison.OrdinalIgnoreCase) Then
                    descending = False
                Else
                    Return False
                End If

            End If

            sortField = StockMovementSortField.CreatedAt
            sortDescending = descending
            Return True

        End Function

        Private Shared Function CanonicalMovementSort(sortField As StockMovementSortField, sortDescending As Boolean) As String
            Return "createdAt" & If(sortDescending, ":desc", ":asc")
        End Function

        Private Function ValidationFailed(field As String, message As String, correlationId As String) As IActionResult

            Return BadRequest(New ApiErrorResponse With {
                .ErrorCode = "VALIDATION_FAILED",
                .Message = "The request failed validation.",
                .CorrelationId = correlationId,
                .Errors = New Dictionary(Of String, String()) From {{field, New String() {message}}}
            })

        End Function

        ''' <summary>
        ''' True only for the canonical 36-character "D" format - the same
        ''' check CorrelationIdMiddleware applies to X-Correlation-Id, for
        ''' the same reason: Guid.TryParse alone also accepts "B"/"P"/"N"/"X"
        ''' formats that would not round-trip to what the caller sent.
        ''' </summary>
        Private Shared Function IsWellFormedIdempotencyKey(value As String) As Boolean

            Dim parsed As Guid = Guid.Empty
            Return Guid.TryParseExact(value, "D", parsed) AndAlso parsed <> Guid.Empty

        End Function

    End Class

End Namespace
