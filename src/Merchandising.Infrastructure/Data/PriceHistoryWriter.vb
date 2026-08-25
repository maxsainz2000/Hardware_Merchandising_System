' Merchandising.Infrastructure.Data.PriceHistoryWriter
'
' Single insertion point for PriceHistory (CLAUDE.md section 5: append-only,
' enforced by grant - merch_api holds INSERT only, never UPDATE or DELETE,
' on this table; db/grants/0008_product-master-grants.sql). Mirrors
' StockMovementWriter's shape: takes an already-open connection and the
' caller's transaction, so the history row commits or rolls back with
' everything else in the same atomic operation (ADR-006).

Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Data

    Public NotInheritable Class PriceHistoryWriter

        ''' <summary>Writes one PriceHistory row. <paramref name="changedField"/> is "Price" or "Cost" (0006_product-master.sql's CK_PriceHistory_ChangedField).</summary>
        Public Shared Async Function WriteAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            productId As Integer,
            changedField As String,
            oldValue As Decimal,
            newValue As Decimal,
            actorUserId As Integer,
            effectiveAtUtc As DateTime,
            correlationId As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Integer)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO PriceHistory " &
                    "(ProductId, ChangedField, OldValue, NewValue, ActorUserId, EffectiveAtUtc, CorrelationId, CreatedAtUtc) " &
                    "VALUES (@productId, @changedField, @oldValue, @newValue, @actorUserId, @effectiveAtUtc, @correlationId, UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@productId", productId)
                command.Parameters.AddWithValue("@changedField", changedField)
                command.Parameters.AddWithValue("@oldValue", oldValue)
                command.Parameters.AddWithValue("@newValue", newValue)
                command.Parameters.AddWithValue("@actorUserId", actorUserId)
                command.Parameters.AddWithValue("@effectiveAtUtc", effectiveAtUtc)
                command.Parameters.AddWithValue("@correlationId", correlationId)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return CInt(command.LastInsertedId)
            End Using

        End Function

    End Class

End Namespace
