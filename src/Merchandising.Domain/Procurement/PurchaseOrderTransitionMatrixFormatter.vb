' Merchandising.Domain.Procurement.PurchaseOrderTransitionMatrixFormatter
'
' Renders evidence/phase-3/p3-01-transition-matrix.txt from
' PurchaseOrderTransitions.Table. P3-01's evidence line asks for "one row per
' state x action pair, legal and illegal" - this is the generator half, and
' PurchaseOrderTransitionTests.CommittedTransitionMatrixEvidence_MatchesRenderedTable
' is the verifier: it renders again and asserts the committed file is
' unchanged, so a row edited in the table without regenerating the artifact
' fails the suite rather than shipping evidence that describes code that no
' longer exists. Same generator/verifier pattern as P2-02's role-permission
' matrix.
'
' Rows are enumerated from [Enum].GetValues, not from the dictionary, so the
' artifact lists the illegal pairs too - the ones with no row in the table are
' precisely the ones worth reading.
'
' Lines are joined with an explicit vbLf, never StringBuilder.AppendLine
' (which writes Environment.NewLine). A clean clone on another machine must
' render byte-for-byte the same content, or the equality test fails on line
' endings alone rather than on real drift. ASCII only, for the same reason.
'
' Pure string formatting, no I/O - Domain depends on nothing (CLAUDE.md
' section 4). Whoever regenerates the artifact writes Render()'s result to the
' evidence path directly.

Namespace Procurement

    ''' <summary>
    ''' Renders the full state x action transition matrix as plain text.
    ''' </summary>
    Public NotInheritable Class PurchaseOrderTransitionMatrixFormatter

        Private Sub New()
        End Sub

        Private Const FromColumnWidth As Integer = 20
        Private Const ActionColumnWidth As Integer = 20

        ''' <summary>
        ''' Renders one row per state x action pair, in enum order, legal and
        ''' illegal. Lines are joined with vbLf and the result ends with a
        ''' single trailing vbLf.
        ''' </summary>
        Public Shared Function Render() As String

            Dim states As PurchaseOrderStatus() = [Enum].GetValues(Of PurchaseOrderStatus)()
            Dim actions As PurchaseOrderAction() = [Enum].GetValues(Of PurchaseOrderAction)()

            Dim lines As New List(Of String)

            lines.Add("P3-01 - Purchase-order transition matrix")
            lines.Add("========================================")
            lines.Add(String.Empty)
            lines.Add("Generated from Merchandising.Domain.Procurement.PurchaseOrderTransitions.Table.")
            lines.Add("PurchaseOrderTransitionTests renders this content again and asserts this file is")
            lines.Add("unchanged - do not hand-edit it. Change the table and regenerate instead.")
            lines.Add(String.Empty)
            lines.Add("Spec section 10.1 states seven statuses and the rules in prose; ADR-020 records why")
            lines.Add("they are a table rather than scattered checks, and which rows Phase 4 drives.")
            lines.Add(String.Empty)
            lines.Add("Every pair below is decided. A pair with no row in the table is REFUSED, and names")
            lines.Add("the stable error code (ADR-014) a caller keys off - never a bare false.")
            lines.Add(String.Empty)

            lines.Add(
                "FROM".PadRight(FromColumnWidth) &
                "ACTION".PadRight(ActionColumnWidth) &
                "RESULT")
            lines.Add(
                New String("-"c, FromColumnWidth - 2).PadRight(FromColumnWidth) &
                New String("-"c, ActionColumnWidth - 2).PadRight(ActionColumnWidth) &
                New String("-"c, 40))

            Dim allowedCount As Integer = 0
            Dim refusedCount As Integer = 0

            For Each fromState As PurchaseOrderStatus In states

                For Each action As PurchaseOrderAction In actions

                    Dim result As PurchaseOrderTransitionResult =
                        PurchaseOrderTransitions.CanTransition(fromState, action)

                    Dim outcome As String

                    If result.IsAllowed Then
                        allowedCount += 1
                        outcome = "ALLOWED  -> " & result.[To].Value.ToString()
                    Else
                        refusedCount += 1
                        outcome = "REFUSED  " & result.ErrorCode
                    End If

                    lines.Add(
                        fromState.ToString().PadRight(FromColumnWidth) &
                        action.ToString().PadRight(ActionColumnWidth) &
                        outcome)

                Next

                lines.Add(String.Empty)

            Next

            lines.Add(
                "Totals: " & (states.Length * actions.Length).ToString() & " pairs (" &
                states.Length.ToString() & " states x " & actions.Length.ToString() & " actions), " &
                allowedCount.ToString() & " allowed, " & refusedCount.ToString() & " refused.")
            lines.Add(String.Empty)
            lines.Add("Phase 4 drives ReceivePartially and ReceiveFully; no endpoint exists for them yet.")
            lines.Add("The rows are present so that Phase 3's refusals are already complete.")

            Return String.Join(vbLf, lines) & vbLf

        End Function

    End Class

End Namespace
