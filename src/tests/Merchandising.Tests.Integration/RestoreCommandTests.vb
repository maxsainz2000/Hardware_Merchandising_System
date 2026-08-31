Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading.Tasks
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Backup
Imports Merchandising.Maintenance.Restore
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

''' <summary>
''' P6-08 evidence: <see cref="RestoreCommand"/> against the real, pinned
''' MariaDB instance and the real mysql.exe/mysqldump.exe (ADR-002, P0-04).
''' </summary>
''' <remarks>
''' <para>
''' Every restore in this class targets <c>merchandising_restoretest</c>, a
''' throwaway schema created once by <c>db/grants/0015_restore-rehearsal-grants.sql</c>
''' (root, run as a one-time environment-setup step, same as every other file
''' in that directory) - never the live <c>merchandising</c> schema every
''' other integration test in this suite also depends on. A restore that
''' fails partway drops and recreates tables with no transaction around the
''' DDL (RestoreCommand's own header comment; the LOCK TABLES gap
''' db/grants/0006_restore-grants.sql records once destroyed a live table
''' this way); running that risk against the shared schema for every commit
''' would be reckless. RestoreCommand itself still runs as merch_migrator,
''' the real production identity, not root - this proves that identity's
''' grants are sufficient rather than sidestepping the question.
''' </para>
''' <para>
''' Restoring INTO the scratch schema needs one extra step beyond what
''' P1-18's manual rehearsal needed to discover: mysqldump's own
''' <c>--databases</c> dump carries a <c>CREATE DATABASE</c>/<c>USE</c> pair
''' that OVERRIDES any <c>--database=</c> redirect and restores over the LIVE
''' schema instead (evidence/phase-1/p1-18-restore-log.txt, section 1). Every
''' test here restores a FILTERED COPY of the real dump with those two lines
''' removed - the original, recorded dump is never touched, which is exactly
''' what the checksum-refusal test below depends on.
''' </para>
''' </remarks>
<TestClass>
Public Class RestoreCommandTests

    Private Const MysqlDumpPath As String = "C:\xampp\mysql\bin\mysqldump.exe"
    Private Const MysqlClientPath As String = "C:\xampp\mysql\bin\mysql.exe"
    Private Const ScratchDatabaseName As String = "merchandising_restoretest"
    Private Const MigratorConfigFileName As String = "database.migrator.json"

    ''' <summary>PA-005: a demonstrated RTO of no more than 15 minutes to verified state.</summary>
    Private Const RtoTargetSeconds As Double = 15 * 60

    Private _workDirectory As String
    Private _migratorOptions As DatabaseOptions

    <TestInitialize>
    Public Async Function SetUpAsync() As Task
        _workDirectory = Path.Combine(Path.GetTempPath(), "merch-restore-test-" & Guid.NewGuid().ToString("n"))
        Directory.CreateDirectory(_workDirectory)
        _migratorOptions = LoadMigratorOptions()
        Await DropAllTablesInScratchDatabaseAsync()
    End Function

    <TestCleanup>
    Public Async Function TearDownAsync() As Task
        If Directory.Exists(_workDirectory) Then
            Directory.Delete(_workDirectory, recursive:=True)
        End If
        Await DropAllTablesInScratchDatabaseAsync()
    End Function

    ''' <summary>
    ''' Done-when boxes 1, 2 and 4: a real backup, restored end to end into
    ''' the isolated schema, verified by DATA rather than file existence, with
    ''' the elapsed time measured as a number and checked against PA-005's
    ''' 15-minute target explicitly - and the restored copy's ledger must
    ''' still reconcile (ADR-021), because a restore that silently drops
    ''' movements is the worst possible outcome of this card.
    ''' </summary>
    <TestMethod>
    Public Async Function Backup_ThenRestore_EndToEnd_MeasuresRtoAndVerifiesData() As Task

        Dim backupResult As BackupResult = Await RunRealBackupAsync()

        Assert.IsTrue(backupResult.Outcome = BackupOutcome.Succeeded OrElse backupResult.Outcome = BackupOutcome.[Partial],
                      $"Backup did not produce a usable dump. Detail: {backupResult.Detail}")

        Dim filteredDumpPath As String = FilterOutDatabaseSelectionLines(backupResult.FilePath)

        Dim restoreResult As RestoreResult = Await RestoreCommand.ExecuteAsync(
            MysqlClientPath, _migratorOptions, filteredDumpPath,
            targetDatabase:=ScratchDatabaseName, logDirectory:=_workDirectory)

        Assert.IsTrue(restoreResult.Succeeded, $"Restore did not succeed. Detail: {restoreResult.Detail}")
        Assert.IsEmpty(restoreResult.MissingTables,
            "Restore is missing tables: " & String.Join(", ", restoreResult.MissingTables))

        ' Verification is by DATA, not file existence: the specific rows this
        ' card asks about, not merely "mysql.exe exited 0".
        Assert.IsGreaterThan(0L, restoreResult.UserCount, "No users landed in the restored copy.")
        Assert.IsGreaterThan(0L, restoreResult.ProductCount, "No products landed in the restored copy.")
        Assert.IsGreaterThanOrEqualTo(0L, restoreResult.StockBalanceCount, "Stock balance count could not be read.")

        ' The elapsed time is a NUMBER, compared against the target
        ' explicitly - not rounded up if it happens to be close.
        Assert.IsLessThanOrEqualTo(RtoTargetSeconds, restoreResult.ElapsedSeconds,
            $"Restore took {restoreResult.ElapsedSeconds:F1}s to verified state, exceeding PA-005's " &
            $"{RtoTargetSeconds:F0}s (15 min) demonstrated RTO target.")

        Dim scratchOptions As New DatabaseOptions With {
            .Host = _migratorOptions.Host,
            .Port = _migratorOptions.Port,
            .Database = ScratchDatabaseName,
            .UserId = _migratorOptions.UserId,
            .Password = _migratorOptions.Password
        }

        Using connection As MySqlConnection = Await New ConnectionFactory(scratchOptions).CreateOpenConnectionAsync()
            Dim discrepancies = Await LedgerReconciliation.FindDiscrepanciesAsync(connection)
            Assert.IsEmpty(discrepancies,
                "The restored copy's ledger does not reconcile: " &
                String.Join("; ", discrepancies.Select(
                    Function(d) $"product {d.ProductId} expected {d.ExpectedQuantity} actual {d.ActualQuantity}")))
        End Using

    End Function

    ''' <summary>
    ''' Done-when box 5, watched fail: a restore must refuse to run against a
    ''' dump whose bytes no longer match the checksum BackupCommand recorded
    ''' for it - and refuse BEFORE touching mysql.exe or the target schema,
    ''' not merely fail partway through.
    ''' </summary>
    <TestMethod>
    Public Async Function Restore_WithMismatchedChecksum_RefusesBeforeTouchingAnything() As Task

        Dim backupResult As BackupResult = Await RunRealBackupAsync()

        Assert.IsTrue(backupResult.Outcome = BackupOutcome.Succeeded OrElse backupResult.Outcome = BackupOutcome.[Partial],
                      $"Backup did not produce a usable dump. Detail: {backupResult.Detail}")
        Assert.IsTrue(backupResult.LoggedToDatabase,
            "Backup was not recorded in BackupLogs - there is no recorded checksum for this test to contradict.")

        ' Corrupted IN PLACE, at the exact path BackupLogs recorded - the
        ' point of this test is that the recorded checksum no longer matches
        ' the file BackupLogs names, not a copy under a different name.
        File.AppendAllText(backupResult.FilePath, Environment.NewLine & "-- tampered by RestoreCommandTests")

        Dim restoreResult As RestoreResult = Await RestoreCommand.ExecuteAsync(
            MysqlClientPath, _migratorOptions, backupResult.FilePath,
            targetDatabase:=ScratchDatabaseName, logDirectory:=_workDirectory)

        Assert.IsFalse(restoreResult.Succeeded, "A restore against a corrupted dump reported success.")
        StringAssert.Contains(restoreResult.Detail, "checksum",
            $"Refusal reason did not name a checksum mismatch - was some other check what actually refused it? Detail: {restoreResult.Detail}")

        ' Nothing was changed: the scratch schema must still be empty,
        ' proving the refusal happened before mysql.exe ever ran against it.
        Dim tableCount As Long = Await CountTablesInScratchDatabaseAsync()
        Assert.AreEqual(0L, tableCount,
            "The scratch schema is not empty - the checksum refusal did not happen before the restore touched it.")

    End Function

    Private Async Function RunRealBackupAsync() As Task(Of BackupResult)
        Dim backupOptions As DatabaseOptions = DatabaseOptionsLoader.Load(BackupConfigPath())
        Dim settings As New BackupSettings With {.Directory = _workDirectory, .RetentionCount = 99}
        Return Await BackupCommand.ExecuteAsync(backupOptions, settings, MysqlDumpPath, Guid.NewGuid().ToString("d"))
    End Function

    ''' <summary>
    ''' Strips the dump's own <c>CREATE DATABASE</c>/<c>USE</c> lines onto a
    ''' new file, leaving the original untouched. See the class remarks for
    ''' why this is necessary - discovered at P1-18, not invented here.
    ''' </summary>
    Private Function FilterOutDatabaseSelectionLines(dumpPath As String) As String

        Dim filteredPath As String = Path.Combine(_workDirectory, "filtered-" & Path.GetFileName(dumpPath))

        Using reader As New StreamReader(dumpPath)
            Using writer As New StreamWriter(filteredPath, append:=False, New UTF8Encoding(encoderShouldEmitUTF8Identifier:=False))
                Dim line As String = reader.ReadLine()
                While line IsNot Nothing
                    If Not (line.StartsWith("CREATE DATABASE", StringComparison.OrdinalIgnoreCase) OrElse
                            line.StartsWith("USE ", StringComparison.OrdinalIgnoreCase)) Then
                        writer.WriteLine(line)
                    End If
                    line = reader.ReadLine()
                End While
            End Using
        End Using

        Return filteredPath

    End Function

    Private Shared Function LoadMigratorOptions() As DatabaseOptions
        Dim migratorConfigPath As String =
            Path.Combine(Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName)
        Return DatabaseOptionsLoader.Load(migratorConfigPath)
    End Function

    Private Shared Function BackupConfigPath() As String
        Return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MerchandisingSystem", "config", "database.backup.json")
    End Function

    ''' <summary>
    ''' Keeps the scratch schema empty between tests. mysqldump's own
    ''' <c>DROP TABLE IF EXISTS</c> would make this unnecessary for a
    ''' successful restore, but the checksum-refusal test needs to find the
    ''' schema genuinely empty to prove it was never touched.
    ''' </summary>
    Private Async Function DropAllTablesInScratchDatabaseAsync() As Task

        Dim scratchOptions As New DatabaseOptions With {
            .Host = _migratorOptions.Host,
            .Port = _migratorOptions.Port,
            .Database = ScratchDatabaseName,
            .UserId = _migratorOptions.UserId,
            .Password = _migratorOptions.Password
        }

        Using connection As MySqlConnection = Await New ConnectionFactory(scratchOptions).CreateOpenConnectionAsync()

            Dim tableNames As New List(Of String)

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE();"
                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync()
                    While Await reader.ReadAsync()
                        tableNames.Add(reader.GetString(0))
                    End While
                End Using
            End Using

            If tableNames.Count = 0 Then Return

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SET FOREIGN_KEY_CHECKS = 0;"
                Await command.ExecuteNonQueryAsync()
            End Using

            For Each tableName As String In tableNames
                Using command As MySqlCommand = connection.CreateCommand()
                    command.CommandText = $"DROP TABLE IF EXISTS `{tableName}`;"
                    Await command.ExecuteNonQueryAsync()
                End Using
            Next

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SET FOREIGN_KEY_CHECKS = 1;"
                Await command.ExecuteNonQueryAsync()
            End Using

        End Using

    End Function

    Private Async Function CountTablesInScratchDatabaseAsync() As Task(Of Long)

        Dim scratchOptions As New DatabaseOptions With {
            .Host = _migratorOptions.Host,
            .Port = _migratorOptions.Port,
            .Database = ScratchDatabaseName,
            .UserId = _migratorOptions.UserId,
            .Password = _migratorOptions.Password
        }

        Using connection As MySqlConnection = Await New ConnectionFactory(scratchOptions).CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE();"
                Return Convert.ToInt64(Await command.ExecuteScalarAsync())
            End Using
        End Using

    End Function

End Class
