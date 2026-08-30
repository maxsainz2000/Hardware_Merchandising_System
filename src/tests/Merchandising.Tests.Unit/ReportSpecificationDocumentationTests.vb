' Merchandising.Tests.Unit.ReportSpecificationDocumentationTests
'
' P6-01. Three phase gates running (Phase 3, 4, 5 - evidence/phase-5/INDEX.md
' section 4.5's own retrospective) have now been failed by a document whose
' claims outran its evidence, and the last one had every cited artifact
' existing on disk anyway - the defect was content, not existence. This test
' takes the P5-14/P5-16 shape tasks.md names directly: every evidence path
' docs/report-specification.md cites is resolved against disk AND required
' to contain what it is cited for, and the document's own worked numbers are
' recomputed here rather than trusted, the same technique
' UiSpecificationDocumentationTests.WorkAreaFigures_MatchTheDocumentedArithmetic
' uses for the UI spec's DIP arithmetic.
'
' No database needed - file reads, regex, and Merchandising.Domain.StoreTimeZone
' arithmetic only.

Imports System.Collections.Generic
Imports System.IO
Imports System.Text.RegularExpressions
Imports Merchandising.Domain
Imports Merchandising.Domain.Security
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public NotInheritable Class ReportSpecificationDocumentationTests

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
            "'. This test reads docs/report-specification.md from the working tree.")
        Return Nothing

    End Function

    Private Shared Function ReadRepoFile(relativePath As String) As String

        Dim fullPath As String = Path.Combine(FindRepositoryRoot().FullName, relativePath)
        Assert.IsTrue(File.Exists(fullPath), $"Expected file '{relativePath}' does not exist.")
        Return File.ReadAllText(fullPath)

    End Function

    Private Shared Function ReadDocument() As String

        Dim documentPath As String =
            Path.Combine(FindRepositoryRoot().FullName, "docs", "report-specification.md")

        Assert.IsTrue(
            File.Exists(documentPath),
            "docs/report-specification.md is missing. P6-01 owes a written document, not a planned one.")

        Return File.ReadAllText(documentPath)

    End Function

    ' --- Done when: states the academic-prototype framing plan.md section 5 requires ---

    <TestMethod>
    Public Sub Document_StatesTheAcademicPrototypeFraming()

        Dim document As String = ReadDocument()

        StringAssert.Contains(document, "academic prototype",
            "docs/report-specification.md does not state the academic-prototype framing plan.md section 5 requires.")

        StringAssert.Contains(document, "XAMPP",
            "docs/report-specification.md's scope note does not explain XAMPP is a course requirement.")

    End Sub

    ' --- Done when: every one of spec section 14's twelve reports has a row ---

    <TestMethod>
    Public Sub AllTwelveReports_AppearInBothTheScopeTableAndTheReturnsTreatmentTable()

        Dim document As String = ReadDocument()

        Dim reportNames As String() = {
            "Daily sales summary", "Sales by product", "Sales by cashier", "Payment-method summary",
            "Returns and cancellations", "Purchase-order history", "Goods-receiving history",
            "Current stock", "Low-stock report", "Stock movement report", "Stock-adjustment report",
            "Product performance summary"
        }

        Assert.HasCount(12, reportNames,
            "This test's own fixture no longer lists spec section 14's twelve reports - fix the fixture, not the assertions below.")

        For Each name As String In reportNames

            Dim occurrences As Integer = Regex.Matches(document, Regex.Escape(name)).Count

            Assert.IsGreaterThanOrEqualTo(2, occurrences,
                $"'{name}' must appear at least twice in docs/report-specification.md - once in section 1's scope table and once in section 4's returns-treatment table - but appears {occurrences} time(s).")

        Next

    End Sub

    ' --- Done when: returns treatment is Included or Excluded for every report, never left blank ---

    <TestMethod>
    Public Sub ReturnsTreatmentTable_AssignsIncludedOrExcluded_ToEveryReport()

        Dim document As String = ReadDocument()

        Dim section4Start As Integer = document.IndexOf("## 4. Returns/cancellations treatment", StringComparison.Ordinal)
        Dim section5Start As Integer = document.IndexOf("## 5. Cost basis", StringComparison.Ordinal)

        Assert.IsGreaterThanOrEqualTo(0, section4Start, "docs/report-specification.md's section 4 header is missing or reworded.")
        Assert.IsGreaterThan(section4Start, section5Start, "docs/report-specification.md's section 5 header is missing, reworded, or precedes section 4.")

        Dim section4Body As String = document.Substring(section4Start, section5Start - section4Start)

        Dim tableRows As MatchCollection = Regex.Matches(section4Body, "^\|\s*(?!Report\b)([^|]+)\|\s*\*\*(Included|Excluded)\*\*\s*\|", RegexOptions.Multiline)

        Assert.HasCount(12, tableRows,
            $"docs/report-specification.md section 4 must have exactly 12 report rows each stating **Included** or **Excluded** - found {tableRows.Count}.")

    End Sub

    ' --- Done when: the store-local -> UTC worked example is real arithmetic, not a copied sentence ---

    <TestMethod>
    Public Sub WorkedBoundaryExample_MatchesLiveStoreTimeZoneArithmetic()

        Dim document As String = ReadDocument()

        Dim date20260830 As New DateOnly(2026, 8, 30)
        Dim startUtc As DateTime = StoreTimeZone.StartOfDayUtc(date20260830)
        Dim endUtc As DateTime = StoreTimeZone.EndOfDayUtcExclusive(date20260830)

        Assert.AreEqual(New DateTime(2026, 8, 29, 16, 0, 0), startUtc)
        Assert.AreEqual(New DateTime(2026, 8, 30, 16, 0, 0), endUtc)

        StringAssert.Contains(document, "2026-08-29 16:00:00",
            "docs/report-specification.md section 2 must state StartOfDayUtc(2026-08-30) as a real computed timestamp.")
        StringAssert.Contains(document, "2026-08-30 16:00:00",
            "docs/report-specification.md section 2 must state EndOfDayUtcExclusive(2026-08-30) as a real computed timestamp.")
        StringAssert.Contains(document, "2026-08-30 15:59:59",
            "docs/report-specification.md section 2 must state the 23:59:59-local -> UTC conversion as a real computed timestamp.")

    End Sub

    ' --- Done when: the permission each report requires is stated, and matches the live policy registry ---

    <TestMethod>
    Public Sub ReportsViewPolicy_IsStatedAndMatchesTheLiveRegistry()

        Dim document As String = ReadDocument()

        StringAssert.Contains(document, "Reports.View",
            "docs/report-specification.md section 8 must name the live policy every report requires.")

        Dim definitions As IReadOnlyList(Of PolicyDefinition) = PolicyRegistry.Definitions
        Dim reportsView As PolicyDefinition = Nothing

        For Each definition As PolicyDefinition In definitions
            If String.Equals(definition.PolicyName, PolicyRegistry.Names.ReportsView, StringComparison.Ordinal) Then
                reportsView = definition
                Exit For
            End If
        Next

        Assert.IsNotNull(reportsView,
            "PolicyRegistry no longer registers Reports.View - docs/report-specification.md section 8 depends on it existing.")

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
            "docs/report-specification.md cites no evidence file at all - section 9 is supposed to name one.")

        Dim missing As New List(Of String)()

        For Each cited As String In citedPaths

            If Not File.Exists(Path.Combine(root, cited.Replace("/"c, Path.DirectorySeparatorChar))) Then
                missing.Add(cited)
            End If

        Next

        Assert.IsEmpty(missing,
            "docs/report-specification.md cites evidence files that do not exist on disk:" & Environment.NewLine &
            String.Join(Environment.NewLine, missing))

        ' Existing is not enough - P6-01's card explicitly names this as
        ' "the Phase 5 gate's finding 2, not repeated."
        Const evidenceFile As String = "evidence/phase-6/p6-01-report-definitions.txt"

        Assert.Contains(evidenceFile, citedPaths,
            $"docs/report-specification.md section 9 no longer cites '{evidenceFile}'.")

        Dim body As String = ReadRepoFile(evidenceFile.Replace("/"c, Path.DirectorySeparatorChar))

        StringAssert.Contains(body, "guardrail", StringComparison.OrdinalIgnoreCase,
            $"'{evidenceFile}' is cited for a guardrail run but contains no mention of one.")
        StringAssert.Contains(body, "watched fail",
            $"'{evidenceFile}' is cited for the harness's watched-fail proofs but does not record one.")

    End Sub

    ' --- Done when: the Domain/Contracts artifacts this document describes actually exist and are named as described ---

    <TestMethod>
    Public Sub ReturnsTreatmentEnum_AndReportRangeEnvelope_ExistAsDescribed()

        Dim enumSource As String = ReadRepoFile(
            Path.Combine("src", "Merchandising.Domain", "Reporting", "ReturnsTreatment.vb"))

        StringAssert.Contains(enumSource, "Included")
        StringAssert.Contains(enumSource, "Excluded")

        Dim envelopeSource As String = ReadRepoFile(
            Path.Combine("src", "Merchandising.Contracts", "Reporting", "ReportRangeEnvelope.vb"))

        StringAssert.Contains(envelopeSource, "FromDate")
        StringAssert.Contains(envelopeSource, "ToDate")
        StringAssert.Contains(envelopeSource, "TimeZone")
        StringAssert.Contains(envelopeSource, "ReturnsTreatment")

    End Sub

End Class
