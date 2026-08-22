' Merchandising.Infrastructure.Data.MaintenanceLockRepository
'
' P1-18. Reads and writes MaintenanceLocks (spec section 15 steps 1, 2, 7).
'
' AT MOST ONE ACTIVE LOCK IS ENFORCED BY THE DATABASE, NOT HERE. There is
' deliberately no "is a lock already held?" check before the insert in
' AcquireAsync: that would be a read-then-write race, and two Super Admins
' acting at the same moment would both read "no lock" and both insert. The
' unique index over the generated IsActive column refuses the second one with
' ERROR 1062 (0004_maintenance.sql), which is the same reasoning ADR-006
' applies to stock balances - never read-then-write, let the database refuse
' it and handle the refusal.
'
' MySqlConnector GOTCHA, found by a failing test rather than by reading docs.
' MySqlConnector maps CHAR(36) to System.Guid, not to String, so
' reader.GetString() on CorrelationId throws
'     System.InvalidCastException: Unable to cast object of type
'     'System.Guid' to type 'System.String'
' and - because the exception surfaces inside middleware - it presents as a
' generic HTTP 500 with a correctly sanitised error envelope, giving no hint
' as to the cause. GetGuid() is the correct typed accessor. Every CHAR(36)
' column in this schema is affected: StockMovements.CorrelationId,
' IdempotencyKeys.KeyValue, BackupLogs.CorrelationId and this one. Nothing
' had READ one back before P1-18 - they were all write-only until now, which
' is why this surfaced here first.

Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Data

    ''' <summary>Reads and writes the maintenance lock.</summary>
    Public NotInheritable Class MaintenanceLockRepository

        ''' <summary>MariaDB's duplicate-key error. A second active lock surfaces as this.</summary>
        Public Const DuplicateKeyErrorNumber As Integer = 1062

        Private ReadOnly _connectionFactory As ConnectionFactory

        Public Sub New(connectionFactory As ConnectionFactory)
            If connectionFactory Is Nothing Then
                Throw New ArgumentNullException(NameOf(connectionFactory))
            End If
            _connectionFactory = connectionFactory
        End Sub

        ''' <summary>
        ''' The active lock, or Nothing when the system is open for business.
        ''' </summary>
        Public Async Function GetActiveAsync(
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of MaintenanceLock)

            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)
                Return Await GetActiveAsync(connection, cancellationToken).ConfigureAwait(False)
            End Using

        End Function

        ''' <summary>Overload for callers that already hold a connection.</summary>
        Public Shared Async Function GetActiveAsync(
            connection As MySqlConnection,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of MaintenanceLock)

            Using command As MySqlCommand = connection.CreateCommand()

                command.CommandText =
                    "SELECT Id, Reason, RequestedByUserId, AcquiredAtUtc, CorrelationId " &
                    "FROM MaintenanceLocks WHERE ReleasedAtUtc IS NULL LIMIT 1;"

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)

                    If Not Await reader.ReadAsync(cancellationToken).ConfigureAwait(False) Then
                        Return Nothing
                    End If

                    Return New MaintenanceLock With {
                        .Id = reader.GetInt32(0),
                        .Reason = reader.GetString(1),
                        .RequestedByUserId = reader.GetInt32(2),
                        .AcquiredAtUtc = reader.GetDateTime(3),
                        .CorrelationId = reader.GetGuid(4).ToString("d")
                    }

                End Using

            End Using

        End Function

        ''' <summary>
        ''' Takes the lock. Returns the new lock, or Nothing when one is
        ''' already held - the caller turns that into a 409.
        ''' </summary>
        Public Async Function TryAcquireAsync(
            reason As String,
            requestedByUserId As Integer,
            correlationId As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of MaintenanceLock)

            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Try
                    Using command As MySqlCommand = connection.CreateCommand()
                        command.CommandText =
                            "INSERT INTO MaintenanceLocks (Reason, RequestedByUserId, AcquiredAtUtc, CorrelationId) " &
                            "VALUES (@reason, @requestedByUserId, UTC_TIMESTAMP(6), @correlationId);"
                        command.Parameters.AddWithValue("@reason", reason)
                        command.Parameters.AddWithValue("@requestedByUserId", requestedByUserId)
                        command.Parameters.AddWithValue("@correlationId", correlationId)
                        Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                    End Using

                Catch ex As MySqlException When ex.Number = DuplicateKeyErrorNumber
                    ' The database refused a second active lock. This is the
                    ' expected contention outcome, not an error condition -
                    ' caught narrowly so any other MySqlException still escapes.
                    Return Nothing
                End Try

                Return Await GetActiveAsync(connection, cancellationToken).ConfigureAwait(False)

            End Using

        End Function

        ''' <summary>
        ''' Releases the active lock - but only when verification passed.
        ''' </summary>
        ''' <returns>
        ''' True if a lock was released. False when no lock was held.
        ''' </returns>
        ''' <remarks>
        ''' Spec section 15 step 7: "releases maintenance mode only after
        ''' verification succeeds." <paramref name="verificationPassed"/>
        ''' False is refused by the caller before reaching here; the flag is
        ''' still recorded so the row shows what was claimed.
        ''' </remarks>
        Public Async Function ReleaseAsync(
            releasedByUserId As Integer,
            verificationPassed As Boolean,
            detail As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Boolean)

            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)
                Using command As MySqlCommand = connection.CreateCommand()

                    ' Conditional on ReleasedAtUtc IS NULL, so a double release
                    ' affects zero rows rather than re-stamping a closed lock
                    ' with a later timestamp.
                    command.CommandText =
                        "UPDATE MaintenanceLocks " &
                        "SET ReleasedAtUtc = UTC_TIMESTAMP(6), " &
                        "    ReleasedByUserId = @releasedByUserId, " &
                        "    VerificationPassed = @verificationPassed, " &
                        "    Detail = @detail " &
                        "WHERE ReleasedAtUtc IS NULL;"

                    command.Parameters.AddWithValue("@releasedByUserId", releasedByUserId)
                    command.Parameters.AddWithValue("@verificationPassed", If(verificationPassed, 1, 0))
                    command.Parameters.AddWithValue("@detail", If(CObj(detail), DBNull.Value))

                    Dim affected As Integer = Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                    Return affected > 0

                End Using
            End Using

        End Function

    End Class

End Namespace
