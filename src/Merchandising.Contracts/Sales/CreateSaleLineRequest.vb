' Merchandising.Contracts.Sales.CreateSaleLineRequest
'
' One line of a POST /api/v1/sales body.
'
' NO UnitPrice/Cost. Spec section 10.3: "It captures the effective unit price
' and recorded cost in the sale line" - taken server-side, fresh, from the
' product row at completion time (SaleService.CompleteAsync). A client-
' supplied price would be exactly the "current price" re-check this card's
' own Done-when box exists to guarantee never happens - the same reasoning
' ReceiveGoodsLineRequest's header gives for omitting ProductId there (a
' client-supplied value would be a second, untrustworthy source for
' something the server already has an authoritative source for).

Imports System.Text.Json.Serialization

Namespace Sales

    Public NotInheritable Class CreateSaleLineRequest

        <JsonPropertyName("productId")>
        Public Property ProductId As Integer

        ''' <summary>Validated to DECIMAL(19,3) scale at the API boundary (ADR-004.1) before any parameter is bound. Must be greater than zero (SaleLine's own hard precondition).</summary>
        <JsonPropertyName("quantity")>
        Public Property Quantity As Decimal

    End Class

End Namespace
