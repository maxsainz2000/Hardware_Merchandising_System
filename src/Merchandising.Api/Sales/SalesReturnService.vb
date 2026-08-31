' Merchandising.Api.Sales.SalesReturnService
'
' P5-11 / ADR-006 / ADR-007 / ADR-017 section 6. Spec section 10.3: "A
' completed-sale return must identify the original sale line, cannot exceed
' the quantity sold minus prior returns, and records whether the returned
' item is eligible to re-enter stock. Payment reversal is recorded
' operationally." Three atomic commands, each its own transaction - the
' identical three-command shape AdjustmentService uses for Request/Approve/
' Reject:
'
'   RecordAsync - claims the idempotency key, locks and validates every
'                 line against sold-minus-prior-returns
'                 (SalesReturnRepository - never a client-supplied figure),
'                 computes the total refund value from SaleLines.UnitPrice
'                 (never re-captured, never client-supplied), and decides
'                 via SalesReturnThresholdPolicy.ExceedsThreshold:
'                   - below the configured threshold: inserts Completed,
'                     with RefundMethod/RefundAmount set immediately, and,
'                     in the SAME transaction, applies the stock effect for
'                     every RestocksItem line (a plain increment,
'                     StockRepository.IncrementAsync - it can never fail
'                     the way a decrement can, so there is no
'                     InsufficientStock outcome for this command at all).
'                   - at or above it: inserts PendingApproval. No stock or
'                     refund effect at all - CLAUDE.md section 5's
'                     atomicity rule means the movement/balance/audit
'                     triple, and RefundMethod/RefundAmount, write only at
'                     the Completed transition, never at PendingApproval
'                     (card Done-when box 3; 0012's own migration comment).
'   ApproveExceptionalAsync - the row must be locked and PendingApproval
'                 (SalesReturnRepository.GetForUpdateAsync). Applies the
'                 stock effect for every RestocksItem line exactly like
'                 RecordAsync's below-threshold branch, recomputes the
'                 refund value from the already-committed lines (safe to
'                 recompute - SaleLines.UnitPrice cannot change after the
'                 fact, SaleLines is append-only), then marks the row
'                 Completed with the approver and the REQUESTED refund
'                 method recorded - one atomic transaction. The self-
'                 approval veto (ADR-017 section 6) is enforced by the
'                 CONTROLLER, via IAuthorizationService.AuthorizeAsync
'                 against this entity's IOwnershipResource - never here
'                 (the P3-04/P4-10 precedent this card's Done-when box 6
'                 names as binding).
'   RejectExceptionalAsync - the row must be locked and PendingApproval.
'                 Marks it Rejected. No stock or refund effect, ever.
'
' UNEXPECTED EXCEPTIONS ARE DELIBERATELY NOT CAUGHT, AND THERE IS NO Try
' AROUND ANY TRANSACTION - the identical arrangement every other service in
' this solution uses (ReceivingService's header explains the mechanism in
' full).

Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports System.Linq
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports Merchandising.Contracts.Sales
Imports Merchandising.Domain
Imports Merchandising.Domain.Configuration
Imports Merchandising.Domain.Entities
Imports Merchandising.Domain.Sales
Imports Merchandising.Infrastructure.Data
Imports MySqlConnector

