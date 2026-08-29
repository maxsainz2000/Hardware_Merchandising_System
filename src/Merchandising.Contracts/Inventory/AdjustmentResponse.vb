' Merchandising.Contracts.Inventory.AdjustmentResponse
'
' A stock adjustment - Pending (awaiting a second person's approval),
' Applied (the movement/balance/audit triple has committed, either
' immediately below threshold or after approval), or Rejected. MovementId
' is Nothing until Applied - a Pending or Rejected adjustment has never
' touched stock (card Done-when box 4).

Imports System.Text.Json.Serialization

Namespace Inventory

    Public NotInheritable Class AdjustmentResponse

        <JsonPropertyName("id")>
        Public Property Id As Integer

        <JsonPropertyName("productId")>
        Public Property ProductId As Integer

        <JsonPropertyName("productSku")>
        Public Property ProductSku As String = String.Empty

        <JsonPropertyName("productName")>
        Public Property ProductName As String = String.Empty

        <JsonPropertyName("quantityVariance")>
        Public Property QuantityVariance As Decimal

        <JsonPropertyName("reason")>
        Public Property Reason As String = String.Empty

        <JsonPropertyName("requestedByUserId")>
        Public Property RequestedByUserId As Integer

        ''' <summary>Nothing until approved. Stays Nothing forever for an adjustment applied directly below threshold - nobody approved it.</summary>
        <JsonPropertyName("approvedByUserId")>
        Public Property ApprovedByUserId As Integer?

        ''' <summary>Captured at request time against the threshold then in force (SystemSettings) - never recomputed later.</summary>
        <JsonPropertyName("exceedsThreshold")>
        Public Property ExceedsThreshold As Boolean

        <JsonPropertyName("status")>
        Public Property Status As String = String.Empty

        ''' <summary>The StockMovements row this adjustment produced. Nothing until Applied.</summary>
        <JsonPropertyName("movementId")>
        Public Property MovementId As Integer?

        <JsonPropertyName("rowVersion")>
        Public Property RowVersion As Long

        <JsonPropertyName("createdAtUtc")>
        Public Property CreatedAtUtc As DateTime

        <JsonPropertyName("updatedAtUtc")>
        Public Property UpdatedAtUtc As DateTime

    End Class

End Namespace
