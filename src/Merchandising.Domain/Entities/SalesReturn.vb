' Merchandising.Domain.Entities.SalesReturn
'
' Plain data holder for a SalesReturns row (db/migrations/0012_sales-returns.sql),
' matching StockAdjustment.vb's shape - Domain depends on nothing (CLAUDE.md
' section 4).
'
' Status is the DOMAIN ENUM, not the stored string - the same ADR-020
' reasoning PurchaseOrder.vb/StockAdjustment.vb already give: 0012 stores the
' enum NAME under COLLATE utf8mb4_bin and a CHECK constraint, so the only
' values that can reach this property are the three SalesReturnStatus names.
'
' IMPLEMENTS IOwnershipResource (ADR-017 section 6, applied here at P5-11 -
' see AuthorizationPolicyRegistration.vb's header for the correction this
' card made: SalesReturns.ApproveExceptional DOES carry a self-approval veto,
' because CashierAndAbove and AdminAndAbove both include Admin/SuperAdmin, so
' the same actor could otherwise request an exceptional return and then
' approve their own request). The interface member is RequestedByUserId, but
' this property keeps its own domain name - ReturnedByUserId, the column
' 0012 actually declares - rather than renaming the column's concept to fit
' the interface; VB's explicit "Implements" clause does not require the
' names to match.
'
' PENDING-APPROVAL SPECIFIC: RefundMethod/RefundAmount/ApprovedByUserId/
' ApprovedAtUtc are all Nothing until Status reaches Completed or Rejected -
' 0012's own migration comment explains why (CK_SalesReturns_RefundPairing
' and the deliberate choice not to persist a value that might be decided
' differently by whoever approves).

Imports Merchandising.Domain.Sales
Imports Merchandising.Domain.Security

Namespace Entities

    ''' <summary>A single SalesReturns row.</summary>
    Public NotInheritable Class SalesReturn
        Implements IOwnershipResource

        Public Property Id As Integer

        Public Property SaleId As Integer

        Public Property ReturnedByUserId As Integer Implements IOwnershipResource.RequestedByUserId

        ''' <summary>Null until Completed or Rejected. Kept distinct from <see cref="ReturnedByUserId"/> so the self-approval rule has both to compare (ADR-017 section 6).</summary>
        Public Property ApprovedByUserId As Integer?

        Public Property Reason As String = String.Empty

        ''' <summary>Captured at request time against the SystemSettings threshold then in force - never recomputed later (0012's own migration comment, the identical StockAdjustments argument).</summary>
        Public Property ExceedsThreshold As Boolean

        Public Property Status As SalesReturnStatus

        ''' <summary>Null until Completed - operational recording only (G-24), never an external bank/terminal reversal.</summary>
        Public Property RefundMethod As String

        ''' <summary>Null until Completed. DECIMAL(19,4).</summary>
        Public Property RefundAmount As Decimal?

        Public Property ReturnedAtUtc As DateTime

        Public Property ApprovedAtUtc As DateTime?

        Public Property RowVersion As Long

        Public Property CreatedAtUtc As DateTime

        Public Property UpdatedAtUtc As DateTime

    End Class

End Namespace
