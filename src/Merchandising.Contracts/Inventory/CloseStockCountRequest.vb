' Merchandising.Contracts.Inventory.CloseStockCountRequest
'
' POST /api/v1/stock-counts/{id}/close body. Closing carries no product
' data - it only locks the session's already-recorded lines as immutable
' (card Done-when box 4).
'
' P4-09 / ADR-007: IdempotencyKey required. A repeated key replays the
' original close rather than re-closing (which would fail anyway - a
' Closed count is no longer Open, see StockCountOutcomeKind.NotOpen).

Imports System.Text.Json.Serialization

Namespace Inventory

    Public NotInheritable Class CloseStockCountRequest

        <JsonPropertyName("idempotencyKey")>
        Public Property IdempotencyKey As String = String.Empty

    End Class

End Namespace
