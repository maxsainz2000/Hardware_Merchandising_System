' Merchandising.Tests.Unit.PurchaseOrderTransitionTests
'
' P3-01 / ADR-020. plan.md section 7's key design call for this phase:
' "encode the status machine as an explicit transition table in Domain with a
' single CanTransition function, tested exhaustively over all state x action
' pairs."
'
' Exhaustively means computed, not hand-listed. Every enumeration below walks
' [Enum].GetValues, so a state or action added to the enums grows this suite
' by itself rather than leaving a silent hole. The one deliberately
' hand-written list is ExpectedLegalTransitions - that is double-entry
' bookkeeping against PurchaseOrderTransitions.Table, not a duplicate of it:
' if the two ever disagree the suite fails, and neither one alone is trusted.
'
' Pure Domain - no database, no HTTP, no fixture (CLAUDE.md section 4).

Imports System.IO
Imports System.Text.RegularExpressions
Imports Merchandising.Domain.Procurement
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class PurchaseOrderTransitionTests

#Region "Helpers"

    Private Shared Function AllStates() As PurchaseOrderStatus()
        Return [Enum].GetValues(Of PurchaseOrderStatus)()
    End Function

    Private Shared Function AllActions() As PurchaseOrderAction()
        Return [Enum].GetValues(Of PurchaseOrderAction)()
    End Function

    ''' <summary>
    ''' The eleven legal transitions, written out longhand and independently
    ''' of the production table. Format: "From|Action|To".
    ''' </summary>
    Private Shared ReadOnly ExpectedLegalTransitions As String() = {
        "Draft|Submit|Submitted",
        "Draft|Cancel|Cancelled",
        "Submitted|Approve|Approved",
        "Submitted|Cancel|Cancelled",
        "Approved|Cancel|Cancelled",
        "Approved|ReceivePartially|PartiallyReceived",
        "Approved|ReceiveFully|FullyReceived",
        "PartiallyReceived|ReceivePartially|PartiallyReceived",
        "PartiallyReceived|ReceiveFully|FullyReceived",
        "PartiallyReceived|Close|Closed",
        "FullyReceived|Close|Closed"
    }

    Private Shared Function FindRepositoryRoot() As DirectoryInfo

        Dim current As DirectoryInfo = New DirectoryInfo(AppContext.BaseDirectory)

        While current IsNot Nothing
            If File.Exists(Path.Combine(current.FullName, "CLAUDE.md")) Then
                Return current
            End If
            current = current.Parent
        End While

        Assert.Fail(
            "Could not locate the repository root above '" & AppContext.BaseDirectory & "'.")
        Return Nothing

    End Function

    Private Shared Function NormalizeLineEndings(value As String) As String
        Return value.Replace(vbCrLf, vbLf)
    End Function

#End Region

