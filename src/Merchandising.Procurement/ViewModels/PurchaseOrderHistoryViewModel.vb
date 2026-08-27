' Merchandising.Procurement.ViewModels.PurchaseOrderHistoryViewModel
'
' P3-07 procurement history: a read-only report over GET /api/v1/purchase-
' orders/history (P3-06). Dates are plain store-local yyyy-MM-dd text sent
' straight through - the server, not this client, resolves them against
' Asia/Manila (StoreTimeZone), and every aggregate column (ordered/received/
' outstanding) is computed there too. This screen only displays what came
' back; it never sums PurchaseOrderResponse.Lines itself.

Imports System.Collections.ObjectModel
Imports System.Globalization
Imports Merchandising.ClientCommon.Api
Imports Merchandising.ClientCommon.Mvvm
Imports Merchandising.Contracts.Procurement

Namespace ViewModels

    ''' <summary>Read-only purchase-order history report.</summary>
    Public NotInheritable Class PurchaseOrderHistoryViewModel
        Inherits ObservableObject

        Private Const PageSize As Integer = 100

        Private ReadOnly _client As MerchandisingApiClient

        Private _supplierIdFilterText As String = String.Empty
        Private _statusFilter As String = String.Empty
        Private _fromDate As String = String.Empty
        Private _toDate As String = String.Empty
        Private _isBusy As Boolean
        Private _statusMessage As String = String.Empty
        Private _resultText As String = String.Empty
        Private _correlationId As String = String.Empty
        Private _lastCallFailed As Boolean
        Private _totalCount As Integer
        Private _appliedTimeZone As String = String.Empty

        Public Sub New(client As MerchandisingApiClient)

            If client Is Nothing Then
                Throw New ArgumentNullException(NameOf(client))
            End If

            _client = client
            SearchCommand = New AsyncRelayCommand(AddressOf SearchAsync, Function() Not IsBusy)

        End Sub

        Public Shared ReadOnly StatusFilterOptions As String() = PurchaseOrderListViewModel.StatusFilterOptions

        Public ReadOnly Property Items As New ObservableCollection(Of PurchaseOrderHistoryItemResponse)()

        Public ReadOnly Property SearchCommand As AsyncRelayCommand

        Public Property SupplierIdFilterText As String
            Get
                Return _supplierIdFilterText
            End Get
            Set(value As String)
                SetProperty(_supplierIdFilterText, value)
            End Set
        End Property

        Public Property StatusFilter As String
            Get
                Return _statusFilter
            End Get
            Set(value As String)
                SetProperty(_statusFilter, value)
            End Set
        End Property

        ''' <summary>Inclusive lower bound, yyyy-MM-dd, store-local. Blank means unbounded.</summary>
        Public Property FromDate As String
            Get
                Return _fromDate
            End Get
            Set(value As String)
                SetProperty(_fromDate, value)
            End Set
        End Property

        ''' <summary>Inclusive upper bound, yyyy-MM-dd, store-local. Blank means unbounded.</summary>
        Public Property ToDate As String
            Get
                Return _toDate
            End Get
            Set(value As String)
                SetProperty(_toDate, value)
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

        ''' <summary>The IANA zone the server actually applied (StoreTimeZone.IanaId, always "Asia/Manila") - echoed, never assumed.</summary>
        Public Property AppliedTimeZone As String
            Get
                Return _appliedTimeZone
            End Get
            Private Set(value As String)
                SetProperty(_appliedTimeZone, value)
            End Set
        End Property

        Public Async Function SearchAsync() As Task

            Dim supplierId As Integer? = Nothing
            Dim parsedSupplierId As Integer

            If Integer.TryParse(SupplierIdFilterText, NumberStyles.Integer, CultureInfo.CurrentCulture, parsedSupplierId) Then
                supplierId = parsedSupplierId
            End If

            Dim status As String = If(String.IsNullOrWhiteSpace(StatusFilter), Nothing, StatusFilter)
            Dim fromDateText As String = If(String.IsNullOrWhiteSpace(FromDate), Nothing, FromDate.Trim())
            Dim toDateText As String = If(String.IsNullOrWhiteSpace(ToDate), Nothing, ToDate.Trim())

            IsBusy = True

            Try
                Dim result As ApiResult(Of PurchaseOrderHistoryResponse) =
                    Await _client.GetPurchaseOrderHistoryAsync(supplierId, status, fromDateText, toDateText, "createdAt:desc", 1, PageSize)

                If result.IsSuccess Then

                    Items.Clear()
                    For Each item As PurchaseOrderHistoryItemResponse In result.Value.Items
                        Items.Add(item)
                    Next

                    TotalCount = result.Value.TotalCount
                    AppliedTimeZone = result.Value.TimeZone
                    StatusMessage = String.Format(CultureInfo.CurrentCulture, "{0} order(s) in range.", TotalCount)
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

    End Class

End Namespace
