' Merchandising.Infrastructure.Data.PurchaseReturnRepository
'
' P4-08: writes against PurchaseReturns and PurchaseReturnLines
' (db/migrations/0009_receiving.sql), and the locked read against
' ReceiptLines the bound check depends on.
'
' THE CONCURRENCY MECHANISM, NAMED EXPLICITLY. Spec section 12: "receipt
' quantity bounded by ordered quantity" has CK_PurchaseOrderLines_ReceivedQuantity
' as its database-level backstop (P3-02/P4-07); "return quantity bounded by
' received quantity less prior returns" has NO equivalent CHECK constraint,
' because the bound is an AGGREGATE over every prior sibling PurchaseReturnLines
' row for the same ReceiptLineId - a value a single-row CHECK cannot see
' (0009's own comment on PurchaseReturnLines says this explicitly). So the
' bound is enforced here, at the API, computed from committed rows inside a
' transaction - and GetReceiptLineForUpdateAsync's SELECT ... FOR UPDATE is
' what makes that safe under concurrency. Locking a row this class never
' writes to is deliberate: ReceiptLines has no UPDATE grant to merch_api
' (db/grants/0011 - INSERT only), but SELECT ... FOR UPDATE needs only
' SELECT privilege, which merch_api already holds at the database level
' (ADR-013). Two return attempts against the SAME ReceiptLineId cannot both
' proceed past this lock: the second blocks until the first's transaction
' resolves, then (under READ COMMITTED, ADR-006) sees whatever the first
' actually committed before computing its own prior-returned sum - the same
' "lock, then decide" shape PurchaseOrderRepository.GetLinesForUpdateAsync
' uses for P4-05/P4-07, applied here because there is no StockBalances-style
' single row to hang a conditional UPDATE off instead.
'
' EVERY WRITE HERE TAKES A TRANSACTION, WITH NO OPTIONAL OVERLOAD -
' PurchaseReturnService owns it (ADR-006), the same arrangement every other
' repository in this solution uses.
'
' A DUPLICATE REFERENCE NUMBER IS AN OUTCOME, NOT AN EXCEPTION - the same
' ERROR 1062 translation ReceiptRepository.InsertReceiptAsync already uses,
' for the identical reason: the reference number is the clerk's own
' document number, so a collision is the caller's problem to correct, not
' the server's race to retry invisibly.

Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Data

    ''' <summary>Outcome of a purchase-return header insert.</summary>
    Public Enum PurchaseReturnWriteOutcomeKind
        Success
        DuplicateReferenceNumber
    End Enum

    Public NotInheritable Class PurchaseReturnRepository

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Locks one ReceiptLines row with <c>SELECT ... FOR UPDATE</c>,
        ''' inside <paramref name="transaction"/> - see this file's header
        ''' for why this is the concurrency mechanism rather than a
        ''' conditional UPDATE. Found = False when no such line exists.
        ''' </summary>
        Public Shared Async Function GetReceiptLineForUpdateAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            receiptLineId As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Found As Boolean, ReceiptId As Integer, ProductId As Integer, Cost As Decimal, QuantityReceived As Decimal))

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "SELECT ReceiptId, ProductId, Cost, QuantityReceived " &
                    "  FROM ReceiptLines " &
                    " WHERE Id = @id " &
                    " FOR UPDATE;"
                command.Parameters.AddWithValue("@id", receiptLineId)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    If Not Await reader.ReadAsync(cancellationToken).ConfigureAwait(False) Then
                        Return (Found:=False, ReceiptId:=0, ProductId:=0, Cost:=0D, QuantityReceived:=0D)
                    End If

                    Return (Found:=True,
                            ReceiptId:=reader.GetInt32(0),
                            ProductId:=reader.GetInt32(1),
                            Cost:=reader.GetDecimal(2),
                            QuantityReceived:=reader.GetDecimal(3))
                End Using
            End Using

        End Function

        ''' <summary>
        ''' Sum of every COMMITTED PurchaseReturnLines.QuantityReturned for
        ''' <paramref name="receiptLineId"/>, 0 when none exist. Read inside
        ''' <paramref name="transaction"/>, AFTER the caller has already
        ''' locked the parent ReceiptLines row via
        ''' <see cref="GetReceiptLineForUpdateAsync"/> - that lock is what
        ''' guarantees no concurrent return can commit a new row for this
        ''' same line between this read and this transaction's own insert.
        ''' </summary>
        Public Shared Async Function GetPriorReturnedQuantityAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            receiptLineId As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Decimal)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "SELECT COALESCE(SUM(QuantityReturned), 0.000) FROM PurchaseReturnLines WHERE ReceiptLineId = @receiptLineId;"
                command.Parameters.AddWithValue("@receiptLineId", receiptLineId)

                Return CDec(Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False))
            End Using

        End Function

        ''' <summary>
        ''' Inserts the return header, already resolved (P4-08: no
        ''' two-actor workflow - see PurchaseReturnService's header).
        ''' </summary>
        Public Shared Async Function InsertPurchaseReturnAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            receiptId As Integer,
            requestedByUserId As Integer,
            approvedByUserId As Integer,
            returnedAtUtc As DateTime,
            approvedAtUtc As DateTime,
            referenceNumber As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Kind As PurchaseReturnWriteOutcomeKind, PurchaseReturnId As Integer))

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO PurchaseReturns " &
                    "(ReceiptId, RequestedByUserId, ApprovedByUserId, Status, ReturnedAtUtc, ApprovedAtUtc, " &
                    " ReferenceNumber, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@receiptId, @requestedByUserId, @approvedByUserId, 'Approved', @returnedAtUtc, @approvedAtUtc, " &
                    " @referenceNumber, 0, @returnedAtUtc, @returnedAtUtc);"
                command.Parameters.AddWithValue("@receiptId", receiptId)
                command.Parameters.AddWithValue("@requestedByUserId", requestedByUserId)
                command.Parameters.AddWithValue("@approvedByUserId", approvedByUserId)
                command.Parameters.AddWithValue("@returnedAtUtc", returnedAtUtc)
                command.Parameters.AddWithValue("@approvedAtUtc", approvedAtUtc)
                command.Parameters.AddWithValue("@referenceNumber", referenceNumber)

                Try
                    Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                    Return (Kind:=PurchaseReturnWriteOutcomeKind.Success, PurchaseReturnId:=CInt(command.LastInsertedId))

                Catch ex As MySqlException When ex.ErrorCode = MySqlErrorCode.DuplicateKeyEntry
                    Return (Kind:=PurchaseReturnWriteOutcomeKind.DuplicateReferenceNumber, PurchaseReturnId:=0)
                End Try

            End Using

        End Function

        ''' <summary>Inserts one return line. ProductId and Cost are the CALLER's responsibility to have already read from the locked ReceiptLines row - never client-supplied (RecordPurchaseReturnLineRequest's header).</summary>
        ''' <returns>The new line's Id.</returns>
        Public Shared Async Function InsertPurchaseReturnLineAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            purchaseReturnId As Integer,
            receiptLineId As Integer,
            productId As Integer,
            quantityReturned As Decimal,
            cost As Decimal,
            reason As String,
            removesStock As Boolean,
            createdAtUtc As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Integer)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO PurchaseReturnLines " &
                    "(PurchaseReturnId, ReceiptLineId, ProductId, QuantityReturned, Cost, Reason, RemovesStock, CreatedAtUtc) " &
                    "VALUES (@purchaseReturnId, @receiptLineId, @productId, @quantityReturned, @cost, @reason, @removesStock, @createdAtUtc);"
                command.Parameters.AddWithValue("@purchaseReturnId", purchaseReturnId)
                command.Parameters.AddWithValue("@receiptLineId", receiptLineId)
                command.Parameters.AddWithValue("@productId", productId)
                command.Parameters.AddWithValue("@quantityReturned", quantityReturned)
                command.Parameters.AddWithValue("@cost", cost)
                command.Parameters.AddWithValue("@reason", reason)
                command.Parameters.AddWithValue("@removesStock", If(removesStock, 1, 0))
                command.Parameters.AddWithValue("@createdAtUtc", createdAtUtc)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return CInt(command.LastInsertedId)
            End Using

        End Function

    End Class

End Namespace
