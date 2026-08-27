' Merchandising.Contracts.Procurement.PurchaseOrderHistoryItemResponse
'
' P3-06: one row of GET /api/v1/purchase-orders/history - spec section 14's
' Purchase-order history report definition: "Supplier, order number,
' statuses, ordered quantity/value, received quantity/value, and outstanding
' quantity." Deliberately a SEPARATE type from PurchaseOrderSummaryResponse
' (P3-03) rather than the same shape with more fields -
' PurchaseOrderSummaryResponse's own header reserved these aggregates for
' this card by name, precisely so the plain list stays a header-only read and
' this one stays the report-shaped read; giving both endpoints the same
' response type would blur which endpoint owns which contract.
'
' EVERY AGGREGATE IS COMPUTED SERVER-SIDE, over PurchaseOrderLines, never by
' the client re-summing PurchaseOrderResponse.Lines itself (CLAUDE.md
' section 5: the API is authoritative). ReceivedValue uses the SAME captured
' PurchaseCost as OrderedValue - PurchaseOrderLines has no separate
' "received cost" column (0008's own header: cost is captured once, at order
' time, so a later cost drift never restates what was agreed with the
' supplier). OutstandingQuantity is Ordered minus Received; spec section 14
' lists no "outstanding value", so none is invented here.
'
' A CANCELLED OR CLOSED ORDER APPEARS HERE LIKE ANY OTHER - Status is the
' label spec section 10.1's done-when box asks for ("appears in history,
' labelled, rather than vanishing"). Nothing in this endpoint excludes a
' terminal status by default; the status filter stays opt-in, same as
' GET /api/v1/purchase-orders.

Imports System.Text.Json.Serialization

Namespace Procurement

    Public NotInheritable Class PurchaseOrderHistoryItemResponse

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

        <JsonPropertyName("requestedByUserId")>
        Public Property RequestedByUserId As Integer

        <JsonPropertyName("approvedByUserId")>
        Public Property ApprovedByUserId As Integer?

        <JsonPropertyName("submittedAtUtc")>
        Public Property SubmittedAtUtc As DateTime?

        <JsonPropertyName("approvedAtUtc")>
        Public Property ApprovedAtUtc As DateTime?

        <JsonPropertyName("createdAtUtc")>
        Public Property CreatedAtUtc As DateTime

        ''' <summary>Sum of every line's OrderedQuantity. DECIMAL(19,3) scale (ADR-004).</summary>
        <JsonPropertyName("orderedQuantity")>
        Public Property OrderedQuantity As Decimal

        ''' <summary>Sum of every line's OrderedQuantity * PurchaseCost. DECIMAL(19,4) scale (ADR-004).</summary>
        <JsonPropertyName("orderedValue")>
        Public Property OrderedValue As Decimal

        ''' <summary>Sum of every line's ReceivedQuantity. Zero for every order Phase 3 alone can produce - Phase 4 owns receiving.</summary>
        <JsonPropertyName("receivedQuantity")>
        Public Property ReceivedQuantity As Decimal

        ''' <summary>Sum of every line's ReceivedQuantity * PurchaseCost (the captured cost, not a later one - see this file's header).</summary>
        <JsonPropertyName("receivedValue")>
        Public Property ReceivedValue As Decimal

        ''' <summary>OrderedQuantity minus ReceivedQuantity. Spec section 14 does not ask for an outstanding VALUE, so none is computed.</summary>
        <JsonPropertyName("outstandingQuantity")>
        Public Property OutstandingQuantity As Decimal

    End Class

End Namespace
