' Merchandising.Inventory.ViewModels.StockCountsViewModel
'
' P4-12 Counts tab: open a stock-count session, record counted quantities one
' product at a time, close it, and load any session back by Id - the three
' routes plus the one read StockCountsController exposes (StockCounts.Perform,
' P4-09). The system quantity and variance are never computed here; every
' grid row below is exactly what RecordStockCountLine/GetStockCount returned,
' re-fetched after every write rather than merged locally, so the screen can
' never show a figure the server did not itself commit.

Imports System.Collections.ObjectModel
Imports System.Globalization
Imports Merchandising.ClientCommon.Api
Imports Merchandising.ClientCommon.Mvvm
Imports Merchandising.Contracts.Inventory
Imports Merchandising.Contracts.Products

Namespace ViewModels

    ''' <summary>Opens, records into, closes, and loads a stock-count session.</summary>
    Public NotInheritable Class StockCountsViewModel
        Inherits ObservableObject

        Private Const LookupPageSize As Integer = 25

        Private ReadOnly _client As MerchandisingApiClient

        Private _loadCountIdText As String = String.Empty
        Private _currentCount As StockCountResponse
        Private _productSearchText As String = String.Empty
        Private _selectedProduct As ProductResponse
        Private _countedQuantityText As String = String.Empty
        Private _isBusy As Boolean
        Private _statusMessage As String = String.Empty
        Private _resultText As String = String.Empty
        Private _correlationId As String = String.Empty
        Private _lastCallFailed As Boolean

        Public Sub New(client As MerchandisingApiClient)

            If client Is Nothing Then
                Throw New ArgumentNullException(NameOf(client))
            End If

            _client = client

            OpenCommand = New AsyncRelayCommand(AddressOf OpenAsync, Function() Not IsBusy)
            LoadCommand = New AsyncRelayCommand(AddressOf LoadAsync, Function() Not IsBusy)
            SearchProductsCommand = New AsyncRelayCommand(AddressOf SearchProductsAsync, Function() Not IsBusy)
            RecordLineCommand = New AsyncRelayCommand(AddressOf RecordLineAsync,
                                                      Function() Not IsBusy AndAlso CurrentCount IsNot Nothing AndAlso SelectedProduct IsNot Nothing)
            CloseCommand = New AsyncRelayCommand(AddressOf CloseAsync, Function() Not IsBusy AndAlso CurrentCount IsNot Nothing)

        End Sub

        Public ReadOnly Property ProductResults As New ObservableCollection(Of ProductResponse)()
        Public ReadOnly Property CountLines As New ObservableCollection(Of StockCountLineResponse)()

        Public ReadOnly Property OpenCommand As AsyncRelayCommand
        Public ReadOnly Property LoadCommand As AsyncRelayCommand
        Public ReadOnly Property SearchProductsCommand As AsyncRelayCommand
        Public ReadOnly Property RecordLineCommand As AsyncRelayCommand
        Public ReadOnly Property CloseCommand As AsyncRelayCommand

        Public Property LoadCountIdText As String
            Get
                Return _loadCountIdText
            End Get
            Set(value As String)
                SetProperty(_loadCountIdText, value)
            End Set
        End Property

        Public Property CurrentCount As StockCountResponse
            Get
                Return _currentCount
            End Get
            Private Set(value As StockCountResponse)
                If SetProperty(_currentCount, value) Then
                    RaisePropertyChanged(NameOf(CurrentCountDisplay))
                End If
            End Set
        End Property

        Public ReadOnly Property CurrentCountDisplay As String
            Get
                Return If(CurrentCount Is Nothing, "(no count session loaded)",
                         String.Format(CultureInfo.CurrentCulture, "Count #{0} - {1}", CurrentCount.Id, CurrentCount.Status))
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
                SetProperty(_selectedProduct, value)
            End Set
        End Property

        Public Property CountedQuantityText As String
            Get
                Return _countedQuantityText
            End Get
            Set(value As String)
                SetProperty(_countedQuantityText, value)
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

        Public Async Function OpenAsync() As Task

            Dim idempotencyKey As String = Guid.NewGuid().ToString("D")

            IsBusy = True

            Try
                Dim result As ApiResult(Of StockCountResponse) = Await _client.OpenStockCountAsync(idempotencyKey)

                If result.IsSuccess Then
                    ApplyLoadedCount(result.Value)
                    StatusMessage = String.Format(CultureInfo.CurrentCulture, "Count #{0} opened.", result.Value.Id)
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

        Public Async Function LoadAsync() As Task

            Dim countId As Integer
            If Not Integer.TryParse(LoadCountIdText, NumberStyles.Integer, CultureInfo.CurrentCulture, countId) OrElse countId <= 0 Then
                ShowValidation("Count Id must be a positive whole number.")
                Return
            End If

            Await ReloadAsync(countId, "loaded")

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

        Public Async Function RecordLineAsync() As Task

            If CurrentCount Is Nothing Then
                ShowValidation("Open or load a count session first.")
                Return
            End If

            If SelectedProduct Is Nothing Then
                ShowValidation("Select a product before recording a line.")
                Return
            End If

            Dim quantity As Decimal
            If Not Decimal.TryParse(CountedQuantityText, NumberStyles.Number, CultureInfo.CurrentCulture, quantity) OrElse quantity < 0D Then
                ShowValidation("Counted quantity must be zero or a positive number.")
                Return
            End If

            Dim countId As Integer = CurrentCount.Id
            Dim idempotencyKey As String = Guid.NewGuid().ToString("D")

            IsBusy = True

            Try
                Dim result As ApiResult(Of StockCountLineResponse) =
                    Await _client.RecordStockCountLineAsync(countId, SelectedProduct.Id, quantity, idempotencyKey)

                If result.IsSuccess Then
                    StatusMessage = String.Format(CultureInfo.CurrentCulture,
                                                  "Line recorded: {0} counted {1}, variance {2}.",
                                                  SelectedProduct.Sku, result.Value.CountedQuantity, result.Value.Variance)
                    ResultText = String.Empty
                    CorrelationId = result.CorrelationId
                    LastCallFailed = False
                    CountedQuantityText = String.Empty
                Else
                    ShowFailure(result)
                End If

            Finally
                IsBusy = False
            End Try

            If Not LastCallFailed Then
                Await ReloadQuietlyAsync(countId)
            End If

        End Function

        Public Async Function CloseAsync() As Task

            If CurrentCount Is Nothing Then
                Return
            End If

            Dim countId As Integer = CurrentCount.Id
            Dim idempotencyKey As String = Guid.NewGuid().ToString("D")

            IsBusy = True

            Try
                Dim result As ApiResult(Of StockCountResponse) = Await _client.CloseStockCountAsync(countId, idempotencyKey)

                If result.IsSuccess Then
                    ApplyLoadedCount(result.Value)
                    StatusMessage = String.Format(CultureInfo.CurrentCulture, "Count #{0} closed.", result.Value.Id)
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

        ''' <summary>Loads a session and reports the outcome as the operator's own action (Load/reload).</summary>
        Private Async Function ReloadAsync(countId As Integer, verb As String) As Task

            IsBusy = True

            Try
                Dim result As ApiResult(Of StockCountResponse) = Await _client.GetStockCountAsync(countId)

                If result.IsSuccess Then
                    ApplyLoadedCount(result.Value)
                    StatusMessage = String.Format(CultureInfo.CurrentCulture, "Count #{0} {1}.", result.Value.Id, verb)
                    ResultText = String.Empty
                    CorrelationId = result.CorrelationId
                    LastCallFailed = False
                Else
                    CurrentCount = Nothing
                    CountLines.Clear()
                    ShowFailure(result)
                End If

            Finally
                IsBusy = False
            End Try

        End Function

        ''' <summary>
        ''' Re-fetches the current session WITHOUT touching StatusMessage/
        ''' ResultText/CorrelationId/LastCallFailed - RecordLineAsync already set
        ''' those to describe what it just did, the same "do not clobber the
        ''' answer the operator just asked about" rule
        ''' PurchaseOrderListViewModel.ReloadOrdersListQuietlyAsync establishes.
        ''' </summary>
        Private Async Function ReloadQuietlyAsync(countId As Integer) As Task

            Dim result As ApiResult(Of StockCountResponse) = Await _client.GetStockCountAsync(countId)

            If result.IsSuccess Then
                CurrentCount = result.Value
                CountLines.Clear()
                For Each line As StockCountLineResponse In result.Value.Lines
                    CountLines.Add(line)
                Next
            End If

        End Function

        Private Sub ApplyLoadedCount(count As StockCountResponse)

            CurrentCount = count
            CountLines.Clear()
            For Each line As StockCountLineResponse In count.Lines
                CountLines.Add(line)
            Next

        End Sub

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
