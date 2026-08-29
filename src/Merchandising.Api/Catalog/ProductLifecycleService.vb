' Merchandising.Api.Catalog.ProductLifecycleService
'
' P2-09 / spec section 12: "Transactional records are never physically
' deleted. Master data is deactivated where possible." Deactivate/Reactivate
' each flip Products.IsActive and write an audit row in one transaction
' (spec section 11: "Product created/deactivated | Product master and audit
' event are committed together") - the same frozen ADR-006 shape P2-08
' reused for price changes, applied here through ProductRepository.
' SetActiveStateAsync's single conditional UPDATE instead of a locked
' read-then-write, because there is no derived value to compute first (only
' PriceChangeService needed FOR UPDATE, to know the OLD price/cost to
' record).
'
' Deactivating an already-inactive product (or reactivating an already-
' active one) is NoChange, not a silent success - same reasoning as
' PriceChangeService's own NoChange: an <AuditRequired> 2xx with nothing
' audited is not a real option under P2-04's enforcement filter.
'
' Deletion is never attempted here or anywhere else in this codebase for
' Products - db/grants/0002_post-migration-grants.sql gives merch_api no
' DELETE on it, and the FK from StockMovements/PriceHistory/ProductBarcodes
' would refuse it regardless (P2-09's own evidence proves that directly).
' "Deactivated where possible" IS the deletion story for this table.

Imports Merchandising.Domain.Entities
Imports Merchandising.Infrastructure.Data
Imports System.Data
Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Catalog

    Public NotInheritable Class ProductLifecycleService

        Private ReadOnly _connectionFactory As ConnectionFactory

        Public Sub New(connectionFactory As ConnectionFactory)

            If connectionFactory Is Nothing Then
                Throw New ArgumentNullException(NameOf(connectionFactory))
            End If

            _connectionFactory = connectionFactory

        End Sub

        Public Async Function DeactivateAsync(
            productId As Integer, actorUserId As Integer, correlationId As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of ProductLifecycleOutcome)

            Return Await SetActiveStateAsync(productId, False, "ProductDeactivated", actorUserId, correlationId, cancellationToken).ConfigureAwait(False)

        End Function

        Public Async Function ReactivateAsync(
            productId As Integer, actorUserId As Integer, correlationId As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of ProductLifecycleOutcome)

            Return Await SetActiveStateAsync(productId, True, "ProductReactivated", actorUserId, correlationId, cancellationToken).ConfigureAwait(False)

        End Function

        Private Async Function SetActiveStateAsync(
            productId As Integer, targetIsActive As Boolean, auditAction As String, actorUserId As Integer, correlationId As String,
            cancellationToken As CancellationToken) As Task(Of ProductLifecycleOutcome)

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                ' P4-04/CARRY-03/ADR-006 amendment: the session-level
                ' READ-COMMITTED setting does not survive BeginTransaction -
                ' it must be passed here explicitly (measured at P3-03).
                Dim transaction As MySqlTransaction =
                    Await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(False)

                Dim changed As Boolean =
                    Await ProductRepository.SetActiveStateAsync(connection, transaction, productId, targetIsActive, cancellationToken).ConfigureAwait(False)

                If Not changed Then

                    ' Nothing was written, so there is nothing this read
                    ' could race - it exists only to tell "no such product"
                    ' apart from "already in the requested state" for the
                    ' caller's outcome. Rolled back and disposed FIRST:
                    ' MySqlConnector refuses to run a command against this
                    ' connection without explicitly attaching the still-open
                    ' transaction ("not the connection's active transaction"),
                    ' and GetByIdAsync's own SELECT has no reason to run
                    ' inside one - nothing here needs the isolation.
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)

                    Dim existing As Product = Await ProductRepository.GetByIdAsync(connection, productId, cancellationToken).ConfigureAwait(False)

                    If existing Is Nothing Then
                        Return ProductLifecycleOutcome.ProductNotFound()
                    End If
                    Return ProductLifecycleOutcome.NoChange()

                End If

                Await AuditLogWriter.WriteAsync(
                    connection, actorUserId, auditAction, $"Product:{productId}", "Success", correlationId,
                    detail:=Nothing,
                    cancellationToken:=cancellationToken,
                    transaction:=transaction).ConfigureAwait(False)

                Await transaction.CommitAsync(cancellationToken).ConfigureAwait(False)
                Await transaction.DisposeAsync().ConfigureAwait(False)

                Dim updated As Product = Await ProductRepository.GetByIdAsync(connection, productId, cancellationToken).ConfigureAwait(False)
                Return ProductLifecycleOutcome.Success(Controllers.ProductsController.ToResponse(updated))

            End Using

        End Function

    End Class

End Namespace
