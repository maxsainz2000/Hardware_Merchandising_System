' Merchandising.Contracts.Receiving.RecordPurchaseReturnLineRequest
'
' One line of a POST /api/v1/receipts/{receiptId}/returns body.
'
' NO ProductId AND NO Cost. Both are derived server-side from the locked
' ReceiptLines row this line names - spec section 10.1's "record request
' records the receipt line", never a client-supplied product or cost. The
' same trust boundary ReceiveGoodsLineRequest's header argues for the
' receiving side.

Imports System.Text.Json.Serialization

Namespace Receiving

    Public NotInheritable Class RecordPurchaseReturnLineRequest

        <JsonPropertyName("receiptLineId")>
        Public Property ReceiptLineId As Integer

        ''' <summary>Validated to DECIMAL(19,3) scale at the API boundary (ADR-004.1) before any parameter is bound. Bounded server-side by received-minus-prior-returns (P4-08), never by a client-supplied figure.</summary>
        <JsonPropertyName("quantityReturned")>
        Public Property QuantityReturned As Decimal

        ''' <summary>Required (0009's PurchaseReturnLines.Reason NOT NULL). Spec section 10.1: "reason" is one of the fields a return request records.</summary>
        <JsonPropertyName("reason")>
        Public Property Reason As String = String.Empty

        ''' <summary>Spec section 10.1: "whether stock is removed". True decrements StockBalances and writes a stock-out movement in the same transaction; False records the return operationally with no stock effect (e.g. a defective item already written off).</summary>
        <JsonPropertyName("removesStock")>
        Public Property RemovesStock As Boolean = True

    End Class

End Namespace
