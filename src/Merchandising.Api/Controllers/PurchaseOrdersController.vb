' Merchandising.Api.Controllers.PurchaseOrdersController
'
' P3-03: spec section 10.1's `POST /api/v1/purchase-orders` and the two reads
' that go with it - `GET /api/v1/purchase-orders` and
' `GET /api/v1/purchase-orders/{id}`.
'
' Gated by PurchaseOrders.Create and PurchaseOrders.Track, both already in
' PolicyRegistry (ProcurementAndAbove) ahead of this card - these are the
' first LIVE endpoints for either policy, so AuthorizationMatrixTests gains a
' cell for each. Nothing here decides who may do what; the registry does, and
' the matrix suite is data-driven from it.
'
' WHAT THIS CONTROLLER OWNS, AND WHAT IT DELIBERATELY DOES NOT.
' It owns the HTTP shape: parsing and clamping query parameters, refusing a
' malformed request with field-level detail (ADR-014), and mapping a service
' outcome onto a status code. It owns NO business rule. Whether a supplier is
' active, whether a product may be ordered, whether the created order is
' Draft, and what the order number is are all decided inside
' PurchaseOrderService, in one transaction. The lookups below duplicate two
' of those checks ONLY to produce a usable error body - see that class's
' header for why the in-transaction check is the one that counts, and
' SuppliersController for the same "a pre-check is a courtesy, not the
' guarantee" arrangement.
'
' SPEC SECTION 13 REQUIRES A LIST ENDPOINT TO DEFINE ITS OWN CONTRACT, so the
' constants below are the definition and PurchaseOrderSearchResponse echoes
' the applied values back rather than leaving a caller to infer them:
'
'   page          defaults to 1;  anything below 1 is clamped to 1
'   pageSize      defaults to 25; anything below 1 is clamped to the default,
'                 anything above MaxPageSize is clamped DOWN, never honoured
'   sort          defaults to createdAt:desc (newest first); a whitelist, so
'                 an unregistered field is refused rather than concatenated
'   filters       supplierId and status, both optional; an unknown status is
'                 refused, never silently treated as "no filter"
'
' DATE-BOUNDARY FILTERING IS DELIBERATELY ABSENT. Spec section 13 lists it,
' and P3-06 owns it: a date filter has to be expressed in the STORE time zone
' (Asia/Manila) against UTC storage, and defining that boundary in two cards
' would give P3-06 a second definition to reconcile against. Confirmed with
' the user at P3-03.

Imports System.Collections.Generic
Imports System.Linq
Imports System.Security.Claims
Imports System.Threading.Tasks
Imports Merchandising.Api.Middleware
Imports Merchandising.Api.Procurement
Imports Merchandising.Api.Security
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Procurement
Imports Merchandising.Domain
Imports Merchandising.Domain.Entities
Imports Merchandising.Domain.Security
Imports Merchandising.Infrastructure.Data
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Mvc
Imports MySqlConnector

Imports DomainProcurement = Merchandising.Domain.Procurement

