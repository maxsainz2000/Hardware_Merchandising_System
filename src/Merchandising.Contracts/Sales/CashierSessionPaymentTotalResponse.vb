' Merchandising.Contracts.Sales.CashierSessionPaymentTotalResponse
'
' One payment method's committed total for a cashier session - spec section
' 10.3's "totals... by payment method" and section 14's payment-method
' summary ("recorded cash, card, and e-wallet totals; explicitly labeled as
' operational recordings, not external settlement confirmation").
'
' Method alone is enough for a client to apply that label itself (Card/
' EWallet are recorded, not authorised; Cash is the one method this session
' actually reconciles against a physical count) - the wording itself is
' P5-12's to write, not this card's (G-24).

Imports System.Text.Json.Serialization

Namespace Sales

    Public NotInheritable Class CashierSessionPaymentTotalResponse

        ''' <summary>A Merchandising.Domain.Sales.PaymentMethod name - Cash, Card, or EWallet.</summary>
        <JsonPropertyName("method")>
        Public Property Method As String = String.Empty

        ''' <summary>Sum of committed SalePayments.Amount for this method, across every sale attached to the session. 0.0000 when the method was never used.</summary>
        <JsonPropertyName("amount")>
        Public Property Amount As Decimal

    End Class

End Namespace
