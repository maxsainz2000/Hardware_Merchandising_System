' Merchandising.Inventory.ViewModels.StockBrowseViewModel
'
' P4-12 Stock tab: read-only balance browse (Stock.Read, P4-11) plus a
' per-product movement-ledger lookup (Stock.ReviewMovements, P4-11) - the two
' surfaces P4-11's own header says should "reconcile rather than re-query",
' so they live on one screen rather than two. Selecting a balance row seeds
' the movement lookup's Product Id - a convenience only; the operator can
' still type any Product Id directly.
'
' Neither half writes anything. Every business rule these two reads touch -
' which products are low, what a movement's delta/before/after mean - is
' already proven server-side by P4-01/P4-11; this screen repeats none of it.

Imports System.Collections.ObjectModel
Imports System.Globalization
Imports Merchandising.ClientCommon.Api
Imports Merchandising.ClientCommon.Mvvm
Imports Merchandising.Contracts.Inventory

Namespace ViewModels

    ''' <summary>Stock-balance browse plus one product's movement ledger.</summary>
    Public NotInheritable Class StockBrowseViewModel
        Inherits ObservableObject

        Private Const PageSize As Integer = 100

        Private ReadOnly _client As MerchandisingApiClient

        Private _includeInactive As Boolean
        Private _sortText As String = String.Empty
        Private _selectedBalance As StockBalanceResponse
        Private _isBusy As Boolean
        Private _statusMessage As String = String.Empty
        Private _resultText As String = String.Empty
        Private _correlationId As String = String.Empty
        Private _lastCallFailed As Boolean
        Private _totalCount As Integer

        Private _movementProductIdText As String = String.Empty
        Private _movementFromDate As String = String.Empty
        Private _movementToDate As String = String.Empty
        Private _movementSortText As String = String.Empty
        Private _movementCurrentBalance As Decimal
        Private _movementTimeZone As String = String.Empty
        Private _movementReconciliation As String = String.Empty

        Public Sub New(client As MerchandisingApiClient)

            If client Is Nothing Then
                Throw New ArgumentNullException(NameOf(client))
            End If

            _client = client

            SearchCommand = New AsyncRelayCommand(AddressOf SearchAsync, Function() Not IsBusy)
            LoadMovementsCommand = New AsyncRelayCommand(AddressOf LoadMovementsAsync, Function() Not IsBusy)

        End Sub

        Public ReadOnly Property Balances As New ObservableCollection(Of StockBalanceResponse)()
        Public ReadOnly Property Movements As New ObservableCollection(Of StockMovementItemResponse)()

        Public ReadOnly Property SearchCommand As AsyncRelayCommand
        Public ReadOnly Property LoadMovementsCommand As AsyncRelayCommand

        Public Property IncludeInactive As Boolean
            Get
                Return _includeInactive
            End Get
            Set(value As Boolean)
                SetProperty(_includeInactive, value)
            End Set
        End Property

        Public Property SortText As String
            Get
                Return _sortText
            End Get
            Set(value As String)
                SetProperty(_sortText, value)
            End Set
        End Property

        ''' <summary>Selecting a row seeds the movement lookup's Product Id - a convenience only.</summary>
        Public Property SelectedBalance As StockBalanceResponse
            Get
                Return _selectedBalance
            End Get
            Set(value As StockBalanceResponse)
                If SetProperty(_selectedBalance, value) AndAlso value IsNot Nothing Then
                    MovementProductIdText = value.ProductId.ToString(CultureInfo.InvariantCulture)
                End If
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

        Public Property MovementProductIdText As String
            Get
                Return _movementProductIdText
            End Get
            Set(value As String)
                SetProperty(_movementProductIdText, value)
            End Set
        End Property

        Public Property MovementFromDate As String
            Get
                Return _movementFromDate
            End Get
            Set(value As String)
                SetProperty(_movementFromDate, value)
            End Set
        End Property

        Public Property MovementToDate As String
            Get
                Return _movementToDate
            End Get
            Set(value As String)
                SetProperty(_movementToDate, value)
            End Set
        End Property

        Public Property MovementSortText As String
            Get
                Return _movementSortText
            End Get
            Set(value As String)
                SetProperty(_movementSortText, value)
            End Set
        End Property

        Public Property MovementCurrentBalance As Decimal
            Get
                Return _movementCurrentBalance
            End Get
            Private Set(value As Decimal)
                SetProperty(_movementCurrentBalance, value)
            End Set
        End Property

        Public Property MovementTimeZone As String
            Get
                Return _movementTimeZone
            End Get
            Private Set(value As String)
                SetProperty(_movementTimeZone, value)
            End Set
        End Property

        ''' <summary>
        ''' Sums only the movements THIS PAGE returned, against the echoed
        ''' current balance - a demonstration, not a proof of the full-ledger
        ''' invariant P4-01 asserts server-side. Worded to say exactly that.
        ''' </summary>
        Public Property MovementReconciliation As String
            Get
                Return _movementReconciliation
            End Get
            Private Set(value As String)
                SetProperty(_movementReconciliation, value)
            End Set
        End Property

        Public Async Function SearchAsync() As Task

            IsBusy = True

            Try
                Dim result As ApiResult(Of StockSearchResponse) =
                    Await _client.SearchStockAsync(IncludeInactive, SortText, 1, PageSize)

                If result.IsSuccess Then

                    Balances.Clear()
                    For Each balance As StockBalanceResponse In result.Value.Items
                        Balances.Add(balance)
                    Next

                    TotalCount = result.Value.TotalCount
                    StatusMessage = String.Format(CultureInfo.CurrentCulture, "{0} product(s) found.", TotalCount)
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

        Public Async Function LoadMovementsAsync() As Task

            Dim productId As Integer
            If Not Integer.TryParse(MovementProductIdText, NumberStyles.Integer, CultureInfo.CurrentCulture, productId) OrElse productId <= 0 Then
                ShowValidation("Product Id must be a positive whole number.")
                Return
            End If

            IsBusy = True

            Try
                Dim result As ApiResult(Of StockMovementSearchResponse) =
                    Await _client.GetStockMovementsAsync(
                        productId,
                        If(String.IsNullOrWhiteSpace(MovementFromDate), Nothing, MovementFromDate),
                        If(String.IsNullOrWhiteSpace(MovementToDate), Nothing, MovementToDate),
                        MovementSortText, 1, PageSize)

                If result.IsSuccess Then

                    Movements.Clear()
                    Dim sumOfShown As Decimal = 0D
                    For Each movement As StockMovementItemResponse In result.Value.Items
                        Movements.Add(movement)
                        sumOfShown += movement.Delta
                    Next

                    MovementCurrentBalance = result.Value.CurrentBalance
                    MovementTimeZone = result.Value.TimeZone
                    MovementReconciliation = String.Format(CultureInfo.CurrentCulture,
                                                           "Sum of movements shown: {0} (page may not cover the full history). Current balance: {1}.",
                                                           sumOfShown, result.Value.CurrentBalance)

                    StatusMessage = String.Format(CultureInfo.CurrentCulture, "{0} movement(s) found.", result.Value.TotalCount)
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
