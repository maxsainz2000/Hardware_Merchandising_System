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
'
' P4-11 ADDS THREE READ QUERIES - Stock.Read, LowStock.Review,
' Stock.ReviewMovements - in PurchaseOrderRepository's own SearchAsync shape
' (that class's header): sort is a closed enum, never a caller-supplied
' column name; COUNT(*) then a LIMIT/OFFSET page; Id is always the final
' tie-break so paging is stable. SearchStockAsync and SearchLowStockAsync
' share one query shape (StockBalanceItem's own header explains why) - the
' low-stock list is that same projection with a server-side WHERE applied,
' not a second definition of "current stock".

Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Tasks
Imports Merchandising.Domain.Entities
Imports MySqlConnector

Namespace Data

    ''' <summary>The sort fields <see cref="StockRepository.SearchStockAsync"/> and <see cref="StockRepository.SearchLowStockAsync"/> will honour.</summary>
    Public Enum StockSortField
        ProductName
        Quantity
        ReorderLevel
    End Enum

    ''' <summary>The one sort field <see cref="StockRepository.SearchMovementsAsync"/> honours - kept as an enum, not a bare "always CreatedAt", so a future second field is a one-line addition rather than a new mechanism.</summary>
    Public Enum StockMovementSortField
        CreatedAt
    End Enum

    Public NotInheritable Class StockRepository

        ''' <summary>
        ''' A plain, non-locking read of <paramref name="productId"/>'s
        ''' current balance - 0 when no StockBalances row exists yet (a
        ''' product that has never been touched by a stock-changing
        ''' command). P4-09 uses this, deliberately never
        ''' <c>SELECT ... FOR UPDATE</c>, so capturing a stock count's
        ''' "system quantity at the moment of counting" places no lock on
        ''' the row at all - the card's own Done-when box 2: "a count in
        ''' progress does not block sales or receiving on the same
        ''' product."
        ''' </summary>
        Public Shared Async Function GetQuantityAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            productId As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Decimal)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText = "SELECT Quantity FROM StockBalances WHERE ProductId = @productId;"
                command.Parameters.AddWithValue("@productId", productId)

                Dim result As Object = Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False)
                Return If(result Is Nothing OrElse result Is DBNull.Value, 0D, CDec(result))
            End Using

        End Function

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

        ''' <summary>
        ''' Paginated, sorted list of every product's current stock position -
        ''' Stock.Read (spec section 13 <c>GET /stock</c>). <paramref name="includeInactive"/>
        ''' defaults False, the same convention ProductRepository.SearchAsync
        ''' uses for the same reason (P2-09).
        ''' </summary>
        Public Shared Async Function SearchStockAsync(
            connection As MySqlConnection,
            includeInactive As Boolean,
            sortField As StockSortField,
            sortDescending As Boolean,
            page As Integer,
            pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Items As IReadOnlyList(Of StockBalanceItem), TotalCount As Integer))

            Dim whereClause As String = If(includeInactive, String.Empty, " WHERE p.IsActive = 1")

            Dim totalCount As Integer

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM Products p" & whereClause & ";"
                totalCount = CInt(Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False))
            End Using

            Dim items As New List(Of StockBalanceItem)

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT p.Id, p.Sku, p.Name, p.IsActive, p.ReorderLevel, COALESCE(b.Quantity, 0.000) AS Quantity " &
                    "  FROM Products p" &
                    "  LEFT JOIN StockBalances b ON b.ProductId = p.Id" &
                    whereClause &
                    " ORDER BY " & StockOrderByClause(sortField, sortDescending) &
                    " LIMIT @pageSize OFFSET @offset;"
                command.Parameters.AddWithValue("@pageSize", pageSize)
                command.Parameters.AddWithValue("@offset", (page - 1) * pageSize)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        items.Add(ReadStockBalanceItem(reader))
                    End While
                End Using
            End Using

            Return (Items:=items, TotalCount:=totalCount)

        End Function

        ''' <summary>
        ''' Paginated, sorted list of ACTIVE products at or below their own
        ''' reorder level - LowStock.Review (spec section 14's low-stock
        ''' report: "Active products at or below reorder level"). A product
        ''' with no StockBalances row at all is treated as zero, which is at
        ''' or below any non-negative reorder level - never excluded merely
        ''' for having no balance row yet.
        ''' </summary>
        Public Shared Async Function SearchLowStockAsync(
            connection As MySqlConnection,
            sortField As StockSortField,
            sortDescending As Boolean,
            page As Integer,
            pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Items As IReadOnlyList(Of StockBalanceItem), TotalCount As Integer))

            Const whereClause As String = " WHERE p.IsActive = 1 AND COALESCE(b.Quantity, 0.000) <= p.ReorderLevel"

            Dim totalCount As Integer

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT COUNT(*) FROM Products p LEFT JOIN StockBalances b ON b.ProductId = p.Id" & whereClause & ";"
                totalCount = CInt(Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False))
            End Using

            Dim items As New List(Of StockBalanceItem)

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT p.Id, p.Sku, p.Name, p.IsActive, p.ReorderLevel, COALESCE(b.Quantity, 0.000) AS Quantity " &
                    "  FROM Products p" &
                    "  LEFT JOIN StockBalances b ON b.ProductId = p.Id" &
                    whereClause &
                    " ORDER BY " & StockOrderByClause(sortField, sortDescending) &
                    " LIMIT @pageSize OFFSET @offset;"
                command.Parameters.AddWithValue("@pageSize", pageSize)
                command.Parameters.AddWithValue("@offset", (page - 1) * pageSize)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        items.Add(ReadStockBalanceItem(reader))
                    End While
                End Using
            End Using

            Return (Items:=items, TotalCount:=totalCount)

        End Function

        ''' <summary>
        ''' Paginated, sorted movement ledger for ONE product -
        ''' Stock.ReviewMovements (spec section 13 <c>GET /stock/movements</c>).
        ''' <paramref name="fromUtc"/>/<paramref name="toUtcExclusive"/> are
        ''' already-converted UTC instants - the caller (StockService) does the
        ''' store-local-date-to-UTC conversion, the same layering
        ''' PurchaseOrderRepository.SearchHistoryAsync uses.
        ''' </summary>
        Public Shared Async Function SearchMovementsAsync(
            connection As MySqlConnection,
            productId As Integer,
            fromUtc As DateTime?,
            toUtcExclusive As DateTime?,
            sortField As StockMovementSortField,
            sortDescending As Boolean,
            page As Integer,
            pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Items As IReadOnlyList(Of StockMovementItem), TotalCount As Integer))

            Dim conditions As New List(Of String) From {"ProductId = @productId"}

            If fromUtc.HasValue Then
                conditions.Add("CreatedAtUtc >= @fromUtc")
            End If

            If toUtcExclusive.HasValue Then
                conditions.Add("CreatedAtUtc < @toUtcExclusive")
            End If

            Dim whereClause As String = " WHERE " & String.Join(" AND ", conditions)

            Dim totalCount As Integer

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM StockMovements" & whereClause & ";"
                AddMovementFilterParameters(command, productId, fromUtc, toUtcExclusive)
                totalCount = CInt(Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False))
            End Using

            Dim items As New List(Of StockMovementItem)

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT Id, ProductId, Delta, QuantityBefore, QuantityAfter, Reason, ActorUserId, CorrelationId, CreatedAtUtc " &
                    "  FROM StockMovements" &
                    whereClause &
                    " ORDER BY " & MovementOrderByClause(sortField, sortDescending) &
                    " LIMIT @pageSize OFFSET @offset;"
                AddMovementFilterParameters(command, productId, fromUtc, toUtcExclusive)
                command.Parameters.AddWithValue("@pageSize", pageSize)
                command.Parameters.AddWithValue("@offset", (page - 1) * pageSize)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        items.Add(ReadMovementItem(reader))
                    End While
                End Using
            End Using

            Return (Items:=items, TotalCount:=totalCount)

        End Function

        ' --------------------------------------------------------------- helpers

        Private Shared Sub AddMovementFilterParameters(
            command As MySqlCommand, productId As Integer, fromUtc As DateTime?, toUtcExclusive As DateTime?)

            command.Parameters.AddWithValue("@productId", productId)

            If fromUtc.HasValue Then
                command.Parameters.AddWithValue("@fromUtc", fromUtc.Value)
            End If

            If toUtcExclusive.HasValue Then
                command.Parameters.AddWithValue("@toUtcExclusive", toUtcExclusive.Value)
            End If

        End Sub

        ''' <summary>Id is always the final tie-break so paging is stable - the same reasoning PurchaseOrderRepository.OrderByClause's own header gives.</summary>
        Private Shared Function StockOrderByClause(sortField As StockSortField, sortDescending As Boolean) As String

            Dim column As String

            Select Case sortField
                Case StockSortField.Quantity
                    column = "Quantity"
                Case StockSortField.ReorderLevel
                    column = "p.ReorderLevel"
                Case Else
                    column = "p.Name"
            End Select

            Dim direction As String = If(sortDescending, " DESC", " ASC")

            Return column & direction & ", p.Id" & direction

        End Function

        Private Shared Function MovementOrderByClause(sortField As StockMovementSortField, sortDescending As Boolean) As String

            Dim direction As String = If(sortDescending, " DESC", " ASC")
            Return "CreatedAtUtc" & direction & ", Id" & direction

        End Function

        Private Shared Function ReadStockBalanceItem(reader As MySqlDataReader) As StockBalanceItem

            Return New StockBalanceItem With {
                .ProductId = reader.GetInt32(reader.GetOrdinal("Id")),
                .Sku = reader.GetString(reader.GetOrdinal("Sku")),
                .Name = reader.GetString(reader.GetOrdinal("Name")),
                .IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                .ReorderLevel = reader.GetDecimal(reader.GetOrdinal("ReorderLevel")),
                .Quantity = reader.GetDecimal(reader.GetOrdinal("Quantity"))
            }

        End Function

        ''' <summary>
        ''' MySqlConnector maps CHAR(36) to System.Guid, not String -
        ''' reader.GetString() throws InvalidCastException on CorrelationId
        ''' (MaintenanceLockRepository's header names this exact gotcha for
        ''' every CHAR(36) column in the schema). GetGuid() is the correct
        ''' typed accessor.
        ''' </summary>
        Private Shared Function ReadMovementItem(reader As MySqlDataReader) As StockMovementItem

            Return New StockMovementItem With {
                .Id = reader.GetInt32(reader.GetOrdinal("Id")),
                .ProductId = reader.GetInt32(reader.GetOrdinal("ProductId")),
                .Delta = reader.GetDecimal(reader.GetOrdinal("Delta")),
                .QuantityBefore = reader.GetDecimal(reader.GetOrdinal("QuantityBefore")),
                .QuantityAfter = reader.GetDecimal(reader.GetOrdinal("QuantityAfter")),
                .Reason = reader.GetString(reader.GetOrdinal("Reason")),
                .ActorUserId = reader.GetInt32(reader.GetOrdinal("ActorUserId")),
                .CorrelationId = reader.GetGuid(reader.GetOrdinal("CorrelationId")).ToString("d"),
                .CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("CreatedAtUtc"))
            }

        End Function

    End Class

End Namespace
