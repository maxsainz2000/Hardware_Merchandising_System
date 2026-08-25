' Merchandising.Api.Catalog.PriceChangeOutcome
'
' Result of PriceChangeService.ChangePriceAsync. Mirrors Inventory.
' StockDecrementOutcome's shape: "product not found" and "nothing would
' change" are expected outcomes of calling the endpoint, not exceptional
' conditions, so ProductsController maps them to specific status codes
' rather than catching an exception.

Imports Merchandising.Contracts.Products

Namespace Catalog

    Public Enum PriceChangeOutcomeKind
        Success
        ProductNotFound
        NoChange
    End Enum

    Public NotInheritable Class PriceChangeOutcome

        Public Property Kind As PriceChangeOutcomeKind
        Public Property Response As ProductResponse

        Private Sub New()
        End Sub

        Public Shared Function Success(response As ProductResponse) As PriceChangeOutcome
            Return New PriceChangeOutcome With {.Kind = PriceChangeOutcomeKind.Success, .Response = response}
        End Function

        Public Shared Function ProductNotFound() As PriceChangeOutcome
            Return New PriceChangeOutcome With {.Kind = PriceChangeOutcomeKind.ProductNotFound}
        End Function

        Public Shared Function NoChange() As PriceChangeOutcome
            Return New PriceChangeOutcome With {.Kind = PriceChangeOutcomeKind.NoChange}
        End Function

    End Class

End Namespace
