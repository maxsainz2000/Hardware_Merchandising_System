' Merchandising.Contracts.Reporting.SalesByCashierItemResponse
'
' P6-02: one row of spec section 14's "Sales by cashier" - "Completed sales,
' returns, net value, transaction count, and payment totals by cashier/
' session."
'
' "BY CASHIER", NOT "BY SESSION" - SalesReturns (0012_sales-returns.sql)
' carries no CashierSessionId column, only ReturnedByUserId, so a return
' cannot be attributed to a session at all, only to the user who processed
' it. Grouping this report by CashierUserId (Sales) / ReturnedByUserId
' (SalesReturns) rather than by CashierSessionId is therefore the only
' grouping the schema supports - confirmed by inspection before this card
' was written. A cashier who processes a return DURING A DIFFERENT SESSION
' than the one that made the original sale still has that return counted
' against them here, which is correct for cash-drawer accountability: the
' refund leaves whichever drawer is open when it happens.
'
' ONLY CASHIERS WITH AT LEAST ONE COMPLETED SALE IN THE PERIOD APPEAR - a
' cashier who only processed returns in the window, with no sale of their
' own, is not listed (SalesByCashierResponse's own header explains the same
' choice SalesByProductResponse makes for an unsold product).

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Reporting

    Public NotInheritable Class SalesByCashierItemResponse

        <JsonPropertyName("cashierUserId")>
        Public Property CashierUserId As Integer

        <JsonPropertyName("cashierUsername")>
        Public Property CashierUsername As String = String.Empty

        <JsonPropertyName("completedSalesCount")>
        Public Property CompletedSalesCount As Integer

        <JsonPropertyName("completedSalesTotal")>
        Public Property CompletedSalesTotal As Decimal

        <JsonPropertyName("returnsCount")>
        Public Property ReturnsCount As Integer

        <JsonPropertyName("returnsTotal")>
        Public Property ReturnsTotal As Decimal

        ''' <summary>CompletedSalesTotal - ReturnsTotal.</summary>
        <JsonPropertyName("netValue")>
        Public Property NetValue As Decimal

        <JsonPropertyName("paymentTotals")>
        Public Property PaymentTotals As IReadOnlyList(Of PaymentMethodTotalResponse) =
            Array.Empty(Of PaymentMethodTotalResponse)()

    End Class

End Namespace
