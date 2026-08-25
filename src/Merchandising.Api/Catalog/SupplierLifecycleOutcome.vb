' Merchandising.Api.Catalog.SupplierLifecycleOutcome
'
' Result of SupplierLifecycleService.DeactivateAsync/ReactivateAsync. Mirrors
' ProductLifecycleOutcome's shape exactly (P2-09/P2-10): "supplier not found"
' and "already in the requested state" are expected outcomes of calling the
' endpoint, not exceptional conditions.

Imports Merchandising.Contracts.Suppliers

Namespace Catalog

    Public Enum SupplierLifecycleOutcomeKind
        Success
        SupplierNotFound
        NoChange
    End Enum

    Public NotInheritable Class SupplierLifecycleOutcome

        Public Property Kind As SupplierLifecycleOutcomeKind
        Public Property Response As SupplierResponse

        Private Sub New()
        End Sub

        Public Shared Function Success(response As SupplierResponse) As SupplierLifecycleOutcome
            Return New SupplierLifecycleOutcome With {.Kind = SupplierLifecycleOutcomeKind.Success, .Response = response}
        End Function

        Public Shared Function SupplierNotFound() As SupplierLifecycleOutcome
            Return New SupplierLifecycleOutcome With {.Kind = SupplierLifecycleOutcomeKind.SupplierNotFound}
        End Function

        Public Shared Function NoChange() As SupplierLifecycleOutcome
            Return New SupplierLifecycleOutcome With {.Kind = SupplierLifecycleOutcomeKind.NoChange}
        End Function

    End Class

End Namespace
