' Merchandising.Contracts.Sales.CreateSaleRequest
'
' POST /api/v1/sales body. Spec section 10.3: "A sale requires an open
' cashier session... The sale command includes a client-generated idempotency
' key. The API re-checks product activity, current price, available stock,
' duplicate request state, and payment validity."
'
' NO CashierSessionId. The server derives it from the CALLING user's own open
' session (SaleService.CompleteAsync, CashierSessionRepository.
' GetOpenForUpdateByUserAsync) - a client-supplied session id would let one
' cashier's request name another cashier's session, which nothing about this
' endpoint's authorization model is designed to arbitrate. The same reasoning
' OpenCashierSessionRequest's own shape already applies to OpenedByUserId.

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Sales

    Public NotInheritable Class CreateSaleRequest

        <JsonPropertyName("lines")>
        Public Property Lines As IReadOnlyList(Of CreateSaleLineRequest) =
            Array.Empty(Of CreateSaleLineRequest)()

        <JsonPropertyName("payment")>
        Public Property Payment As CreateSalePaymentRequest

        ''' <summary>ADR-007: required, canonical 36-character UUID. A repeated key replays the original committed sale rather than completing a second one.</summary>
        <JsonPropertyName("idempotencyKey")>
        Public Property IdempotencyKey As String = String.Empty

    End Class

End Namespace
