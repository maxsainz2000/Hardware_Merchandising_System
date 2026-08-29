' Merchandising.Contracts.Receiving.RecordPurchaseReturnRequest
'
' POST /api/v1/receipts/{receiptId}/returns body. NO ReceiptId property -
' the route itself names the receipt ("ONE RETURN, ONE RECEIPT", 0009's own
' comment), so there is nothing for a body value to disagree with.
'
' P4-08: a purchase return is recorded and resolved in ONE atomic command,
' not a two-actor request/approve workflow - PurchaseReturns.Manage is a
' single policy (PolicyRegistry), and ADR-017 section 6 names only
' PurchaseOrders.Approve/Adjustments.Approve as carrying a self-approval
' veto; purchase returns carry none. The committed record's Status is always
' "Approved", RequestedByUserId and ApprovedByUserId both the same actor.

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Receiving

    Public NotInheritable Class RecordPurchaseReturnRequest

        ''' <summary>The clerk's own reference for this return. Unique (UQ_PurchaseReturns_ReferenceNumber, 0009) - a duplicate is the caller's problem, not the server's race.</summary>
        <JsonPropertyName("referenceNumber")>
        Public Property ReferenceNumber As String = String.Empty

        <JsonPropertyName("lines")>
        Public Property Lines As IReadOnlyList(Of RecordPurchaseReturnLineRequest) =
            Array.Empty(Of RecordPurchaseReturnLineRequest)()

        ''' <summary>ADR-007: required, canonical 36-character UUID. A repeated key replays the original committed return rather than returning stock a second time.</summary>
        <JsonPropertyName("idempotencyKey")>
        Public Property IdempotencyKey As String = String.Empty

    End Class

End Namespace
