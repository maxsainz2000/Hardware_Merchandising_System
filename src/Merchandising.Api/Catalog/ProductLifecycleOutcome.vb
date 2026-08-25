' Merchandising.Api.Catalog.ProductLifecycleOutcome
'
' Result of ProductLifecycleService.DeactivateAsync/ReactivateAsync. Mirrors
' Catalog.PriceChangeOutcome's shape: "product not found" and "already in
' the requested state" are expected outcomes of calling the endpoint, not
' exceptional conditions.

Imports Merchandising.Contracts.Products

Namespace Catalog

    Public Enum ProductLifecycleOutcomeKind
        Success
        ProductNotFound
        NoChange
    End Enum

    Public NotInheritable Class ProductLifecycleOutcome

        Public Property Kind As ProductLifecycleOutcomeKind
        Public Property Response As ProductResponse

        Private Sub New()
        End Sub

        Public Shared Function Success(response As ProductResponse) As ProductLifecycleOutcome
            Return New ProductLifecycleOutcome With {.Kind = ProductLifecycleOutcomeKind.Success, .Response = response}
        End Function

        Public Shared Function ProductNotFound() As ProductLifecycleOutcome
            Return New ProductLifecycleOutcome With {.Kind = ProductLifecycleOutcomeKind.ProductNotFound}
        End Function

        Public Shared Function NoChange() As ProductLifecycleOutcome
            Return New ProductLifecycleOutcome With {.Kind = ProductLifecycleOutcomeKind.NoChange}
        End Function

    End Class

End Namespace
