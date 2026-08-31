' Merchandising.Contracts.Reporting.StockAdjustmentReportItemResponse
'
' P6-05: spec section 14 row 11 - "Count/adjustment, variance, reason,
' requestor, approver, result, and stock effect." This report is the FIRST
' place StockAdjustments becomes queryable through a GET route at all - P4-10
' only ever added POST routes (AdjustmentsController's own header confirms
' this by inspection). "Count/adjustment" is Id; StockAdjustments carries no
' StockCountId (0010_counts-and-adjustments.sql's own header: "no count
' linkage named"), so there is nothing to join or distinguish here.
'
' "RESULT" IS Status, READ AND ECHOED VERBATIM (ADR-020's sibling status
' machine for adjustments, Merchandising.Domain.Inventory.StockAdjustmentStatus)
' - never re-decided here, the same discipline P6-04 applies to purchase-order
' Status.
'
' "STOCK EFFECT" IS StockEffectApplied, A BOOLEAN, NOT A JOINED MOVEMENT ROW -
' StockAdjustments carries no MovementId column (AdjustmentService.ApplyStockEffectAsync's
' return value is never persisted back onto the adjustment; AdjustmentResponse.MovementId
' is populated only in the same HTTP response that just wrote it). True only
' when Status = "Applied" (AdjustmentService never calls ApplyStockEffectAsync
' for a Pending or Rejected row - that class's own header, "the ONLY two
' places that ever move stock for an adjustment"). The actual quantity effect,
' when applied, IS QuantityVariance - there is no second figure to report.

Imports System.Text.Json.Serialization

Namespace Reporting

    Public NotInheritable Class StockAdjustmentReportItemResponse

        <JsonPropertyName("id")>
        Public Property Id As Integer

        <JsonPropertyName("productId")>
        Public Property ProductId As Integer

        <JsonPropertyName("productSku")>
        Public Property ProductSku As String = String.Empty

        <JsonPropertyName("productName")>
        Public Property ProductName As String = String.Empty

        <JsonPropertyName("quantityVariance")>
        Public Property QuantityVariance As Decimal

        <JsonPropertyName("reason")>
        Public Property Reason As String = String.Empty

        <JsonPropertyName("requestedByUserId")>
        Public Property RequestedByUserId As Integer

        <JsonPropertyName("requestedByUsername")>
        Public Property RequestedByUsername As String = String.Empty

        <JsonPropertyName("approvedByUserId")>
        Public Property ApprovedByUserId As Integer?

        <JsonPropertyName("approvedByUsername")>
        Public Property ApprovedByUsername As String = Nothing

        <JsonPropertyName("exceedsThreshold")>
        Public Property ExceedsThreshold As Boolean

        ''' <summary>"Pending", "Rejected", or "Applied" - StockAdjustmentStatus's name, verbatim. "Approved" is never observed here (AdjustmentService's own header).</summary>
        <JsonPropertyName("status")>
        Public Property Status As String = String.Empty

        ''' <summary>True only when Status = "Applied" - this class's header.</summary>
        <JsonPropertyName("stockEffectApplied")>
        Public Property StockEffectApplied As Boolean

        <JsonPropertyName("createdAtUtc")>
        Public Property CreatedAtUtc As DateTime

        <JsonPropertyName("updatedAtUtc")>
        Public Property UpdatedAtUtc As DateTime

    End Class

End Namespace
