' Merchandising.Domain.Entities.StockMovementItem
'
' P4-11: one row of StockRepository.SearchMovementsAsync - a StockMovements
' row (db/migrations/0001_foundation.sql), read back exactly as
' StockMovementWriter wrote it. Append-only source, so this is a plain
' projection with no computed field.
'
' Domain depends on nothing (CLAUDE.md section 4).

Namespace Entities

    ''' <summary>One StockMovements row, for the Stock.ReviewMovements ledger surface.</summary>
    Public NotInheritable Class StockMovementItem

        Public Property Id As Integer
        Public Property ProductId As Integer
        Public Property Delta As Decimal
        Public Property QuantityBefore As Decimal
        Public Property QuantityAfter As Decimal
        Public Property Reason As String = String.Empty
        Public Property ActorUserId As Integer
        Public Property CorrelationId As String = String.Empty
        Public Property CreatedAtUtc As DateTime

    End Class

End Namespace
