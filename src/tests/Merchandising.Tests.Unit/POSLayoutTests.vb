' Merchandising.Tests.Unit.POSLayoutTests
'
' P5-13's own Done-when boxes: "the whole sale workflow is completable without
' a mouse, asserted by a test over TabIndex and access keys per screen" and
' "keyboard navigation and focus order work at 1366x768 and 125% scaling -
' asserted by a test... extending ProcurementLayoutTests / InventoryLayoutTests'
' three assertions to this window." This suite is that extension, adapted
' line-for-line from InventoryLayoutTests (P4-12) against
' Merchandising.POS.MainWindow instead.
'
' Phase 3's own closure note names the mistake this guards against: a window
' whose declared MinHeight exceeded the work area it was supposedly fitted
' to, discovered only because the arithmetic was written down and never
' compared. The three assertions below are that comparison, run as code.
'
' Nothing here shows a window. Measure/Arrange on the content tree is enough
' for a layout assertion and keeps the suite runnable on a machine with no
' interactive desktop session - which matters because /orchestrate dispatches
' this suite to box3 (ProcurementLayoutTests' own header).

Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Controls.Primitives
Imports Merchandising.ClientCommon.Api
Imports Merchandising.POS
Imports Merchandising.POS.ViewModels
Imports Microsoft.VisualStudio.TestTools.UnitTesting

