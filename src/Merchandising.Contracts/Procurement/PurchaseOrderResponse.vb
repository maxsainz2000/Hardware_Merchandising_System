' Merchandising.Contracts.Procurement.PurchaseOrderResponse
'
' A purchase order with its lines - POST /api/v1/purchase-orders and
' GET /api/v1/purchase-orders/{id}.
'
' Status is the enum NAME ("Draft"), matching what 0008 stores and what
' Merchandising.Domain.Procurement.PurchaseOrderStatus is named - never an
' ordinal (ADR-020 section 5). A client that switches on this string keeps
' working when a state is added; one that switched on a number would not.
'
' ApprovedByUserId, SubmittedAtUtc and ApprovedAtUtc are nullable and are
' always null on an order this card can create. They are present from the
' start rather than added at P3-04 because the response SHAPE should not
' change when a status transition endpoint arrives - only the values in it.

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Procurement

    Public NotInheritable Class PurchaseOrderResponse

        <JsonPropertyName("id")>
        Public Property Id As Integer

        ''' <summary>Server-generated, unique. See PurchaseOrderNumberGenerator for the format and why the client does not supply it.</summary>
        <JsonPropertyName("orderNumber")>
        Public Property OrderNumber As String = String.Empty

        <JsonPropertyName("supplierId")>
        Public Property SupplierId As Integer

        <JsonPropertyName("supplierName")>
        Public Property SupplierName As String = String.Empty

        ''' <summary>One of the seven spec section 10.1 status names. Always "Draft" for an order created by this endpoint.</summary>
        <JsonPropertyName("status")>
        Public Property Status As String = String.Empty

        <JsonPropertyName("requestedByUserId")>
        Public Property RequestedByUserId As Integer

        ''' <summary>Null until P3-04's approval endpoint sets it. Kept separate from RequestedByUserId so the self-approval rule has both to compare (ADR-017 section 6).</summary>
        <JsonPropertyName("approvedByUserId")>
        Public Property ApprovedByUserId As Integer?

        <JsonPropertyName("submittedAtUtc")>
        Public Property SubmittedAtUtc As DateTime?

        <JsonPropertyName("approvedAtUtc")>
        Public Property ApprovedAtUtc As DateTime?

        <JsonPropertyName("rowVersion")>
        Public Property RowVersion As Long

        <JsonPropertyName("createdAtUtc")>
        Public Property CreatedAtUtc As DateTime

        <JsonPropertyName("updatedAtUtc")>
        Public Property UpdatedAtUtc As DateTime

        ''' <summary>Always at least one line - an order with no lines is refused at creation.</summary>
        <JsonPropertyName("lines")>
        Public Property Lines As IReadOnlyList(Of PurchaseOrderLineResponse) =
            Array.Empty(Of PurchaseOrderLineResponse)()

    End Class

End Namespace