#Region "The seven states"

    ''' <summary>
    ''' Done-when: all seven spec section 10.1 states modelled - including
    ''' PartiallyReceived and FullyReceived, which only Phase 4's receiving
    ''' command can drive. A table that omits them has to be rewritten next
    ''' phase.
    ''' </summary>
    <TestMethod>
    Public Sub Statuses_AreExactlyTheSevenSpecStates()

        Dim expected As String() = {
            "Draft", "Submitted", "Approved", "PartiallyReceived",
            "FullyReceived", "Cancelled", "Closed"
        }

        Dim actual As String() = [Enum].GetNames(Of PurchaseOrderStatus)()

        CollectionAssert.AreEquivalent(
            expected, actual,
            "Spec section 10.1 names exactly seven purchase-order statuses. Got: " &
            String.Join(", ", actual))

    End Sub

    ''' <summary>
    ''' The enum name is the stable identifier P3-02 stores, so a rename is a
    ''' storage-format change and an ordinal renumber must never be one. This
    ''' pins the numeric values so nobody reorders the enum casually.
    ''' </summary>
    <TestMethod>
    Public Sub StatusValues_AreStableAndExplicit()

        ' Driven from a lookup rather than written as literal-vs-literal
        ' comparisons: those fold to a compile-time constant, which MSTEST0032
        ' correctly reports as an assertion that always passes.
        Dim expectedValues As New Dictionary(Of String, Integer) From {
            {"Draft", 1},
            {"Submitted", 2},
            {"Approved", 3},
            {"PartiallyReceived", 4},
            {"FullyReceived", 5},
            {"Cancelled", 6},
            {"Closed", 7}
        }

        For Each state As PurchaseOrderStatus In AllStates()

            Dim name As String = state.ToString()

            Assert.Contains(
                name, expectedValues.Keys,
                $"Status '{name}' has no pinned numeric value. Add one deliberately.")

            Assert.AreEqual(
                expectedValues(name), CInt(state),
                $"Status '{name}' was renumbered. The enum name is what P3-02 stores, but a " &
                "casual reorder still deserves to fail loudly.")

        Next

    End Sub

#End Region

#Region "Exhaustive enumeration over state x action"

    ''' <summary>
    ''' Done-when: the test enumerates EVERY state x action pair, computed
    ''' from the enums. Seven states x six actions = forty-two decisions, and
    ''' CanTransition must decide every one of them - never throw, never
    ''' return an undecided result.
    ''' </summary>
    <TestMethod>
    Public Sub CanTransition_DecidesEveryStateActionPair()

        Dim decided As Integer = 0

        For Each fromState As PurchaseOrderStatus In AllStates()
            For Each action As PurchaseOrderAction In AllActions()

                Dim result As PurchaseOrderTransitionResult =
                    PurchaseOrderTransitions.CanTransition(fromState, action)

                Assert.IsNotNull(
                    result,
                    $"CanTransition({fromState}, {action}) returned Nothing. Every pair must be decided.")

                decided += 1

            Next
        Next

        Assert.AreEqual(
            AllStates().Length * AllActions().Length, decided,
            "Every state x action pair must be enumerated.")

        Assert.AreEqual(
            42, decided,
            "Seven states x six actions. If this number changed, a state or action was added - " &
            "extend ExpectedLegalTransitions and the ADR-020 table, do not just update this count.")

    End Sub

    ''' <summary>
    ''' Done-when: exactly the expected legal pairs are allowed, and every
    ''' one lands on the expected target state. The production table and
    ''' ExpectedLegalTransitions are written independently; this is where
    ''' they are reconciled.
    ''' </summary>
    <TestMethod>
    Public Sub CanTransition_AllowsExactlyTheExpectedTransitions()

        Dim expected As New HashSet(Of String)(ExpectedLegalTransitions, StringComparer.Ordinal)
        Dim observed As New HashSet(Of String)(StringComparer.Ordinal)

        For Each fromState As PurchaseOrderStatus In AllStates()
            For Each action As PurchaseOrderAction In AllActions()

                Dim result As PurchaseOrderTransitionResult =
                    PurchaseOrderTransitions.CanTransition(fromState, action)

                If result.IsAllowed Then
                    observed.Add($"{fromState}|{action}|{result.[To]}")
                End If

            Next
        Next

        Dim missing As String() = expected.Except(observed).OrderBy(Function(s) s).ToArray()
        Dim unexpected As String() = observed.Except(expected).OrderBy(Function(s) s).ToArray()

        Assert.IsEmpty(
            missing,
            "Expected legal transitions that the table refuses or routes elsewhere: " &
            String.Join(" ; ", missing))

        Assert.IsEmpty(
            unexpected,
            "Transitions the table allows that no rule permits: " & String.Join(" ; ", unexpected))

        Assert.HasCount(11, observed, "Eleven legal transitions, per ADR-020's table.")

    End Sub

    ''' <summary>
    ''' Done-when: each illegal pair names a stable error code (ADR-014), not
    ''' a boolean false. Codes are SCREAMING_SNAKE to match every code
    ''' already in the API - PRODUCT_NOT_FOUND, SUPPLIER_ALREADY_INACTIVE.
    ''' </summary>
    <TestMethod>
    Public Sub EveryRefusal_NamesAStableErrorCode()

        Dim knownCodes As New HashSet(Of String)(
            {
                PurchaseOrderTransitionErrors.Cancelled,
                PurchaseOrderTransitionErrors.Closed,
                PurchaseOrderTransitionErrors.FullyReceived,
                PurchaseOrderTransitionErrors.InvalidTransition
            }, StringComparer.Ordinal)

        Dim refusals As Integer = 0

        For Each fromState As PurchaseOrderStatus In AllStates()
            For Each action As PurchaseOrderAction In AllActions()

                Dim result As PurchaseOrderTransitionResult =
                    PurchaseOrderTransitions.CanTransition(fromState, action)

                If result.IsAllowed Then Continue For

                refusals += 1

                Assert.IsFalse(
                    String.IsNullOrWhiteSpace(result.ErrorCode),
                    $"({fromState}, {action}) was refused without an error code.")

                Assert.IsTrue(
                    Regex.IsMatch(result.ErrorCode, "^[A-Z][A-Z0-9_]*$"),
                    $"Error code '{result.ErrorCode}' is not in the project's SCREAMING_SNAKE form.")

                Assert.Contains(
                    result.ErrorCode, knownCodes,
                    $"({fromState}, {action}) named an unregistered error code '{result.ErrorCode}'. " &
                    "Every code must be a constant on PurchaseOrderTransitionErrors so the API " &
                    "specification (P3-08) can list it.")

            Next
        Next

        Assert.AreEqual(31, refusals, "Forty-two pairs minus eleven legal ones.")

    End Sub

    ''' <summary>
    ''' A refusal carries no target state. If a caller reads result.[To]
    ''' without checking IsAllowed, it must not get a plausible-looking
    ''' status back.
    ''' </summary>
    <TestMethod>
    Public Sub RefusedTransition_CarriesNoTargetState()

        For Each fromState As PurchaseOrderStatus In AllStates()
            For Each action As PurchaseOrderAction In AllActions()

                Dim result As PurchaseOrderTransitionResult =
                    PurchaseOrderTransitions.CanTransition(fromState, action)

                If Not result.IsAllowed Then
                    Assert.IsNull(
                        result.[To],
                        $"({fromState}, {action}) was refused but still reported a target state.")
                End If

            Next
        Next

    End Sub

    ''' <summary>
    ''' An allowed transition always names where it lands, and never lands on
    ''' the same terminal nowhere.
    ''' </summary>
    <TestMethod>
    Public Sub AllowedTransition_AlwaysNamesATargetStateAndNoErrorCode()

        For Each fromState As PurchaseOrderStatus In AllStates()
            For Each action As PurchaseOrderAction In AllActions()

                Dim result As PurchaseOrderTransitionResult =
                    PurchaseOrderTransitions.CanTransition(fromState, action)

                If result.IsAllowed Then

                    Assert.IsTrue(
                        result.[To].HasValue,
                        $"({fromState}, {action}) was allowed without a target state.")

                    Assert.IsTrue(
                        String.IsNullOrEmpty(result.ErrorCode),
                        $"({fromState}, {action}) was allowed but carried error code " &
                        $"'{result.ErrorCode}'.")

                End If

            Next
        Next

    End Sub

#End Region

#Region "The two rules the spec states in words"

    ''' <summary>
    ''' Done-when: "A cancelled order cannot proceed" is a row in the table,
    ''' not a comment. Spec section 10.1. Enumerated over every action, not
    ''' spot-checked - P3-05 leans on this being exhaustive.
    ''' </summary>
    <TestMethod>
    Public Sub CancelledOrder_RefusesEveryAction()

        For Each action As PurchaseOrderAction In AllActions()

            Dim result As PurchaseOrderTransitionResult =
                PurchaseOrderTransitions.CanTransition(PurchaseOrderStatus.Cancelled, action)

            Assert.IsFalse(
                result.IsAllowed,
                $"A cancelled order allowed '{action}'. Spec section 10.1: a cancelled order " &
                "cannot proceed.")

            Assert.AreEqual(
                PurchaseOrderTransitionErrors.Cancelled, result.ErrorCode,
                $"Refusing '{action}' on a cancelled order must say why it was refused.")

        Next

    End Sub

    ''' <summary>
    ''' Done-when: "a fully received order rejects further receiving" is a
    ''' row in the table. Spec section 10.1: "A fully received order cannot
    ''' receive additional quantity unless an authorized override policy is
    ''' later approved; the MVP default is to reject over-receiving."
    ''' </summary>
    <TestMethod>
    Public Sub FullyReceivedOrder_RefusesFurtherReceiving()

        For Each action As PurchaseOrderAction In {PurchaseOrderAction.ReceivePartially,
                                                   PurchaseOrderAction.ReceiveFully}

            Dim result As PurchaseOrderTransitionResult =
                PurchaseOrderTransitions.CanTransition(PurchaseOrderStatus.FullyReceived, action)

            Assert.IsFalse(
                result.IsAllowed,
                $"A fully received order allowed '{action}' - that is over-receiving.")

            Assert.AreEqual(
                PurchaseOrderTransitionErrors.FullyReceived, result.ErrorCode,
                "Over-receiving needs its own error code, distinct from a generic bad transition, " &
                "because spec section 10.1 anticipates a later override policy for exactly this case.")

        Next

    End Sub

    ''' <summary>
    ''' A closed order is terminal, the same way a cancelled one is.
    ''' </summary>
    <TestMethod>
    Public Sub ClosedOrder_RefusesEveryAction()

        For Each action As PurchaseOrderAction In AllActions()

            Dim result As PurchaseOrderTransitionResult =
                PurchaseOrderTransitions.CanTransition(PurchaseOrderStatus.Closed, action)

            Assert.IsFalse(result.IsAllowed, $"A closed order allowed '{action}'.")

            Assert.AreEqual(PurchaseOrderTransitionErrors.Closed, result.ErrorCode)

        Next

    End Sub

    ''' <summary>
    ''' ADR-020's two discretionary rows, asserted rather than left implicit:
    ''' cancellation stops at the first receipt, and closure requires one.
    ''' </summary>
    <TestMethod>
    Public Sub ReceivedOrder_CannotBeCancelled_AndUnreceivedOrder_CannotBeClosed()

        Assert.IsFalse(
            PurchaseOrderTransitions.CanTransition(
                PurchaseOrderStatus.PartiallyReceived, PurchaseOrderAction.Cancel).IsAllowed,
            "Once a receipt has written append-only StockMovements, the order is short-closed, " &
            "not cancelled (ADR-020).")

        Assert.IsFalse(
            PurchaseOrderTransitions.CanTransition(
                PurchaseOrderStatus.Approved, PurchaseOrderAction.Close).IsAllowed,
            "An approved order that will never be received is cancelled, not closed (ADR-020).")

        Assert.IsTrue(
            PurchaseOrderTransitions.CanTransition(
                PurchaseOrderStatus.PartiallyReceived, PurchaseOrderAction.Close).IsAllowed,
            "Short-closing a partially received order is the route ADR-020 leaves open.")

    End Sub

#End Region

#Region "One decision point, one artifact"

    ''' <summary>
    ''' Done-when: the evidence artifact is rendered from the table rather
    ''' than transcribed, so it cannot drift. Same generator/verifier pattern
    ''' as P2-02's role-permission matrix.
    ''' </summary>
    <TestMethod>
    Public Sub CommittedTransitionMatrixEvidence_MatchesRenderedTable()

        Dim evidencePath As String =
            Path.Combine(
                FindRepositoryRoot().FullName,
                "evidence", "phase-3", "p3-01-transition-matrix.txt")

        Assert.IsTrue(
            File.Exists(evidencePath),
            "evidence/phase-3/p3-01-transition-matrix.txt is missing. P3-01 owes a generated " &
            "artifact, not a described one.")

        Dim committed As String = NormalizeLineEndings(File.ReadAllText(evidencePath))
        Dim rendered As String = NormalizeLineEndings(PurchaseOrderTransitionMatrixFormatter.Render())

        Assert.AreEqual(
            rendered, committed,
            "The committed transition-matrix evidence has drifted from " &
            "PurchaseOrderTransitions.Table. Regenerate it from " &
            "PurchaseOrderTransitionMatrixFormatter.Render() rather than hand-editing.")

    End Sub

    ''' <summary>
    ''' Done-when: one CanTransition function is the only place a transition
    ''' is decided; no Select Case on status anywhere else in the solution.
    '''
    ''' What this scan catches: the idiomatic form - a `Select Case` line
    ''' that mentions a status - anywhere under src/ outside the transition
    ''' table itself. That is the shape plan.md section 7 warns about and the
    ''' shape a later controller would most plausibly grow.
    '''
    ''' What it does NOT catch, stated rather than implied: a chain of
    ''' `If order.Status = ...` comparisons, a Select Case over a local
    ''' aliased to something not named "status", or a decision made in SQL.
    ''' Those need review, not a regex. This is a lint, not a proof.
    ''' </summary>
    <TestMethod>
    Public Sub NoSelectCaseOnPurchaseOrderStatus_OutsideTheTransitionTable()

        Dim root As DirectoryInfo = FindRepositoryRoot()
        Dim sourceRoot As String = Path.Combine(root.FullName, "src")

        Dim offenders As New List(Of String)

        ' Named sourceFile, not file: Visual Basic is case-insensitive, so a
        ' local named "file" shadows System.IO.File for the whole loop body.
        For Each sourceFile As String In Directory.EnumerateFiles(sourceRoot, "*.vb", SearchOption.AllDirectories)

            Dim relative As String = Path.GetRelativePath(root.FullName, sourceFile)

            If relative.Contains(Path.DirectorySeparatorChar & "bin" & Path.DirectorySeparatorChar) OrElse
               relative.Contains(Path.DirectorySeparatorChar & "obj" & Path.DirectorySeparatorChar) Then
                Continue For
            End If

            Dim name As String = Path.GetFileName(sourceFile)

            ' The transition table is where the decision is supposed to live,
            ' and this test file legitimately names the type throughout.
            If String.Equals(name, "PurchaseOrderTransitions.vb", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(name, "PurchaseOrderTransitionTests.vb", StringComparison.OrdinalIgnoreCase) Then
                Continue For
            End If

            Dim lines As String() = File.ReadAllLines(sourceFile)

            For index As Integer = 0 To lines.Length - 1

                Dim line As String = lines(index)

                If line.IndexOf("Select Case", StringComparison.OrdinalIgnoreCase) >= 0 AndAlso
                   line.IndexOf("Status", StringComparison.OrdinalIgnoreCase) >= 0 Then

                    offenders.Add($"{relative}({index + 1}): {line.Trim()}")

                End If

            Next

        Next

        Assert.IsEmpty(
            offenders,
            "A status decision was found outside PurchaseOrderTransitions.CanTransition. " &
            "plan.md section 7: scattered status checks across controllers is how invalid " &
            "transitions leak in. Offenders:" & vbLf & String.Join(vbLf, offenders))

    End Sub

#End Region

End Class
