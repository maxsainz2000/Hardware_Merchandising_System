' Merchandising.Contracts.Receiving.ReceiveGoodsLineRequest
'
' One line of a POST /api/v1/receipts body.
'
' NO ProductId. The product is derived server-side from the locked
' PurchaseOrderLine (spec section 10.1: the receiving command "validates ...
' line identity") - a client-supplied product would be a second, untrustworthy
' source for a value the order line already fixes, the same reasoning
' CreatePurchaseOrderLineRequest's header gives for omitting LineNumber.

Imports System.Text.Json.Serialization

Namespace Receiving

    Public NotInheritable Class ReceiveGoodsLineRequest

        <JsonPropertyName("purchaseOrderLineId")>
        Public Property PurchaseOrderLineId As Integer

        ''' <summary>Validated to DECIMAL(19,3) scale at the API boundary (ADR-004.1) before any parameter is bound.</summary>
        <JsonPropertyName("quantityReceived")>
        Public Property QuantityReceived As Decimal

        ''' <summary>The actual received/invoiced cost, which may differ from the order line's agreed PurchaseCost. Validated to DECIMAL(19,4) scale.</summary>
        <JsonPropertyName("cost")>
        Public Property Cost As Decimal

    End Class

End Namespace
