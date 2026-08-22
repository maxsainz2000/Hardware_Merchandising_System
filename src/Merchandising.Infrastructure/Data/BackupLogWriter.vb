' Merchandising.Infrastructure.Data.BackupLogWriter
'
' Single insertion point for BackupLogs (P1-17, spec section 15 "Integrity").
' Append-only and enforced by grant: db/grants/0004_backup-grants.sql gives
' merch_backup INSERT on this one table and nothing else - no UPDATE, no
' DELETE. A failed run therefore cannot later be edited into a successful
' one; it gets a second row, the same way a stock correction is a
' compensating movement rather than an edit (CLAUDE.md section 5).
'
' Mirrors AuditLogWriter deliberately, including the Optional transaction
' parameter position, so the two read the same way side by side.

Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Data

    Public NotInheritable Class BackupLogWriter

        ''' <summary>
        ''' Writes one row describing a backup ATTEMPT - not a backup
        ''' success. A row is written whether the run worked, half-worked,
        ''' or failed outright.
        ''' </summary>
        ''' <param name="result">
        ''' One of "Succeeded", "Partial", "Failed". Matches
        ''' Merchandising.Maintenance.Backup.BackupOutcome by name. Passed as
        ''' a string rather than typed, because Infrastructure must not
        ''' depend on Maintenance (CLAUDE.md section 4).
        ''' </param>
        ''' <param name="detail">
        ''' Error or warning text. Must never contain a credential; the
        ''' caller is responsible for that, for the same reason spelled out
        ''' on <see cref="AuditLogWriter"/>.
        ''' </param>
        Public Shared Async Function WriteAsync(
            connection As MySqlConnection,
            startedAtUtc As Date,
            completedAtUtc As Date?,
            result As String,
            filePath As String,
            sizeBytes As Long?,
            sha256 As String,
            offHostPath As String,
            sourceDbVersion As String,
            retentionCount As Integer?,
            prunedFileCount As Integer,
            correlationId As String,
            Optional detail As String = Nothing,
            Optional cancellationToken As CancellationToken = Nothing,
            Optional transaction As MySqlTransaction = Nothing) As Task

            Using command As MySqlCommand = connection.CreateCommand()

                If transaction IsNot Nothing Then
                    command.Transaction = transaction
                End If

                command.CommandText =
                    "INSERT INTO BackupLogs " &
                    "(StartedAtUtc, CompletedAtUtc, Result, FilePath, SizeBytes, Sha256, " &
                    " OffHostPath, SourceDbVersion, RetentionCount, PrunedFileCount, CorrelationId, Detail) " &
                    "VALUES " &
                    "(@startedAtUtc, @completedAtUtc, @result, @filePath, @sizeBytes, @sha256, " &
                    " @offHostPath, @sourceDbVersion, @retentionCount, @prunedFileCount, @correlationId, @detail);"

                command.Parameters.AddWithValue("@startedAtUtc", startedAtUtc)
                command.Parameters.AddWithValue("@completedAtUtc", If(CObj(completedAtUtc), DBNull.Value))
                command.Parameters.AddWithValue("@result", result)
                command.Parameters.AddWithValue("@filePath", If(CObj(filePath), DBNull.Value))
                command.Parameters.AddWithValue("@sizeBytes", If(CObj(sizeBytes), DBNull.Value))
                command.Parameters.AddWithValue("@sha256", If(CObj(sha256), DBNull.Value))
                command.Parameters.AddWithValue("@offHostPath", If(CObj(offHostPath), DBNull.Value))
                command.Parameters.AddWithValue("@sourceDbVersion", If(CObj(sourceDbVersion), DBNull.Value))
                command.Parameters.AddWithValue("@retentionCount", If(CObj(retentionCount), DBNull.Value))
                command.Parameters.AddWithValue("@prunedFileCount", prunedFileCount)
                command.Parameters.AddWithValue("@correlationId", correlationId)
                command.Parameters.AddWithValue("@detail", If(CObj(detail), DBNull.Value))

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)

            End Using

        End Function

    End Class

End Namespace
