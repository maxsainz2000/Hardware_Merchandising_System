' Merchandising.Api.Procurement.PurchaseOrderService
'
' P3-03: creating a purchase order, atomically (ADR-006) and idempotently
' (ADR-007). One transaction holds all of it -
'
'   0. Claim the idempotency key FIRST, before any work. A losing claim means
'      a committed attempt already owns this key, so its stored response is
'      replayed verbatim and nothing is written. Same shape as
'      StockService.DecrementAsync; IdempotencyStore's header explains why a
'      losing claim is always guaranteed a completed row to replay.
'   1. Re-check the supplier and every product INSIDE the transaction.
'   2. Insert the header, retrying the order number on ERROR 1062.
'   3. Insert lines 1..N.
'   4. AuditLogs row.
'   5. Store the response payload on the claimed key row.
'   6. Commit.
'
' Everything after step 0 rolls back together, the claim included - so a
' refused create leaves its idempotency key free for a corrected retry to
' reuse, with no separate cleanup. That is the same property P1-14 relies on.
'
' WHY THE SUPPLIER AND PRODUCT CHECKS ARE HERE AND ALSO IN THE CONTROLLER.
' The same two-places argument StockService's header makes for scale
' validation. PurchaseOrdersController checks first because it is the only
' layer that can build the field-level 400 body a caller needs - this service
' has no HTTP concept to report one with. This service checks again as a hard
' precondition of the method itself, so the rule holds for any caller that
' ever reaches it, and because only the check made INSIDE the transaction is
' the one the insert can rely on: a product deactivated between the
' controller's read and this commit is caught here and nowhere else.
'
' UNEXPECTED EXCEPTIONS ARE DELIBERATELY NOT CAUGHT, AND THERE IS NO Try
' AROUND THE TRANSACTION. A genuine MySqlException propagates out through the
' enclosing `Using connection`, whose synchronous Dispose() severs the
' connection, and MariaDB rolls back whatever was still open - the identical
' mechanism StockService relies on and P1-12 proved empirically. Swallowing
' one here is the highest-consequence bug class in this system (CLAUDE.md
' section 10), and an explicit rollback in a Catch would buy nothing this
' does not already guarantee.
'
' It could not be written that way in any case: VB cannot Await inside a
' Catch (BC36943), unlike C#, so the "roll back then rethrow" shape that
' training data reaches for does not compile here. That is a VB gotcha worth
' knowing rather than working around - the connection-severing mechanism is
' the better answer regardless.

Imports System.Collections.Generic
Imports System.Data
Imports System.Linq
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports Merchandising.Contracts.Procurement
Imports Merchandising.Domain
Imports Merchandising.Domain.Entities
Imports Merchandising.Infrastructure.Data
Imports MySqlConnector

' Three different namespaces are called Procurement in this solution -
' Merchandising.Contracts.Procurement (imported above, the wire types),
' Merchandising.Domain.Procurement (the status enum and transition table),
' and Merchandising.Api.Procurement (this file's own). An alias is used for
' the Domain one rather than a bare `Procurement.` qualifier, so which is
' meant is never a question of VB's name-resolution order.
Imports DomainProcurement = Merchandising.Domain.Procurement

