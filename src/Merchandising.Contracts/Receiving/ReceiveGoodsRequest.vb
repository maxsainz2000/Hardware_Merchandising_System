' Merchandising.Contracts.Receiving.ReceiveGoodsRequest
'
' POST /api/v1/receipts body. Spec section 10.1: "the client sends a
' receiving command containing a unique request identifier. The API
' validates the order status, line identity, received quantity, duplicate
' request state, and user permission."
'
' NO ReceivedAtUtc. The server stamps UtcNow - nothing in the spec asks for a
' backdated receiving time in the MVP, and a client-supplied timestamp would
' be one more untrusted value ReceivingService would have to re-validate for
' no requirement that needs it.

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Receiving

    Public NotInheritable Class ReceiveGoodsRequest

        <JsonPropertyName("purchaseOrderId")>
        Public Property PurchaseOrderId As Integer

        ''' <summary>The receiving clerk's own goods-received-note number. Unique (UQ_Receipts_ReferenceNumber, 0009) - a duplicate is the caller's problem, not the server's race.</summary>
        <JsonPropertyName("referenceNumber")>
        Public Property ReferenceNumber As String = String.Empty

        <JsonPropertyName("lines")>
        Public Property Lines As IReadOnlyList(Of ReceiveGoodsLineRequest) =
            Array.Empty(Of ReceiveGoodsLineRequest)()

        ''' <summary>ADR-007: required, canonical 36-character UUID. A repeated key replays the original committed receipt rather than receiving stock a second time.</summary>
        <JsonPropertyName("idempotencyKey")>
        Public Property IdempotencyKey As String = String.Empty

    End Class

End Namespace
