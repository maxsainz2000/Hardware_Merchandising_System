' Merchandising.Contracts.Sales.OpenCashierSessionRequest
'
' POST /api/v1/cashier-sessions body. Spec section 10.3: "A sale requires an
' open cashier session" - opened with a declared opening float.
'
' P5-04 / ADR-007: IdempotencyKey required, canonical 36-character UUID. A
' repeated key replays the original committed open rather than opening a
' second session.

Imports System.Text.Json.Serialization

Namespace Sales

    Public NotInheritable Class OpenCashierSessionRequest

        ''' <summary>Non-negative. Validated to DECIMAL(19,4) scale at the API boundary (ADR-004.1) before any parameter is bound.</summary>
        <JsonPropertyName("openingFloat")>
        Public Property OpeningFloat As Decimal

        <JsonPropertyName("idempotencyKey")>
        Public Property IdempotencyKey As String = String.Empty

    End Class

End Namespace
