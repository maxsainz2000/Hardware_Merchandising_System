' Merchandising.Inventory.ViewModels.ReceivingLineEntryViewModel
'
' One row of the Receive tab's drafted-lines grid. Text-backed, the same
' reasoning PurchaseOrderLineEntryViewModel (Merchandising.Procurement)
' already gives: a bad value reads as a validation message rather than a WPF
' binding failure. This view model does not decide whether a quantity would
' over-receive - that bound (received-minus-prior-receipts against the
' ordered quantity) is ReceivingService's to enforce (P4-07), and
' ReceivingViewModel surfaces its refusal verbatim if this line turns out to
' exceed it.

Imports Merchandising.ClientCommon.Mvvm

Namespace ViewModels

    ''' <summary>One editable line of a receipt being drafted.</summary>
    Public NotInheritable Class ReceivingLineEntryViewModel
        Inherits ObservableObject

        Private _purchaseOrderLineId As Integer
        Private _productDisplay As String = String.Empty
        Private _quantityReceived As String = String.Empty
        Private _cost As String = String.Empty

        ''' <summary>The purchase-order line this receipt line is against. Set by picking a row from the loaded order.</summary>
        Public Property PurchaseOrderLineId As Integer
            Get
                Return _purchaseOrderLineId
            End Get
            Set(value As Integer)
                SetProperty(_purchaseOrderLineId, value)
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

        Public Property QuantityReceived As String
            Get
                Return _quantityReceived
            End Get
            Set(value As String)
                SetProperty(_quantityReceived, value)
            End Set
        End Property

        Public Property Cost As String
            Get
                Return _cost
            End Get
            Set(value As String)
                SetProperty(_cost, value)
            End Set
        End Property

    End Class

End Namespace
