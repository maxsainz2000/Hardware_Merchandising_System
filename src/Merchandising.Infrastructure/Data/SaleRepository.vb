' Merchandising.Infrastructure.Data.SaleRepository
'
' P5-07: writes against Sales, SaleLines and SalePayments (db/migrations/
' 0011_pos.sql). All three are append-only (db/grants/0013_pos-grants.sql
' grants INSERT only, never UPDATE or DELETE) - this class has no update or
' delete method for any of them, on purpose, the same absence
' StockMovementWriter/AuditLogWriter already establish for their own
' append-only tables.
'
' EVERY WRITE HERE TAKES A TRANSACTION, WITH NO OPTIONAL OVERLOAD -
' SaleService owns it (ADR-006), the same arrangement every other repository
' in this solution uses (CashierSessionRepository's identical header note).

Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Data

    Public NotInheritable Class SaleRepository

        Private Sub New()
        End Sub

        ''' <summary>Inserts the Sales header, already Completed - a sale row is never inserted in any other status (Merchandising.Domain.Sales.SaleStatus's own header).</summary>
        ''' <returns>The new sale's Id.</returns>
        Public Shared Async Function InsertSaleAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            cashierSessionId As Integer,
            cashierUserId As Integer,
            total As Decimal,
            correlationId As String,
            createdAtUtc As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Integer)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO Sales (CashierSessionId, CashierUserId, Total, Status, CorrelationId, CreatedAtUtc) " &
                    "VALUES (@cashierSessionId, @cashierUserId, @total, 'Completed', @correlationId, @createdAtUtc);"
                command.Parameters.AddWithValue("@cashierSessionId", cashierSessionId)
                command.Parameters.AddWithValue("@cashierUserId", cashierUserId)
                command.Parameters.AddWithValue("@total", total)
                command.Parameters.AddWithValue("@correlationId", correlationId)
                command.Parameters.AddWithValue("@createdAtUtc", createdAtUtc)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return CInt(command.LastInsertedId)
            End Using

        End Function

        ''' <summary>Inserts one SaleLines row - UnitPrice/Cost/LineTotal are the already-captured, already-rounded values (Merchandising.Domain.Sales.SaleLine), never recomputed here.</summary>
        ''' <returns>The new line's Id.</returns>
        Public Shared Async Function InsertLineAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            saleId As Integer,
            productId As Integer,
            quantity As Decimal,
            unitPrice As Decimal,
            cost As Decimal,
            lineTotal As Decimal,
            createdAtUtc As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Integer)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO SaleLines (SaleId, ProductId, Quantity, UnitPrice, Cost, LineTotal, CreatedAtUtc) " &
                    "VALUES (@saleId, @productId, @quantity, @unitPrice, @cost, @lineTotal, @createdAtUtc);"
                command.Parameters.AddWithValue("@saleId", saleId)
                command.Parameters.AddWithValue("@productId", productId)
                command.Parameters.AddWithValue("@quantity", quantity)
                command.Parameters.AddWithValue("@unitPrice", unitPrice)
                command.Parameters.AddWithValue("@cost", cost)
                command.Parameters.AddWithValue("@lineTotal", lineTotal)
                command.Parameters.AddWithValue("@createdAtUtc", createdAtUtc)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return CInt(command.LastInsertedId)
            End Using

        End Function

        ''' <summary>
        ''' Inserts the one SalePayments row a sale carries (SaleService's own
        ''' header explains why this is a single row, not a list, in this
        ''' card). <paramref name="tenderedAmount"/>/<paramref name="changeAmount"/>
        ''' must both be Nothing for Card/EWallet and both set for Cash -
        ''' CK_SalePayments_CashTenderPairing (0011_pos.sql) is the backstop
        ''' if that pairing is ever wrong; SalesController's own validation
        ''' is what makes it a controlled 400 rather than a raw CHECK failure.
        ''' </summary>
        ''' <returns>The new payment's Id.</returns>
        Public Shared Async Function InsertPaymentAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            saleId As Integer,
            method As String,
            amount As Decimal,
            tenderedAmount As Decimal?,
            changeAmount As Decimal?,
            createdAtUtc As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Integer)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO SalePayments (SaleId, Method, Amount, TenderedAmount, ChangeAmount, CreatedAtUtc) " &
                    "VALUES (@saleId, @method, @amount, @tenderedAmount, @changeAmount, @createdAtUtc);"
                command.Parameters.AddWithValue("@saleId", saleId)
                command.Parameters.AddWithValue("@method", method)
                command.Parameters.AddWithValue("@amount", amount)
                command.Parameters.AddWithValue("@tenderedAmount", If(CObj(tenderedAmount), DBNull.Value))
                command.Parameters.AddWithValue("@changeAmount", If(CObj(changeAmount), DBNull.Value))
                command.Parameters.AddWithValue("@createdAtUtc", createdAtUtc)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return CInt(command.LastInsertedId)
            End Using

        End Function

        ''' <summary>P5-11: confirms a Sales row exists before validating its return's lines - the same "confirm first, for a clear NotFound before line-level errors" shape ReceiptRepository.ExistsAsync uses for purchase returns.</summary>
        Public Shared Async Function ExistsAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            saleId As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Boolean)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText = "SELECT 1 FROM Sales WHERE Id = @id;"
                command.Parameters.AddWithValue("@id", saleId)

                Dim result As Object = Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False)
                Return result IsNot Nothing
            End Using

        End Function

    End Class

End Namespace
