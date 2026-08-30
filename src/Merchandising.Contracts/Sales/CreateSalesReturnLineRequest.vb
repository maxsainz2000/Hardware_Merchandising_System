' Merchandising.Contracts.Sales.CreateSalesReturnLineRequest
'
' One line of a POST /api/v1/sales/{saleId}/returns body.
'
' NO ProductId AND NO UnitPrice. Both are derived server-side from the
' locked SaleLines row this line names - spec section 10.3's "must identify
' the original sale line", never a client-supplied product or price. The
' same trust boundary RecordPurchaseReturnLineRequest's header argues for
' the purchase-return side.

Imports System.Text.Json.Serialization

Namespace Sales

    Public NotInheritable Class CreateSalesReturnLineRequest

        <JsonPropertyName("saleLineId")>
        Public Property SaleLineId As Integer

        ''' <summary>Validated to DECIMAL(19,3) scale at the API boundary (ADR-004.1) before any parameter is bound. Bounded server-side by sold-minus-prior-returns (P5-11), never by a client-supplied figure.</summary>
        <JsonPropertyName("quantityReturned")>
        Public Property QuantityReturned As Decimal

        ''' <summary>Spec section 10.3: "whether the returned item is eligible to re-enter stock." True increments StockBalances and writes a stock-in movement (only once the return actually completes); False records the return operationally with no stock effect - e.g. a defective item.</summary>
        <JsonPropertyName("restocksItem")>
        Public Property RestocksItem As Boolean = True

    End Class

End Namespace
