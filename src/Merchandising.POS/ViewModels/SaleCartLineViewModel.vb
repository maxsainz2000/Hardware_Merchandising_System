' Merchandising.POS.ViewModels.SaleCartLineViewModel
'
' One row of the Checkout tab's cart grid. Unlike ReceivingLineEntryViewModel
' (Merchandising.Inventory) this is not edited in place - a line is added
' whole from the selected product and quantity, and only removed, never
' retyped - so it carries no ObservableObject overhead, just the values the
' grid displays.
'
' EstimatedUnitPrice/EstimatedLineTotal ARE NOT WHAT THE SALE WILL CHARGE.
' CreateSaleLineRequest's own header is explicit: the server captures the
' EFFECTIVE price fresh from the product row at completion time, never a
' client-supplied figure - this client sends no price at all
' (MerchandisingApiClient.CreateSaleAsync). These two properties exist only
' so the cashier sees a plausible running total before pressing "Complete
' sale"; SaleResponse.Lines, not this class, is what CheckoutViewModel
' displays as the sale's real, committed figures once one exists.

Namespace ViewModels

    ''' <summary>One line of a cart being built, before it is sent as a sale.</summary>
    Public NotInheritable Class SaleCartLineViewModel

        Public Sub New(productId As Integer,
                       sku As String,
                       name As String,
                       quantity As Decimal,
                       estimatedUnitPrice As Decimal,
                       availableStock As Decimal?)

            Me.ProductId = productId
            Me.Sku = sku
            Me.Name = name
            Me.Quantity = quantity
            Me.EstimatedUnitPrice = estimatedUnitPrice
            Me.EstimatedLineTotal = quantity * estimatedUnitPrice
            Me.AvailableStock = availableStock

        End Sub

        Public ReadOnly Property ProductId As Integer
        Public ReadOnly Property Sku As String
        Public ReadOnly Property Name As String
        Public ReadOnly Property Quantity As Decimal
        Public ReadOnly Property EstimatedUnitPrice As Decimal
        Public ReadOnly Property EstimatedLineTotal As Decimal

        ''' <summary>The stock figure the product search returned at the moment this line was added - not re-read live. InsufficientStock, if it happens, is surfaced by CompleteSaleAsync's own refusal, never guessed here.</summary>
        Public ReadOnly Property AvailableStock As Decimal?

    End Class

End Namespace