Namespace Procurement

    Public NotInheritable Class PurchaseOrderService

        ''' <summary>ADR-007's Scope column value for this command.</summary>
        Public Const IdempotencyScope As String = "Procurement.PurchaseOrderCreate"

        ''' <summary>
        ''' How many order numbers to try before giving up.
        ''' </summary>
        ''' <remarks>
        ''' <para>
        ''' The bound must exceed the number of creates running SIMULTANEOUSLY,
        ''' not the number of retries that feel reasonable.
        ''' </para>
        ''' <para>
        ''' Every concurrent caller reads the same committed MAX and so
        ''' proposes the same candidate. The losers block on
        ''' UQ_PurchaseOrders_OrderNumber until the winner's whole transaction
        ''' commits, then fail 1062 together and all retry with the same next
        ''' candidate. So each round retires exactly ONE caller: with N
        ''' simultaneous creates the unluckiest needs N collisions before its
        ''' success, i.e. N+1 attempts.
        ''' </para>
        ''' <para>
        ''' A bound of 10 was tried first and the ten-way concurrency test
        ''' failed - but raising it did not help, because the real cause was
        ''' the isolation level, not the bound (see CreateAsync's comment at
        ''' BeginTransactionAsync). Both matter: the isolation fix is what
        ''' lets a retry see a NEW candidate at all, and this bound is what
        ''' stops the cascade above from spinning forever.
        ''' </para>
        ''' <para>
        ''' 50 leaves real headroom over any plausible simultaneous load in a
        ''' single-store MVP - the whole system has five roles and a handful of
        ''' procurement officers. It is a bound on a loop that must not spin
        ''' forever, not a tuning parameter, and exhausting it is reported as a
        ''' controlled 409 rather than an exception.
        ''' </para>
        ''' </remarks>
        Private Const MaxOrderNumberAttempts As Integer = 50

        Private ReadOnly _connectionFactory As ConnectionFactory

        Public Sub New(connectionFactory As ConnectionFactory)

            If connectionFactory Is Nothing Then
                Throw New ArgumentNullException(NameOf(connectionFactory))
            End If

            _connectionFactory = connectionFactory

        End Sub

        ''' <summary>
        ''' Creates a Draft purchase order with <paramref name="lines"/>, or
        ''' reports why it could not. <paramref name="lines"/> must be
        ''' non-empty; every value must already be at storage scale, which
        ''' this method re-asserts before opening a connection.
        ''' </summary>
        Public Async Function CreateAsync(
            supplierId As Integer,
            lines As IReadOnlyList(Of CreatePurchaseOrderLineRequest),
            actorUserId As Integer,
            correlationId As String,
            idempotencyKey As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of PurchaseOrderCreationOutcome)

            If lines Is Nothing OrElse lines.Count = 0 Then
                Throw New ArgumentException(
                    "A purchase order must have at least one line. The caller is responsible for refusing an empty " &
                    "order with a field-level validation error before reaching this method.", NameOf(lines))
            End If

            ' ADR-004.1: never trust a caller-supplied money or quantity is
            ' already at storage scale, even a caller inside this process.
            ' Throws before a connection is opened, so an over-scale value can
            ' never reach a bound SQL parameter - and, because this runs
            ' before step 0, never causes an idempotency key to be claimed
            ' either.
            For Each line As CreatePurchaseOrderLineRequest In lines
                DecimalScaleGuard.EnsureQuantityScale(line.OrderedQuantity)
                DecimalScaleGuard.EnsureMoneyScale(line.PurchaseCost)
            Next

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                ' READ COMMITTED IS PASSED EXPLICITLY, AND THAT IS NOT
                ' BELT-AND-BRACES - IT IS THE ONLY THING THAT MAKES IT TRUE
                ' HERE. ConnectionFactory issues `SET SESSION tx_isolation =
                ' 'READ-COMMITTED'` on every connection (P1-05, ADR-006), and
                ' ConnectionFactoryTests asserts it - but MySqlConnector's
                ' BeginTransaction sends its OWN `SET TRANSACTION ISOLATION
                ' LEVEL` for the transaction it opens, which overrides the
                ' session value. Without the argument below, `SELECT
                ' @@tx_isolation` inside this transaction reports
                ' REPEATABLE-READ - measured, not assumed.
                '
                ' What that cost, before it was found: under REPEATABLE READ
                ' this transaction reads from the snapshot taken at its first
                ' read, so PurchaseOrderNumberGenerator's MAX query could not
                ' see order numbers committed after this transaction started.
                ' Every retry recomputed the SAME candidate and collided
                ' again, and ten concurrent creates failed nine of ten
                ' requests. The INSERT saw current data (writes always do)
                ' while the SELECT did not - which is exactly the shape of
                ' bug CLAUDE.md section 6.2 warns about: "the default
                ' isolation level is REPEATABLE-READ ... set it explicitly.
                ' Never assume it."
                Dim transaction As MySqlTransaction =
                    Await connection.BeginTransactionAsync(
                        IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(False)

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

                    ' A replay is a request that was served, so it audits -
                    ' under its OWN action name, never "PurchaseOrderCreated",
                    ' because nothing was created. Two things depend on this
                    ' row existing: [AuditRequired] on the create endpoint
                    ' rejects any 2xx that recorded no audit (P2-04), and a
                    ' reviewer asking "who else fired this key, and when"
                    ' otherwise has nothing to read. It is written outside a
                    ' transaction because there is nothing left to be atomic
                    ' with - the claim was rolled back above and AuditLogs is
                    ' append-only regardless (ADR-013).
                    Await AuditLogWriter.WriteAsync(
                        connection, actorUserId, "PurchaseOrderCreateReplayed", idempotencyKey, "Success", correlationId,
                        detail:="Idempotency key already committed; the original response was replayed.",
                        cancellationToken:=cancellationToken).ConfigureAwait(False)

                    Return PurchaseOrderCreationOutcome.Replayed(storedPayload)

                End If

                    ' ------------------------------------------------- step 1
                    Dim supplier As Supplier =
                        Await SupplierRepository.GetByIdAsync(
                            connection, supplierId, cancellationToken, transaction).ConfigureAwait(False)

                    If supplier Is Nothing Then
                        Return Await RolledBackAsync(
                            transaction, PurchaseOrderCreationOutcome.SupplierNotFound(), cancellationToken).ConfigureAwait(False)
                    End If

                    If Not supplier.IsActive Then
                        Return Await RolledBackAsync(
                            transaction, PurchaseOrderCreationOutcome.SupplierInactive(), cancellationToken).ConfigureAwait(False)
                    End If

                    For Each line As CreatePurchaseOrderLineRequest In lines

                        Dim product As Product =
                            Await ProductRepository.GetByIdAsync(
                                connection, line.ProductId, cancellationToken, transaction).ConfigureAwait(False)

                        If product Is Nothing Then
                            Return Await RolledBackAsync(
                                transaction, PurchaseOrderCreationOutcome.ProductNotFound(line.ProductId), cancellationToken).ConfigureAwait(False)
                        End If

                        If Not product.IsActive Then
                            Return Await RolledBackAsync(
                                transaction, PurchaseOrderCreationOutcome.ProductInactive(line.ProductId), cancellationToken).ConfigureAwait(False)
                        End If

                    Next

                    ' ------------------------------------------------- step 2
                    Dim purchaseOrderId As Integer =
                        Await InsertOrderWithGeneratedNumberAsync(
                            connection, transaction, supplierId, actorUserId, cancellationToken).ConfigureAwait(False)

                    If purchaseOrderId = 0 Then
                        Return Await RolledBackAsync(
                            transaction, PurchaseOrderCreationOutcome.OrderNumberUnavailable(), cancellationToken).ConfigureAwait(False)
                    End If

                    ' ------------------------------------------------- step 3
                    ' Line numbers are assigned 1..N in the order the lines
                    ' arrived - the server's assignment, never the client's
                    ' (CreatePurchaseOrderLineRequest's header).
                    Dim lineNumber As Integer = 0

                    For Each line As CreatePurchaseOrderLineRequest In lines
                        lineNumber += 1
                        Await PurchaseOrderRepository.InsertLineAsync(
                            connection, transaction, purchaseOrderId, lineNumber, line.ProductId,
                            line.OrderedQuantity, line.PurchaseCost, cancellationToken).ConfigureAwait(False)
                    Next

                    ' Read back what was actually written, inside the same
                    ' transaction - so the response reports the server's
                    ' stored values (order number, line numbers, timestamps,
                    ' joined names), not a hopeful reconstruction of them.
                    Dim committed As PurchaseOrder =
                        Await PurchaseOrderRepository.GetByIdAsync(
                            connection, purchaseOrderId, cancellationToken, transaction).ConfigureAwait(False)

                    Dim response As PurchaseOrderResponse = ToResponse(committed)

                    ' ------------------------------------------------- step 4
                    Await AuditLogWriter.WriteAsync(
                        connection, actorUserId, "PurchaseOrderCreated", committed.OrderNumber, "Success", correlationId,
                        detail:=$"Id={committed.Id}, SupplierId={committed.SupplierId}, Lines={lineNumber}",
                        cancellationToken:=cancellationToken,
                        transaction:=transaction).ConfigureAwait(False)

                    ' ------------------------------------------------- step 5
                    Await IdempotencyStore.CompleteAsync(
                        connection, transaction, claim.Id, JsonSerializer.Serialize(response), cancellationToken).ConfigureAwait(False)

                    ' ------------------------------------------------- step 6
                    Await transaction.CommitAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)

                    Return PurchaseOrderCreationOutcome.Created(response)

            End Using

        End Function

        ''' <summary>
        ''' Moves <paramref name="purchaseOrderId"/> to Submitted (spec section
        ''' 10.1). One transaction: lock, ask CanTransition, write, audit,
        ''' commit - the same shape CreateAsync and PriceChangeService use.
        ''' <paramref name="testOnlyFaultAfterAuditInsert"/> is P1-12/P2-08-
        ''' shaped fault injection, compiled out of Release.
        ''' </summary>
        Public Async Function SubmitAsync(
            purchaseOrderId As Integer,
            actorUserId As Integer,
            correlationId As String,
            Optional testOnlyFaultAfterAuditInsert As Action = Nothing,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of PurchaseOrderTransitionOutcome)

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim transaction As MySqlTransaction =
                    Await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(False)

                Dim locked =
                    Await PurchaseOrderRepository.GetStatusForUpdateAsync(
                        connection, transaction, purchaseOrderId, cancellationToken).ConfigureAwait(False)

                If Not locked.Found Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return PurchaseOrderTransitionOutcome.NotFound()
                End If

                Dim decision As DomainProcurement.PurchaseOrderTransitionResult =
                    DomainProcurement.PurchaseOrderTransitions.CanTransition(
                        locked.Status, DomainProcurement.PurchaseOrderAction.Submit)

                If Not decision.IsAllowed Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return PurchaseOrderTransitionOutcome.Refused(decision.ErrorCode)
                End If

                Dim submittedAtUtc As DateTime = DateTime.UtcNow

                Dim updated As Boolean =
                    Await PurchaseOrderRepository.MarkSubmittedAsync(
                        connection, transaction, purchaseOrderId, submittedAtUtc, cancellationToken).ConfigureAwait(False)

                If Not updated Then
                    Throw New InvalidOperationException(
                        $"PurchaseOrder {purchaseOrderId} was locked by GetStatusForUpdateAsync but MarkSubmittedAsync affected zero rows. This should be unreachable.")
                End If

                ' Read back what was actually written, inside the same
                ' transaction (CreateAsync's own arrangement) - OrderNumber is
                ' the audit target, and the same read becomes the response
                ' after commit with no second round trip.
                Dim committed As PurchaseOrder =
                    Await PurchaseOrderRepository.GetByIdAsync(
                        connection, purchaseOrderId, cancellationToken, transaction).ConfigureAwait(False)

                Await AuditLogWriter.WriteAsync(
                    connection, actorUserId, "PurchaseOrderSubmitted", committed.OrderNumber, "Success", correlationId,
                    cancellationToken:=cancellationToken,
                    transaction:=transaction).ConfigureAwait(False)

#If DEBUG Then
                testOnlyFaultAfterAuditInsert?.Invoke()
#End If

                Await transaction.CommitAsync(cancellationToken).ConfigureAwait(False)
                Await transaction.DisposeAsync().ConfigureAwait(False)

                Return PurchaseOrderTransitionOutcome.Success(ToResponse(committed))

            End Using

        End Function

        ''' <summary>
        ''' Moves <paramref name="purchaseOrderId"/> to Approved, recording
        ''' <paramref name="actorUserId"/> as the approver. The self-approval
        ''' veto (ADR-017 section 6) has already been decided by the caller
        ''' through IAuthorizationService before this method is ever reached -
        ''' this method trusts that and enforces only the status machine.
        ''' </summary>
        Public Async Function ApproveAsync(
            purchaseOrderId As Integer,
            actorUserId As Integer,
            correlationId As String,
            Optional testOnlyFaultAfterAuditInsert As Action = Nothing,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of PurchaseOrderTransitionOutcome)

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim transaction As MySqlTransaction =
                    Await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(False)

                Dim locked =
                    Await PurchaseOrderRepository.GetStatusForUpdateAsync(
                        connection, transaction, purchaseOrderId, cancellationToken).ConfigureAwait(False)

                If Not locked.Found Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return PurchaseOrderTransitionOutcome.NotFound()
                End If

                Dim decision As DomainProcurement.PurchaseOrderTransitionResult =
                    DomainProcurement.PurchaseOrderTransitions.CanTransition(
                        locked.Status, DomainProcurement.PurchaseOrderAction.Approve)

                If Not decision.IsAllowed Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return PurchaseOrderTransitionOutcome.Refused(decision.ErrorCode)
                End If

                Dim approvedAtUtc As DateTime = DateTime.UtcNow

                Dim updated As Boolean =
                    Await PurchaseOrderRepository.MarkApprovedAsync(
                        connection, transaction, purchaseOrderId, actorUserId, approvedAtUtc, cancellationToken).ConfigureAwait(False)

                If Not updated Then
                    Throw New InvalidOperationException(
                        $"PurchaseOrder {purchaseOrderId} was locked by GetStatusForUpdateAsync but MarkApprovedAsync affected zero rows. This should be unreachable.")
                End If

                Dim committed As PurchaseOrder =
                    Await PurchaseOrderRepository.GetByIdAsync(
                        connection, purchaseOrderId, cancellationToken, transaction).ConfigureAwait(False)

                Await AuditLogWriter.WriteAsync(
                    connection, actorUserId, "PurchaseOrderApproved", committed.OrderNumber, "Success", correlationId,
                    detail:=$"ApprovedByUserId={actorUserId}",
                    cancellationToken:=cancellationToken,
                    transaction:=transaction).ConfigureAwait(False)

#If DEBUG Then
                testOnlyFaultAfterAuditInsert?.Invoke()
#End If

                Await transaction.CommitAsync(cancellationToken).ConfigureAwait(False)
                Await transaction.DisposeAsync().ConfigureAwait(False)

                Return PurchaseOrderTransitionOutcome.Success(ToResponse(committed))

            End Using

        End Function

        ''' <summary>
        ''' Abandons <paramref name="purchaseOrderId"/> (spec section 10.1: "A
        ''' cancelled order cannot receive goods"). Legal from Draft,
        ''' Submitted or Approved (P3-01's table) - never from
        ''' PartiallyReceived/FullyReceived, where goods already moved and
        ''' Close, not Cancel, is the route for abandoning the remainder.
        ''' </summary>
        Public Async Function CancelAsync(
            purchaseOrderId As Integer,
            actorUserId As Integer,
            reason As String,
            correlationId As String,
            Optional testOnlyFaultAfterAuditInsert As Action = Nothing,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of PurchaseOrderTransitionOutcome)

            Return Await TransitionWithReasonAsync(
                purchaseOrderId, DomainProcurement.PurchaseOrderAction.Cancel, "PurchaseOrderCancelled",
                actorUserId, reason, correlationId, testOnlyFaultAfterAuditInsert, cancellationToken).ConfigureAwait(False)

        End Function

        ''' <summary>
        ''' Finishes <paramref name="purchaseOrderId"/>, accepting whatever
        ''' has been received. Legal only from PartiallyReceived or
        ''' FullyReceived (P3-01's table) - Phase 4 owns the receiving
        ''' endpoints that reach those states; this card adds no rule of its
        ''' own beyond what CanTransition already decides.
        ''' </summary>
        Public Async Function CloseAsync(
            purchaseOrderId As Integer,
            actorUserId As Integer,
            reason As String,
            correlationId As String,
            Optional testOnlyFaultAfterAuditInsert As Action = Nothing,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of PurchaseOrderTransitionOutcome)

            Return Await TransitionWithReasonAsync(
                purchaseOrderId, DomainProcurement.PurchaseOrderAction.Close, "PurchaseOrderClosed",
                actorUserId, reason, correlationId, testOnlyFaultAfterAuditInsert, cancellationToken).ConfigureAwait(False)

        End Function

        ''' <summary>
        ''' Shared shape for CancelAsync/CloseAsync - both are "lock, ask
        ''' CanTransition, write the ONE column (Status) both share, audit
        ''' with the caller's reason, commit" with nothing else distinguishing
        ''' them (contrast SubmitAsync/ApproveAsync, which each own a
        ''' distinct extra column and so stay separate methods). The target
        ''' status comes from CanTransition's own decision, never hardcoded
        ''' here - this method does not know or care which of the two callers
        ''' invoked it beyond the action/audit-name pair they pass in.
        ''' </summary>
        Private Async Function TransitionWithReasonAsync(
            purchaseOrderId As Integer,
            action As DomainProcurement.PurchaseOrderAction,
            auditAction As String,
            actorUserId As Integer,
            reason As String,
            correlationId As String,
            testOnlyFaultAfterAuditInsert As Action,
            cancellationToken As CancellationToken) As Task(Of PurchaseOrderTransitionOutcome)

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim transaction As MySqlTransaction =
                    Await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(False)

                Dim locked =
                    Await PurchaseOrderRepository.GetStatusForUpdateAsync(
                        connection, transaction, purchaseOrderId, cancellationToken).ConfigureAwait(False)

                If Not locked.Found Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return PurchaseOrderTransitionOutcome.NotFound()
                End If

                Dim decision As DomainProcurement.PurchaseOrderTransitionResult =
                    DomainProcurement.PurchaseOrderTransitions.CanTransition(locked.Status, action)

                If Not decision.IsAllowed Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return PurchaseOrderTransitionOutcome.Refused(decision.ErrorCode)
                End If

                Dim updated As Boolean =
                    Await PurchaseOrderRepository.MarkStatusAsync(
                        connection, transaction, purchaseOrderId, decision.To.Value, cancellationToken).ConfigureAwait(False)

                If Not updated Then
                    Throw New InvalidOperationException(
                        $"PurchaseOrder {purchaseOrderId} was locked by GetStatusForUpdateAsync but MarkStatusAsync affected zero rows. This should be unreachable.")
                End If

                Dim committed As PurchaseOrder =
                    Await PurchaseOrderRepository.GetByIdAsync(
                        connection, purchaseOrderId, cancellationToken, transaction).ConfigureAwait(False)

                Await AuditLogWriter.WriteAsync(
                    connection, actorUserId, auditAction, committed.OrderNumber, "Success", correlationId,
                    detail:=reason,
                    cancellationToken:=cancellationToken,
                    transaction:=transaction).ConfigureAwait(False)

#If DEBUG Then
                testOnlyFaultAfterAuditInsert?.Invoke()
#End If

                Await transaction.CommitAsync(cancellationToken).ConfigureAwait(False)
                Await transaction.DisposeAsync().ConfigureAwait(False)

                Return PurchaseOrderTransitionOutcome.Success(ToResponse(committed))

            End Using

        End Function

        ''' <summary>
        ''' Reads one order with its lines. Nothing if no such order exists.
        ''' </summary>
        Public Async Function GetAsync(
            id As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of PurchaseOrderResponse)

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim order As PurchaseOrder =
                    Await PurchaseOrderRepository.GetByIdAsync(connection, id, cancellationToken).ConfigureAwait(False)

                If order Is Nothing Then
                    Return Nothing
                End If

                Return ToResponse(order)

            End Using

        End Function

        ''' <summary>
        ''' Paginated, filtered, sorted list of order summaries.
        ''' <paramref name="page"/> and <paramref name="pageSize"/> must
        ''' already have been clamped by the caller, which is also what echoes
        ''' the applied values back to the client.
        ''' </summary>
        Public Async Function SearchAsync(
            supplierId As Integer?,
            status As DomainProcurement.PurchaseOrderStatus?,
            sortField As PurchaseOrderSortField,
            sortDescending As Boolean,
            page As Integer,
            pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Items As IReadOnlyList(Of PurchaseOrderSummaryResponse), TotalCount As Integer))

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim result = Await PurchaseOrderRepository.SearchAsync(
                    connection, supplierId, status, sortField, sortDescending, page, pageSize, cancellationToken).ConfigureAwait(False)

                Return (Items:=CType(result.Items.Select(AddressOf ToSummaryResponse).ToList(), IReadOnlyList(Of PurchaseOrderSummaryResponse)),
                        TotalCount:=result.TotalCount)

            End Using

        End Function

        ' --------------------------------------------------------------- helpers

        ''' <summary>
        ''' Inserts the header, retrying on a duplicate order number. Returns
        ''' 0 when every attempt collided. See
        ''' PurchaseOrderNumberGenerator's header for why the collision is the
        ''' designed path rather than an anomaly: the generator's read is a
        ''' guess, and UQ_PurchaseOrders_OrderNumber is the guarantee.
        ''' </summary>
        Private Shared Async Function InsertOrderWithGeneratedNumberAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            supplierId As Integer,
            requestedByUserId As Integer,
            cancellationToken As CancellationToken) As Task(Of Integer)

            For attempt As Integer = 1 To MaxOrderNumberAttempts

                Dim candidate As String =
                    Await PurchaseOrderNumberGenerator.NextCandidateAsync(
                        connection, transaction, DateTime.UtcNow, cancellationToken).ConfigureAwait(False)

                Dim insertResult =
                    Await PurchaseOrderRepository.InsertOrderAsync(
                        connection, transaction, candidate, supplierId, requestedByUserId, cancellationToken).ConfigureAwait(False)

                If insertResult.Kind = PurchaseOrderWriteOutcomeKind.Success Then
                    Return insertResult.PurchaseOrderId
                End If

            Next

            Return 0

        End Function

        ''' <summary>Rolls the transaction back and hands the caller's outcome straight back, so each refusal path above stays one statement.</summary>
        Private Shared Async Function RolledBackAsync(
            transaction As MySqlTransaction,
            outcome As PurchaseOrderCreationOutcome,
            cancellationToken As CancellationToken) As Task(Of PurchaseOrderCreationOutcome)

            Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
            Await transaction.DisposeAsync().ConfigureAwait(False)
            Return outcome

        End Function

        ''' <summary>Maps a stored order onto its wire response. Status is the enum NAME, never the ordinal (ADR-020).</summary>
        Friend Shared Function ToResponse(order As PurchaseOrder) As PurchaseOrderResponse

            Return New PurchaseOrderResponse With {
                .Id = order.Id,
                .OrderNumber = order.OrderNumber,
                .SupplierId = order.SupplierId,
                .SupplierName = order.SupplierName,
                .Status = order.Status.ToString(),
                .RequestedByUserId = order.RequestedByUserId,
                .ApprovedByUserId = order.ApprovedByUserId,
                .SubmittedAtUtc = order.SubmittedAtUtc,
                .ApprovedAtUtc = order.ApprovedAtUtc,
                .RowVersion = order.RowVersion,
                .CreatedAtUtc = order.CreatedAtUtc,
                .UpdatedAtUtc = order.UpdatedAtUtc,
                .Lines = order.Lines.Select(AddressOf ToLineResponse).ToList()
            }

        End Function

        Private Shared Function ToLineResponse(line As PurchaseOrderLine) As PurchaseOrderLineResponse

            Return New PurchaseOrderLineResponse With {
                .Id = line.Id,
                .LineNumber = line.LineNumber,
                .ProductId = line.ProductId,
                .ProductSku = line.ProductSku,
                .ProductName = line.ProductName,
                .OrderedQuantity = line.OrderedQuantity,
                .PurchaseCost = line.PurchaseCost,
                .ReceivedQuantity = line.ReceivedQuantity,
                .RowVersion = line.RowVersion,
                .CreatedAtUtc = line.CreatedAtUtc,
                .UpdatedAtUtc = line.UpdatedAtUtc
            }

        End Function

        Private Shared Function ToSummaryResponse(order As PurchaseOrder) As PurchaseOrderSummaryResponse

            Return New PurchaseOrderSummaryResponse With {
                .Id = order.Id,
                .OrderNumber = order.OrderNumber,
                .SupplierId = order.SupplierId,
                .SupplierName = order.SupplierName,
                .Status = order.Status.ToString(),
                .RequestedByUserId = order.RequestedByUserId,
                .ApprovedByUserId = order.ApprovedByUserId,
                .SubmittedAtUtc = order.SubmittedAtUtc,
                .ApprovedAtUtc = order.ApprovedAtUtc,
                .LineCount = order.LineCount,
                .RowVersion = order.RowVersion,
                .CreatedAtUtc = order.CreatedAtUtc,
                .UpdatedAtUtc = order.UpdatedAtUtc
            }

        End Function

    End Class

End Namespace
