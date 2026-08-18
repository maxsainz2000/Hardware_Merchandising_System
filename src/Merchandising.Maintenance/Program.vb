' Merchandising.Maintenance.Program
'
' CLI entry point for the maintenance utility. Today this wires up one
' command, "migrate" (P1-06). Backup/restore/schema-verification join it in
' later Phase 1/6 tasks per spec section 7.

Imports System
Imports System.IO
Imports System.Threading.Tasks
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Migrations
Imports MySqlConnector

Module Program

    Private Const MigratorConfigFileName As String = "database.migrator.json"

    ' vbc does not accept an Async Function as an entry point at all (tried
    ' both "As Task" and "As Task(Of Integer)" - both fail BC30737 "No
    ' accessible Main method with an appropriate signature"). Unlike C#,
    ' VB never got compiler-level async-Main sugar, so CLAUDE.md section 3's
    ' "(or Async Function Main() As Task)" does not compile under this SDK.
    ' Sub Main blocking on GetAwaiter().GetResult() is the standard VB
    ' workaround - the exception to "no .Result/.Wait()" this project
    ' otherwise holds, because Main has no async caller above it to await
    ' instead.
    Sub Main(args As String())
        RunAsync(args).GetAwaiter().GetResult()
    End Sub

    Private Async Function RunAsync(args As String()) As Task

        If args.Length = 0 OrElse Not String.Equals(args(0), "migrate", StringComparison.OrdinalIgnoreCase) Then
            Console.Error.WriteLine("Usage: Merchandising.Maintenance.exe migrate [--config <path>] [--migrations-dir <path>]")
            Environment.ExitCode = 1
            Return
        End If

        Dim configPath As String = Nothing
        Dim migrationsDir As String = Path.Combine(Directory.GetCurrentDirectory(), "db", "migrations")

        Dim i As Integer = 1
        While i < args.Length

            Select Case args(i)

                Case "--config"
                    i += 1
                    If i >= args.Length Then
                        Console.Error.WriteLine("--config requires a path argument.")
                        Environment.ExitCode = 1
                        Return
                    End If
                    configPath = args(i)

                Case "--migrations-dir"
                    i += 1
                    If i >= args.Length Then
                        Console.Error.WriteLine("--migrations-dir requires a path argument.")
                        Environment.ExitCode = 1
                        Return
                    End If
                    migrationsDir = args(i)

                Case Else
                    Console.Error.WriteLine($"Unrecognized argument '{args(i)}'.")
                    Environment.ExitCode = 1
                    Return

            End Select

            i += 1

        End While

        ' Default to the sibling of the API's config file, per
        ' docs/installation-guide.md section 3.1 - same directory, same
        ' ACL, different account (ADR-013).
        Dim resolvedConfigPath As String =
            If(configPath,
               Path.Combine(Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName))

        Try

            Dim options As DatabaseOptions = DatabaseOptionsLoader.Load(resolvedConfigPath)
            Dim factory As New ConnectionFactory(options)
            Dim runner As New MigrationRunner(factory)

            Console.WriteLine($"Applying migrations from '{migrationsDir}' as '{options.UserId}'...")

            Dim summary As MigrationRunSummary = Await runner.RunAsync(migrationsDir)

            For Each application As MigrationApplication In summary.Applied
                If application.Succeeded Then
                    Console.WriteLine($"  [OK]   {application.MigrationId}")
                Else
                    Console.WriteLine($"  [FAIL] {application.MigrationId} - {application.ErrorMessage}")
                End If
            Next

            If summary.Applied.Count = 0 Then
                Console.WriteLine("No new migrations to apply.")
            End If

            Environment.ExitCode = If(summary.AllSucceeded, 0, 1)

        Catch ex As MigrationChecksumMismatchException

            Console.Error.WriteLine($"Refusing to run: {ex.Message}")
            Environment.ExitCode = 2

        Catch ex As MySqlException

            ' The outer CLI boundary: translates an unhandled connection or
            ' privilege failure (for example merch_api lacking DDL,
            ' ADR-013) into a clear message and a non-zero exit code rather
            ' than letting the process crash with a raw stack trace.
            Console.Error.WriteLine($"Migration run failed: {ex.Message}")
            Environment.ExitCode = 1

        End Try

    End Function

End Module
