' Merchandising.Domain.Inventory.AdjustmentThresholdPolicy
'
' P4-10: spec section 10.2's "Adjustment approval is required when the
' absolute variance exceeds the configured threshold." The card's own "Do"
' text sharpens the boundary: "Below the configured threshold it applies
' directly; at or above it requires a second person's approval" - so the
' comparison is >=, not >, matching StockAdjustments.ExceedsThreshold's own
' name (0010's migration: "captured at request time, never recomputed").
'
' Pure function, no ASP.NET Core or database dependency - the same
' "PurchaseOrderTransitions.CanTransition" shape: a business rule Domain
' owns, that Api calls once and stores the result, rather than a rule
' re-evaluated on every later read.

Namespace Inventory

    Public NotInheritable Class AdjustmentThresholdPolicy

        Private Sub New()
        End Sub

        ''' <summary>
        ''' True when the absolute value of <paramref name="quantityVariance"/>
        ''' is at or above <paramref name="threshold"/> - the adjustment
        ''' requires a second person's approval rather than applying directly.
        ''' </summary>
        Public Shared Function ExceedsThreshold(quantityVariance As Decimal, threshold As Decimal) As Boolean
            Return Math.Abs(quantityVariance) >= threshold
        End Function

    End Class

End Namespace
