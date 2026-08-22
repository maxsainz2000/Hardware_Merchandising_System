' Merchandising.Maintenance.Restore.RestoreCommand
'
' P1-18. Spec section 15 steps 4-6: the operator runs this utility on the host
' with the selected backup file; it restores the database, validates the
' expected schema, and records a local maintenance log.
'
' WHY THIS IS NOT AN API ENDPOINT, AND NEVER WILL BE.
' Spec section 15: restore "is not executed as a normal request inside a live
' API process that depends on the database". The reason is structural rather
' than stylistic - a restore drops and recreates the very schema the API is
' mid-query against, and a process cannot coherently survive having its data
' source replaced underneath it. It also runs with the API service STOPPED,
' which an API endpoint by definition cannot arrange for itself. The absence
' of any such route is asserted by
' MaintenanceModeTests.Api_ExposesNoRestoreEndpoint.
'
' WHICH IDENTITY. merch_migrator, the schema owner (ADR-013) - the only
' account with DDL. merch_api has no DDL at all and could not create a table
' if it wanted to, which is the entire point of the split.
'
' VERIFICATION IS PART OF THE RESTORE, NOT A SEPARATE COURTESY.
' Spec section 15 step 6 requires confirming that the expected users,
' products, balances and recent transactions are present, and step 7 makes
' releasing maintenance mode conditional on it. A restore command that
' reported success on "mysql.exe exited 0" would be making exactly the claim
' P1-17 was careful not to make about backups.

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Merchandising.Infrastructure.Data
Imports MySqlConnector