Namespace Controllers

    <ApiController>
    <Route("api/v1/purchase-orders")>
    Public Class PurchaseOrdersController
        Inherits ControllerBase

        Private Const DefaultPageSize As Integer = 25
        Private Const MaxPageSize As Integer = 100

        Private ReadOnly _connectionFactory As ConnectionFactory
        Private ReadOnly _purchaseOrderService As PurchaseOrderService

        Public Sub New(connectionFactory As ConnectionFactory, purchaseOrderService As PurchaseOrderService)
            _connectionFactory = connectionFactory
            _purchaseOrderService = purchaseOrderService
        End Sub

        ''' <summary>
        ''' Creates a Draft purchase order with its lines. The status is
        ''' assigned server-side; a client-supplied one is refused rather than
        ''' ignored (CreatePurchaseOrderRequest's header explains why the
        ''' property exists at all).
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.PurchaseOrdersCreate)>
        <AuditRequired>
        <HttpPost>
        Public Async Function CreatePurchaseOrder(<FromBody> request As CreatePurchaseOrderRequest) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            ValidateCreateRequestShape(request, fieldErrors)

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            ' Everything below needs the database. The shape checks above did
            ' not, which is what keeps an over-scale value from ever reaching
            ' a connection, let alone a bound parameter (ADR-004.1).
            Dim referenceFailure As IActionResult =
                Await CheckReferencesAsync(request, correlationId)

            If referenceFailure IsNot Nothing Then
                Return referenceFailure
            End If

            Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

            Dim outcome As PurchaseOrderCreationOutcome =
                Await _purchaseOrderService.CreateAsync(
                    request.SupplierId, request.Lines, actorUserId, correlationId,
                    request.IdempotencyKey, HttpContext.RequestAborted)

            Select Case outcome.Kind

                Case PurchaseOrderCreationOutcomeKind.Created
                    Return CreatedAtAction(
                        NameOf(GetPurchaseOrder), New With {.id = outcome.Response.Id}, outcome.Response)

                Case PurchaseOrderCreationOutcomeKind.Replayed
                    ' ADR-007: the ORIGINAL committed result, byte for byte,
                    ' rather than a re-serialisation of a row that a later
                    ' card may since have moved on. 200, not 201 - this
                    ' request created nothing.
                    Return Content(outcome.ReplayPayload, "application/json")

                Case PurchaseOrderCreationOutcomeKind.SupplierNotFound
                    Return ValidationFailed(
                        New Dictionary(Of String, String()) From {
                            {"supplierId", New String() {"No such supplier exists."}}},
                        correlationId)

                Case PurchaseOrderCreationOutcomeKind.SupplierInactive
                    Return SupplierInactive(correlationId)

                Case PurchaseOrderCreationOutcomeKind.ProductNotFound
                    Return ValidationFailed(
                        New Dictionary(Of String, String()) From {
                            {ProductFieldFor(request, outcome.OffendingProductId), New String() {"No such product exists."}}},
                        correlationId)

                Case PurchaseOrderCreationOutcomeKind.ProductInactive
                    Return ProductInactive(correlationId)

                Case Else ' OrderNumberUnavailable
                    ' Practically unreachable - see PurchaseOrderService's
                    ' retry bound. A controlled 409 is still better than an
                    ' uncaught exception: it tells the caller the request was
                    ' understood and may simply be retried.
                    Return Conflict(New ApiErrorResponse With {
                        .ErrorCode = "ORDER_NUMBER_UNAVAILABLE",
                        .Message = "A purchase-order number could not be allocated. Retry the request.",
                        .CorrelationId = correlationId
                    })

            End Select

        End Function

        ''' <summary>
        ''' Paginated, filtered, sorted list of purchase orders - headers
        ''' only. See this class's header for the pagination, sort and filter
        ''' contract spec section 13 requires this endpoint to define.
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.PurchaseOrdersTrack)>
        <HttpGet>
        Public Async Function SearchPurchaseOrders(
            <FromQuery> Optional supplierId As Integer? = Nothing,
            <FromQuery> Optional status As String = Nothing,
            <FromQuery> Optional sort As String = Nothing,
            <FromQuery> Optional page As Integer = 1,
            <FromQuery> Optional pageSize As Integer = DefaultPageSize) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            Dim statusFilter As DomainProcurement.PurchaseOrderStatus? = Nothing

            If Not String.IsNullOrWhiteSpace(status) Then

                Dim parsedStatus As DomainProcurement.PurchaseOrderStatus

                If TryParseStatusName(status, parsedStatus) Then
                    statusFilter = parsedStatus
                Else
                    fieldErrors("status") = {
                        "Unknown purchase-order status. Valid values are " & String.Join(", ", StatusNames()) & "."}
                End If

            End If

            Dim sortField As PurchaseOrderSortField = PurchaseOrderSortField.CreatedAt
            Dim sortDescending As Boolean = True

            If Not String.IsNullOrWhiteSpace(sort) Then
                If Not TryParseSort(sort, sortField, sortDescending) Then
                    fieldErrors("sort") = {
                        "Unsupported sort. Use one of " & String.Join(", ", SortFieldNames()) &
                        ", optionally suffixed with ':asc' or ':desc'."}
                End If
            End If

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            ' Clamped rather than refused: an out-of-range page size is a
            ' caller being optimistic, not a caller being wrong, and the
            ' response says what was actually applied.
            Dim effectivePage As Integer = If(page < 1, 1, page)
            Dim effectivePageSize As Integer = If(pageSize < 1, DefaultPageSize, Math.Min(pageSize, MaxPageSize))

            Dim result = Await _purchaseOrderService.SearchAsync(
                supplierId, statusFilter, sortField, sortDescending,
                effectivePage, effectivePageSize, HttpContext.RequestAborted)

            Return Ok(New PurchaseOrderSearchResponse With {
                .Items = result.Items,
                .TotalCount = result.TotalCount,
                .Page = effectivePage,
                .PageSize = effectivePageSize,
                .MaxPageSize = MaxPageSize,
                .Sort = CanonicalSort(sortField, sortDescending)
            })

        End Function

        ''' <summary>
        ''' One purchase order with every line. A controlled 404 with a stable
        ''' error code when no such order exists - never a leak (ADR-014).
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.PurchaseOrdersTrack)>
        <HttpGet("{id}")>
        Public Async Function GetPurchaseOrder(id As Integer) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()

            Dim order As PurchaseOrderResponse =
                Await _purchaseOrderService.GetAsync(id, HttpContext.RequestAborted)

            If order Is Nothing Then
                Return NotFound(New ApiErrorResponse With {
                    .ErrorCode = "PURCHASE_ORDER_NOT_FOUND",
                    .Message = $"No purchase order with Id {id} exists.",
                    .CorrelationId = correlationId
                })
            End If

            Return Ok(order)

        End Function

        ' --------------------------------------------------------- validation

        ''' <summary>
        ''' Everything about the request that can be judged without touching
        ''' the database. Runs first precisely so an over-scale money or
        ''' quantity value is refused before a connection is opened and before
        ''' any idempotency key is claimed (ADR-004.1, box 3).
        ''' </summary>
        Private Shared Sub ValidateCreateRequestShape(
            request As CreatePurchaseOrderRequest, fieldErrors As Dictionary(Of String, String()))

            If request Is Nothing Then
                fieldErrors("request") = {"A request body is required."}
                Return
            End If

            ' The status property exists only so a client-supplied one can be
            ' REFUSED. Dropping it silently would leave a caller believing it
            ' had created an Approved order.
            If Not String.IsNullOrWhiteSpace(request.Status) Then
                fieldErrors("status") = {
                    "Status is assigned by the server. A new purchase order is always Draft; " &
                    "use the submit and approve endpoints to move it."}
            End If

            ' ADR-007: required on every write command, and validated to the
            ' same canonical 36-character UUID shape IdempotencyKeys.KeyValue
            ' is declared as - so an over-length value is refused here rather
            ' than surfacing as an ERROR 1406 from the database.
            If String.IsNullOrWhiteSpace(request.IdempotencyKey) Then
                fieldErrors("idempotencyKey") = {"Idempotency key is required."}
            ElseIf Not IsWellFormedIdempotencyKey(request.IdempotencyKey) Then
                fieldErrors("idempotencyKey") = {
                    "Idempotency key must be a UUID in the canonical 36-character form, for example " &
                    "3f2504e0-4f89-41d3-9a0c-0305e82c3301."}
            End If

            If request.SupplierId <= 0 Then
                fieldErrors("supplierId") = {"A supplier is required."}
            End If

            If request.Lines Is Nothing OrElse request.Lines.Count = 0 Then
                fieldErrors("lines") = {"A purchase order must have at least one line."}
                Return
            End If

            For index As Integer = 0 To request.Lines.Count - 1

                Dim line As CreatePurchaseOrderLineRequest = request.Lines(index)

                If line Is Nothing Then
                    fieldErrors($"lines[{index}]") = {"A line is required."}
                    Continue For
                End If

                If line.ProductId <= 0 Then
                    fieldErrors($"lines[{index}].productId") = {"A product is required."}
                End If

                ValidateQuantity(line.OrderedQuantity, $"lines[{index}].orderedQuantity", fieldErrors)
                ValidateCost(line.PurchaseCost, $"lines[{index}].purchaseCost", fieldErrors)

            Next

        End Sub

        ''' <summary>
        ''' Quantity must be positive (0008's CK_PurchaseOrderLines_
        ''' OrderedQuantity) and already at DECIMAL(19,3) scale - an
        ''' over-scale value is a validation failure, never something to round
        ''' on the caller's behalf (ADR-004.1).
        ''' </summary>
        Private Shared Sub ValidateQuantity(
            value As Decimal, field As String, fieldErrors As Dictionary(Of String, String()))

            If value <= 0D Then
                fieldErrors(field) = {"Ordered quantity must be greater than zero."}
            ElseIf Not DecimalScaleGuard.IsAtQuantityScale(value) Then
                fieldErrors(field) = {
                    $"Ordered quantity must have no more than {DecimalScaleGuard.QuantityScale} decimal places."}
            End If

        End Sub

        ''' <summary>Cost must be non-negative (CK_PurchaseOrderLines_PurchaseCost) and already at DECIMAL(19,4) scale.</summary>
        Private Shared Sub ValidateCost(
            value As Decimal, field As String, fieldErrors As Dictionary(Of String, String()))

            If value < 0D Then
                fieldErrors(field) = {"Purchase cost cannot be negative."}
            ElseIf Not DecimalScaleGuard.IsAtMoneyScale(value) Then
                fieldErrors(field) = {
                    $"Purchase cost must have no more than {DecimalScaleGuard.MoneyScale} decimal places."}
            End If

        End Sub

        ''' <summary>
        ''' Resolves the supplier and every referenced product, returning the
        ''' refusal to send or Nothing to continue. Unknown ids are reported
        ''' TOGETHER as field-level validation detail rather than one at a
        ''' time, so a caller with two mistakes learns about both; an inactive
        ''' supplier or product is a distinct, stable error code because it is
        ''' a lifecycle rule (spec section 10.2) rather than a malformed
        ''' request.
        ''' </summary>
        Private Async Function CheckReferencesAsync(
            request As CreatePurchaseOrderRequest, correlationId As String) As Task(Of IActionResult)

            Dim fieldErrors As New Dictionary(Of String, String())

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(HttpContext.RequestAborted)

                Dim supplier As Supplier =
                    Await SupplierRepository.GetByIdAsync(connection, request.SupplierId, HttpContext.RequestAborted)

                If supplier Is Nothing Then
                    fieldErrors("supplierId") = {"No such supplier exists."}
                End If

                Dim inactiveProductFound As Boolean = False

                For index As Integer = 0 To request.Lines.Count - 1

                    Dim productId As Integer = request.Lines(index).ProductId

                    Dim product As Product =
                        Await ProductRepository.GetByIdAsync(connection, productId, HttpContext.RequestAborted)

                    If product Is Nothing Then
                        fieldErrors($"lines[{index}].productId") = {"No such product exists."}
                    ElseIf Not product.IsActive Then
                        inactiveProductFound = True
                    End If

                Next

                If fieldErrors.Count > 0 Then
                    Return ValidationFailed(fieldErrors, correlationId)
                End If

                If Not supplier.IsActive Then
                    Return SupplierInactive(correlationId)
                End If

                If inactiveProductFound Then
                    Return ProductInactive(correlationId)
                End If

            End Using

            Return Nothing

        End Function

        ' ------------------------------------------------------------ parsing

        ''' <summary>
        ''' Matches a status name EXACTLY, the same byte-for-byte comparison
        ''' 0008's utf8mb4_bin CHECK constraint makes. <c>[Enum].TryParse</c>
        ''' is deliberately not used: it would also accept "1" and the other
        ''' ordinals, which is precisely what ADR-020 rules out as a stable
        ''' identifier.
        ''' </summary>
        Private Shared Function TryParseStatusName(
            value As String, ByRef parsed As DomainProcurement.PurchaseOrderStatus) As Boolean

            For Each name As String In StatusNames()
                If String.Equals(value, name, StringComparison.Ordinal) Then
                    parsed = CType([Enum].Parse(GetType(DomainProcurement.PurchaseOrderStatus), name),
                                   DomainProcurement.PurchaseOrderStatus)
                    Return True
                End If
            Next

            Return False

        End Function

        Private Shared Function StatusNames() As String()
            Return [Enum].GetNames(GetType(DomainProcurement.PurchaseOrderStatus))
        End Function

        Private Shared Function SortFieldNames() As String()
            Return {"createdAt", "orderNumber", "status"}
        End Function

        ''' <summary>
        ''' Parses "field" or "field:direction" against the whitelist above.
        ''' Anything unrecognised is refused - no caller-supplied text ever
        ''' reaches an ORDER BY, which PurchaseOrderRepository makes
        ''' structurally impossible anyway by taking an enum rather than a
        ''' column name.
        ''' </summary>
        Private Shared Function TryParseSort(
            value As String, ByRef sortField As PurchaseOrderSortField, ByRef sortDescending As Boolean) As Boolean

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
                sortField = PurchaseOrderSortField.CreatedAt
            ElseIf String.Equals(fieldName, "orderNumber", StringComparison.OrdinalIgnoreCase) Then
                sortField = PurchaseOrderSortField.OrderNumber
            ElseIf String.Equals(fieldName, "status", StringComparison.OrdinalIgnoreCase) Then
                sortField = PurchaseOrderSortField.Status
            Else
                Return False
            End If

            sortDescending = descending
            Return True

        End Function

        ''' <summary>The canonical "field:direction" text for what was actually applied, echoed back so a caller never has to infer it.</summary>
        Private Shared Function CanonicalSort(sortField As PurchaseOrderSortField, sortDescending As Boolean) As String

            Dim fieldName As String

            Select Case sortField
                Case PurchaseOrderSortField.OrderNumber
                    fieldName = "orderNumber"
                Case PurchaseOrderSortField.Status
                    fieldName = "status"
                Case Else
                    fieldName = "createdAt"
            End Select

            Return fieldName & If(sortDescending, ":desc", ":asc")

        End Function

        ''' <summary>
        ''' Guid.TryParseExact with "D", not Guid.TryParse - the same check
        ''' InventoryController makes, for the same reason: TryParse also
        ''' accepts the "B"/"P"/"N"/"X" forms, which would not round-trip to
        ''' what the caller sent.
        ''' </summary>
        Private Shared Function IsWellFormedIdempotencyKey(value As String) As Boolean

            Dim parsed As Guid = Guid.Empty
            Return Guid.TryParseExact(value, "D", parsed) AndAlso parsed <> Guid.Empty

        End Function

        ''' <summary>
        ''' The field name for a product the SERVICE rejected. The service
        ''' reports an id, not a position, because it has no notion of the
        ''' request's shape; this maps it back to the line the caller wrote.
        ''' </summary>
        Private Shared Function ProductFieldFor(request As CreatePurchaseOrderRequest, productId As Integer) As String

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
                .Message = "The purchase order could not be processed because of a validation failure.",
                .CorrelationId = correlationId,
                .Errors = fieldErrors
            })

        End Function

        Private Function SupplierInactive(correlationId As String) As IActionResult

            Return BadRequest(New ApiErrorResponse With {
                .ErrorCode = "SUPPLIER_INACTIVE",
                .Message = "A purchase order cannot be raised against a deactivated supplier.",
                .CorrelationId = correlationId
            })

        End Function

        Private Function ProductInactive(correlationId As String) As IActionResult

            Return BadRequest(New ApiErrorResponse With {
                .ErrorCode = "PRODUCT_INACTIVE",
                .Message = "A deactivated product cannot be newly ordered.",
                .CorrelationId = correlationId
            })

        End Function

    End Class

End Namespace
