' Merchandising.Domain.Procurement.PurchaseOrderTransitions
'
' P3-01 / ADR-020. plan.md section 7's key design call for Phase 3:
'
'   "encode the status machine as an explicit transition table in Domain with
'    a single CanTransition function, tested exhaustively over all state x
'    action pairs. Scattered `If status = ...` checks across controllers is
'    how invalid transitions leak in."
'
' THIS FILE IS THE ONLY PLACE A PURCHASE-ORDER TRANSITION IS DECIDED. Every
' controller, repository and client asks CanTransition and obeys the answer.
' A `Select Case` on a purchase-order status anywhere else under src/ fails
' PurchaseOrderTransitionTests.NoSelectCaseOnPurchaseOrderStatus_OutsideTheTransitionTable.
'
' The table is closed: eleven legal transitions out of forty-two state x
' action pairs. Anything absent from Table is refused, so adding a state or an
' action defaults to "refuse everything" rather than to "allow silently" - the
' safe direction for a table that governs money and stock.
'
' Phase 4 drives exactly two of the seven states. ReceivePartially and
' ReceiveFully have no endpoint yet; the rows exist now so that Phase 3's
' refusals are complete (a fully received order must already reject further
' receiving) and Phase 4 adds a controller rather than rewriting this table.
'
' Pure Domain - no database, no ASP.NET Core, no I/O (CLAUDE.md section 4).

