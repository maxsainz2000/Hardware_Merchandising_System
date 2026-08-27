' Merchandising.Procurement.ViewModels.NewPurchaseOrderViewModel
'
' P3-07 order creation with lines: picks a supplier and one or more products
' by searching (never by memorising an Id), builds the lines locally, and
' sends POST /api/v1/purchase-orders once. Every business rule this screen
' touches - supplier/product existence and active state, quantity and cost
' scale, at least one line - is already enforced server-side (PurchaseOrders-
' Controller.ValidateCreateRequestShape/CheckReferencesAsync, P3-03) and this
' view model repeats none of it beyond "is this text a positive number",
' which is a usability check, not the guarantee (CLAUDE.md section 5).
'
' A NEW idempotency key is minted per create ATTEMPT (per button press), the
' same rule SpikeViewModel.DecrementStockAsync already established: each
' press is a new intent, and re-sending a key belongs to a retry of ONE
' intent, not to every attempt.

Imports System.Collections.ObjectModel
Imports System.Globalization
Imports Merchandising.ClientCommon.Api
Imports Merchandising.ClientCommon.Mvvm
Imports Merchandising.Contracts.Procurement
Imports Merchandising.Contracts.Products
Imports Merchandising.Contracts.Suppliers

Namespace ViewModels

    ''' <summary>Drafts and creates a purchase order.</summary>
    Public NotInheritable Class NewPurchaseOrderViewModel
        Inherits ObservableObject

        Private Const LookupPageSize As Integer = 25

        Private ReadOnly _client As MerchandisingApiClient

        Private _supplierSearchText As String = String.Empty
        Private _selectedSupplier As SupplierResponse
        Private _productSearchText As String = String.Empty
        Private _selectedProduct As ProductResponse
        Private _newLineQuantity As String = "1"
        Private _newLineCost As String = String.Empty
        Private _selectedLine As PurchaseOrderLineEntryViewModel
        Private _isBusy As Boolean
        Private _statusMessage As String = String.Empty
        Private _resultText As String = String.Empty
        Private _correlationId As String = String.Empty
        Private _lastCallFailed As Boolean
        Private _createdOrderNumber As String = String.Empty

        Public Sub New(client As MerchandisingApiClient)

            If client Is Nothing Then
                Throw New ArgumentNullException(NameOf(client))
            End If

            _client = client

            SearchSuppliersCommand = New AsyncRelayCommand(AddressOf SearchSuppliersAsync, Function() Not IsBusy)
            SearchProductsCommand = New AsyncRelayCommand(AddressOf SearchProductsAsync, Function() Not IsBusy)
            AddLineCommand = New RelayCommand(AddressOf AddLine, Function() Not IsBusy AndAlso SelectedProduct IsNot Nothing)
            RemoveSelectedLineCommand = New RelayCommand(AddressOf RemoveSelectedLine, Function() Not IsBusy AndAlso SelectedLine IsNot Nothing)
            CreateOrderCommand = New AsyncRelayCommand(AddressOf CreateOrderAsync,
                                                       Function() Not IsBusy AndAlso SelectedSupplier IsNot Nothing AndAlso Lines.Count > 0)

        End Sub

        Public ReadOnly Property SupplierResults As New ObservableCollection(Of SupplierResponse)()
        Public ReadOnly Property ProductResults As New ObservableCollection(Of ProductResponse)()
        Public ReadOnly Property Lines As New ObservableCollection(Of PurchaseOrderLineEntryViewModel)()

        Public ReadOnly Property SearchSuppliersCommand As AsyncRelayCommand
        Public ReadOnly Property SearchProductsCommand As AsyncRelayCommand
        Public ReadOnly Property AddLineCommand As RelayCommand
        Public ReadOnly Property RemoveSelectedLineCommand As RelayCommand
        Public ReadOnly Property CreateOrderCommand As AsyncRelayCommand

        Public Property SupplierSearchText As String
            Get
                Return _supplierSearchText
            End Get
            Set(value As String)
                SetProperty(_supplierSearchText, value)
            End Set
        End Property

        ''' <summary>The supplier this order will be raised against. Set by selecting a row in the search results grid.</summary>
        Public Property SelectedSupplier As SupplierResponse
            Get
                Return _selectedSupplier
            End Get
            Set(value As SupplierResponse)
                If SetProperty(_selectedSupplier, value) Then
                    RaisePropertyChanged(NameOf(SelectedSupplierDisplay))
                End If
            End Set
        End Property

        Public ReadOnly Property SelectedSupplierDisplay As String
            Get
                Return If(SelectedSupplier Is Nothing, "(none selected)", SelectedSupplier.Name)
            End Get
        End Property

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
                If SetProperty(_selectedProduct, value) Then
                    If value IsNot Nothing Then
                        NewLineCost = value.Cost.ToString(CultureInfo.InvariantCulture)
                    End If
                End If
            End Set
        End Property

        Public Property NewLineQuantity As String
            Get
                Return _newLineQuantity
            End Get
            Set(value As String)
                SetProperty(_newLineQuantity, value)
            End Set
        End Property

        Public Property NewLineCost As String
            Get
                Return _newLineCost
            End Get
            Set(value As String)
                SetProperty(_newLineCost, value)
            End Set
        End Property

        Public Property SelectedLine As PurchaseOrderLineEntryViewModel
            Get
                Return _selectedLine
            End Get
            Set(value As PurchaseOrderLineEntryViewModel)
                SetProperty(_selectedLine, value)
            End Set
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

        Public Property CreatedOrderNumber As String
            Get
                Return _createdOrderNumber
            End Get
            Private Set(value As String)
                SetProperty(_createdOrderNumber, value)
            End Set
        End Property

        Public Async Function SearchSuppliersAsync() As Task

            IsBusy = True

            Try
                Dim result As ApiResult(Of SupplierSearchResponse) =
                    Await _client.SearchSuppliersAsync(SupplierSearchText, 1, LookupPageSize, includeInactive:=False)

                If result.IsSuccess Then

                    SupplierResults.Clear()
                    For Each supplier As SupplierResponse In result.Value.Items
                        SupplierResults.Add(supplier)
                    Next

                    StatusMessage = String.Format(CultureInfo.CurrentCulture, "{0} supplier(s) found.", result.Value.TotalCount)
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

        ''' <summary>
        ''' Adds a line from the selected product. Only checks that the text
        ''' parses as a plausible positive number - the scale rules that
        ''' actually govern DECIMAL(19,3)/DECIMAL(19,4) storage are the API's
        ''' to enforce (ADR-004.1), and CreateOrderAsync surfaces its refusal
        ''' verbatim if this line turns out to be wrong.
        ''' </summary>
        Private Sub AddLine()

            If SelectedProduct Is Nothing Then
                ShowValidation("Select a product before adding a line.")
                Return
            End If

            Dim quantity As Decimal
            If Not Decimal.TryParse(NewLineQuantity, NumberStyles.Number, CultureInfo.CurrentCulture, quantity) OrElse quantity <= 0D Then
                ShowValidation("Ordered quantity must be a positive number.")
                Return
            End If

            Dim cost As Decimal
            If Not Decimal.TryParse(NewLineCost, NumberStyles.Number, CultureInfo.CurrentCulture, cost) OrElse cost < 0D Then
                ShowValidation("Purchase cost must be zero or a positive number.")
                Return
            End If

            Lines.Add(New PurchaseOrderLineEntryViewModel With {
                .ProductId = SelectedProduct.Id.ToString(CultureInfo.InvariantCulture),
                .ProductDisplay = String.Format(CultureInfo.CurrentCulture, "{0} - {1}", SelectedProduct.Sku, SelectedProduct.Name),
                .OrderedQuantity = quantity.ToString(CultureInfo.InvariantCulture),
                .PurchaseCost = cost.ToString(CultureInfo.InvariantCulture)
            })

            NewLineQuantity = "1"
            StatusMessage = String.Format(CultureInfo.CurrentCulture, "Line added. {0} line(s) so far.", Lines.Count)
            ResultText = String.Empty
            LastCallFailed = False

        End Sub

        Private Sub RemoveSelectedLine()

            If SelectedLine Is Nothing Then
                Return
            End If

            Lines.Remove(SelectedLine)
            SelectedLine = Nothing

        End Sub

        Public Async Function CreateOrderAsync() As Task

            If SelectedSupplier Is Nothing Then
                ShowValidation("Select a supplier before creating the order.")
                Return
            End If

            If Lines.Count = 0 Then
                ShowValidation("Add at least one line before creating the order.")
                Return
            End If

            Dim lineRequests As New List(Of CreatePurchaseOrderLineRequest)

            For Each line As PurchaseOrderLineEntryViewModel In Lines
                lineRequests.Add(New CreatePurchaseOrderLineRequest With {
                    .ProductId = Integer.Parse(line.ProductId, CultureInfo.InvariantCulture),
                    .OrderedQuantity = Decimal.Parse(line.OrderedQuantity, CultureInfo.InvariantCulture),
                    .PurchaseCost = Decimal.Parse(line.PurchaseCost, CultureInfo.InvariantCulture)
                })
            Next

            Dim idempotencyKey As String = Guid.NewGuid().ToString("D")

            IsBusy = True

            Try
                Dim result As ApiResult(Of PurchaseOrderResponse) =
                    Await _client.CreatePurchaseOrderAsync(SelectedSupplier.Id, lineRequests, idempotencyKey)

                If result.IsSuccess Then

                    CreatedOrderNumber = result.Value.OrderNumber
                    StatusMessage = String.Format(CultureInfo.CurrentCulture,
                                                  "Purchase order {0} created (Draft).", result.Value.OrderNumber)
                    ResultText = String.Empty
                    CorrelationId = result.CorrelationId
                    LastCallFailed = False

                    Lines.Clear()
                    SelectedSupplier = Nothing
                    SupplierResults.Clear()

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
