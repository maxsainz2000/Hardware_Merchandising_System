Imports System.IO
Imports System.Threading.Tasks
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Backup
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

''' <summary>
''' P1-17 evidence: <see cref="BackupCommand"/> against the real, pinned
''' MariaDB instance and the real mysqldump.exe (ADR-002, P0-04 - there is no
''' mariadb-dump.exe in this XAMPP distribution).
''' </summary>
''' <remarks>
''' <para>
''' Every test writes into its own temporary directory rather than into
''' C:\MerchandisingBackups. The real backup directory is operational data:
''' a test that prunes files by retention count would happily delete a real
''' dump, and the retention test exists precisely to prove deletion works.
''' </para>
''' <para>
''' Requires database.backup.json to exist. That file is this machine's
''' setup, not something committed - see docs/installation-guide.md.
''' </para>
''' </remarks>
<TestClass>
Public Class BackupCommandTests

    Private Const MysqlDumpPath As String = "C:\xampp\mysql\bin\mysqldump.exe"

    Private _workDirectory As String
    Private _offHostFolderName As String

    <TestInitialize>
    Public Sub CreateWorkDirectory()
        _workDirectory = Path.Combine(Path.GetTempPath(), "merch-backup-test-" & Guid.NewGuid().ToString("n"))
        Directory.CreateDirectory(_workDirectory)

        ' The off-host tests copy to the REAL USB volume, because proving the
        ' copy works against a fake one would prove nothing. They must not
        ' leave anything behind on it, and they must never be able to delete
        ' a genuine backup - so they get their own folder rather than sharing
        ' the operational one.
        _offHostFolderName = "MerchandisingBackups-test-" & Guid.NewGuid().ToString("n").Substring(0, 8)
    End Sub

    <TestCleanup>
    Public Sub RemoveWorkDirectory()

        If Directory.Exists(_workDirectory) Then
            Directory.Delete(_workDirectory, recursive:=True)
        End If

        For Each drive As DriveInfo In DriveInfo.GetDrives()
            Try
                If Not drive.IsReady Then Continue For
                Dim candidate As String = Path.Combine(drive.RootDirectory.FullName, _offHostFolderName)
                If Directory.Exists(candidate) Then
                    Directory.Delete(candidate, recursive:=True)
                End If
            Catch ex As IOException
                ' A drive that cannot be interrogated held nothing of ours.
                Continue For
            End Try
        Next

    End Sub

    ''' <summary>
    ''' The whole point of the card: a run produces a dump, records its size
    ''' and checksum, and the checksum recomputed from the file on disk
    ''' matches what was recorded. A checksum that is merely *stored* proves
    ''' nothing - it has to be checked against the bytes.
    ''' </summary>
    <TestMethod>
    Public Async Function Backup_Succeeds_WritesDumpWithChecksumThatVerifies() As Task

        Dim result As BackupResult = Await RunBackupAsync(BackupSettingsForTest(retentionCount:=5))

        Assert.AreEqual(BackupOutcome.Succeeded, result.Outcome,
                        $"Backup did not succeed. Detail: {result.Detail}")

        Assert.IsTrue(File.Exists(result.FilePath), "Dump file was reported but does not exist.")
        Assert.IsGreaterThan(0L, result.SizeBytes.Value, "Dump file is empty.")

        Dim recomputed As String = Merchandising.Maintenance.Migrations.ChecksumCalculator.ComputeSha256Hex(result.FilePath)
        Assert.AreEqual(result.Sha256, recomputed, "Recorded checksum does not match the file on disk.")

        ' A dump that cannot be recognised as a dump is not a valid backup.
        ' Note the header says "MariaDB dump", not "MySQL dump" - this is
        ' MariaDB's own mysqldump build (10.19, Distrib 10.4.32), and asserting
        ' the MySQL wording passed nothing but a wrong expectation.
        Dim contents As String = File.ReadAllText(result.FilePath)
        StringAssert.Contains(contents.Substring(0, 512), "MariaDB dump", "File does not look like a mysqldump output.")
        StringAssert.Contains(contents.Substring(0, 512), "Database: merchandising", "Dump is not of the expected database.")

        ' The marker that proves the dump ran to the END rather than merely
        ' starting. This is the same check IsUsableDump makes in production.
        StringAssert.Contains(contents, "-- Dump completed", "Dump has no completion marker - it was truncated.")

        Assert.IsTrue(result.LoggedToDatabase, "No BackupLogs row was written.")

    End Function

    ''' <summary>
    ''' The rule this card is really about (spec section 15, "Failure
    ''' handling"): a broken run must report failure. The dangerous
    ''' behaviour is not an error - it is a run that exits zero, writes a
    ''' zero-byte file, and lets a scheduled task report success every night
    ''' until the day someone needs the backup.
    ''' </summary>
    <TestMethod>
    Public Async Function Backup_WithWrongCredentials_ReportsFailureAndNeverClaimsSuccess() As Task

        Dim badCredentials As New DatabaseOptions With {
            .Host = "127.0.0.1",
            .Port = 3306,
            .Database = "merchandising",
            .UserId = "merch_backup",
            .Password = "this-is-not-the-password"
        }

        Dim result As BackupResult = Await BackupCommand.ExecuteAsync(
            badCredentials,
            BackupSettingsForTest(retentionCount:=5),
            MysqlDumpPath,
            Guid.NewGuid().ToString("d"))

        Assert.AreEqual(BackupOutcome.Failed, result.Outcome, "A run with wrong credentials reported something other than Failed.")
        Assert.IsFalse(String.IsNullOrWhiteSpace(result.Detail), "A failed run recorded no error detail.")

        ' No usable artefact may be left behind pretending to be a backup.
        If result.FilePath IsNot Nothing AndAlso File.Exists(result.FilePath) Then
            Assert.Fail("A failed run left a dump file behind: " & result.FilePath)
        End If

        ' The credentials are wrong for logging too, so the database row is
        ' impossible. The failure must still be recorded somewhere a human
        ' will find it - that is what the local failure log is for.
        Assert.IsFalse(result.LoggedToDatabase, "Expected the BackupLogs write to be impossible with these credentials.")
        Assert.IsTrue(File.Exists(Path.Combine(_workDirectory, BackupCommand.FailureLogFileName)),
                      "No local failure log was written when the database could not be reached.")

    End Function

    ''' <summary>
    ''' The USB stick will not be plugged in some nights. That is neither a
    ''' success nor a failure and must not be reported as either: the dump
    ''' is good and worth keeping, but the off-host copy that protects
    ''' against losing the host did not happen.
    ''' </summary>
    <TestMethod>
    Public Async Function Backup_WhenOffHostVolumeAbsent_ReportsPartialNotSucceeded() As Task

        Dim settings As BackupSettings = BackupSettingsForTest(retentionCount:=5)
        settings.OffHostVolumeLabel = "NO-SUCH-VOLUME-" & Guid.NewGuid().ToString("n").Substring(0, 8)

        Dim result As BackupResult = Await RunBackupAsync(settings)

        Assert.AreEqual(BackupOutcome.[Partial], result.Outcome,
                        $"Expected Partial when the off-host volume is missing. Detail: {result.Detail}")
        Assert.IsTrue(File.Exists(result.FilePath), "The local dump should still have been kept.")
        Assert.IsNull(result.OffHostPath, "No off-host path should be recorded when the volume is absent.")
        StringAssert.Contains(result.Detail, "off-host", "The warning does not mention the off-host copy.")

    End Function

    ''' <summary>
    ''' Spec section 15, "Retention": a configurable count. Proven by running
    ''' more backups than the count allows and checking the oldest are gone.
    ''' </summary>
    <TestMethod>
    Public Async Function Backup_PrunesOldestDumpsBeyondRetentionCount() As Task

        Dim settings As BackupSettings = BackupSettingsForTest(retentionCount:=2)

        For i As Integer = 1 To 3
            Dim run As BackupResult = Await RunBackupAsync(settings)
            Assert.AreNotEqual(BackupOutcome.Failed, run.Outcome, $"Run {i} failed: {run.Detail}")
            ' Dump file names carry a whole-second timestamp; without this the
            ' three runs would collide on one name and prove nothing.
            Await Task.Delay(1100)
        Next

        Dim remaining As String() = Directory.GetFiles(_workDirectory, "*.sql")
        Assert.HasCount(2, remaining,
                        "Retention count of 2 should have left exactly two dumps, found " & remaining.Length)

    End Function

    Private Function BackupSettingsForTest(retentionCount As Integer) As BackupSettings
        Return New BackupSettings With {
            .Directory = _workDirectory,
            .RetentionCount = retentionCount,
            .OffHostVolumeLabel = "MERCHBACKUP",
            .OffHostFolderName = _offHostFolderName
        }
    End Function

    Private Async Function RunBackupAsync(settings As BackupSettings) As Task(Of BackupResult)
        Dim options As DatabaseOptions = DatabaseOptionsLoader.Load(BackupConfigPath())
        Return Await BackupCommand.ExecuteAsync(
            options, settings, MysqlDumpPath, Guid.NewGuid().ToString("d"))
    End Function

    Private Shared Function BackupConfigPath() As String
        Return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MerchandisingSystem", "config", "database.backup.json")
    End Function

End Class
