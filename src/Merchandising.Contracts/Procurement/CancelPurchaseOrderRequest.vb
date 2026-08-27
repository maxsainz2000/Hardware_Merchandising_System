' Merchandising.Contracts.Procurement.CancelPurchaseOrderRequest
'
' P3-05: POST /api/v1/purchase-orders/{id}/cancel. Reason is required, the
' same shape EnterMaintenanceRequest uses for the same purpose - spec
' section 9's "require reason/confirmation where configured" control, and
' this card's own done-when box: "Both transitions audited with actor,
' reason and correlation ID."

Imports System.Text.Json.Serialization

Namespace Procurement

    Public NotInheritable Class CancelPurchaseOrderRequest

        <JsonPropertyName("reason")>
        Public Property Reason As String = String.Empty

    End Class

End Namespace
