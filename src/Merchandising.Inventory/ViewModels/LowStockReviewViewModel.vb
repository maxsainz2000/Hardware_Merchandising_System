' Merchandising.Inventory.ViewModels.LowStockReviewViewModel
'
' P4-12 Low-stock tab: read-only search over GET /api/v1/inventory/low-stock
' (LowStock.Review, P4-11). Every item the API returns is already known
' active and at-or-below its own reorder level server-side (StockRepository.
' SearchLowStockAsync's WHERE clause) - this screen adds no client-side
' filter of its own.

Imports System.Collections.ObjectModel
Imports System.Globalization
Imports Merchandising.ClientCommon.Api
Imports Merchandising.ClientCommon.Mvvm
Imports Merchandising.Contracts.Inventory

Namespace ViewModels

    ''' <summary>Read-only low-stock search for the Low-stock tab.</summary>
    Public NotInheritable Class LowStockReviewViewModel
        Inherits ObservableObject

        Private Const PageSize As Integer = 100

        Private ReadOnly _client As MerchandisingApiClient

        Private _sortText As String = String.Empty
        Private _isBusy As Boolean
        Private _statusMessage As String = String.Empty
        Private _resultText As String = String.Empty
        Private _correlationId As String = String.Empty
        Private _lastCallFailed As Boolean
        Private _totalCount As Integer

        Public Sub New(client As MerchandisingApiClient)

            If client Is Nothing Then
                Throw New ArgumentNullException(NameOf(client))
            End If

            _client = client
            SearchCommand = New AsyncRelayCommand(AddressOf SearchAsync, Function() Not IsBusy)

        End Sub

        Public ReadOnly Property Items As New ObservableCollection(Of LowStockItemResponse)()

        Public ReadOnly Property SearchCommand As AsyncRelayCommand

        Public Property SortText As String
            Get
                Return _sortText
            End Get
            Set(value As String)
                SetProperty(_sortText, value)
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

        Public Property TotalCount As Integer
            Get
                Return _totalCount
            End Get
            Private Set(value As Integer)
                SetProperty(_totalCount, value)
            End Set
        End Property

        Public Async Function SearchAsync() As Task

            IsBusy = True

            Try
                Dim result As ApiResult(Of LowStockSearchResponse) =
                    Await _client.SearchLowStockAsync(SortText, 1, PageSize)

                If result.IsSuccess Then

                    Items.Clear()
                    For Each item As LowStockItemResponse In result.Value.Items
                        Items.Add(item)
                    Next

                    TotalCount = result.Value.TotalCount
                    StatusMessage = String.Format(CultureInfo.CurrentCulture, "{0} low-stock product(s) found.", TotalCount)
                    ResultText = String.Empty
                    CorrelationId = result.CorrelationId
                    LastCallFailed = False

                Else

                    LastCallFailed = True
                    Dim display As ApiFailureDisplay = ApiFailurePresenter.Describe(result)
                    StatusMessage = display.StatusMessage
                    ResultText = display.Detail
                    CorrelationId = display.CorrelationId

                End If

            Finally
                IsBusy = False
            End Try

        End Function

    End Class

End Namespace
