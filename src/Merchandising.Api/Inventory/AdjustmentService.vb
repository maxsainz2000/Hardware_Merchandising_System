' Merchandising.Api.Inventory.AdjustmentService
'
' P4-10 / ADR-006 / ADR-007 / ADR-017 section 6. Spec section 10.2: "an
' adjustment applies a variance to stock... Adjustment approval is required
' when the absolute variance exceeds the configured threshold." Three
' atomic commands, each its own transaction:
'
'   RequestAsync - claims the idempotency key, reads the threshold from
'                  SystemSettings (falling back to the registry default if
'                  never written), and decides via
'                  AdjustmentThresholdPolicy.ExceedsThreshold:
'                    - at or above it: inserts Pending. No stock effect at
'                      all - CLAUDE.md section 5's atomicity rule means the
'                      movement/balance/audit triple writes only at the
'                      Applied transition, never at Pending (card
'                      Done-when box 4).
'                    - below it: inserts Applied directly (no approver -
'                      nobody approved it) and, in the SAME transaction,
'                      applies the stock effect - increment for a positive
'                      variance, a conditional decrement for a negative one
'                      (StockRepository, never read-then-write). If the
'                      decrement fails (insufficient stock), the WHOLE
'                      transaction rolls back, including the insert - as if
'                      the request never happened, the same all-or-nothing
'                      shape PurchaseReturnService's InsufficientStock path
'                      uses.
'   ApproveAsync - the row must be locked and Pending
'                  (AdjustmentRepository.GetForUpdateAsync). Applies the
'                  stock effect exactly like RequestAsync's below-threshold
'                  branch, then marks the row Applied with the approver
'                  recorded - one atomic transaction. The self-approval
'                  veto (ADR-017 section 6) is enforced by the CONTROLLER,
'                  via IAuthorizationService.AuthorizeAsync against this
'                  entity's IOwnershipResource - never here (the P3-04
'                  precedent this card's Done-when box 2 names as binding).
'   RejectAsync  - the row must be locked and Pending. Marks it Rejected.
'                  No stock effect at all.
'
' THE SCHEMA'S "Approved" STATUS NAME IS NEVER ACTUALLY PERSISTED AS A
' RESTING STATE. StockAdjustmentStatus models Pending/Approved/Rejected/
' Applied because P4-03 built the whole lifecycle ahead of the endpoint that
' drives it (the same PurchaseOrderStatus precedent), but this card - like
' P4-08 did for purchase returns - collapses "approve" and "apply" into one
' atomic command: ApproveAsync writes Status = Applied directly, with
' ApprovedByUserId set in that same UPDATE (StockAdjustments carries no
' ApprovedAtUtc column - UpdatedAtUtc already records when this happened),
' never a separate Approved-then-Applied pair of commits. Nothing outside
' this class could
' ever observe a row sitting at Approved - only Pending, Applied or
' Rejected are ever committed.
'
' UNEXPECTED EXCEPTIONS ARE DELIBERATELY NOT CAUGHT, AND THERE IS NO Try
' AROUND ANY TRANSACTION - the identical arrangement every other service in
' this solution uses (ReceivingService's header explains the mechanism in
' full).

Imports System.Data
Imports System.Globalization
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports Merchandising.Contracts.Inventory
Imports Merchandising.Domain
Imports Merchandising.Domain.Configuration
Imports Merchandising.Domain.Entities
Imports Merchandising.Domain.Inventory
Imports Merchandising.Infrastructure.Data
Imports MySqlConnector

