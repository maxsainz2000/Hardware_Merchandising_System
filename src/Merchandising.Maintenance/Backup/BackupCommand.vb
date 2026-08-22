' Merchandising.Maintenance.Backup.BackupCommand
'
' P1-17. Implements spec section 15's backup control table: schedule (via
' Task Scheduler, registered separately), retention, location, off-host copy,
' integrity, security, and failure handling.
'
' THE RULE THIS TYPE EXISTS TO ENFORCE. A backup system that fails silently
' is worse than none, because it manufactures confidence. Every path through
' this command therefore ends in a recorded outcome: a BackupLogs row if the
' database can be reached, and a local failure log if it cannot. There is no
' path that returns without saying what happened, and no path that reports
' success without having recomputed the checksum from the bytes on disk.

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Migrations
Imports MySqlConnector

Namespace Backup

    ''' <summary>Runs one backup: dump, verify, copy off-host, prune, record.</summary>
    Public NotInheritable Class BackupCommand

        ''' <summary>
        ''' Written into the backup directory when the outcome could not be
        ''' recorded in BackupLogs - which is exactly the case where the
        ''' database is unreachable and a database-only record would be no
        ''' record at all.
        ''' </summary>
        Public Const FailureLogFileName As String = "backup-failures.log"

        Private Const SettingsPrefix As String = "backup."

        ''' <summary>
        ''' Executes one backup run. Never throws for an operational failure -
        ''' a failed backup is a <see cref="BackupResult"/> with
        ''' <see cref="BackupOutcome.Failed"/>, not an exception, because the
        ''' caller must record it either way. Programming errors (a missing
        ''' mysqldump.exe, an unwritable directory) still throw.
        ''' </summary>
        Public Shared Async Function ExecuteAsync(
            databaseOptions As DatabaseOptions,
            settings As BackupSettings,
            mysqlDumpPath As String,
            correlationId As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of BackupResult)

            If databaseOptions Is Nothing Then Throw New ArgumentNullException(NameOf(databaseOptions))
            If settings Is Nothing Then Throw New ArgumentNullException(NameOf(settings))

            Dim result As New BackupResult With {
                .StartedAtUtc = Date.UtcNow,
                .CorrelationId = correlationId,
                .RetentionCount = settings.RetentionCount,
                .Outcome = BackupOutcome.Failed
            }

            Directory.CreateDirectory(settings.Directory)

            Dim destination As String = Path.Combine(
                settings.Directory,
                $"{databaseOptions.Database}-{result.StartedAtUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.sql")

            Dim dump As MysqlDumpOutcome = Await MysqlDumpRunner.RunAsync(
                mysqlDumpPath, databaseOptions, destination, cancellationToken).ConfigureAwait(False)

            If Not IsUsableDump(dump, destination) Then

                ' mysqldump creates the result file before it authenticates,
                ' so a failed run leaves an empty or truncated file behind.
                ' Deleting it matters: a zero-byte .sql sitting in the backup
                ' directory looks like a backup to anyone glancing at the
                ' folder, and would be counted by retention as one.
                DeleteQuietly(destination)

                result.CompletedAtUtc = Date.UtcNow
                result.Outcome = BackupOutcome.Failed
                result.Detail = DescribeDumpFailure(dump)

                Await RecordAsync(databaseOptions, settings, result, cancellationToken).ConfigureAwait(False)
                Return result

            End If

            result.FilePath = destination
            result.SizeBytes = New FileInfo(destination).Length

            ' Recomputed from the file on disk, never from a buffer we still
            ' hold - a checksum of what we meant to write proves nothing
            ' about what actually landed.
            result.Sha256 = ChecksumCalculator.ComputeSha256Hex(destination)

            Dim offHostPath As String = TryCopyOffHost(destination, settings)

            If offHostPath Is Nothing Then
                result.Outcome = BackupOutcome.[Partial]
                result.OffHostPath = Nothing
                result.Detail =
                    $"Dump written and verified, but the off-host copy did not happen: no ready volume " &
                    $"labelled '{settings.OffHostVolumeLabel}' is attached. The local dump is the only copy."
            Else
                result.Outcome = BackupOutcome.Succeeded
                result.OffHostPath = offHostPath
            End If

            result.PrunedFileCount = PruneOldDumps(settings, databaseOptions.Database)
            result.CompletedAtUtc = Date.UtcNow

            Await RecordAsync(databaseOptions, settings, result, cancellationToken).ConfigureAwait(False)
            Return result

        End Function

        ''' <summary>
        ''' Builds settings from SystemSettings, falling back to the
        ''' <see cref="BackupSettings"/> defaults for any key that is absent.
        ''' </summary>
        Public Shared Async Function LoadSettingsAsync(
            connection As MySqlConnection,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of BackupSettings)

            Dim raw As Dictionary(Of String, String) =
                Await SystemSettingsRepository.LoadByPrefixAsync(connection, SettingsPrefix, cancellationToken).ConfigureAwait(False)

            Dim settings As New BackupSettings()

            Dim directoryValue As String = Nothing
            If raw.TryGetValue("backup.directory", directoryValue) AndAlso Not String.IsNullOrWhiteSpace(directoryValue) Then
                settings.Directory = directoryValue
            End If

            Dim labelValue As String = Nothing
            If raw.TryGetValue("backup.offHostVolumeLabel", labelValue) AndAlso Not String.IsNullOrWhiteSpace(labelValue) Then
                settings.OffHostVolumeLabel = labelValue
            End If

            Dim retentionValue As String = Nothing
            If raw.TryGetValue("backup.retentionCount", retentionValue) Then
                Dim parsed As Integer
                If Integer.TryParse(retentionValue, NumberStyles.Integer, CultureInfo.InvariantCulture, parsed) Then
                    settings.RetentionCount = parsed
                End If
                ' A malformed value deliberately leaves the default in place
                ' rather than throwing. Refusing to back up because someone
                ' typed "seven" into a settings row would be the wrong
                ' failure: the backup matters more than the tidiness.
            End If

            Return settings

        End Function

        ''' <summary>
        ''' Exit code 0 only for an unqualified success. A partial run exits
        ''' non-zero so Task Scheduler shows it as failed - a degraded backup
        ''' that reports success is how a system ends up with months of
        ''' dumps that only ever existed on the machine that died.
        ''' </summary>
        Private Shared Function IsUsableDump(dump As MysqlDumpOutcome, destination As String) As Boolean

            If dump.ExitCode <> 0 Then Return False
            If Not File.Exists(destination) Then Return False

            ' Exit code 0 and a zero-byte file is a real combination, not a
            ' theoretical one. Checked separately so the two cases can be
            ' told apart in the recorded detail.
            If New FileInfo(destination).Length = 0 Then Return False

            ' The completeness check that actually matters. Exit code 0 and a
            ' non-empty file together still do not prove the dump ran to the
            ' end - a disk that fills, a killed process, or a connection
            ' dropped mid-table all leave a large, plausible-looking, useless
            ' file. mysqldump writes "-- Dump completed on <timestamp>" as its
            ' final line and only gets there after every table is out, so its
            ' presence is the cheapest available proof of a whole dump.
            Return HasCompletionMarker(destination)

        End Function

        ''' <summary>
        ''' Looks for mysqldump's trailing completion marker without reading
        ''' the whole file - a dump is the largest file this system writes and
        ''' loading it into memory to check its last line would be wasteful.
        ''' </summary>
        Private Shared Function HasCompletionMarker(path As String) As Boolean

            Const markerText As String = "-- Dump completed"
            Const tailBytes As Integer = 512

            Using stream As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)

                Dim take As Integer = CInt(Math.Min(CLng(tailBytes), stream.Length))
                stream.Seek(-take, SeekOrigin.End)

                Dim buffer As Byte() = New Byte(take - 1) {}
                Dim read As Integer = stream.Read(buffer, 0, take)

                Dim tail As String = Encoding.UTF8.GetString(buffer, 0, read)
                Return tail.IndexOf(markerText, StringComparison.Ordinal) >= 0

            End Using

        End Function

        Private Shared Function DescribeDumpFailure(dump As MysqlDumpOutcome) As String

            Dim message As New StringBuilder()
            message.Append($"mysqldump failed (exit code {dump.ExitCode}).")

            If Not String.IsNullOrWhiteSpace(dump.StandardError) Then
                message.Append(" ")
                message.Append(dump.StandardError.Trim())
            ElseIf dump.ExitCode = 0 Then
                message.Append(" It reported success but produced no usable file: " &
                               "the output was missing, empty, or had no completion marker, " &
                               "which means the dump did not run to the end.")
            End If

            Return message.ToString()

        End Function

        ''' <summary>
        ''' Finds the off-host volume by LABEL and copies the dump to it.
        ''' Returns the destination path, or Nothing if no such volume is
        ''' attached - which is an ordinary nightly occurrence, not an error.
        ''' </summary>
        Private Shared Function TryCopyOffHost(dumpPath As String, settings As BackupSettings) As String

            If String.IsNullOrWhiteSpace(settings.OffHostVolumeLabel) Then Return Nothing

            Dim target As DriveInfo = Nothing

            For Each drive As DriveInfo In DriveInfo.GetDrives()
                Try
                    ' IsReady must be checked first: reading VolumeLabel on a
                    ' drive with no media (an empty card reader, a
                    ' disconnected network drive) throws rather than
                    ' returning empty.
                    If drive.IsReady AndAlso
                       String.Equals(drive.VolumeLabel, settings.OffHostVolumeLabel, StringComparison.OrdinalIgnoreCase) Then
                        target = drive
                        Exit For
                    End If
                Catch ex As IOException
                    ' A drive that cannot be interrogated is simply not the
                    ' one we are looking for. Swallowing this specific case
                    ' is correct; swallowing the copy itself would not be.
                    Continue For
                End Try
            Next

            If target Is Nothing Then Return Nothing

            Dim folder As String = Path.Combine(target.RootDirectory.FullName, settings.OffHostFolderName)
            Directory.CreateDirectory(folder)

            Dim destination As String = Path.Combine(folder, Path.GetFileName(dumpPath))
            File.Copy(dumpPath, destination, overwrite:=True)

            Return destination

        End Function

        ''' <summary>
        ''' Keeps the newest <see cref="BackupSettings.RetentionCount"/>
        ''' dumps and deletes the rest. Returns how many were deleted.
        ''' </summary>
        Private Shared Function PruneOldDumps(settings As BackupSettings, databaseName As String) As Integer

            ' A retention count of zero or less would mean "delete every
            ' backup on the machine, including the one just taken". That is
            ' never what an operator means, and a misconfigured settings row
            ' must not be able to destroy the backups (spec section 15 calls
            ' for a configurable count, not a configurable self-destruct).
            If settings.RetentionCount < 1 Then Return 0

            Dim dumps As FileInfo() = New DirectoryInfo(settings.Directory).
                GetFiles(databaseName & "-*.sql").
                OrderByDescending(Function(f) f.CreationTimeUtc).
                ToArray()

            If dumps.Length <= settings.RetentionCount Then Return 0

            Dim pruned As Integer = 0

            For Each stale As FileInfo In dumps.Skip(settings.RetentionCount)
                If DeleteQuietly(stale.FullName) Then
                    pruned += 1
                End If
            Next

            Return pruned

        End Function

        ''' <summary>
        ''' Records the outcome. Tries BackupLogs first; falls back to a
        ''' local log file when the database cannot be reached, so that the
        ''' one failure mode that also breaks database logging still leaves
        ''' a trace a human can find.
        ''' </summary>
        Private Shared Async Function RecordAsync(
            databaseOptions As DatabaseOptions,
            settings As BackupSettings,
            result As BackupResult,
            cancellationToken As CancellationToken) As Task

            Try
                Dim factory As New ConnectionFactory(databaseOptions)

                Using connection As MySqlConnection = Await factory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                    result.SourceDbVersion =
                        Await SystemSettingsRepository.ReadServerVersionAsync(connection, cancellationToken).ConfigureAwait(False)

                    Await BackupLogWriter.WriteAsync(
                        connection,
                        result.StartedAtUtc,
                        result.CompletedAtUtc,
                        result.Outcome.ToString(),
                        result.FilePath,
                        result.SizeBytes,
                        result.Sha256,
                        result.OffHostPath,
                        result.SourceDbVersion,
                        result.RetentionCount,
                        result.PrunedFileCount,
                        result.CorrelationId,
                        result.Detail,
                        cancellationToken).ConfigureAwait(False)

                    result.LoggedToDatabase = True

                End Using

            Catch ex As MySqlException
                ' Narrow on purpose: a database that refuses the connection or
                ' the INSERT is the expected failure here, and it must not be
                ' allowed to mask the backup's own result by propagating. Any
                ' other exception type is a programming error and still
                ' escapes (CLAUDE.md section 10).
                result.LoggedToDatabase = False
                AppendFailureLog(settings, result, ex)
            End Try

            If Not result.LoggedToDatabase AndAlso result.Outcome = BackupOutcome.Succeeded Then
                ' A dump that worked but could not be recorded is not a clean
                ' success: nothing downstream can prove it happened.
                result.Outcome = BackupOutcome.[Partial]
            End If

        End Function

        Private Shared Sub AppendFailureLog(settings As BackupSettings, result As BackupResult, ex As Exception)

            Try
                Dim line As New StringBuilder()
                line.Append(Date.UtcNow.ToString("u", CultureInfo.InvariantCulture))
                line.Append($" [{result.Outcome}] correlation={result.CorrelationId}")
                line.Append($" detail={result.Detail}")
                line.Append($" logWriteError={ex.Message}")

                File.AppendAllText(
                    Path.Combine(settings.Directory, FailureLogFileName),
                    line.ToString() & Environment.NewLine)

            Catch ioEx As IOException
                ' Last resort. If neither the database nor the log file can be
                ' written, stderr is all that is left - and staying silent
                ' here would defeat the entire point of the command.
                Console.Error.WriteLine($"CRITICAL: backup outcome could not be recorded anywhere: {ioEx.Message}")
            End Try

        End Sub

        Private Shared Function DeleteQuietly(path As String) As Boolean
            Try
                If File.Exists(path) Then
                    File.Delete(path)
                    Return True
                End If
            Catch ex As IOException
                Console.Error.WriteLine($"WARNING: could not delete '{path}': {ex.Message}")
            End Try
            Return False
        End Function

    End Class

End Namespace
