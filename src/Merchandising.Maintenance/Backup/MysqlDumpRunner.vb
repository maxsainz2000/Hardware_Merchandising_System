' Merchandising.Maintenance.Backup.MysqlDumpRunner
'
' Invokes C:\xampp\mysql\bin\mysqldump.exe (P0-04: mariadb-dump.exe does not
' exist in this XAMPP distribution, so spec section 15's "preferably
' mariadb-dump when available" resolves to mysqldump here).
'
' THE CREDENTIAL NEVER GOES ON THE COMMAND LINE. A --password=... argument is
' readable by any user on the machine for as long as the process lives, via
' Task Manager's command-line column or a WMI query - mysqldump itself warns
' about this on stderr. Instead the credential is written to a
' --defaults-extra-file that exists for the duration of the dump and is
' deleted in a Finally. That file inherits the ACL of the backup directory,
' which is already restricted (P0-07).

Imports System.Diagnostics
Imports System.IO
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Merchandising.Infrastructure.Data

Namespace Backup

    ''' <summary>
    ''' Runs mysqldump and reports what happened, without interpreting it.
    ''' </summary>
    Public NotInheritable Class MysqlDumpRunner

        ''' <summary>
        ''' Dumps <paramref name="options"/>.Database to
        ''' <paramref name="destinationPath"/>.
        ''' </summary>
        ''' <returns>
        ''' The process exit code and whatever it wrote to stderr. An exit
        ''' code of 0 is necessary but not sufficient for success - the
        ''' caller still has to check that a non-empty file arrived.
        ''' </returns>
        Public Shared Async Function RunAsync(
            mysqlDumpPath As String,
            options As DatabaseOptions,
            destinationPath As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of MysqlDumpOutcome)

            If Not File.Exists(mysqlDumpPath) Then
                Throw New FileNotFoundException(
                    $"mysqldump.exe not found at '{mysqlDumpPath}'. P0-04 pinned this path; " &
                    "note that mariadb-dump.exe does not exist in this XAMPP distribution.",
                    mysqlDumpPath)
            End If

            Dim defaultsFile As String = Path.Combine(
                Path.GetDirectoryName(destinationPath),
                "." & Guid.NewGuid().ToString("n") & ".cnf")

            Try
                WriteDefaultsFile(defaultsFile, options)

                Dim startInfo As New ProcessStartInfo With {
                    .FileName = mysqlDumpPath,
                    .UseShellExecute = False,
                    .RedirectStandardError = True,
                    .RedirectStandardOutput = True,
                    .CreateNoWindow = True
                }

                ' --defaults-extra-file MUST be the first argument; mysqldump
                ' rejects it anywhere else.
                startInfo.ArgumentList.Add("--defaults-extra-file=" & defaultsFile)
                ' Consistent snapshot without blocking writers. InnoDB only,
                ' which is every table this schema creates (ADR-003).
                startInfo.ArgumentList.Add("--single-transaction")
                startInfo.ArgumentList.Add("--routines")
                startInfo.ArgumentList.Add("--events")
                startInfo.ArgumentList.Add("--triggers")
                ' --databases keeps the CREATE DATABASE statement in the dump,
                ' so a restore onto an empty server recreates the schema with
                ' the right charset and collation rather than inheriting the
                ' server default, which on this machine is the wrong one
                ' (utf8mb4_general_ci - CLAUDE.md section 6.2).
                startInfo.ArgumentList.Add("--databases")
                startInfo.ArgumentList.Add(options.Database)
                startInfo.ArgumentList.Add("--result-file=" & destinationPath)

                Using process As New Process()
                    process.StartInfo = startInfo
                    process.Start()

                    Dim stdErrTask As Task(Of String) = process.StandardError.ReadToEndAsync()
                    Dim stdOutTask As Task(Of String) = process.StandardOutput.ReadToEndAsync()

                    Await process.WaitForExitAsync(cancellationToken).ConfigureAwait(False)

                    Dim stdErr As String = Await stdErrTask.ConfigureAwait(False)
                    Await stdOutTask.ConfigureAwait(False)

                    Return New MysqlDumpOutcome(process.ExitCode, stdErr)
                End Using

            Finally
                ' The defaults file holds a password. It goes even if the
                ' dump threw - this is the one cleanup that must not be
                ' allowed to be skipped.
                Try
                    If File.Exists(defaultsFile) Then
                        File.Delete(defaultsFile)
                    End If
                Catch ex As IOException
                    ' Deliberately narrow, and deliberately not silent: if the
                    ' credential file cannot be removed the operator has to
                    ' know, but it must not mask the dump's own result.
                    Console.Error.WriteLine(
                        $"WARNING: could not delete temporary credential file '{defaultsFile}': {ex.Message}")
                End Try
            End Try

        End Function

        Private Shared Sub WriteDefaultsFile(path As String, options As DatabaseOptions)

            Dim contents As New StringBuilder()
            contents.AppendLine("[client]")
            contents.AppendLine($"user={options.UserId}")
            contents.AppendLine($"password={options.Password}")
            contents.AppendLine($"host={options.Host}")
            contents.AppendLine($"port={options.Port}")

            ' ASCII, no BOM. mysqldump's option parser treats a UTF-8 BOM as
            ' part of the first line and fails to find the [client] group.
            File.WriteAllText(path, contents.ToString(), New UTF8Encoding(encoderShouldEmitUTF8Identifier:=False))

        End Sub

    End Class

End Namespace
