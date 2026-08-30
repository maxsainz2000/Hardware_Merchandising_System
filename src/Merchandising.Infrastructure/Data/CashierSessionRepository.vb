' Merchandising.Infrastructure.Data.CashierSessionRepository
'
' P5-04: writes and locked reads against CashierSessions
' (db/migrations/0011_pos.sql).
'
' InsertOpenAsync IS NOT WRAPPED IN A TRY. The unique index on
' OpenSessionOwner (0011_pos.sql, ADR-018's generated-column trick) is the
' mechanism that makes "one open session per cashier" real - CashierSessionService
' catches the resulting MySqlException (ErrorCode = DuplicateKeyEntry) itself
' and turns it into a controlled AlreadyOpen outcome, the same division
' IdempotencyStore.TryClaimAsync already uses for its own unique key: the
' repository stays a thin SQL wrapper, and the caller decides what a
' constraint violation MEANS for its own command.
'
' THE ROW IS LOCKED BEFORE A TRANSITION IS DECIDED - GetForUpdateAsync's
' SELECT ... FOR UPDATE is what makes CloseAsync safe against a concurrent
' second close racing the same row, the identical "lock, then decide, then
' write" shape AdjustmentRepository.GetForUpdateAsync uses.
'
' EVERY WRITE HERE TAKES A TRANSACTION, WITH NO OPTIONAL OVERLOAD -
' CashierSessionService owns it (ADR-006), the same arrangement every other
' repository in this solution uses.

Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Data

    Public NotInheritable Class CashierSessionRepository

        Private Const SessionColumns As String =
            "Id, OpenedByUserId, ClosedByUserId, OpeningFloat, DeclaredCash, CalculatedCash, CashVariance, " &
            "Status, OpenedAtUtc, ClosedAtUtc, RowVersion, CreatedAtUtc, UpdatedAtUtc"

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Inserts a new Open session for <paramref name="openedByUserId"/>.
        ''' Throws a duplicate-key MySqlException, uncaught, if that cashier
        ''' already holds an Open session - see this class's header.
        ''' </summary>
        ''' <returns>The new session's Id.</returns>
        Public Shared Async Function InsertOpenAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            openedByUserId As Integer,
            openingFloat As Decimal,
            openedAtUtc As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Integer)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO CashierSessions " &
                    "(OpenedByUserId, OpeningFloat, Status, OpenedAtUtc, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@openedByUserId, @openingFloat, 'Open', @openedAtUtc, 0, @openedAtUtc, @openedAtUtc);"
                command.Parameters.AddWithValue("@openedByUserId", openedByUserId)
                command.Parameters.AddWithValue("@openingFloat", openingFloat)
                command.Parameters.AddWithValue("@openedAtUtc", openedAtUtc)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return CInt(command.LastInsertedId)
            End Using

        End Function

        ''' <summary>
        ''' Locks one CashierSessions row with <c>SELECT ... FOR UPDATE</c>,
        ''' inside <paramref name="transaction"/>. Found = False when no such
        ''' session exists.
        ''' </summary>
        Public Shared Async Function GetForUpdateAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            id As Integer,
            Optional cancellationToken As CancellationToken = Nothing) _
            As Task(Of (Found As Boolean, OpenedByUserId As Integer, Status As String))

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "SELECT OpenedByUserId, Status FROM CashierSessions WHERE Id = @id FOR UPDATE;"
                command.Parameters.AddWithValue("@id", id)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    If Not Await reader.ReadAsync(cancellationToken).ConfigureAwait(False) Then
                        Return (Found:=False, OpenedByUserId:=0, Status:=String.Empty)
                    End If

                    Return (Found:=True, OpenedByUserId:=reader.GetInt32(0), Status:=reader.GetString(1))
                End Using
            End Using

        End Function

        ''' <summary>
        ''' Moves a locked (Open) session to Closed. Always called after
        ''' <see cref="GetForUpdateAsync"/> has already locked and confirmed
        ''' the row within the same transaction, so the affected-row count
        ''' here is a defensive check, not the mechanism that prevents a lost
        ''' update - the row lock is (same arrangement as
        ''' AdjustmentRepository.MarkAppliedAsync). DeclaredCash/CalculatedCash/
        ''' CashVariance are P5-05's columns to set, in the same UPDATE it
        ''' extends this one into - untouched here.
        ''' </summary>
        Public Shared Async Function MarkClosedAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            id As Integer,
            closedByUserId As Integer,
            closedAtUtc As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Boolean)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "UPDATE CashierSessions " &
                    "   SET Status = 'Closed', " &
                    "       ClosedByUserId = @closedByUserId, " &
                    "       ClosedAtUtc = @closedAtUtc, " &
                    "       RowVersion = RowVersion + 1, " &
                    "       UpdatedAtUtc = @closedAtUtc " &
                    " WHERE Id = @id;"
                command.Parameters.AddWithValue("@closedByUserId", closedByUserId)
                command.Parameters.AddWithValue("@closedAtUtc", closedAtUtc)
                command.Parameters.AddWithValue("@id", id)

                Dim affectedRows As Integer = Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return affectedRows = 1
            End Using

        End Function

        ''' <summary>A plain, non-locking read of one session - for building a response after a write already committed, or for GET.</summary>
        Public Shared Async Function GetByIdAsync(
            connection As MySqlConnection,
            id As Integer,
            Optional cancellationToken As CancellationToken = Nothing,
            Optional transaction As MySqlTransaction = Nothing) _
            As Task(Of (Found As Boolean, Id As Integer, OpenedByUserId As Integer, ClosedByUserId As Integer?,
                       OpeningFloat As Decimal, DeclaredCash As Decimal?, CalculatedCash As Decimal?, CashVariance As Decimal?,
                       Status As String, OpenedAtUtc As DateTime, ClosedAtUtc As DateTime?,
                       RowVersion As Long, CreatedAtUtc As DateTime, UpdatedAtUtc As DateTime))

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText = "SELECT " & SessionColumns & " FROM CashierSessions WHERE Id = @id;"
                command.Parameters.AddWithValue("@id", id)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    If Not Await reader.ReadAsync(cancellationToken).ConfigureAwait(False) Then
                        Return (Found:=False, Id:=0, OpenedByUserId:=0, ClosedByUserId:=Nothing,
                                OpeningFloat:=0D, DeclaredCash:=Nothing, CalculatedCash:=Nothing, CashVariance:=Nothing,
                                Status:=String.Empty, OpenedAtUtc:=DateTime.MinValue, ClosedAtUtc:=Nothing,
                                RowVersion:=0L, CreatedAtUtc:=DateTime.MinValue, UpdatedAtUtc:=DateTime.MinValue)
                    End If

                    Return ReadSession(reader)
                End Using
            End Using

        End Function

        Private Shared Function ReadSession(reader As MySqlDataReader) _
            As (Found As Boolean, Id As Integer, OpenedByUserId As Integer, ClosedByUserId As Integer?,
               OpeningFloat As Decimal, DeclaredCash As Decimal?, CalculatedCash As Decimal?, CashVariance As Decimal?,
               Status As String, OpenedAtUtc As DateTime, ClosedAtUtc As DateTime?,
               RowVersion As Long, CreatedAtUtc As DateTime, UpdatedAtUtc As DateTime)

            Dim closedByOrdinal As Integer = reader.GetOrdinal("ClosedByUserId")
            Dim declaredCashOrdinal As Integer = reader.GetOrdinal("DeclaredCash")
            Dim calculatedCashOrdinal As Integer = reader.GetOrdinal("CalculatedCash")
            Dim cashVarianceOrdinal As Integer = reader.GetOrdinal("CashVariance")
            Dim closedAtOrdinal As Integer = reader.GetOrdinal("ClosedAtUtc")

            Return (Found:=True,
                    Id:=reader.GetInt32(reader.GetOrdinal("Id")),
                    OpenedByUserId:=reader.GetInt32(reader.GetOrdinal("OpenedByUserId")),
                    ClosedByUserId:=If(reader.IsDBNull(closedByOrdinal), CType(Nothing, Integer?), reader.GetInt32(closedByOrdinal)),
                    OpeningFloat:=reader.GetDecimal(reader.GetOrdinal("OpeningFloat")),
                    DeclaredCash:=If(reader.IsDBNull(declaredCashOrdinal), CType(Nothing, Decimal?), reader.GetDecimal(declaredCashOrdinal)),
                    CalculatedCash:=If(reader.IsDBNull(calculatedCashOrdinal), CType(Nothing, Decimal?), reader.GetDecimal(calculatedCashOrdinal)),
                    CashVariance:=If(reader.IsDBNull(cashVarianceOrdinal), CType(Nothing, Decimal?), reader.GetDecimal(cashVarianceOrdinal)),
                    Status:=reader.GetString(reader.GetOrdinal("Status")),
                    OpenedAtUtc:=reader.GetDateTime(reader.GetOrdinal("OpenedAtUtc")),
                    ClosedAtUtc:=If(reader.IsDBNull(closedAtOrdinal), CType(Nothing, DateTime?), reader.GetDateTime(closedAtOrdinal)),
                    RowVersion:=reader.GetInt64(reader.GetOrdinal("RowVersion")),
                    CreatedAtUtc:=reader.GetDateTime(reader.GetOrdinal("CreatedAtUtc")),
                    UpdatedAtUtc:=reader.GetDateTime(reader.GetOrdinal("UpdatedAtUtc")))

        End Function

    End Class

End Namespace
