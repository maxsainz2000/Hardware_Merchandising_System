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

    End Class

End Namespace
