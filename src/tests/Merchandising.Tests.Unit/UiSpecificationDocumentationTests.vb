' Merchandising.Tests.Unit.UiSpecificationDocumentationTests
'
' P5-14: docs/ui-specification.md must be "verified against the running
' clients rather than transcribed by hand, in the P2-12 / P3-08 / P4-13
' shape - a drift check that fails when the XAML moves." Those three
' precedents split into two techniques: RolePermissionMatrixDocumentationTests
' regenerates a pure-data document byte-for-byte; ApiSpecificationDocumentationTests
' and DatabaseDesignDocumentationTests reflect live facts (routes, policies,
' column types) and assert the prose document mentions them. The UI
' specification is prose around window geometry and XAML conventions rather
' than pure data, so this test takes the second technique: construct each
' client's MainWindow on an STA thread (the same posture
' ProcurementLayoutTests/InventoryLayoutTests/POSLayoutTests already use) to
' read its declared geometry, read each MainWindow.xaml and
' ApiFailurePresenter.vb as source text for the conventions that are not
' CLR properties, and assert the committed document matches all of it.
'
' No database needed - construction + file reads only, the same posture as
' the three *LayoutTests suites this file draws its window-size fixture from.

Imports System.Globalization
Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Threading
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
<DoNotParallelize>
Public NotInheritable Class UiSpecificationDocumentationTests

    ' --------------------------------------------------------------- fixtures

    ''' <summary>
    ''' Walks up from the test assembly until it finds the repository root,
    ''' identified by CLAUDE.md - the same marker
    ''' RolePermissionMatrixDocumentationTests.FindRepositoryRoot and
    ''' ApiSpecificationDocumentationTests.FindRepositoryRoot use.
    ''' </summary>
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
            "'. This test reads docs/ui-specification.md and client source files from the working tree.")
        Return Nothing

    End Function

    Private Shared Function ReadRepoFile(relativePath As String) As String

        Dim fullPath As String = Path.Combine(FindRepositoryRoot().FullName, relativePath)
        Assert.IsTrue(File.Exists(fullPath), $"Expected file '{relativePath}' does not exist.")
        Return File.ReadAllText(fullPath)

    End Function

    Private Shared Function ReadDocument() As String

        Dim documentPath As String =
            Path.Combine(FindRepositoryRoot().FullName, "docs", "ui-specification.md")

        Assert.IsTrue(
            File.Exists(documentPath),
            "docs/ui-specification.md is missing. P5-14 owes a written document, not a planned one.")

        Return File.ReadAllText(documentPath)

    End Function

    ''' <summary>Runs WPF work on a dedicated STA thread. MSTest gives no apartment guarantee.</summary>
    Private Shared Function OnStaThread(Of T)(work As Func(Of T)) As T

        Dim result As T = Nothing
        Dim captured As Exception = Nothing

        Dim worker As New Thread(
            Sub()
                Try
                    result = work()
                Catch ex As Exception
                    captured = ex
                End Try
            End Sub)

        worker.SetApartmentState(ApartmentState.STA)
        worker.IsBackground = True
        worker.Start()
        worker.Join()

        If captured IsNot Nothing Then
            Throw New InvalidOperationException("The STA construction pass threw: " & captured.Message, captured)
        End If

        Return result

    End Function

    Private NotInheritable Class WindowGeometry

        Public Sub New(clientLabel As String, width As Double, height As Double, minWidth As Double, minHeight As Double)
            Me.ClientLabel = clientLabel
            Me.Width = width
            Me.Height = height
            Me.MinWidth = minWidth
            Me.MinHeight = minHeight
        End Sub

        Public ReadOnly Property ClientLabel As String
        Public ReadOnly Property Width As Double
        Public ReadOnly Property Height As Double
        Public ReadOnly Property MinWidth As Double
        Public ReadOnly Property MinHeight As Double

    End Class

    ''' <summary>
    ''' The three clients' MainWindow types share a bare name in different
    ''' namespaces, so each is constructed through a fully-qualified New()
    ''' rather than three conflicting Imports in one file.
    ''' </summary>
    Private Shared Function AllClientGeometries() As IReadOnlyList(Of WindowGeometry)

        Return New List(Of WindowGeometry) From {
            OnStaThread(Function()
                            Dim w As New Merchandising.Procurement.MainWindow()
                            Return New WindowGeometry("Procurement", w.Width, w.Height, w.MinWidth, w.MinHeight)
                        End Function),
            OnStaThread(Function()
                            Dim w As New Merchandising.Inventory.MainWindow()
                            Return New WindowGeometry("Inventory", w.Width, w.Height, w.MinWidth, w.MinHeight)
                        End Function),
            OnStaThread(Function()
                            Dim w As New Merchandising.POS.MainWindow()
                            Return New WindowGeometry("POS", w.Width, w.Height, w.MinWidth, w.MinHeight)
                        End Function)
        }

    End Function

    ''' <summary>Parses "| `PropertyName` | 1024 | 1092.8 | yes |" for PropertyName's declared column.</summary>
    Private Shared Function ExtractDeclaredValue(document As String, propertyName As String) As Double

        Dim pattern As String = "\|\s*`" & Regex.Escape(propertyName) & "`\s*\|\s*(\d+(?:\.\d+)?)\s*\|"
        Dim m As Match = Regex.Match(document, pattern)

        Assert.IsTrue(m.Success,
            $"docs/ui-specification.md's §3 table has no row for `{propertyName}` in the expected " &
            "'| `Name` | <value> | ... |' shape.")

        Return Double.Parse(m.Groups(1).Value, CultureInfo.InvariantCulture)

    End Function

    ' ------------------------------------------------------------------- tests

    ''' <summary>
    ''' The Phase 3 regression §3 names by story: all three clients must agree
    ''' on the same four numbers, and those are the numbers §3's table states.
    ''' A window resized without updating the document fails here.
    ''' </summary>
    <TestMethod>
    Public Sub AllThreeClients_DeclareTheSameGeometry_MatchingSection3Table()

        Dim document As String = ReadDocument()
        Dim geometries As IReadOnlyList(Of WindowGeometry) = AllClientGeometries()

        Dim documentedWidth As Double = ExtractDeclaredValue(document, "Width")
        Dim documentedHeight As Double = ExtractDeclaredValue(document, "Height")
        Dim documentedMinWidth As Double = ExtractDeclaredValue(document, "MinWidth")
        Dim documentedMinHeight As Double = ExtractDeclaredValue(document, "MinHeight")

        For Each g As WindowGeometry In geometries

            Assert.AreEqual(documentedWidth, g.Width, 0.001,
                $"{g.ClientLabel}'s MainWindow.Width is {g.Width}, but docs/ui-specification.md's §3 table declares {documentedWidth}.")
            Assert.AreEqual(documentedHeight, g.Height, 0.001,
                $"{g.ClientLabel}'s MainWindow.Height is {g.Height}, but docs/ui-specification.md's §3 table declares {documentedHeight}.")
            Assert.AreEqual(documentedMinWidth, g.MinWidth, 0.001,
                $"{g.ClientLabel}'s MainWindow.MinWidth is {g.MinWidth}, but docs/ui-specification.md's §3 table declares {documentedMinWidth}.")
            Assert.AreEqual(documentedMinHeight, g.MinHeight, 0.001,
                $"{g.ClientLabel}'s MainWindow.MinHeight is {g.MinHeight}, but docs/ui-specification.md's §3 table declares {documentedMinHeight}.")

        Next

    End Sub

    ''' <summary>
    ''' The 1092.8 x 576.0 DIP work area §3 states as a number must actually be
    ''' the result of the 1366x768 @ 125% arithmetic - recomputed here, not
    ''' copied from the document, so the two cannot silently diverge.
    ''' </summary>
    <TestMethod>
    Public Sub WorkAreaFigures_MatchTheDocumentedArithmetic()

        Const smallestWidthPhysical As Double = 1366
        Const smallestHeightPhysical As Double = 768
        Const smallestScaling As Double = 1.25
        Const taskbarPhysical As Double = 48

        Dim workAreaWidth As Double = smallestWidthPhysical / smallestScaling
        Dim workAreaHeight As Double = (smallestHeightPhysical - taskbarPhysical) / smallestScaling

        Assert.AreEqual(1092.8, workAreaWidth, 0.001)
        Assert.AreEqual(576.0, workAreaHeight, 0.001)

        Dim document As String = ReadDocument()

        StringAssert.Contains(document, "1092.8",
            "docs/ui-specification.md's §3 does not state the work-area width (1092.8 DIP) as a number.")
        StringAssert.Contains(document, "576.0",
            "docs/ui-specification.md's §3 does not state the work-area height (576.0 DIP) as a number.")

    End Sub

    ''' <summary>
    ''' §4's TabIndex convention (0/1/2 sign-in, 99 chrome) and §5's status-bar
    ''' bindings are asserted against all three MainWindow.xaml files as raw
    ''' source text - the same file-as-text technique
    ''' ApiSpecificationDocumentationTests.CrossCuttingErrorCodes_MatchTheLiveHandlers
    ''' uses for SessionAuthenticationHandler.vb.
    ''' </summary>
    <TestMethod>
    Public Sub EveryClientWindow_SourceMatchesTheDocumentedConventions()

        Dim document As String = ReadDocument()

        Dim windowPaths As String() = {
            Path.Combine("src", "Merchandising.Procurement", "MainWindow.xaml"),
            Path.Combine("src", "Merchandising.Inventory", "MainWindow.xaml"),
            Path.Combine("src", "Merchandising.POS", "MainWindow.xaml")
        }

        Dim chromeMarkers As String() = {
            "x:Name=""UsernameBox"" TabIndex=""0""",
            "x:Name=""PasswordBox"" TabIndex=""1""",
            "IsDefault=""True"" TabIndex=""2""",
            "x:Name=""SignOutButton""",
            "TabIndex=""99""",
            "{Binding StatusMessage}",
            "{Binding ResultText}",
            "HasCorrelationId",
            "{Binding CorrelationId, Mode=OneWay}",
            "IsInMaintenance",
            "{Binding MaintenanceMessage}",
            "#FFF3CD"
        }

        For Each relativePath As String In windowPaths

            Dim source As String = ReadRepoFile(relativePath)

            For Each marker As String In chromeMarkers
                StringAssert.Contains(source, marker,
                    $"'{relativePath}' no longer contains '{marker}' - docs/ui-specification.md §4/§5 document this as a convention every client's window follows.")
            Next

        Next

        For Each marker As String In New String() {
            "TabIndex", "99", "StatusMessage", "ResultText", "HasCorrelationId",
            "IsInMaintenance", "MaintenanceMessage", "#FFF3CD"
        }

            StringAssert.Contains(document, marker,
                $"docs/ui-specification.md does not mention '{marker}', which every client's MainWindow.xaml declares.")

        Next

    End Sub

    ''' <summary>
    ''' P5-16. §4 states, as a rule a fourth screen would be built from, that
    ''' access keys are not used anywhere in any of the three clients. The Phase 5
    ''' gate found that sentence true but unasserted - it would have survived a
    ''' screen that added a mnemonic tomorrow. POSLayoutTests asserts it over the
    ''' POS window's live logical tree; this asserts it over all three windows as
    ''' source text, which is the level the document makes the claim at.
    ''' </summary>
    <TestMethod>
    Public Sub NoClientWindow_AuthorsAnAccessKey_MatchingSection4sStatedConvention()

        Dim document As String = ReadDocument()

        StringAssert.Contains(document, "Access keys (Alt+letter mnemonics)",
            "docs/ui-specification.md §4 no longer states the access-key convention this test exists to hold it to.")

        ' A single underscore before a letter or digit inside a Content= or Header=
        ' attribute value declares a WPF access key. A doubled underscore is WPF's
        ' escape for a literal underscore and is deliberately not matched.
        Dim mnemonicAttribute As New Regex("(Content|Header)\s*=\s*""[^""]*(?<!_)_[A-Za-z0-9][^""]*""")

        Dim otherMechanisms As New Dictionary(Of String, Regex) From {
            {"a Label with a Target (an access key that forwards focus)", New Regex("<Label\b[^>]*\bTarget\s*=")},
            {"an explicit AccessText element", New Regex("<AccessText\b")},
            {"a KeyBinding gesture", New Regex("<KeyBinding\b|\.InputBindings>")}
        }

        Dim windowPaths As String() = {
            Path.Combine("src", "Merchandising.Procurement", "MainWindow.xaml"),
            Path.Combine("src", "Merchandising.Inventory", "MainWindow.xaml"),
            Path.Combine("src", "Merchandising.POS", "MainWindow.xaml")
        }

        Dim violations As New List(Of String)()

        For Each relativePath As String In windowPaths

            Dim source As String = ReadRepoFile(relativePath)

            For Each m As Match In mnemonicAttribute.Matches(source)
                violations.Add($"{relativePath}: {m.Value}")
            Next

            For Each mechanism As KeyValuePair(Of String, Regex) In otherMechanisms

                If mechanism.Value.IsMatch(source) Then
                    violations.Add($"{relativePath}: declares {mechanism.Key}.")
                End If

            Next

        Next

        Assert.IsEmpty(violations,
            "docs/ui-specification.md §4 states access keys are not used anywhere in any of the three clients. " &
            "These declare one, so either the XAML or that paragraph is now wrong:" & Environment.NewLine &
            String.Join(Environment.NewLine, violations))

    End Sub

    ''' <summary>
    ''' P5-16, and the reason this test exists is worth stating plainly. The
    ''' Phase 5 gate found §4 citing "evidence/phase-5/p5-13-pos-client.txt §2"
    ''' for a by-hand focus traversal; §2 of that file is a finding about a stale
    ''' Windows Service, and no traversal was recorded in it at all. A document
    ''' whose whole purpose is that a fourth screen could be built from it cannot
    ''' carry citations nobody checks, so every evidence path it names is now
    ''' resolved against disk, and the traversal transcripts are additionally
    ''' required to contain a traversal.
    ''' </summary>
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
            "docs/ui-specification.md cites no evidence file at all - §4's traversal claim is supposed to name one.")

        Dim missing As New List(Of String)()

        For Each cited As String In citedPaths

            If Not File.Exists(Path.Combine(root, cited.Replace("/"c, Path.DirectorySeparatorChar))) Then
                missing.Add(cited)
            End If

        Next

        Assert.IsEmpty(missing,
            "docs/ui-specification.md cites evidence files that do not exist on disk:" & Environment.NewLine &
            String.Join(Environment.NewLine, missing))

        ' Existing is not enough - the Phase 5 gate's finding was a file that
        ' existed and did not contain what it was cited for.
        Dim traversalTranscripts As String() = {
            "evidence/phase-3/p3-07-focus-order.txt",
            "evidence/phase-5/p5-16-inventory-focus-order.txt",
            "evidence/phase-5/p5-16-pos-focus-order.txt"
        }

        For Each transcript As String In traversalTranscripts

            Assert.Contains(transcript, citedPaths,
                $"docs/ui-specification.md §4 claims a live traversal was captured per client but no longer cites '{transcript}'.")

            Dim body As String = ReadRepoFile(transcript.Replace("/"c, Path.DirectorySeparatorChar))

            StringAssert.Contains(body, "LIVE FOCUS TRAVERSAL",
                $"'{transcript}' is cited by §4 as a live focus-traversal capture but contains no such section.")

            StringAssert.Contains(body, "MoveFocus",
                $"'{transcript}' records no MoveFocus walk, so it does not support the claim §4 cites it for.")

        Next

    End Sub

    ''' <summary>
    ''' §5's failure-presentation table quotes ApiFailurePresenter's two
    ''' client-authored sentences and its MaintenanceErrorCode constant
    ''' verbatim. A reworded sentence or a renamed code fails this test rather
    ''' than shipping a document that quotes wording the client no longer says.
    ''' </summary>
    <TestMethod>
    Public Sub ApiFailurePresenter_MatchesSection5sQuotedWording()

        Dim document As String = ReadDocument()
        Dim source As String =
            ReadRepoFile(Path.Combine("src", "Merchandising.ClientCommon", "Api", "ApiFailurePresenter.vb"))

        ' Read the constant's literal value out of the source text, rather than
        ' referencing ApiFailurePresenter.MaintenanceErrorCode directly - a Const
        ' reference is folded to a compile-time literal by the VB compiler, which
        ' makes an Assert against it trivially true (MSTEST0032) and proves
        ' nothing about what the source file actually declares.
        Dim constantMatch As Match = Regex.Match(source,
            "Public Const MaintenanceErrorCode As String = ""([^""]+)""")

        Assert.IsTrue(constantMatch.Success,
            "ApiFailurePresenter.vb no longer declares 'Public Const MaintenanceErrorCode As String = ""...""' in the expected shape.")

        Dim maintenanceErrorCode As String = constantMatch.Groups(1).Value

        Assert.AreEqual("MAINTENANCE_MODE", maintenanceErrorCode,
            "ApiFailurePresenter.MaintenanceErrorCode no longer reads MAINTENANCE_MODE - update docs/ui-specification.md §5 to match.")

        Dim quotedSentences As String() = {
            "The API could not be reached. Nothing was sent and nothing was saved.",
            "The system is under maintenance. Your change was not saved."
        }

        For Each sentence As String In quotedSentences

            StringAssert.Contains(source, sentence,
                $"ApiFailurePresenter.vb no longer contains the literal sentence '{sentence}'.")

            StringAssert.Contains(document, sentence,
                $"docs/ui-specification.md's §5 does not quote ApiFailurePresenter's own sentence '{sentence}'.")

        Next

        StringAssert.Contains(document, maintenanceErrorCode,
            "docs/ui-specification.md's §5 does not mention the live MaintenanceErrorCode value.")

    End Sub

    ''' <summary>The academic-prototype framing plan.md §5 requires on every Phase 5+ document.</summary>
    <TestMethod>
    Public Sub Document_StatesTheAcademicPrototypeFraming()

        Dim document As String = ReadDocument()

        StringAssert.Contains(document, "academic prototype",
            "docs/ui-specification.md does not state the academic-prototype framing plan.md §5 requires.")

        StringAssert.Contains(document, "XAMPP",
            "docs/ui-specification.md's scope note does not explain XAMPP is a course requirement.")

    End Sub

End Class
