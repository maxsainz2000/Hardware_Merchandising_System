' Merchandising.Api.Controllers.AdjustmentsController
'
' P4-10: spec section 10.2's stock adjustments - "An adjustment applies a
' variance to stock. Below the configured threshold it applies directly; at
' or above it requires a second person's approval." Three routes:
'
'   POST /api/v1/adjustments             - request (and, below threshold,
'                                           immediately apply) an adjustment.
'                                           Gated by Adjustments.Request.
'   POST /api/v1/adjustments/{id}/approve - approves AND applies a Pending
'                                           adjustment. Gated by
'                                           Adjustments.Approve PLUS the
'                                           self-approval veto (below).
'   POST /api/v1/adjustments/{id}/reject  - rejects a Pending adjustment, no
'                                           stock effect. Gated by
'                                           Adjustments.Approve alone - spec
'                                           section 9 restricts APPROVING
'                                           one's own adjustment, not
'                                           rejecting it, so no self-review
'                                           veto is added here (CLAUDE.md:
'                                           don't invent a rule the spec
'                                           never states).
'
' APPROVE FOLLOWS PurchaseOrdersController.ApprovePurchaseOrder'S PATTERN
' EXACTLY (ADR-017 section 6, this card's Done-when box 2: "reusing... not a
' new check"). Adjustments.Approve carries a resource-based
' SelfApprovalRequirement (AuthorizationPolicyRegistration), which a
' declarative &lt;Authorize(Policy:=...)&gt; attribute cannot evaluate - it
' runs before this method body, with no adjustment loaded, so
' SelfApprovalHandler (typed to IOwnershipResource) would never see a
' matching resource and would deny EVERY caller, always. So ApproveAdjustment
' carries only &lt;Authorize&gt; (authentication), checks role membership
' itself against the SAME PolicyRegistry.Definitions list
' AuthorizationPolicyRegistration registers (never a second, hand-written
' copy) BEFORE the adjustment is ever loaded - a role-disallowed caller must
' get the identical 403 whether id 1 or id 999999999 is requested, never a
' 404 that would leak which ids exist - then loads the adjustment as a
' courtesy read (RequestedByUserId is immutable after creation, so no lock
' is needed for the value this decision depends on; AdjustmentService.ApproveAsync
' re-locks and re-decides the STATUS transition inside its own transaction
' regardless - that is the check that counts) and calls
' IAuthorizationService.AuthorizeAsync(User, adjustment, Adjustments.Approve)
' itself, evaluating role membership AND the ownership veto together.
' Registered in AuthorizationMatrixTests' AuthenticatedNoPolicyAllowlist for
' exactly this reason - still fully policy-gated, just imperatively.

Imports System.Collections.Generic
Imports System.Linq
Imports System.Security.Claims
Imports System.Threading.Tasks
Imports Merchandising.Api.Inventory
Imports Merchandising.Api.Middleware
Imports Merchandising.Api.Security
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Inventory
Imports Merchandising.Domain
Imports Merchandising.Domain.Entities
Imports Merchandising.Domain.Security
Imports Merchandising.Infrastructure.Data
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Mvc
Imports MySqlConnector

