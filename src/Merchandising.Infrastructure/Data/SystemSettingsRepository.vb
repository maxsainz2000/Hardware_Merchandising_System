' Merchandising.Infrastructure.Data.SystemSettingsRepository
'
' First reader of SystemSettings. The table has existed since
' 0001_foundation.sql but nothing consulted it until P1-17 needed spec
' section 15's "Retention - configurable count, with the configured value
' recorded in SystemSettings".
'
' Returns raw key/value pairs rather than a typed settings object, because
' the typed object for backup settings lives in Merchandising.Maintenance
' and Infrastructure must never depend on Maintenance (CLAUDE.md section 4).
' Later phases will bind other prefixes to their own types the same way.

Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Data

    Public NotInheritable Class SystemSettingsRepository

        ''' <summary>
        ''' Reads every setting whose key begins with
        ''' <paramref name="keyPrefix"/>, for example "backup.".
        ''' </summary>
        ''' <remarks>
        ''' Comparison is done by the database with a LIKE on an indexed
        ''' primary key, not in memory, so this does not read the whole
        ''' table to filter it. The prefix is parameterised - a
        ''' concatenated LIKE pattern would be string-built SQL, which is a
        ''' defect here regardless of the input source (CLAUDE.md section 10).
        ''' </remarks>
        ''' <returns>
        ''' Keys mapped to values. Ordinal comparison: settings keys are
        ''' identifiers, not display text, and Option Compare Binary is set
        ''' globally (CLAUDE.md section 3).
        ''' </returns>
        Public Shared Async Function LoadByPrefixAsync(
            connection As MySqlConnection,
            keyPrefix As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Dictionary(Of String, String))

            Dim settings As New Dictionary(Of String, String)(StringComparer.Ordinal)

            Using command As MySqlCommand = connection.CreateCommand()

                command.CommandText =
                    "SELECT SettingKey, SettingValue FROM SystemSettings WHERE SettingKey LIKE @prefix;"
                command.Parameters.AddWithValue("@prefix", keyPrefix & "%")

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        settings(reader.GetString(0)) = reader.GetString(1)
                    End While
                End Using

            End Using

            Return settings

        End Function

        ''' <summary>
        ''' Reads the server version string, for BackupLogs.SourceDbVersion
        ''' (spec section 15, "Integrity"). Recorded because a dump is only
        ''' restorable onto a compatible server, and "which version produced
        ''' this file" is exactly the question asked during a recovery when
        ''' nobody can remember.
        ''' </summary>
        Public Shared Async Function ReadServerVersionAsync(
            connection As MySqlConnection,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of String)

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT VERSION();"
                Dim value As Object = Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False)
                Return If(value Is Nothing OrElse value Is DBNull.Value, Nothing, value.ToString())
            End Using

        End Function

        ''' <summary>
        ''' P2-05: reads one setting's current value inside the caller's
        ''' transaction, locking the row (or the gap, if absent) with
        ''' <c>FOR UPDATE</c> so a concurrent write cannot change the value
        ''' between this read and the caller's own upsert - the "previous
        ''' value" an audit row records must be the value this write
        ''' actually replaced, not a stale read from before a race.
        ''' </summary>
        ''' <returns>Nothing if the key has never been written.</returns>
        Public Shared Async Function ReadForUpdateAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            settingKey As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of String)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText = "SELECT SettingValue FROM SystemSettings WHERE SettingKey = @settingKey FOR UPDATE;"
                command.Parameters.AddWithValue("@settingKey", settingKey)

                Dim value As Object = Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False)
                Return If(value Is Nothing OrElse value Is DBNull.Value, Nothing, CStr(value))
            End Using

        End Function

        ''' <summary>
        ''' P4-10: a plain, non-locking read of one setting's current value,
        ''' inside the caller's transaction - unlike <see cref="ReadForUpdateAsync"/>,
        ''' this takes no <c>FOR UPDATE</c> lock. AdjustmentService reads the
        ''' approval threshold this way: deciding whether ONE adjustment
        ''' request exceeds it does not need to serialize against a
        ''' concurrent administrator writing a NEW threshold value the way
        ''' UpdateSetting's own read-modify-write of that same row does.
        ''' </summary>
        ''' <returns>Nothing if the key has never been written.</returns>
        Public Shared Async Function ReadAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            settingKey As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of String)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText = "SELECT SettingValue FROM SystemSettings WHERE SettingKey = @settingKey;"
                command.Parameters.AddWithValue("@settingKey", settingKey)

                Dim value As Object = Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False)
                Return If(value Is Nothing OrElse value Is DBNull.Value, Nothing, CStr(value))
            End Using

        End Function

        ''' <summary>
        ''' P2-05: writes one setting's value, inserting a new row or
        ''' updating the existing one in a single statement - never
        ''' read-then-write, the same discipline CLAUDE.md section 5
        ''' requires of every conditional stock update, applied here to
        ''' avoid a lost-update race between two concurrent administrators.
        ''' Participates in the caller's transaction so the value change and
        ''' the audit row it is paired with commit or roll back together.
        ''' </summary>
        Public Shared Async Function UpsertAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            settingKey As String,
            settingValue As String,
            updatedByUserId As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO SystemSettings (SettingKey, SettingValue, UpdatedAtUtc, UpdatedByUserId) " &
                    "VALUES (@settingKey, @settingValue, UTC_TIMESTAMP(6), @updatedByUserId) " &
                    "ON DUPLICATE KEY UPDATE " &
                    "SettingValue = VALUES(SettingValue), UpdatedAtUtc = VALUES(UpdatedAtUtc), UpdatedByUserId = VALUES(UpdatedByUserId);"
                command.Parameters.AddWithValue("@settingKey", settingKey)
                command.Parameters.AddWithValue("@settingValue", settingValue)
                command.Parameters.AddWithValue("@updatedByUserId", updatedByUserId)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
            End Using

        End Function

    End Class

End Namespace
