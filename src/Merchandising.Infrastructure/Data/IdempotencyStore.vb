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
'
' P5-09 / ADR-007.1: requestHash is OPTIONAL and additive. A caller that
' opts in (currently only SaleService.CompleteAsync) passes a hash of its
' own meaningful request fields; TryClaimAsync stores it on the claiming
' INSERT and, on a LOSING claim, reads the winner's stored hash back as
' ExistingRequestHash so the caller can tell "the same retry" from "a
' different command reusing this key" BEFORE deciding to replay. The
' winner's row is guaranteed already committed by the time a losing claim
' observes it (this header's own paragraph above), so the read is never
' racing an uncommitted hash. A caller that does not pass one writes NULL
' and gets NULL back - every other scope's behavior is untouched.

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
        ''' outcome of that resolution) - ExistingRequestHash is populated
        ''' only in that case, from the row that won the claim.
        ''' <paramref name="requestHash"/> is optional (see this class's
        ''' header) - Nothing writes a NULL and skips the read-back. Placed
        ''' AFTER <paramref name="cancellationToken"/>, not before it -
        ''' every existing caller passes cancellationToken positionally as
        ''' the 5th argument, and inserting a new parameter ahead of it
        ''' would have silently reordered every one of those call sites.
        ''' </summary>
        Public Shared Async Function TryClaimAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            scope As String,
            keyValue As String,
            Optional cancellationToken As CancellationToken = Nothing,
            Optional requestHash As String = Nothing) As Task(Of (Claimed As Boolean, Id As Integer, ExistingRequestHash As String))

            Dim claimed As Boolean = False
            Dim claimedId As Integer = 0
            Dim duplicateKey As Boolean = False

            ' VB cannot Await inside a Catch (BC36943, CLAUDE.md section 3) -
            ' the duplicate-key branch only sets a flag here; the read-back
            ' that needs Await happens below, once this Using/Try has exited.
            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO IdempotencyKeys (Scope, KeyValue, RequestHash, CreatedAtUtc) " &
                    "VALUES (@scope, @keyValue, @requestHash, UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@scope", scope)
                command.Parameters.AddWithValue("@keyValue", keyValue)
                command.Parameters.AddWithValue("@requestHash", CType(If(requestHash, CType(DBNull.Value, Object)), Object))

                Try
                    Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                    claimed = True
                    claimedId = CInt(command.LastInsertedId)

                Catch ex As MySqlException When ex.ErrorCode = MySqlErrorCode.DuplicateKeyEntry
                    duplicateKey = True
                End Try

            End Using

            If claimed Then
                Return (Claimed:=True, Id:=claimedId, ExistingRequestHash:=CType(Nothing, String))
            End If

            If Not duplicateKey Then
                Throw New InvalidOperationException(
                    $"TryClaimAsync for scope '{scope}', key '{keyValue}' neither claimed nor hit a duplicate-key error. This should be unreachable.")
            End If

            Dim existingHash As String = Nothing

            Using hashCommand As MySqlCommand = connection.CreateCommand()
                hashCommand.Transaction = transaction
                hashCommand.CommandText =
                    "SELECT RequestHash FROM IdempotencyKeys WHERE Scope = @scope AND KeyValue = @keyValue;"
                hashCommand.Parameters.AddWithValue("@scope", scope)
                hashCommand.Parameters.AddWithValue("@keyValue", keyValue)

                Dim result As Object = Await hashCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False)
                existingHash = If(result Is Nothing OrElse result Is DBNull.Value, Nothing, CStr(result))
            End Using

            Return (Claimed:=False, Id:=0, ExistingRequestHash:=existingHash)

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
