' Merchandising.Contracts.Reporting.PurchaseOrderHistoryReportItemResponse
'
' P6-04: one row of GET /api/v1/reports/procurement/purchase-orders - spec
' section 14's Purchase-order history report definition: "Supplier, order
' number, statuses, ordered quantity/value, received quantity/value, and
' outstanding quantity."
'
' DELIBERATELY A SEPARATE TYPE FROM Merchandising.Contracts.Procurement.
' PurchaseOrderHistoryItemResponse (P3-06), even though the two carry
' overlapping fields. P3-06's endpoint computes ReceivedQuantity/Value from
' PurchaseOrderLines.ReceivedQuantity, the denormalized accumulator
' ReceivingService maintains. THIS type's ReceivedQuantity/Value/
' OutstandingQuantity are computed by ReportRepository.
' GetPurchaseOrderHistoryReportAsync summing ReceiptLines directly - "from
' committed receipt rows" (this card's own Done-when box), a genuinely
' independent code path. The reconciliation test asserts the two agree,
' which is exactly what proves the accumulator column trustworthy
' (P4-06's accumulation rule, read back) - see docs/report-specification.md
' section 7 for why the harness requires two independently-written queries,
' never the same one run twice.
'
' Status is read and echoed verbatim from PurchaseOrders.Status (ADR-020's
' enum name, never an ordinal) - this report consumes the status machine,
' it does not re-decide it.

Imports System.Text.Json.Serialization

Namespace Reporting

    Public NotInheritable Class PurchaseOrderHistoryReportItemResponse

        <JsonPropertyName("id")>
        Public Property Id As Integer

        <JsonPropertyName("orderNumber")>
        Public Property OrderNumber As String = String.Empty

        <JsonPropertyName("supplierId")>
        Public Property SupplierId As Integer

        <JsonPropertyName("supplierName")>
        Public Property SupplierName As String = String.Empty

        ''' <summary>One of the seven spec section 10.1 status names, never an ordinal (ADR-020).</summary>
        <JsonPropertyName("status")>
        Public Property Status As String = String.Empty

        <JsonPropertyName("createdAtUtc")>
        Public Property CreatedAtUtc As DateTime

        ''' <summary>Sum of every line's OrderedQuantity, from PurchaseOrderLines. DECIMAL(19,3) scale (ADR-004).</summary>
        <JsonPropertyName("orderedQuantity")>
        Public Property OrderedQuantity As Decimal

        ''' <summary>Sum of every line's OrderedQuantity * PurchaseCost. DECIMAL(19,4) scale (ADR-004).</summary>
        <JsonPropertyName("orderedValue")>
        Public Property OrderedValue As Decimal

        ''' <summary>Sum of every ReceiptLines.QuantityReceived across every receipt against this order - read from the committed receipt rows, never PurchaseOrderLines.ReceivedQuantity.</summary>
        <JsonPropertyName("receivedQuantity")>
        Public Property ReceivedQuantity As Decimal

        ''' <summary>Received quantity valued at the ORDER's captured PurchaseCost - the same captured-cost convention OrderedValue and P3-06's ReceivedValue both use (docs/report-specification.md section 5), never the receipt's own (possibly different) invoiced Cost.</summary>
        <JsonPropertyName("receivedValue")>
        Public Property ReceivedValue As Decimal

        ''' <summary>OrderedQuantity minus ReceivedQuantity, both computed from committed rows in this same query.</summary>
        <JsonPropertyName("outstandingQuantity")>
        Public Property OutstandingQuantity As Decimal

    End Class

End Namespace
