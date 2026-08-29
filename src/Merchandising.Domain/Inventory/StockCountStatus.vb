' Merchandising.Domain.Inventory.StockCountStatus
'
' Spec section 10.2: "Stock counts record the counted quantity, system
' quantity, variance, count session, counted by, reviewed by, reason, and
' approval state." P4-03 models all four states now, including Approved and
' Rejected which only a later card's review endpoint can drive - the same
' shape PurchaseOrderStatus (P3-02) used for PartiallyReceived/FullyReceived
' before Phase 4 could drive them, so this table is not rewritten when that
' endpoint is built.
'
' THE ENUM NAME IS THE STABLE IDENTIFIER (ADR-020's principle, applied here
' the same way P3-02 and P4-02 applied it). StockCountSchemaTests walks
' [Enum].GetNames on this enum and asserts every one of the four is accepted
' by CK_StockCounts_Status, so a status added later without a matching
' migration fails the suite rather than failing in production.
'
' Domain depends on nothing (CLAUDE.md section 4).

Namespace Inventory

    ''' <summary>
    ''' The four stock-count statuses of spec section 10.2's "approval state".
    ''' </summary>
    Public Enum StockCountStatus

        ''' <summary>Counting in progress. Counted quantities may still be recorded.</summary>
        Open = 1

        ''' <summary>Counting finished; the counted data is locked and immutable.</summary>
        Closed = 2

        ''' <summary>Reviewed and approved. ApprovedByUserId and ApprovedAtUtc are set.</summary>
        Approved = 3

        ''' <summary>Reviewed and rejected. No adjustment follows from this count.</summary>
        Rejected = 4

    End Enum

End Namespace
