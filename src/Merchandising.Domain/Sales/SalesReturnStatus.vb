' Merchandising.Domain.Sales.SalesReturnStatus
'
' Spec section 9: "policies for sensitive operations such as approvals,
' returns above a threshold, restores, and price changes" - a return within
' a cashier's permitted scope completes immediately (SalesReturns.Create);
' one above the configured threshold requires a second actor
' (SalesReturns.ApproveExceptional, ADR-017 section 6's self-approval veto,
' which only makes sense with a genuinely distinct requester and approver).
' Stock movements are append-only and irreversible, so an exceptional
' return's stock/refund effects cannot commit until approved - it lands
' PendingApproval with no effects yet, the same reason StockAdjustments
' (P4-03/P4-10) defers its movement/balance/audit triple to the Applied
' transition alone.
'
' ONLY THREE STATES, NOT FOUR. StockAdjustmentStatus names a fourth,
' "Approved, not yet Applied" - P4-10's own evidence
' (evidence/phase-4/p4-10-adjustments.txt SS0.3) records that state was
' never actually persisted: ApproveAsync writes Status = Applied directly,
' with ApprovedByUserId set in the same UPDATE. Knowing that going in, this
' enum does not name a state nothing will ever observe at rest - Completed
' is written directly, whichever path reached it.
'
' THE ENUM NAME IS THE STABLE IDENTIFIER (ADR-020's principle). The
' SalesReturnSchemaTests integration suite walks [Enum].GetNames on this
' enum and asserts every one is accepted by CK_SalesReturns_Status.
'
' Domain depends on nothing (CLAUDE.md section 4).

Namespace Sales

    ''' <summary>The three sales-return statuses spec section 9's threshold-based approval rule requires.</summary>
    Public Enum SalesReturnStatus

        ''' <summary>Above the configured threshold; awaiting SalesReturns.ApproveExceptional. No stock or refund effect yet.</summary>
        PendingApproval = 1

        ''' <summary>Committed - by direct creation (within the cashier's permitted scope) or by approval. Immutable from here.</summary>
        Completed = 2

        ''' <summary>Rejected by the approver. Never applied to stock; no refund recorded.</summary>
        Rejected = 3

    End Enum

End Namespace
