' Merchandising.Maintenance.Program
'
' CLI entry point for the maintenance utility. Wires up "migrate" (P1-06),
' "create-user" (P1-08, account bootstrap - see CreateUserCommand.vb) and
' "seed-demo" (P1-15, the one product the client spike decrements).
' Backup/restore/schema-verification join them in later Phase 1/6 tasks per
' spec section 7.

Imports System
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Threading.Tasks
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Backup
Imports Merchandising.Maintenance.Demo
Imports Merchandising.Maintenance.Migrations
Imports Merchandising.Maintenance.Restore
Imports Merchandising.Maintenance.Seed
Imports Merchandising.Maintenance.Users
Imports MySqlConnector

Module Program

    Private Const MigratorConfigFileName As String = "database.migrator.json"

    ' P2-11. Written beside the config files bootstrap.ps1 already writes in
    ' its own ACL-protected ConfigRoot (step 4), under the exact filename it
    ' already uses for the three database identities - one place the operator
    ' looks for every credential this installation generated, not two.
    Private Const CredentialRecordFileName As String = "installation-credentials.txt"

    ' P2-11. Non-secret catalog data (categories/brands/units/products/
    ' suppliers) - see db/seed/seed-data.json's own header for why user
    ' credentials are never in this file.
    Private Const DefaultSeedFileName As String = "seed-data.json"

    ' P1-17. The backup job runs as merch_backup - the third ADR-013 identity,
    ' provisioned at P1-04 and unused until now. It is NOT merch_api: the
    ' account that serves requests has no business dumping the database, and
    ' the account that dumps the database holds INSERT on exactly one table
    ' (db/grants/0004_backup-grants.sql).
    Private Const BackupConfigFileName As String = "database.backup.json"

    ' P0-04: mariadb-dump.exe does not exist in this XAMPP distribution, and
    ' neither does mariadb.exe. Spec section 15's "preferably mariadb-dump
    ' when available" therefore resolves to mysqldump here. Overridable so a
    ' different XAMPP layout does not need a rebuild.
    Private Const DefaultMysqlDumpPath As String = "C:\xampp\mysql\bin\mysqldump.exe"

    ' P0-04 again: mariadb.exe does not exist in this distribution either.
    Private Const DefaultMysqlClientPath As String = "C:\xampp\mysql\bin\mysql.exe"

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

        If args.Length = 0 Then
            PrintUsage()
            Environment.ExitCode = 1
            Return
        End If

        Select Case args(0).ToLowerInvariant()

            Case "migrate"
                Await RunMigrateAsync(args)

            Case "create-user"
                Await RunCreateUserAsync(args)

            Case "seed-demo"
                Await RunSeedDemoAsync(args)

            Case "seed"
                Await RunSeedAsync(args)

            Case "backup"
                Await RunBackupAsync(args)

            Case "restore"
                Await RunRestoreAsync(args)

            Case Else
                PrintUsage()
                Environment.ExitCode = 1

        End Select

    End Function

    Private Sub PrintUsage()
        Console.Error.WriteLine("Usage: Merchandising.Maintenance.exe migrate [--config <path>] [--migrations-dir <path>]")
        Console.Error.WriteLine("       Merchandising.Maintenance.exe create-user <username> <password> <role> [--config <path>]")
        Console.Error.WriteLine("       Merchandising.Maintenance.exe seed-demo <actor-username> [--sku <sku>] [--quantity <n>] [--config <path>]")
        Console.Error.WriteLine("       Merchandising.Maintenance.exe seed [--seed-file <path>] [--config <path>]")
        Console.Error.WriteLine("       Merchandising.Maintenance.exe backup [--config <path>] [--mysqldump <path>] [--directory <path>] [--retention <n>]")
        Console.Error.WriteLine("       Merchandising.Maintenance.exe restore --file <dump.sql> [--target <database>] [--config <path>] [--mysql <path>]")
    End Sub

    Private Async Function RunMigrateAsync(args As String()) As Task

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

    ''' <summary>
    ''' P1-15: creates the demo product the client spike points at, with an
    ''' opening balance posted as a movement rather than written straight into
    ''' the balance. Re-runnable - a second run reports that it changed nothing.
    ''' </summary>
    Private Async Function RunSeedDemoAsync(args As String()) As Task

        If args.Length < 2 Then
            PrintUsage()
            Environment.ExitCode = 1
            Return
        End If

        Dim actorUsername As String = args(1)
        Dim sku As String = "DEMO-001"
        Dim quantity As Decimal = 100D
        Dim configPath As String = Nothing

        Dim i As Integer = 2
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

                Case "--sku"
                    i += 1
                    If i >= args.Length Then
                        Console.Error.WriteLine("--sku requires a value.")
                        Environment.ExitCode = 1
                        Return
                    End If
                    sku = args(i)

                Case "--quantity"
                    i += 1
                    If i >= args.Length Then
                        Console.Error.WriteLine("--quantity requires a value.")
                        Environment.ExitCode = 1
                        Return
                    End If
                    If Not Decimal.TryParse(args(i), NumberStyles.Number, CultureInfo.InvariantCulture, quantity) Then
                        Console.Error.WriteLine($"'{args(i)}' is not a number.")
                        Environment.ExitCode = 1
                        Return
                    End If

                Case Else
                    Console.Error.WriteLine($"Unrecognized argument '{args(i)}'.")
                    Environment.ExitCode = 1
                    Return

            End Select

            i += 1

        End While

        ' merch_migrator, the same identity as migrate and create-user: seeding
        ' is a host-side bootstrap operation, not something the API does.
        Dim resolvedConfigPath As String =
            If(configPath,
               Path.Combine(Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName))

        Try

            Dim options As DatabaseOptions = DatabaseOptionsLoader.Load(resolvedConfigPath)
            Dim factory As New ConnectionFactory(options)

            Dim result As SeedDemoResult = Await SeedDemoCommand.RunAsync(
                factory, actorUsername, sku, "Demo product (P1-15 client spike)", 199.5D, 120D, quantity)

            If result.ProductCreated Then
                Console.WriteLine($"Created product '{sku}' (Id {result.ProductId}).")
            Else
                Console.WriteLine($"Product '{sku}' already existed (Id {result.ProductId}).")
            End If

            If result.OpeningApplied Then
                Console.WriteLine($"Opening balance posted as a movement: {result.QuantityOnHand} on hand.")
            Else
                Console.WriteLine($"Opening balance left alone: {result.QuantityOnHand} already on hand.")
            End If

            Console.WriteLine($"Point the client window at Product ID {result.ProductId}.")
            Environment.ExitCode = 0

        Catch ex As SeedDemoCommandException

            Console.Error.WriteLine($"seed-demo failed: {ex.Message}")
            Environment.ExitCode = 1

        Catch ex As MySqlException

            Console.Error.WriteLine($"seed-demo failed: {ex.Message}")
            Environment.ExitCode = 1

        End Try

    End Function

    ''' <summary>
    ''' P2-11: one seed pass that takes a freshly migrated database to a
    ''' database someone can log into and look at - see SeedCommand.vb for
    ''' what it does and why it is safe to run more than once.
    ''' </summary>
    Private Async Function RunSeedAsync(args As String()) As Task

        Dim configPath As String = Nothing
        Dim seedFilePath As String = Nothing

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

                Case "--seed-file"
                    i += 1
                    If i >= args.Length Then
                        Console.Error.WriteLine("--seed-file requires a path argument.")
                        Environment.ExitCode = 1
                        Return
                    End If
                    seedFilePath = args(i)

                Case Else
                    Console.Error.WriteLine($"Unrecognized argument '{args(i)}'.")
                    Environment.ExitCode = 1
                    Return

            End Select

            i += 1

        End While

        ' merch_migrator, same identity as migrate/create-user/seed-demo -
        ' seeding is a host-side bootstrap operation, not something the API
        ' does for itself.
        Dim resolvedConfigPath As String =
            If(configPath,
               Path.Combine(Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName))

        Dim resolvedSeedFilePath As String =
            If(seedFilePath,
               Path.Combine(Directory.GetCurrentDirectory(), "db", "seed", DefaultSeedFileName))

        Try

            Dim options As DatabaseOptions = DatabaseOptionsLoader.Load(resolvedConfigPath)
            Dim factory As New ConnectionFactory(options)

            Console.WriteLine($"Seeding from '{resolvedSeedFilePath}'...")

            Dim result As SeedResult = Await SeedCommand.RunAsync(factory, resolvedSeedFilePath)

            PrintSeedResult(result)

            If result.UsersCreated.Count > 0 Then
                Dim credentialPath As String =
                    Path.Combine(Path.GetDirectoryName(resolvedConfigPath), CredentialRecordFileName)
                AppendSeededCredentials(credentialPath, result.UsersCreated)
                Console.WriteLine($"New account credentials appended to '{credentialPath}'.")
            End If

            Environment.ExitCode = 0

        Catch ex As SeedCommandException

            Console.Error.WriteLine($"seed failed: {ex.Message}")
            Environment.ExitCode = 1

        Catch ex As MySqlException

            Console.Error.WriteLine($"seed failed: {ex.Message}")
            Environment.ExitCode = 1

        End Try

    End Function

    Private Sub PrintSeedResult(result As SeedResult)

        Console.WriteLine($"  categories  {result.CategoriesCreated} created, {result.CategoriesSkipped} already present")
        Console.WriteLine($"  brands      {result.BrandsCreated} created, {result.BrandsSkipped} already present")
        Console.WriteLine($"  units       {result.UnitsCreated} created, {result.UnitsSkipped} already present")
        Console.WriteLine($"  products    {result.ProductsCreated} created, {result.ProductsSkipped} already present")
        Console.WriteLine($"  suppliers   {result.SuppliersCreated} created, {result.SuppliersSkipped} already present")
        Console.WriteLine($"  accounts    {result.UsersCreated.Count} created, {result.UsersSkipped} already present")

        If result.UsersCreated.Count = 0 Then
            Console.WriteLine("  No new accounts - already seeded.")
            Return
        End If

        Console.WriteLine("")
        Console.WriteLine("  NEW ACCOUNT CREDENTIALS - shown once, never printed again:")
        For Each credential As SeededUserCredential In result.UsersCreated
            Console.WriteLine($"    {credential.RoleName,-20} {credential.Username,-20} {credential.Password}")
        Next

    End Sub

    ''' <summary>
    ''' Appends to the same credential record bootstrap.ps1's step 4 already
    ''' writes for the three database identities, so an operator has exactly
    ''' one file to read instead of two. Created fresh when run outside
    ''' bootstrap.ps1 (a standalone "dotnet run -- seed").
    ''' </summary>
    Private Sub AppendSeededCredentials(credentialPath As String, created As IReadOnlyList(Of SeededUserCredential))

        Dim builder As New StringBuilder()
        builder.AppendLine()
        builder.AppendLine("Merchandising System - seeded application accounts")
        builder.AppendLine($"Generated {Date.UtcNow:u} by Merchandising.Maintenance seed on {Environment.MachineName}")
        builder.AppendLine()
        builder.AppendLine("These accounts log into the API/clients directly - they are not database")
        builder.AppendLine("identities. Generated for THIS installation; not known to the author (ADR-012")
        builder.AppendLine("requirement 6). Re-running seed never reprints or changes them.")
        builder.AppendLine()
        For Each credential As SeededUserCredential In created
            builder.AppendLine($"  {credential.RoleName,-20} {credential.Username,-20} {credential.Password}")
        Next
        builder.AppendLine()
        builder.AppendLine("Do not copy this file into the repository, an email, or a chat message.")

        File.AppendAllText(credentialPath, builder.ToString())

    End Sub

    Private Async Function RunCreateUserAsync(args As String()) As Task

        If args.Length < 4 Then
            PrintUsage()
            Environment.ExitCode = 1
            Return
        End If

        Dim username As String = args(1)
        Dim password As String = args(2)
        Dim roleName As String = args(3)
        Dim configPath As String = Nothing

        Dim i As Integer = 4
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

                Case Else
                    Console.Error.WriteLine($"Unrecognized argument '{args(i)}'.")
                    Environment.ExitCode = 1
                    Return

            End Select

            i += 1

        End While

        ' Same identity as "migrate" - creating an account is a schema-
        ' owner-level bootstrap operation, not something merch_api does for
        ' itself (it has no self-registration endpoint, deliberately).
        Dim resolvedConfigPath As String =
            If(configPath,
               Path.Combine(Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName))

        Try

            Dim options As DatabaseOptions = DatabaseOptionsLoader.Load(resolvedConfigPath)
            Dim factory As New ConnectionFactory(options)

            Dim userId As Integer = Await CreateUserCommand.RunAsync(factory, username, password, roleName)

            Console.WriteLine($"Created user '{username}' (Id {userId}) with role '{roleName}'.")
            Environment.ExitCode = 0

        Catch ex As CreateUserCommandException

            Console.Error.WriteLine($"create-user failed: {ex.Message}")
            Environment.ExitCode = 1

        Catch ex As MySqlException

            Console.Error.WriteLine($"create-user failed: {ex.Message}")
            Environment.ExitCode = 1

        End Try

    End Function

    ''' <summary>
    ''' P1-17. Spec section 15: dump, verify, copy off-host, prune, record.
    ''' </summary>
    ''' <remarks>
    ''' Exits non-zero for BOTH a failed run and a partial one. A partial
    ''' run - dump good, off-host copy missing because the USB stick was
    ''' not plugged in - is a degraded backup, and a scheduled task that
    ''' reports success for it is how a system ends up with months of
    ''' backups that only ever existed on the machine that died.
    ''' </remarks>
    Private Async Function RunBackupAsync(args As String()) As Task

        Dim configPath As String = Nothing
        Dim mysqlDumpPath As String = DefaultMysqlDumpPath
        Dim directoryOverride As String = Nothing
        Dim retentionOverride As Integer? = Nothing

        Dim i As Integer = 1
        While i < args.Length

            Select Case args(i)

                Case "--config", "--mysqldump", "--directory", "--retention"

                    Dim switchName As String = args(i)
                    i += 1
                    If i >= args.Length Then
                        Console.Error.WriteLine($"{switchName} requires an argument.")
                        Environment.ExitCode = 1
                        Return
                    End If

                    Select Case switchName
                        Case "--config"
                            configPath = args(i)
                        Case "--mysqldump"
                            mysqlDumpPath = args(i)
                        Case "--directory"
                            directoryOverride = args(i)
                        Case "--retention"
                            Dim parsed As Integer
                            If Not Integer.TryParse(args(i), NumberStyles.Integer, CultureInfo.InvariantCulture, parsed) Then
                                Console.Error.WriteLine($"--retention requires a whole number, got '{args(i)}'.")
                                Environment.ExitCode = 1
                                Return
                            End If
                            retentionOverride = parsed
                    End Select

                Case Else
                    Console.Error.WriteLine($"Unrecognized argument '{args(i)}'.")
                    Environment.ExitCode = 1
                    Return

            End Select

            i += 1

        End While

        Dim resolvedConfigPath As String =
            If(configPath,
               Path.Combine(Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), BackupConfigFileName))

        Dim correlationId As String = Guid.NewGuid().ToString("d")

        Try

            Dim options As DatabaseOptions = DatabaseOptionsLoader.Load(resolvedConfigPath)

            Dim settings As BackupSettings = Await ResolveBackupSettingsAsync(options)

            If directoryOverride IsNot Nothing Then settings.Directory = directoryOverride
            If retentionOverride.HasValue Then settings.RetentionCount = retentionOverride.Value

            Dim result As BackupResult = Await BackupCommand.ExecuteAsync(
                options, settings, mysqlDumpPath, correlationId)

            PrintBackupResult(result)
            Environment.ExitCode = result.ExitCode

        Catch ex As FileNotFoundException

            ' Missing config or missing mysqldump.exe. Both are setup faults
            ' rather than backup failures, and neither can be recorded in
            ' BackupLogs because there is no usable connection to record with.
            Console.Error.WriteLine($"backup could not start: {ex.Message}")
            Environment.ExitCode = 1

        End Try

    End Function

    ''' <summary>
    ''' Reads backup settings from SystemSettings, falling back to the
    ''' compiled defaults when the database cannot be reached.
    ''' </summary>
    ''' <remarks>
    ''' A database that will not answer is exactly the case where the backup
    ''' matters most, so it must not stop the attempt. The run proceeds on
    ''' defaults, fails at the dump, and is recorded by BackupCommand as a
    ''' failure with the real reason attached - which is far more useful than
    ''' aborting here with a settings-load error.
    ''' </remarks>
    Private Async Function ResolveBackupSettingsAsync(options As DatabaseOptions) As Task(Of BackupSettings)

        Try
            Dim factory As New ConnectionFactory(options)
            Using connection As MySqlConnection = Await factory.CreateOpenConnectionAsync()
                Return Await BackupCommand.LoadSettingsAsync(connection)
            End Using

        Catch ex As MySqlException
            Console.Error.WriteLine(
                $"WARNING: could not read backup settings from SystemSettings ({ex.Message}). " &
                "Falling back to built-in defaults and attempting the backup anyway.")
            Return New BackupSettings()
        End Try

    End Function

    Private Sub PrintBackupResult(result As BackupResult)

        Console.WriteLine($"Backup {result.Outcome} (correlation {result.CorrelationId})")

        If result.FilePath IsNot Nothing Then
            Console.WriteLine($"  file        {result.FilePath}")
            Console.WriteLine($"  size        {result.SizeBytes} bytes")
            Console.WriteLine($"  sha256      {result.Sha256}")
        End If

        If result.OffHostPath IsNot Nothing Then
            Console.WriteLine($"  off-host    {result.OffHostPath}")
        End If

        If result.SourceDbVersion IsNot Nothing Then
            Console.WriteLine($"  server      {result.SourceDbVersion}")
        End If

        Console.WriteLine($"  retention   {result.RetentionCount} (pruned {result.PrunedFileCount})")
        Console.WriteLine($"  logged      {If(result.LoggedToDatabase, "BackupLogs", "LOCAL FILE ONLY - database unreachable")}")

        If Not String.IsNullOrWhiteSpace(result.Detail) Then
            ' Warnings and errors both go to stderr so a scheduled task's
            ' error stream carries them even when stdout is discarded.
            Console.Error.WriteLine($"  ** {result.Detail}")
        End If

    End Sub

    ''' <summary>
    ''' P1-18. Spec section 15 steps 4-6, run by the operator with the API
    ''' service STOPPED.
    ''' </summary>
    ''' <remarks>
    ''' Runs as merch_migrator, the only identity with DDL (ADR-013).
    ''' merch_api has none and could not recreate a table if it tried, which
    ''' is exactly why the identities are split.
    '''
    ''' There is no confirmation prompt. This command is invoked by a
    ''' scheduled or scripted maintenance procedure with the service already
    ''' stopped, and a prompt would hang that unattended. The safeguard is
    ''' that it cannot be reached over HTTP at all, not that it asks nicely.
    ''' </remarks>
    Private Async Function RunRestoreAsync(args As String()) As Task

        Dim configPath As String = Nothing
        Dim mysqlClientPath As String = DefaultMysqlClientPath
        Dim dumpPath As String = Nothing
        Dim targetDatabase As String = Nothing

        Dim i As Integer = 1
        While i < args.Length

            Select Case args(i)

                Case "--file", "--target", "--config", "--mysql"

                    Dim switchName As String = args(i)
                    i += 1
                    If i >= args.Length Then
                        Console.Error.WriteLine($"{switchName} requires an argument.")
                        Environment.ExitCode = 1
                        Return
                    End If

                    Select Case switchName
                        Case "--file"
                            dumpPath = args(i)
                        Case "--target"
                            targetDatabase = args(i)
                        Case "--config"
                            configPath = args(i)
                        Case "--mysql"
                            mysqlClientPath = args(i)
                    End Select

                Case Else
                    Console.Error.WriteLine($"Unrecognized argument '{args(i)}'.")
                    Environment.ExitCode = 1
                    Return

            End Select

            i += 1

        End While

        If String.IsNullOrWhiteSpace(dumpPath) Then
            Console.Error.WriteLine("restore requires --file <dump.sql>.")
            PrintUsage()
            Environment.ExitCode = 1
            Return
        End If

        Dim resolvedConfigPath As String =
            If(configPath,
               Path.Combine(Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName))

        Try

            Dim options As DatabaseOptions = DatabaseOptionsLoader.Load(resolvedConfigPath)

            Console.WriteLine($"Restoring '{dumpPath}' into '{If(targetDatabase, options.Database)}' as '{options.UserId}'...")

            Dim result As RestoreResult = Await RestoreCommand.ExecuteAsync(
                mysqlClientPath, options, dumpPath, targetDatabase)

            PrintRestoreResult(result)
            Environment.ExitCode = If(result.Succeeded, 0, 1)

        Catch ex As FileNotFoundException

            Console.Error.WriteLine($"restore could not start: {ex.Message}")
            Environment.ExitCode = 1

        End Try

    End Function

    Private Sub PrintRestoreResult(result As RestoreResult)

        Console.WriteLine($"Restore {If(result.Succeeded, "SUCCEEDED", "FAILED")}")
        Console.WriteLine($"  target      {result.TargetDatabase}")
        Console.WriteLine($"  dump        {result.DumpPath}")

        ' Reported in both units on purpose: seconds is the measurement,
        ' minutes is what PA-005's 15-minute target is stated in, and making
        ' the reader convert is how a missed target goes unnoticed.
        Console.WriteLine($"  elapsed     {result.ElapsedSeconds:F1}s ({result.ElapsedSeconds / 60D:F2} min) to VERIFIED state")
        Console.WriteLine($"  users       {result.UserCount}")
        Console.WriteLine($"  products    {result.ProductCount}")
        Console.WriteLine($"  balances    {result.StockBalanceCount}")
        Console.WriteLine($"  movements   {result.StockMovementCount}")
        Console.WriteLine($"  audit rows  {result.AuditLogCount}")

        If result.MissingTables.Count > 0 Then
            Console.Error.WriteLine($"  ** MISSING TABLES: {String.Join(", ", result.MissingTables)}")
        End If

        If Not String.IsNullOrWhiteSpace(result.Detail) Then
            Console.Error.WriteLine($"  ** {result.Detail}")
        End If

    End Sub

End Module
