' Merchandising.Contracts.Reporting.PaymentMethodTotalResponse
'
' P6-02: one row of "Method -> recorded total" - the shape
' CashierSessionRepository.GetPaymentTotalsAsync (P5-04) already returns as a
' plain tuple for a single session, pulled out here as a wire type because
' three of this card's four reports need the SAME shape (daily sales
' summary's PaymentTotals, sales-by-cashier's per-cashier PaymentTotals, and
' the payment-method summary itself) - one type, not three copies.
'
' Always one row per Merchandising.Domain.Sales.PaymentMethod name, COALESCEd
' to 0.0000 for a method with no committed SalePayments rows in scope - the
' same "always all three methods, zero-filled" rule GetPaymentTotalsAsync
' already establishes, so a caller never has to guess whether a missing
' method means zero or means "not computed."

Imports System.Text.Json.Serialization

Namespace Reporting

    Public NotInheritable Class PaymentMethodTotalResponse

        ''' <summary>One of "Cash", "Card", "EWallet" - Merchandising.Domain.Sales.PaymentMethod's enum names.</summary>
        <JsonPropertyName("method")>
        Public Property Method As String = String.Empty

        <JsonPropertyName("amount")>
        Public Property Amount As Decimal

    End Class

End Namespace
