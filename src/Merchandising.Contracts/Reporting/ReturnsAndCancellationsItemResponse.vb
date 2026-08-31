' Merchandising.Contracts.Reporting.ReturnsAndCancellationsItemResponse
'
' P6-03: spec section 14 row 5 - "Return/cancellation identifiers, source
' sale, product, quantity, reason, actor, approval, and stock effect." One
' row per SalesReturnLines row (SalesReturnId/LineId together are the
' "identifiers"), flattened with its parent SalesReturns header - there is
' no aggregation here, so this is a plain multi-table join, not the
' pre-aggregated-subquery shape ReportRepository's header requires for a
' SUM() over more than one one-to-many relation.
'
' EVERY STATUS APPEARS, NOT ONLY Completed. This report's entire subject is
' return/cancellation ACTIVITY (docs/report-specification.md section 4 row
' 5: "there is nothing to exclude"), and the "approval" column spec section
' 14 names is meaningless if only already-decided rows were ever shown - a
' PendingApproval row IS the fact that something is awaiting approval, and a
' Rejected row IS the outcome of an approval decision. Status is the
' SalesReturnStatus enum name verbatim (PendingApproval/Completed/Rejected) -
' never a bare "Approved", so this is untouched by PaymentWordingTests'
' denylist the same way SalesReturnResponse.Status already is.
'
' STOCK EFFECT IS RestocksItem, NOT A COMPUTED "was stock actually
' incremented" FLAG. P5-11's flag already distinguishes a line that is
' eligible to re-enter stock from one that is not; P6-03's own Done-when box
' asks for exactly that distinction, not a second one layered on top -
' whether the increment has actually happened yet is recoverable from Status
' (only Completed has committed a stock effect, PendingApproval/Rejected
' never have, per SalesReturnService's own header).

Imports System.Text.Json.Serialization

Namespace Reporting

    Public NotInheritable Class ReturnsAndCancellationsItemResponse

        <JsonPropertyName("salesReturnId")>
        Public Property SalesReturnId As Integer

        <JsonPropertyName("salesReturnLineId")>
        Public Property SalesReturnLineId As Integer

        <JsonPropertyName("saleId")>
        Public Property SaleId As Integer

        <JsonPropertyName("productId")>
        Public Property ProductId As Integer

        <JsonPropertyName("productSku")>
        Public Property ProductSku As String = String.Empty

        <JsonPropertyName("productName")>
        Public Property ProductName As String = String.Empty

        <JsonPropertyName("quantityReturned")>
        Public Property QuantityReturned As Decimal

        <JsonPropertyName("reason")>
        Public Property Reason As String = String.Empty

        <JsonPropertyName("returnedByUserId")>
        Public Property ReturnedByUserId As Integer

        <JsonPropertyName("returnedByUsername")>
        Public Property ReturnedByUsername As String = String.Empty

        ''' <summary>Null until Completed or Rejected - same nullability as SalesReturnResponse.ApprovedByUserId.</summary>
        <JsonPropertyName("approvedByUserId")>
        Public Property ApprovedByUserId As Integer?

        ''' <summary>Null until Completed or Rejected.</summary>
        <JsonPropertyName("approvedByUsername")>
        Public Property ApprovedByUsername As String

        ''' <summary>One of Merchandising.Domain.Sales.SalesReturnStatus's names - PendingApproval, Completed or Rejected. The "approval" column spec section 14 asks for.</summary>
        <JsonPropertyName("status")>
        Public Property Status As String = String.Empty

        ''' <summary>P5-11's flag, echoed - the stock effect column. See this class's header for why no second flag is layered on top.</summary>
        <JsonPropertyName("restocksItem")>
        Public Property RestocksItem As Boolean

        <JsonPropertyName("returnedAtUtc")>
        Public Property ReturnedAtUtc As DateTime

    End Class

End Namespace
