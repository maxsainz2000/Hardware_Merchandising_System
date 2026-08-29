' Merchandising.Contracts.Receiving.PurchaseReturnResponse
'
' 201 (or replayed 200) body for POST /api/v1/receipts/{receiptId}/returns.
' Status is always "Approved" (this card's own header explains why - no
' two-actor workflow exists here); RequestedByUserId and ApprovedByUserId
' are both present, and equal, so the shape stays consistent with a future
' phase that might split them without a response-shape change.

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Receiving

    Public NotInheritable Class PurchaseReturnResponse

        <JsonPropertyName("id")>
        Public Property Id As Integer

        <JsonPropertyName("receiptId")>
        Public Property ReceiptId As Integer

        <JsonPropertyName("status")>
        Public Property Status As String = String.Empty

        <JsonPropertyName("requestedByUserId")>
        Public Property RequestedByUserId As Integer

        <JsonPropertyName("approvedByUserId")>
        Public Property ApprovedByUserId As Integer

        <JsonPropertyName("returnedAtUtc")>
        Public Property ReturnedAtUtc As DateTime

        <JsonPropertyName("approvedAtUtc")>
        Public Property ApprovedAtUtc As DateTime

        <JsonPropertyName("referenceNumber")>
        Public Property ReferenceNumber As String = String.Empty

        <JsonPropertyName("rowVersion")>
        Public Property RowVersion As Long

        <JsonPropertyName("createdAtUtc")>
        Public Property CreatedAtUtc As DateTime

        <JsonPropertyName("updatedAtUtc")>
        Public Property UpdatedAtUtc As DateTime

        <JsonPropertyName("lines")>
        Public Property Lines As IReadOnlyList(Of PurchaseReturnLineResponse) =
            Array.Empty(Of PurchaseReturnLineResponse)()

    End Class

End Namespace
