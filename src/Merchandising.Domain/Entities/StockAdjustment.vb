' Merchandising.Domain.Entities.StockAdjustment
'
' Plain data holder for a StockAdjustments row
' (db/migrations/0010_counts-and-adjustments.sql), matching PurchaseOrder.vb's
' shape - Domain depends on nothing (CLAUDE.md section 4).
'
' Status is the DOMAIN ENUM, not the stored string - the same ADR-020
' reasoning PurchaseOrder.vb's header gives: 0010 stores the enum NAME under
' COLLATE utf8mb4_bin and a CHECK constraint, so the only values that can
' reach this property are the four StockAdjustmentStatus names.
'
' ProductSku/ProductName are PROJECTIONS, joined by the repository on read -
' the same arrangement PurchaseOrderLine.ProductSku/ProductName use.
'
' IMPLEMENTS IOwnershipResource (ADR-017 section 6, P4-10 applying it -
' "reusing ADR-017 section 6's IOwnershipResource / AuthorizeAsync
' mechanism, not a new check", the card's own Done-when box 2). Nothing
' extra to wire: RequestedByUserId already exists for the response mapping,
' the same way PurchaseOrder.vb's header describes for PurchaseOrders.Approve.

Imports Merchandising.Domain.Inventory
Imports Merchandising.Domain.Security

Namespace Entities

    ''' <summary>A single StockAdjustments row.</summary>
    Public NotInheritable Class StockAdjustment
        Implements IOwnershipResource

        Public Property Id As Integer

        Public Property ProductId As Integer

        ''' <summary>Joined from Products on read; empty on an instance built for an insert.</summary>
        Public Property ProductSku As String = String.Empty

        ''' <summary>Joined from Products on read; empty on an instance built for an insert.</summary>
        Public Property ProductName As String = String.Empty

        ''' <summary>Signed: positive found-more (increase), negative found-less (decrease). Never zero (CK_StockAdjustments_QuantityVariance).</summary>
        Public Property QuantityVariance As Decimal

        Public Property Reason As String = String.Empty

        Public Property RequestedByUserId As Integer Implements IOwnershipResource.RequestedByUserId

        ''' <summary>Null until approved. Kept distinct from <see cref="RequestedByUserId"/> so the self-approval rule has both to compare (ADR-017 section 6). Stays null forever for an adjustment auto-applied below threshold - nobody approved it.</summary>
        Public Property ApprovedByUserId As Integer?

        ''' <summary>Captured at request time against the SystemSettings threshold then in force - never recomputed later (0010's own migration comment).</summary>
        Public Property ExceedsThreshold As Boolean

        Public Property Status As StockAdjustmentStatus

        Public Property RowVersion As Long

        Public Property CreatedAtUtc As DateTime

        Public Property UpdatedAtUtc As DateTime

    End Class

End Namespace
