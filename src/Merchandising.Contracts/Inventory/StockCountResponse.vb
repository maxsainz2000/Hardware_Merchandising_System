' Merchandising.Contracts.Inventory.StockCountResponse
'
' A stock-count session: Open (Lines may still grow), Closed (immutable,
' still fully readable - card Done-when box 4), or a later phase's
' Approved/Rejected (StockCountStatus models all four now; only Open and
' Closed are reachable through this card's endpoints).

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Inventory

    Public NotInheritable Class StockCountResponse

        <JsonPropertyName("id")>
        Public Property Id As Integer

        <JsonPropertyName("status")>
        Public Property Status As String = String.Empty

        <JsonPropertyName("countedByUserId")>
        Public Property CountedByUserId As Integer

        <JsonPropertyName("approvedByUserId")>
        Public Property ApprovedByUserId As Integer?

        <JsonPropertyName("countedAtUtc")>
        Public Property CountedAtUtc As DateTime

        <JsonPropertyName("approvedAtUtc")>
        Public Property ApprovedAtUtc As DateTime?

        <JsonPropertyName("rowVersion")>
        Public Property RowVersion As Long

        <JsonPropertyName("createdAtUtc")>
        Public Property CreatedAtUtc As DateTime

        <JsonPropertyName("updatedAtUtc")>
        Public Property UpdatedAtUtc As DateTime

        <JsonPropertyName("lines")>
        Public Property Lines As IReadOnlyList(Of StockCountLineResponse) =
            Array.Empty(Of StockCountLineResponse)()

    End Class

End Namespace