''' <summary>
''' Layout and focus-order acceptance for the POS client's one window,
''' against the smallest desktop the spec supports.
''' </summary>
<TestClass>
<DoNotParallelize>
Public NotInheritable Class POSLayoutTests

    ' --------------------------------------------------------------- the budget

    ''' <summary>The smallest desktop spec section 5 / 16 require the client to work on.</summary>
    Private Const SmallestWidthPhysical As Double = 1366
    Private Const SmallestHeightPhysical As Double = 768
    Private Const SmallestScaling As Double = 1.25

    ''' <summary>
    ''' A default Windows 11 taskbar is 48 physical pixels tall. At 125% that is
    ''' 38.4 DIP, and it is subtracted because a window may not open underneath it.
    ''' </summary>
    Private Const TaskbarPhysical As Double = 48

    ''' <summary>
    ''' Caption plus resize border, in DIP. Deliberately a constant rather than
    ''' SystemParameters.WindowCaptionHeight: this is a budget the XAML must fit
    ''' inside, so it must not shrink because the machine running the test happens
    ''' to use a compact theme. 40x16 is comfortably above the standard 31 + 8.
    ''' </summary>
    Private Const ChromeHeight As Double = 40
    Private Const ChromeWidth As Double = 16

    Private Shared ReadOnly Property WorkAreaWidth As Double
        Get
            Return SmallestWidthPhysical / SmallestScaling
        End Get
    End Property

    Private Shared ReadOnly Property WorkAreaHeight As Double
        Get
            Return (SmallestHeightPhysical - TaskbarPhysical) / SmallestScaling
        End Get
    End Property

    ' ------------------------------------------------------------------- tests

    ''' <summary>
    ''' The Phase 3 regression this card was warned not to repeat. MinHeight is
    ''' the value that matters: a window whose minimum exceeds the work area
    ''' cannot be shrunk to fit, so its bottom row leaves the screen permanently.
    ''' </summary>
    <TestMethod>
    Public Sub Window_DeclaredSizes_FitTheSmallestSupportedDesktop()

        Dim declared As WindowSizes = OnStaThread(
            Function()
                Dim window As New MainWindow()
                Return New WindowSizes(window.Width, window.Height, window.MinWidth, window.MinHeight)
            End Function)

        Assert.IsLessThanOrEqualTo(WorkAreaHeight, declared.MinHeight,
                                   Describe("MinHeight", declared.MinHeight, WorkAreaHeight) &
                                   " A minimum taller than the work area can never be resized to fit.")

        Assert.IsLessThanOrEqualTo(WorkAreaHeight, declared.Height,
                                   Describe("Height", declared.Height, WorkAreaHeight) &
                                   " The window would open with its lower edge off-screen.")

        Assert.IsLessThanOrEqualTo(WorkAreaWidth, declared.MinWidth,
                                   Describe("MinWidth", declared.MinWidth, WorkAreaWidth))

        Assert.IsLessThanOrEqualTo(WorkAreaWidth, declared.Width,
                                   Describe("Width", declared.Width, WorkAreaWidth))

    End Sub

    ''' <summary>
    ''' Fitting on screen is not the same as being usable once there. Every screen
    ''' is laid out at the window's own minimum size and every data grid on it must
    ''' still be tall enough to show a header and a row.
    ''' </summary>
    <TestMethod>
    Public Async Function EveryScreen_AtMinimumWindowSize_LaysOutWithoutStarvingAGrid() As Task

        Dim signedIn As MainViewModel = Await SignedInViewModelAsync()

        Dim failures As List(Of String) = OnStaThread(
            Function()

                Dim found As New List(Of String)()

                Dim window As New MainWindow()

                window.DataContext = signedIn

                Dim root As FrameworkElement = CType(window.Content, FrameworkElement)
                Dim tabs As TabControl = Descendants(window).OfType(Of TabControl)().Single()

                Dim clientWidth As Double = window.MinWidth - ChromeWidth
                Dim clientHeight As Double = window.MinHeight - ChromeHeight

                For index As Integer = 0 To tabs.Items.Count - 1

                    Dim item As TabItem = CType(tabs.Items(index), TabItem)
                    Dim header As String = item.Header.ToString()

                    tabs.SelectedIndex = index

                    root.Measure(New Size(clientWidth, clientHeight))
                    root.Arrange(New Rect(0, 0, clientWidth, clientHeight))
                    root.UpdateLayout()

                    If root.DesiredSize.Height > clientHeight + Tolerance Then
                        found.Add(String.Format(CultureInfo.InvariantCulture,
                                                "Tab '{0}': content wants {1:0.0} DIP of height but only {2:0.0} is available - it would be clipped.",
                                                header, root.DesiredSize.Height, clientHeight))
                    End If

                    ' Only this tab's own subtree: a TabControl arranges the selected
                    ' item alone, and a deselected tab's controls keep whatever size
                    ' they last had, which would be read as a live measurement.
                    For Each grid As DataGrid In Descendants(item).OfType(Of DataGrid)()

                        If grid.ActualHeight < MinimumUsableGridHeight Then
                            found.Add(String.Format(CultureInfo.InvariantCulture,
                                                    "Tab '{0}': the '{1}' grid arranged to {2:0.0} DIP, below the {3:0.0} DIP that shows a header plus one row.",
                                                    header, FirstColumnOf(grid), grid.ActualHeight, MinimumUsableGridHeight))
                        End If

                    Next

                Next

                Return found

            End Function)

        Assert.IsEmpty(failures,
                       "Layout at the window's minimum size starves content:" & Environment.NewLine &
                       String.Join(Environment.NewLine, failures))

    End Function

    ''' <summary>
    ''' Focus order. WPF visits tab stops in TabIndex order, so the authored
    ''' numbers ARE the focus order - which makes three things worth asserting and
    ''' one worth stating. Asserted: every interactive control carries an explicit
    ''' TabIndex, no two controls share one, and within a screen they ascend in the
    ''' order the XAML declares them, so reading order and Tab order agree. Not
    ''' asserted here: that a person pressing Tab sees what these numbers predict -
    ''' that needs a shown window and is captured once, by hand, in
    ''' evidence/phase-5/p5-13-pos-client.txt.
    ''' </summary>
    <TestMethod>
    Public Sub EveryInteractiveControl_HasAUniqueTabIndex_AscendingInReadingOrder()

        Dim stops As List(Of TabStop) = OnStaThread(
            Function()
                Dim window As New MainWindow()
                Return CollectTabStops(window)
            End Function)

        Assert.IsNotEmpty(stops, "No interactive controls were found - the walk itself is broken.")

        Dim unnumbered = stops.Where(Function(s) s.TabIndex = Integer.MaxValue).ToList()
        Assert.IsEmpty(unnumbered,
                       "These controls are keyboard-reachable but carry no explicit TabIndex, so their focus position is whatever the tree happens to yield:" &
                       Environment.NewLine & String.Join(Environment.NewLine, unnumbered.Select(Function(s) s.Describe())))

        Dim duplicates = stops.GroupBy(Function(s) s.TabIndex).Where(Function(g) g.Count() > 1).ToList()
        Assert.IsEmpty(duplicates,
                       "Two controls share a TabIndex, which leaves their relative focus order undefined:" &
                       Environment.NewLine &
                       String.Join(Environment.NewLine, duplicates.Select(Function(g) g.Key.ToString(CultureInfo.InvariantCulture) & ": " & String.Join(", ", g.Select(Function(s) s.Describe())))))

        For Each screen In stops.GroupBy(Function(s) s.Screen)

            Dim inOrder = screen.ToList()

            For position As Integer = 1 To inOrder.Count - 1

                Assert.IsGreaterThan(inOrder(position - 1).TabIndex, inOrder(position).TabIndex,
                                     String.Format(CultureInfo.InvariantCulture,
                                                   "On screen '{0}', Tab order disagrees with reading order: {1} is declared after {2} but has the lower TabIndex.",
                                                   screen.Key, inOrder(position).Describe(), inOrder(position - 1).Describe()))

            Next

        Next

    End Sub

    ' ----------------------------------------------------------------- helpers

    Private Const Tolerance As Double = 0.5

    ''' <summary>A column header plus one row of a default-styled DataGrid.</summary>
    Private Const MinimumUsableGridHeight As Double = 48

    Private NotInheritable Class WindowSizes

        Public Sub New(width As Double, height As Double, minWidth As Double, minHeight As Double)
            Me.Width = width
            Me.Height = height
            Me.MinWidth = minWidth
            Me.MinHeight = minHeight
        End Sub

        Public ReadOnly Property Width As Double
        Public ReadOnly Property Height As Double
        Public ReadOnly Property MinWidth As Double
        Public ReadOnly Property MinHeight As Double

    End Class

    Private NotInheritable Class TabStop

        Public Sub New(screen As String, kind As String, label As String, tabIndex As Integer)
            Me.Screen = screen
            Me.Kind = kind
            Me.Label = label
            Me.TabIndex = tabIndex
        End Sub

        Public ReadOnly Property Screen As String
        Public ReadOnly Property Kind As String
        Public ReadOnly Property Label As String
        Public ReadOnly Property TabIndex As Integer

        Public Function Describe() As String

            Return String.Format(CultureInfo.InvariantCulture, "{0} '{1}' (TabIndex {2})",
                                 Kind, Label,
                                 If(TabIndex = Integer.MaxValue, "unset", TabIndex.ToString(CultureInfo.InvariantCulture)))

        End Function

    End Class

    ''' <summary>
    ''' Names a grid the way a reader would - by its first column header - since
    ''' none of them carry an x:Name and "a DataGrid" does not say which one.
    ''' </summary>
    Private Shared Function FirstColumnOf(grid As DataGrid) As String

        If grid.Columns.Count = 0 OrElse grid.Columns(0).Header Is Nothing Then
            Return "<unnamed>"
        End If

        Return grid.Columns(0).Header.ToString()

    End Function

    Private Shared Function Describe(name As String, declared As Double, budget As Double) As String

        Return String.Format(CultureInfo.InvariantCulture,
                             "MainWindow {0} is {1:0.0} DIP but the {2:0}x{3:0} @ {4:0}% work area is only {5:0.0} DIP.",
                             name, declared, SmallestWidthPhysical, SmallestHeightPhysical,
                             SmallestScaling * 100, budget)

    End Function

    ''' <summary>
    ''' A MainViewModel that has actually signed in, so the workspace tabs are
    ''' visible and can be laid out. The transport is stubbed; every other line of
    ''' the client is the real one, the same posture InventoryClientTests takes.
    ''' Signed in as "cashier1" - spec section 9 assigns this client to the
    ''' Cashier role - not that the role matters for a pure layout pass.
    ''' </summary>
    Private Shared Async Function SignedInViewModelAsync() As Task(Of MainViewModel)

        Dim handler As New StubHttpMessageHandler(
            Function(request)

                Dim response As New HttpResponseMessage(HttpStatusCode.OK) With {
                    .Content = New StringContent(LoginJson(), Encoding.UTF8, "application/json")
                }

                response.Headers.TryAddWithoutValidation(MerchandisingApiClient.CorrelationIdHeaderName, Guid.NewGuid().ToString("D"))

                Return response

            End Function)

        Dim client As New MerchandisingApiClient(New Uri(MainViewModel.DefaultApiAddress), handler, TimeSpan.FromSeconds(5))

        Dim viewModel As New MainViewModel(client)
        viewModel.Username = "cashier1"

        Await viewModel.SignInAsync("password")

        Assert.IsTrue(viewModel.IsSignedIn, "The layout fixture could not reach the signed-in state, so no tab would be visible to measure.")

        Return viewModel

    End Function

    Private Shared Function LoginJson() As String

        Return JsonSerializer.Serialize(New With {
            .token = "P5-13-LAYOUT-FIXTURE-TOKEN",
            .expiresAtUtc = DateTime.UtcNow.AddHours(8),
            .username = "cashier1",
            .roles = New String() {"Cashier"}
        })

    End Function

    ''' <summary>
    ''' Walks the LOGICAL tree, which - unlike the visual tree - contains every
    ''' TabItem's content whether or not that tab has ever been selected. That is
    ''' what lets one pass see all three screens without realizing any of them.
    ''' </summary>
    Private Shared Function CollectTabStops(window As MainWindow) As List(Of TabStop)

        Dim found As New List(Of TabStop)()

        Walk(window, "Chrome", found)

        Return found

    End Function

    Private Shared Sub Walk(node As DependencyObject, screen As String, into As List(Of TabStop))

        If node Is Nothing Then
            Return
        End If

        Dim currentScreen As String = screen

        Dim tab As TabItem = TryCast(node, TabItem)
        If tab IsNot Nothing AndAlso tab.Header IsNot Nothing Then
            currentScreen = tab.Header.ToString()
        End If

        Dim element As FrameworkElement = TryCast(node, FrameworkElement)
        If element IsNot Nothing AndAlso String.Equals(element.Name, "SignInPanel", StringComparison.Ordinal) Then
            currentScreen = "Sign in"
        End If

        Dim control As Control = TryCast(node, Control)
        If control IsNot Nothing AndAlso IsInteractive(control) Then
            into.Add(New TabStop(currentScreen, control.GetType().Name, LabelOf(control), control.TabIndex))
        End If

        For Each child As Object In LogicalTreeHelper.GetChildren(node)

            Dim childNode As DependencyObject = TryCast(child, DependencyObject)
            If childNode IsNot Nothing Then
                Walk(childNode, currentScreen, into)
            End If

        Next

    End Sub

    ''' <summary>
    ''' The control types this window uses that a keyboard user lands on. Labels,
    ''' group boxes, borders and the TabControl's own headers are excluded - the
    ''' first three are not tab stops and the last is navigated with the arrow keys.
    ''' CheckBox is caught here too - it derives from ButtonBase.
    ''' </summary>
    Private Shared Function IsInteractive(control As Control) As Boolean

        Return TypeOf control Is TextBox OrElse
               TypeOf control Is PasswordBox OrElse
               TypeOf control Is ButtonBase OrElse
               TypeOf control Is ComboBox OrElse
               TypeOf control Is DataGrid

    End Function

    Private Shared Function LabelOf(control As Control) As String

        If Not String.IsNullOrEmpty(control.Name) Then
            Return control.Name
        End If

        Dim content As ContentControl = TryCast(control, ContentControl)
        If content IsNot Nothing AndAlso content.Content IsNot Nothing Then
            Return content.Content.ToString()
        End If

        If Not String.IsNullOrEmpty(control.ToolTip?.ToString()) Then
            Return control.ToolTip.ToString()
        End If

        Return "<unnamed>"

    End Function

    Private Shared Function Descendants(root As DependencyObject) As IEnumerable(Of DependencyObject)

        Dim found As New List(Of DependencyObject)()

        CollectDescendants(root, found)

        Return found

    End Function

    Private Shared Sub CollectDescendants(node As DependencyObject, into As List(Of DependencyObject))

        For Each child As Object In LogicalTreeHelper.GetChildren(node)

            Dim childNode As DependencyObject = TryCast(child, DependencyObject)
            If childNode IsNot Nothing Then
                into.Add(childNode)
                CollectDescendants(childNode, into)
            End If

        Next

    End Sub

    ''' <summary>
    ''' Runs WPF work on a dedicated STA thread. MSTest gives no apartment
    ''' guarantee, and constructing a Window from an MTA thread throws.
    ''' </summary>
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
            Throw New InvalidOperationException("The STA layout pass threw: " & captured.Message, captured)
        End If

        Return result

    End Function

End Class
