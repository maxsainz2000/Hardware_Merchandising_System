' Merchandising.Infrastructure.Data.StockCountRepository
'
' P4-09: writes and locked reads against StockCounts and StockCountLines
' (db/migrations/0010_counts-and-adjustments.sql).
'
' THE HEADER IS LOCKED; THE BALANCE NEVER IS. GetForUpdateAsync's
' SELECT ... FOR UPDATE on the StockCounts row is what makes "record a line"
' and "close" safe against a concurrent close/second-record racing the same
' session - the identical "lock the row this class owns before deciding"
' shape PurchaseOrderRepository.GetLinesForUpdateAsync and
' PurchaseReturnRepository.GetReceiptLineForUpdateAsync already use. It
' deliberately never touches StockBalances: the card's own Done-when box 2
' ("a count in progress does not block sales or receiving on the same
' product") is met by never locking that row at all - StockCountService
' reads the current balance with StockRepository.GetQuantityAsync, an
' ordinary non-locking SELECT, not a FOR UPDATE.
'
' EVERY WRITE HERE TAKES A TRANSACTION, WITH NO OPTIONAL OVERLOAD -
' StockCountService owns it (ADR-006), the same arrangement every other
' repository in this solution uses.

Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Data

    Public NotInheritable Class StockCountRepository

        Private Sub New()
        End Sub

        ''' <summary>Opens a new count session, already Open, CountedAtUtc = <paramref name="countedAtUtc"/>.</summary>
        ''' <returns>The new session's Id.</returns>
        Public Shared Async Function InsertStockCountAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            countedByUserId As Integer,
            countedAtUtc As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Integer)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO StockCounts (Status, CountedByUserId, CountedAtUtc, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES ('Open', @countedByUserId, @countedAtUtc, 0, @countedAtUtc, @countedAtUtc);"
                command.Parameters.AddWithValue("@countedByUserId", countedByUserId)
                command.Parameters.AddWithValue("@countedAtUtc", countedAtUtc)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return CInt(command.LastInsertedId)
            End Using

        End Function

        ''' <summary>
        ''' Locks one StockCounts row with <c>SELECT ... FOR UPDATE</c>, inside
        ''' <paramref name="transaction"/> - see this file's header for why
        ''' this is the concurrency mechanism for the header, and never for
        ''' StockBalances. Found = False when no such session exists.
        ''' </summary>
        Public Shared Async Function GetForUpdateAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            stockCountId As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Found As Boolean, Status As String, CountedByUserId As Integer))

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "SELECT Status, CountedByUserId FROM StockCounts WHERE Id = @id FOR UPDATE;"
                command.Parameters.AddWithValue("@id", stockCountId)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    If Not Await reader.ReadAsync(cancellationToken).ConfigureAwait(False) Then
                        Return (Found:=False, Status:=Nothing, CountedByUserId:=0)
                    End If

                    Return (Found:=True, Status:=reader.GetString(0), CountedByUserId:=reader.GetInt32(1))
                End Using
            End Using

        End Function

        ''' <summary>A plain, non-locking read of one session's header - for GET and for building a response after a write already committed.</summary>
        Public Shared Async Function GetByIdAsync(
            connection As MySqlConnection,
            stockCountId As Integer,
            Optional cancellationToken As CancellationToken = Nothing,
            Optional transaction As MySqlTransaction = Nothing) _
            As Task(Of (Found As Boolean, Status As String, CountedByUserId As Integer, ApprovedByUserId As Integer?,
                        CountedAtUtc As DateTime, ApprovedAtUtc As DateTime?, RowVersion As Long,
                        CreatedAtUtc As DateTime, UpdatedAtUtc As DateTime))

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "SELECT Status, CountedByUserId, ApprovedByUserId, CountedAtUtc, ApprovedAtUtc, " &
                    "       RowVersion, CreatedAtUtc, UpdatedAtUtc " &
                    "  FROM StockCounts WHERE Id = @id;"
                command.Parameters.AddWithValue("@id", stockCountId)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    If Not Await reader.ReadAsync(cancellationToken).ConfigureAwait(False) Then
                        Return (Found:=False, Status:=Nothing, CountedByUserId:=0, ApprovedByUserId:=Nothing,
                                CountedAtUtc:=DateTime.MinValue, ApprovedAtUtc:=Nothing, RowVersion:=0,
                                CreatedAtUtc:=DateTime.MinValue, UpdatedAtUtc:=DateTime.MinValue)
                    End If

                    Return (Found:=True,
                            Status:=reader.GetString(0),
                            CountedByUserId:=reader.GetInt32(1),
                            ApprovedByUserId:=If(reader.IsDBNull(2), CType(Nothing, Integer?), reader.GetInt32(2)),
                            CountedAtUtc:=reader.GetDateTime(3),
                            ApprovedAtUtc:=If(reader.IsDBNull(4), CType(Nothing, DateTime?), reader.GetDateTime(4)),
                            RowVersion:=reader.GetInt64(5),
                            CreatedAtUtc:=reader.GetDateTime(6),
                            UpdatedAtUtc:=reader.GetDateTime(7))
                End Using
            End Using

        End Function

        ''' <summary>Inserts one counted line. SystemQuantity and Variance are the CALLER's responsibility to have already computed from a fresh StockBalances read (RecordStockCountLineRequest's header).</summary>
        ''' <returns>The new line's Id.</returns>
        Public Shared Async Function InsertStockCountLineAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            stockCountId As Integer,
            productId As Integer,
            countedQuantity As Decimal,
            systemQuantity As Decimal,
            variance As Decimal,
            createdAtUtc As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Integer)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO StockCountLines (StockCountId, ProductId, CountedQuantity, SystemQuantity, Variance, CreatedAtUtc) " &
                    "VALUES (@stockCountId, @productId, @counted, @system, @variance, @createdAtUtc);"
                command.Parameters.AddWithValue("@stockCountId", stockCountId)
                command.Parameters.AddWithValue("@productId", productId)
                command.Parameters.AddWithValue("@counted", countedQuantity)
                command.Parameters.AddWithValue("@system", systemQuantity)
                command.Parameters.AddWithValue("@variance", variance)
                command.Parameters.AddWithValue("@createdAtUtc", createdAtUtc)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return CInt(command.LastInsertedId)
            End Using

        End Function

        ''' <summary>Every line of one session, oldest first. Lines are never edited (0010's own migration comment), so there is no locked variant of this read.</summary>
        Public Shared Async Function GetLinesAsync(
            connection As MySqlConnection,
            stockCountId As Integer,
            Optional cancellationToken As CancellationToken = Nothing,
            Optional transaction As MySqlTransaction = Nothing) _
            As Task(Of IReadOnlyList(Of (Id As Integer, ProductId As Integer, CountedQuantity As Decimal, SystemQuantity As Decimal, Variance As Decimal, CreatedAtUtc As DateTime)))

            Dim lines As New List(Of (Id As Integer, ProductId As Integer, CountedQuantity As Decimal, SystemQuantity As Decimal, Variance As Decimal, CreatedAtUtc As DateTime))

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "SELECT Id, ProductId, CountedQuantity, SystemQuantity, Variance, CreatedAtUtc " &
                    "  FROM StockCountLines WHERE StockCountId = @stockCountId ORDER BY Id ASC;"
                command.Parameters.AddWithValue("@stockCountId", stockCountId)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        lines.Add((Id:=reader.GetInt32(0),
                                   ProductId:=reader.GetInt32(1),
                                   CountedQuantity:=reader.GetDecimal(2),
                                   SystemQuantity:=reader.GetDecimal(3),
                                   Variance:=reader.GetDecimal(4),
                                   CreatedAtUtc:=reader.GetDateTime(5)))
                    End While
                End Using
            End Using

            Return lines

        End Function

        ''' <summary>Transitions a locked (Open) session to Closed.</summary>
        Public Shared Async Function CloseStockCountAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            stockCountId As Integer,
            updatedAtUtc As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) As Task

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "UPDATE StockCounts SET Status = 'Closed', RowVersion = RowVersion + 1, UpdatedAtUtc = @updatedAtUtc " &
                    " WHERE Id = @id;"
                command.Parameters.AddWithValue("@updatedAtUtc", updatedAtUtc)
                command.Parameters.AddWithValue("@id", stockCountId)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
            End Using

        End Function

    End Class

End Namespace
