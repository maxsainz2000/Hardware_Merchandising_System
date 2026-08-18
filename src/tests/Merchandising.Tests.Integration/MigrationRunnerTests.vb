Imports System.IO
Imports System.Threading.Tasks
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Migrations
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

''' <summary>
''' P1-06 evidence: <see cref="MigrationRunner"/> against the real, pinned
''' MariaDB instance (ADR-000, ADR-008, ADR-009) - never a substitute.
''' </summary>
''' <remarks>
''' merch_migrator's grant (db/grants/0001_accounts-and-grants.sql) is
''' scoped to the literal "merchandising" database name, not a wildcard
''' pattern - it cannot CREATE an arbitrarily-named scratch database
''' alongside it. Each test therefore DROPs and recreates "merchandising"
''' itself to get a clean slate (exactly the capability P1-04's evidence
''' describes as unblocking "P1-06's re-runnable tests"), and TestCleanup
''' recreates it empty again afterward so it stays empty for P1-07.
'''
''' Requires both host configuration files from P1-04/P1-05/P1-06 to exist
''' on this machine: database.json (merch_api) at
''' <see cref="DatabaseOptionsLoader.DefaultConfigPath"/>, and
''' database.migrator.json (merch_migrator) alongside it. Neither is
''' committed to the repository. See docs/installation-guide.md section 3.1.
''' </remarks>
<TestClass>
Public Class MigrationRunnerTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"

    Private _migrationsDirectory As String
    Private _migratorOptions As DatabaseOptions
    Private _migratorAdminOptions As DatabaseOptions

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _migratorOptions = LoadMigratorOptions()

        _migratorAdminOptions = New DatabaseOptions With {
            .Host = _migratorOptions.Host,
            .Port = _migratorOptions.Port,
            .Database = String.Empty,
            .UserId = _migratorOptions.UserId,
            .Password = _migratorOptions.Password
        }

        Await RecreateMerchandisingDatabaseAsync()

        _migrationsDirectory = Path.Combine(Path.GetTempPath(), "merch_mig_test_" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(_migrationsDirectory)

    End Function

    <TestCleanup>
    Public Async Function TearDownAsync() As Task

        Await RecreateMerchandisingDatabaseAsync()

        If Directory.Exists(_migrationsDirectory) Then
            Directory.Delete(_migrationsDirectory, recursive:=True)
        End If

    End Function

    Private Async Function RecreateMerchandisingDatabaseAsync() As Task
        Dim adminFactory As New ConnectionFactory(_migratorAdminOptions)
        Using connection As MySqlConnection = Await adminFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "DROP DATABASE IF EXISTS merchandising; " &
                    "CREATE DATABASE merchandising CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"
                Await command.ExecuteNonQueryAsync()
            End Using
        End Using
    End Function

    Private Shared Function LoadMigratorOptions() As DatabaseOptions
        Dim migratorConfigPath As String =
            Path.Combine(Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName)
        Return DatabaseOptionsLoader.Load(migratorConfigPath)
    End Function

    Private Function WriteMigration(fileName As String, sql As String) As String
        ' VB is case-insensitive, so a local named "path" would shadow the
        ' System.IO.Path type within this method - named filePath instead.
        Dim filePath As String = Path.Combine(_migrationsDirectory, fileName)
        File.WriteAllText(filePath, sql)
        Return filePath
    End Function

    Private Function CreateRunner() As MigrationRunner
        Return New MigrationRunner(New ConnectionFactory(_migratorOptions))
    End Function

    ''' <summary>Done-when box 1: first run applies migrations and records them.</summary>
    <TestMethod>
    Public Async Function FirstRun_AppliesMigrationsAndRecordsThem() As Task

        WriteMigration("0001_create_widgets.sql",
            "CREATE TABLE Widgets (Id INT NOT NULL PRIMARY KEY, Name VARCHAR(50) NOT NULL) ENGINE=InnoDB;")
        WriteMigration("0002_seed_widgets.sql",
            "INSERT INTO Widgets (Id, Name) VALUES (1, 'hammer');")

        Dim summary As MigrationRunSummary = Await CreateRunner().RunAsync(_migrationsDirectory)

        Assert.HasCount(2, summary.Applied)
        Assert.IsTrue(summary.AllSucceeded)

        Using connection As MySqlConnection = Await New ConnectionFactory(_migratorOptions).CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM SchemaMigrations WHERE Succeeded = 1;"
                Assert.AreEqual(2L, CLng(Await command.ExecuteScalarAsync()))
            End Using

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM Widgets;"
                Assert.AreEqual(1L, CLng(Await command.ExecuteScalarAsync()))
            End Using

        End Using

    End Function

    ''' <summary>Done-when box 2: second run applies nothing.</summary>
    <TestMethod>
    Public Async Function SecondRun_AppliesNothing() As Task

        WriteMigration("0001_create_widgets.sql",
            "CREATE TABLE Widgets (Id INT NOT NULL PRIMARY KEY, Name VARCHAR(50) NOT NULL) ENGINE=InnoDB;")

        Dim runner As MigrationRunner = CreateRunner()
        Await runner.RunAsync(_migrationsDirectory)

        Dim secondSummary As MigrationRunSummary = Await runner.RunAsync(_migrationsDirectory)

        Assert.IsEmpty(secondSummary.Applied)

    End Function

    ''' <summary>Done-when box 3: tampering with an applied file causes a clear refusal, not a silent skip.</summary>
    <TestMethod>
    Public Async Function TamperedAppliedFile_CausesRefusal() As Task

        Dim path As String = WriteMigration("0001_create_widgets.sql",
            "CREATE TABLE Widgets (Id INT NOT NULL PRIMARY KEY, Name VARCHAR(50) NOT NULL) ENGINE=InnoDB;")

        Dim runner As MigrationRunner = CreateRunner()
        Await runner.RunAsync(_migrationsDirectory)

        File.WriteAllText(path,
            "CREATE TABLE Widgets (Id INT NOT NULL PRIMARY KEY, Name VARCHAR(999) NOT NULL) ENGINE=InnoDB;")

        Await Assert.ThrowsExactlyAsync(Of MigrationChecksumMismatchException)(
            Function() runner.RunAsync(_migrationsDirectory))

    End Function

    ''' <summary>Done-when box 4: a failing migration rolls back and records the failure.</summary>
    <TestMethod>
    Public Async Function FailingMigration_RollsBackAndRecordsFailure() As Task

        WriteMigration("0001_create_probe.sql",
            "CREATE TABLE RollbackProbe (Id INT NOT NULL PRIMARY KEY, Value VARCHAR(50) NOT NULL) ENGINE=InnoDB;")
        WriteMigration("0002_conflicting_inserts.sql",
            "INSERT INTO RollbackProbe (Id, Value) VALUES (1, 'first');" &
            "INSERT INTO RollbackProbe (Id, Value) VALUES (1, 'duplicate-fails');")

        Dim summary As MigrationRunSummary = Await CreateRunner().RunAsync(_migrationsDirectory)

        Assert.HasCount(2, summary.Applied)
        Assert.IsTrue(summary.Applied(0).Succeeded)
        Assert.IsFalse(summary.Applied(1).Succeeded)

        Using connection As MySqlConnection = Await New ConnectionFactory(_migratorOptions).CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM RollbackProbe;"
                Assert.AreEqual(
                    0L, CLng(Await command.ExecuteScalarAsync()),
                    "Both inserts were in the same uncommitted transaction - the failed " &
                    "second insert must roll back the first, not just itself.")
            End Using

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT Succeeded FROM SchemaMigrations WHERE MigrationId = '0002_conflicting_inserts';"
                Assert.IsFalse(CBool(Await command.ExecuteScalarAsync()))
            End Using

        End Using

    End Function

    ''' <summary>Done-when box 5: runner connects as merch_migrator; running it with database.json (the API account) fails loudly rather than half-applying.</summary>
    <TestMethod>
    Public Async Function RunningAsApiAccount_FailsLoudlyRatherThanHalfApplying() As Task

        WriteMigration("0001_create_widgets.sql",
            "CREATE TABLE Widgets (Id INT NOT NULL PRIMARY KEY, Name VARCHAR(50) NOT NULL) ENGINE=InnoDB;")

        ' Deliberately points at the REAL "merchandising" database (via the
        ' default database.json / merch_api config), not the scratch
        ' database - the claim under test is specifically that merch_api
        ' holds no DDL privilege there (ADR-013), which a scratch database
        ' it was never granted anything on would not distinguish from a
        ' plain "access denied to this database" failure.
        Dim apiOptions As DatabaseOptions = DatabaseOptionsLoader.Load()
        Dim runner As New MigrationRunner(New ConnectionFactory(apiOptions))

        ' VB does not allow Await inside Catch/Finally (CLAUDE.md section 3),
        ' so the safety-net cleanup below runs unconditionally after this
        ' Try/Catch, and any assertion failure is captured and rethrown
        ' afterward rather than lost.
        Dim assertionFailure As Exception = Nothing

        Try
            Await Assert.ThrowsExactlyAsync(Of MySqlException)(
                Function() runner.RunAsync(_migrationsDirectory))
        Catch ex As Exception
            assertionFailure = ex
        End Try

        ' Safety net, not the assertion: if the privilege model ever
        ' regresses and this DOES half-apply, do not leave a stray
        ' table behind in the real schema - P1-07 needs it empty.
        Dim migratorOptions As DatabaseOptions = _migratorOptions
        Using connection As MySqlConnection = Await New ConnectionFactory(migratorOptions).CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "DROP TABLE IF EXISTS Widgets; DROP TABLE IF EXISTS SchemaMigrations;"
                Await command.ExecuteNonQueryAsync()
            End Using
        End Using

        If assertionFailure IsNot Nothing Then
            Throw assertionFailure
        End If

    End Function

End Class
