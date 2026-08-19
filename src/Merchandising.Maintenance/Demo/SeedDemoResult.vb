' Merchandising.Maintenance.Demo.SeedDemoResult
'
' What a seed-demo run did, so the operator is told whether anything changed
' rather than just "OK". The command is re-runnable, and a second run that
' quietly reported success would be indistinguishable from a first one.

Namespace Demo

    ''' <summary>Outcome of one <c>seed-demo</c> run.</summary>
    Public NotInheritable Class SeedDemoResult

        Public Sub New(productId As Integer,
                       productCreated As Boolean,
                       openingApplied As Boolean,
                       quantityOnHand As Decimal,
                       actorUserId As Integer)

            Me.ProductId = productId
            Me.ProductCreated = productCreated
            Me.OpeningApplied = openingApplied
            Me.QuantityOnHand = quantityOnHand
            Me.ActorUserId = actorUserId

        End Sub

        ''' <summary>The demo product's Id - what to type into the client window.</summary>
        Public ReadOnly Property ProductId As Integer

        ''' <summary>True when this run created the product rather than finding it.</summary>
        Public ReadOnly Property ProductCreated As Boolean

        ''' <summary>True when this run posted the opening-balance movement.</summary>
        Public ReadOnly Property OpeningApplied As Boolean

        ''' <summary>Quantity on hand after the run.</summary>
        Public ReadOnly Property QuantityOnHand As Decimal

        ''' <summary>The user recorded as actor on the opening movement.</summary>
        Public ReadOnly Property ActorUserId As Integer

    End Class

End Namespace
