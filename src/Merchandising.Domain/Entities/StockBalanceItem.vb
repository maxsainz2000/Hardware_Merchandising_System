' Merchandising.Domain.Entities.StockBalanceItem
'
' P4-11: one row of StockRepository.SearchStockAsync/SearchLowStockAsync - a
' Products row LEFT JOINed to its StockBalances row (a product that has never
' been touched by a stock-changing command has no StockBalances row at all,
' so Quantity here is COALESCE(b.Quantity, 0), never a missing value). Both
' the plain Stock.Read list and the LowStock.Review list share this shape;
' the low-stock query is simply this same projection with a WHERE clause
' applied server-side, not a different read.
'
' Domain depends on nothing (CLAUDE.md section 4).

Namespace Entities

    ''' <summary>A product's current stock position, for both the Stock.Read list and the LowStock.Review list.</summary>
    Public NotInheritable Class StockBalanceItem

        Public Property ProductId As Integer
        Public Property Sku As String = String.Empty
        Public Property Name As String = String.Empty
        Public Property IsActive As Boolean
        Public Property Quantity As Decimal
        Public Property ReorderLevel As Decimal

    End Class

End Namespace