Namespace Controllers

    <ApiController>
    <Route("api/v1/adjustments")>
    Public Class AdjustmentsController
        Inherits ControllerBase

        Private Const MaxReasonLength As Integer = 255

        Private ReadOnly _connectionFactory As ConnectionFactory
        Private ReadOnly _adjustmentService As AdjustmentService
        Private ReadOnly _authorizationService As IAuthorizationService

        Public Sub New(
            connectionFactory As ConnectionFactory,
            adjustmentService As AdjustmentService,
            authorizationService As IAuthorizationService)

            _connectionFactory = connectionFactory
            _adjustmentService = adjustmentService
            _authorizationService = authorizationService

        End Sub

        ''' <summary>
        ''' Requests an adjustment. Below the configured threshold it is
        ''' applied immediately (201, Status=Applied, MovementId set); at or
        ''' above it, it is recorded Pending (201, Status=Pending,
        ''' MovementId Nothing) and awaits ApproveAdjustment/RejectAdjustment.
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.AdjustmentsRequest)>
        <AuditRequired>
        <HttpPost>
        Public Async Function RequestAdjustment(<FromBody> request As RequestAdjustmentRequest) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim fieldErrors As New Dictionary(Of String, String())

            ValidateRequestShape(request, fieldErrors)

            If fieldErrors.Count > 0 Then
                Return ValidationFailed(fieldErrors, correlationId)
            End If

            Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

            Dim outcome As AdjustmentOutcome =
                Await _adjustmentService.RequestAsync(
                    request.ProductId, request.QuantityVariance, request.Reason, actorUserId, correlationId,
                    request.IdempotencyKey, cancellationToken:=HttpContext.RequestAborted)

            Select Case outcome.Kind

                Case AdjustmentOutcomeKind.Created
                    Return Created($"/api/v1/adjustments/{outcome.Response.Id}", outcome.Response)

                Case AdjustmentOutcomeKind.Replayed
                    ' ADR-007: the ORIGINAL committed result, byte for byte.
                    ' 200, not 201 - this request created nothing new.
                    Return Content(outcome.ReplayPayload, "application/json")

                Case AdjustmentOutcomeKind.ProductNotFound
                    Return ValidationFailed(
                        New Dictionary(Of String, String()) From {
                            {"productId", New String() {"No such product exists."}}},
                        correlationId)

                Case Else ' InsufficientStock
                    Return Conflict(New ApiErrorResponse With {
                        .ErrorCode = AdjustmentOutcome.InsufficientStockErrorCode,
                        .Message = "There is not enough stock on hand to apply this decrease - it may have already been sold or adjusted since this request was made.",
                        .CorrelationId = correlationId
                    })

            End Select

        End Function

        ''' <summary>Approves and applies a Pending adjustment. See this class's header for why the self-approval veto is evaluated here, imperatively, rather than declaratively.</summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName)>
        <AuditRequired>
        <HttpPost("{id}/approve")>
        Public Async Function ApproveAdjustment(id As Integer) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()

            Dim allowedRoles As IReadOnlyList(Of String) =
                PolicyRegistry.Definitions.Single(
                    Function(d) d.PolicyName = PolicyRegistry.Names.AdjustmentsApprove).AllowedRoles

            If Not allowedRoles.Any(AddressOf User.IsInRole) Then
                Return Await DeniedAsync(id, correlationId, "Actor's role does not hold Adjustments.Approve.")
            End If

            Dim adjustment As StockAdjustment

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(HttpContext.RequestAborted)
                adjustment = Await AdjustmentRepository.GetByIdAsync(connection, id, HttpContext.RequestAborted)
            End Using

            If adjustment Is Nothing Then
                Return NotFound(New ApiErrorResponse With {
                    .ErrorCode = "ADJUSTMENT_NOT_FOUND",
                    .Message = $"No stock adjustment with Id {id} exists.",
                    .CorrelationId = correlationId
                })
            End If

            Dim authorization As AuthorizationResult =
                Await _authorizationService.AuthorizeAsync(User, adjustment, PolicyRegistry.Names.AdjustmentsApprove)

            If Not authorization.Succeeded Then
                Return Await DeniedAsync(id, correlationId, "Self-approval prohibited (spec section 9).")
            End If

            Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

            Dim outcome As AdjustmentOutcome =
                Await _adjustmentService.ApproveAsync(id, actorUserId, correlationId, cancellationToken:=HttpContext.RequestAborted)

            Return TransitionResult(outcome, id, correlationId)

        End Function

        ''' <summary>Rejects a Pending adjustment. Role-only check - no self-approval veto (this class's header explains why).</summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.AdjustmentsApprove)>
        <AuditRequired>
        <HttpPost("{id}/reject")>
        Public Async Function RejectAdjustment(id As Integer) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()
            Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

            Dim outcome As AdjustmentOutcome =
                Await _adjustmentService.RejectAsync(id, actorUserId, correlationId, cancellationToken:=HttpContext.RequestAborted)

            Return TransitionResult(outcome, id, correlationId)

        End Function

        ' --------------------------------------------------------- validation

        Private Shared Sub ValidateRequestShape(
            request As RequestAdjustmentRequest, fieldErrors As Dictionary(Of String, String()))

            If request Is Nothing Then
                fieldErrors("request") = {"A request body is required."}
                Return
            End If

            If request.ProductId <= 0 Then
                fieldErrors("productId") = {"Product Id is required and must be a positive integer."}
            End If

            If request.QuantityVariance = 0D Then
                fieldErrors("quantityVariance") = {"Quantity variance cannot be zero."}
            ElseIf Not DecimalScaleGuard.IsAtQuantityScale(request.QuantityVariance) Then
                fieldErrors("quantityVariance") = {
                    $"Quantity variance must have no more than {DecimalScaleGuard.QuantityScale} decimal places."}
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

        End Sub

        Private Shared Function IsWellFormedIdempotencyKey(value As String) As Boolean

            Dim parsed As Guid = Guid.Empty
            Return Guid.TryParseExact(value, "D", parsed) AndAlso parsed <> Guid.Empty

        End Function

        ' ------------------------------------------------------------ results

        Private Function ValidationFailed(
            fieldErrors As Dictionary(Of String, String()), correlationId As String) As IActionResult

            Return BadRequest(New ApiErrorResponse With {
                .ErrorCode = "VALIDATION_FAILED",
                .Message = "The adjustment request could not be processed because of a validation failure.",
                .CorrelationId = correlationId,
                .Errors = fieldErrors
            })

        End Function

        Private Function TransitionResult(outcome As AdjustmentOutcome, id As Integer, correlationId As String) As IActionResult

            Select Case outcome.Kind

                Case AdjustmentOutcomeKind.Created
                    Return Ok(outcome.Response)

                Case AdjustmentOutcomeKind.NotFound
                    Return NotFound(New ApiErrorResponse With {
                        .ErrorCode = "ADJUSTMENT_NOT_FOUND",
                        .Message = $"No stock adjustment with Id {id} exists.",
                        .CorrelationId = correlationId
                    })

                Case AdjustmentOutcomeKind.NotPending
                    Return Conflict(New ApiErrorResponse With {
                        .ErrorCode = AdjustmentOutcome.NotPendingErrorCode,
                        .Message = "This adjustment is not pending - it may already have been approved, applied or rejected.",
                        .CorrelationId = correlationId
                    })

                Case Else ' InsufficientStock
                    Return Conflict(New ApiErrorResponse With {
                        .ErrorCode = AdjustmentOutcome.InsufficientStockErrorCode,
                        .Message = "There is not enough stock on hand to apply this decrease - it may have already been sold or adjusted since this adjustment was requested.",
                        .CorrelationId = correlationId
                    })

            End Select

        End Function

        ''' <summary>
        ''' Shared 403 path for ApproveAdjustment's two denial reasons (wrong
        ''' role, self-approval) - the same shape
        ''' PurchaseOrdersController.DeniedAsync uses. Audited even though
        ''' AuditPipelineFilter only forces auditing on a 2xx result - spec
        ''' section 9's control table asks a privileged action for an audit
        ''' record regardless of outcome.
        ''' </summary>
        Private Async Function DeniedAsync(adjustmentId As Integer, correlationId As String, reason As String) As Task(Of IActionResult)

            Dim deniedActorUserId As Integer? = Nothing
            Dim parsedActorUserId As Integer

            If Integer.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), parsedActorUserId) Then
                deniedActorUserId = parsedActorUserId
            End If

            Using connection = Await _connectionFactory.CreateOpenConnectionAsync(HttpContext.RequestAborted)
                Await AuditLogWriter.WriteAsync(
                    connection, deniedActorUserId,
                    "AdjustmentApprovalDenied", adjustmentId.ToString(), "Denied", correlationId,
                    detail:=reason,
                    cancellationToken:=HttpContext.RequestAborted)
            End Using

            Return Forbid(SessionAuthenticationHandler.SchemeName)

        End Function

    End Class

End Namespace
