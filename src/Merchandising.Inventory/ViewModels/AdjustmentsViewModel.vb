' Merchandising.Inventory.ViewModels.AdjustmentsViewModel
'
' P4-12 Adjustments tab: request a stock adjustment (Adjustments.Request),
' then approve or reject it (Adjustments.Approve, P4-10). Below the
' configured threshold a request applies immediately; at or above it, the
' response comes back Pending and needs a second person.
'
' NO LIST/GET ENDPOINT EXISTS FOR ADJUSTMENTS - AdjustmentsController exposes
' only the three POST routes (request/approve/reject), each returning the
' full AdjustmentResponse it just produced. So the grid below is built
' entirely from what THIS SESSION has requested or acted on, never a server
' query; Approve/Reject replace that row with the exact response the server
' returned, never a locally guessed status.
'
' The self-approval veto (ADR-017 section 6) and the Admin-only approval
' policy (Adjustments.Approve is AdminAndAbove - an InventoryClerk can
' request but never approve, spec section 9) both surface as an ordinary
' 403 Rejected outcome, rendered exactly as PurchaseOrderListViewModel's own
' header argues for: this view model never pre-empts the Approve/Reject
' buttons or guesses why either would fail.

Imports System.Collections.ObjectModel
Imports System.Globalization
Imports System.Linq
Imports Merchandising.ClientCommon.Api
Imports Merchandising.ClientCommon.Mvvm
Imports Merchandising.Contracts.Inventory
Imports Merchandising.Contracts.Products

Namespace ViewModels

    ''' <summary>Requests, approves, and rejects stock adjustments for this session.</summary>
    Public NotInheritable Class AdjustmentsViewModel
        Inherits ObservableObject

        Private Const LookupPageSize As Integer = 25

        Private ReadOnly _client As MerchandisingApiClient

        Private _productSearchText As String = String.Empty
        Private _selectedProduct As ProductResponse
        Private _varianceText As String = String.Empty
        Private _reasonText As String = String.Empty
        Private _selectedAdjustment As AdjustmentResponse
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

            SearchProductsCommand = New AsyncRelayCommand(AddressOf SearchProductsAsync, Function() Not IsBusy)
            RequestCommand = New AsyncRelayCommand(AddressOf RequestAsync, Function() Not IsBusy AndAlso SelectedProduct IsNot Nothing)
            ApproveCommand = New AsyncRelayCommand(AddressOf ApproveAsync, Function() Not IsBusy AndAlso SelectedAdjustment IsNot Nothing)
            RejectCommand = New AsyncRelayCommand(AddressOf RejectAsync, Function() Not IsBusy AndAlso SelectedAdjustment IsNot Nothing)

        End Sub

        Public ReadOnly Property ProductResults As New ObservableCollection(Of ProductResponse)()
        Public ReadOnly Property Adjustments As New ObservableCollection(Of AdjustmentResponse)()

        Public ReadOnly Property SearchProductsCommand As AsyncRelayCommand
        Public ReadOnly Property RequestCommand As AsyncRelayCommand
        Public ReadOnly Property ApproveCommand As AsyncRelayCommand
        Public ReadOnly Property RejectCommand As AsyncRelayCommand

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

        ''' <summary>Signed - positive found-more, negative found-less. Text so a bad value reads as validation, not a binding failure.</summary>
        Public Property VarianceText As String
            Get
                Return _varianceText
            End Get
            Set(value As String)
                SetProperty(_varianceText, value)
            End Set
        End Property

        Public Property ReasonText As String
            Get
                Return _reasonText
            End Get
            Set(value As String)
                SetProperty(_reasonText, value)
            End Set
        End Property

        Public Property SelectedAdjustment As AdjustmentResponse
            Get
                Return _selectedAdjustment
            End Get
            Set(value As AdjustmentResponse)
                SetProperty(_selectedAdjustment, value)
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

        Public Async Function RequestAsync() As Task

            If SelectedProduct Is Nothing Then
                ShowValidation("Select a product before requesting an adjustment.")
                Return
            End If

            Dim variance As Decimal
            If Not Decimal.TryParse(VarianceText, NumberStyles.Number Or NumberStyles.AllowLeadingSign, CultureInfo.CurrentCulture, variance) OrElse variance = 0D Then
                ShowValidation("Variance must be a non-zero number (positive for found-more, negative for found-less).")
                Return
            End If

            If String.IsNullOrWhiteSpace(ReasonText) Then
                ShowValidation("A reason is required.")
                Return
            End If

            Dim idempotencyKey As String = Guid.NewGuid().ToString("D")

            IsBusy = True

            Try
                Dim result As ApiResult(Of AdjustmentResponse) =
                    Await _client.RequestAdjustmentAsync(SelectedProduct.Id, variance, ReasonText, idempotencyKey)

                If result.IsSuccess Then

                    Adjustments.Insert(0, result.Value)
                    StatusMessage = String.Format(CultureInfo.CurrentCulture,
                                                  "Adjustment #{0} is {1}.", result.Value.Id, result.Value.Status)
                    ResultText = String.Empty
                    CorrelationId = result.CorrelationId
                    LastCallFailed = False
                    VarianceText = String.Empty
                    ReasonText = String.Empty

                Else
                    ShowFailure(result)
                End If

            Finally
                IsBusy = False
            End Try

        End Function

        Public Async Function ApproveAsync() As Task

            If SelectedAdjustment Is Nothing Then
                Return
            End If

            Await ApplyDecisionAsync(Function() _client.ApproveAdjustmentAsync(SelectedAdjustment.Id))

        End Function

        Public Async Function RejectAsync() As Task

            If SelectedAdjustment Is Nothing Then
                Return
            End If

            Await ApplyDecisionAsync(Function() _client.RejectAdjustmentAsync(SelectedAdjustment.Id))

        End Function

        ''' <summary>
        ''' Runs one approve/reject call. On success, the row is replaced with
        ''' exactly what the server committed - never a locally guessed status -
        ''' the same rule PurchaseOrderListViewModel.ApplyTransitionAsync follows
        ''' for order transitions.
        ''' </summary>
        Private Async Function ApplyDecisionAsync(apiCall As Func(Of Task(Of ApiResult(Of AdjustmentResponse)))) As Task

            IsBusy = True

            Try
                Dim result As ApiResult(Of AdjustmentResponse) = Await apiCall()

                If result.IsSuccess Then

                    Dim index As Integer = Adjustments.ToList().FindIndex(Function(a) a.Id = result.Value.Id)
                    If index >= 0 Then
                        Adjustments(index) = result.Value
                    Else
                        Adjustments.Insert(0, result.Value)
                    End If

                    SelectedAdjustment = result.Value
                    StatusMessage = String.Format(CultureInfo.CurrentCulture,
                                                  "Adjustment #{0} is {1}.", result.Value.Id, result.Value.Status)
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
