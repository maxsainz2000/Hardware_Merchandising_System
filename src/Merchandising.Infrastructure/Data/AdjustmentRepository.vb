' Merchandising.Infrastructure.Data.AdjustmentRepository
'
' P4-10: writes and locked reads against StockAdjustments
' (db/migrations/0010_counts-and-adjustments.sql).
'
' THE ROW IS LOCKED BEFORE A TRANSITION IS DECIDED - GetForUpdateAsync's
' SELECT ... FOR UPDATE is what makes Approve/Reject safe against a
' concurrent second Approve/Reject racing the same row, the identical
' "lock, then decide, then write" shape PurchaseOrderRepository.GetStatusForUpdateAsync
' uses for a purchase order's own status column.
'
' EVERY WRITE HERE TAKES A TRANSACTION, WITH NO OPTIONAL OVERLOAD -
' AdjustmentService owns it (ADR-006), the same arrangement every other
' repository in this solution uses.

Imports System.Threading
Imports System.Threading.Tasks
Imports Merchandising.Domain.Entities
Imports Merchandising.Domain.Inventory
Imports MySqlConnector

Namespace Data

    Public NotInheritable Class AdjustmentRepository

        Private Const AdjustmentColumns As String =
            "a.Id, a.ProductId, p.Sku AS ProductSku, p.Name AS ProductName, a.QuantityVariance, a.Reason, " &
            "a.RequestedByUserId, a.ApprovedByUserId, a.ExceedsThreshold, a.Status, " &
            "a.RowVersion, a.CreatedAtUtc, a.UpdatedAtUtc"

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Inserts a new adjustment, already resolved to
        ''' <paramref name="status"/> - <c>Pending</c> (no approver yet) when
        ''' <paramref name="exceedsThreshold"/>, or <c>Applied</c> (approver
        ''' stays null forever - nobody approved it) when it does not.
        ''' </summary>
        ''' <returns>The new adjustment's Id.</returns>
        Public Shared Async Function InsertAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            productId As Integer,
            quantityVariance As Decimal,
            reason As String,
            requestedByUserId As Integer,
            exceedsThreshold As Boolean,
            status As StockAdjustmentStatus,
            createdAtUtc As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Integer)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO StockAdjustments " &
                    "(ProductId, QuantityVariance, Reason, RequestedByUserId, ExceedsThreshold, Status, " &
                    " RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@productId, @quantityVariance, @reason, @requestedByUserId, @exceedsThreshold, @status, " &
                    " 0, @createdAtUtc, @createdAtUtc);"
                command.Parameters.AddWithValue("@productId", productId)
                command.Parameters.AddWithValue("@quantityVariance", quantityVariance)
                command.Parameters.AddWithValue("@reason", reason)
                command.Parameters.AddWithValue("@requestedByUserId", requestedByUserId)
                command.Parameters.AddWithValue("@exceedsThreshold", If(exceedsThreshold, 1, 0))
                command.Parameters.AddWithValue("@status", status.ToString())
                command.Parameters.AddWithValue("@createdAtUtc", createdAtUtc)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return CInt(command.LastInsertedId)
            End Using

        End Function

        ''' <summary>
        ''' Locks one StockAdjustments row with <c>SELECT ... FOR UPDATE</c>,
        ''' inside <paramref name="transaction"/>. Found = False when no such
        ''' adjustment exists.
        ''' </summary>
        Public Shared Async Function GetForUpdateAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            id As Integer,
            Optional cancellationToken As CancellationToken = Nothing) _
            As Task(Of (Found As Boolean, ProductId As Integer, QuantityVariance As Decimal, RequestedByUserId As Integer, Status As StockAdjustmentStatus))

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "SELECT ProductId, QuantityVariance, RequestedByUserId, Status " &
                    "  FROM StockAdjustments WHERE Id = @id FOR UPDATE;"
                command.Parameters.AddWithValue("@id", id)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    If Not Await reader.ReadAsync(cancellationToken).ConfigureAwait(False) Then
                        Return (Found:=False, ProductId:=0, QuantityVariance:=0D, RequestedByUserId:=0, Status:=CType(0, StockAdjustmentStatus))
                    End If

                    Return (Found:=True,
                            ProductId:=reader.GetInt32(0),
                            QuantityVariance:=reader.GetDecimal(1),
                            RequestedByUserId:=reader.GetInt32(2),
                            Status:=ParseStatus(reader.GetString(3)))
                End Using
            End Using

        End Function

        ''' <summary>
        ''' Moves a locked (Pending) adjustment to Applied. Always called
        ''' after <see cref="GetForUpdateAsync"/> has already locked and
        ''' confirmed the row within the same transaction, so the
        ''' affected-row count here is a defensive check, not the mechanism
        ''' that prevents a lost update - the row lock is (same arrangement
        ''' as PurchaseOrderRepository.MarkApprovedAsync). <paramref name="approvedByUserId"/>
        ''' is Nothing for an adjustment applied directly (below threshold) -
        ''' nobody approved it.
        ''' </summary>
        Public Shared Async Function MarkAppliedAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            id As Integer,
            approvedByUserId As Integer?,
            updatedAtUtc As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Boolean)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "UPDATE StockAdjustments " &
                    "   SET Status = @status, " &
                    "       ApprovedByUserId = @approvedByUserId, " &
                    "       RowVersion = RowVersion + 1, " &
                    "       UpdatedAtUtc = @updatedAtUtc " &
                    " WHERE Id = @id;"
                command.Parameters.AddWithValue("@status", StockAdjustmentStatus.Applied.ToString())
                command.Parameters.AddWithValue("@approvedByUserId", If(CObj(approvedByUserId), DBNull.Value))
                command.Parameters.AddWithValue("@updatedAtUtc", updatedAtUtc)
                command.Parameters.AddWithValue("@id", id)

                Dim affectedRows As Integer = Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return affectedRows = 1
            End Using

        End Function

        ''' <summary>Moves a locked (Pending) adjustment to Rejected. No stock effect - same locked-row arrangement as <see cref="MarkAppliedAsync"/>.</summary>
        Public Shared Async Function MarkRejectedAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            id As Integer,
            approvedByUserId As Integer,
            updatedAtUtc As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Boolean)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "UPDATE StockAdjustments " &
                    "   SET Status = @status, " &
                    "       ApprovedByUserId = @approvedByUserId, " &
                    "       RowVersion = RowVersion + 1, " &
                    "       UpdatedAtUtc = @updatedAtUtc " &
                    " WHERE Id = @id;"
                command.Parameters.AddWithValue("@status", StockAdjustmentStatus.Rejected.ToString())
                command.Parameters.AddWithValue("@approvedByUserId", approvedByUserId)
                command.Parameters.AddWithValue("@updatedAtUtc", updatedAtUtc)
                command.Parameters.AddWithValue("@id", id)

                Dim affectedRows As Integer = Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return affectedRows = 1
            End Using

        End Function

        ''' <summary>A plain, non-locking read of one adjustment, joined to Products - for building a response after a write already committed.</summary>
        Public Shared Async Function GetByIdAsync(
            connection As MySqlConnection,
            id As Integer,
            Optional cancellationToken As CancellationToken = Nothing,
            Optional transaction As MySqlTransaction = Nothing) As Task(Of StockAdjustment)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "SELECT " & AdjustmentColumns &
                    "  FROM StockAdjustments a" &
                    "  JOIN Products p ON p.Id = a.ProductId" &
                    " WHERE a.Id = @id;"
                command.Parameters.AddWithValue("@id", id)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    If Not Await reader.ReadAsync(cancellationToken).ConfigureAwait(False) Then
                        Return Nothing
                    End If
                    Return ReadAdjustment(reader)
                End Using
            End Using

        End Function

        Private Shared Function ReadAdjustment(reader As MySqlDataReader) As StockAdjustment

            Dim approvedByOrdinal As Integer = reader.GetOrdinal("ApprovedByUserId")

            Return New StockAdjustment With {
                .Id = reader.GetInt32(reader.GetOrdinal("Id")),
                .ProductId = reader.GetInt32(reader.GetOrdinal("ProductId")),
                .ProductSku = reader.GetString(reader.GetOrdinal("ProductSku")),
                .ProductName = reader.GetString(reader.GetOrdinal("ProductName")),
                .QuantityVariance = reader.GetDecimal(reader.GetOrdinal("QuantityVariance")),
                .Reason = reader.GetString(reader.GetOrdinal("Reason")),
                .RequestedByUserId = reader.GetInt32(reader.GetOrdinal("RequestedByUserId")),
                .ApprovedByUserId = If(reader.IsDBNull(approvedByOrdinal), CType(Nothing, Integer?), reader.GetInt32(approvedByOrdinal)),
                .ExceedsThreshold = reader.GetBoolean(reader.GetOrdinal("ExceedsThreshold")),
                .Status = ParseStatus(reader.GetString(reader.GetOrdinal("Status"))),
                .RowVersion = reader.GetInt64(reader.GetOrdinal("RowVersion")),
                .CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("CreatedAtUtc")),
                .UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("UpdatedAtUtc"))
            }

        End Function

        Private Shared Function ParseStatus(storedName As String) As StockAdjustmentStatus
            Return CType([Enum].Parse(GetType(StockAdjustmentStatus), storedName), StockAdjustmentStatus)
        End Function

    End Class

End Namespace
