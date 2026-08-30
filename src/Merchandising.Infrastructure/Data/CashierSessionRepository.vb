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
'
' P5-05: MarkClosedAsync now also sets DeclaredCash/CalculatedCash/
' CashVariance, computed once by CashierSessionService.CloseAsync and stored
' here - never recomputed by a later read, the same "computed once, inside
' the transaction, and stored" rule StockCountService.RecordLineAsync already
' applies to a count line's own Variance.
'
' GetPaymentTotalsAsync IS A PLAIN, NON-LOCKING READ - it has nothing to
' lock. It always returns one row per Merchandising.Domain.Sales.PaymentMethod
' name, COALESCEd to 0.0000 for a method with no committed SalePayments
' rows against this session (card Done-when box 4: "a session with no sales
' closes cleanly with zero totals"). Safe to call for a session that is
' still Open - no sale can ever attach to a session that later Closes and
' then un-Closes, so recomputing this on every read (CashierSessionService's
' ToResponse does, for a Closed session) can never disagree with what was
' true at close time. The persisted CalculatedCash column above is the one
' exception that is NOT safe to recompute this way - see CloseAsync's
' header for why it is captured once instead.

Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Merchandising.Domain.Sales
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
        ''' P5-07: locks and finds <paramref name="userId"/>'s own OPEN
        ''' session, if any, with <c>SELECT ... FOR UPDATE</c> - the same
        ''' "lock, then decide" discipline <see cref="GetForUpdateAsync"/>
        ''' already uses, applied here so a sale cannot commit against a
        ''' session that closes concurrently underneath it (and, symmetrically,
        ''' a concurrent close blocks until this sale's transaction resolves).
        ''' At most one row can ever match - UQ_CashierSessions_OpenSessionOwner
        ''' (0011_pos.sql, ADR-018's generated-column trick) guarantees a user
        ''' holds at most one Open session at a time. Found = False when this
        ''' user holds no Open session at all.
        ''' </summary>
        Public Shared Async Function GetOpenForUpdateByUserAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            userId As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Found As Boolean, Id As Integer))

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "SELECT Id FROM CashierSessions WHERE OpenedByUserId = @userId AND Status = 'Open' FOR UPDATE;"
                command.Parameters.AddWithValue("@userId", userId)

                Dim result As Object = Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False)
                If result Is Nothing Then
                    Return (Found:=False, Id:=0)
                End If
                Return (Found:=True, Id:=CInt(result))
            End Using

        End Function

        ''' <summary>
        ''' Moves a locked (Open) session to Closed, storing the
        ''' already-computed declared cash, calculated cash and variance.
        ''' Always called after <see cref="GetForUpdateAsync"/> has already
        ''' locked and confirmed the row within the same transaction, so the
        ''' affected-row count here is a defensive check, not the mechanism
        ''' that prevents a lost update - the row lock is (same arrangement
        ''' as AdjustmentRepository.MarkAppliedAsync).
        ''' </summary>
        Public Shared Async Function MarkClosedAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            id As Integer,
            closedByUserId As Integer,
            declaredCash As Decimal,
            calculatedCash As Decimal,
            cashVariance As Decimal,
            closedAtUtc As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Boolean)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "UPDATE CashierSessions " &
                    "   SET Status = 'Closed', " &
                    "       ClosedByUserId = @closedByUserId, " &
                    "       DeclaredCash = @declaredCash, " &
                    "       CalculatedCash = @calculatedCash, " &
                    "       CashVariance = @cashVariance, " &
                    "       ClosedAtUtc = @closedAtUtc, " &
                    "       RowVersion = RowVersion + 1, " &
                    "       UpdatedAtUtc = @closedAtUtc " &
                    " WHERE Id = @id;"
                command.Parameters.AddWithValue("@closedByUserId", closedByUserId)
                command.Parameters.AddWithValue("@declaredCash", declaredCash)
                command.Parameters.AddWithValue("@calculatedCash", calculatedCash)
                command.Parameters.AddWithValue("@cashVariance", cashVariance)
                command.Parameters.AddWithValue("@closedAtUtc", closedAtUtc)
                command.Parameters.AddWithValue("@id", id)

                Dim affectedRows As Integer = Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return affectedRows = 1
            End Using

        End Function

        ''' <summary>
        ''' Sums committed SalePayments.Amount by Method for every sale
        ''' attached to <paramref name="sessionId"/>, one row per
        ''' <see cref="PaymentMethod"/> name, COALESCEd to 0.0000 for a
        ''' method with no rows - see this class's header for why this is
        ''' safe to call outside a transaction, and why it is a DIFFERENT
        ''' guarantee than the persisted CalculatedCash column.
        ''' </summary>
        Public Shared Async Function GetPaymentTotalsAsync(
            connection As MySqlConnection,
            sessionId As Integer,
            Optional cancellationToken As CancellationToken = Nothing,
            Optional transaction As MySqlTransaction = Nothing) As Task(Of IReadOnlyList(Of (Method As String, Amount As Decimal)))

            Dim totals As New Dictionary(Of String, Decimal)(StringComparer.Ordinal)
            For Each methodName As String In [Enum].GetNames(GetType(PaymentMethod))
                totals(methodName) = 0D
            Next

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "SELECT sp.Method, SUM(sp.Amount) " &
                    "  FROM SalePayments sp " &
                    "  JOIN Sales s ON s.Id = sp.SaleId " &
                    " WHERE s.CashierSessionId = @sessionId " &
                    " GROUP BY sp.Method;"
                command.Parameters.AddWithValue("@sessionId", sessionId)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        totals(reader.GetString(0)) = reader.GetDecimal(1)
                    End While
                End Using
            End Using

            Return [Enum].GetNames(GetType(PaymentMethod)).
                Select(Function(methodName) (Method:=methodName, Amount:=totals(methodName))).
                ToList()

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
