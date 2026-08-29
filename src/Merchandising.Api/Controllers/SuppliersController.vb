' Merchandising.Api.Controllers.SuppliersController
'
' P2-10: spec section 13 `GET/POST /api/v1/suppliers`, `PUT /api/v1/
' suppliers/{id}` - create, read, update, search, plus deactivate/reactivate
' mirroring P2-09's ProductLifecycleService shape (confirmed with the user
' before implementing: Suppliers gets the "same lifecycle rules" this card's
' own title names).
'
' Uniqueness is enforced at both layers (plan.md section 7's exit
' criterion, same as P2-07's Products precedent): a pre-check SELECT here
' gives a fast, friendly 409 in the common case, but SupplierRepository.
' InsertAsync/UpdateAsync are what actually guarantee it, by catching the
' real unique-index violation (ERROR 1062) - a race that slips past the
' pre-check still cannot create two rows.
'
' Gated by Suppliers.Read/Suppliers.Manage - both already registered in
' PolicyRegistry (ProcurementAndAbove: SuperAdmin, Admin, ProcurementOfficer)
' ahead of this card; no PolicyRegistry change was needed to satisfy "a
' Procurement Officer may maintain suppliers; a Cashier may not".

Imports System.Collections.Generic
Imports System.Data
Imports System.Linq
Imports System.Security.Claims
Imports System.Threading.Tasks
Imports Merchandising.Api.Catalog
Imports Merchandising.Api.Middleware
Imports Merchandising.Api.Security
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Suppliers
Imports Merchandising.Domain.Entities
Imports Merchandising.Domain.Security
Imports Merchandising.Infrastructure.Data
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Mvc
Imports MySqlConnector

