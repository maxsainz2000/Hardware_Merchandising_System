' Merchandising.Contracts.Sales.SalesReturnResponse
'
' The committed SalesReturns row plus its lines - returned by
' SalesReturnsController's Create/ApproveExceptional/RejectExceptional
' actions, and stored verbatim as the idempotency replay payload for Create
' (ADR-007).

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Sales

    Public NotInheritable Class SalesReturnResponse

        <JsonPropertyName("id")>
        Public Property Id As Integer

        <JsonPropertyName("saleId")>
        Public Property SaleId As Integer

        ''' <summary>One of Merchandising.Domain.Sales.SalesReturnStatus's names - PendingApproval, Completed or Rejected.</summary>
        <JsonPropertyName("status")>
        Public Property Status As String = String.Empty

        <JsonPropertyName("returnedByUserId")>
        Public Property ReturnedByUserId As Integer

        ''' <summary>Null until Completed or Rejected.</summary>
        <JsonPropertyName("approvedByUserId")>
        Public Property ApprovedByUserId As Integer?

        <JsonPropertyName("reason")>
        Public Property Reason As String = String.Empty

        <JsonPropertyName("exceedsThreshold")>
        Public Property ExceedsThreshold As Boolean

        ''' <summary>Null until Completed. Recorded operationally only - never an external bank/terminal reversal (G-24).</summary>
        <JsonPropertyName("refundMethod")>
        Public Property RefundMethod As String

        ''' <summary>Null until Completed.</summary>
        <JsonPropertyName("refundAmount")>
        Public Property RefundAmount As Decimal?

        <JsonPropertyName("returnedAtUtc")>
        Public Property ReturnedAtUtc As DateTime

        <JsonPropertyName("approvedAtUtc")>
        Public Property ApprovedAtUtc As DateTime?

        <JsonPropertyName("rowVersion")>
        Public Property RowVersion As Long

        <JsonPropertyName("createdAtUtc")>
        Public Property CreatedAtUtc As DateTime

        <JsonPropertyName("updatedAtUtc")>
        Public Property UpdatedAtUtc As DateTime

        <JsonPropertyName("lines")>
        Public Property Lines As IReadOnlyList(Of SalesReturnLineResponse) =
            Array.Empty(Of SalesReturnLineResponse)()

    End Class

End Namespace
