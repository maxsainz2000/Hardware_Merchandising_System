' Merchandising.Api.Catalog.SupplierLifecycleService
'
' P2-10 / spec section 12: "Transactional records are never physically
' deleted. Master data is deactivated where possible." Deactivate/Reactivate
' each flip Suppliers.IsActive and write an audit row in one transaction -
' the same frozen ADR-006 shape P2-09's ProductLifecycleService uses, applied
' here through SupplierRepository.
'
' Deactivating an already-inactive supplier (or reactivating an already-
' active one) is NoChange, not a silent success - same reasoning as
' ProductLifecycleService's own NoChange: an <AuditRequired> 2xx with
' nothing audited is not a real option under P2-04's enforcement filter.
'
' Deletion is never attempted here or anywhere else in this codebase for
' Suppliers - db/grants/0009_supplier-grants.sql gives merch_api no DELETE on
' it. "Deactivated where possible" IS the deletion story for this table, the
' same as Products.

Imports Merchandising.Domain.Entities
Imports Merchandising.Infrastructure.Data
Imports System.Data
Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Catalog

    Public NotInheritable Class SupplierLifecycleService

        Private ReadOnly _connectionFactory As ConnectionFactory

        Public Sub New(connectionFactory As ConnectionFactory)

            If connectionFactory Is Nothing Then
                Throw New ArgumentNullException(NameOf(connectionFactory))
            End If

            _connectionFactory = connectionFactory

        End Sub

        Public Async Function DeactivateAsync(
            supplierId As Integer, actorUserId As Integer, correlationId As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of SupplierLifecycleOutcome)

            Return Await SetActiveStateAsync(supplierId, False, "SupplierDeactivated", actorUserId, correlationId, cancellationToken).ConfigureAwait(False)

        End Function

        Public Async Function ReactivateAsync(
            supplierId As Integer, actorUserId As Integer, correlationId As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of SupplierLifecycleOutcome)

            Return Await SetActiveStateAsync(supplierId, True, "SupplierReactivated", actorUserId, correlationId, cancellationToken).ConfigureAwait(False)

        End Function

        Private Async Function SetActiveStateAsync(
            supplierId As Integer, targetIsActive As Boolean, auditAction As String, actorUserId As Integer, correlationId As String,
            cancellationToken As CancellationToken) As Task(Of SupplierLifecycleOutcome)

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                ' P4-04/CARRY-03/ADR-006 amendment: the session-level
                ' READ-COMMITTED setting does not survive BeginTransaction -
                ' it must be passed here explicitly (measured at P3-03).
                Dim transaction As MySqlTransaction =
                    Await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(False)

                Dim changed As Boolean =
                    Await SupplierRepository.SetActiveStateAsync(connection, transaction, supplierId, targetIsActive, cancellationToken).ConfigureAwait(False)

                If Not changed Then

                    ' Nothing was written, so there is nothing this read
                    ' could race - see ProductLifecycleService's own header
                    ' for why rollback+dispose happens before this plain
                    ' read (MySqlConnector refuses a command against a
                    ' connection without its still-open transaction
                    ' explicitly attached).
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)

                    Dim existing As Supplier = Await SupplierRepository.GetByIdAsync(connection, supplierId, cancellationToken).ConfigureAwait(False)

                    If existing Is Nothing Then
                        Return SupplierLifecycleOutcome.SupplierNotFound()
                    End If
                    Return SupplierLifecycleOutcome.NoChange()

                End If

                Await AuditLogWriter.WriteAsync(
                    connection, actorUserId, auditAction, $"Supplier:{supplierId}", "Success", correlationId,
                    detail:=Nothing,
                    cancellationToken:=cancellationToken,
                    transaction:=transaction).ConfigureAwait(False)

                Await transaction.CommitAsync(cancellationToken).ConfigureAwait(False)
                Await transaction.DisposeAsync().ConfigureAwait(False)

                Dim updated As Supplier = Await SupplierRepository.GetByIdAsync(connection, supplierId, cancellationToken).ConfigureAwait(False)
                Return SupplierLifecycleOutcome.Success(Controllers.SuppliersController.ToResponse(updated))

            End Using

        End Function

    End Class

End Namespace
