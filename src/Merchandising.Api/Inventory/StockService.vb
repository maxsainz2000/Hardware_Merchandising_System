' Merchandising.Api.Inventory.StockService
'
' P1-11 / ADR-006: the atomic stock decrement. In one transaction -
'   1. StockRepository.TryDecrementAsync runs the conditional UPDATE and
'      verifies the affected-row count (never read-then-write).
'   2. Insufficient stock -> explicit rollback, controlled outcome, zero
'      rows written anywhere.
'   3. Success -> StockMovements row, then AuditLogs row (same transaction,
'      via AuditLogWriter's transaction parameter added by this task), then
'      commit.
'
' Kept independent of ASP.NET Core's HTTP types, the same shape as
' Security.AuthService - directly testable against the real database.
'
' ADR-004.1 scale validation is checked in TWO places, deliberately, for two
' different reasons: InventoryController checks it first, to produce the
' field-level 400 body a caller needs (this service has no HTTP concept to
' report one with). This service ALSO calls DecimalScaleGuard as its own
' first line, as a hard precondition of the method itself - "a caller cannot
' reach a bound SQL parameter with an over-scale value through this method,
' regardless of which caller" - independent of whichever front door reached
' it. ADR-004.1's own text requires this be provable as an integration test
' against the real database, which StockDecrementTests.OverScaleQuantity_
' RejectedBeforeAnyRowWritten does directly against this method.
'
' Unexpected exceptions (a genuine MySqlException, not the ordinary
' "insufficient stock" outcome) are deliberately NOT caught here. They
' propagate out through the enclosing `Using connection`, whose synchronous
' Dispose() severs the connection - MariaDB rolls back any still-open
' transaction when the connection that opened it drops. This is the same
' pattern Merchandising.Maintenance.Users.CreateUserCommand relies on for
' any error other than its one explicitly-handled case, and it is what lets
' P1-12's fault injection prove the rollback guarantee without this method
' needing to know about it in advance.
'
' P1-12: testOnlyFaultAfterAuditInsert is the fault-injection seam itself.
' VB's preprocessor cannot interrupt a comma-continued parameter list (a
' `#If` there is a compile error, BC30203/BC30013 - confirmed by trying it),
' so the parameter itself is present in every configuration. What makes it
' inert in Release is that its ONLY call site, below, is wrapped in
' `#If DEBUG` - a Release build's compiled DecrementAsync never invokes it,
' no matter what a caller passes. p1-12-rollback.txt proves this
' empirically: the same throwing delegate that rolls the transaction back
' under a Debug build is passed under a Release build and is silently
' ignored - commit succeeds, the delegate never fires. It fires after BOTH
' the StockMovements and AuditLogs inserts, immediately before CommitAsync -
' the superset of the card's "after the movement insert but before commit"
' window, and the strongest point available: proving rollback here proves
' it for every row this method ever writes, not just the first one. Left
' un-set (Nothing) by every caller except the P1-12 test itself.

Imports Merchandising.Contracts.Inventory
Imports Merchandising.Domain
Imports Merchandising.Infrastructure.Data
Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Inventory

    Public NotInheritable Class StockService

        Private ReadOnly _connectionFactory As ConnectionFactory

        Public Sub New(connectionFactory As ConnectionFactory)

            If connectionFactory Is Nothing Then
                Throw New ArgumentNullException(NameOf(connectionFactory))
            End If

            _connectionFactory = connectionFactory

        End Sub

        ''' <summary>
        ''' Decrements <paramref name="productId"/>'s balance by
        ''' <paramref name="quantity"/>, recording the movement and audit
        ''' rows atomically. <paramref name="quantity"/> must already be
        ''' validated (positive, at or below storage scale) by the caller.
        ''' </summary>
        Public Async Function DecrementAsync(
            productId As Integer,
            quantity As Decimal,
            reason As String,
            actorUserId As Integer,
            correlationId As String,
            Optional testOnlyFaultAfterAuditInsert As Action = Nothing,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of StockDecrementOutcome)

            ' ADR-004.1: never trust a caller-supplied quantity is already at
            ' storage scale, even a caller inside this process. Throws
            ' ArgumentException before any connection is opened, so an
            ' over-scale value never reaches a bound SQL parameter.
            DecimalScaleGuard.EnsureQuantityScale(quantity)

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim transaction As MySqlTransaction =
                    Await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(False)

                Dim decrementResult =
                    Await StockRepository.TryDecrementAsync(
                        connection, transaction, productId, quantity, cancellationToken).ConfigureAwait(False)

                If Not decrementResult.Succeeded Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return StockDecrementOutcome.InsufficientStock()
                End If

                Dim movementId As Integer =
                    Await StockMovementWriter.WriteAsync(
                        connection, transaction, productId, -quantity,
                        decrementResult.QuantityBefore, decrementResult.QuantityAfter,
                        reason, actorUserId, correlationId, cancellationToken).ConfigureAwait(False)

                Await AuditLogWriter.WriteAsync(
                    connection, actorUserId, "StockDecremented", $"Product:{productId}", "Success", correlationId,
                    detail:=$"Quantity {decrementResult.QuantityBefore:0.000} -> {decrementResult.QuantityAfter:0.000} (movement {movementId})",
                    cancellationToken:=cancellationToken,
                    transaction:=transaction).ConfigureAwait(False)

#If DEBUG Then
                ' P1-12: both inserts above are already sent to the server,
                ' still uncommitted. Throwing here and letting it propagate
                ' (see the class header) is the rollback proof.
                testOnlyFaultAfterAuditInsert?.Invoke()
#End If

                Await transaction.CommitAsync(cancellationToken).ConfigureAwait(False)
                Await transaction.DisposeAsync().ConfigureAwait(False)

                Return StockDecrementOutcome.Success(New StockDecrementResponse With {
                    .ProductId = productId,
                    .MovementId = movementId,
                    .QuantityBefore = decrementResult.QuantityBefore,
                    .QuantityAfter = decrementResult.QuantityAfter,
                    .CorrelationId = correlationId
                })

            End Using

        End Function

    End Class

End Namespace