Namespace Inventory

    Public NotInheritable Class AdjustmentService

        ''' <summary>ADR-007's Scope column value for requesting an adjustment. Approve/Reject carry no idempotency key - a status transition is safe to retry without one (PurchaseOrdersController.ApprovePurchaseOrder's identical reasoning).</summary>
        Public Const RequestIdempotencyScope As String = "Inventory.AdjustmentRequest"

        ''' <summary>StockMovements.Reason for every row this command writes.</summary>
        Private Const MovementReason As String = "StockAdjustment"

        Private Const AuditActionRequested As String = "AdjustmentRequested"
        Private Const AuditActionApplied As String = "AdjustmentApplied"
        Private Const AuditActionRejected As String = "AdjustmentRejected"
        Private Const AuditActionReplayed As String = "AdjustmentRequestReplayed"

        Private ReadOnly _connectionFactory As ConnectionFactory

        Public Sub New(connectionFactory As ConnectionFactory)

            If connectionFactory Is Nothing Then
                Throw New ArgumentNullException(NameOf(connectionFactory))
            End If

            _connectionFactory = connectionFactory

        End Sub

        ''' <summary>
        ''' Requests an adjustment against <paramref name="productId"/>.
        ''' <paramref name="quantityVariance"/> must be non-zero and already
        ''' at storage scale - the caller's responsibility to refuse a
        ''' malformed request before reaching this method (ADR-004.1),
        ''' re-asserted here as a hard precondition.
        ''' </summary>
        Public Async Function RequestAsync(
            productId As Integer,
            quantityVariance As Decimal,
            reason As String,
            actorUserId As Integer,
            correlationId As String,
            idempotencyKey As String,
            Optional testOnlyFaultAfterAuditInsert As Action = Nothing,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of AdjustmentOutcome)

            If quantityVariance = 0D Then
                Throw New ArgumentException(
                    "An adjustment's variance cannot be zero. The caller is responsible for refusing that with a " &
                    "field-level validation error before reaching this method.", NameOf(quantityVariance))
            End If

            DecimalScaleGuard.EnsureQuantityScale(quantityVariance)

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim transaction As MySqlTransaction =
                    Await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(False)

                Dim claim =
                    Await IdempotencyStore.TryClaimAsync(
                        connection, transaction, RequestIdempotencyScope, idempotencyKey, cancellationToken).ConfigureAwait(False)

                If Not claim.Claimed Then

                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)

                    Dim storedPayload As String =
                        Await IdempotencyStore.FindCompletedResponsePayloadAsync(
                            connection, RequestIdempotencyScope, idempotencyKey, cancellationToken).ConfigureAwait(False)

                    If storedPayload Is Nothing Then
                        Throw New InvalidOperationException(
                            $"IdempotencyKeys row for scope '{RequestIdempotencyScope}', key '{idempotencyKey}' exists but has no " &
                            "completed response. This should be unreachable - see IdempotencyStore's class header.")
                    End If

                    Await AuditLogWriter.WriteAsync(
                        connection, actorUserId, AuditActionReplayed, idempotencyKey, "Success", correlationId,
                        detail:="Idempotency key already committed; the original response was replayed.",
                        cancellationToken:=cancellationToken).ConfigureAwait(False)

                    Return AdjustmentOutcome.Replayed(storedPayload)

                End If

                Dim product As Product =
                    Await ProductRepository.GetByIdAsync(
                        connection, productId, cancellationToken, transaction).ConfigureAwait(False)

                If product Is Nothing Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return AdjustmentOutcome.ProductNotFound()
                End If

                Dim threshold As Decimal = Await ReadThresholdAsync(connection, transaction, cancellationToken).ConfigureAwait(False)
                Dim exceedsThreshold As Boolean = AdjustmentThresholdPolicy.ExceedsThreshold(quantityVariance, threshold)

                Dim requestedAtUtc As DateTime = DateTime.UtcNow
                Dim initialStatus As StockAdjustmentStatus =
                    If(exceedsThreshold, StockAdjustmentStatus.Pending, StockAdjustmentStatus.Applied)

                Dim adjustmentId As Integer =
                    Await AdjustmentRepository.InsertAsync(
                        connection, transaction, productId, quantityVariance, reason, actorUserId, exceedsThreshold,
                        initialStatus, requestedAtUtc, cancellationToken).ConfigureAwait(False)

                Dim movementId As Integer? = Nothing

                If Not exceedsThreshold Then

                    Dim applyResult =
                        Await ApplyStockEffectAsync(
                            connection, transaction, productId, quantityVariance, actorUserId, correlationId, cancellationToken).ConfigureAwait(False)

                    If Not applyResult.Succeeded Then
                        Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                        Await transaction.DisposeAsync().ConfigureAwait(False)
                        Return AdjustmentOutcome.InsufficientStock()
                    End If

                    movementId = applyResult.MovementId

                End If

                Dim adjustment As StockAdjustment =
                    Await AdjustmentRepository.GetByIdAsync(
                        connection, adjustmentId, cancellationToken, transaction).ConfigureAwait(False)

                Dim response As AdjustmentResponse = ToResponse(adjustment, movementId)

                Await AuditLogWriter.WriteAsync(
                    connection, actorUserId,
                    If(exceedsThreshold, AuditActionRequested, AuditActionApplied),
                    adjustmentId.ToString(), "Success", correlationId,
                    detail:=$"ProductId={productId}, QuantityVariance={quantityVariance}, ExceedsThreshold={exceedsThreshold}",
                    cancellationToken:=cancellationToken, transaction:=transaction).ConfigureAwait(False)

                Await IdempotencyStore.CompleteAsync(
                    connection, transaction, claim.Id, JsonSerializer.Serialize(response), cancellationToken).ConfigureAwait(False)

#If DEBUG Then
                testOnlyFaultAfterAuditInsert?.Invoke()
#End If

                Await transaction.CommitAsync(cancellationToken).ConfigureAwait(False)
                Await transaction.DisposeAsync().ConfigureAwait(False)

                Return AdjustmentOutcome.Created(response)

            End Using

        End Function

        ''' <summary>
        ''' Approves and applies a locked, Pending adjustment - one atomic
        ''' transaction. Self-approval must already have been refused by the
        ''' CALLER (ADR-017 section 6) before this is ever reached; this
        ''' method re-decides only the STATUS transition, the same division
        ''' PurchaseOrderService.ApproveAsync uses.
        ''' </summary>
        Public Async Function ApproveAsync(
            adjustmentId As Integer,
            approverUserId As Integer,
            correlationId As String,
            Optional testOnlyFaultAfterAuditInsert As Action = Nothing,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of AdjustmentOutcome)

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim transaction As MySqlTransaction =
                    Await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(False)

                Dim locked =
                    Await AdjustmentRepository.GetForUpdateAsync(
                        connection, transaction, adjustmentId, cancellationToken).ConfigureAwait(False)

                If Not locked.Found Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return AdjustmentOutcome.NotFound()
                End If

                If locked.Status <> StockAdjustmentStatus.Pending Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return AdjustmentOutcome.NotPending()
                End If

                Dim applyResult =
                    Await ApplyStockEffectAsync(
                        connection, transaction, locked.ProductId, locked.QuantityVariance, approverUserId, correlationId, cancellationToken).ConfigureAwait(False)

                If Not applyResult.Succeeded Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return AdjustmentOutcome.InsufficientStock()
                End If

                Dim approvedAtUtc As DateTime = DateTime.UtcNow

                Dim applied As Boolean =
                    Await AdjustmentRepository.MarkAppliedAsync(
                        connection, transaction, adjustmentId, approverUserId, approvedAtUtc, cancellationToken).ConfigureAwait(False)

                If Not applied Then
                    Throw New InvalidOperationException(
                        $"StockAdjustment {adjustmentId} was locked by GetForUpdateAsync but MarkAppliedAsync affected " &
                        "zero rows. This should be unreachable.")
                End If

                Dim adjustment As StockAdjustment =
                    Await AdjustmentRepository.GetByIdAsync(
                        connection, adjustmentId, cancellationToken, transaction).ConfigureAwait(False)

                Dim response As AdjustmentResponse = ToResponse(adjustment, applyResult.MovementId)

                Await AuditLogWriter.WriteAsync(
                    connection, approverUserId, AuditActionApplied, adjustmentId.ToString(), "Success", correlationId,
                    detail:=$"ProductId={locked.ProductId}, QuantityVariance={locked.QuantityVariance}, ApprovedByUserId={approverUserId}",
                    cancellationToken:=cancellationToken, transaction:=transaction).ConfigureAwait(False)

