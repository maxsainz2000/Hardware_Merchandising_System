' Merchandising.Api.Catalog.PriceChangeService
'
' P2-08 / ADR-006 (frozen pattern, reused not reinvented): a price or cost
' change updates Products, appends PriceHistory, and writes audit, in ONE
' transaction. Shape mirrors Inventory.StockService.DecrementAsync closely -
'   1. ProductRepository.ReadPriceForUpdateAsync locks the row (SELECT ...
'      FOR UPDATE) and reads the current values - never read-then-write
'      across two statements without the lock held between them.
'   2. No such product -> explicit rollback, controlled ProductNotFound.
'   3. Neither Price nor Cost actually differs from the locked current
'      value -> explicit rollback, controlled NoChange. An <AuditRequired>
'      2xx with nothing to audit is not a real option (P2-04's filter would
'      reject it), so "nothing changed" is rejected before any row is
'      touched, not silently accepted as a success with an empty audit
'      trail.
'   4. Success -> ProductRepository.UpdatePriceCostAsync, then one
'      PriceHistoryWriter.WriteAsync call per field that actually changed
'      (never both unconditionally - a request changing only Price must not
'      manufacture a Cost history row for a value that did not move), then
'      AuditLogWriter.WriteAsync (same transaction), then commit.
'
' Kept independent of ASP.NET Core's HTTP types, the same shape as
' StockService/Security.AuthService - directly testable against the real
' database.
'
' ADR-004.1 scale validation is this method's own hard precondition, exactly
' like StockService's - a caller cannot reach a bound SQL parameter with an
' over-scale Price/Cost through this method, regardless of which caller.
' ProductsController checks it first too, for the field-level 400 body.
'
' P1-12-shaped fault injection: testOnlyFaultAfterAuditInsert, #If DEBUG
' gated exactly like StockService's own - see that class's header for why
' the parameter is unconditionally present but its only call site compiles
' out of Release. Unexpected exceptions are not caught here; they propagate
' through the enclosing `Using connection`, whose Dispose() severs the
' connection and lets MariaDB roll back whatever transaction was still open.

Imports Merchandising.Contracts.Products
Imports Merchandising.Domain
Imports Merchandising.Infrastructure.Data
Imports System.Data
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Catalog

    Public NotInheritable Class PriceChangeService

        Private ReadOnly _connectionFactory As ConnectionFactory

        Public Sub New(connectionFactory As ConnectionFactory)

            If connectionFactory Is Nothing Then
                Throw New ArgumentNullException(NameOf(connectionFactory))
            End If

            _connectionFactory = connectionFactory

        End Sub

        ''' <summary>
        ''' Changes <paramref name="productId"/>'s Price and/or Cost.
        ''' Nothing (either parameter) means "leave this field unchanged".
        ''' Both Nothing, or both equal to the product's current stored
        ''' value, produces <see cref="PriceChangeOutcomeKind.NoChange"/>.
        ''' </summary>
        Public Async Function ChangePriceAsync(
            productId As Integer,
            newPrice As Decimal?,
            newCost As Decimal?,
            actorUserId As Integer,
            correlationId As String,
            Optional testOnlyFaultAfterAuditInsert As Action = Nothing,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of PriceChangeOutcome)

            If newPrice.HasValue Then
                DecimalScaleGuard.EnsureMoneyScale(newPrice.Value)
            End If
            If newCost.HasValue Then
                DecimalScaleGuard.EnsureMoneyScale(newCost.Value)
            End If

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                ' P4-04/CARRY-03/ADR-006 amendment: the session-level
                ' READ-COMMITTED setting does not survive BeginTransaction -
                ' it must be passed here explicitly (measured at P3-03).
                Dim transaction As MySqlTransaction =
                    Await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(False)

                Dim current =
                    Await ProductRepository.ReadPriceForUpdateAsync(connection, transaction, productId, cancellationToken).ConfigureAwait(False)

                If Not current.Found Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return PriceChangeOutcome.ProductNotFound()
                End If

                Dim effectivePrice As Decimal = If(newPrice, current.Price)
                Dim effectiveCost As Decimal = If(newCost, current.Cost)
                Dim priceChanged As Boolean = effectivePrice <> current.Price
                Dim costChanged As Boolean = effectiveCost <> current.Cost

                If Not priceChanged AndAlso Not costChanged Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return PriceChangeOutcome.NoChange()
                End If

                Dim updated As Boolean =
                    Await ProductRepository.UpdatePriceCostAsync(
                        connection, transaction, productId, effectivePrice, effectiveCost, cancellationToken).ConfigureAwait(False)

                If Not updated Then
                    Throw New InvalidOperationException(
                        $"Product {productId} was locked by ReadPriceForUpdateAsync but UpdatePriceCostAsync affected zero rows. This should be unreachable.")
                End If

                Dim effectiveAtUtc As DateTime = DateTime.UtcNow

                If priceChanged Then
                    Await PriceHistoryWriter.WriteAsync(
                        connection, transaction, productId, "Price", current.Price, effectivePrice,
                        actorUserId, effectiveAtUtc, correlationId, cancellationToken).ConfigureAwait(False)
                End If

                If costChanged Then
                    Await PriceHistoryWriter.WriteAsync(
                        connection, transaction, productId, "Cost", current.Cost, effectiveCost,
                        actorUserId, effectiveAtUtc, correlationId, cancellationToken).ConfigureAwait(False)
                End If

                Dim changeSummary As String =
                    String.Join("; ", {
                        If(priceChanged, $"Price {current.Price:0.0000} -> {effectivePrice:0.0000}", Nothing),
                        If(costChanged, $"Cost {current.Cost:0.0000} -> {effectiveCost:0.0000}", Nothing)
                    }.Where(Function(s) s IsNot Nothing))

                Await AuditLogWriter.WriteAsync(
                    connection, actorUserId, "ProductPriceChanged", $"Product:{productId}", "Success", correlationId,
                    detail:=changeSummary,
                    cancellationToken:=cancellationToken,
                    transaction:=transaction).ConfigureAwait(False)

#If DEBUG Then
                ' P1-12-shaped proof: every insert/update above is already
                ' sent to the server, still uncommitted.
                testOnlyFaultAfterAuditInsert?.Invoke()
#End If

                Await transaction.CommitAsync(cancellationToken).ConfigureAwait(False)
                Await transaction.DisposeAsync().ConfigureAwait(False)

                Dim updatedProduct = Await ProductRepository.GetByIdAsync(connection, productId, cancellationToken).ConfigureAwait(False)
                Return PriceChangeOutcome.Success(Controllers.ProductsController.ToResponse(updatedProduct))

            End Using

        End Function

    End Class

End Namespace
