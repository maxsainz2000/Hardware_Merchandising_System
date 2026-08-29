' Merchandising.Contracts.Inventory.RecordStockCountLineRequest
'
' POST /api/v1/stock-counts/{id}/lines body. NO SystemQuantity AND NO
' Variance - both are computed server-side from StockBalances at the moment
' this line is recorded (spec section 10.2's "variance ... at the moment of
' counting", the card's own Done-when box 1), never a client-supplied
' figure. The same trust boundary RecordPurchaseReturnLineRequest's header
' argues for a purchase return.

Imports System.Text.Json.Serialization

Namespace Inventory

    Public NotInheritable Class RecordStockCountLineRequest

        <JsonPropertyName("productId")>
        Public Property ProductId As Integer

        ''' <summary>Validated to DECIMAL(19,3) scale at the API boundary (ADR-004.1) before any parameter is bound.</summary>
        <JsonPropertyName("countedQuantity")>
        Public Property CountedQuantity As Decimal

        ''' <summary>ADR-007: required, canonical 36-character UUID. A repeated key replays the original committed line rather than recording it twice.</summary>
        <JsonPropertyName("idempotencyKey")>
        Public Property IdempotencyKey As String = String.Empty

    End Class

End Namespace
