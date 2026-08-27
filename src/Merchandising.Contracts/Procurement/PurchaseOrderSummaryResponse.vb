' Merchandising.Contracts.Procurement.PurchaseOrderSummaryResponse
'
' One row of GET /api/v1/purchase-orders - the header WITHOUT its lines.
'
' A separate type rather than PurchaseOrderResponse with an empty Lines
' array. Returning every line of every order on a list page is a query the
' page size cannot bound (25 orders of 40 lines each is 1,000 rows for a
' screen that shows 25), and an empty array on a list response would be
' indistinguishable-by-shape from "this order has no lines" to anyone reading
' the JSON rather than this comment. LineCount is what a list actually needs;
' the lines themselves come from GET /{id}.
'
' NOT here: ordered value, outstanding quantity, or any other aggregate.
' Those are spec section 14's purchase-history figures and belong to P3-06,
' which must compute them server-side. Adding a half-version of them here
' would give that card a second definition to reconcile against.

Imports System.Text.Json.Serialization

Namespace Procurement

    Public NotInheritable Class PurchaseOrderSummaryResponse

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

        <JsonPropertyName("lineCount")>
        Public Property LineCount As Integer

        <JsonPropertyName("rowVersion")>
        Public Property RowVersion As Long

        <JsonPropertyName("createdAtUtc")>
        Public Property CreatedAtUtc As DateTime

        <JsonPropertyName("updatedAtUtc")>
        Public Property UpdatedAtUtc As DateTime

    End Class

End Namespace
