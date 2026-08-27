' Merchandising.Contracts.Procurement.CreatePurchaseOrderRequest
'
' POST /api/v1/purchase-orders body.
'
' NO OrderNumber. The server generates it (PO-yyyyMMdd-NNNN) and
' UQ_PurchaseOrders_OrderNumber (0008) is the guarantee. Clients do not
' invent identifiers; IdempotencyKey below is how a client makes a retry
' safe, which is the only thing a client-supplied order number would have
' bought.
'
' STATUS EXISTS ONLY TO BE REJECTED. P3-03's first acceptance box requires
' that "a client-supplied status is rejected, not honoured". Omitting the
' property entirely would satisfy "not honoured" - System.Text.Json ignores
' unmapped members by default - but the caller would never learn that the
' status it sent was silently dropped, which is the failure mode the box is
' written against. So the property is declared, and PurchaseOrdersController
' returns 400 VALIDATION_FAILED when it arrives non-null. A created order is
' always Draft (spec section 10.1); the transitions out of it are P3-04's and
' P3-05's endpoints, never a creation flag.

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Procurement

    Public NotInheritable Class CreatePurchaseOrderRequest

        <JsonPropertyName("supplierId")>
        Public Property SupplierId As Integer

        ''' <summary>
        ''' Must be absent or null. Any value is refused with 400
        ''' VALIDATION_FAILED - see this file's header for why the property
        ''' exists at all.
        ''' </summary>
        <JsonPropertyName("status")>
        Public Property Status As String = Nothing

        <JsonPropertyName("lines")>
        Public Property Lines As IReadOnlyList(Of CreatePurchaseOrderLineRequest) =
            Array.Empty(Of CreatePurchaseOrderLineRequest)()

        ''' <summary>ADR-007: required, canonical 36-character UUID. A repeated key replays the original committed order rather than creating a second one.</summary>
        <JsonPropertyName("idempotencyKey")>
        Public Property IdempotencyKey As String = String.Empty

    End Class

End Namespace
