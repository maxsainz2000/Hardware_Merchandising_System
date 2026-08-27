' Merchandising.Procurement.ViewModels.PurchaseOrderLineEntryViewModel
'
' One row of the new-order lines grid. Text-backed, the same reasoning
' SpikeViewModel's ProductId/Quantity fields already used: a bad value reads
' as a validation message rather than a WPF binding failure that never
' reaches the operator.
'
' This view model does NOT decide whether a quantity or cost is valid beyond
' "is this text parseable as a positive number" - scale limits (DECIMAL(19,3)
' quantity, DECIMAL(19,4) cost) and reference checks (does the product exist,
' is it active) are exactly the rules CLAUDE.md section 5 requires the API to
' re-check regardless, and NewPurchaseOrderViewModel surfaces the API's own
' field-level errors rather than duplicating ADR-004.1's scale rule here.

Imports Merchandising.ClientCommon.Mvvm

Namespace ViewModels

    ''' <summary>One editable line of a purchase order being drafted.</summary>
    Public NotInheritable Class PurchaseOrderLineEntryViewModel
        Inherits ObservableObject

        Private _productId As String = String.Empty
        Private _productDisplay As String = String.Empty
        Private _orderedQuantity As String = String.Empty
        Private _purchaseCost As String = String.Empty

        ''' <summary>The Id this line will submit. Set by picking a product search result.</summary>
        Public Property ProductId As String
            Get
                Return _productId
            End Get
            Set(value As String)
                SetProperty(_productId, value)
            End Set
        End Property

        ''' <summary>SKU and name, for the operator to recognise the line without memorising an Id.</summary>
        Public Property ProductDisplay As String
            Get
                Return _productDisplay
            End Get
            Set(value As String)
                SetProperty(_productDisplay, value)
            End Set
        End Property

        Public Property OrderedQuantity As String
            Get
                Return _orderedQuantity
            End Get
            Set(value As String)
                SetProperty(_orderedQuantity, value)
            End Set
        End Property

        Public Property PurchaseCost As String
            Get
                Return _purchaseCost
            End Get
            Set(value As String)
                SetProperty(_purchaseCost, value)
            End Set
        End Property

    End Class

End Namespace
