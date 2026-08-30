' Merchandising.Contracts.Sales.CloseCashierSessionRequest
'
' POST /api/v1/cashier-sessions/{id}/close body. Spec section 10.3's closing
' paragraph: "declared cash, calculated cash, variance." DeclaredCash is the
' cashier's physical cash count - the ONLY cash figure this request carries.
'
' DELIBERATELY NO CalculatedCash FIELD (card Done-when box 1). The API is
' authoritative for that figure; a client-supplied one would be exactly the
' thing CLAUDE.md section 5 forbids trusting. If a caller sends one anyway
' (e.g. a hand-built JSON body with an extra "calculatedCash" property),
' System.Text.Json's reflection-based deserialization (ADR: no source
' generators from VB) simply drops the unmapped property - it never reaches
' CashierSessionService, which computes CalculatedCash itself from committed
' SalePayments rows regardless of what arrived in the request.
'
' P5-04 / ADR-007: IdempotencyKey required. A repeated key replays the
' original close rather than re-closing (which would fail anyway - a Closed
' session is no longer Open, see CashierSessionOutcomeKind.NotOpen).

Imports System.Text.Json.Serialization

Namespace Sales

    Public NotInheritable Class CloseCashierSessionRequest

        ''' <summary>The cashier's physical cash count. Validated to DECIMAL(19,4) scale at the API boundary (ADR-004.1) before any parameter is bound.</summary>
        <JsonPropertyName("declaredCash")>
        Public Property DeclaredCash As Decimal

        <JsonPropertyName("idempotencyKey")>
        Public Property IdempotencyKey As String = String.Empty

    End Class

End Namespace
