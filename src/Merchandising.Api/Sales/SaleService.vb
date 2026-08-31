' Merchandising.Api.Sales.SaleService
'
' P5-07 / ADR-006 / ADR-007: "the card the phase exists for." One transaction
' commits the sale header, its lines, the payment record, one StockMovements
' row per line, the conditional balance decrement, and the audit event - or
' none of them (spec section 11's "Sale completed" atomic-result row). The
' session-totals effect spec section 11 also names is satisfied structurally,
' not by a stored counter: Sales.CashierSessionId is written in this same
' transaction, and CashierSessionRepository.GetPaymentTotalsAsync (P5-04)
' already computes totals from committed Sales/SalePayments rows on demand -
' 0011_pos.sql's own header explains why nothing here maintains a running sum.
'
'   0. Claim the idempotency key FIRST, before any work - same shape as
'      ReceivingService.ReceiveAsync and every other claim-first command in
'      this codebase.
'   1. Lock the CALLING USER's own Open CashierSession
'      (CashierSessionRepository.GetOpenForUpdateByUserAsync) - spec section
'      10.3's foundational "a sale requires an open cashier session". No
'      client-supplied session id exists to trust or distrust (CreateSaleRequest's
'      own header).
'   2. Per line: read the product FRESH, inside this transaction
'      (ProductRepository.GetByIdAsync's transaction overload - a PLAIN read,
'      not FOR UPDATE. Deliberately: locking every product a sale touches, in
'      whatever order the request happened to list them, is exactly how two
'      concurrent sales naming the same two products in reverse order would
'      deadlock. Nothing about "current price" needs a lock that survives
'      past this one read - the value is captured into the immutable
'      SaleLine the instant it is read, never re-read later in this method).
'      Not found -> ProductNotFound. Found but Inactive -> ProductInactive.
'      Both refusals happen before ANY row is written.
'   3. Resolve the configured rounding policy
'      (SystemSettings["currency.roundingPolicy"], P2-05/P5-01's first real
'      consumer) and build one Merchandising.Domain.Sales.SaleLine per
'      request line - captures UnitPrice/Cost/LineTotal once, here.
'   4. Payment validity: Cash requires tendered >= total
'      (Merchandising.Domain.Sales.CashTender.ComputeChange) - refused before
'      any row is written; Card/EWallet always pass (recorded, never
'      authorized, G-24).
'   5. Insert the Sales header.
'   6. Per line: StockRepository.TryDecrementAsync's ADR-006 conditional
'      UPDATE (never read-then-write - this is the one check among the five
'      spec section 10.3 names that genuinely cannot be pre-verified without
'      a lock, so it is checked here, at write time, by the write itself),
'      then the StockMovements row, then the SaleLines row.
'   7. Insert the one SalePayments row.
'   8. AuditLogs row.
'   9. Store the response payload on the claimed idempotency key.
'  10. Commit.
'
' UNEXPECTED EXCEPTIONS ARE DELIBERATELY NOT CAUGHT, AND THERE IS NO Try
' AROUND THE TRANSACTION - the identical arrangement ReceivingService uses,
' for the identical reason (that class's header explains the mechanism in
' full): an uncaught MySqlException (or any other exception) propagates out
' through the enclosing `Using connection`, whose synchronous Dispose()
' severs the connection, and MariaDB rolls back whatever was still open.
'
' testOnlyFaultAfterAuditInsert is the P1-12/P2-08/P4-05-shaped
' fault-injection seam, compiled out of Release - see ReceivingService's
' header for the full mechanism. It fires after the audit insert and the
' idempotency completion write, immediately before commit: the strongest
' point available, proving rollback for every row this method writes.
'
' P5-09 / ADR-007.1: STEP 0 now also computes a RequestHash - SHA-256 over
' this request's meaningful fields (every line's ProductId+Quantity, the
' payment method, the tendered amount), BEFORE the key is claimed, and
' passes it to IdempotencyStore.TryClaimAsync. A LOSING claim whose
' ExistingRequestHash disagrees with this freshly-computed hash names a
' real client bug - the same key reused for a genuinely different command -
' and is refused (IdempotencyKeyReused), never replayed. ExistingRequestHash
' being Nothing (a claim made before this feature existed, or by a scope
' that never opts in) is NOT treated as a mismatch: there is no evidence of
' a different body, so the pre-existing "always replay" behavior applies -
' this is what makes the migration additive rather than a breaking change
' for keys already in flight.

Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Data
Imports System.Text
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

    Public NotInheritable Class SaleService

        ''' <summary>ADR-007's Scope column value for this command.</summary>
        Public Const IdempotencyScope As String = "Sales.CompleteSale"

        ''' <summary>StockMovements.Reason for every row this command writes.</summary>
        Private Const MovementReason As String = "SaleCompleted"

        ''' <summary>AuditLogs.Action for a successfully committed sale.</summary>
        Private Const AuditActionCompleted As String = "SaleCompleted"

        ''' <summary>AuditLogs.Action for an idempotency replay - nothing was sold, so it audits under its own name (ReceivingService's identical reasoning).</summary>
        Private Const AuditActionReplayed As String = "SaleReplayed"

        ''' <summary>AuditLogs.Action for a refused same-key-different-body reuse (P5-09) - nothing was sold, and nothing was replayed either.</summary>
        Private Const AuditActionKeyReused As String = "SaleIdempotencyKeyReused"

        Private ReadOnly _connectionFactory As ConnectionFactory

        Public Sub New(connectionFactory As ConnectionFactory)

            If connectionFactory Is Nothing Then
                Throw New ArgumentNullException(NameOf(connectionFactory))
            End If

            _connectionFactory = connectionFactory

        End Sub

        ''' <summary>
        ''' Completes a sale for <paramref name="actorUserId"/>. <paramref name="lines"/>
        ''' must be non-empty, and every quantity must already be at storage
        ''' scale; <paramref name="tenderedAmount"/> must already be at
        ''' storage scale when supplied - the caller's responsibility to
        ''' refuse a malformed request before reaching this method
        ''' (ADR-004.1), re-asserted here as a hard precondition.
        ''' <paramref name="tenderedAmount"/> must be supplied for
        ''' <see cref="PaymentMethod.Cash"/> and Nothing otherwise -
        ''' SalesController's own validation is what turns a caller getting
        ''' this wrong into a controlled 400 rather than reaching here at all.
        ''' </summary>
        Public Async Function CompleteAsync(
            lines As IReadOnlyList(Of CreateSaleLineRequest),
            paymentMethod As PaymentMethod,
            tenderedAmount As Decimal?,
            actorUserId As Integer,
            correlationId As String,
            idempotencyKey As String,
            Optional testOnlyFaultAfterAuditInsert As Action = Nothing,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of SaleOutcome)

            If lines Is Nothing OrElse lines.Count = 0 Then
                Throw New ArgumentException(
                    "A sale must have at least one line. The caller is responsible for refusing an empty " &
                    "sale with a field-level validation error before reaching this method.", NameOf(lines))
            End If

            ' ADR-004.1: never trust a caller-supplied money or quantity is
            ' already at storage scale, even a caller inside this process.
            ' Throws before a connection is opened, so an over-scale value can
            ' never reach a bound SQL parameter - and, because this runs
            ' before step 0, never causes an idempotency key to be claimed.
            For Each line As CreateSaleLineRequest In lines
                DecimalScaleGuard.EnsureQuantityScale(line.Quantity)
            Next
            If tenderedAmount.HasValue Then
                DecimalScaleGuard.EnsureMoneyScale(tenderedAmount.Value)
            End If

            Dim requestHash As String = ComputeRequestHash(lines, paymentMethod, tenderedAmount)

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
                        connection, transaction, IdempotencyScope, idempotencyKey, cancellationToken, requestHash).ConfigureAwait(False)

                If Not claim.Claimed Then

                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)

                    ' P5-09/ADR-007.1: a stored hash that disagrees with this
                    ' request's own hash means a DIFFERENT command reused the
                    ' key - refused, never replayed. Nothing (no stored hash
                    ' at all) is not a mismatch - see this file's own header.
                    If claim.ExistingRequestHash IsNot Nothing AndAlso
                       Not String.Equals(claim.ExistingRequestHash, requestHash, StringComparison.Ordinal) Then

                        Await AuditLogWriter.WriteAsync(
                            connection, actorUserId, AuditActionKeyReused, idempotencyKey, "Refused", correlationId,
                            detail:="Idempotency key already committed under a different request body; refused rather than replayed.",
                            cancellationToken:=cancellationToken).ConfigureAwait(False)

                        Return SaleOutcome.IdempotencyKeyReused()

                    End If

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

                    Return SaleOutcome.Replayed(storedPayload)

                End If

                ' ----------------------------------------------------- step 1
                Dim session =
                    Await CashierSessionRepository.GetOpenForUpdateByUserAsync(
                        connection, transaction, actorUserId, cancellationToken).ConfigureAwait(False)

                If Not session.Found Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return SaleOutcome.NoOpenSession()
                End If

                ' ----------------------------------------------------- step 2
                Dim productsById As New Dictionary(Of Integer, Product)

                For Each line As CreateSaleLineRequest In lines

                    If productsById.ContainsKey(line.ProductId) Then
                        Continue For
                    End If

                    Dim product As Product =
                        Await ProductRepository.GetByIdAsync(
                            connection, line.ProductId, cancellationToken, transaction).ConfigureAwait(False)

                    If product Is Nothing Then
                        Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                        Await transaction.DisposeAsync().ConfigureAwait(False)
                        Return SaleOutcome.ProductNotFound(line.ProductId)
                    End If

                    If Not product.IsActive Then
                        Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                        Await transaction.DisposeAsync().ConfigureAwait(False)
                        Return SaleOutcome.ProductInactive(line.ProductId)
                    End If

                    productsById(line.ProductId) = product

                Next

                ' ----------------------------------------------------- step 3
                Dim storedRoundingPolicy As String =
                    Await SystemSettingsRepository.ReadAsync(
                        connection, transaction, SystemSettingRegistry.Keys.CurrencyRoundingPolicy, cancellationToken).ConfigureAwait(False)

                Dim roundingPolicyName As String =
                    If(storedRoundingPolicy, SystemSettingRegistry.Find(SystemSettingRegistry.Keys.CurrencyRoundingPolicy).DefaultValue)

                Dim roundingPolicy As MidpointRounding = SaleRoundingPolicy.Parse(roundingPolicyName)

                Dim saleLines As New List(Of SaleLine)

                For Each line As CreateSaleLineRequest In lines
                    Dim product As Product = productsById(line.ProductId)
                    saleLines.Add(New SaleLine(line.ProductId, line.Quantity, product.Price, product.Cost, roundingPolicy))
                Next

                Dim total As Decimal = SaleTotals.ComputeSaleTotal(saleLines)

                ' ----------------------------------------------------- step 4
                Dim paymentAmount As Decimal = total
                Dim recordedTendered As Decimal? = Nothing
                Dim recordedChange As Decimal? = Nothing

                If paymentMethod = PaymentMethod.Cash Then

                    Dim tenderResult As CashTenderResult = CashTender.ComputeChange(tenderedAmount.Value, total)

                    If Not tenderResult.IsAccepted Then
                        Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                        Await transaction.DisposeAsync().ConfigureAwait(False)
                        Return SaleOutcome.CashTenderInsufficient(tenderResult.ShortfallAmount)
                    End If

                    recordedTendered = tenderedAmount.Value
                    recordedChange = tenderResult.Change

                End If

                ' ----------------------------------------------------- step 5
                Dim completedAtUtc As DateTime = DateTime.UtcNow

                Dim saleId As Integer =
                    Await SaleRepository.InsertSaleAsync(
                        connection, transaction, session.Id, actorUserId, total, correlationId, completedAtUtc,
                        cancellationToken).ConfigureAwait(False)

                ' ----------------------------------------------------- step 6
                Dim lineResponses As New List(Of SaleLineResponse)

                For Each saleLine As SaleLine In saleLines

                    Dim stockResult =
                        Await StockRepository.TryDecrementAsync(
                            connection, transaction, saleLine.ProductId, saleLine.Quantity, cancellationToken).ConfigureAwait(False)

                    If Not stockResult.Succeeded Then
                        Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                        Await transaction.DisposeAsync().ConfigureAwait(False)
                        Return SaleOutcome.InsufficientStock(saleLine.ProductId)
                    End If

                    Await StockMovementWriter.WriteAsync(
                        connection, transaction, saleLine.ProductId, -saleLine.Quantity,
                        stockResult.QuantityBefore, stockResult.QuantityAfter,
                        MovementReason, actorUserId, correlationId, cancellationToken).ConfigureAwait(False)

                    Dim lineId As Integer =
                        Await SaleRepository.InsertLineAsync(
                            connection, transaction, saleId, saleLine.ProductId, saleLine.Quantity,
                            saleLine.CapturedUnitPrice, saleLine.CapturedUnitCost, saleLine.LineTotal, completedAtUtc,
                            cancellationToken).ConfigureAwait(False)

                    Dim product As Product = productsById(saleLine.ProductId)

                    lineResponses.Add(New SaleLineResponse With {
                        .Id = lineId,
                        .ProductId = saleLine.ProductId,
                        .ProductSku = product.Sku,
                        .ProductName = product.Name,
                        .Quantity = saleLine.Quantity,
                        .UnitPrice = saleLine.CapturedUnitPrice,
                        .Cost = saleLine.CapturedUnitCost,
                        .LineTotal = saleLine.LineTotal
                    })

                Next

                ' ----------------------------------------------------- step 7
                Dim paymentId As Integer =
                    Await SaleRepository.InsertPaymentAsync(
                        connection, transaction, saleId, paymentMethod.ToString(), paymentAmount,
                        recordedTendered, recordedChange, completedAtUtc, cancellationToken).ConfigureAwait(False)

                Dim response As New SaleResponse With {
                    .Id = saleId,
                    .CashierSessionId = session.Id,
                    .CashierUserId = actorUserId,
                    .Total = total,
                    .Status = NameOf(SaleStatus.Completed),
                    .CorrelationId = correlationId,
                    .CreatedAtUtc = completedAtUtc,
                    .Lines = lineResponses,
                    .Payment = New SalePaymentResponse With {
                        .Id = paymentId,
                        .Method = paymentMethod.ToString(),
                        .Amount = paymentAmount,
                        .TenderedAmount = recordedTendered,
                        .ChangeAmount = recordedChange
                    }
                }

                ' ----------------------------------------------------- step 8
                Await AuditLogWriter.WriteAsync(
                    connection, actorUserId, AuditActionCompleted, saleId.ToString(), "Success", correlationId,
                    detail:=$"CashierSessionId={session.Id}, Total={total}, Lines={saleLines.Count}, PaymentMethod={paymentMethod}",
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

                Return SaleOutcome.Created(response)

            End Using

        End Function

        ''' <summary>
        ''' P6-02: GET /api/v1/sales - a plain, filtered, paginated read, no
        ''' transaction. <paramref name="fromUtc"/>/<paramref name="toUtcExclusive"/>
        ''' are already-converted UTC instants (SalesController does the
        ''' store-local-to-UTC conversion, the same layering
        ''' PurchaseOrdersController.GetPurchaseOrderHistory uses).
        ''' <paramref name="page"/>/<paramref name="pageSize"/> must already
        ''' be clamped by the caller.
        ''' </summary>
        Public Async Function SearchAsync(
            fromUtc As DateTime?,
            toUtcExclusive As DateTime?,
            cashierUserId As Integer?,
            sortField As SaleSortField,
            sortDescending As Boolean,
            page As Integer,
            pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Items As IReadOnlyList(Of SaleResponse), TotalCount As Integer))

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim result =
                    Await SaleRepository.SearchAsync(
                        connection, fromUtc, toUtcExclusive, cashierUserId, sortField, sortDescending,
                        page, pageSize, cancellationToken).ConfigureAwait(False)

                Dim saleIds As IReadOnlyList(Of Integer) = result.Items.Select(Function(s) s.Id).ToList()

                Dim linesBySaleId = Await SaleRepository.GetLinesForSalesAsync(connection, saleIds, cancellationToken).ConfigureAwait(False)
                Dim paymentsBySaleId = Await SaleRepository.GetPaymentsForSalesAsync(connection, saleIds, cancellationToken).ConfigureAwait(False)

                Dim items As New List(Of SaleResponse)

                For Each header In result.Items

                    Dim lineResponses As New List(Of SaleLineResponse)
                    For Each line In linesBySaleId(header.Id)
                        lineResponses.Add(New SaleLineResponse With {
                            .Id = line.Id,
                            .ProductId = line.ProductId,
                            .ProductSku = line.ProductSku,
                            .ProductName = line.ProductName,
                            .Quantity = line.Quantity,
                            .UnitPrice = line.UnitPrice,
                            .Cost = line.Cost,
                            .LineTotal = line.LineTotal
                        })
                    Next

                    Dim paymentResponse As SalePaymentResponse = Nothing
                    If paymentsBySaleId.ContainsKey(header.Id) Then
                        Dim payment = paymentsBySaleId(header.Id)
                        paymentResponse = New SalePaymentResponse With {
                            .Id = payment.Id,
                            .Method = payment.Method,
                            .Amount = payment.Amount,
                            .TenderedAmount = payment.TenderedAmount,
                            .ChangeAmount = payment.ChangeAmount
                        }
                    End If

                    items.Add(New SaleResponse With {
                        .Id = header.Id,
                        .CashierSessionId = header.CashierSessionId,
                        .CashierUserId = header.CashierUserId,
                        .Total = header.Total,
                        .Status = header.Status,
                        .CorrelationId = header.CorrelationId,
                        .CreatedAtUtc = header.CreatedAtUtc,
                        .Lines = lineResponses,
                        .Payment = paymentResponse
                    })

                Next

                Return (Items:=CType(items, IReadOnlyList(Of SaleResponse)), TotalCount:=result.TotalCount)

            End Using

        End Function

        ' ----------------------------------------------------------- helpers

        ''' <summary>
        ''' SHA-256 (lowercase hex, 64 characters - IdempotencyKeys.RequestHash's
        ''' own CHAR(64) shape) over this request's meaningful fields: every
        ''' line's ProductId+Quantity, IN THE ORDER SENT, then the payment
        ''' method, then the tendered amount. Order matters deliberately - a
        ''' genuine retry from the same client resends the same request, lines
        ''' in the same order; this is not trying to be order-invariant, only
        ''' to tell "the same command, sent twice" from "a different one."
        ''' Quantities and amounts are formatted at their own storage scale
        ''' (already validated by this point) so two requests that are equal
        ''' in value hash identically regardless of how the caller's Decimal
        ''' happened to be constructed.
        ''' </summary>
        Private Shared Function ComputeRequestHash(
            lines As IReadOnlyList(Of CreateSaleLineRequest), paymentMethod As PaymentMethod, tenderedAmount As Decimal?) As String

            Dim canonical As New StringBuilder()

            For Each line As CreateSaleLineRequest In lines
                canonical.Append(line.ProductId).
                    Append(":").
                    Append(line.Quantity.ToString("0.000", CultureInfo.InvariantCulture)).
                    Append(";")
            Next

            canonical.Append("|method=").Append(paymentMethod.ToString())
            canonical.Append("|tendered=").
                Append(If(tenderedAmount.HasValue, tenderedAmount.Value.ToString("0.0000", CultureInfo.InvariantCulture), "none"))

            Dim digest As Byte() = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))
            Return Convert.ToHexStringLower(digest)

        End Function

    End Class

End Namespace
