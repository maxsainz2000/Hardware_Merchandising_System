' Merchandising.Contracts.Inventory.RequestAdjustmentRequest
'
' POST /api/v1/adjustments body. Spec section 10.2: "an adjustment applies a
' variance to stock." QuantityVariance is SIGNED - positive found-more
' (increase), negative found-less (decrease) - the caller's own corrected
' figure, never split into separate "direction" and "magnitude" fields.
'
' P4-10 / ADR-007: IdempotencyKey required, canonical 36-character UUID. A
' repeated key replays the original committed request rather than creating
' (or applying) a second adjustment.

Imports System.Text.Json.Serialization

Namespace Inventory

    Public NotInheritable Class RequestAdjustmentRequest

        <JsonPropertyName("productId")>
        Public Property ProductId As Integer

        ''' <summary>Signed, non-zero. Validated to DECIMAL(19,3) scale at the API boundary (ADR-004.1) before any parameter is bound.</summary>
        <JsonPropertyName("quantityVariance")>
        Public Property QuantityVariance As Decimal

        ''' <summary>Required (StockAdjustments.Reason NOT NULL). Spec section 10.2: "reason" is one of the fields a stock count/adjustment records.</summary>
        <JsonPropertyName("reason")>
        Public Property Reason As String = String.Empty

        <JsonPropertyName("idempotencyKey")>
        Public Property IdempotencyKey As String = String.Empty

    End Class

End Namespace
