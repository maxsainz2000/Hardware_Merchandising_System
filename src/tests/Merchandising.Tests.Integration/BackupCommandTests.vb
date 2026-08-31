Imports System.Collections.Generic
Imports System.IO
Imports System.Runtime.Versioning
Imports System.Security.AccessControl
Imports System.Security.Principal
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

        ' Succeeded and Partial both mean "dump written, checksum verified".
        ' They differ only in whether the off-host copy happened, and that is
        ' a property of which USB stick is plugged into this desk, not of the
        ' code under test. Asserting Succeeded here made a CHECKSUM test fail
        ' on any machine without a volume labelled MERCHBACKUP attached - it
        ' died on this line before reaching a single one of the assertions
        ' below that are the actual point of the test. Failed is still a
        ' failure: no usable dump was produced, and none of what follows
        ' could hold.
        '
        ' Nothing is lost by not demanding Succeeded here. The absent-volume
        ' branch is asserted by Backup_WhenOffHostVolumeAbsent_ReportsPartial-
        ' NotSucceeded, which points at a deliberately nonexistent label and
        ' so never depended on the environment. The volume-PRESENT off-host
        ' copy was never asserted by any test in this class - it is evidenced
        ' at evidence/phase-1/p1-17-backup-success.log (real D:, byte-
        ' identical, checksums agree) and is owed a real assertion when Phase
        ' 6 promotes backup to production quality. See ADR-019.
        Assert.IsTrue(result.Outcome = BackupOutcome.Succeeded OrElse result.Outcome = BackupOutcome.[Partial],
                      $"Backup produced no usable dump. Outcome: {result.Outcome}. Detail: {result.Detail}")

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
    ''' more backups than the count allows and checking not merely that two
    ''' remain, but that they are the two NEWEST - a test that only counts
    ''' files would pass identically for a bug that deleted the wrong end of
    ''' the list.
    ''' </summary>
    <TestMethod>
    Public Async Function Backup_PrunesOldestDumpsBeyondRetentionCount() As Task

        Dim settings As BackupSettings = BackupSettingsForTest(retentionCount:=2)
        Dim producedPaths As New List(Of String)

        For i As Integer = 1 To 3
            Dim run As BackupResult = Await RunBackupAsync(settings)
            Assert.AreNotEqual(BackupOutcome.Failed, run.Outcome, $"Run {i} failed: {run.Detail}")
            producedPaths.Add(run.FilePath)
            ' Dump file names carry a whole-second timestamp; without this the
            ' three runs would collide on one name and prove nothing.
            Await Task.Delay(1100)
        Next

        Dim remaining As String() = Directory.GetFiles(_workDirectory, "*.sql")
        Assert.HasCount(2, remaining,
                        "Retention count of 2 should have left exactly two dumps, found " & remaining.Length)

        Dim oldest As String = producedPaths(0)
        Dim newest As String() = {producedPaths(1), producedPaths(2)}

        Assert.IsFalse(File.Exists(oldest), $"The OLDEST dump should have been pruned: {oldest}")
        For Each expected As String In newest
            Assert.IsTrue(File.Exists(expected), $"A NEWER dump was pruned instead of the oldest: {expected}")
        Next

    End Function

    ''' <summary>
    ''' P6-07 / CARRY-02: closes the debt ADR-019 recorded. Until this test,
    ''' the volume-PRESENT off-host copy was evidenced only by
    ''' evidence/phase-1/p1-17-backup-success.log - never asserted by any
    ''' test in this class, deliberately, because a checksum test must not
    ''' depend on which USB stick is plugged into this desk (ADR-019).
    ''' </summary>
    ''' <remarks>
    ''' This test's entire subject IS the off-host copy, unlike the checksum
    ''' test ADR-019 was written about - so unlike that test, this one SKIPS
    ''' LOUDLY (Assert.Inconclusive, with a message explaining why) rather
    ''' than pretending to prove something it cannot, when no MERCHBACKUP
    ''' volume is attached. ADR-019 rejected Inconclusive for the checksum
    ''' test because that test could - and should - assert everything it
    ''' could regardless of hardware. This test cannot: without a volume
    ''' there is nothing here to assert.
    ''' </remarks>
    <TestMethod>
    Public Async Function Backup_WhenOffHostVolumeAttached_CopiesByteIdenticalDump() As Task

        If Not IsMerchBackupVolumeReady() Then
            Assert.Inconclusive(
                "SKIPPED LOUDLY: no volume labelled 'MERCHBACKUP' is attached to this machine. " &
                "This test asserts the off-host copy against a REAL volume (CARRY-02 / ADR-019) " &
                "and proves nothing without one - it is reported Inconclusive, not a silent pass.")
        End If

        Dim result As BackupResult = Await RunBackupAsync(BackupSettingsForTest(retentionCount:=5))

        Assert.AreEqual(BackupOutcome.Succeeded, result.Outcome,
                        $"A MERCHBACKUP volume was attached; the run should have copied off-host. Detail: {result.Detail}")
        Assert.IsNotNull(result.OffHostPath, "Succeeded but recorded no off-host path.")
        Assert.IsTrue(File.Exists(result.OffHostPath), $"Recorded off-host path does not exist: {result.OffHostPath}")

        Dim localBytes As Byte() = File.ReadAllBytes(result.FilePath)
        Dim offHostBytes As Byte() = File.ReadAllBytes(result.OffHostPath)
        CollectionAssert.AreEqual(localBytes, offHostBytes, "Off-host copy is not byte-identical to the local dump.")

        Dim offHostChecksum As String = Merchandising.Maintenance.Migrations.ChecksumCalculator.ComputeSha256Hex(result.OffHostPath)
        Assert.AreEqual(result.Sha256, offHostChecksum, "Off-host copy's checksum does not match the recorded one.")

    End Function

    ''' <summary>
    ''' P6-07 / ADR-025: the off-host copy is rotated by the same
    ''' RetentionCount as the local directory - proven the same way the
    ''' local retention test above is proven, against the real volume.
    ''' Skips loudly under the same rule as the test above: this is exactly
    ''' the branch that cannot be proven without real hardware attached.
    ''' </summary>
    <TestMethod>
    Public Async Function Backup_PrunesOffHostDumpsBeyondRetentionCountToo() As Task

        If Not IsMerchBackupVolumeReady() Then
            Assert.Inconclusive(
                "SKIPPED LOUDLY: no volume labelled 'MERCHBACKUP' is attached to this machine. " &
                "Off-host rotation (P6-07 / ADR-025) cannot be proven without a real off-host copy happening.")
        End If

        Dim settings As BackupSettings = BackupSettingsForTest(retentionCount:=2)

        For i As Integer = 1 To 3
            Dim run As BackupResult = Await RunBackupAsync(settings)
            Assert.AreEqual(BackupOutcome.Succeeded, run.Outcome, $"Run {i} did not succeed: {run.Detail}")
            Await Task.Delay(1100)
        Next

        Dim offHostFolder As String = FindOffHostFolder(settings)
        Dim remaining As String() = Directory.GetFiles(offHostFolder, "*.sql")
        Assert.HasCount(2, remaining,
                        "Retention count of 2 should have left exactly two off-host dumps, found " & remaining.Length)

    End Function

    ''' <summary>
    ''' Spec section 15, "Failure handling", against a different failure
    ''' than <see cref="Backup_WithWrongCredentials_ReportsFailureAndNeverClaimsSuccess"/>:
    ''' the database is fully reachable, but the backup directory itself
    ''' denies write access. This is the induced failure the card names
    ''' explicitly.
    ''' </summary>
    ''' <remarks>
    ''' Written expecting mysqldump's own result-file write to be what fails.
    ''' Measured instead: <see cref="MysqlDumpRunner"/> writes its
    ''' --defaults-extra-file (the credential file) into the SAME directory
    ''' before it ever starts mysqldump, and that write is what threw -
    ''' System.UnauthorizedAccessException, straight out of ExecuteAsync,
    ''' with NOTHING recorded anywhere. That was the actual defect this card
    ''' exists to close (see BackupCommand.ExecuteAsync's new Try/Catch), not
    ''' a pre-existing behaviour this test merely documents. Because the
    ''' database connection itself is unaffected, the fix also proves a
    ''' failed run lands in BackupLogs here, not only in the local fallback
    ''' log the wrong-credentials test depends on.
    ''' </remarks>
    <TestMethod>
    <SupportedOSPlatform("windows")>
    Public Async Function Backup_WithUnwritableDirectory_ReportsFailureAndNeverClaimsSuccess() As Task

        Dim directoryInfo As New DirectoryInfo(_workDirectory)
        Dim security As DirectorySecurity = directoryInfo.GetAccessControl()
        Dim currentUser As SecurityIdentifier = WindowsIdentity.GetCurrent().User

        ' Denies exactly the rights mysqldump needs to create its
        ' --result-file. A deny ACE wins over any allow ACE for the same
        ' identity, including one inherited from an administrator group, so
        ' this is a real denial rather than one this test's own account can
        ' bypass.
        Dim denyRule As New FileSystemAccessRule(
            currentUser,
            FileSystemRights.CreateFiles Or FileSystemRights.WriteData,
            AccessControlType.Deny)

        security.AddAccessRule(denyRule)
        directoryInfo.SetAccessControl(security)

        Try

            Dim result As BackupResult = Await RunBackupAsync(BackupSettingsForTest(retentionCount:=5))

            Assert.AreEqual(BackupOutcome.Failed, result.Outcome,
                            $"An unwritable backup directory should have reported Failed. Detail: {result.Detail}")
            Assert.IsFalse(String.IsNullOrWhiteSpace(result.Detail), "A failed run recorded no error detail.")

            If result.FilePath IsNot Nothing AndAlso File.Exists(result.FilePath) Then
                Assert.Fail("A failed run left a dump file behind: " & result.FilePath)
            End If

            Assert.IsTrue(result.LoggedToDatabase,
                          "The database connection was never broken by this failure - it should have logged to BackupLogs.")

        Finally

            ' Removed before TestCleanup tries to delete _workDirectory -
            ' otherwise cleanup fails with the exact denial this test just
            ' induced.
            security.RemoveAccessRule(denyRule)
            directoryInfo.SetAccessControl(security)

        End Try

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

    ''' <summary>
    ''' Same lookup <see cref="BackupCommand"/> itself uses in production -
    ''' matching by LABEL, never by drive letter (a USB stick mounts
    ''' differently on every machine, ADR-012/ADR-015).
    ''' </summary>
    Private Shared Function IsMerchBackupVolumeReady() As Boolean

        For Each drive As DriveInfo In DriveInfo.GetDrives()
            Try
                If drive.IsReady AndAlso
                   String.Equals(drive.VolumeLabel, "MERCHBACKUP", StringComparison.OrdinalIgnoreCase) Then
                    Return True
                End If
            Catch ex As IOException
                Continue For
            End Try
        Next

        Return False

    End Function

    ''' <summary>
    ''' Finds this test run's own off-host subfolder on the MERCHBACKUP
    ''' volume - the unique <see cref="_offHostFolderName"/>, never the real
    ''' operational one, for the same reason <see cref="RemoveWorkDirectory"/>
    ''' only ever touches that folder.
    ''' </summary>
    Private Function FindOffHostFolder(settings As BackupSettings) As String

        For Each drive As DriveInfo In DriveInfo.GetDrives()
            Try
                If drive.IsReady AndAlso
                   String.Equals(drive.VolumeLabel, settings.OffHostVolumeLabel, StringComparison.OrdinalIgnoreCase) Then
                    Return Path.Combine(drive.RootDirectory.FullName, settings.OffHostFolderName)
                End If
            Catch ex As IOException
                Continue For
            End Try
        Next

        Assert.Fail("MERCHBACKUP volume disappeared between IsMerchBackupVolumeReady() and this call.")
        Return Nothing

    End Function

    Private Shared Function BackupConfigPath() As String
        Return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MerchandisingSystem", "config", "database.backup.json")
    End Function

End Class
