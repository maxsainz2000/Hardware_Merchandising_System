' Merchandising.Contracts.Reporting.StockMovementReportItemResponse
'
' P6-05: spec section 14 row 10 - "Movement source, product, delta,
' before/after quantity, user, time, reason, and correlation identifier."
' "Movement source" is Reason (StockMovements carries no separate source
' column - Reason IS the source: "StockAdjustment", "Sale", "Receiving", and
' so on, exactly as StockMovementWriter's callers name it).
'
' CorrelationId IS THE ONE FIELD THIS CARD'S OWN DONE-WHEN BOX NAMES
' EXPLICITLY - "so a row in a report can be traced to the request that made
' it." It was already on Merchandising.Domain.Entities.StockMovementItem
' (P4-11); this is the first report to carry it through to a report
' response.

Imports System.Text.Json.Serialization

Namespace Reporting

    Public NotInheritable Class StockMovementReportItemResponse

        <JsonPropertyName("id")>
        Public Property Id As Integer

        <JsonPropertyName("productId")>
        Public Property ProductId As Integer

        <JsonPropertyName("productSku")>
        Public Property ProductSku As String = String.Empty

        <JsonPropertyName("productName")>
        Public Property ProductName As String = String.Empty

        <JsonPropertyName("delta")>
        Public Property Delta As Decimal

        <JsonPropertyName("quantityBefore")>
        Public Property QuantityBefore As Decimal

        <JsonPropertyName("quantityAfter")>
        Public Property QuantityAfter As Decimal

        ''' <summary>The movement's source - "StockAdjustment", "Sale", "Receiving", and so on. StockMovements carries no separate source column; Reason already is it.</summary>
        <JsonPropertyName("reason")>
        Public Property Reason As String = String.Empty

        <JsonPropertyName("actorUserId")>
        Public Property ActorUserId As Integer

        <JsonPropertyName("actorUsername")>
        Public Property ActorUsername As String = String.Empty

        ''' <summary>Traces this row to the request that made it - this card's own Done-when box.</summary>
        <JsonPropertyName("correlationId")>
        Public Property CorrelationId As String = String.Empty

        <JsonPropertyName("createdAtUtc")>
        Public Property CreatedAtUtc As DateTime

    End Class

End Namespace
