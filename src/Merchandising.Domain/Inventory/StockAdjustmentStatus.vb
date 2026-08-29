' Merchandising.Domain.Inventory.StockAdjustmentStatus
'
' Spec section 10.2: "Adjustment approval is required when the absolute
' variance exceeds the configured threshold." P4-03 models the whole
' lifecycle now, including states only P4-10's approval endpoint can drive -
' the same PurchaseOrderStatus/StockCountStatus precedent.
'
' Pending and Applied are deliberately separate from Approved: docs/adr.md's
' ADR-017 section 6 already fixes StockAdjustment as an IOwnershipResource
' compared for self-approval, and P4-10's card text distinguishes "applies
' directly" (below threshold, no approval step) from "requires a second
' person's approval" (at or above it) - both paths end at Applied, but only
' the second one passes through Approved first. Rejected never reaches
' Applied; CLAUDE.md section 5's "every stock-changing operation is atomic"
' means the movement/balance/audit triple writes only at the Applied
' transition, never at Pending or Approved alone.
'
' THE ENUM NAME IS THE STABLE IDENTIFIER (ADR-020's principle).
' StockCountSchemaTests walks [Enum].GetNames on this enum and asserts every
' one of the four is accepted by CK_StockAdjustments_Status.
'
' Domain depends on nothing (CLAUDE.md section 4).

Namespace Inventory

    ''' <summary>
    ''' The four stock-adjustment statuses spec section 10.2's
    ''' threshold-based approval rule requires.
    ''' </summary>
    Public Enum StockAdjustmentStatus

        ''' <summary>At or above the configured threshold; awaiting a second person's approval.</summary>
        Pending = 1

        ''' <summary>Approved by someone other than the requester. Not yet applied to stock.</summary>
        Approved = 2

        ''' <summary>Rejected. Never applied to stock.</summary>
        Rejected = 3

        ''' <summary>Applied to stock - the movement/balance/audit triple has committed.</summary>
        Applied = 4

    End Enum

End Namespace
