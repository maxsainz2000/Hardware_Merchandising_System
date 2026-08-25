' Merchandising.Domain.Procurement.PurchaseOrderAction
'
' What a caller can attempt against a purchase order. One action per command
' endpoint, so a controller names the action it is performing and asks
' PurchaseOrderTransitions.CanTransition rather than reasoning about status
' itself.
'
' WHY RECEIVING IS TWO ACTIONS AND NOT ONE. A single "Receive" from Approved
' could land in either PartiallyReceived or FullyReceived depending on
' quantity, which would stop the table being a function of (from, action) and
' force the caller to re-derive the target state - exactly the scattered
' decision plan.md section 7 warns about. Splitting it keeps CanTransition
' total and pure: Phase 4's receiving command computes the remaining quantity
' first, then names which of the two it is performing. See ADR-020.
'
' Domain depends on nothing (CLAUDE.md section 4).

Namespace Procurement

    ''' <summary>
    ''' An action that may be attempted against a purchase order.
    ''' </summary>
    Public Enum PurchaseOrderAction

        ''' <summary>Send a draft for approval. P3-04.</summary>
        Submit = 1

        ''' <summary>Approve a submitted order. P3-04; never by the requester (ADR-017 section 6).</summary>
        Approve = 2

        ''' <summary>Receive part of the outstanding quantity, leaving a remainder. Phase 4.</summary>
        ReceivePartially = 3

        ''' <summary>Receive the whole remaining outstanding quantity. Phase 4.</summary>
        ReceiveFully = 4

        ''' <summary>Abandon the order before any goods have been received. P3-05.</summary>
        Cancel = 5

        ''' <summary>Finish the order, accepting whatever has been received. P3-05.</summary>
        Close = 6

    End Enum

End Namespace
