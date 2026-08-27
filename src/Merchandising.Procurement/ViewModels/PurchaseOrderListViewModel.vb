' Merchandising.Procurement.ViewModels.PurchaseOrderListViewModel
'
' P3-07 order tracking: browse purchase orders (GET /api/v1/purchase-orders),
' view one with its lines (GET .../{id}), and drive the three transitions
' this card names - submit, approve, cancel.
'
' THIS VIEW MODEL NEVER DECIDES WHETHER A TRANSITION IS ALLOWED. Every button
' below is enabled whenever an order is selected and no call is in flight -
' nothing more. Whether the actual transition succeeds is the status machine
' (ADR-020) and, for approval, the self-approval veto (ADR-017 section 6);
' both live entirely server-side, and a refusal (self-approval 403, illegal
' transition 409) is rendered through ApiFailurePresenter with the API's own
' error code and message, never a client-invented guess at why it failed
' (CLAUDE.md section 5). Pre-emptively hiding a button because "this looks
' like it would fail" would be inventing a second, untrustworthy copy of a
' rule the server already owns.

Imports System.Collections.ObjectModel
Imports System.Globalization
Imports System.Linq
Imports Merchandising.ClientCommon.Api
Imports Merchandising.ClientCommon.Mvvm
Imports Merchandising.Contracts.Procurement

