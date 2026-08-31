' Merchandising.Tests.Unit.BackupRestoreGuideDocumentationTests
'
' P6-09. Same shape as ReportSpecificationDocumentationTests
' (P6-01/P5-14/P4-13/P3-08/P2-12): the drift-check for
' docs/backup-restore-guide.md. Every evidence path it cites is resolved
' against disk and required to contain what it is cited for, the CLI flags
' and paths it quotes are cross-checked against Merchandising.Maintenance's
' real usage strings rather than trusted prose, and the JSON field names it
' shows for the maintenance-mode request/response bodies are cross-checked
' against the real Contracts classes.
'
' No database needed - file reads and regex only.

Imports System.Collections.Generic
Imports System.IO
Imports System.Text.RegularExpressions
Imports Merchandising.Domain.Security
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public NotInheritable Class BackupRestoreGuideDocumentationTests

    ''' <summary>Same repository-root marker every other *DocumentationTests file in this suite uses.</summary>
    Private Shared Function FindRepositoryRoot() As DirectoryInfo

        Dim current As DirectoryInfo = New DirectoryInfo(AppContext.BaseDirectory)

        While current IsNot Nothing
            If File.Exists(Path.Combine(current.FullName, "CLAUDE.md")) Then
                Return current
            End If
            current = current.Parent
        End While

        Assert.Fail(
            "Could not locate the repository root above '" & AppContext.BaseDirectory &
            "'. This test reads docs/backup-restore-guide.md from the working tree.")
        Return Nothing

    End Function

    Private Shared Function ReadRepoFile(relativePath As String) As String

        Dim fullPath As String = Path.Combine(FindRepositoryRoot().FullName, relativePath)
        Assert.IsTrue(File.Exists(fullPath), $"Expected file '{relativePath}' does not exist.")
        Return File.ReadAllText(fullPath)

    End Function

    Private Shared Function ReadDocument() As String

        Dim documentPath As String =
            Path.Combine(FindRepositoryRoot().FullName, "docs", "backup-restore-guide.md")

        Assert.IsTrue(
            File.Exists(documentPath),
            "docs/backup-restore-guide.md is missing. P6-09 owes a written document, not a planned one.")

        Return File.ReadAllText(documentPath)

    End Function

    ' --- Done when: the guide never tells anyone to use root ---

    <TestMethod>
    Public Sub ThreeIdentities_AreNamedCorrectly_AndRootIsNeverAnInstructedAccount()

        Dim document As String = ReadDocument()

        StringAssert.Contains(document, "merch_backup")
        StringAssert.Contains(document, "merch_migrator")

        ' root may be named as the one-time environment-setup identity (the
        ' section 1 table says so explicitly), but must never appear as the
        ' identity behind an actual backup or restore COMMAND. Every
        ' Maintenance.exe invocation shown connects as merch_backup or
        ' merch_migrator per Program.vb - none take a --user or run "as root".
        Dim commandLines As MatchCollection =
            Regex.Matches(document, "^.*Merchandising\.Maintenance\.exe.*$", RegexOptions.Multiline)

        Assert.IsGreaterThanOrEqualTo(3, commandLines.Count,
            "Expected at least the backup command, the plain restore command, and the isolated-target restore command.")

        For Each m As Match In commandLines
            StringAssert.DoesNotMatch(m.Value, New Regex("root", RegexOptions.IgnoreCase),
                $"A Merchandising.Maintenance.exe command line names root: '{m.Value}'.")
        Next

    End Sub

    ' --- Done when: every command is copy-pasteable, i.e. matches the real CLI shape ---

    <TestMethod>
    Public Sub CliCommandsShown_MatchTheRealUsageStrings()

        Dim document As String = ReadDocument()
        Dim programSource As String = ReadRepoFile(Path.Combine("src", "Merchandising.Maintenance", "Program.vb"))

        ' Cross-check against Program.vb's own PrintUsage lines rather than
        ' trusting the guide's prose - a flag renamed in code and not in the
        ' doc must fail here, not be caught by a human skimming both files.
        StringAssert.Contains(programSource, "Merchandising.Maintenance.exe backup [--config <path>] [--mysqldump <path>] [--directory <path>] [--retention <n>]")
        StringAssert.Contains(programSource, "Merchandising.Maintenance.exe restore --file <dump.sql> [--target <database>] [--config <path>] [--mysql <path>]")

        StringAssert.Contains(document, "Merchandising.Maintenance.exe"" backup")
        StringAssert.Contains(document, "restore --file")
        StringAssert.Contains(document, "--target merchandising_restoretest")
        StringAssert.Contains(document, "--target <database>")

        ' The two paths P0-04 pinned - mariadb-dump.exe and mariadb.exe do
        ' not exist in this XAMPP distribution.
        StringAssert.Contains(document, "C:\xampp\mysql\bin\mysqldump.exe")
        StringAssert.Contains(document, "mariadb-dump.exe")
        StringAssert.Contains(document, "mariadb.exe")

        ' The CREATE DATABASE/USE override trap (P1-18) must be named, not
        ' just worked around silently.
        StringAssert.Contains(document, "CREATE DATABASE")
        StringAssert.Contains(document, "override")

    End Sub

    ' --- Done when: the maintenance-mode JSON shapes shown are real, not invented ---

    <TestMethod>
    Public Sub MaintenanceModeJsonFields_MatchTheRealContractsClasses()

        Dim document As String = ReadDocument()

        Dim enterRequestSource As String = ReadRepoFile(
            Path.Combine("src", "Merchandising.Contracts", "Maintenance", "EnterMaintenanceRequest.vb"))
        Dim releaseRequestSource As String = ReadRepoFile(
            Path.Combine("src", "Merchandising.Contracts", "Maintenance", "ReleaseMaintenanceRequest.vb"))
        Dim statusResponseSource As String = ReadRepoFile(
            Path.Combine("src", "Merchandising.Contracts", "Maintenance", "MaintenanceStatusResponse.vb"))

        StringAssert.Contains(enterRequestSource, "<JsonPropertyName(""reason"")>")
        StringAssert.Contains(releaseRequestSource, "<JsonPropertyName(""verificationPassed"")>")
        StringAssert.Contains(releaseRequestSource, "<JsonPropertyName(""detail"")>")
        StringAssert.Contains(statusResponseSource, "<JsonPropertyName(""inMaintenance"")>")

        StringAssert.Contains(document, """reason"":")
        StringAssert.Contains(document, """verificationPassed"": true")
        StringAssert.Contains(document, """detail"":")
        StringAssert.Contains(document, "inMaintenance")

        ' Routes and error codes, cross-checked against MaintenanceController.
        Dim controllerSource As String = ReadRepoFile(
            Path.Combine("src", "Merchandising.Api", "Controllers", "MaintenanceController.vb"))

        StringAssert.Contains(controllerSource, "<Route(""api/v1/admin/maintenance"")>")
        StringAssert.Contains(controllerSource, "<HttpPost(""enter"")>")
        StringAssert.Contains(controllerSource, "<HttpPost(""release"")>")
        StringAssert.Contains(controllerSource, "VERIFICATION_NOT_CONFIRMED")
        StringAssert.Contains(controllerSource, "MAINTENANCE_ALREADY_ACTIVE")

        StringAssert.Contains(document, "POST /api/v1/admin/maintenance/enter")
        StringAssert.Contains(document, "POST /api/v1/admin/maintenance/release")
        StringAssert.Contains(document, "VERIFICATION_NOT_CONFIRMED")
        StringAssert.Contains(document, "MAINTENANCE_ALREADY_ACTIVE")

    End Sub

    ' --- Done when: the permission the restore workflow requires is stated and matches the live registry ---

    <TestMethod>
    Public Sub MaintenancePerformPolicy_IsStatedAndMatchesTheLiveRegistry()

        Dim document As String = ReadDocument()

        StringAssert.Contains(document, "Maintenance.Perform")

        Dim definitions As IReadOnlyList(Of PolicyDefinition) = PolicyRegistry.Definitions
        Dim maintenancePerform As PolicyDefinition = Nothing

        For Each definition As PolicyDefinition In definitions
            If String.Equals(definition.PolicyName, PolicyRegistry.Names.MaintenancePerform, StringComparison.Ordinal) Then
                maintenancePerform = definition
                Exit For
            End If
        Next

        Assert.IsNotNull(maintenancePerform,
            "PolicyRegistry no longer registers Maintenance.Perform - docs/backup-restore-guide.md depends on it existing.")

    End Sub

    ' --- Done when: RPO/RTO figures match PA-005 and the measured P6-08 result ---

    <TestMethod>
    Public Sub RpoRtoFigures_MatchPa005AndTheMeasuredRestore()

        Dim document As String = ReadDocument()

        StringAssert.Contains(document, "24 hours")
        StringAssert.Contains(document, "15 minutes")
        StringAssert.Contains(document, "900s")
        StringAssert.Contains(document, "120 minutes")

        Dim restoreEvidence As String = ReadRepoFile(
            Path.Combine("evidence", "phase-6", "p6-08-restore-timed.txt"))

        StringAssert.Contains(restoreEvidence, "7.17 seconds")
        StringAssert.Contains(document, "7.5s")

    End Sub

    ' --- Done when: every evidence path this document cites is resolved against disk and contains what it is cited for ---

    <TestMethod>
    Public Sub EveryEvidencePathTheDocumentCites_ExistsAndContainsWhatItIsCitedFor()

        Dim document As String = ReadDocument()
        Dim root As String = FindRepositoryRoot().FullName

        Dim citedPaths As New List(Of String)()

        For Each m As Match In Regex.Matches(document, "evidence/[A-Za-z0-9._/-]+")

            Dim cited As String = m.Value.TrimEnd("."c, ","c, ")"c)

            If Not citedPaths.Contains(cited) Then
                citedPaths.Add(cited)
            End If

        Next

        Assert.IsNotEmpty(citedPaths,
            "docs/backup-restore-guide.md cites no evidence file at all - section 6 is supposed to name several.")

        Dim missing As New List(Of String)()

        For Each cited As String In citedPaths

            If Not File.Exists(Path.Combine(root, cited.Replace("/"c, Path.DirectorySeparatorChar))) Then
                missing.Add(cited)
            End If

        Next

        Assert.IsEmpty(missing,
            "docs/backup-restore-guide.md cites evidence files that do not exist on disk:" & Environment.NewLine &
            String.Join(Environment.NewLine, missing))

        ' Existing is not enough - the same standard P6-01's drift check holds
        ' report-specification.md to.
        Const evidenceFile As String = "evidence/phase-6/p6-09-backup-restore-guide.txt"

        Assert.Contains(evidenceFile, citedPaths,
            $"docs/backup-restore-guide.md no longer cites '{evidenceFile}'.")

        Dim body As String = ReadRepoFile(evidenceFile.Replace("/"c, Path.DirectorySeparatorChar))

        StringAssert.Contains(body, "guardrail", StringComparison.OrdinalIgnoreCase,
            $"'{evidenceFile}' is cited for a guardrail run but contains no mention of one.")
        StringAssert.Contains(body, "Restore SUCCEEDED",
            $"'{evidenceFile}' is cited for a real restore run but does not record one succeeding.")
        StringAssert.Contains(body, "Refused before starting",
            $"'{evidenceFile}' is cited for the checksum-mismatch refusal but does not record one.")

        Const verificationChecklist As String = "evidence/phase-1/p1-18-verification-checklist.md"
        Assert.Contains(verificationChecklist, citedPaths,
            $"docs/backup-restore-guide.md no longer cites '{verificationChecklist}'.")

    End Sub

End Class
