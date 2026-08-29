' Merchandising.Api.Receiving.ReceivingService
'
' P4-05 / ADR-006 / ADR-007 / ADR-020: "the card the phase exists for." One
' transaction commits the receipt, its lines, one StockMovements row per
' line, the conditional balance increase, the PurchaseOrderLines.ReceivedQuantity
' accumulation, the order's status transition and the audit event - or none
' of them (spec section 11's "Goods received" atomic-result row).
'
'   0. Claim the idempotency key FIRST, before any work - same shape as
'      PurchaseOrderService.CreateAsync and StockService.DecrementAsync.
'   1. Lock the order's Status (GetStatusForUpdateAsync).
'   2. Lock EVERY line of the order (GetLinesForUpdateAsync), not just the
'      ones this receipt touches - see that method's header for why: deciding
'      ReceivePartially vs. ReceiveFully requires knowing whether every line
'      will be at its ordered quantity after this receipt, and two receipts
'      racing against the same order must serialize on that decision.
'   3. Validate every request line names a real line of THIS order.
'   4. Project this receipt's quantities onto the locked lines and decide
'      which of the two receiving actions this is (PurchaseOrderTransitions.
'      CanTransition still makes the actual transition decision - this method
'      only computes WHICH action to ask it for, per ADR-020's own reasoning:
'      a single "Receive" action could not stay a function of (state,
'      action)). If CanTransition refuses (the order's STATUS forbids
'      receiving at all - Cancelled/Closed/already-FullyReceived), that
'      refusal wins even over a line that would also have been over-received
'      - a coarser "is this order receivable right now" check takes
'      precedence over the finer per-line one. Only once CanTransition has
'      ALLOWED the action does a line whose projection would EXCEED its
'      OrderedQuantity refuse the whole request as OverReceived (P4-07) -
'      still before any write, so an over-receipt leaves no trace at all,
'      not even a rolled-back row.
'   5. Insert the receipt header, retrying on a duplicate reference number
'      never - a duplicate reference number is the CALLER's problem
'      (ReceiptRepository's header), reported back rather than retried.
'   6. Per line: increment StockBalances (never read-then-write), write the
'      StockMovements row, insert the ReceiptLines row, accumulate
'      PurchaseOrderLines.ReceivedQuantity.
'   7. Write the order's new status.
'   8. AuditLogs row.
'   9. Store the response payload on the claimed idempotency key.
'  10. Commit.
'
' P4-07: THE API'S OWN GUARD IS A CONTROLLED RESPONSE, NOT THE GUARANTEE.
' Step 4's over-receiving check is this card's "turn the constraint
' violation into a controlled response" - it exists so a caller gets a 409
' RECEIPT_QUANTITY_EXCEEDS_ORDERED instead of a 500. It is NOT what actually
' prevents an over-receipt from being stored: 0008's
' CK_PurchaseOrderLines_ReceivedQuantity is, and step 6's
' IncrementReceivedQuantityAsync would still hit it and throw (rolling back
' the whole transaction, same as any other unexpected exception here) if
' this guard were ever removed, wrong, or raced past. ReceivingTests proves
' this directly by calling IncrementReceivedQuantityAsync with an
' over-limit quantity, bypassing this method's guard entirely.
'
' UNEXPECTED EXCEPTIONS ARE DELIBERATELY NOT CAUGHT, AND THERE IS NO Try
' AROUND THE TRANSACTION - the identical arrangement PurchaseOrderService and
' StockService use, and for the same reason (their headers explain it in
' full): a genuine MySqlException propagates out through the enclosing
' `Using connection`, whose synchronous Dispose() severs the connection, and
' MariaDB rolls back whatever was still open. VB cannot Await inside a Catch
' (BC36943, CLAUDE.md section 3), so the "roll back then rethrow" shape does
' not compile here even if it were wanted.
'
' testOnlyFaultAfterAuditInsert is the P1-12/P2-08-shaped fault-injection
' seam, compiled out of Release - see StockService's header for the full
' mechanism. It fires after the audit insert and the idempotency completion
' write, immediately before commit: the strongest point available, proving
' rollback for every row this method writes.

Imports System.Collections.Generic
Imports System.Data
Imports System.Linq
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports Merchandising.Contracts.Receiving
Imports Merchandising.Domain
Imports Merchandising.Domain.Entities
Imports Merchandising.Infrastructure.Data
Imports MySqlConnector

Imports DomainProcurement = Merchandising.Domain.Procurement

