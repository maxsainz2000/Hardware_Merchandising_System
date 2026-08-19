' Merchandising.Infrastructure.Data.StockMovementWriter
'
' Single insertion point for StockMovements (CLAUDE.md section 5: append-only,
' enforced by grant - merch_api holds INSERT only, never UPDATE or DELETE, on
' this table; db/grants/0002_post-migration-grants.sql). Mirrors
' AuditLogWriter's shape: takes an already-open connection and the caller's
' transaction, so the movement row commits or rolls back with everything
' else in the same atomic operation (ADR-006).

Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Data

    Public NotInheritable Class StockMovementWriter

        ''' <summary>
        ''' Writes one movement row. <paramref name="delta"/> is signed - a
        ''' decrement passes a negative value, so a future increment (goods
        ''' receipt, sale return) can reuse this method unchanged.
        ''' </summary>
        ''' <returns>The new movement's Id.</returns>
        Public Shared Async Function WriteAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            productId As Integer,
            delta As Decimal,
            quantityBefore As Decimal,
            quantityAfter As Decimal,
            reason As String,
            actorUserId As Integer,
            correlationId As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Integer)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO StockMovements " &
                    "(ProductId, Delta, QuantityBefore, QuantityAfter, Reason, ActorUserId, CorrelationId, CreatedAtUtc) " &
                    "VALUES (@productId, @delta, @quantityBefore, @quantityAfter, @reason, @actorUserId, @correlationId, UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@productId", productId)
                command.Parameters.AddWithValue("@delta", delta)
                command.Parameters.AddWithValue("@quantityBefore", quantityBefore)
                command.Parameters.AddWithValue("@quantityAfter", quantityAfter)
                command.Parameters.AddWithValue("@reason", reason)
                command.Parameters.AddWithValue("@actorUserId", actorUserId)
                command.Parameters.AddWithValue("@correlationId", correlationId)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return CInt(command.LastInsertedId)
            End Using

        End Function

    End Class

End Namespace
