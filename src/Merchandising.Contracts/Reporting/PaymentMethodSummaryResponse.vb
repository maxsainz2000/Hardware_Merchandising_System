' Merchandising.Contracts.Reporting.PaymentMethodSummaryResponse
'
' P6-02: spec section 14's fourth report - "Recorded cash, card, and e-wallet
' totals; explicitly labeled as operational recordings, not external
' settlement confirmation." G-24 (CLAUDE.md section 5, spec section 23):
' this is the one report row spec section 14 itself requires an explicit
' label on, so this response carries the wording directly, the same way
' SalePaymentResponse.RecordedStatus carries P5-12's wording for a single
' payment - PaymentWordingTests (P5-12, widened at this card) scans this
' file and asserts Note is never itself a denylist violation.
'
' Returns treatment: Excluded (docs/report-specification.md section 4, row 4) -
' Totals sums SalePayments for completed SALES only; a sales return's
' RefundMethod is a separate fact, reported by the Returns and cancellations
' report (P6-03), not netted in here - see report-specification.md section 4
' row 4's full reasoning for why that decision is not this document's to
' make.

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Reporting

    Public NotInheritable Class PaymentMethodSummaryResponse

        ''' <summary>
        ''' G-24's stable wording for this report - every word in the
        ''' PaymentWordingTests denylist (approved/authorised/authorized/
        ''' accepted/cleared/settled/charged) that appears below is
        ''' individually negated with "not" immediately before it, the only
        ''' shape the denylist regex accepts as correct wording (that test's
        ''' own header explains why).
        ''' </summary>
        Public Const RecordedNotAuthorisedNote As String =
            "Card and e-wallet totals shown here are recorded for this period only - not authorised, " &
            "not cleared, and not settled by any external processor. This is an operational recording, " &
            "never an external settlement confirmation."

        <JsonPropertyName("range")>
        Public Property Range As ReportRangeEnvelope

        <JsonPropertyName("totals")>
        Public Property Totals As IReadOnlyList(Of PaymentMethodTotalResponse) =
            Array.Empty(Of PaymentMethodTotalResponse)()

        ''' <summary>Always <see cref="RecordedNotAuthorisedNote"/> - see this class's header (G-24).</summary>
        <JsonPropertyName("note")>
        Public Property Note As String = RecordedNotAuthorisedNote

    End Class

End Namespace
