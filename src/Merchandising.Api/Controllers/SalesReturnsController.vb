' Merchandising.Api.Controllers.SalesReturnsController
'
' P5-11: spec section 10.3's completed-sale return, three routes:
'
'   POST /api/v1/sales/{saleId}/returns                    - record a
'                     return against one sale (spec's own `/sales/{id}/
'                     returns` shape). Below the configured threshold it
'                     completes immediately (Sales Returns.Create); at or
'                     above it, it lands PendingApproval and awaits
'                     ApproveExceptional/RejectExceptional. Gated by
'                     SalesReturns.Create.
'   POST /api/v1/sales/returns/{id}/approve-exceptional    - approves AND
'                     completes a PendingApproval return. Gated by
'                     SalesReturns.ApproveExceptional PLUS the self-approval
'                     veto (below).
'   POST /api/v1/sales/returns/{id}/reject-exceptional     - rejects a
'                     PendingApproval return, no stock or refund effect.
'                     Gated by SalesReturns.ApproveExceptional's own role
'                     set alone - spec section 9 restricts APPROVING one's
'                     own exceptional return, not rejecting it, the same
'                     reasoning AdjustmentsController.RejectAdjustment gives
'                     (CLAUDE.md: don't invent a rule the spec never
'                     states) - checked IMPERATIVELY, not via the
'                     declarative &lt;Authorize(Policy:=...)&gt; attribute
'                     AdjustmentsController.RejectAdjustment itself uses.
'                     See RejectExceptional's own header for the real defect
'                     that difference exists to avoid.
'
' A RETURN'S OWN ROUTE SEGMENT IS "returns", NOT NESTED UNDER A SALE - the
' approve/reject actions operate on the return by its own Id, not by the
' sale it came from, so there is nothing for a {saleId} route segment to
' name. This still lives under api/v1/sales (this card's own file list
' names the controller SalesReturnsController, not a change to
' SalesController) because a sales return is conceptually part of the sales
' surface, the same way ReceivingController hosts purchase returns under
' api/v1/receipts rather than a separate top-level resource.
'
' APPROVE-EXCEPTIONAL FOLLOWS AdjustmentsController.ApproveAdjustment'S
' PATTERN EXACTLY (ADR-017 section 6, this card's Done-when box 6: "reusing
' ... not a new check"). SalesReturns.ApproveExceptional carries a
' resource-based SelfApprovalRequirement (AuthorizationPolicyRegistration),
' which a declarative &lt;Authorize(Policy:=...)&gt; attribute cannot
' evaluate - it runs before this method body, with no return loaded, so
' SelfApprovalHandler (typed to IOwnershipResource) would never see a
' matching resource and would deny EVERY caller, always. So
' ApproveExceptional carries only &lt;Authorize&gt; (authentication), checks
' role membership itself against the SAME PolicyRegistry.Definitions list
' AuthorizationPolicyRegistration registers BEFORE the return is ever
' loaded - a role-disallowed caller must get the identical 403 whether id 1
' or id 999999999 is requested, never a 404 that would leak which ids exist -
' then loads the return as a courtesy read (ReturnedByUserId is immutable
' after creation, so no lock is needed for the value this decision depends
' on; SalesReturnService.ApproveExceptionalAsync re-locks and re-decides the
' STATUS transition inside its own transaction regardless - that is the
' check that counts) and calls
' IAuthorizationService.AuthorizeAsync(User, salesReturn, SalesReturns.ApproveExceptional)
' itself, evaluating role membership AND the ownership veto together.
' Registered in AuthorizationMatrixTests' AuthenticatedNoPolicyAllowlist for
' exactly this reason - still fully policy-gated, just imperatively.

Imports System.Collections.Generic
Imports System.Linq
Imports System.Security.Claims
Imports System.Threading.Tasks
Imports Merchandising.Api.Middleware
Imports Merchandising.Api.Sales
Imports Merchandising.Api.Security
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Sales
Imports Merchandising.Domain
Imports Merchandising.Domain.Entities
Imports Merchandising.Domain.Sales
Imports Merchandising.Domain.Security
Imports Merchandising.Infrastructure.Data
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Mvc
Imports MySqlConnector

