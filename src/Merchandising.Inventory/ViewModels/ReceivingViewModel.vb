' Merchandising.Inventory.ViewModels.ReceivingViewModel
'
' P4-12 Receive tab: loads one purchase order by Id (GET /api/v1/purchase-
' orders/{id}, PurchaseOrders.Track), drafts receipt lines against its own
' lines, and confirms receiving in one call (POST /api/v1/receipts,
' Receiving.Confirm, P4-07). Every business rule this screen touches - order
' status, line identity, the received-minus-prior-receipts bound, duplicate
' reference numbers - is already enforced server-side and surfaced verbatim
' through ApiFailurePresenter; this view model repeats none of it beyond "is
' this text a positive number" (CLAUDE.md section 5).
'
' A KNOWN, FROZEN GAP THIS SCREEN CANNOT WORK AROUND: PurchaseOrders.Track
' (needed for LoadOrderAsync below) is ProcurementAndAbove; Receiving.Confirm
' is InventoryAndAbove. The two groups only overlap at Admin/SuperAdmin - a
' pure InventoryClerk sign-in can confirm a receipt but cannot look one up by
' Id first, and will see LoadOrderAsync refused with a 403 rendered exactly
' as the API worded it. Phase 2's role/policy matrix is frozen (tasks.md's
' Phase 4 closure note); this is recorded, not silently patched around, in
' evidence/phase-4/p4-12-inventory-client.txt.
'
' A fresh idempotency key is minted per confirm ATTEMPT (per button press),
' the same rule NewPurchaseOrderViewModel.CreateOrderAsync already
' establishes for Procurement.

Imports System.Collections.ObjectModel
Imports System.Globalization
Imports Merchandising.ClientCommon.Api
Imports Merchandising.ClientCommon.Mvvm
Imports Merchandising.Contracts.Procurement
Imports Merchandising.Contracts.Receiving

Namespace ViewModels

    ''' <summary>Loads a purchase order and confirms receipt of goods against it.</summary>
    Public NotInheritable Class ReceivingViewModel
        Inherits ObservableObject

        Private ReadOnly _client As MerchandisingApiClient

        Private _orderIdText As String = String.Empty
        Private _loadedOrder As PurchaseOrderResponse
        Private _selectedOrderLine As PurchaseOrderLineResponse
        Private _receiveQuantityText As String = String.Empty
        Private _receiveCostText As String = String.Empty
        Private _selectedDraftLine As ReceivingLineEntryViewModel
        Private _referenceNumberText As String = String.Empty
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

            LoadOrderCommand = New AsyncRelayCommand(AddressOf LoadOrderAsync, Function() Not IsBusy)
            AddLineCommand = New RelayCommand(AddressOf AddLine, Function() Not IsBusy AndAlso SelectedOrderLine IsNot Nothing)
            RemoveSelectedLineCommand = New RelayCommand(AddressOf RemoveSelectedLine, Function() Not IsBusy AndAlso SelectedDraftLine IsNot Nothing)
            ReceiveCommand = New AsyncRelayCommand(AddressOf ReceiveAsync,
                                                   Function() Not IsBusy AndAlso LoadedOrder IsNot Nothing AndAlso DraftLines.Count > 0)

        End Sub

        Public ReadOnly Property OrderLines As New ObservableCollection(Of PurchaseOrderLineResponse)()
        Public ReadOnly Property DraftLines As New ObservableCollection(Of ReceivingLineEntryViewModel)()

        Public ReadOnly Property LoadOrderCommand As AsyncRelayCommand
        Public ReadOnly Property AddLineCommand As RelayCommand
        Public ReadOnly Property RemoveSelectedLineCommand As RelayCommand
        Public ReadOnly Property ReceiveCommand As AsyncRelayCommand

        Public Property OrderIdText As String
            Get
                Return _orderIdText
            End Get
            Set(value As String)
                SetProperty(_orderIdText, value)
            End Set
        End Property

        Public Property LoadedOrder As PurchaseOrderResponse
            Get
                Return _loadedOrder
            End Get
            Private Set(value As PurchaseOrderResponse)
                If SetProperty(_loadedOrder, value) Then
                    RaisePropertyChanged(NameOf(LoadedOrderDisplay))
                End If
            End Set
        End Property

        Public ReadOnly Property LoadedOrderDisplay As String
            Get
                Return If(LoadedOrder Is Nothing, "(no order loaded)",
                         String.Format(CultureInfo.CurrentCulture, "{0} - {1} ({2})",
                                      LoadedOrder.OrderNumber, LoadedOrder.SupplierName, LoadedOrder.Status))
            End Get
        End Property

        Public Property SelectedOrderLine As PurchaseOrderLineResponse
            Get
                Return _selectedOrderLine
            End Get
            Set(value As PurchaseOrderLineResponse)
                If SetProperty(_selectedOrderLine, value) AndAlso value IsNot Nothing Then
                    ReceiveQuantityText = (value.OrderedQuantity - value.ReceivedQuantity).ToString(CultureInfo.InvariantCulture)
                    ReceiveCostText = value.PurchaseCost.ToString(CultureInfo.InvariantCulture)
                End If
            End Set
        End Property

        Public Property ReceiveQuantityText As String
            Get
                Return _receiveQuantityText
            End Get
            Set(value As String)
                SetProperty(_receiveQuantityText, value)
            End Set
        End Property

        Public Property ReceiveCostText As String
            Get
                Return _receiveCostText
            End Get
            Set(value As String)
                SetProperty(_receiveCostText, value)
            End Set
        End Property

        Public Property SelectedDraftLine As ReceivingLineEntryViewModel
            Get
                Return _selectedDraftLine
            End Get
            Set(value As ReceivingLineEntryViewModel)
                SetProperty(_selectedDraftLine, value)
            End Set
        End Property

        Public Property ReferenceNumberText As String
            Get
                Return _referenceNumberText
            End Get
            Set(value As String)
                SetProperty(_referenceNumberText, value)
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

        Public Async Function LoadOrderAsync() As Task

            Dim orderId As Integer
            If Not Integer.TryParse(OrderIdText, NumberStyles.Integer, CultureInfo.CurrentCulture, orderId) OrElse orderId <= 0 Then
                ShowValidation("Purchase order Id must be a positive whole number.")
                Return
            End If

            IsBusy = True

            Try
                Dim result As ApiResult(Of PurchaseOrderResponse) = Await _client.GetPurchaseOrderAsync(orderId)

                If result.IsSuccess Then

                    LoadedOrder = result.Value
                    DraftLines.Clear()

                    OrderLines.Clear()
                    For Each line As PurchaseOrderLineResponse In result.Value.Lines
                        OrderLines.Add(line)
                    Next

                    StatusMessage = String.Format(CultureInfo.CurrentCulture,
                                                  "Order {0} loaded ({1} line(s)).", result.Value.OrderNumber, OrderLines.Count)
                    ResultText = String.Empty
                    CorrelationId = result.CorrelationId
                    LastCallFailed = False

                Else
                    LoadedOrder = Nothing
                    OrderLines.Clear()
                    ShowFailure(result)
                End If

            Finally
                IsBusy = False
            End Try

        End Function

        ''' <summary>
        ''' Adds a drafted receipt line from the selected order line. Only checks
        ''' that the text parses as a plausible positive number - the
        ''' received-minus-prior-receipts bound is ReceivingService's to enforce
        ''' (P4-07), and ReceiveAsync surfaces its refusal verbatim if this line
        ''' turns out to exceed it.
        ''' </summary>
        Private Sub AddLine()

            If SelectedOrderLine Is Nothing Then
                ShowValidation("Select an order line before adding it to the receipt.")
                Return
            End If

            Dim quantity As Decimal
            If Not Decimal.TryParse(ReceiveQuantityText, NumberStyles.Number, CultureInfo.CurrentCulture, quantity) OrElse quantity <= 0D Then
                ShowValidation("Quantity received must be a positive number.")
                Return
            End If

            Dim cost As Decimal
            If Not Decimal.TryParse(ReceiveCostText, NumberStyles.Number, CultureInfo.CurrentCulture, cost) OrElse cost < 0D Then
                ShowValidation("Cost must be zero or a positive number.")
                Return
            End If

            DraftLines.Add(New ReceivingLineEntryViewModel With {
                .PurchaseOrderLineId = SelectedOrderLine.Id,
                .ProductDisplay = String.Format(CultureInfo.CurrentCulture, "{0} - {1}", SelectedOrderLine.ProductSku, SelectedOrderLine.ProductName),
                .QuantityReceived = quantity.ToString(CultureInfo.InvariantCulture),
                .Cost = cost.ToString(CultureInfo.InvariantCulture)
            })

            StatusMessage = String.Format(CultureInfo.CurrentCulture, "Line added. {0} line(s) drafted.", DraftLines.Count)
            ResultText = String.Empty
            LastCallFailed = False

        End Sub

        Private Sub RemoveSelectedLine()

            If SelectedDraftLine Is Nothing Then
                Return
            End If

            DraftLines.Remove(SelectedDraftLine)
            SelectedDraftLine = Nothing

        End Sub

        Public Async Function ReceiveAsync() As Task

            If LoadedOrder Is Nothing Then
                Return
            End If

            If DraftLines.Count = 0 Then
                ShowValidation("Add at least one line before confirming receipt.")
                Return
            End If

            If String.IsNullOrWhiteSpace(ReferenceNumberText) Then
                ShowValidation("A reference number (the goods-received-note number) is required.")
                Return
            End If

            Dim lineRequests As New List(Of ReceiveGoodsLineRequest)

            For Each line As ReceivingLineEntryViewModel In DraftLines
                lineRequests.Add(New ReceiveGoodsLineRequest With {
                    .PurchaseOrderLineId = line.PurchaseOrderLineId,
                    .QuantityReceived = Decimal.Parse(line.QuantityReceived, CultureInfo.InvariantCulture),
                    .Cost = Decimal.Parse(line.Cost, CultureInfo.InvariantCulture)
                })
            Next

            Dim idempotencyKey As String = Guid.NewGuid().ToString("D")

            IsBusy = True

            Try
                Dim result As ApiResult(Of ReceiptResponse) =
                    Await _client.ReceiveGoodsAsync(LoadedOrder.Id, ReferenceNumberText, lineRequests, idempotencyKey)

                If result.IsSuccess Then

                    StatusMessage = String.Format(CultureInfo.CurrentCulture,
                                                  "Receipt recorded. Order {0} is now {1}.",
                                                  result.Value.OrderNumber, result.Value.PurchaseOrderStatus)
                    ResultText = String.Empty
                    CorrelationId = result.CorrelationId
                    LastCallFailed = False

                    DraftLines.Clear()
                    ReferenceNumberText = String.Empty

                Else
                    ShowFailure(result)
                End If

            Finally
                IsBusy = False
            End Try

            If Not LastCallFailed Then
                ' Re-fetch so ReceivedQuantity / OrderLines reflect what was just
                ' committed - never a locally guessed figure. A failure here is
                ' swallowed: the success message above is what the operator asked
                ' about, and PurchaseOrderListViewModel.ApplyTransitionAsync gives
                ' the same precedent for not letting a background refresh clobber it.
                Dim reload As ApiResult(Of PurchaseOrderResponse) = Await _client.GetPurchaseOrderAsync(LoadedOrder.Id)
                If reload.IsSuccess Then
                    LoadedOrder = reload.Value
                    OrderLines.Clear()
                    For Each line As PurchaseOrderLineResponse In reload.Value.Lines
                        OrderLines.Add(line)
                    Next
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

        Private Sub ShowValidation(message As String)

            LastCallFailed = True
            StatusMessage = "Check the values before sending."
            ResultText = message
            CorrelationId = String.Empty

        End Sub

    End Class

End Namespace
