' Merchandising.Contracts.Reporting.DailySalesSummaryResponse
'
' P6-02: spec section 14's first report - "Completed sales, completed
' returns, net sales total, transaction count, and payment totals for the
' selected store-local day." Singular DAY, not a range - unlike the other
' three Track B reports in this card, this one takes exactly one required
' `date` query parameter rather than the optional fromDate/toDate range
' docs/report-specification.md section 3 inherits from P3-03; Range still
' carries the shared ReportRangeEnvelope shape (FromDate = ToDate = the
' requested date) so the response echoes what was applied the same way
' every other report in this document does, per spec section 14's own
' universal requirement to state the selected range.
'
' Returns treatment: Included (docs/report-specification.md section 4, row 1) -
' CompletedReturnsCount/CompletedReturnsTotal are real figures on this
' response, not merely a treatment label, and NetSalesTotal =
' CompletedSalesTotal - CompletedReturnsTotal.
'
' PaymentTotals reads committed SalePayments for COMPLETED SALES in the
' window - it is not netted against returns (see report-specification.md
' section 4 row 4's reasoning for the payment-method summary: which side of
' a return absorbs a refund is a decision this document does not make).

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Reporting

    Public NotInheritable Class DailySalesSummaryResponse

        <JsonPropertyName("range")>
        Public Property Range As ReportRangeEnvelope

        <JsonPropertyName("completedSalesCount")>
        Public Property CompletedSalesCount As Integer

        <JsonPropertyName("completedSalesTotal")>
        Public Property CompletedSalesTotal As Decimal

        <JsonPropertyName("completedReturnsCount")>
        Public Property CompletedReturnsCount As Integer

        <JsonPropertyName("completedReturnsTotal")>
        Public Property CompletedReturnsTotal As Decimal

        ''' <summary>CompletedSalesTotal - CompletedReturnsTotal.</summary>
        <JsonPropertyName("netSalesTotal")>
        Public Property NetSalesTotal As Decimal

        <JsonPropertyName("paymentTotals")>
        Public Property PaymentTotals As IReadOnlyList(Of PaymentMethodTotalResponse) =
            Array.Empty(Of PaymentMethodTotalResponse)()

    End Class

End Namespace