Namespace Controllers

    <ApiController>
    <Route("api/v1/sales")>
    Public Class SalesReturnsController
        Inherits ControllerBase

        ''' <summary>Matches SalesReturns.Reason VARCHAR(255) (0012).</summary>
        Private Const MaxReasonLength As Integer = 255

        Private ReadOnly _connectionFactory As ConnectionFactory
        Private ReadOnly _salesReturnService As SalesReturnService
        Private ReadOnly _authorizationService As IAuthorizationService

        Public Sub New(
            connectionFactory As ConnectionFactory,
            salesReturnService As SalesReturnService,
            authorizationService As IAuthorizationService)

            _connectionFactory = connectionFactory
            _salesReturnService = salesReturnService
            _authorizationService = authorizationService

        End Sub

        ''' <summary>
        ''' Records a return against <paramref name="saleId"/>. Below the
        ''' configured threshold it completes immediately (201,
        ''' Status=Completed, RefundMethod/RefundAmount set); at or above
        ''' it, it is recorded PendingApproval (201, Status=PendingApproval,
        ''' RefundMethod/RefundAmount Nothing) and awaits
        ''' ApproveExceptional/RejectExceptional.
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.SalesReturnsCreate)>
        <AuditRequired>
        <HttpPost("{saleId}/returns")>
        Public Async Function RecordReturn(
            saleId As Integer, <FromBody> request As CreateSalesReturnRequest) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            Dim refundMethod As PaymentMethod = PaymentMethod.Cash
            ValidateRequestShape(request, fieldErrors, refundMethod)

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

            Dim outcome As SalesReturnOutcome =
                Await _salesReturnService.RecordAsync(
                    saleId, request.Lines, request.Reason, refundMethod.ToString(), actorUserId, correlationId,
                    request.IdempotencyKey, cancellationToken:=HttpContext.RequestAborted)

            Select Case outcome.Kind

                Case SalesReturnOutcomeKind.Created
                    Return Created($"/api/v1/sales/returns/{outcome.Response.Id}", outcome.Response)

                Case SalesReturnOutcomeKind.Replayed
                    ' ADR-007: the ORIGINAL committed result, byte for byte.
                    ' 200, not 201 - this request returned nothing new.
                    Return Content(outcome.ReplayPayload, "application/json")

                Case SalesReturnOutcomeKind.SaleNotFound
                    Return NotFound(New ApiErrorResponse With {
                        .ErrorCode = "SALE_NOT_FOUND",
                        .Message = $"No sale with Id {saleId} exists.",
                        .CorrelationId = correlationId
                    })

                Case SalesReturnOutcomeKind.LineNotFound
                    Return ValidationFailed(
                        New Dictionary(Of String, String()) From {
                            {LineFieldFor(request, outcome.OffendingSaleLineId),
                             New String() {"No such sale line exists on this sale."}}},
                        correlationId)

                Case Else ' OverReturned
                    Return Conflict(New ApiErrorResponse With {
                        .ErrorCode = SalesReturnOutcome.OverReturnedErrorCode,
                        .Message = $"The quantity requested for {LineFieldFor(request, outcome.OffendingSaleLineId)} " &
                                   "would exceed what remains available to return (sold less prior returns) on that line.",
                        .CorrelationId = correlationId
                    })

            End Select

        End Function

        ''' <summary>Approves and completes a PendingApproval return. See this class's header for why the self-approval veto is evaluated here, imperatively, rather than declaratively.</summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName)>
        <AuditRequired>
        <HttpPost("returns/{id}/approve-exceptional")>
        Public Async Function ApproveExceptional(id As Integer, <FromBody> request As ApproveExceptionalSalesReturnRequest) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()

            Dim refundMethod As PaymentMethod = PaymentMethod.Cash
            Dim fieldErrors As New Dictionary(Of String, String())
            ValidateRefundMethod(If(request?.RefundMethod, String.Empty), fieldErrors, refundMethod)

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim allowedRoles As IReadOnlyList(Of String) =
                PolicyRegistry.Definitions.Single(
                    Function(d) d.PolicyName = PolicyRegistry.Names.SalesReturnsApproveExceptional).AllowedRoles

            If Not allowedRoles.Any(AddressOf User.IsInRole) Then
                Return Await DeniedAsync(id, correlationId, "Actor's role does not hold SalesReturns.ApproveExceptional.")
            End If

            Dim salesReturn As SalesReturn

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(HttpContext.RequestAborted)
                salesReturn = Await SalesReturnRepository.GetByIdAsync(connection, id, HttpContext.RequestAborted)
            End Using

            If salesReturn Is Nothing Then
                Return NotFound(New ApiErrorResponse With {
                    .ErrorCode = "SALES_RETURN_NOT_FOUND",
                    .Message = $"No sales return with Id {id} exists.",
                    .CorrelationId = correlationId
                })
            End If

            Dim authorization As AuthorizationResult =
                Await _authorizationService.AuthorizeAsync(User, salesReturn, PolicyRegistry.Names.SalesReturnsApproveExceptional)

            If Not authorization.Succeeded Then
                Return Await DeniedAsync(id, correlationId, "Self-approval prohibited (spec section 9).")
            End If

            Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

            Dim outcome As SalesReturnOutcome =
                Await _salesReturnService.ApproveExceptionalAsync(
                    id, refundMethod.ToString(), actorUserId, correlationId, cancellationToken:=HttpContext.RequestAborted)

            Return TransitionResult(outcome, id, correlationId)

        End Function

        ''' <summary>
        ''' Rejects a PendingApproval return. Role-only check - no self-
        ''' approval veto (this class's header explains why) - but that
        ''' check CANNOT be the declarative
        ''' &lt;Authorize(Policy:=SalesReturns.ApproveExceptional)&gt; attribute,
        ''' even though no veto is wanted here: that policy now carries
        ''' ADR-017 section 6's resource-based SelfApprovalRequirement
        ''' (this card added it), and ASP.NET Core's endpoint-routing
        ''' pipeline evaluates a declarative attribute's policy against
        ''' whatever resource IT supplies (never an
        ''' IOwnershipResource-typed one) - SelfApprovalHandler is typed to
        ''' IOwnershipResource, so it never runs for a mismatched resource
        ''' type, the requirement is never marked Succeeded by ANY handler,
        ''' and the policy is refused for EVERY caller regardless of role.
        ''' Caught by ApproveExceptional_DifferentAuthorizedUser's own
        ''' sibling test for this action, which failed 403 for an Admin
        ''' fixture before this method was rewritten - not a hypothetical.
        ''' So this repeats ApproveExceptional's own role-membership check
        ''' verbatim and stops there, never reaching AuthorizeAsync at all -
        ''' the same role list, just without the veto.
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName)>
        <AuditRequired>
        <HttpPost("returns/{id}/reject-exceptional")>
        Public Async Function RejectExceptional(id As Integer) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()

            Dim allowedRoles As IReadOnlyList(Of String) =
                PolicyRegistry.Definitions.Single(
                    Function(d) d.PolicyName = PolicyRegistry.Names.SalesReturnsApproveExceptional).AllowedRoles

            If Not allowedRoles.Any(AddressOf User.IsInRole) Then
                Return Await DeniedAsync(id, correlationId, "Actor's role does not hold SalesReturns.ApproveExceptional.")
            End If

            Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

            Dim outcome As SalesReturnOutcome =
                Await _salesReturnService.RejectExceptionalAsync(id, actorUserId, correlationId, cancellationToken:=HttpContext.RequestAborted)

            Return TransitionResult(outcome, id, correlationId)

        End Function

        ' --------------------------------------------------------- validation

        Private Shared Sub ValidateRequestShape(
            request As CreateSalesReturnRequest, fieldErrors As Dictionary(Of String, String()), ByRef parsedRefundMethod As PaymentMethod)

            If request Is Nothing Then
                fieldErrors("request") = {"A request body is required."}
                Return
            End If

            If String.IsNullOrWhiteSpace(request.Reason) Then
                fieldErrors("reason") = {"A reason is required."}
            ElseIf request.Reason.Length > MaxReasonLength Then
                fieldErrors("reason") = {$"Reason must be {MaxReasonLength} characters or fewer."}
            End If

            If String.IsNullOrWhiteSpace(request.IdempotencyKey) Then
                fieldErrors("idempotencyKey") = {"Idempotency key is required."}
            ElseIf Not IsWellFormedIdempotencyKey(request.IdempotencyKey) Then
                fieldErrors("idempotencyKey") = {
                    "Idempotency key must be a UUID in the canonical 36-character form, for example " &
                    "3f2504e0-4f89-41d3-9a0c-0305e82c3301."}
            End If

            ValidateRefundMethod(request.RefundMethod, fieldErrors, parsedRefundMethod)

            If request.Lines Is Nothing OrElse request.Lines.Count = 0 Then
                fieldErrors("lines") = {"A sales return must have at least one line."}
                Return
            End If

            Dim seenLineIds As New HashSet(Of Integer)

            For index As Integer = 0 To request.Lines.Count - 1

                Dim line As CreateSalesReturnLineRequest = request.Lines(index)

                If line Is Nothing Then
                    fieldErrors($"lines[{index}]") = {"A line is required."}
                    Continue For
                End If

                If line.SaleLineId <= 0 Then
                    fieldErrors($"lines[{index}].saleLineId") = {"A sale line is required."}
                ElseIf Not seenLineIds.Add(line.SaleLineId) Then
                    fieldErrors($"lines[{index}].saleLineId") = {"This sale line is already named by another line in this return."}
                End If

                ValidateQuantity(line.QuantityReturned, $"lines[{index}].quantityReturned", fieldErrors)

            Next

        End Sub

        ''' <summary>Quantity must be positive (CK_SalesReturnLines_QuantityReturned) and already at DECIMAL(19,3) scale.</summary>
        Private Shared Sub ValidateQuantity(
            value As Decimal, field As String, fieldErrors As Dictionary(Of String, String()))

            If value <= 0D Then
                fieldErrors(field) = {"Quantity returned must be greater than zero."}
            ElseIf Not DecimalScaleGuard.IsAtQuantityScale(value) Then
                fieldErrors(field) = {$"Quantity returned must have no more than {DecimalScaleGuard.QuantityScale} decimal places."}
            End If

        End Sub

        Private Shared Sub ValidateRefundMethod(
            value As String, fieldErrors As Dictionary(Of String, String()), ByRef parsedMethod As PaymentMethod)

            If Not TryParsePaymentMethod(value, parsedMethod) Then
                fieldErrors("refundMethod") = {
                    $"Refund method must be one of {String.Join(", ", [Enum].GetNames(GetType(PaymentMethod)))}."}
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

        ''' <summary>The field name for a line the SERVICE rejected. The service reports a SaleLineId, not a position; this maps it back to the line the caller wrote - ReceivingController's identical LineFieldFor.</summary>
        Private Shared Function LineFieldFor(request As CreateSalesReturnRequest, saleLineId As Integer) As String

            For index As Integer = 0 To request.Lines.Count - 1
                If request.Lines(index).SaleLineId = saleLineId Then
                    Return $"lines[{index}].saleLineId"
                End If
            Next

            Return "lines"

        End Function

        ' ------------------------------------------------------------ results

        Private Function ValidationFailed(
            fieldErrors As Dictionary(Of String, String()), correlationId As String) As IActionResult

            Return BadRequest(New ApiErrorResponse With {
                .ErrorCode = "VALIDATION_FAILED",
                .Message = "The sales return could not be processed because of a validation failure.",
                .CorrelationId = correlationId,
                .Errors = fieldErrors
            })

        End Function

        Private Function TransitionResult(outcome As SalesReturnOutcome, id As Integer, correlationId As String) As IActionResult

            Select Case outcome.Kind

                Case SalesReturnOutcomeKind.Created
                    Return Ok(outcome.Response)

                Case SalesReturnOutcomeKind.NotFound
                    Return NotFound(New ApiErrorResponse With {
                        .ErrorCode = "SALES_RETURN_NOT_FOUND",
                        .Message = $"No sales return with Id {id} exists.",
                        .CorrelationId = correlationId
                    })

                Case Else ' NotPending
                    Return Conflict(New ApiErrorResponse With {
                        .ErrorCode = SalesReturnOutcome.NotPendingErrorCode,
                        .Message = "This sales return is not pending approval - it may already have been approved or rejected.",
                        .CorrelationId = correlationId
                    })

            End Select

        End Function

        ''' <summary>
        ''' Shared 403 path for ApproveExceptional's two denial reasons
        ''' (wrong role, self-approval) - the same shape
        ''' AdjustmentsController.DeniedAsync uses. Audited even though
        ''' AuditPipelineFilter only forces auditing on a 2xx result - spec
        ''' section 9's control table asks a privileged action for an audit
        ''' record regardless of outcome.
        ''' </summary>
        Private Async Function DeniedAsync(salesReturnId As Integer, correlationId As String, reason As String) As Task(Of IActionResult)

            Dim deniedActorUserId As Integer? = Nothing
            Dim parsedActorUserId As Integer

            If Integer.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), parsedActorUserId) Then
                deniedActorUserId = parsedActorUserId
            End If

            Using connection = Await _connectionFactory.CreateOpenConnectionAsync(HttpContext.RequestAborted)
                Await AuditLogWriter.WriteAsync(
                    connection, deniedActorUserId,
                    "SalesReturnApprovalDenied", salesReturnId.ToString(), "Denied", correlationId,
                    detail:=reason,
                    cancellationToken:=HttpContext.RequestAborted)
            End Using

            Return Forbid(SessionAuthenticationHandler.SchemeName)

        End Function

    End Class

End Namespace
