' Merchandising.Domain.Entities.PurchaseOrderLine
'
' Plain data holder for a PurchaseOrderLines row
' (db/migrations/0008_purchase-orders.sql). Domain depends on nothing
' (CLAUDE.md section 4).
'
' PurchaseCost is CAPTURED on the row; ProductSku and ProductName are
' PROJECTIONS joined from Products on read. That asymmetry is deliberate and
' is 0008's own reasoning: a cost that drifted after the order was placed
' would silently restate what was agreed with the supplier, whereas a name
' that changes should read as the product's current name. A referenced
' product can never be deleted (FK_PurchaseOrderLines_Products, RESTRICT), so
' the join can never fail to resolve.
'
' ReceivedQuantity starts at zero and only Phase 4's receiving command may
' move it - "nothing received yet" is a quantity, not an absence.

Namespace Entities

    ''' <summary>
    ''' A single PurchaseOrderLines row.
    ''' </summary>
    Public NotInheritable Class PurchaseOrderLine

        Public Property Id As Integer

        Public Property PurchaseOrderId As Integer

        ''' <summary>Identity of this line within its order (1..N), server-assigned and unique per order (UQ_PurchaseOrderLines_OrderLine).</summary>
        Public Property LineNumber As Integer

        Public Property ProductId As Integer

        ''' <summary>Joined from Products on read; empty on an instance built for an insert.</summary>
        Public Property ProductSku As String = String.Empty

        ''' <summary>Joined from Products on read; empty on an instance built for an insert.</summary>
        Public Property ProductName As String = String.Empty

        ''' <summary>DECIMAL(19,3). Validated to scale at the API boundary before binding (ADR-004.1).</summary>
        Public Property OrderedQuantity As Decimal

        ''' <summary>DECIMAL(19,4). Validated to scale at the API boundary before binding (ADR-004.1).</summary>
        Public Property PurchaseCost As Decimal

        ''' <summary>Accumulated across receipts by Phase 4. Zero on every line this phase can create.</summary>
        Public Property ReceivedQuantity As Decimal

        Public Property RowVersion As Long

        Public Property CreatedAtUtc As DateTime

        Public Property UpdatedAtUtc As DateTime

    End Class

End Namespace
