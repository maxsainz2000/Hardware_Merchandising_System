' Merchandising.Infrastructure.Data.IdempotencyStore
'
' P1-14 / ADR-007: insert-first idempotency against IdempotencyKeys, unique
' on (Scope, KeyValue). A command claims its key with TryClaimAsync before
' doing any work; the caller stores the committed response payload on that
' same row with CompleteAsync, in the same transaction, immediately before
' commit - so the claim and its response are always committed together, or
' neither is (a rolled-back command's claim rolls back too, which is what
' lets a retry after a failed attempt reuse the same key).
'
' A losing TryClaimAsync means another transaction already holds this key.
' MariaDB takes a lock on the unique index entry as soon as the first INSERT
' runs, before that transaction commits - so a genuinely concurrent second
' INSERT blocks on that lock rather than failing immediately, and only
' returns (with a duplicate-key error) once the first transaction resolves.
' If the first transaction committed, the second INSERT fails duplicate and
' FindCompletedResponsePayloadAsync is guaranteed to find a completed row
' (READ COMMITTED, ADR-006, makes the just-committed data visible). If the
' first transaction rolled back, the lock is released and the row no longer
' exists, so the second INSERT does not collide at all. Either way, this
' mirrors the row-locking mechanism P1-13 already proved for
' StockRepository.TryDecrementAsync, not a fresh assumption.

Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Data

    Public NotInheritable Class IdempotencyStore

        ''' <summary>
        ''' Attempts to claim <paramref name="keyValue"/> within
        ''' <paramref name="scope"/>, inside <paramref name="transaction"/>.
        ''' Returns Claimed = False, with no row changed, when the key is
        ''' already claimed by a committed transaction (or blocks until a
        ''' concurrently-claiming transaction resolves, then reports the
        ''' outcome of that resolution).
        ''' </summary>
        Public Shared Async Function TryClaimAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            scope As String,
            keyValue As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Claimed As Boolean, Id As Integer))

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO IdempotencyKeys (Scope, KeyValue, CreatedAtUtc) " &
                    "VALUES (@scope, @keyValue, UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@scope", scope)
                command.Parameters.AddWithValue("@keyValue", keyValue)

                Try
                    Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                    Return (Claimed:=True, Id:=CInt(command.LastInsertedId))

                Catch ex As MySqlException When ex.ErrorCode = MySqlErrorCode.DuplicateKeyEntry
                    Return (Claimed:=False, Id:=0)
                End Try

            End Using

        End Function

        ''' <summary>
        ''' Stores the committed response payload on an already-claimed row,
        ''' inside the same transaction as the rest of the command - see the
        ''' class header for why this must happen before commit, not after.
        ''' </summary>
        Public Shared Async Function CompleteAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            id As Integer,
            responsePayload As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "UPDATE IdempotencyKeys SET ResponsePayload = @payload, CompletedAtUtc = UTC_TIMESTAMP(6) WHERE Id = @id;"
                command.Parameters.AddWithValue("@payload", responsePayload)
                command.Parameters.AddWithValue("@id", id)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
            End Using

        End Function

        ''' <summary>
        ''' Reads back a completed claim's stored response payload, for the
        ''' caller to replay verbatim. Returns Nothing if no completed row
        ''' exists for this (scope, keyValue) - which the class header
        ''' argues should be unreachable whenever this is called immediately
        ''' after a losing TryClaimAsync.
        ''' </summary>
        Public Shared Async Function FindCompletedResponsePayloadAsync(
            connection As MySqlConnection,
            scope As String,
            keyValue As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of String)

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT ResponsePayload FROM IdempotencyKeys " &
                    " WHERE Scope = @scope AND KeyValue = @keyValue AND CompletedAtUtc IS NOT NULL;"
                command.Parameters.AddWithValue("@scope", scope)
                command.Parameters.AddWithValue("@keyValue", keyValue)

                Dim result As Object = Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False)
                Return If(result Is Nothing OrElse result Is DBNull.Value, Nothing, CStr(result))
            End Using

        End Function

    End Class

End Namespace
