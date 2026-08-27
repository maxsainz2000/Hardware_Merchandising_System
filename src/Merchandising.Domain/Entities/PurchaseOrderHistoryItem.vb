' Merchandising.Domain.Entities.PurchaseOrderHistoryItem
'
' P3-06: one row of PurchaseOrderRepository.SearchHistoryAsync - a
' PurchaseOrders header joined with its lines' SUMs, never PurchaseOrder
' itself with extra properties bolted on. PurchaseOrder's Lines collection is
' populated only by GetByIdAsync's two-query read (header, then every line);
' this type is the single-query, GROUP BY-aggregated shape P3-06's report
' surface needs instead, matching PurchaseOrderHistoryItemResponse's own
' fields one for one.
'
' Domain depends on nothing (CLAUDE.md section 4).

Namespace Entities

    ''' <summary>One purchase order with its line aggregates, for the history/report surface.</summary>
    Public NotInheritable Class PurchaseOrderHistoryItem

        Public Property Id As Integer
        Public Property OrderNumber As String = String.Empty
        Public Property SupplierId As Integer
        Public Property SupplierName As String = String.Empty
        Public Property Status As Procurement.PurchaseOrderStatus
        Public Property RequestedByUserId As Integer
        Public Property ApprovedByUserId As Integer?
        Public Property SubmittedAtUtc As DateTime?
        Public Property ApprovedAtUtc As DateTime?
        Public Property CreatedAtUtc As DateTime

        ''' <summary>Sum of every line's OrderedQuantity. Zero for an order with no lines - unreachable today (an order must have at least one), but not assumed.</summary>
        Public Property OrderedQuantity As Decimal

        ''' <summary>Sum of every line's OrderedQuantity * PurchaseCost.</summary>
        Public Property OrderedValue As Decimal

        ''' <summary>Sum of every line's ReceivedQuantity.</summary>
        Public Property ReceivedQuantity As Decimal

        ''' <summary>Sum of every line's ReceivedQuantity * PurchaseCost (the captured order-time cost).</summary>
        Public Property ReceivedValue As Decimal

        ''' <summary>OrderedQuantity minus ReceivedQuantity.</summary>
        Public Property OutstandingQuantity As Decimal

    End Class

End Namespace
