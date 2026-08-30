' Merchandising.Contracts.Sales.CashierSessionResponse
'
' A cashier session - Open (a sale may attach to it) or Closed (immutable,
' spec section 10.3). DeclaredCash/CalculatedCash/CashVariance already exist
' on CashierSessions (0011_pos.sql - migration 0012 declares them ahead of
' the card that drives them, the same StockCounts/StockAdjustments
' precedent); P5-04's OpenAsync leaves them Nothing, P5-05's CloseAsync sets
' them, once, and never recomputes them on a later read.
'
' PaymentTotals IS NOT A STORED COLUMN (P5-05, card Done-when box 3). It is
' computed live from Sales/SalePayments every time a response is built for
' a Closed session (CashierSessionService.ToResponse) - safe because no
' further sale can ever attach to a session once Closed, so the aggregate
' can never drift after the fact the way a stored running total could
' (ADR-021). Empty for an Open session - there is nothing final to report
' yet.

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Sales

    Public NotInheritable Class CashierSessionResponse

        <JsonPropertyName("id")>
        Public Property Id As Integer

        <JsonPropertyName("openedByUserId")>
        Public Property OpenedByUserId As Integer

        <JsonPropertyName("closedByUserId")>
        Public Property ClosedByUserId As Integer?

        <JsonPropertyName("openingFloat")>
        Public Property OpeningFloat As Decimal

        <JsonPropertyName("declaredCash")>
        Public Property DeclaredCash As Decimal?

        <JsonPropertyName("calculatedCash")>
        Public Property CalculatedCash As Decimal?

        <JsonPropertyName("cashVariance")>
        Public Property CashVariance As Decimal?

        <JsonPropertyName("status")>
        Public Property Status As String = String.Empty

        <JsonPropertyName("openedAtUtc")>
        Public Property OpenedAtUtc As DateTime

        <JsonPropertyName("closedAtUtc")>
        Public Property ClosedAtUtc As DateTime?

        <JsonPropertyName("rowVersion")>
        Public Property RowVersion As Long

        <JsonPropertyName("createdAtUtc")>
        Public Property CreatedAtUtc As DateTime

        <JsonPropertyName("updatedAtUtc")>
        Public Property UpdatedAtUtc As DateTime

        ''' <summary>Committed totals by payment method. Empty while Open.</summary>
        <JsonPropertyName("paymentTotals")>
        Public Property PaymentTotals As IReadOnlyList(Of CashierSessionPaymentTotalResponse) = Array.Empty(Of CashierSessionPaymentTotalResponse)()

    End Class

End Namespace
