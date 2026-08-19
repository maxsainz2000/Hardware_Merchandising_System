' Merchandising.Api.Inventory.StockDecrementOutcome
'
' Result of StockService.DecrementAsync. Mirrors Security.LoginOutcome's
' shape: "insufficient stock" is an expected outcome of calling the
' endpoint, not an exceptional condition, so InventoryController maps it to
' a specific status code (409) rather than catching an exception.

Imports Merchandising.Contracts.Inventory

Namespace Inventory

    Public Enum StockDecrementOutcomeKind
        Success
        InsufficientStock
    End Enum

    Public NotInheritable Class StockDecrementOutcome

        Public Property Kind As StockDecrementOutcomeKind
        Public Property Response As StockDecrementResponse

        Private Sub New()
        End Sub

        Public Shared Function Success(response As StockDecrementResponse) As StockDecrementOutcome
            Return New StockDecrementOutcome With {.Kind = StockDecrementOutcomeKind.Success, .Response = response}
        End Function

        Public Shared Function InsufficientStock() As StockDecrementOutcome
            Return New StockDecrementOutcome With {.Kind = StockDecrementOutcomeKind.InsufficientStock}
        End Function

    End Class

End Namespace
