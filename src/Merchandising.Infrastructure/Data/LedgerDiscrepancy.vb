' Merchandising.Infrastructure.Data.LedgerDiscrepancy
'
' One row of LedgerReconciliation.FindDiscrepanciesAsync's result - a single
' product whose StockMovements ledger does not sum to its StockBalances
' quantity. ExpectedQuantity/ActualQuantity/Delta are all DECIMAL(19,3) end
' to end (ADR-004): MySqlConnector maps the column type to System.Decimal,
' and the mismatch itself is decided in SQL by an exact DECIMAL comparison,
' never a floating-point tolerance computed here.

Namespace Data

    Public NotInheritable Class LedgerDiscrepancy

        Public ReadOnly Property ProductId As Integer
        Public ReadOnly Property ExpectedQuantity As Decimal
        Public ReadOnly Property ActualQuantity As Decimal
        Public ReadOnly Property Delta As Decimal

        Public Sub New(productId As Integer, expectedQuantity As Decimal, actualQuantity As Decimal)

            Me.ProductId = productId
            Me.ExpectedQuantity = expectedQuantity
            Me.ActualQuantity = actualQuantity
            Me.Delta = actualQuantity - expectedQuantity

        End Sub

    End Class

End Namespace