Namespace Sales

    Public NotInheritable Class SalesReturnService

        ''' <summary>ADR-007's Scope column value for RecordAsync. ApproveExceptional/RejectExceptional carry no idempotency key - a status transition is safe to retry without one (AdjustmentService's identical reasoning).</summary>
        Public Const RecordIdempotencyScope As String = "Sales.RecordReturn"

        ''' <summary>StockMovements.Reason for every row this command writes.</summary>
        Private Const MovementReason As String = "SalesReturn"

        Private Const AuditActionRecorded As String = "SalesReturnRecorded"
        Private Const AuditActionEscalated As String = "SalesReturnEscalated"
        Private Const AuditActionReplayed As String = "SalesReturnReplayed"
        Private Const AuditActionApproved As String = "SalesReturnApproved"
        Private Const AuditActionRejected As String = "SalesReturnRejected"

        Private ReadOnly _connectionFactory As ConnectionFactory

        Public Sub New(connectionFactory As ConnectionFactory)

            If connectionFactory Is Nothing Then
                Throw New ArgumentNullException(NameOf(connectionFactory))
            End If

            _connectionFactory = connectionFactory

        End Sub

        ''' <summary>
        ''' Records a return against <paramref name="saleId"/>.
        ''' <paramref name="lines"/> must be non-empty with no repeated
        ''' SaleLineId, and every quantity must already be at storage scale -
        ''' the caller's responsibility to refuse a malformed request before
        ''' reaching this method (ADR-004.1), re-asserted here as a hard
        ''' precondition. <paramref name="refundMethod"/> is used only when
        ''' this return completes immediately - see
        ''' CreateSalesReturnRequest's header.
        ''' </summary>
        Public Async Function RecordAsync(
            saleId As Integer,
            lines As IReadOnlyList(Of CreateSalesReturnLineRequest),
            reason As String,
            refundMethod As String,
            actorUserId As Integer,
            correlationId As String,
            idempotencyKey As String,
            Optional testOnlyFaultAfterAuditInsert As Action = Nothing,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of SalesReturnOutcome)

            If lines Is Nothing OrElse lines.Count = 0 Then
                Throw New ArgumentException(
                    "A sales return must have at least one line. The caller is responsible for refusing an " &
                    "empty return with a field-level validation error before reaching this method.", NameOf(lines))
            End If

            If lines.Select(Function(l) l.SaleLineId).Distinct().Count() <> lines.Count Then
                Throw New ArgumentException(
                    "A sales return cannot name the same SaleLineId twice. The caller is responsible for " &
                    "refusing that with a field-level validation error before reaching this method.", NameOf(lines))
            End If

            For Each line As CreateSalesReturnLineRequest In lines
                DecimalScaleGuard.EnsureQuantityScale(line.QuantityReturned)
            Next

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim transaction As MySqlTransaction =
                    Await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(False)

                ' ----------------------------------------------------- step 0
                Dim claim =
                    Await IdempotencyStore.TryClaimAsync(
                        connection, transaction, RecordIdempotencyScope, idempotencyKey, cancellationToken).ConfigureAwait(False)

                If Not claim.Claimed Then

                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)

                    Dim storedPayload As String =
                        Await IdempotencyStore.FindCompletedResponsePayloadAsync(
                            connection, RecordIdempotencyScope, idempotencyKey, cancellationToken).ConfigureAwait(False)

                    If storedPayload Is Nothing Then
                        Throw New InvalidOperationException(
                            $"IdempotencyKeys row for scope '{RecordIdempotencyScope}', key '{idempotencyKey}' exists but has no " &
                            "completed response. This should be unreachable - see IdempotencyStore's class header.")
                    End If

                    Await AuditLogWriter.WriteAsync(
                        connection, actorUserId, AuditActionReplayed, idempotencyKey, "Success", correlationId,
                        detail:="Idempotency key already committed; the original response was replayed.",
                        cancellationToken:=cancellationToken).ConfigureAwait(False)

                    Return SalesReturnOutcome.Replayed(storedPayload)

                End If

                ' ----------------------------------------------------- step 1
                Dim saleExists As Boolean =
                    Await SaleRepository.ExistsAsync(connection, transaction, saleId, cancellationToken).ConfigureAwait(False)

                If Not saleExists Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return SalesReturnOutcome.SaleNotFound()
                End If

                ' ----------------------------------------------------- step 2
                Dim lockedLines As New Dictionary(Of Integer, (ProductId As Integer, UnitPrice As Decimal, QuantitySold As Decimal))

                For Each line As CreateSalesReturnLineRequest In lines

                    Dim locked =
                        Await SalesReturnRepository.GetSaleLineForUpdateAsync(
                            connection, transaction, line.SaleLineId, cancellationToken).ConfigureAwait(False)

                    If Not locked.Found OrElse locked.SaleId <> saleId Then
                        Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                        Await transaction.DisposeAsync().ConfigureAwait(False)
                        Return SalesReturnOutcome.LineNotFound(line.SaleLineId)
                    End If

                    Dim priorReturned As Decimal =
                        Await SalesReturnRepository.GetPriorReturnedQuantityAsync(
                            connection, transaction, line.SaleLineId, cancellationToken).ConfigureAwait(False)

                    If priorReturned + line.QuantityReturned > locked.Quantity Then
                        Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                        Await transaction.DisposeAsync().ConfigureAwait(False)
                        Return SalesReturnOutcome.OverReturned(line.SaleLineId)
                    End If

                    lockedLines(line.SaleLineId) =
                        (ProductId:=locked.ProductId, UnitPrice:=locked.UnitPrice, QuantitySold:=locked.Quantity)

                Next

                ' ----------------------------------------------------- step 3
                Dim roundingPolicy As MidpointRounding =
                    Await ReadRoundingPolicyAsync(connection, transaction, cancellationToken).ConfigureAwait(False)

                Dim totalRefundAmount As Decimal = 0D

                For Each line As CreateSalesReturnLineRequest In lines
                    Dim locked = lockedLines(line.SaleLineId)
                    totalRefundAmount += Decimal.Round(line.QuantityReturned * locked.UnitPrice, DecimalScaleGuard.MoneyScale, roundingPolicy)
                Next

                Dim threshold As Decimal =
                    Await ReadThresholdAsync(connection, transaction, cancellationToken).ConfigureAwait(False)

                Dim exceedsThreshold As Boolean = SalesReturnThresholdPolicy.ExceedsThreshold(totalRefundAmount, threshold)

                ' ----------------------------------------------------- step 4
                Dim returnedAtUtc As DateTime = DateTime.UtcNow
                Dim initialStatus As SalesReturnStatus =
                    If(exceedsThreshold, SalesReturnStatus.PendingApproval, SalesReturnStatus.Completed)

                Dim headerRefundMethod As String = If(exceedsThreshold, Nothing, refundMethod)
                Dim headerRefundAmount As Decimal? = If(exceedsThreshold, CType(Nothing, Decimal?), totalRefundAmount)

                Dim salesReturnId As Integer =
                    Await SalesReturnRepository.InsertSalesReturnAsync(
                        connection, transaction, saleId, actorUserId, reason, exceedsThreshold, initialStatus,
                        headerRefundMethod, headerRefundAmount, returnedAtUtc, cancellationToken).ConfigureAwait(False)

                ' ----------------------------------------------------- step 5
                Dim lineResponses As New List(Of SalesReturnLineResponse)

                For Each line As CreateSalesReturnLineRequest In lines

                    Dim locked = lockedLines(line.SaleLineId)

                    Dim lineId As Integer =
                        Await SalesReturnRepository.InsertSalesReturnLineAsync(
                            connection, transaction, salesReturnId, line.SaleLineId, locked.ProductId,
                            line.QuantityReturned, line.RestocksItem, returnedAtUtc, cancellationToken).ConfigureAwait(False)

                    If Not exceedsThreshold AndAlso line.RestocksItem Then

                        Dim incrementResult =
                            Await StockRepository.IncrementAsync(
                                connection, transaction, locked.ProductId, line.QuantityReturned, cancellationToken).ConfigureAwait(False)

                        Await StockMovementWriter.WriteAsync(
                            connection, transaction, locked.ProductId, line.QuantityReturned,
                            incrementResult.QuantityBefore, incrementResult.QuantityAfter,
                            MovementReason, actorUserId, correlationId, cancellationToken).ConfigureAwait(False)

                    End If

                    Dim product As Product =
                        Await ProductRepository.GetByIdAsync(
                            connection, locked.ProductId, cancellationToken, transaction).ConfigureAwait(False)

                    lineResponses.Add(New SalesReturnLineResponse With {
                        .Id = lineId,
                        .SaleLineId = line.SaleLineId,
                        .ProductId = locked.ProductId,
                        .ProductSku = product.Sku,
                        .ProductName = product.Name,
                        .QuantityReturned = line.QuantityReturned,
                        .UnitPrice = locked.UnitPrice,
                        .RestocksItem = line.RestocksItem,
                        .CreatedAtUtc = returnedAtUtc
                    })

                Next

                Dim response As New SalesReturnResponse With {
                    .Id = salesReturnId,
                    .SaleId = saleId,
                    .Status = initialStatus.ToString(),
                    .ReturnedByUserId = actorUserId,
                    .ApprovedByUserId = Nothing,
                    .Reason = reason,
                    .ExceedsThreshold = exceedsThreshold,
                    .RefundMethod = headerRefundMethod,
                    .RefundAmount = headerRefundAmount,
                    .ReturnedAtUtc = returnedAtUtc,
                    .ApprovedAtUtc = Nothing,
                    .RowVersion = 0,
                    .CreatedAtUtc = returnedAtUtc,
                    .UpdatedAtUtc = returnedAtUtc,
                    .Lines = lineResponses
                }

                ' ----------------------------------------------------- step 6
                Await AuditLogWriter.WriteAsync(
                    connection, actorUserId, If(exceedsThreshold, AuditActionEscalated, AuditActionRecorded),
                    salesReturnId.ToString(), "Success", correlationId,
                    detail:=$"SaleId={saleId}, Lines={lines.Count}, TotalRefundAmount={totalRefundAmount}, ExceedsThreshold={exceedsThreshold}",
                    cancellationToken:=cancellationToken, transaction:=transaction).ConfigureAwait(False)

                ' ----------------------------------------------------- step 7
                Await IdempotencyStore.CompleteAsync(
                    connection, transaction, claim.Id, JsonSerializer.Serialize(response), cancellationToken).ConfigureAwait(False)

