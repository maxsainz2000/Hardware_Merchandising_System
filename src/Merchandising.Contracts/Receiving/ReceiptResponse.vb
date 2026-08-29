' Merchandising.Contracts.Receiving.ReceiptResponse
'
' 201 (or replayed 200) body for POST /api/v1/receipts. PurchaseOrderStatus is
' the order's status AFTER this receipt committed - "PartiallyReceived" or
' "FullyReceived" (ADR-020) - so a caller learns the transition's outcome
' without a second round trip to GET the order.

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Receiving

    Public NotInheritable Class ReceiptResponse

        <JsonPropertyName("id")>
        Public Property Id As Integer

        <JsonPropertyName("purchaseOrderId")>
        Public Property PurchaseOrderId As Integer

        <JsonPropertyName("orderNumber")>
        Public Property OrderNumber As String = String.Empty

        ''' <summary>"PartiallyReceived" or "FullyReceived" - never any other of the seven names, ADR-020's two receiving actions.</summary>
        <JsonPropertyName("purchaseOrderStatus")>
        Public Property PurchaseOrderStatus As String = String.Empty

        <JsonPropertyName("receivedByUserId")>
        Public Property ReceivedByUserId As Integer

        <JsonPropertyName("receivedAtUtc")>
        Public Property ReceivedAtUtc As DateTime

        <JsonPropertyName("referenceNumber")>
        Public Property ReferenceNumber As String = String.Empty

        <JsonPropertyName("createdAtUtc")>
        Public Property CreatedAtUtc As DateTime

        <JsonPropertyName("lines")>
        Public Property Lines As IReadOnlyList(Of ReceiptLineResponse) =
            Array.Empty(Of ReceiptLineResponse)()

    End Class

End Namespace
