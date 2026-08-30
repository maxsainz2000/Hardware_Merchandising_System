' Merchandising.Contracts.Sales.SaleResponse
'
' 201 (or replayed 200) body for POST /api/v1/sales - spec section 11's
' "Sale completed" atomic-result row, built directly from what
' SaleService.CompleteAsync just wrote inside its own transaction (never a
' re-read after commit - every value here is already known before commit,
' the same reasoning ReceivingService could have used but did not need to;
' this card's response has nothing left to look up).

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Sales

    Public NotInheritable Class SaleResponse

        <JsonPropertyName("id")>
        Public Property Id As Integer

        <JsonPropertyName("cashierSessionId")>
        Public Property CashierSessionId As Integer

        <JsonPropertyName("cashierUserId")>
        Public Property CashierUserId As Integer

        <JsonPropertyName("total")>
        Public Property Total As Decimal

        ''' <summary>Always "Completed" - a sale row is only ever inserted already-Completed (Merchandising.Domain.Sales.SaleStatus's own header).</summary>
        <JsonPropertyName("status")>
        Public Property Status As String = String.Empty

        <JsonPropertyName("correlationId")>
        Public Property CorrelationId As String = String.Empty

        <JsonPropertyName("createdAtUtc")>
        Public Property CreatedAtUtc As DateTime

        <JsonPropertyName("lines")>
        Public Property Lines As IReadOnlyList(Of SaleLineResponse) =
            Array.Empty(Of SaleLineResponse)()

        <JsonPropertyName("payment")>
        Public Property Payment As SalePaymentResponse

    End Class

End Namespace
