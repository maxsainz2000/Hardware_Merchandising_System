' Merchandising.POS.ViewModels.CheckoutViewModel
'
' P5-13 Checkout tab: spec section 10.3's fast-checkout workflow - look up a
' product, build a cart, take a payment, complete the sale, show receipt
' data. Four of the card's nine named operations live here.
'
' LOOK UP A PRODUCT reuses MerchandisingApiClient.SearchProductsAsync
' unchanged (P2-07/P5-06) - it already matches SKU, barcode, or partial name
' with exact-match priority (ProductsController.SearchProducts' own header)
' and already carries AvailableStock, so no new API surface was needed for
' this operation.
'
' BUILD A CART is entirely client-side (SaleCartLineViewModel) until Complete
' sale is pressed. No business rule is checked here beyond "is this a
' positive, parseable quantity" - not even the available-stock figure the
' search result carries, on purpose: CLAUDE.md section 5 makes the API
' authoritative for every rule that matters, and InsufficientStock is
' SaleService's own outcome to raise (P5-08), surfaced verbatim if it does,
' the same "never pre-empt the button" posture ReceivingViewModel/
' AdjustmentsViewModel already establish for their own business-rule bounds.
'
' TAKE A PAYMENT / COMPLETE THE SALE send exactly what CreateSaleLineRequest
' and CreateSalePaymentRequest allow and nothing else - no price, no cost, no
' session Id (those files' own headers). A fresh idempotency key is minted
' per Complete-sale ATTEMPT, the same rule NewPurchaseOrderViewModel/
' ReceivingViewModel/AdjustmentsViewModel already establish.
'
' SHOW RECEIPT DATA renders SaleResponse - the CreateSale response itself -
' because no GET /api/v1/sales/{id} route exists anywhere in this API; the
' response completion already returns is the only receipt data there is
' (SaleResponse's own header: "every value here is already known before
' commit"). The payment panel's wording resource keys
' (CardEWalletRecordedNotice / CardEWalletIntegrationExclusionNotice) are
' P5-12's, bound here rather than re-typed - PaymentWordingTests.vb (P5-12)
' scans this project's XAML for the denylist and would fail against any
' screen that invented its own label instead.
'
' completedSales IS A SHARED, INJECTED COLLECTION. MainViewModel constructs
' one ObservableCollection(Of SaleResponse) and hands the same instance to
' both this view model (which appends to it on a successful sale) and
' ReturnsViewModel (which reads it) - the same reasoning "take a return"
' needs a source of sale/line Ids from, given no GET /api/v1/sales/{id}
' route exists to look one up independently (this card's own restated-scope
' note).

Imports System.Collections.ObjectModel
Imports System.Globalization
Imports Merchandising.ClientCommon.Api
Imports Merchandising.ClientCommon.Mvvm
Imports Merchandising.Contracts.Products
Imports Merchandising.Contracts.Sales

