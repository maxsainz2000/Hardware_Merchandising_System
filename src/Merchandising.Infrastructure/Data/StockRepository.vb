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
        ''' Increases <paramref name="productId"/>'s balance by
        ''' <paramref name="quantity"/> inside <paramref name="transaction"/>
        ''' - P4-05's goods-received effect. Unlike
        ''' <see cref="TryDecrementAsync"/> there is no insufficiency case to
        ''' report: an increase can never be refused by a WHERE clause the way
        ''' a decrease can, so this always succeeds.
        ''' </summary>
        ''' <remarks>
        ''' STILL NOT READ-THEN-WRITE, AND STILL ONE ATOMIC STATEMENT - the
        ''' same ADR-006 shape as TryDecrementAsync, adapted for the one thing
        ''' that differs here: a product freshly created by P3-03 has never
        ''' had a StockBalances row written for it (ProductRepository.InsertAsync
        ''' does not seed one; the earliest a row exists is whichever
        ''' stock-changing command touches that product first). An UPDATE
        ''' alone would silently affect zero rows for such a product and this
        ''' receipt's stock increase would be lost. <c>INSERT ... ON DUPLICATE
        ''' KEY UPDATE</c> is MariaDB's single-statement atomic upsert:
        ''' ProductId is the primary key, so the engine's own duplicate-key
        ''' handling - not a prior read - decides whether this becomes an
        ''' INSERT or an UPDATE, and two concurrent increments against the
        ''' same never-yet-balanced product cannot both "win" an insert and
        ''' collide, the same guarantee a unique index gives
        ''' IdempotencyStore.TryClaimAsync.
        ''' </remarks>
        Public Shared Async Function IncrementAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            productId As Integer,
            quantity As Decimal,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (QuantityBefore As Decimal, QuantityAfter As Decimal))

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO StockBalances (ProductId, Quantity, RowVersion, UpdatedAtUtc) " &
                    "VALUES (@productId, @qty, 0, UTC_TIMESTAMP(6)) " &
                    "ON DUPLICATE KEY UPDATE " &
                    "    Quantity = Quantity + VALUES(Quantity), " &
                    "    RowVersion = RowVersion + 1, " &
                    "    UpdatedAtUtc = UTC_TIMESTAMP(6);"
                command.Parameters.AddWithValue("@productId", productId)
                command.Parameters.AddWithValue("@qty", quantity)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
            End Using

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText = "SELECT Quantity FROM StockBalances WHERE ProductId = @productId;"
                command.Parameters.AddWithValue("@productId", productId)

                Dim quantityAfter As Decimal = CDec(Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False))
                Return (QuantityBefore:=quantityAfter - quantity, QuantityAfter:=quantityAfter)
            End Using

        End Function

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