#If DEBUG Then
                testOnlyFaultAfterAuditInsert?.Invoke()
#End If

                Await transaction.CommitAsync(cancellationToken).ConfigureAwait(False)
                Await transaction.DisposeAsync().ConfigureAwait(False)

                Return AdjustmentOutcome.Created(response)

            End Using

        End Function

        ''' <summary>Rejects a locked, Pending adjustment. No stock effect at all.</summary>
        Public Async Function RejectAsync(
            adjustmentId As Integer,
            reviewerUserId As Integer,
            correlationId As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of AdjustmentOutcome)

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim transaction As MySqlTransaction =
                    Await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(False)

                Dim locked =
                    Await AdjustmentRepository.GetForUpdateAsync(
                        connection, transaction, adjustmentId, cancellationToken).ConfigureAwait(False)

                If Not locked.Found Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return AdjustmentOutcome.NotFound()
                End If

                If locked.Status <> StockAdjustmentStatus.Pending Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return AdjustmentOutcome.NotPending()
                End If

                Dim rejectedAtUtc As DateTime = DateTime.UtcNow

                Dim rejected As Boolean =
                    Await AdjustmentRepository.MarkRejectedAsync(
                        connection, transaction, adjustmentId, reviewerUserId, rejectedAtUtc, cancellationToken).ConfigureAwait(False)

                If Not rejected Then
                    Throw New InvalidOperationException(
                        $"StockAdjustment {adjustmentId} was locked by GetForUpdateAsync but MarkRejectedAsync affected " &
                        "zero rows. This should be unreachable.")
                End If

                Dim adjustment As StockAdjustment =
                    Await AdjustmentRepository.GetByIdAsync(
                        connection, adjustmentId, cancellationToken, transaction).ConfigureAwait(False)

                Dim response As AdjustmentResponse = ToResponse(adjustment, movementId:=Nothing)

                Await AuditLogWriter.WriteAsync(
                    connection, reviewerUserId, AuditActionRejected, adjustmentId.ToString(), "Success", correlationId,
                    detail:=$"ProductId={locked.ProductId}, QuantityVariance={locked.QuantityVariance}",
                    cancellationToken:=cancellationToken, transaction:=transaction).ConfigureAwait(False)

                Await transaction.CommitAsync(cancellationToken).ConfigureAwait(False)
                Await transaction.DisposeAsync().ConfigureAwait(False)

                Return AdjustmentOutcome.Created(response)

            End Using

        End Function

        ' ----------------------------------------------------------- helpers

        ''' <summary>
        ''' Applies a signed variance to <paramref name="productId"/>'s
        ''' balance and writes the matching StockMovements row, inside
        ''' <paramref name="transaction"/>. A positive variance always
        ''' succeeds (StockRepository.IncrementAsync); a negative one is a
        ''' conditional decrement (StockRepository.TryDecrementAsync, never
        ''' read-then-write) that can fail. Shared by RequestAsync's
        ''' below-threshold branch and ApproveAsync - the ONLY two places
        ''' that ever move stock for an adjustment (Done-when box 4: a
        ''' Pending or Rejected adjustment never reaches this method).
        ''' </summary>
        Private Shared Async Function ApplyStockEffectAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            productId As Integer,
            quantityVariance As Decimal,
            actorUserId As Integer,
            correlationId As String,
            cancellationToken As CancellationToken) As Task(Of (Succeeded As Boolean, MovementId As Integer?))

            Dim quantityBefore As Decimal
            Dim quantityAfter As Decimal

            If quantityVariance > 0D Then

                Dim incrementResult =
                    Await StockRepository.IncrementAsync(
                        connection, transaction, productId, quantityVariance, cancellationToken).ConfigureAwait(False)
                quantityBefore = incrementResult.QuantityBefore
                quantityAfter = incrementResult.QuantityAfter

            Else

                Dim decrementResult =
                    Await StockRepository.TryDecrementAsync(
                        connection, transaction, productId, Math.Abs(quantityVariance), cancellationToken).ConfigureAwait(False)

                If Not decrementResult.Succeeded Then
                    Return (Succeeded:=False, MovementId:=CType(Nothing, Integer?))
                End If

                quantityBefore = decrementResult.QuantityBefore
                quantityAfter = decrementResult.QuantityAfter

            End If

            Dim movementId As Integer =
                Await StockMovementWriter.WriteAsync(
                    connection, transaction, productId, quantityVariance, quantityBefore, quantityAfter,
                    MovementReason, actorUserId, correlationId, cancellationToken).ConfigureAwait(False)

            Return (Succeeded:=True, MovementId:=movementId)

        End Function

        ''' <summary>
        ''' Reads SystemSettings' Keys.AdjustmentApprovalThreshold, falling
        ''' back to SystemSettingRegistry's own default when nothing has
        ''' ever been written - the same fallback SystemSettingsController.GetSettings
        ''' applies for display. A plain, non-locking read
        ''' (SystemSettingsRepository.ReadAsync): deciding one request's
        ''' routing does not need to serialize against a concurrent
        ''' administrator writing a NEW threshold value.
        ''' </summary>
        Private Shared Async Function ReadThresholdAsync(
            connection As MySqlConnection, transaction As MySqlTransaction, cancellationToken As CancellationToken) As Task(Of Decimal)

            Dim stored As String =
                Await SystemSettingsRepository.ReadAsync(
                    connection, transaction, SystemSettingRegistry.Keys.AdjustmentApprovalThreshold, cancellationToken).ConfigureAwait(False)

            Dim raw As String =
                If(stored, SystemSettingRegistry.Find(SystemSettingRegistry.Keys.AdjustmentApprovalThreshold).DefaultValue)

            Return Decimal.Parse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)

        End Function

        Private Shared Function ToResponse(adjustment As StockAdjustment, movementId As Integer?) As AdjustmentResponse

            Return New AdjustmentResponse With {
                .Id = adjustment.Id,
                .ProductId = adjustment.ProductId,
                .ProductSku = adjustment.ProductSku,
                .ProductName = adjustment.ProductName,
                .QuantityVariance = adjustment.QuantityVariance,
                .Reason = adjustment.Reason,
                .RequestedByUserId = adjustment.RequestedByUserId,
                .ApprovedByUserId = adjustment.ApprovedByUserId,
                .ExceedsThreshold = adjustment.ExceedsThreshold,
                .Status = adjustment.Status.ToString(),
                .MovementId = movementId,
                .RowVersion = adjustment.RowVersion,
                .CreatedAtUtc = adjustment.CreatedAtUtc,
                .UpdatedAtUtc = adjustment.UpdatedAtUtc
            }

        End Function

    End Class

End Namespace