Namespace ViewModels

    ''' <summary>Product lookup, cart, payment, sale completion and receipt display.</summary>
    Public NotInheritable Class CheckoutViewModel
        Inherits ObservableObject

        Private Const LookupPageSize As Integer = 25

        ''' <summary>The three payment methods spec section 10.3 supports, in the exact enum-name form PaymentMethod / CreateSalePaymentRequest.Method expects.</summary>
        Public Shared ReadOnly PaymentMethods As String() = {"Cash", "Card", "EWallet"}

        Private ReadOnly _client As MerchandisingApiClient
        Private ReadOnly _completedSales As ObservableCollection(Of SaleResponse)

        Private _productSearchText As String = String.Empty
        Private _selectedProduct As ProductResponse
        Private _cartQuantityText As String = "1"
        Private _selectedCartLine As SaleCartLineViewModel
        Private _selectedPaymentMethod As String = "Cash"
        Private _tenderedAmountText As String = String.Empty
        Private _lastSale As SaleResponse
        Private _isBusy As Boolean
        Private _statusMessage As String = String.Empty
        Private _resultText As String = String.Empty
        Private _correlationId As String = String.Empty
        Private _lastCallFailed As Boolean

        Public Sub New(client As MerchandisingApiClient, completedSales As ObservableCollection(Of SaleResponse))

            If client Is Nothing Then
                Throw New ArgumentNullException(NameOf(client))
            End If

            If completedSales Is Nothing Then
                Throw New ArgumentNullException(NameOf(completedSales))
            End If

            _client = client
            _completedSales = completedSales

            SearchProductsCommand = New AsyncRelayCommand(AddressOf SearchProductsAsync, Function() Not IsBusy)
            AddToCartCommand = New RelayCommand(AddressOf AddToCart, Function() Not IsBusy AndAlso SelectedProduct IsNot Nothing)
            RemoveFromCartCommand = New RelayCommand(AddressOf RemoveFromCart, Function() Not IsBusy AndAlso SelectedCartLine IsNot Nothing)
            CompleteSaleCommand = New AsyncRelayCommand(AddressOf CompleteSaleAsync, Function() Not IsBusy AndAlso Cart.Count > 0)

        End Sub

        Public ReadOnly Property ProductResults As New ObservableCollection(Of ProductResponse)()
        Public ReadOnly Property Cart As New ObservableCollection(Of SaleCartLineViewModel)()

        Public ReadOnly Property SearchProductsCommand As AsyncRelayCommand
        Public ReadOnly Property AddToCartCommand As RelayCommand
        Public ReadOnly Property RemoveFromCartCommand As RelayCommand
        Public ReadOnly Property CompleteSaleCommand As AsyncRelayCommand

        Public Property ProductSearchText As String
            Get
                Return _productSearchText
            End Get
            Set(value As String)
                SetProperty(_productSearchText, value)
            End Set
        End Property

        Public Property SelectedProduct As ProductResponse
            Get
                Return _selectedProduct
            End Get
            Set(value As ProductResponse)
                SetProperty(_selectedProduct, value)
            End Set
        End Property

        Public Property CartQuantityText As String
            Get
                Return _cartQuantityText
            End Get
            Set(value As String)
                SetProperty(_cartQuantityText, value)
            End Set
        End Property

        Public Property SelectedCartLine As SaleCartLineViewModel
            Get
                Return _selectedCartLine
            End Get
            Set(value As SaleCartLineViewModel)
                SetProperty(_selectedCartLine, value)
            End Set
        End Property

        ''' <summary>Sum of the cart's own EstimatedLineTotal figures - never what the sale actually charges (SaleCartLineViewModel's own header).</summary>
        Public ReadOnly Property CartEstimatedTotal As Decimal
            Get
                Dim total As Decimal = 0D
                For Each line As SaleCartLineViewModel In Cart
                    total += line.EstimatedLineTotal
                Next
                Return total
            End Get
        End Property

        Public Property SelectedPaymentMethod As String
            Get
                Return _selectedPaymentMethod
            End Get
            Set(value As String)
                If SetProperty(_selectedPaymentMethod, value) Then
                    RaisePropertyChanged(NameOf(IsCashPayment))
                End If
            End Set
        End Property

        ''' <summary>Drives whether the tendered-amount box is shown - Card/EWallet never send one (CreateSalePaymentRequest's own header).</summary>
        Public ReadOnly Property IsCashPayment As Boolean
            Get
                Return String.Equals(SelectedPaymentMethod, "Cash", StringComparison.Ordinal)
            End Get
        End Property

        Public Property TenderedAmountText As String
            Get
                Return _tenderedAmountText
            End Get
            Set(value As String)
                SetProperty(_tenderedAmountText, value)
            End Set
        End Property

        Public Property LastSale As SaleResponse
            Get
                Return _lastSale
            End Get
            Private Set(value As SaleResponse)
                If SetProperty(_lastSale, value) Then
                    RaisePropertyChanged(NameOf(HasLastSale))
                    RaisePropertyChanged(NameOf(ReceiptSummary))
                End If
            End Set
        End Property

        Public ReadOnly Property HasLastSale As Boolean
            Get
                Return LastSale IsNot Nothing
            End Get
        End Property

        ''' <summary>Receipt data, straight from what CompleteSaleAsync's own response returned - never re-fetched (no GET /api/v1/sales/{id} route exists; this class's own header).</summary>
        Public ReadOnly Property ReceiptSummary As String
            Get
                If LastSale Is Nothing Then
                    Return "(no sale completed yet this session)"
                End If

                Dim payment As SalePaymentResponse = LastSale.Payment
                Dim changeText As String = If(payment.ChangeAmount.HasValue,
                                              String.Format(CultureInfo.CurrentCulture, ", change {0:0.0000}", payment.ChangeAmount.Value),
                                              String.Empty)

                Return String.Format(CultureInfo.CurrentCulture,
                                     "Sale #{0}: total {1:0.0000}, payment {2} - {3}{4}.",
                                     LastSale.Id, LastSale.Total, payment.Method, payment.Status, changeText)
            End Get
        End Property

        Public Property IsBusy As Boolean
            Get
                Return _isBusy
            End Get
            Private Set(value As Boolean)
                SetProperty(_isBusy, value)
            End Set
        End Property

        Public Property StatusMessage As String
            Get
                Return _statusMessage
            End Get
            Private Set(value As String)
                SetProperty(_statusMessage, value)
            End Set
        End Property

        Public Property ResultText As String
            Get
                Return _resultText
            End Get
            Private Set(value As String)
                SetProperty(_resultText, value)
            End Set
        End Property

        Public Property CorrelationId As String
            Get
                Return _correlationId
            End Get
            Private Set(value As String)
                If SetProperty(_correlationId, value) Then
                    RaisePropertyChanged(NameOf(HasCorrelationId))
                End If
            End Set
        End Property

        Public ReadOnly Property HasCorrelationId As Boolean
            Get
                Return Not String.IsNullOrEmpty(_correlationId)
            End Get
        End Property

        Public Property LastCallFailed As Boolean
            Get
                Return _lastCallFailed
            End Get
            Private Set(value As Boolean)
                SetProperty(_lastCallFailed, value)
            End Set
        End Property

        Public Async Function SearchProductsAsync() As Task

            IsBusy = True

            Try
                Dim result As ApiResult(Of ProductSearchResponse) =
                    Await _client.SearchProductsAsync(ProductSearchText, 1, LookupPageSize)

                If result.IsSuccess Then

                    ProductResults.Clear()
                    For Each product As ProductResponse In result.Value.Items
                        ProductResults.Add(product)
                    Next

                    StatusMessage = String.Format(CultureInfo.CurrentCulture, "{0} product(s) found.", result.Value.TotalCount)
                    ResultText = String.Empty
                    CorrelationId = result.CorrelationId
                    LastCallFailed = False

                Else
                    ShowFailure(result)
                End If

            Finally
                IsBusy = False
            End Try

        End Function

        ''' <summary>Adds a whole cart line from the selected product. Only checks that the quantity text is a plausible positive number - available stock is re-checked, authoritatively, by CompleteSaleAsync (this class's own header).</summary>
        Private Sub AddToCart()

            If SelectedProduct Is Nothing Then
                ShowValidation("Select a product before adding it to the cart.")
                Return
            End If

            Dim quantity As Decimal
            If Not Decimal.TryParse(CartQuantityText, NumberStyles.Number, CultureInfo.CurrentCulture, quantity) OrElse quantity <= 0D Then
                ShowValidation("Quantity must be a positive number.")
                Return
            End If

            Cart.Add(New SaleCartLineViewModel(
                SelectedProduct.Id, SelectedProduct.Sku, SelectedProduct.Name, quantity, SelectedProduct.Price, SelectedProduct.AvailableStock))

            RaisePropertyChanged(NameOf(CartEstimatedTotal))

            StatusMessage = String.Format(CultureInfo.CurrentCulture, "Added to cart. {0} line(s).", Cart.Count)
            ResultText = String.Empty
            LastCallFailed = False
            CartQuantityText = "1"

        End Sub

        Private Sub RemoveFromCart()

            If SelectedCartLine Is Nothing Then
                Return
            End If

            Cart.Remove(SelectedCartLine)
            SelectedCartLine = Nothing
            RaisePropertyChanged(NameOf(CartEstimatedTotal))

        End Sub

        Public Async Function CompleteSaleAsync() As Task

            If Cart.Count = 0 Then
                ShowValidation("Add at least one line to the cart before completing the sale.")
                Return
            End If

            Dim tenderedAmount As Decimal? = Nothing

            If IsCashPayment Then

                Dim parsedTender As Decimal
                If Not Decimal.TryParse(TenderedAmountText, NumberStyles.Number, CultureInfo.CurrentCulture, parsedTender) OrElse parsedTender < 0D Then
                    ShowValidation("Tendered amount must be zero or a positive amount for a Cash sale.")
                    Return
                End If

                tenderedAmount = parsedTender

            End If

            Dim lineRequests As New List(Of CreateSaleLineRequest)

            For Each line As SaleCartLineViewModel In Cart
                lineRequests.Add(New CreateSaleLineRequest With {
                    .ProductId = line.ProductId,
                    .Quantity = line.Quantity
                })
            Next

            Dim idempotencyKey As String = Guid.NewGuid().ToString("D")

            IsBusy = True

            Try
                Dim result As ApiResult(Of SaleResponse) =
                    Await _client.CreateSaleAsync(lineRequests, SelectedPaymentMethod, tenderedAmount, idempotencyKey)

                If result.IsSuccess Then

                    LastSale = result.Value
                    _completedSales.Insert(0, result.Value)

                    StatusMessage = String.Format(CultureInfo.CurrentCulture, "Sale #{0} completed.", result.Value.Id)
                    ResultText = String.Empty
                    CorrelationId = result.CorrelationId
                    LastCallFailed = False

                    Cart.Clear()
                    RaisePropertyChanged(NameOf(CartEstimatedTotal))
                    TenderedAmountText = String.Empty

                Else
                    ShowFailure(result)
                End If

            Finally
                IsBusy = False
            End Try

        End Function

        Private Sub ShowFailure(Of T As Class)(result As ApiResult(Of T))

            LastCallFailed = True

            Dim display As ApiFailureDisplay = ApiFailurePresenter.Describe(result)
            StatusMessage = display.StatusMessage
            ResultText = display.Detail
            CorrelationId = display.CorrelationId

        End Sub

        Private Sub ShowValidation(message As String)

            LastCallFailed = True
            StatusMessage = "Check the values before sending."
            ResultText = message
            CorrelationId = String.Empty

        End Sub

    End Class

End Namespace