Namespace ViewModels

    ''' <summary>Browses purchase orders and drives submit/approve/cancel.</summary>
    Public NotInheritable Class PurchaseOrderListViewModel
        Inherits ObservableObject

        Private Const PageSize As Integer = 100

        Private ReadOnly _client As MerchandisingApiClient

        Private _supplierIdFilterText As String = String.Empty
        Private _statusFilter As String = String.Empty
        Private _selectedOrderSummary As PurchaseOrderSummaryResponse
        Private _selectedOrderDetail As PurchaseOrderResponse
        Private _cancelReason As String = String.Empty
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

            RefreshCommand = New AsyncRelayCommand(AddressOf RefreshAsync, Function() Not IsBusy)
            SubmitCommand = New AsyncRelayCommand(AddressOf SubmitAsync, Function() Not IsBusy AndAlso SelectedOrderDetail IsNot Nothing)
            ApproveCommand = New AsyncRelayCommand(AddressOf ApproveAsync, Function() Not IsBusy AndAlso SelectedOrderDetail IsNot Nothing)
            CancelCommand = New AsyncRelayCommand(AddressOf CancelAsync, Function() Not IsBusy AndAlso SelectedOrderDetail IsNot Nothing)

        End Sub

        ''' <summary>The seven spec section 10.1 status names, plus a leading blank meaning "any status".</summary>
        Public Shared ReadOnly StatusFilterOptions As String() = {
            String.Empty, "Draft", "Submitted", "Approved", "PartiallyReceived", "FullyReceived", "Cancelled", "Closed"
        }

        Public ReadOnly Property Orders As New ObservableCollection(Of PurchaseOrderSummaryResponse)()

        Public ReadOnly Property RefreshCommand As AsyncRelayCommand
        Public ReadOnly Property SubmitCommand As AsyncRelayCommand
        Public ReadOnly Property ApproveCommand As AsyncRelayCommand
        Public ReadOnly Property CancelCommand As AsyncRelayCommand

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

        Public Property SelectedOrderSummary As PurchaseOrderSummaryResponse
            Get
                Return _selectedOrderSummary
            End Get
            Set(value As PurchaseOrderSummaryResponse)
                SetProperty(_selectedOrderSummary, value)
            End Set
        End Property

        Public Property SelectedOrderDetail As PurchaseOrderResponse
            Get
                Return _selectedOrderDetail
            End Get
            Private Set(value As PurchaseOrderResponse)
                SetProperty(_selectedOrderDetail, value)
            End Set
        End Property

        Public Property CancelReason As String
            Get
                Return _cancelReason
            End Get
            Set(value As String)
                SetProperty(_cancelReason, value)
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

        Public Async Function RefreshAsync() As Task

            Dim supplierId As Integer? = Nothing
            Dim parsedSupplierId As Integer

            If Integer.TryParse(SupplierIdFilterText, NumberStyles.Integer, CultureInfo.CurrentCulture, parsedSupplierId) Then
                supplierId = parsedSupplierId
            End If

            Dim status As String = If(String.IsNullOrWhiteSpace(StatusFilter), Nothing, StatusFilter)
            Dim previouslySelectedId As Integer? = If(SelectedOrderSummary IsNot Nothing, CType(SelectedOrderSummary.Id, Integer?), Nothing)

            IsBusy = True

            Try
                Dim result As ApiResult(Of PurchaseOrderSearchResponse) =
                    Await _client.SearchPurchaseOrdersAsync(supplierId, status, "createdAt:desc", 1, PageSize)

                If result.IsSuccess Then

                    Orders.Clear()
                    For Each summary As PurchaseOrderSummaryResponse In result.Value.Items
                        Orders.Add(summary)
                    Next

                    TotalCount = result.Value.TotalCount
                    StatusMessage = String.Format(CultureInfo.CurrentCulture, "{0} order(s) found.", TotalCount)
                    ResultText = String.Empty
                    CorrelationId = result.CorrelationId
                    LastCallFailed = False

                    If previouslySelectedId.HasValue Then
                        SelectedOrderSummary = Orders.FirstOrDefault(Function(o) o.Id = previouslySelectedId.Value)
                    End If

                Else
                    ShowFailure(result)
                End If

            Finally
                IsBusy = False
            End Try

            ' Awaited explicitly rather than fired from the selection setter -
            ' an async void triggered by a property setter races the caller
            ' that just set it (see this project's own ADR-017 AsyncLocal
            ' lesson for another instance of that class of bug). The DataGrid's
            ' SelectionChanged handler in MainWindow.xaml.vb awaits this
            ' directly for the same reason.
            Await LoadSelectedOrderDetailAsync()

        End Function

        ''' <summary>Loads <see cref="SelectedOrderDetail"/> for whatever <see cref="SelectedOrderSummary"/> currently is.</summary>
        Public Async Function LoadSelectedOrderDetailAsync() As Task

            Dim summary As PurchaseOrderSummaryResponse = SelectedOrderSummary

            If summary Is Nothing Then
                SelectedOrderDetail = Nothing
                Return
            End If

            IsBusy = True

            Try
                Dim result As ApiResult(Of PurchaseOrderResponse) = Await _client.GetPurchaseOrderAsync(summary.Id)

                If result.IsSuccess Then
                    SelectedOrderDetail = result.Value
                    CorrelationId = result.CorrelationId
                Else
                    SelectedOrderDetail = Nothing
                    ShowFailure(result)
                End If

            Finally
                IsBusy = False
            End Try

        End Function

        Public Async Function SubmitAsync() As Task

            If SelectedOrderDetail Is Nothing Then
                Return
            End If

            Await ApplyTransitionAsync(Function() _client.SubmitPurchaseOrderAsync(SelectedOrderDetail.Id))

        End Function

        Public Async Function ApproveAsync() As Task

            If SelectedOrderDetail Is Nothing Then
                Return
            End If

            Await ApplyTransitionAsync(Function() _client.ApprovePurchaseOrderAsync(SelectedOrderDetail.Id))

        End Function

        Public Async Function CancelAsync() As Task

            If SelectedOrderDetail Is Nothing Then
                Return
            End If

            If String.IsNullOrWhiteSpace(CancelReason) Then
                LastCallFailed = True
                StatusMessage = "Check the values before sending."
                ResultText = "A reason is required to cancel a purchase order."
                CorrelationId = String.Empty
                Return
            End If

            Dim reason As String = CancelReason

            Await ApplyTransitionAsync(Function() _client.CancelPurchaseOrderAsync(SelectedOrderDetail.Id, reason))

        End Function

        ''' <summary>
        ''' Runs one submit/approve/cancel call. On success, the detail pane is
        ''' replaced with exactly what the server committed - never a locally
        ''' guessed status - and the list is quietly re-synced behind it. On a
        ''' refusal, nothing else happens: in particular the list is NOT
        ''' re-fetched, because RefreshAsync's own success message would
        ''' otherwise overwrite the refusal's error code and message the
        ''' instant they were shown - the wording this card requires the
        ''' operator to actually see.
        ''' </summary>
        Private Async Function ApplyTransitionAsync(apiCall As Func(Of Task(Of ApiResult(Of PurchaseOrderResponse)))) As Task

            Dim succeeded As Boolean = False

            IsBusy = True

            Try
                Dim result As ApiResult(Of PurchaseOrderResponse) = Await apiCall()

                If result.IsSuccess Then

                    SelectedOrderDetail = result.Value
                    StatusMessage = String.Format(CultureInfo.CurrentCulture,
                                                  "Purchase order {0} is now {1}.", result.Value.OrderNumber, result.Value.Status)
                    ResultText = String.Empty
                    CorrelationId = result.CorrelationId
                    LastCallFailed = False
                    CancelReason = String.Empty
                    succeeded = True

                Else
                    ShowFailure(result)
                End If

            Finally
                IsBusy = False
            End Try

            If succeeded Then
                Await ReloadOrdersListQuietlyAsync()
            End If

        End Function

        ''' <summary>
        ''' Re-fetches the Orders list under the current filters without
        ''' touching StatusMessage/ResultText/CorrelationId/LastCallFailed -
        ''' the caller (ApplyTransitionAsync) already set those to describe
        ''' what it just did, and a background list refresh must not clobber
        ''' that. A failure here is swallowed for the same reason: it is not
        ''' what the operator just asked about.
        ''' </summary>
        Private Async Function ReloadOrdersListQuietlyAsync() As Task

            Dim supplierId As Integer? = Nothing
            Dim parsedSupplierId As Integer

            If Integer.TryParse(SupplierIdFilterText, NumberStyles.Integer, CultureInfo.CurrentCulture, parsedSupplierId) Then
                supplierId = parsedSupplierId
            End If

            Dim status As String = If(String.IsNullOrWhiteSpace(StatusFilter), Nothing, StatusFilter)

            Dim result As ApiResult(Of PurchaseOrderSearchResponse) =
                Await _client.SearchPurchaseOrdersAsync(supplierId, status, "createdAt:desc", 1, PageSize)

            If result.IsSuccess Then

                Dim selectedId As Integer? = If(SelectedOrderDetail IsNot Nothing, CType(SelectedOrderDetail.Id, Integer?), Nothing)

                Orders.Clear()
                For Each summary As PurchaseOrderSummaryResponse In result.Value.Items
                    Orders.Add(summary)
                Next

                TotalCount = result.Value.TotalCount

                If selectedId.HasValue Then
                    _selectedOrderSummary = Orders.FirstOrDefault(Function(o) o.Id = selectedId.Value)
                    RaisePropertyChanged(NameOf(SelectedOrderSummary))
                End If

            End If

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
