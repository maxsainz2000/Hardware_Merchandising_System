' Merchandising.Contracts.Procurement.CreatePurchaseOrderLineRequest
'
' One line of a POST /api/v1/purchase-orders body.
'
' NO LineNumber. The server assigns 1..N in the order the lines arrive, and
' UQ_PurchaseOrderLines_OrderLine (0008) is what guarantees two lines never
' claim the same one. A client-supplied line number would be a second,
' untrustworthy source for an identity Phase 4's receiving command relies on
' (spec section 10.1: the receiving command "validates ... line identity").
'
' NO ReceivedQuantity. It starts at zero for every new line and only Phase
' 4's receiving command may move it.

Imports System.Text.Json.Serialization

Namespace Procurement

    Public NotInheritable Class CreatePurchaseOrderLineRequest

        <JsonPropertyName("productId")>
        Public Property ProductId As Integer

        ''' <summary>Expected quantity. Validated to DECIMAL(19,3) scale at the API boundary (ADR-004.1) before any parameter is bound.</summary>
        <JsonPropertyName("orderedQuantity")>
        Public Property OrderedQuantity As Decimal

        ''' <summary>Agreed unit cost. Validated to DECIMAL(19,4) scale at the API boundary (ADR-004.1).</summary>
        <JsonPropertyName("purchaseCost")>
        Public Property PurchaseCost As Decimal

    End Class

End Namespace
