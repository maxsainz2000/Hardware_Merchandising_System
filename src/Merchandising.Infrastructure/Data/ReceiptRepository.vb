' Merchandising.Infrastructure.Data.ReceiptRepository
'
' P4-05: writes against Receipts and ReceiptLines
' (db/migrations/0009_receiving.sql). Both tables are INSERT-only at the
' database grant (db/grants/0011_receiving-grants.sql) - a receipt is a
' completed transactional record from the moment it exists (spec section 11:
' "Goods received" commits once), so there is no update method here to add
' later without a new grant to argue for it first.
'
' EVERY WRITE HERE TAKES A TRANSACTION, WITH NO OPTIONAL OVERLOAD - the same
' arrangement PurchaseOrderRepository uses and for the same reason: a receipt
' committed without its lines, or a line committed without its movement and
' balance effects, would be a document that lies about what happened.
' ReceivingService owns the transaction (ADR-006).
'
' NO GetByIdAsync. ReceivingService builds the response directly from the
' values it already holds - the receipt's ReceivedAtUtc/CreatedAtUtc are a
' single app-computed instant (Optional cancellationToken aside, no column
' here is server-computed in a way the caller cannot already state exactly),
' so there is nothing a re-read would recover that the caller does not
' already know with certainty. Contrast PurchaseOrderRepository.GetByIdAsync,
' which exists because OrderNumber and UTC_TIMESTAMP(6)-computed columns are
' genuinely unknown to the caller until after the INSERT.
'
' A DUPLICATE REFERENCE NUMBER IS AN OUTCOME, NOT AN EXCEPTION - the same
' ERROR 1062 translation PurchaseOrderRepository.InsertOrderAsync and
' SupplierRepository.InsertAsync already use. Unlike a duplicate order
' number (the server's own race, retried invisibly), a duplicate receipt
' reference number is the CALLER's problem: it is the receiving clerk's own
' goods-received-note number, so ReceivingService reports it back as a
' refusal rather than inventing a new one.

Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Data

    ''' <summary>Outcome of a receipt-header insert.</summary>
    Public Enum ReceiptWriteOutcomeKind
        Success
        DuplicateReferenceNumber
    End Enum

    Public NotInheritable Class ReceiptRepository

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Inserts the receipt header inside <paramref name="transaction"/>.
        ''' <paramref name="receivedAtUtc"/> is written verbatim to both
        ''' ReceivedAtUtc and CreatedAtUtc - see this file's header for why no
        ''' UTC_TIMESTAMP(6) default or later re-read is needed.
        ''' </summary>
        Public Shared Async Function InsertReceiptAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            purchaseOrderId As Integer,
            receivedByUserId As Integer,
            receivedAtUtc As DateTime,
            referenceNumber As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Kind As ReceiptWriteOutcomeKind, ReceiptId As Integer))

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO Receipts " &
                    "(PurchaseOrderId, ReceivedByUserId, ReceivedAtUtc, ReferenceNumber, CreatedAtUtc) " &
                    "VALUES (@purchaseOrderId, @receivedByUserId, @receivedAtUtc, @referenceNumber, @receivedAtUtc);"
                command.Parameters.AddWithValue("@purchaseOrderId", purchaseOrderId)
                command.Parameters.AddWithValue("@receivedByUserId", receivedByUserId)
                command.Parameters.AddWithValue("@receivedAtUtc", receivedAtUtc)
                command.Parameters.AddWithValue("@referenceNumber", referenceNumber)

                Try
                    Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                    Return (Kind:=ReceiptWriteOutcomeKind.Success, ReceiptId:=CInt(command.LastInsertedId))

                Catch ex As MySqlException When ex.ErrorCode = MySqlErrorCode.DuplicateKeyEntry
                    Return (Kind:=ReceiptWriteOutcomeKind.DuplicateReferenceNumber, ReceiptId:=0)
                End Try

            End Using

        End Function

        ''' <summary>
        ''' Inserts one receipt line inside <paramref name="transaction"/>.
        ''' <paramref name="createdAtUtc"/> is the same instant
        ''' <see cref="InsertReceiptAsync"/> wrote for this receipt - every row
        ''' this one receipt produces carries one consistent timestamp.
        ''' </summary>
        ''' <returns>The new line's Id.</returns>
        Public Shared Async Function InsertReceiptLineAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            receiptId As Integer,
            purchaseOrderLineId As Integer,
            productId As Integer,
            quantityReceived As Decimal,
            cost As Decimal,
            createdAtUtc As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Integer)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO ReceiptLines " &
                    "(ReceiptId, PurchaseOrderLineId, ProductId, QuantityReceived, Cost, CreatedAtUtc) " &
                    "VALUES (@receiptId, @purchaseOrderLineId, @productId, @quantityReceived, @cost, @createdAtUtc);"
                command.Parameters.AddWithValue("@receiptId", receiptId)
                command.Parameters.AddWithValue("@purchaseOrderLineId", purchaseOrderLineId)
                command.Parameters.AddWithValue("@productId", productId)
                command.Parameters.AddWithValue("@quantityReceived", quantityReceived)
                command.Parameters.AddWithValue("@cost", cost)
                command.Parameters.AddWithValue("@createdAtUtc", createdAtUtc)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return CInt(command.LastInsertedId)
            End Using

        End Function

    End Class

End Namespace