Namespace Controllers

    <ApiController>
    <Route("api/v1/suppliers")>
    Public Class SuppliersController
        Inherits ControllerBase

        Private Const MaxNameLength As Integer = 255
        Private Const MaxContactNameLength As Integer = 255
        Private Const MaxPhoneLength As Integer = 50
        Private Const MaxEmailLength As Integer = 255
        Private Const MaxAddressLength As Integer = 500
        Private Const DefaultPageSize As Integer = 25
        Private Const MaxPageSize As Integer = 100

        Private ReadOnly _connectionFactory As ConnectionFactory
        Private ReadOnly _lifecycleService As SupplierLifecycleService

        Public Sub New(connectionFactory As ConnectionFactory, lifecycleService As SupplierLifecycleService)
            _connectionFactory = connectionFactory
            _lifecycleService = lifecycleService
        End Sub

        ''' <summary>
        ''' Free-text search against supplier Name, paginated. An absent/
        ''' blank <paramref name="q"/> returns every supplier.
        ''' <paramref name="includeInactive"/> defaults False, mirroring
        ''' ProductsController.SearchProducts.
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.SuppliersRead)>
        <HttpGet>
        Public Async Function SearchSuppliers(
            <FromQuery(Name:="q")> q As String,
            <FromQuery> Optional page As Integer = 1,
            <FromQuery> Optional pageSize As Integer = DefaultPageSize,
            <FromQuery> Optional includeInactive As Boolean = False) As Task(Of IActionResult)

            Dim effectivePage As Integer = If(page < 1, 1, page)
            Dim effectivePageSize As Integer = If(pageSize < 1, DefaultPageSize, Math.Min(pageSize, MaxPageSize))

            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync(HttpContext.RequestAborted)

                Dim result = Await SupplierRepository.SearchAsync(
                    connection, q, effectivePage, effectivePageSize, includeInactive, HttpContext.RequestAborted)

                Return Ok(New SupplierSearchResponse With {
                    .Items = result.Items.Select(AddressOf ToResponse).ToList(),
                    .TotalCount = result.TotalCount,
                    .Page = effectivePage,
                    .PageSize = effectivePageSize
                })

            End Using

        End Function

        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.SuppliersRead)>
        <HttpGet("{id}")>
        Public Async Function GetSupplier(id As Integer) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()

            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync(HttpContext.RequestAborted)

                Dim supplier As Supplier = Await SupplierRepository.GetByIdAsync(connection, id, HttpContext.RequestAborted)
                If supplier Is Nothing Then
                    Return NotFound(New ApiErrorResponse With {
                        .ErrorCode = "SUPPLIER_NOT_FOUND",
                        .Message = $"No supplier with Id {id} exists.",
                        .CorrelationId = correlationId
                    })
                End If

                Return Ok(ToResponse(supplier))

            End Using

        End Function

        ''' <summary>
        ''' Creates a new, always-Active supplier. Duplicate name is
        ''' rejected 409 with field-level detail (ADR-014, same shape as
        ''' P2-07's duplicate-SKU handling).
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.SuppliersManage)>
        <AuditRequired>
        <HttpPost>
        Public Async Function CreateSupplier(<FromBody> request As CreateSupplierRequest) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            Dim name As String = If(request?.Name, String.Empty).Trim()
            Dim contactName As String = NullIfBlank(request?.ContactName)
            Dim phone As String = NullIfBlank(request?.Phone)
            Dim email As String = NullIfBlank(request?.Email)
            Dim address As String = NullIfBlank(request?.Address)

            ValidateName(name, fieldErrors)
            ValidateLength(contactName, "contactName", MaxContactNameLength, fieldErrors)
            ValidateLength(phone, "phone", MaxPhoneLength, fieldErrors)
            ValidateLength(email, "email", MaxEmailLength, fieldErrors)
            ValidateLength(address, "address", MaxAddressLength, fieldErrors)

            If fieldErrors.Count > 0 Then
                Return BadRequest(New ApiErrorResponse With {
                    .ErrorCode = "VALIDATION_FAILED",
                    .Message = "The supplier could not be created because of a validation failure.",
                    .CorrelationId = correlationId,
                    .Errors = fieldErrors
                })
            End If

            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync(HttpContext.RequestAborted)

                ' Pre-check: fast, friendly 409 in the common (non-racing)
                ' case. Not the guarantee - see this class's own header and
                ' SupplierRepository's.
                If Await SupplierRepository.FindByNameAsync(connection, name, HttpContext.RequestAborted) IsNot Nothing Then
                    Return DuplicateNameConflict(name, correlationId)
                End If

                Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

                ' P4-04/CARRY-03/ADR-006 amendment: the session-level
                ' READ-COMMITTED setting does not survive BeginTransaction -
                ' it must be passed here explicitly (measured at P3-03).
                Dim transaction As MySqlTransaction = Await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, HttpContext.RequestAborted)

                Dim newSupplier As New Supplier With {
                    .Name = name,
                    .ContactName = contactName,
                    .Phone = phone,
                    .Email = email,
                    .Address = address
                }

                Dim insertResult = Await SupplierRepository.InsertAsync(connection, transaction, newSupplier, HttpContext.RequestAborted)

                If insertResult.Kind <> SupplierWriteOutcomeKind.Success Then
                    ' The database won a race the pre-check missed. Roll back
                    ' and report the same controlled conflict as the
                    ' pre-check path - the caller cannot tell, and should
                    ' not be able to tell, which layer caught it.
                    Await transaction.RollbackAsync(HttpContext.RequestAborted)
                    Await transaction.DisposeAsync()
                    Return DuplicateNameConflict(name, correlationId)
                End If

                Await AuditLogWriter.WriteAsync(
                    connection, actorUserId, "SupplierCreated", name, "Success", correlationId,
                    detail:=$"Id={insertResult.SupplierId}",
                    cancellationToken:=HttpContext.RequestAborted,
                    transaction:=transaction)

                Await transaction.CommitAsync(HttpContext.RequestAborted)
                Await transaction.DisposeAsync()

                Dim created As Supplier = Await SupplierRepository.GetByIdAsync(connection, insertResult.SupplierId, HttpContext.RequestAborted)
                Return CreatedAtAction(NameOf(GetSupplier), New With {.id = created.Id}, ToResponse(created))

            End Using

        End Function

        ''' <summary>
        ''' Updates a supplier's name and contact fields. Never IsActive -
        ''' the deactivate/reactivate endpoints below own that.
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.SuppliersManage)>
        <AuditRequired>
        <HttpPut("{id}")>
        Public Async Function UpdateSupplier(id As Integer, <FromBody> request As UpdateSupplierRequest) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            Dim name As String = If(request?.Name, String.Empty).Trim()
            Dim contactName As String = NullIfBlank(request?.ContactName)
            Dim phone As String = NullIfBlank(request?.Phone)
            Dim email As String = NullIfBlank(request?.Email)
            Dim address As String = NullIfBlank(request?.Address)

            ValidateName(name, fieldErrors)
            ValidateLength(contactName, "contactName", MaxContactNameLength, fieldErrors)
            ValidateLength(phone, "phone", MaxPhoneLength, fieldErrors)
            ValidateLength(email, "email", MaxEmailLength, fieldErrors)
            ValidateLength(address, "address", MaxAddressLength, fieldErrors)

            If fieldErrors.Count > 0 Then
                Return BadRequest(New ApiErrorResponse With {
                    .ErrorCode = "VALIDATION_FAILED",
                    .Message = "The supplier could not be updated because of a validation failure.",
                    .CorrelationId = correlationId,
                    .Errors = fieldErrors
                })
            End If

            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync(HttpContext.RequestAborted)

                Dim existing As Supplier = Await SupplierRepository.GetByIdAsync(connection, id, HttpContext.RequestAborted)
                If existing Is Nothing Then
                    Return NotFound(New ApiErrorResponse With {
                        .ErrorCode = "SUPPLIER_NOT_FOUND",
                        .Message = $"No supplier with Id {id} exists.",
                        .CorrelationId = correlationId
                    })
                End If

                Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

                ' P4-04/CARRY-03/ADR-006 amendment: the session-level
                ' READ-COMMITTED setting does not survive BeginTransaction -
                ' it must be passed here explicitly (measured at P3-03).
                Dim transaction As MySqlTransaction = Await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, HttpContext.RequestAborted)

                Dim outcome As SupplierWriteOutcomeKind = Await SupplierRepository.UpdateAsync(
                    connection, transaction, id, name, contactName, phone, email, address, HttpContext.RequestAborted)

                If outcome = SupplierWriteOutcomeKind.NotFound Then
                    Await transaction.RollbackAsync(HttpContext.RequestAborted)
                    Await transaction.DisposeAsync()
                    Return NotFound(New ApiErrorResponse With {
                        .ErrorCode = "SUPPLIER_NOT_FOUND",
                        .Message = $"No supplier with Id {id} exists.",
                        .CorrelationId = correlationId
                    })
                End If

                If outcome = SupplierWriteOutcomeKind.DuplicateName Then
                    Await transaction.RollbackAsync(HttpContext.RequestAborted)
                    Await transaction.DisposeAsync()
                    Return DuplicateNameConflict(name, correlationId)
                End If

                Await AuditLogWriter.WriteAsync(
                    connection, actorUserId, "SupplierUpdated", existing.Name, "Success", correlationId,
                    detail:=$"Id={id}",
                    cancellationToken:=HttpContext.RequestAborted,
                    transaction:=transaction)

                Await transaction.CommitAsync(HttpContext.RequestAborted)
                Await transaction.DisposeAsync()

                Dim updated As Supplier = Await SupplierRepository.GetByIdAsync(connection, id, HttpContext.RequestAborted)
                Return Ok(ToResponse(updated))

            End Using

        End Function

        ''' <summary>
        ''' Deactivates a supplier (spec section 12: "master data is
        ''' deactivated where possible"). Never deletes a row - see
        ''' SupplierLifecycleService's own header. Deactivating an
        ''' already-inactive supplier is a controlled 400, not a silent
        ''' success.
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.SuppliersManage)>
        <AuditRequired>
        <HttpPost("{id}/deactivate")>
        Public Async Function DeactivateSupplier(id As Integer) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

            Dim outcome As SupplierLifecycleOutcome = Await _lifecycleService.DeactivateAsync(id, actorUserId, correlationId, HttpContext.RequestAborted)
            Return LifecycleResult(outcome, id, correlationId, "SUPPLIER_ALREADY_INACTIVE", "This supplier is already inactive.")

        End Function

        ''' <summary>
        ''' Reactivates a previously deactivated supplier - the same row,
        ''' same Id, no duplicate created. Symmetric with Deactivate above,
        ''' gated by the same policy.
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.SuppliersManage)>
        <AuditRequired>
        <HttpPost("{id}/reactivate")>
        Public Async Function ReactivateSupplier(id As Integer) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

            Dim outcome As SupplierLifecycleOutcome = Await _lifecycleService.ReactivateAsync(id, actorUserId, correlationId, HttpContext.RequestAborted)
            Return LifecycleResult(outcome, id, correlationId, "SUPPLIER_ALREADY_ACTIVE", "This supplier is already active.")

        End Function

        Private Function LifecycleResult(
            outcome As SupplierLifecycleOutcome, id As Integer, correlationId As String,
            noChangeErrorCode As String, noChangeMessage As String) As IActionResult

            Select Case outcome.Kind

                Case SupplierLifecycleOutcomeKind.Success
                    Return Ok(outcome.Response)

                Case SupplierLifecycleOutcomeKind.SupplierNotFound
                    Return NotFound(New ApiErrorResponse With {
                        .ErrorCode = "SUPPLIER_NOT_FOUND",
                        .Message = $"No supplier with Id {id} exists.",
                        .CorrelationId = correlationId
                    })

                Case Else ' NoChange
                    Return BadRequest(New ApiErrorResponse With {
                        .ErrorCode = noChangeErrorCode,
                        .Message = noChangeMessage,
                        .CorrelationId = correlationId
                    })

            End Select

        End Function

        ' --------------------------------------------------------------- helpers

        ''' <summary>Friend, not Private - Catalog.SupplierLifecycleService (same assembly) reuses this rather than duplicating the mapping.</summary>
        Friend Shared Function ToResponse(supplier As Supplier) As SupplierResponse
            Return New SupplierResponse With {
                .Id = supplier.Id,
                .Name = supplier.Name,
                .ContactName = supplier.ContactName,
                .Phone = supplier.Phone,
                .Email = supplier.Email,
                .Address = supplier.Address,
                .IsActive = supplier.IsActive,
                .RowVersion = supplier.RowVersion,
                .CreatedAtUtc = supplier.CreatedAtUtc,
                .UpdatedAtUtc = supplier.UpdatedAtUtc
            }
        End Function

        Private Function DuplicateNameConflict(name As String, correlationId As String) As IActionResult
            Return Conflict(New ApiErrorResponse With {
                .ErrorCode = "DUPLICATE_SUPPLIER_NAME",
                .Message = $"Supplier name '{name}' is already in use.",
                .CorrelationId = correlationId,
                .Errors = New Dictionary(Of String, String()) From {{"name", New String() {"Name must be unique."}}}
            })
        End Function

        Private Shared Function NullIfBlank(value As String) As String
            If String.IsNullOrWhiteSpace(value) Then
                Return Nothing
            End If
            Return value.Trim()
        End Function

        Private Shared Sub ValidateName(name As String, fieldErrors As Dictionary(Of String, String()))
            If String.IsNullOrWhiteSpace(name) Then
                fieldErrors("name") = {"Name is required."}
            ElseIf name.Length > MaxNameLength Then
                fieldErrors("name") = {$"Name must be {MaxNameLength} characters or fewer."}
            End If
        End Sub

        Private Shared Sub ValidateLength(value As String, field As String, maxLength As Integer, fieldErrors As Dictionary(Of String, String()))
            If value IsNot Nothing AndAlso value.Length > maxLength Then
                fieldErrors(field) = {$"{field} must be {maxLength} characters or fewer."}
            End If
        End Sub

    End Class

End Namespace