Namespace Restore

    ''' <summary>Restores a dump and verifies what landed.</summary>
    Public NotInheritable Class RestoreCommand

        ''' <summary>Local maintenance log, written beside the backups (spec §15 step 5).</summary>
        Public Const MaintenanceLogFileName As String = "restore-maintenance.log"

        ''' <summary>
        ''' Tables that must exist after any restore of this system. Checked by
        ''' name against information_schema rather than by trusting the dump.
        ''' </summary>
        Private Shared ReadOnly ExpectedTables As String() = {
            "users", "roles", "userroles", "products", "stockbalances",
            "stockmovements", "auditlogs", "idempotencykeys", "systemsettings",
            "schemamigrations", "sessions", "backuplogs", "maintenancelocks"
        }

        ''' <summary>
        ''' Restores <paramref name="dumpPath"/> and verifies the result.
        ''' </summary>
        ''' <param name="targetDatabase">
        ''' Normally Nothing, meaning "whatever the dump says" - the dump is
        ''' taken with --databases so it carries its own CREATE DATABASE and
        ''' USE. Supply a name to redirect the restore into an ISOLATED
        ''' database instead, which is what spec §15's "Verification" control
        ''' means by "test restore against an isolated database". Redirecting
        ''' needs an account that can create that database; merch_migrator's
        ''' grant is scoped to the literal `merchandising` schema and cannot.
        ''' </param>
        Public Shared Async Function ExecuteAsync(
            mysqlClientPath As String,
            options As DatabaseOptions,
            dumpPath As String,
            Optional targetDatabase As String = Nothing,
            Optional logDirectory As String = Nothing,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of RestoreResult)

            If Not File.Exists(mysqlClientPath) Then
                Throw New FileNotFoundException(
                    $"mysql.exe not found at '{mysqlClientPath}'. P0-04 pinned this path; note that " &
                    "mariadb.exe does not exist in this XAMPP distribution.", mysqlClientPath)
            End If

            If Not File.Exists(dumpPath) Then
                Throw New FileNotFoundException($"Backup file not found at '{dumpPath}'.", dumpPath)
            End If

            Dim result As New RestoreResult With {
                .StartedAtUtc = Date.UtcNow,
                .DumpPath = dumpPath,
                .TargetDatabase = If(targetDatabase, options.Database),
                .Succeeded = False
            }

            ' The RTO clock starts here and stops after verification, not after
            ' the restore. "Restored" and "confirmed usable" are different
            ' claims, and PA-005's target is to a VERIFIED state.
            Dim clock As Stopwatch = Stopwatch.StartNew()

            Dim shellOutcome As RestoreShellOutcome =
                Await RunMysqlClientAsync(mysqlClientPath, options, dumpPath, targetDatabase, cancellationToken).ConfigureAwait(False)

            If shellOutcome.ExitCode <> 0 Then
                clock.Stop()
                result.CompletedAtUtc = Date.UtcNow
                result.ElapsedSeconds = clock.Elapsed.TotalSeconds
                result.Detail =
                    $"mysql.exe failed (exit code {shellOutcome.ExitCode}). " &
                    If(String.IsNullOrWhiteSpace(shellOutcome.StandardError),
                       "It produced no error output, which usually means it exited before reading the dump - " &
                       "check that a database is selected and the credentials are right.",
                       shellOutcome.StandardError.Trim())
                WriteMaintenanceLog(logDirectory, result)
                Return result
            End If

            Await VerifyAsync(options, result.TargetDatabase, result, cancellationToken).ConfigureAwait(False)

            clock.Stop()
            result.CompletedAtUtc = Date.UtcNow
            result.ElapsedSeconds = clock.Elapsed.TotalSeconds
            result.Succeeded = result.MissingTables.Count = 0 AndAlso result.UserCount > 0

            If Not result.Succeeded AndAlso String.IsNullOrEmpty(result.Detail) Then
                result.Detail =
                    If(result.MissingTables.Count > 0,
                       "Restore completed but the schema is incomplete. Missing: " & String.Join(", ", result.MissingTables),
                       "Restore completed but no user rows are present, so the data did not land.")
            End If

            WriteMaintenanceLog(logDirectory, result)
            Return result

        End Function

        Private Shared Async Function RunMysqlClientAsync(
            mysqlClientPath As String,
            options As DatabaseOptions,
            dumpPath As String,
            targetDatabase As String,
            cancellationToken As CancellationToken) As Task(Of RestoreShellOutcome)

            ' Same reasoning as MysqlDumpRunner: the credential never goes on
            ' the command line, where it is readable in the process list.
            Dim defaultsFile As String = Path.Combine(
                Path.GetTempPath(), "." & Guid.NewGuid().ToString("n") & ".cnf")

            Try
                Dim contents As New StringBuilder()
                contents.AppendLine("[client]")
                contents.AppendLine($"user={options.UserId}")
                contents.AppendLine($"password={options.Password}")
                contents.AppendLine($"host={options.Host}")
                contents.AppendLine($"port={options.Port}")
                File.WriteAllText(defaultsFile, contents.ToString(), New UTF8Encoding(encoderShouldEmitUTF8Identifier:=False))

                Dim startInfo As New ProcessStartInfo With {
                    .FileName = mysqlClientPath,
                    .UseShellExecute = False,
                    .RedirectStandardInput = True,
                    .RedirectStandardError = True,
                    .RedirectStandardOutput = True,
                    .CreateNoWindow = True
                }

                startInfo.ArgumentList.Add("--defaults-extra-file=" & defaultsFile)

                If Not String.IsNullOrWhiteSpace(targetDatabase) Then
                    ' Redirecting into an isolated database. The dump's own
                    ' CREATE DATABASE/USE lines would override this, so the
                    ' caller is responsible for having taken a dump without
                    ' --databases, or for accepting that the dump wins.
                    startInfo.ArgumentList.Add("--database=" & targetDatabase)
                End If

                Using process As New Process()
                    process.StartInfo = startInfo
                    process.Start()

                    Dim stdErrTask As Task(Of String) = process.StandardError.ReadToEndAsync()
                    Dim stdOutTask As Task(Of String) = process.StandardOutput.ReadToEndAsync()

                    ' Streamed rather than read into memory: a dump is the
                    ' largest file this system handles and there is no reason
                    ' to hold all of it at once.
                    '
                    ' THE BROKEN PIPE IS AN EXPECTED PATH, NOT AN EXCEPTION.
                    ' When mysql.exe rejects an early statement - no database
                    ' selected, bad credentials, a syntax error from a
                    ' truncated dump - it exits immediately, and the rest of
                    ' this copy writes into a closed pipe. Left unhandled that
                    ' surfaces as an unhandled IOException ("The pipe has been
                    ' ended") that crashes the utility and tells the operator
                    ' nothing about what mysql actually objected to. Found by
                    ' running it. Swallowing the pipe error here is correct
                    ' precisely BECAUSE the real diagnosis is waiting on
                    ' stderr, which the code below goes on to read.
                    Try
                        Using dumpStream As New FileStream(dumpPath, FileMode.Open, FileAccess.Read, FileShare.Read)
                            Await dumpStream.CopyToAsync(process.StandardInput.BaseStream, cancellationToken).ConfigureAwait(False)
                            Await process.StandardInput.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(False)
                        End Using
                    Catch ex As IOException
                        ' Deliberately narrow: only the pipe. Anything else
                        ' still escapes.
                    End Try

                    Try
                        process.StandardInput.Close()
                    Catch ex As IOException
                    End Try

                    Await process.WaitForExitAsync(cancellationToken).ConfigureAwait(False)

                    Dim stdErr As String = Await stdErrTask.ConfigureAwait(False)
                    Await stdOutTask.ConfigureAwait(False)

                    Return New RestoreShellOutcome(process.ExitCode, stdErr)
                End Using

            Finally
                Try
                    If File.Exists(defaultsFile) Then File.Delete(defaultsFile)
                Catch ex As IOException
                    Console.Error.WriteLine(
                        $"WARNING: could not delete temporary credential file '{defaultsFile}': {ex.Message}")
                End Try
            End Try

        End Function

        ''' <summary>
        ''' Spec §15 step 6: confirm the expected users, products and balances
        ''' are present. Counts rows rather than trusting the exit code.
        ''' </summary>
        Private Shared Async Function VerifyAsync(
            options As DatabaseOptions,
            targetDatabase As String,
            result As RestoreResult,
            cancellationToken As CancellationToken) As Task

            Dim verifyOptions As New DatabaseOptions With {
                .Host = options.Host,
                .Port = options.Port,
                .Database = targetDatabase,
                .UserId = options.UserId,
                .Password = options.Password
            }

            Dim factory As New ConnectionFactory(verifyOptions)

            Using connection As MySqlConnection = Await factory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim present As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

                Using command As MySqlCommand = connection.CreateCommand()
                    command.CommandText =
                        "SELECT LOWER(table_name) FROM information_schema.tables WHERE table_schema = @schema;"
                    command.Parameters.AddWithValue("@schema", targetDatabase)
                    Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                        While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                            present.Add(reader.GetString(0))
                        End While
                    End Using
                End Using

                result.MissingTables = ExpectedTables.Where(Function(name) Not present.Contains(name)).ToList()

                result.UserCount = Await ScalarCountAsync(connection, "SELECT COUNT(*) FROM Users;", cancellationToken).ConfigureAwait(False)
                result.ProductCount = Await ScalarCountAsync(connection, "SELECT COUNT(*) FROM Products;", cancellationToken).ConfigureAwait(False)
                result.StockBalanceCount = Await ScalarCountAsync(connection, "SELECT COUNT(*) FROM StockBalances;", cancellationToken).ConfigureAwait(False)
                result.StockMovementCount = Await ScalarCountAsync(connection, "SELECT COUNT(*) FROM StockMovements;", cancellationToken).ConfigureAwait(False)
                result.AuditLogCount = Await ScalarCountAsync(connection, "SELECT COUNT(*) FROM AuditLogs;", cancellationToken).ConfigureAwait(False)

            End Using

        End Function

        Private Shared Async Function ScalarCountAsync(
            connection As MySqlConnection, sql As String, cancellationToken As CancellationToken) As Task(Of Long)

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = sql
                Dim value As Object = Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False)
                Return Convert.ToInt64(value, CultureInfo.InvariantCulture)
            End Using

        End Function

        ''' <summary>
        ''' Spec §15 step 5: "records a local maintenance log". Local on
        ''' purpose - a restore log written into the database being restored
        ''' would be destroyed by the next restore, and would be unwritable
        ''' during the window it most needs to record.
        ''' </summary>
        Private Shared Sub WriteMaintenanceLog(logDirectory As String, result As RestoreResult)

            Dim directoryPath As String = If(logDirectory, "C:\MerchandisingBackups")

            Try
                Directory.CreateDirectory(directoryPath)

                Dim line As New StringBuilder()
                line.Append(result.StartedAtUtc.ToString("u", CultureInfo.InvariantCulture))
                line.Append(If(result.Succeeded, " [RESTORED] ", " [FAILED] "))
                line.Append($"target={result.TargetDatabase} ")
                line.Append($"dump={Path.GetFileName(result.DumpPath)} ")
                line.Append($"elapsed={result.ElapsedSeconds:F1}s ")
                line.Append($"users={result.UserCount} products={result.ProductCount} ")
                line.Append($"balances={result.StockBalanceCount} movements={result.StockMovementCount} ")
                line.Append($"audit={result.AuditLogCount}")

                If result.MissingTables.Count > 0 Then
                    line.Append(" missingTables=" & String.Join("|", result.MissingTables))
                End If

                If Not String.IsNullOrWhiteSpace(result.Detail) Then
                    line.Append(" detail=" & result.Detail)
                End If

                File.AppendAllText(Path.Combine(directoryPath, MaintenanceLogFileName), line.ToString() & Environment.NewLine)

            Catch ex As IOException
                Console.Error.WriteLine($"CRITICAL: restore outcome could not be logged: {ex.Message}")
            End Try

        End Sub

    End Class

End Namespace