#If DEBUG Then
                testOnlyFaultAfterAuditInsert?.Invoke()
#End If

                ' ----------------------------------------------------- step 8
                Await transaction.CommitAsync(cancellationToken).ConfigureAwait(False)
                Await transaction.DisposeAsync().ConfigureAwait(False)

                Return SalesReturnOutcome.Created(response)

            End Using

        End Function

        ''' <summary>
        ''' Approves and completes a locked, PendingApproval return - one
        ''' atomic transaction. Self-approval must already have been refused
        ''' by the CALLER (ADR-017 section 6) before this is ever reached;
        ''' this method re-decides only the STATUS transition, the same
        ''' division AdjustmentService.ApproveAsync uses.
        ''' </summary>
        Public Async Function ApproveExceptionalAsync(
            id As Integer,
            refundMethod As String,
            approverUserId As Integer,
            correlationId As String,
            Optional testOnlyFaultAfterAuditInsert As Action = Nothing,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of SalesReturnOutcome)

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim transaction As MySqlTransaction =
                    Await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(False)

                Dim locked =
                    Await SalesReturnRepository.GetForUpdateAsync(connection, transaction, id, cancellationToken).ConfigureAwait(False)

                If Not locked.Found Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return SalesReturnOutcome.NotFound()
                End If

                If locked.Status <> SalesReturnStatus.PendingApproval Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return SalesReturnOutcome.NotPending()
                End If

                Dim lines As IReadOnlyList(Of SalesReturnLine) =
                    Await SalesReturnRepository.GetLinesAsync(connection, id, cancellationToken, transaction).ConfigureAwait(False)

                Dim roundingPolicy As MidpointRounding =
                    Await ReadRoundingPolicyAsync(connection, transaction, cancellationToken).ConfigureAwait(False)

                Dim totalRefundAmount As Decimal = 0D

                For Each line As SalesReturnLine In lines

                    totalRefundAmount += Decimal.Round(line.QuantityReturned * line.UnitPrice, DecimalScaleGuard.MoneyScale, roundingPolicy)

                    If line.RestocksItem Then

                        Dim incrementResult =
                            Await StockRepository.IncrementAsync(
                                connection, transaction, line.ProductId, line.QuantityReturned, cancellationToken).ConfigureAwait(False)

                        Await StockMovementWriter.WriteAsync(
                            connection, transaction, line.ProductId, line.QuantityReturned,
                            incrementResult.QuantityBefore, incrementResult.QuantityAfter,
                            MovementReason, approverUserId, correlationId, cancellationToken).ConfigureAwait(False)

                    End If

                Next

                Dim approvedAtUtc As DateTime = DateTime.UtcNow

                Dim marked As Boolean =
                    Await SalesReturnRepository.MarkCompletedAsync(
                        connection, transaction, id, approverUserId, refundMethod, totalRefundAmount, approvedAtUtc,
                        cancellationToken).ConfigureAwait(False)

                If Not marked Then
                    Throw New InvalidOperationException(
                        $"SalesReturn {id} was locked by GetForUpdateAsync but MarkCompletedAsync affected zero rows. " &
                        "This should be unreachable.")
                End If

                Dim salesReturn As SalesReturn =
                    Await SalesReturnRepository.GetByIdAsync(connection, id, cancellationToken, transaction).ConfigureAwait(False)

                Dim response As SalesReturnResponse = ToResponse(salesReturn, lines)

                Await AuditLogWriter.WriteAsync(
                    connection, approverUserId, AuditActionApproved, id.ToString(), "Success", correlationId,
                    detail:=$"SaleId={locked.SaleId}, TotalRefundAmount={totalRefundAmount}, ApprovedByUserId={approverUserId}",
                    cancellationToken:=cancellationToken, transaction:=transaction).ConfigureAwait(False)

#If DEBUG Then
                testOnlyFaultAfterAuditInsert?.Invoke()
#End If

                Await transaction.CommitAsync(cancellationToken).ConfigureAwait(False)
                Await transaction.DisposeAsync().ConfigureAwait(False)

                Return SalesReturnOutcome.Created(response)

            End Using

        End Function

        ''' <summary>Rejects a locked, PendingApproval return. No stock or refund effect, ever.</summary>
        Public Async Function RejectExceptionalAsync(
            id As Integer,
            approverUserId As Integer,
            correlationId As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of SalesReturnOutcome)

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim transaction As MySqlTransaction =
                    Await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(False)

                Dim locked =
                    Await SalesReturnRepository.GetForUpdateAsync(connection, transaction, id, cancellationToken).ConfigureAwait(False)

                If Not locked.Found Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return SalesReturnOutcome.NotFound()
                End If

                If locked.Status <> SalesReturnStatus.PendingApproval Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return SalesReturnOutcome.NotPending()
                End If

                Dim rejectedAtUtc As DateTime = DateTime.UtcNow

                Dim rejected As Boolean =
                    Await SalesReturnRepository.MarkRejectedAsync(
                        connection, transaction, id, approverUserId, rejectedAtUtc, cancellationToken).ConfigureAwait(False)

                If Not rejected Then
                    Throw New InvalidOperationException(
                        $"SalesReturn {id} was locked by GetForUpdateAsync but MarkRejectedAsync affected zero rows. " &
                        "This should be unreachable.")
                End If

                Dim lines As IReadOnlyList(Of SalesReturnLine) =
                    Await SalesReturnRepository.GetLinesAsync(connection, id, cancellationToken, transaction).ConfigureAwait(False)

                Dim salesReturn As SalesReturn =
                    Await SalesReturnRepository.GetByIdAsync(connection, id, cancellationToken, transaction).ConfigureAwait(False)

                Dim response As SalesReturnResponse = ToResponse(salesReturn, lines)

                Await AuditLogWriter.WriteAsync(
                    connection, approverUserId, AuditActionRejected, id.ToString(), "Success", correlationId,
                    detail:=$"SaleId={locked.SaleId}",
                    cancellationToken:=cancellationToken, transaction:=transaction).ConfigureAwait(False)

                Await transaction.CommitAsync(cancellationToken).ConfigureAwait(False)
                Await transaction.DisposeAsync().ConfigureAwait(False)

                Return SalesReturnOutcome.Created(response)

            End Using

        End Function

        ''' <summary>
        ''' P6-03: paginated, plain (non-locking) read of return headers plus
        ''' their lines - GET /api/v1/sales/returns, the detail endpoint the
        ''' returns-and-cancellations and product-performance reports
        ''' reconcile against. No transaction: every read here is a plain
        ''' SELECT, the same connection-per-call shape SaleService.SearchAsync
        ''' already establishes for GET /api/v1/sales.
        ''' </summary>
        Public Async Function SearchAsync(
            fromUtc As DateTime?,
            toUtcExclusive As DateTime?,
            sortDescending As Boolean,
            page As Integer,
            pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Items As IReadOnlyList(Of SalesReturnResponse), TotalCount As Integer))

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim result =
                    Await SalesReturnRepository.SearchAsync(
                        connection, fromUtc, toUtcExclusive, sortDescending, page, pageSize, cancellationToken).ConfigureAwait(False)

                Dim returnIds As IReadOnlyList(Of Integer) = result.Items.Select(Function(r) r.Id).ToList()

                Dim linesByReturnId As ILookup(Of Integer, SalesReturnLine) =
                    Await SalesReturnRepository.GetLinesForReturnsAsync(connection, returnIds, cancellationToken).ConfigureAwait(False)

                Dim items = result.Items.Select(
                    Function(r) ToResponse(r, linesByReturnId(r.Id).ToList())).ToList()

                Return (Items:=CType(items, IReadOnlyList(Of SalesReturnResponse)), TotalCount:=result.TotalCount)

            End Using

        End Function

        ' ----------------------------------------------------------- helpers

        Private Shared Async Function ReadRoundingPolicyAsync(
            connection As MySqlConnection, transaction As MySqlTransaction, cancellationToken As CancellationToken) As Task(Of MidpointRounding)

            Dim storedRoundingPolicy As String =
                Await SystemSettingsRepository.ReadAsync(
                    connection, transaction, SystemSettingRegistry.Keys.CurrencyRoundingPolicy, cancellationToken).ConfigureAwait(False)

            Dim roundingPolicyName As String =
                If(storedRoundingPolicy, SystemSettingRegistry.Find(SystemSettingRegistry.Keys.CurrencyRoundingPolicy).DefaultValue)

            Return SaleRoundingPolicy.Parse(roundingPolicyName)

        End Function

        ''' <summary>
        ''' Reads SystemSettings' Keys.SalesReturnApprovalThreshold, falling
        ''' back to SystemSettingRegistry's own default when nothing has
        ''' ever been written - the same fallback AdjustmentService.ReadThresholdAsync
        ''' uses. A plain, non-locking read: deciding one request's routing
        ''' does not need to serialize against a concurrent administrator
        ''' writing a NEW threshold value.
        ''' </summary>
        Private Shared Async Function ReadThresholdAsync(
            connection As MySqlConnection, transaction As MySqlTransaction, cancellationToken As CancellationToken) As Task(Of Decimal)

            Dim stored As String =
                Await SystemSettingsRepository.ReadAsync(
                    connection, transaction, SystemSettingRegistry.Keys.SalesReturnApprovalThreshold, cancellationToken).ConfigureAwait(False)

            Dim raw As String =
                If(stored, SystemSettingRegistry.Find(SystemSettingRegistry.Keys.SalesReturnApprovalThreshold).DefaultValue)

            Return Decimal.Parse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)

        End Function

        Private Shared Function ToResponse(salesReturn As SalesReturn, lines As IReadOnlyList(Of SalesReturnLine)) As SalesReturnResponse

            Return New SalesReturnResponse With {
                .Id = salesReturn.Id,
                .SaleId = salesReturn.SaleId,
                .Status = salesReturn.Status.ToString(),
                .ReturnedByUserId = salesReturn.ReturnedByUserId,
                .ApprovedByUserId = salesReturn.ApprovedByUserId,
                .Reason = salesReturn.Reason,
                .ExceedsThreshold = salesReturn.ExceedsThreshold,
                .RefundMethod = salesReturn.RefundMethod,
                .RefundAmount = salesReturn.RefundAmount,
                .ReturnedAtUtc = salesReturn.ReturnedAtUtc,
                .ApprovedAtUtc = salesReturn.ApprovedAtUtc,
                .RowVersion = salesReturn.RowVersion,
                .CreatedAtUtc = salesReturn.CreatedAtUtc,
                .UpdatedAtUtc = salesReturn.UpdatedAtUtc,
                .Lines = lines.Select(Function(line) New SalesReturnLineResponse With {
                    .Id = line.Id,
                    .SaleLineId = line.SaleLineId,
                    .ProductId = line.ProductId,
                    .ProductSku = line.ProductSku,
                    .ProductName = line.ProductName,
                    .QuantityReturned = line.QuantityReturned,
                    .UnitPrice = line.UnitPrice,
                    .RestocksItem = line.RestocksItem,
                    .CreatedAtUtc = line.CreatedAtUtc
                }).ToList()
            }

        End Function

    End Class

End Namespace
