' Merchandising.Contracts.Reporting.GoodsReceivingHistoryItemResponse
'
' P6-04: one row of GET /api/v1/reports/procurement/goods-receiving - spec
' section 14's Goods-receiving history report definition: "Receipt,
' supplier, date, product, ordered quantity, received quantity, and
' responsible user."
'
' A FLAT PER-RECEIPT-LINE LISTING - one row per ReceiptLines row, the same
' "no GROUP BY, no fan-out concern" shape ReturnsAndCancellationsItemResponse
' (P6-03) uses for the same reason: this report's subject already IS
' transaction-line grain, so there is nothing to pre-aggregate.
'
' OrderedQuantity IS THE LINE'S OWN PurchaseOrderLines.OrderedQuantity - the
' quantity ordered on the specific order line this receipt line was received
' against, not any order-level total. A caller wanting the order's full
' ordered/received/outstanding picture reads the purchase-order history
' report (PurchaseOrderHistoryReportItemResponse) instead - the two reports
' are deliberately not the same shape (spec section 14 defines them as two
' separate rows).
'
' ReceivedQuantity is grouped by PurchaseOrderId and reconciled against
' PurchaseOrderHistoryReportItemResponse.ReceivedQuantity for the same order
' in the integration test - this is the "goods-receiving history reconciles
' through the harness" box, and simultaneously the "P4-06's accumulation
' rule, read back" box: two, then three, separate receipts against the same
' order must sum to the one correct total on both reports.

Imports System.Text.Json.Serialization

Namespace Reporting

    Public NotInheritable Class GoodsReceivingHistoryItemResponse

        <JsonPropertyName("receiptId")>
        Public Property ReceiptId As Integer

        <JsonPropertyName("receiptLineId")>
        Public Property ReceiptLineId As Integer

        <JsonPropertyName("referenceNumber")>
        Public Property ReferenceNumber As String = String.Empty

        <JsonPropertyName("purchaseOrderId")>
        Public Property PurchaseOrderId As Integer

        <JsonPropertyName("orderNumber")>
        Public Property OrderNumber As String = String.Empty

        <JsonPropertyName("supplierId")>
        Public Property SupplierId As Integer

        <JsonPropertyName("supplierName")>
        Public Property SupplierName As String = String.Empty

        <JsonPropertyName("receivedAtUtc")>
        Public Property ReceivedAtUtc As DateTime

        <JsonPropertyName("productId")>
        Public Property ProductId As Integer

        <JsonPropertyName("productSku")>
        Public Property ProductSku As String = String.Empty

        <JsonPropertyName("productName")>
        Public Property ProductName As String = String.Empty

        ''' <summary>The receipt line's own purchase-order line's OrderedQuantity - see this file's header.</summary>
        <JsonPropertyName("orderedQuantity")>
        Public Property OrderedQuantity As Decimal

        <JsonPropertyName("receivedQuantity")>
        Public Property ReceivedQuantity As Decimal

        <JsonPropertyName("receivedByUserId")>
        Public Property ReceivedByUserId As Integer

        <JsonPropertyName("receivedByUsername")>
        Public Property ReceivedByUsername As String = String.Empty

    End Class

End Namespace
