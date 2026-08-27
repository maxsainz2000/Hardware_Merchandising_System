' Merchandising.Contracts.Procurement.ClosePurchaseOrderRequest
'
' P3-05: POST /api/v1/purchase-orders/{id}/close. Reason is required - same
' shape and same reasoning as CancelPurchaseOrderRequest.

Imports System.Text.Json.Serialization

Namespace Procurement

    Public NotInheritable Class ClosePurchaseOrderRequest

        <JsonPropertyName("reason")>
        Public Property Reason As String = String.Empty

    End Class

End Namespace
