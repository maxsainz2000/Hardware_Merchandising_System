' Merchandising.Domain.Procurement.PurchaseOrderStatus
'
' Spec section 10.1: "Purchase-order statuses are Draft, Submitted, Approved,
' PartiallyReceived, FullyReceived, Cancelled, and Closed." All seven are
' modelled here, in Phase 3, including the two that only Phase 4's receiving
' command can drive - a table that omitted them would have to be rewritten
' next phase, and every Phase 3 refusal that mentions them would be missing.
'
' THE ENUM NAME IS THE STABLE IDENTIFIER. P3-02 stores the name
' ("PartiallyReceived"), never the ordinal, so a state added later cannot
' renumber stored rows. The explicit numeric values below exist to make that
' promise visible and to keep a casual reorder from being silent - they are
' not what goes in the database.
'
' Domain depends on nothing (CLAUDE.md section 4).

Namespace Procurement

    ''' <summary>
    ''' The seven purchase-order statuses of spec section 10.1.
    ''' </summary>
    Public Enum PurchaseOrderStatus

        ''' <summary>Created, editable, not yet sent for approval.</summary>
        Draft = 1

        ''' <summary>Sent for approval. Awaiting an approver who is not the requester.</summary>
        Submitted = 2

        ''' <summary>Approved and ready to receive against. Nothing received yet.</summary>
        Approved = 3

        ''' <summary>Some ordered quantity received; a remainder is still outstanding. Phase 4 drives this.</summary>
        PartiallyReceived = 4

        ''' <summary>The full ordered quantity has been received. Phase 4 drives this.</summary>
        FullyReceived = 5

        ''' <summary>Abandoned before any goods were received. Terminal.</summary>
        Cancelled = 6

        ''' <summary>Receiving is finished or deliberately stopped short. Terminal.</summary>
        Closed = 7

    End Enum

End Namespace
