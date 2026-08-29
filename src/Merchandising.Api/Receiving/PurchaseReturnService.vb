' Merchandising.Api.Receiving.PurchaseReturnService
'
' P4-08 / ADR-006 / ADR-007. Spec section 10.1: "A return request records
' the receipt line, quantity, reason, user, approval state, and whether
' stock is removed. The API rejects a return quantity that exceeds the
' received quantity less prior returns." One transaction commits the
' return header (already resolved - see below), its lines, one
' StockMovements row per stock-removing line, the conditional balance
' decrease, and the audit event - or none of them.
'
' P4-08 IS A SINGLE ATOMIC COMMAND, NOT A TWO-ACTOR REQUEST/APPROVE
' WORKFLOW. The P4-02 schema models PurchaseReturns.Status as
' Requested -> Approved/Rejected, and its own migration comment says that
' shape was built "even before a self-approval rule is decided for
' returns." That rule is decided HERE: PolicyRegistry has exactly ONE
' policy, PurchaseReturns.Manage - not a split Request/Approve pair the way
' PurchaseOrders/Adjustments have - and ADR-017 section 6 names only
' PurchaseOrders.Approve and Adjustments.Approve as carrying a self-approval
' veto; purchase returns carry none. So RecordAsync writes the return
' ALREADY Approved, in the same transaction, with RequestedByUserId and
' ApprovedByUserId both the calling actor. Requested/Rejected stay reachable
' in the schema's CHECK constraint for a future phase, unused here - the
' same shape Receiving.Prepare's policy sits unused in this phase.
'
'   0. Claim the idempotency key FIRST, before any work - same shape as
'      ReceivingService.ReceiveAsync.
'   1. Confirm the receipt exists (ReceiptRepository.ExistsAsync) -
'      Receipts is immutable, so there is nothing to lock, only to confirm.
'   2. For every line: lock its ReceiptLines row (GetReceiptLineForUpdateAsync),
'      confirm it belongs to THIS receipt, sum prior committed returns
'      against it (GetPriorReturnedQuantityAsync), and refuse OverReturned
'      if prior-plus-this would exceed QuantityReceived. See
'      PurchaseReturnRepository's header for why the row lock (not a CHECK
'      constraint) is what makes this safe under concurrency.
'   3. Insert the return header, already Approved.
'   4. Per stock-removing line: decrement StockBalances (never
'      read-then-write, StockRepository.TryDecrementAsync - the SAME
'      conditional-update method P1-11 built for sales, reused here because
'      "take stock back out" is the identical operation regardless of why),
'      write the StockMovements row, insert the PurchaseReturnLines row. A
'      line with RemovesStock = False skips the stock effect entirely and
'      writes no StockMovements row for itself.
'   5. AuditLogs row.
'   6. Store the response payload on the claimed idempotency key.
'   7. Commit.
'
' UNEXPECTED EXCEPTIONS ARE DELIBERATELY NOT CAUGHT, AND THERE IS NO Try
' AROUND THE TRANSACTION - the identical arrangement every other service in
' this solution uses (ReceivingService's header explains the mechanism in
' full).

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

Namespace Receiving

    Public NotInheritable Class PurchaseReturnService

        ''' <summary>ADR-007's Scope column value for this command.</summary>
        Public Const IdempotencyScope As String = "Procurement.PurchaseReturn"

        ''' <summary>StockMovements.Reason for every stock-removing line this command writes.</summary>
        Private Const MovementReason As String = "PurchaseReturn"

        Private Const AuditActionRecorded As String = "PurchaseReturnRecorded"
        Private Const AuditActionReplayed As String = "PurchaseReturnReplayed"

        Private ReadOnly _connectionFactory As ConnectionFactory

        Public Sub New(connectionFactory As ConnectionFactory)

            If connectionFactory Is Nothing Then
                Throw New ArgumentNullException(NameOf(connectionFactory))
            End If

            _connectionFactory = connectionFactory

        End Sub

        ''' <summary>
        ''' Records a purchase return against <paramref name="receiptId"/>.
        ''' <paramref name="lines"/> must be non-empty with no repeated
        ''' ReceiptLineId, and every quantity must already be at storage
        ''' scale - the caller's responsibility to refuse a malformed
        ''' request before reaching this method (ADR-004.1), re-asserted
        ''' here as a hard precondition.
        ''' </summary>
        Public Async Function RecordAsync(
            receiptId As Integer,
            referenceNumber As String,
            lines As IReadOnlyList(Of RecordPurchaseReturnLineRequest),
            actorUserId As Integer,
            correlationId As String,
            idempotencyKey As String,
            Optional testOnlyFaultAfterAuditInsert As Action = Nothing,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of PurchaseReturnOutcome)

            If lines Is Nothing OrElse lines.Count = 0 Then
                Throw New ArgumentException(
                    "A purchase return must have at least one line. The caller is responsible for refusing an " &
                    "empty return with a field-level validation error before reaching this method.", NameOf(lines))
            End If

            If lines.Select(Function(l) l.ReceiptLineId).Distinct().Count() <> lines.Count Then
                Throw New ArgumentException(
                    "A purchase return cannot name the same ReceiptLineId twice. The caller is responsible for " &
                    "refusing that with a field-level validation error before reaching this method.", NameOf(lines))
            End If

            For Each line As RecordPurchaseReturnLineRequest In lines
                DecimalScaleGuard.EnsureQuantityScale(line.QuantityReturned)
            Next

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

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

                    Return PurchaseReturnOutcome.Replayed(storedPayload)

                End If

                ' ----------------------------------------------------- step 1
                Dim receiptExists As Boolean =
                    Await ReceiptRepository.ExistsAsync(connection, transaction, receiptId, cancellationToken).ConfigureAwait(False)

                If Not receiptExists Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return PurchaseReturnOutcome.ReceiptNotFound()
                End If

                ' ----------------------------------------------------- step 2
                Dim lockedLines As New Dictionary(Of Integer, (ProductId As Integer, Cost As Decimal, QuantityReceived As Decimal))

                For Each line As RecordPurchaseReturnLineRequest In lines

                    Dim locked =
                        Await PurchaseReturnRepository.GetReceiptLineForUpdateAsync(
                            connection, transaction, line.ReceiptLineId, cancellationToken).ConfigureAwait(False)

                    If Not locked.Found OrElse locked.ReceiptId <> receiptId Then
                        Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                        Await transaction.DisposeAsync().ConfigureAwait(False)
                        Return PurchaseReturnOutcome.LineNotFound(line.ReceiptLineId)
                    End If

                    Dim priorReturned As Decimal =
                        Await PurchaseReturnRepository.GetPriorReturnedQuantityAsync(
                            connection, transaction, line.ReceiptLineId, cancellationToken).ConfigureAwait(False)

                    If priorReturned + line.QuantityReturned > locked.QuantityReceived Then
                        Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                        Await transaction.DisposeAsync().ConfigureAwait(False)
                        Return PurchaseReturnOutcome.OverReturned(line.ReceiptLineId)
                    End If

                    lockedLines(line.ReceiptLineId) =
                        (ProductId:=locked.ProductId, Cost:=locked.Cost, QuantityReceived:=locked.QuantityReceived)

                Next

                ' ----------------------------------------------------- step 3
                Dim returnedAtUtc As DateTime = DateTime.UtcNow

                Dim headerResult =
                    Await PurchaseReturnRepository.InsertPurchaseReturnAsync(
                        connection, transaction, receiptId, actorUserId, actorUserId, returnedAtUtc, returnedAtUtc,
                        referenceNumber, cancellationToken).ConfigureAwait(False)

                If headerResult.Kind = PurchaseReturnWriteOutcomeKind.DuplicateReferenceNumber Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return PurchaseReturnOutcome.DuplicateReferenceNumber()
                End If

                Dim purchaseReturnId As Integer = headerResult.PurchaseReturnId

                ' ----------------------------------------------------- step 4
                Dim lineResponses As New List(Of PurchaseReturnLineResponse)

                For Each line As RecordPurchaseReturnLineRequest In lines

                    Dim receiptLine = lockedLines(line.ReceiptLineId)
                    Dim movementId As Integer? = Nothing

                    If line.RemovesStock Then

                        Dim decrementResult =
                            Await StockRepository.TryDecrementAsync(
                                connection, transaction, receiptLine.ProductId, line.QuantityReturned, cancellationToken).ConfigureAwait(False)

                        If Not decrementResult.Succeeded Then
                            Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                            Await transaction.DisposeAsync().ConfigureAwait(False)
                            Return PurchaseReturnOutcome.InsufficientStock(line.ReceiptLineId)
                        End If

                        movementId =
                            Await StockMovementWriter.WriteAsync(
                                connection, transaction, receiptLine.ProductId, -line.QuantityReturned,
                                decrementResult.QuantityBefore, decrementResult.QuantityAfter,
                                MovementReason, actorUserId, correlationId, cancellationToken).ConfigureAwait(False)

                    End If

                    Dim purchaseReturnLineId As Integer =
                        Await PurchaseReturnRepository.InsertPurchaseReturnLineAsync(
                            connection, transaction, purchaseReturnId, line.ReceiptLineId, receiptLine.ProductId,
                            line.QuantityReturned, receiptLine.Cost, line.Reason, line.RemovesStock, returnedAtUtc,
                            cancellationToken).ConfigureAwait(False)

                    Dim product As Product =
                        Await ProductRepository.GetByIdAsync(
                            connection, receiptLine.ProductId, cancellationToken, transaction).ConfigureAwait(False)

                    lineResponses.Add(New PurchaseReturnLineResponse With {
                        .Id = purchaseReturnLineId,
                        .ReceiptLineId = line.ReceiptLineId,
                        .ProductId = receiptLine.ProductId,
                        .ProductSku = product.Sku,
                        .ProductName = product.Name,
                        .QuantityReturned = line.QuantityReturned,
                        .Cost = receiptLine.Cost,
                        .Reason = line.Reason,
                        .RemovesStock = line.RemovesStock,
                        .MovementId = movementId,
                        .CreatedAtUtc = returnedAtUtc
                    })

                Next

                Dim response As New PurchaseReturnResponse With {
                    .Id = purchaseReturnId,
                    .ReceiptId = receiptId,
                    .Status = "Approved",
                    .RequestedByUserId = actorUserId,
                    .ApprovedByUserId = actorUserId,
                    .ReturnedAtUtc = returnedAtUtc,
                    .ApprovedAtUtc = returnedAtUtc,
                    .ReferenceNumber = referenceNumber,
                    .RowVersion = 0,
                    .CreatedAtUtc = returnedAtUtc,
                    .UpdatedAtUtc = returnedAtUtc,
                    .Lines = lineResponses
                }

                ' ----------------------------------------------------- step 5
                Await AuditLogWriter.WriteAsync(
                    connection, actorUserId, AuditActionRecorded, referenceNumber, "Success", correlationId,
                    detail:=$"ReceiptId={receiptId}, PurchaseReturnId={purchaseReturnId}, Lines={lines.Count}",
                    cancellationToken:=cancellationToken,
                    transaction:=transaction).ConfigureAwait(False)

                ' ----------------------------------------------------- step 6
                Await IdempotencyStore.CompleteAsync(
                    connection, transaction, claim.Id, JsonSerializer.Serialize(response), cancellationToken).ConfigureAwait(False)

#If DEBUG Then
                testOnlyFaultAfterAuditInsert?.Invoke()
#End If

                ' ----------------------------------------------------- step 7
                Await transaction.CommitAsync(cancellationToken).ConfigureAwait(False)
                Await transaction.DisposeAsync().ConfigureAwait(False)

                Return PurchaseReturnOutcome.Created(response)

            End Using

        End Function

    End Class

End Namespace
