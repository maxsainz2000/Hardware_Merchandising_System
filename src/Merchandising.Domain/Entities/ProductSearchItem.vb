' Merchandising.Domain.Entities.ProductSearchItem
'
' P5-06: one row of ProductRepository.SearchAsync - a Products row LEFT
' JOINed to its StockBalances row, the same "product that has never been
' touched by a stock-changing command has no StockBalances row at all, so
' AvailableStock here is COALESCE(b.Quantity, 0), never a missing value"
' shape StockBalanceItem already established for Stock.Read/LowStock.Review.
'
' Domain depends on nothing (CLAUDE.md section 4).

Namespace Entities

    ''' <summary>A product plus its current available stock, for the POS/Inventory lookup (spec section 10.3).</summary>
    Public NotInheritable Class ProductSearchItem

        Public Property Product As Product
        Public Property AvailableStock As Decimal

    End Class

End Namespace
