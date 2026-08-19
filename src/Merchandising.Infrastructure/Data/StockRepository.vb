' Merchandising.Infrastructure.Data.StockRepository
'
' ADR-006's conditional update, exactly as pinned in CLAUDE.md section 5 and
' docs/adr.md: the WHERE clause carries the sufficiency check, so the loser
' of a race simply affects zero rows rather than being decided by a prior
' read. This is never read-then-write - TryDecrementAsync's own SELECT runs
' AFTER the UPDATE, inside the same transaction, purely to report the
' before/after values for the movement row. It cannot change what already
' happened, and InnoDB guarantees a transaction sees its own uncommitted
' write, so QuantityAfter always reflects the UPDATE that just ran.

Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Data

    Public NotInheritable Class StockRepository

        ''' <summary>
        ''' Attempts to decrement <paramref name="productId"/>'s balance by
        ''' <paramref name="quantity"/> inside <paramref name="transaction"/>.
        ''' Returns Succeeded = False, with no row changed, when no
        ''' StockBalances row exists for the product or the current quantity
        ''' is less than requested - the two cases the WHERE clause cannot
        ''' tell apart, and does not need to (CLAUDE.md section 5: "a
        ''' controlled insufficient-stock or concurrency response").
        ''' </summary>
        Public Shared Async Function TryDecrementAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            productId As Integer,
            quantity As Decimal,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Succeeded As Boolean, QuantityBefore As Decimal, QuantityAfter As Decimal))

            Dim affectedRows As Integer

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "UPDATE StockBalances " &
                    "   SET Quantity = Quantity - @qty, " &
                    "       RowVersion = RowVersion + 1, " &
                    "       UpdatedAtUtc = UTC_TIMESTAMP(6) " &
                    " WHERE ProductId = @productId " &
                    "   AND Quantity >= @qty;"
                command.Parameters.AddWithValue("@qty", quantity)
                command.Parameters.AddWithValue("@productId", productId)

                affectedRows = Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
            End Using

            ' The affected-row count is verified BEFORE anything is reported
            ' as a success - CLAUDE.md section 5's "affected rows must be
            ' exactly 1, else roll back". A duplicate-key style race cannot
            ' make this 2: ProductId is the primary key, so at most one row
            ' can ever match.
            If affectedRows <> 1 Then
                Return (Succeeded:=False, QuantityBefore:=0D, QuantityAfter:=0D)
            End If

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText = "SELECT Quantity FROM StockBalances WHERE ProductId = @productId;"
                command.Parameters.AddWithValue("@productId", productId)

                Dim quantityAfter As Decimal = CDec(Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False))
                Return (Succeeded:=True, QuantityBefore:=quantityAfter + quantity, QuantityAfter:=quantityAfter)
            End Using

        End Function

    End Class

End Namespace