Namespace Receiving

    Public NotInheritable Class ReceivingService

        ''' <summary>ADR-007's Scope column value for this command.</summary>
        Public Const IdempotencyScope As String = "Procurement.ReceiveGoods"

        ''' <summary>StockMovements.Reason for every row this command writes.</summary>
        Private Const MovementReason As String = "GoodsReceived"

        ''' <summary>AuditLogs.Action for a successfully committed receipt.</summary>
        Private Const AuditActionReceived As String = "GoodsReceived"

        ''' <summary>AuditLogs.Action for an idempotency replay - nothing was received, so it audits under its own name (PurchaseOrderService's identical reasoning).</summary>
        Private Const AuditActionReplayed As String = "GoodsReceiptReplayed"

        Private ReadOnly _connectionFactory As ConnectionFactory

        Public Sub New(connectionFactory As ConnectionFactory)

            If connectionFactory Is Nothing Then
                Throw New ArgumentNullException(NameOf(connectionFactory))
            End If

            _connectionFactory = connectionFactory

        End Sub

        ''' <summary>
        ''' Receives goods against <paramref name="purchaseOrderId"/>.
        ''' <paramref name="lines"/> must be non-empty with no repeated
        ''' PurchaseOrderLineId, and every value must already be at storage
        ''' scale - the caller's responsibility to refuse a malformed request
        ''' before reaching this method (ADR-004.1), re-asserted here as a
        ''' hard precondition.
        ''' </summary>
        Public Async Function ReceiveAsync(
            purchaseOrderId As Integer,
            referenceNumber As String,
            lines As IReadOnlyList(Of ReceiveGoodsLineRequest),
            actorUserId As Integer,
            correlationId As String,
            idempotencyKey As String,
            Optional testOnlyFaultAfterAuditInsert As Action = Nothing,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of ReceivingOutcome)

            If lines Is Nothing OrElse lines.Count = 0 Then
                Throw New ArgumentException(
                    "A receipt must have at least one line. The caller is responsible for refusing an empty " &
                    "receipt with a field-level validation error before reaching this method.", NameOf(lines))
            End If

            If lines.Select(Function(l) l.PurchaseOrderLineId).Distinct().Count() <> lines.Count Then
                Throw New ArgumentException(
                    "A receipt cannot name the same PurchaseOrderLineId twice. The caller is responsible for " &
                    "refusing that with a field-level validation error before reaching this method.", NameOf(lines))
            End If

            ' ADR-004.1: never trust a caller-supplied money or quantity is
            ' already at storage scale, even a caller inside this process.
            ' Throws before a connection is opened, so an over-scale value can
            ' never reach a bound SQL parameter - and, because this runs
            ' before step 0, never causes an idempotency key to be claimed.
            For Each line As ReceiveGoodsLineRequest In lines
                DecimalScaleGuard.EnsureQuantityScale(line.QuantityReceived)
                DecimalScaleGuard.EnsureMoneyScale(line.Cost)
            Next

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                ' ADR-006 amendment (P4-04/CARRY-03): the level must be passed
                ' here explicitly - a session-level SET does not survive
                ' BeginTransaction.
                Dim transaction As MySqlTransaction =
                    Await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(False)

                ' ----------------------------------------------------- step 0
                Dim claim =
                    Await IdempotencyStore.TryClaimAsync(
                        connection, transaction, IdempotencyScope, idempotencyKey, cancellationToken).ConfigureAwait(False)

                If Not claim.Claimed Then

                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)

                    Dim storedPayload As String =
                        Await IdempotencyStore.FindCompletedResponsePayloadAsync(
                            connection, IdempotencyScope, idempotencyKey, cancellationToken).ConfigureAwait(False)

                    If storedPayload Is Nothing Then
                        Throw New InvalidOperationException(
                            $"IdempotencyKeys row for scope '{IdempotencyScope}', key '{idempotencyKey}' exists but has no " &
                            "completed response. This should be unreachable - see IdempotencyStore's class header.")
                    End If

                    Await AuditLogWriter.WriteAsync(
                        connection, actorUserId, AuditActionReplayed, idempotencyKey, "Success", correlationId,
                        detail:="Idempotency key already committed; the original response was replayed.",
                        cancellationToken:=cancellationToken).ConfigureAwait(False)

                    Return ReceivingOutcome.Replayed(storedPayload)

                End If

                ' ----------------------------------------------------- step 1
                Dim locked =
                    Await PurchaseOrderRepository.GetStatusForUpdateAsync(
                        connection, transaction, purchaseOrderId, cancellationToken).ConfigureAwait(False)

                If Not locked.Found Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return ReceivingOutcome.PurchaseOrderNotFound()
                End If

                ' ----------------------------------------------------- step 2
                Dim orderLines As IReadOnlyList(Of PurchaseOrderLine) =
                    Await PurchaseOrderRepository.GetLinesForUpdateAsync(
                        connection, transaction, purchaseOrderId, cancellationToken).ConfigureAwait(False)

                Dim orderLinesById As Dictionary(Of Integer, PurchaseOrderLine) =
                    orderLines.ToDictionary(Function(l) l.Id)

                ' ----------------------------------------------------- step 3
                For Each line As ReceiveGoodsLineRequest In lines
                    If Not orderLinesById.ContainsKey(line.PurchaseOrderLineId) Then
                        Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                        Await transaction.DisposeAsync().ConfigureAwait(False)
                        Return ReceivingOutcome.LineNotFound(line.PurchaseOrderLineId)
                    End If
                Next

                ' ----------------------------------------------------- step 4
                Dim requestedByLineId As Dictionary(Of Integer, Decimal) =
                    lines.ToDictionary(Function(l) l.PurchaseOrderLineId, Function(l) l.QuantityReceived)

                ' One pass over every locked line computes, for each, its
                ' PROJECTED total and whether that projection would exceed
                ' OrderedQuantity - but does NOT return early on an
                ' over-receipt. That is deliberate ordering (P4-07): an
                ' order already Cancelled/Closed/FullyReceived must still be
                ' refused with ITS OWN status code below, via CanTransition,
                ' even though projecting onto its lines would also compute
                ' an over-receive - a coarser "is this order receivable at
                ' all right now" check takes precedence over the finer
                ' "does this specific request exceed a line's remaining
                ' capacity" check. An over-shot line is simply never counted
                ' as "exactly full" for the Partial-vs-Full decision.
                Dim overReceivedLineId As Integer? = Nothing
                Dim everyLineWillBeFull As Boolean = True

                For Each orderLine As PurchaseOrderLine In orderLines

                    Dim requested As Decimal = 0D
                    requestedByLineId.TryGetValue(orderLine.Id, requested)
                    Dim projected As Decimal = orderLine.ReceivedQuantity + requested

                    If projected > orderLine.OrderedQuantity Then
                        If Not overReceivedLineId.HasValue Then
                            overReceivedLineId = orderLine.Id
                        End If
                        everyLineWillBeFull = False
                    ElseIf projected <> orderLine.OrderedQuantity Then
                        everyLineWillBeFull = False
                    End If

                Next

                Dim action As DomainProcurement.PurchaseOrderAction =
                    If(everyLineWillBeFull, DomainProcurement.PurchaseOrderAction.ReceiveFully,
                                             DomainProcurement.PurchaseOrderAction.ReceivePartially)

                Dim decision As DomainProcurement.PurchaseOrderTransitionResult =
                    DomainProcurement.PurchaseOrderTransitions.CanTransition(locked.Status, action)

                If Not decision.IsAllowed Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return ReceivingOutcome.Refused(decision.ErrorCode)
                End If

                ' P4-07: the order IS receivable right now (CanTransition
                ' just allowed it) - only now does an over-receipt on any
                ' individual line get its own controlled refusal, checked
                ' before any row is written.
                If overReceivedLineId.HasValue Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return ReceivingOutcome.OverReceived(overReceivedLineId.Value)
                End If

                ' ----------------------------------------------------- step 5
                Dim receivedAtUtc As DateTime = DateTime.UtcNow

                Dim receiptResult =
                    Await ReceiptRepository.InsertReceiptAsync(
                        connection, transaction, purchaseOrderId, actorUserId, receivedAtUtc, referenceNumber,
                        cancellationToken).ConfigureAwait(False)

                If receiptResult.Kind = ReceiptWriteOutcomeKind.DuplicateReferenceNumber Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return ReceivingOutcome.DuplicateReferenceNumber()
                End If

                Dim receiptId As Integer = receiptResult.ReceiptId

                ' ----------------------------------------------------- step 6
                Dim lineResponses As New List(Of ReceiptLineResponse)

                For Each line As ReceiveGoodsLineRequest In lines

                    Dim orderLine As PurchaseOrderLine = orderLinesById(line.PurchaseOrderLineId)

                    Dim stockResult =
                        Await StockRepository.IncrementAsync(
                            connection, transaction, orderLine.ProductId, line.QuantityReceived, cancellationToken).ConfigureAwait(False)

                    Dim movementId As Integer =
                        Await StockMovementWriter.WriteAsync(
                            connection, transaction, orderLine.ProductId, line.QuantityReceived,
                            stockResult.QuantityBefore, stockResult.QuantityAfter,
                            MovementReason, actorUserId, correlationId, cancellationToken).ConfigureAwait(False)

                    Dim receiptLineId As Integer =
                        Await ReceiptRepository.InsertReceiptLineAsync(
                            connection, transaction, receiptId, orderLine.Id, orderLine.ProductId,
                            line.QuantityReceived, line.Cost, receivedAtUtc, cancellationToken).ConfigureAwait(False)

                    Dim lineUpdated As Boolean =
                        Await PurchaseOrderRepository.IncrementReceivedQuantityAsync(
                            connection, transaction, orderLine.Id, line.QuantityReceived, cancellationToken).ConfigureAwait(False)

                    If Not lineUpdated Then
                        Throw New InvalidOperationException(
                            $"PurchaseOrderLine {orderLine.Id} was locked by GetLinesForUpdateAsync but " &
                            "IncrementReceivedQuantityAsync affected zero rows. This should be unreachable.")
                    End If

                    Dim product As Product =
                        Await ProductRepository.GetByIdAsync(
                            connection, orderLine.ProductId, cancellationToken, transaction).ConfigureAwait(False)

                    lineResponses.Add(New ReceiptLineResponse With {
                        .Id = receiptLineId,
                        .PurchaseOrderLineId = orderLine.Id,
                        .ProductId = orderLine.ProductId,
                        .ProductSku = product.Sku,
                        .ProductName = product.Name,
                        .QuantityReceived = line.QuantityReceived,
                        .Cost = line.Cost,
                        .MovementId = movementId,
                        .CreatedAtUtc = receivedAtUtc
                    })

                Next

                ' ----------------------------------------------------- step 7
                Dim statusUpdated As Boolean =
                    Await PurchaseOrderRepository.MarkStatusAsync(
                        connection, transaction, purchaseOrderId, decision.To.Value, cancellationToken).ConfigureAwait(False)

                If Not statusUpdated Then
                    Throw New InvalidOperationException(
                        $"PurchaseOrder {purchaseOrderId} was locked by GetStatusForUpdateAsync but MarkStatusAsync " &
                        "affected zero rows. This should be unreachable.")
                End If

                Dim committedOrder As PurchaseOrder =
                    Await PurchaseOrderRepository.GetByIdAsync(
                        connection, purchaseOrderId, cancellationToken, transaction).ConfigureAwait(False)

                Dim response As New ReceiptResponse With {
                    .Id = receiptId,
                    .PurchaseOrderId = purchaseOrderId,
                    .OrderNumber = committedOrder.OrderNumber,
                    .PurchaseOrderStatus = committedOrder.Status.ToString(),
                    .ReceivedByUserId = actorUserId,
                    .ReceivedAtUtc = receivedAtUtc,
                    .ReferenceNumber = referenceNumber,
                    .CreatedAtUtc = receivedAtUtc,
                    .Lines = lineResponses
                }

                ' ----------------------------------------------------- step 8
                Await AuditLogWriter.WriteAsync(
                    connection, actorUserId, AuditActionReceived, referenceNumber, "Success", correlationId,
                    detail:=$"PurchaseOrderId={purchaseOrderId}, ReceiptId={receiptId}, Lines={lines.Count}, " &
                            $"NewStatus={committedOrder.Status}",
                    cancellationToken:=cancellationToken,
                    transaction:=transaction).ConfigureAwait(False)

                ' ----------------------------------------------------- step 9
                Await IdempotencyStore.CompleteAsync(
                    connection, transaction, claim.Id, JsonSerializer.Serialize(response), cancellationToken).ConfigureAwait(False)

#If DEBUG Then
                testOnlyFaultAfterAuditInsert?.Invoke()
#End If

                ' ---------------------------------------------------- step 10
                Await transaction.CommitAsync(cancellationToken).ConfigureAwait(False)
                Await transaction.DisposeAsync().ConfigureAwait(False)

                Return ReceivingOutcome.Created(response)

            End Using

        End Function

    End Class

End Namespace
