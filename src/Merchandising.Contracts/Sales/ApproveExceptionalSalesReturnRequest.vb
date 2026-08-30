' Merchandising.Contracts.Sales.ApproveExceptionalSalesReturnRequest
'
' POST /api/v1/sales/returns/{id}/approve-exceptional body. NO idempotency
' key - approving a status transition is safe to retry without one, the
' same reasoning PurchaseOrdersController.ApprovePurchaseOrder and
' AdjustmentsController.ApproveAdjustment's headers already give.
'
' RefundMethod is REQUIRED here, not carried over from the original request -
' see CreateSalesReturnRequest's header for why: 0012_sales-returns.sql
' defers RefundMethod/RefundAmount until the return actually completes, and
' the approver is the one who decides how the store records the refund for
' an exceptional case.

Imports System.Text.Json.Serialization

Namespace Sales

    Public NotInheritable Class ApproveExceptionalSalesReturnRequest

        ''' <summary>One of "Cash", "Card", "EWallet" - Merchandising.Domain.Sales.PaymentMethod's enum names.</summary>
        <JsonPropertyName("refundMethod")>
        Public Property RefundMethod As String = String.Empty

    End Class

End Namespace
