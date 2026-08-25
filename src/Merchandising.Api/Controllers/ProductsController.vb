' Merchandising.Api.Controllers.ProductsController
'
' P2-07: spec section 13 `GET/POST /api/v1/products`, `PUT /api/v1/products/
' {id}` - create, read, update, search. `POST /products/{id}/deactivate` is
' deliberately NOT here: it is P2-09's card, its own lifecycle/history-
' preservation logic rather than a flag on this controller.
'
' P2-08 adds ChangePrice, `PUT /api/v1/products/{id}/price` - not spec
' section 13's literal route table (which lists only GET/POST/PUT/deactivate
' under "Products"), but PolicyRegistry's own comment already scopes
' Products.Manage to "excluding price/cost", so a distinct route gated by
' Products.ChangePrice is what makes that separation real rather than
' aspirational. UpdateProductRequest (the PUT above) still has no Price/Cost
' properties at all - this is the only code path in this controller that
' can touch them, and Catalog.PriceChangeService is the only code path that
' writes PriceHistory.
'
' Uniqueness is enforced at both layers (plan.md section 7's exit
' criterion): a pre-check SELECT here gives a fast, friendly 409 in the
' common case, but ProductRepository.InsertAsync/UpdateAsync are what
' actually guarantee it, by catching the real unique-index violation
' (ERROR 1062, ADR-018) - a race that slips past the pre-check still cannot
' create two rows. See ProductRepository's own header.

Imports System.Collections.Generic
Imports System.Linq
Imports System.Security.Claims
Imports System.Threading.Tasks
Imports Merchandising.Api.Catalog
Imports Merchandising.Api.Middleware
Imports Merchandising.Api.Security
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Products
Imports Merchandising.Domain
Imports Merchandising.Domain.Entities
Imports Merchandising.Domain.Security
Imports Merchandising.Infrastructure.Data
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Mvc
Imports MySqlConnector

Namespace Controllers

    <ApiController>
    <Route("api/v1/products")>
    Public Class ProductsController
        Inherits ControllerBase

        Private Const MaxSkuLength As Integer = 255
        Private Const MaxNameLength As Integer = 255
        Private Const MaxBarcodeLength As Integer = 255
        Private Const DefaultPageSize As Integer = 25
        Private Const MaxPageSize As Integer = 100

        Private ReadOnly _connectionFactory As ConnectionFactory
        Private ReadOnly _priceChangeService As PriceChangeService

        Public Sub New(connectionFactory As ConnectionFactory, priceChangeService As PriceChangeService)
            _connectionFactory = connectionFactory
            _priceChangeService = priceChangeService
        End Sub

        ''' <summary>
        ''' Free-text search against SKU, barcode, and product name (spec
        ''' section 10.3), paginated. An absent/blank <paramref name="q"/>
        ''' returns every product.
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.ProductsRead)>
        <HttpGet>
        Public Async Function SearchProducts(
            <FromQuery(Name:="q")> q As String,
            <FromQuery> Optional page As Integer = 1,
            <FromQuery> Optional pageSize As Integer = DefaultPageSize) As Task(Of IActionResult)

            Dim effectivePage As Integer = If(page < 1, 1, page)
            Dim effectivePageSize As Integer = If(pageSize < 1, DefaultPageSize, Math.Min(pageSize, MaxPageSize))

            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync(HttpContext.RequestAborted)

                Dim result = Await ProductRepository.SearchAsync(connection, q, effectivePage, effectivePageSize, HttpContext.RequestAborted)

                Return Ok(New ProductSearchResponse With {
                    .Items = result.Items.Select(AddressOf ToResponse).ToList(),
                    .TotalCount = result.TotalCount,
                    .Page = effectivePage,
                    .PageSize = effectivePageSize
                })

            End Using

        End Function

        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.ProductsRead)>
        <HttpGet("{id}")>
        Public Async Function GetProduct(id As Integer) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()

            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync(HttpContext.RequestAborted)

                Dim product As Product = Await ProductRepository.GetByIdAsync(connection, id, HttpContext.RequestAborted)
                If product Is Nothing Then
                    Return NotFound(New ApiErrorResponse With {
                        .ErrorCode = "PRODUCT_NOT_FOUND",
                        .Message = $"No product with Id {id} exists.",
                        .CorrelationId = correlationId
                    })
                End If

                Return Ok(ToResponse(product))

            End Using

        End Function

        ''' <summary>
        ''' Creates a new, always-Active product. Duplicate SKU or active
        ''' barcode is rejected 409 with field-level detail (ADR-014); an
        ''' over-scale Price/Cost/ReorderLevel is rejected 400 before
        ''' binding (ADR-004.1); an unresolvable Category/Brand/Unit Id is
        ''' rejected 400 rather than surfacing a raw foreign-key error.
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.ProductsManage)>
        <AuditRequired>
        <HttpPost>
        Public Async Function CreateProduct(<FromBody> request As CreateProductRequest) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            Dim sku As String = If(request?.Sku, String.Empty).Trim()
            Dim name As String = If(request?.Name, String.Empty).Trim()
            Dim barcode As String = NullIfBlank(request?.Barcode)
            Dim reorderLevel As Decimal = If(request?.ReorderLevel, 0D)

            ValidateSku(sku, fieldErrors)
            ValidateName(name, fieldErrors)
            ValidateBarcodeShape(barcode, fieldErrors)

            Dim price As Decimal = ValidateMoney(If(request?.Price, 0D), "price", fieldErrors)
            Dim cost As Decimal = ValidateMoney(If(request?.Cost, 0D), "cost", fieldErrors)
            reorderLevel = ValidateQuantity(reorderLevel, "reorderLevel", fieldErrors)

            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync(HttpContext.RequestAborted)

                Await ValidateReferenceIdsAsync(connection, request?.CategoryId, request?.BrandId, request?.UnitId, fieldErrors)

                If fieldErrors.Count > 0 Then
                    Return BadRequest(New ApiErrorResponse With {
                        .ErrorCode = "VALIDATION_FAILED",
                        .Message = "The product could not be created because of a validation failure.",
                        .CorrelationId = correlationId,
                        .Errors = fieldErrors
                    })
                End If

                ' Pre-check: fast, friendly 409 in the common (non-racing)
                ' case. Not the guarantee - see this class's own header and
                ' ProductRepository's.
                If Await ProductRepository.FindBySkuAsync(connection, sku, HttpContext.RequestAborted) IsNot Nothing Then
                    Return DuplicateConflict("sku", "SKU", sku, correlationId)
                End If
                If barcode IsNot Nothing AndAlso Await ProductRepository.FindByActiveBarcodeAsync(connection, barcode, HttpContext.RequestAborted) IsNot Nothing Then
                    Return DuplicateConflict("barcode", "Barcode", barcode, correlationId)
                End If

                Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

                Dim transaction As MySqlTransaction = Await connection.BeginTransactionAsync(HttpContext.RequestAborted)

                Dim newProduct As New Product With {
                    .Sku = sku,
                    .Barcode = barcode,
                    .Name = name,
                    .Description = NullIfBlank(request?.Description),
                    .CategoryId = request?.CategoryId,
                    .BrandId = request?.BrandId,
                    .UnitId = request?.UnitId,
                    .Price = price,
                    .Cost = cost,
                    .ReorderLevel = reorderLevel
                }

                Dim insertResult = Await ProductRepository.InsertAsync(connection, transaction, newProduct, HttpContext.RequestAborted)

                If insertResult.Kind <> ProductWriteOutcomeKind.Success Then
                    ' The database won a race the pre-check missed. Roll back
                    ' and report the same controlled conflict as the
                    ' pre-check path - the caller cannot tell, and should
                    ' not be able to tell, which layer caught it.
                    Await transaction.RollbackAsync(HttpContext.RequestAborted)
                    Await transaction.DisposeAsync()

                    If insertResult.Kind = ProductWriteOutcomeKind.DuplicateSku Then
                        Return DuplicateConflict("sku", "SKU", sku, correlationId)
                    End If
                    Return DuplicateConflict("barcode", "Barcode", barcode, correlationId)
                End If

                Await AuditLogWriter.WriteAsync(
                    connection, actorUserId, "ProductCreated", sku, "Success", correlationId,
                    detail:=$"Id={insertResult.ProductId}, Name='{name}'",
                    cancellationToken:=HttpContext.RequestAborted,
                    transaction:=transaction)

                Await transaction.CommitAsync(HttpContext.RequestAborted)
                Await transaction.DisposeAsync()

                Dim created As Product = Await ProductRepository.GetByIdAsync(connection, insertResult.ProductId, HttpContext.RequestAborted)
                Return CreatedAtAction(NameOf(GetProduct), New With {.id = created.Id}, ToResponse(created))

            End Using

        End Function

        ''' <summary>
        ''' Updates the fields Products.Manage governs. Never Price, Cost,
        ''' IsActive, or Sku - see UpdateProductRequest's own header.
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.ProductsManage)>
        <AuditRequired>
        <HttpPut("{id}")>
        Public Async Function UpdateProduct(id As Integer, <FromBody> request As UpdateProductRequest) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            Dim name As String = If(request?.Name, String.Empty).Trim()
            Dim barcode As String = NullIfBlank(request?.Barcode)
            Dim reorderLevel As Decimal = If(request?.ReorderLevel, 0D)

            ValidateName(name, fieldErrors)
            ValidateBarcodeShape(barcode, fieldErrors)
            reorderLevel = ValidateQuantity(reorderLevel, "reorderLevel", fieldErrors)

            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync(HttpContext.RequestAborted)

                Await ValidateReferenceIdsAsync(connection, request?.CategoryId, request?.BrandId, request?.UnitId, fieldErrors)

                If fieldErrors.Count > 0 Then
                    Return BadRequest(New ApiErrorResponse With {
                        .ErrorCode = "VALIDATION_FAILED",
                        .Message = "The product could not be updated because of a validation failure.",
                        .CorrelationId = correlationId,
                        .Errors = fieldErrors
                    })
                End If

                Dim existing As Product = Await ProductRepository.GetByIdAsync(connection, id, HttpContext.RequestAborted)
                If existing Is Nothing Then
                    Return NotFound(New ApiErrorResponse With {
                        .ErrorCode = "PRODUCT_NOT_FOUND",
                        .Message = $"No product with Id {id} exists.",
                        .CorrelationId = correlationId
                    })
                End If

                If barcode IsNot Nothing Then
                    Dim holder As Product = Await ProductRepository.FindByActiveBarcodeAsync(connection, barcode, HttpContext.RequestAborted)
                    If holder IsNot Nothing AndAlso holder.Id <> id Then
                        Return DuplicateConflict("barcode", "Barcode", barcode, correlationId)
                    End If
                End If

                Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

                Dim transaction As MySqlTransaction = Await connection.BeginTransactionAsync(HttpContext.RequestAborted)

                Dim outcome As ProductWriteOutcomeKind = Await ProductRepository.UpdateAsync(
                    connection, transaction, id, name, NullIfBlank(request?.Description),
                    request?.CategoryId, request?.BrandId, request?.UnitId, barcode, reorderLevel,
                    HttpContext.RequestAborted)

                If outcome = ProductWriteOutcomeKind.NotFound Then
                    Await transaction.RollbackAsync(HttpContext.RequestAborted)
                    Await transaction.DisposeAsync()
                    Return NotFound(New ApiErrorResponse With {
                        .ErrorCode = "PRODUCT_NOT_FOUND",
                        .Message = $"No product with Id {id} exists.",
                        .CorrelationId = correlationId
                    })
                End If

                If outcome = ProductWriteOutcomeKind.DuplicateBarcode Then
                    Await transaction.RollbackAsync(HttpContext.RequestAborted)
                    Await transaction.DisposeAsync()
                    Return DuplicateConflict("barcode", "Barcode", barcode, correlationId)
                End If

                Await AuditLogWriter.WriteAsync(
                    connection, actorUserId, "ProductUpdated", existing.Sku, "Success", correlationId,
                    detail:=$"Id={id}",
                    cancellationToken:=HttpContext.RequestAborted,
                    transaction:=transaction)

                Await transaction.CommitAsync(HttpContext.RequestAborted)
                Await transaction.DisposeAsync()

                Dim updated As Product = Await ProductRepository.GetByIdAsync(connection, id, HttpContext.RequestAborted)
                Return Ok(ToResponse(updated))

            End Using

        End Function

        ''' <summary>
        ''' Changes a product's Price and/or Cost, atomically with a
        ''' PriceHistory row per changed field and an audit row (spec
        ''' section 11, P2-08). Never folded into UpdateProduct above -
        ''' Products.ChangePrice is a distinct policy from Products.Manage
        ''' (ADR-017), and this is the only route gated by it.
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.ProductsChangePrice)>
        <AuditRequired>
        <HttpPut("{id}/price")>
        Public Async Function ChangePrice(id As Integer, <FromBody> request As ChangeProductPriceRequest) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            If request Is Nothing OrElse (Not request.Price.HasValue AndAlso Not request.Cost.HasValue) Then
                fieldErrors("price") = {"At least one of price or cost must be supplied."}
            Else

                If request.Price.HasValue Then
                    ValidateMoney(request.Price.Value, "price", fieldErrors)
                End If
                If request.Cost.HasValue Then
                    ValidateMoney(request.Cost.Value, "cost", fieldErrors)
                End If

            End If

            If fieldErrors.Count > 0 Then
                Return BadRequest(New ApiErrorResponse With {
                    .ErrorCode = "VALIDATION_FAILED",
                    .Message = "The price change could not be applied because of a validation failure.",
                    .CorrelationId = correlationId,
                    .Errors = fieldErrors
                })
            End If

            Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

            Dim outcome As PriceChangeOutcome =
                Await _priceChangeService.ChangePriceAsync(
                    id, request.Price, request.Cost, actorUserId, correlationId,
                    cancellationToken:=HttpContext.RequestAborted)

            Select Case outcome.Kind

                Case PriceChangeOutcomeKind.Success
                    Return Ok(outcome.Response)

                Case PriceChangeOutcomeKind.ProductNotFound
                    Return NotFound(New ApiErrorResponse With {
                        .ErrorCode = "PRODUCT_NOT_FOUND",
                        .Message = $"No product with Id {id} exists.",
                        .CorrelationId = correlationId
                    })

                Case Else ' NoChange
                    Return BadRequest(New ApiErrorResponse With {
                        .ErrorCode = "VALIDATION_FAILED",
                        .Message = "Neither price nor cost differs from the product's current value.",
                        .CorrelationId = correlationId,
                        .Errors = New Dictionary(Of String, String()) From {{"price", New String() {"At least one supplied value must differ from the current one."}}}
                    })

            End Select

        End Function

        ' --------------------------------------------------------------- helpers

        ''' <summary>Friend, not Private - Catalog.PriceChangeService (same assembly) reuses this rather than duplicating the mapping.</summary>
        Friend Shared Function ToResponse(product As Product) As ProductResponse
            Return New ProductResponse With {
                .Id = product.Id,
                .Sku = product.Sku,
                .Name = product.Name,
                .Description = product.Description,
                .CategoryId = product.CategoryId,
                .BrandId = product.BrandId,
                .UnitId = product.UnitId,
                .Barcode = product.Barcode,
                .Price = product.Price,
                .Cost = product.Cost,
                .ReorderLevel = product.ReorderLevel,
                .IsActive = product.IsActive,
                .RowVersion = product.RowVersion,
                .CreatedAtUtc = product.CreatedAtUtc,
                .UpdatedAtUtc = product.UpdatedAtUtc
            }
        End Function

        Private Function DuplicateConflict(field As String, label As String, value As String, correlationId As String) As IActionResult
            Return Conflict(New ApiErrorResponse With {
                .ErrorCode = If(field = "sku", "DUPLICATE_SKU", "DUPLICATE_BARCODE"),
                .Message = $"{label} '{value}' is already in use by another active product.",
                .CorrelationId = correlationId,
                .Errors = New Dictionary(Of String, String()) From {{field, New String() {$"{label} must be unique."}}}
            })
        End Function

        Private Shared Function NullIfBlank(value As String) As String
            If String.IsNullOrWhiteSpace(value) Then
                Return Nothing
            End If
            Return value.Trim()
        End Function

        Private Shared Sub ValidateSku(sku As String, fieldErrors As Dictionary(Of String, String()))
            If String.IsNullOrWhiteSpace(sku) Then
                fieldErrors("sku") = {"SKU is required."}
            ElseIf sku.Length > MaxSkuLength Then
                fieldErrors("sku") = {$"SKU must be {MaxSkuLength} characters or fewer."}
            End If
        End Sub

        Private Shared Sub ValidateName(name As String, fieldErrors As Dictionary(Of String, String()))
            If String.IsNullOrWhiteSpace(name) Then
                fieldErrors("name") = {"Name is required."}
            ElseIf name.Length > MaxNameLength Then
                fieldErrors("name") = {$"Name must be {MaxNameLength} characters or fewer."}
            End If
        End Sub

        Private Shared Sub ValidateBarcodeShape(barcode As String, fieldErrors As Dictionary(Of String, String()))
            If barcode IsNot Nothing AndAlso barcode.Length > MaxBarcodeLength Then
                fieldErrors("barcode") = {$"Barcode must be {MaxBarcodeLength} characters or fewer."}
            End If
        End Sub

        ''' <summary>Validates scale (ADR-004.1) and non-negativity; returns the value unchanged when valid.</summary>
        Private Shared Function ValidateMoney(value As Decimal, field As String, fieldErrors As Dictionary(Of String, String())) As Decimal

            If value < 0D Then
                fieldErrors(field) = {$"{field} must not be negative."}
                Return value
            End If

            Try
                Return DecimalScaleGuard.EnsureMoneyScale(value)
            Catch ex As ArgumentException
                fieldErrors(field) = {ex.Message}
                Return value
            End Try

        End Function

        Private Shared Function ValidateQuantity(value As Decimal, field As String, fieldErrors As Dictionary(Of String, String())) As Decimal

            If value < 0D Then
                fieldErrors(field) = {$"{field} must not be negative."}
                Return value
            End If

            Try
                Return DecimalScaleGuard.EnsureQuantityScale(value)
            Catch ex As ArgumentException
                fieldErrors(field) = {ex.Message}
                Return value
            End Try

        End Function

        Private Shared Async Function ValidateReferenceIdsAsync(
            connection As MySqlConnection, categoryId As Integer?, brandId As Integer?, unitId As Integer?,
            fieldErrors As Dictionary(Of String, String())) As Task

            If categoryId.HasValue AndAlso Not Await ProductRepository.CategoryExistsAsync(connection, categoryId.Value) Then
                fieldErrors("categoryId") = {$"No category with Id {categoryId.Value} exists."}
            End If
            If brandId.HasValue AndAlso Not Await ProductRepository.BrandExistsAsync(connection, brandId.Value) Then
                fieldErrors("brandId") = {$"No brand with Id {brandId.Value} exists."}
            End If
            If unitId.HasValue AndAlso Not Await ProductRepository.UnitExistsAsync(connection, unitId.Value) Then
                fieldErrors("unitId") = {$"No unit with Id {unitId.Value} exists."}
            End If

        End Function

    End Class

End Namespace
