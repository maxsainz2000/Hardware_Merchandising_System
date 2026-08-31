' Merchandising.Contracts.Reporting.ProductPerformanceItemResponse
'
' P6-03: spec section 14 row 12 - "Net quantity, net sales value, recorded
' cost estimate, informational margin estimate, and current stock
' position."
'
' NetQuantity/NetSalesValue/RecordedCostEstimate ALL RESPECT THE SELECTED
' PERIOD (fromDate/toDate, same optional-independent contract as sales by
' product); CurrentStockQuantity DOES NOT - it reads StockBalances live,
' regardless of the period requested. This is deliberate, not an
' inconsistency: "current stock position" is a fact about right now, and a
' report that tried to compute a historical as-of-date balance would need a
' mechanism nothing in this schema provides (StockBalances has no history -
' StockMovements is the ledger, and reconstructing a point-in-time balance
' from it is a different feature this card does not build). P6-03's own
' Done-when box calls this "the mixed-temporality trap" and asks for it to
' be asserted, not merely stated.
'
' MarginEstimate IS INFORMATIONAL, LABELLED WHEREVER IT APPEARS -
' docs/report-specification.md section 5's own wording, spec section 14's
' explicit requirement. MarginEstimateLabel carries the literal word next to
' the figure, the same "the label is the whole mitigation" shape
' PaymentMethodSummaryResponse.RecordedNotAuthorisedNote uses for G-24.

Imports System.Text.Json.Serialization

Namespace Reporting

    Public NotInheritable Class ProductPerformanceItemResponse

        ''' <summary>Spec section 14's own wording - the entire mitigation for the margin estimate below (docs/report-specification.md section 5).</summary>
        Public Const MarginEstimateLabel As String =
            "Informational estimate from captured cost and captured sale price - not an accounting margin statement."

        <JsonPropertyName("productId")>
        Public Property ProductId As Integer

        <JsonPropertyName("productSku")>
        Public Property ProductSku As String = String.Empty

        <JsonPropertyName("productName")>
        Public Property ProductName As String = String.Empty

        <JsonPropertyName("quantitySold")>
        Public Property QuantitySold As Decimal

        <JsonPropertyName("quantityReturned")>
        Public Property QuantityReturned As Decimal

        <JsonPropertyName("netQuantity")>
        Public Property NetQuantity As Decimal

        ''' <summary>Gross sales value less the value of returned units (both read from captured SaleLines.UnitPrice - never Products.Price).</summary>
        <JsonPropertyName("netSalesValue")>
        Public Property NetSalesValue As Decimal

        ''' <summary>Captured cost basis of units sold less captured cost basis of units returned - never a join to Products.Cost (docs/report-specification.md section 5).</summary>
        <JsonPropertyName("recordedCostEstimate")>
        Public Property RecordedCostEstimate As Decimal

        ''' <summary>NetSalesValue minus RecordedCostEstimate, rounded once through DecimalScaleGuard.RoundMoney (docs/report-specification.md section 6). See <see cref="MarginEstimateLabel"/> - never shown without it.</summary>
        <JsonPropertyName("marginEstimate")>
        Public Property MarginEstimate As Decimal

        ''' <summary>Always <see cref="MarginEstimateLabel"/> - see this class's header.</summary>
        <JsonPropertyName("marginEstimateLabel")>
        Public Property MarginEstimateLabelText As String = MarginEstimateLabel

        ''' <summary>StockBalances, read LIVE - never scoped to the report's own date range. See this class's header for why.</summary>
        <JsonPropertyName("currentStockQuantity")>
        Public Property CurrentStockQuantity As Decimal

    End Class

End Namespace
