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
''' <para>
''' <b>Changed at P1-07.</b> Before P1-07, "merchandising" had no permanent
''' schema, so each test could DROP and recreate the whole database for a
''' clean slate. P1-07 gave it a real, permanent schema (10 tables, seeded
''' roles, an applied "0001_foundation" row in SchemaMigrations) that every
''' other task now depends on - dropping the database out from under it on
''' every test run would destroy real state, not just test fixtures.
''' </para>
''' <para>
''' merch_migrator's grant (db/grants/0001_accounts-and-grants.sql) is also
''' scoped to the literal "merchandising" database name, not a wildcard
''' pattern, so a side-by-side scratch database was never an option either
''' (see the P1-06 task-card result note).
''' </para>
''' <para>
''' Isolation is now per-test-run naming instead of a shared clean slate:
''' every table this suite creates is named <c>migtest_&lt;suffix&gt;_...</c>
''' with a fresh GUID suffix per test, and every migration identifier this
''' suite writes contains "migtest" as a substring. <see cref="CleanUpTestArtifactsAsync"/>
''' finds and drops exactly those tables via <c>information_schema</c> and
''' deletes exactly those <c>SchemaMigrations</c> rows, at both SetUp (in
''' case a previous run crashed mid-test) and TearDown - never touching the
''' real tables or the real "0001_foundation" row.
''' </para>
''' <para>
''' Requires both host configuration files from P1-04/P1-05/P1-06 to exist
''' on this machine: database.json (merch_api) at
''' <see cref="DatabaseOptionsLoader.DefaultConfigPath"/>, and
''' database.migrator.json (merch_migrator) alongside it. Neither is
''' committed to the repository. See docs/installation-guide.md section 3.1.
''' </para>
''' </remarks>
<TestClass>
Public Class MigrationRunnerTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const TestArtifactMarker As String = "migtest"

    Private _migrationsDirectory As String
    Private _migratorOptions As DatabaseOptions
    Private _suffix As String

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _migratorOptions = LoadMigratorOptions()
        _suffix = Guid.NewGuid().ToString("N").Substring(0, 8)

        Await CleanUpTestArtifactsAsync()

        _migrationsDirectory = Path.Combine(Path.GetTempPath(), "merch_mig_test_" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(_migrationsDirectory)

    End Function

    <TestCleanup>
    Public Async Function TearDownAsync() As Task

        Await CleanUpTestArtifactsAsync()

        If Directory.Exists(_migrationsDirectory) Then
            Directory.Delete(_migrationsDirectory, recursive:=True)
        End If

    End Function

    ''' <summary>
    ''' Drops every table named <c>migtest_...</c> (read from
    ''' <c>information_schema</c>, not a fixed list, so a future test cannot
    ''' silently leak a table this misses) and deletes every SchemaMigrations
    ''' row whose identifier contains "migtest". Table/identifier names here
    ''' are not parameter-bindable in MySQL - they come only from this test
    ''' class's own naming convention, never from external input.
    ''' </summary>
    Private Async Function CleanUpTestArtifactsAsync() As Task

        Dim factory As New ConnectionFactory(_migratorOptions)

        Using connection As MySqlConnection = Await factory.CreateOpenConnectionAsync()

            Dim tableNames As New List(Of String)

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT TABLE_NAME FROM information_schema.TABLES " &
                    "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME LIKE 'migtest\_%' ESCAPE '\\';"
                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync()
                    While Await reader.ReadAsync()
                        tableNames.Add(reader.GetString(0))
                    End While
                End Using
            End Using

            For Each tableName As String In tableNames
                Using command As MySqlCommand = connection.CreateCommand()
                    command.CommandText = $"DROP TABLE IF EXISTS `{tableName}`;"
                    Await command.ExecuteNonQueryAsync()
                End Using
            Next

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = $"DELETE FROM SchemaMigrations WHERE MigrationId LIKE '%{TestArtifactMarker}%';"
                Await command.ExecuteNonQueryAsync()
            End Using

        End Using

    End Function

    Private Shared Function LoadMigratorOptions() As DatabaseOptions
        Dim migratorConfigPath As String =
            Path.Combine(Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName)
        Return DatabaseOptionsLoader.Load(migratorConfigPath)
    End Function

    ''' <summary>Test-scoped table name, isolated from the real schema and from other tests by GUID suffix.</summary>
    Private Function TestTableName(baseName As String) As String
        Return $"{TestArtifactMarker}_{_suffix}_{baseName}"
    End Function

    ''' <summary>Test-scoped migration file name - the marker in the identifier is what <see cref="CleanUpTestArtifactsAsync"/> matches on.</summary>
    Private Function MigrationFileName(number As String, description As String) As String
        Return $"{number}_{TestArtifactMarker}_{_suffix}_{description}.sql"
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

        Dim widgetsTable As String = TestTableName("widgets")

        WriteMigration(MigrationFileName("0001", "create_widgets"),
            $"CREATE TABLE {widgetsTable} (Id INT NOT NULL PRIMARY KEY, Name VARCHAR(50) NOT NULL) ENGINE=InnoDB;")
        WriteMigration(MigrationFileName("0002", "seed_widgets"),
            $"INSERT INTO {widgetsTable} (Id, Name) VALUES (1, 'hammer');")

        Dim summary As MigrationRunSummary = Await CreateRunner().RunAsync(_migrationsDirectory)

        Assert.HasCount(2, summary.Applied)
        Assert.IsTrue(summary.AllSucceeded)

        Using connection As MySqlConnection = Await New ConnectionFactory(_migratorOptions).CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    $"SELECT COUNT(*) FROM SchemaMigrations WHERE Succeeded = 1 AND MigrationId LIKE '%{TestArtifactMarker}_{_suffix}%';"
                Assert.AreEqual(2L, CLng(Await command.ExecuteScalarAsync()))
            End Using

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = $"SELECT COUNT(*) FROM {widgetsTable};"
                Assert.AreEqual(1L, CLng(Await command.ExecuteScalarAsync()))
            End Using

        End Using

    End Function

    ''' <summary>Done-when box 2: second run applies nothing.</summary>
    <TestMethod>
    Public Async Function SecondRun_AppliesNothing() As Task

        WriteMigration(MigrationFileName("0001", "create_widgets"),
            $"CREATE TABLE {TestTableName("widgets")} (Id INT NOT NULL PRIMARY KEY, Name VARCHAR(50) NOT NULL) ENGINE=InnoDB;")

        Dim runner As MigrationRunner = CreateRunner()
        Await runner.RunAsync(_migrationsDirectory)

        Dim secondSummary As MigrationRunSummary = Await runner.RunAsync(_migrationsDirectory)

        Assert.IsEmpty(secondSummary.Applied)

    End Function

    ''' <summary>Done-when box 3: tampering with an applied file causes a clear refusal, not a silent skip.</summary>
    <TestMethod>
    Public Async Function TamperedAppliedFile_CausesRefusal() As Task

        Dim widgetsTable As String = TestTableName("widgets")

        Dim filePath As String = WriteMigration(MigrationFileName("0001", "create_widgets"),
            $"CREATE TABLE {widgetsTable} (Id INT NOT NULL PRIMARY KEY, Name VARCHAR(50) NOT NULL) ENGINE=InnoDB;")

        Dim runner As MigrationRunner = CreateRunner()
        Await runner.RunAsync(_migrationsDirectory)

        File.WriteAllText(filePath,
            $"CREATE TABLE {widgetsTable} (Id INT NOT NULL PRIMARY KEY, Name VARCHAR(999) NOT NULL) ENGINE=InnoDB;")

        Await Assert.ThrowsExactlyAsync(Of MigrationChecksumMismatchException)(
            Function() runner.RunAsync(_migrationsDirectory))

    End Function

    ''' <summary>Done-when box 4: a failing migration rolls back and records the failure.</summary>
    <TestMethod>
    Public Async Function FailingMigration_RollsBackAndRecordsFailure() As Task

        Dim probeTable As String = TestTableName("rollbackprobe")
        Dim secondMigrationId As String = Path.GetFileNameWithoutExtension(MigrationFileName("0002", "conflicting_inserts"))

        WriteMigration(MigrationFileName("0001", "create_probe"),
            $"CREATE TABLE {probeTable} (Id INT NOT NULL PRIMARY KEY, Value VARCHAR(50) NOT NULL) ENGINE=InnoDB;")
        WriteMigration(MigrationFileName("0002", "conflicting_inserts"),
            $"INSERT INTO {probeTable} (Id, Value) VALUES (1, 'first');" &
            $"INSERT INTO {probeTable} (Id, Value) VALUES (1, 'duplicate-fails');")

        Dim summary As MigrationRunSummary = Await CreateRunner().RunAsync(_migrationsDirectory)

        Assert.HasCount(2, summary.Applied)
        Assert.IsTrue(summary.Applied(0).Succeeded)
        Assert.IsFalse(summary.Applied(1).Succeeded)

        Using connection As MySqlConnection = Await New ConnectionFactory(_migratorOptions).CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = $"SELECT COUNT(*) FROM {probeTable};"
                Assert.AreEqual(
                    0L, CLng(Await command.ExecuteScalarAsync()),
                    "Both inserts were in the same uncommitted transaction - the failed " &
                    "second insert must roll back the first, not just itself.")
            End Using

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT Succeeded FROM SchemaMigrations WHERE MigrationId = @migrationId;"
                command.Parameters.AddWithValue("@migrationId", secondMigrationId)
                Assert.IsFalse(CBool(Await command.ExecuteScalarAsync()))
            End Using

        End Using

    End Function

    ''' <summary>Done-when box 5: runner connects as merch_migrator; running it with database.json (the API account) fails loudly rather than half-applying.</summary>
    <TestMethod>
    Public Async Function RunningAsApiAccount_FailsLoudlyRatherThanHalfApplying() As Task

        Dim widgetsTable As String = TestTableName("widgets")

        WriteMigration(MigrationFileName("0001", "create_widgets"),
            $"CREATE TABLE {widgetsTable} (Id INT NOT NULL PRIMARY KEY, Name VARCHAR(50) NOT NULL) ENGINE=InnoDB;")

        ' Deliberately points at the REAL "merchandising" database (via the
        ' default database.json / merch_api config), not a scratch database -
        ' the claim under test is specifically that merch_api holds no DDL
        ' privilege there (ADR-013), which a scratch database it was never
        ' granted anything on would not distinguish from a plain "access
        ' denied to this database" failure.
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
        ' regresses and this DOES half-apply, do not leave this test's own
        ' table behind. Never touches SchemaMigrations directly here - it now
        ' holds the real, permanent migration history (P1-07) and merch_api
        ' has already been proven to have no privilege on it anyway.
        Using connection As MySqlConnection = Await New ConnectionFactory(_migratorOptions).CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = $"DROP TABLE IF EXISTS `{widgetsTable}`;"
                Await command.ExecuteNonQueryAsync()
            End Using
        End Using

        If assertionFailure IsNot Nothing Then
            Throw assertionFailure
        End If

    End Function

End Class
