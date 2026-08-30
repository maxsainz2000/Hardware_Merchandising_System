' Merchandising.Contracts.Sales.CloseCashierSessionRequest
'
' POST /api/v1/cashier-sessions/{id}/close body. This card closes a session
' with a plain status flip only - declared/calculated cash and variance are
' P5-05's job, extending this same close command in the same file.
'
' P5-04 / ADR-007: IdempotencyKey required. A repeated key replays the
' original close rather than re-closing (which would fail anyway - a Closed
' session is no longer Open, see CashierSessionOutcomeKind.NotOpen).

Imports System.Text.Json.Serialization

Namespace Sales

    Public NotInheritable Class CloseCashierSessionRequest

        <JsonPropertyName("idempotencyKey")>
        Public Property IdempotencyKey As String = String.Empty

    End Class

End Namespace
