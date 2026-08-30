' Merchandising.Contracts.Sales.SalePaymentResponse
'
' The committed payment record. Status is ALWAYS the literal "Recorded" -
' never "Approved", "Authorised", or "Accepted" (G-24, CLAUDE.md section 5:
' "the application must make this distinction clear to users"). This is the
' one field this card's own Done-when box asks the RESPONSE to carry the
' wording in - P5-12 is the card that asserts the same discipline in UI
' labels and report headers; this is the data shape that wording sits on top
' of, and both must agree because P5-12 reads this response's shape rather
' than re-deriving its own.

Imports System.Text.Json.Serialization

Namespace Sales

    Public NotInheritable Class SalePaymentResponse

        ''' <summary>G-24's stable wording. Every SalePaymentResponse carries exactly this value, whatever the method - cash is no more "approved" than a card payment is.</summary>
        Public Const RecordedStatus As String = "Recorded"

        <JsonPropertyName("id")>
        Public Property Id As Integer

        ''' <summary>One of "Cash", "Card", "EWallet" - Merchandising.Domain.Sales.PaymentMethod's enum names.</summary>
        <JsonPropertyName("method")>
        Public Property Method As String = String.Empty

        ''' <summary>Always equal to the sale's Total - the one payment covers it in full (see CreateSalePaymentRequest's header).</summary>
        <JsonPropertyName("amount")>
        Public Property Amount As Decimal

        ''' <summary>Cash only. Nothing for Card/EWallet.</summary>
        <JsonPropertyName("tenderedAmount")>
        Public Property TenderedAmount As Decimal?

        ''' <summary>Cash only, computed once and stored (never recomputed on read) - Merchandising.Domain.Sales.CashTender, P5-01. Nothing for Card/EWallet.</summary>
        <JsonPropertyName("changeAmount")>
        Public Property ChangeAmount As Decimal?

        ''' <summary>Always <see cref="RecordedStatus"/> - see this class's header (G-24).</summary>
        <JsonPropertyName("status")>
        Public Property Status As String = RecordedStatus

    End Class

End Namespace