Namespace Procurement

    ''' <summary>
    ''' The purchase-order status machine of spec section 10.1, as an explicit
    ''' table with a single decision function.
    ''' </summary>
    Public NotInheritable Class PurchaseOrderTransitions

        Private Sub New()
        End Sub

        Private Shared ReadOnly _table As IReadOnlyDictionary(Of (PurchaseOrderStatus, PurchaseOrderAction), PurchaseOrderStatus) = BuildTable()

        ''' <summary>
        ''' Every legal transition, keyed by (current status, attempted
        ''' action) and valued by the status the order moves to. A pair absent
        ''' from this dictionary is illegal.
        ''' </summary>
        Public Shared ReadOnly Property Table As IReadOnlyDictionary(Of (PurchaseOrderStatus, PurchaseOrderAction), PurchaseOrderStatus)
            Get
                Return _table
            End Get
        End Property

        Private Shared Function BuildTable() As IReadOnlyDictionary(Of (PurchaseOrderStatus, PurchaseOrderAction), PurchaseOrderStatus)

            Dim table As New Dictionary(Of (PurchaseOrderStatus, PurchaseOrderAction), PurchaseOrderStatus)

            ' --- Draft ------------------------------------------------------
            ' Editable until submitted. Cancelling a draft costs nothing.
            table.Add((PurchaseOrderStatus.Draft, PurchaseOrderAction.Submit), PurchaseOrderStatus.Submitted)
            table.Add((PurchaseOrderStatus.Draft, PurchaseOrderAction.Cancel), PurchaseOrderStatus.Cancelled)

            ' --- Submitted --------------------------------------------------
            ' Awaiting approval by someone other than the requester; the
            ' self-approval veto is ADR-017 section 6's, applied at P3-04, not
            ' a rule this table can express.
            table.Add((PurchaseOrderStatus.Submitted, PurchaseOrderAction.Approve), PurchaseOrderStatus.Approved)
            table.Add((PurchaseOrderStatus.Submitted, PurchaseOrderAction.Cancel), PurchaseOrderStatus.Cancelled)

            ' --- Approved ---------------------------------------------------
            ' Receivable, and still cancellable because no goods exist yet.
            ' Deliberately NOT closable: an approved order that will never be
            ' received is cancelled, so that the two terminal states keep
            ' distinct meanings in the spec section 14 history report
            ' (Closed = goods came in, Cancelled = they never did).
            table.Add((PurchaseOrderStatus.Approved, PurchaseOrderAction.Cancel), PurchaseOrderStatus.Cancelled)
            table.Add((PurchaseOrderStatus.Approved, PurchaseOrderAction.ReceivePartially), PurchaseOrderStatus.PartiallyReceived)
            table.Add((PurchaseOrderStatus.Approved, PurchaseOrderAction.ReceiveFully), PurchaseOrderStatus.FullyReceived)

            ' --- PartiallyReceived (Phase 4 drives the two receiving rows) ---
            ' Receiving accumulates: a partial receipt that still leaves a
            ' remainder is a self-transition, which is why the row exists at
            ' all rather than being implied by "no change".
            ' Deliberately NOT cancellable: a receipt has already written
            ' append-only StockMovements (CLAUDE.md section 5), and cancelling
            ' would imply undoing stock that can never be undone. The route
            ' for abandoning the remainder is Close - a short-close.
            table.Add((PurchaseOrderStatus.PartiallyReceived, PurchaseOrderAction.ReceivePartially), PurchaseOrderStatus.PartiallyReceived)
            table.Add((PurchaseOrderStatus.PartiallyReceived, PurchaseOrderAction.ReceiveFully), PurchaseOrderStatus.FullyReceived)
            table.Add((PurchaseOrderStatus.PartiallyReceived, PurchaseOrderAction.Close), PurchaseOrderStatus.Closed)

            ' --- FullyReceived ----------------------------------------------
            ' Closing is the only move left. Further receiving is refused with
            ' its own code - spec section 10.1's over-receiving rule.
            table.Add((PurchaseOrderStatus.FullyReceived, PurchaseOrderAction.Close), PurchaseOrderStatus.Closed)

            ' --- Cancelled, Closed ------------------------------------------
            ' Terminal. No rows, deliberately: absence from this table IS the
            ' rule, and RefusalCodeFor turns it into a specific error code.

            Return table

        End Function

        ''' <summary>
        ''' Decides whether <paramref name="action"/> may be performed on an
        ''' order currently in <paramref name="fromState"/>.
        ''' </summary>
        ''' <returns>
        ''' An allowed result naming the status the order moves to, or a
        ''' refusal naming a stable error code (ADR-014). Never throws for an
        ''' unknown pair - every pair is decided.
        ''' </returns>
        Public Shared Function CanTransition(fromState As PurchaseOrderStatus,
                                             action As PurchaseOrderAction) As PurchaseOrderTransitionResult

            Dim target As PurchaseOrderStatus

            If _table.TryGetValue((fromState, action), target) Then
                Return PurchaseOrderTransitionResult.Allowed(target)
            End If

            Return PurchaseOrderTransitionResult.Refused(RefusalCodeFor(fromState, action))

        End Function

        ''' <summary>
        ''' Which of the four stable codes a refusal reports. Three of them
        ''' exist because a caller has to tell the cases apart: a cancelled or
        ''' closed order will never accept anything again, whereas
        ''' over-receiving is the one refusal spec section 10.1 anticipates an
        ''' override policy for later.
        ''' </summary>
        Private Shared Function RefusalCodeFor(fromState As PurchaseOrderStatus,
                                               action As PurchaseOrderAction) As String

            If fromState = PurchaseOrderStatus.Cancelled Then
                Return PurchaseOrderTransitionErrors.Cancelled
            End If

            If fromState = PurchaseOrderStatus.Closed Then
                Return PurchaseOrderTransitionErrors.Closed
            End If

            If fromState = PurchaseOrderStatus.FullyReceived AndAlso IsReceiving(action) Then
                Return PurchaseOrderTransitionErrors.FullyReceived
            End If

            Return PurchaseOrderTransitionErrors.InvalidTransition

        End Function

        ''' <summary>True for the two actions Phase 4's receiving command performs.</summary>
        Public Shared Function IsReceiving(action As PurchaseOrderAction) As Boolean

            Return action = PurchaseOrderAction.ReceivePartially OrElse
                   action = PurchaseOrderAction.ReceiveFully

        End Function

    End Class

End Namespace
