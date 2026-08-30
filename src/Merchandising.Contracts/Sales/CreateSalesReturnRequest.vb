' Merchandising.Contracts.Sales.CreateSalesReturnRequest
'
' POST /api/v1/sales/{saleId}/returns body. NO SaleId property - the route
' itself names the sale, the same "nothing for a body value to disagree
' with" reasoning RecordPurchaseReturnRequest's header gives.
'
' RefundMethod IS PROVISIONAL, NOT PERSISTED WHEN THE RETURN ESCALATES.
' 0012_sales-returns.sql defers RefundMethod/RefundAmount on the SalesReturns
' row until the return actually reaches Completed - immediately, when this
' request's lines are within the cashier's permitted scope, or later, via
' SalesReturnsController.ApproveExceptional, when they are not. So this
' field is used only on the immediate-complete path; an escalated
' (PendingApproval) return discards it and asks the approver to record the
' method actually used when they finalise the return (a different actor may
' decide a different method entirely for an exceptional case).

Imports System.Collections.Generic
Imports System.Text.Json.Serialization

Namespace Sales

    Public NotInheritable Class CreateSalesReturnRequest

        <JsonPropertyName("lines")>
        Public Property Lines As IReadOnlyList(Of CreateSalesReturnLineRequest) =
            Array.Empty(Of CreateSalesReturnLineRequest)()

        ''' <summary>Spec section 10.3: a return "records whether the item is eligible to re-enter stock" and the payment reversal, per-event, not per-line - 0012's SalesReturns.Reason column.</summary>
        <JsonPropertyName("reason")>
        Public Property Reason As String = String.Empty

        ''' <summary>One of "Cash", "Card", "EWallet" - Merchandising.Domain.Sales.PaymentMethod's enum names. Used only when this return completes immediately - see this file's header.</summary>
        <JsonPropertyName("refundMethod")>
        Public Property RefundMethod As String = String.Empty

        ''' <summary>ADR-007: required, canonical 36-character UUID. A repeated key replays the original committed return rather than recording a second one.</summary>
        <JsonPropertyName("idempotencyKey")>
        Public Property IdempotencyKey As String = String.Empty

    End Class

End Namespace
