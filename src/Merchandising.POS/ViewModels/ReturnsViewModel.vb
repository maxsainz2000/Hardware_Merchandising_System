' Merchandising.POS.ViewModels.ReturnsViewModel
'
' P5-13 Returns tab: spec section 10.3's completed-sale return - "must
' identify the original sale line, cannot exceed the quantity sold minus
' prior returns, and records whether the returned item is eligible to
' re-enter stock." Every one of those bounds is SalesReturnService's to
' enforce (P5-11); this view model repeats none of it beyond "is this text a
' positive, parseable quantity" (CLAUDE.md section 5).
'
' completedSales IS THE SAME ObservableCollection(Of SaleResponse) INSTANCE
' CheckoutViewModel APPENDS TO - injected by MainViewModel, not owned here.
' A return can only be recorded against a sale THIS CLIENT ITSELF completed
' this run, because no GET /api/v1/sales or GET /api/v1/sales/{id} route
' exists anywhere in the API to look one up independently - the same
' "session-scoped list, no server query" shape
' AdjustmentsViewModel.Adjustments already uses for a resource with no list
' endpoint (that class's own header).
'
' ONE LINE PER RETURN SUBMISSION. Spec section 10.3 describes "a return"
' identifying "the original sale line" (singular); CreateSalesReturnRequest's
' shape supports a list, but nothing about this card asks POS to build a
' multi-line return cart, so this screen keeps the simpler shape rather than
' building UI ahead of a requirement (CLAUDE.md: no abstraction ahead of a
' second, real use). A second line is a second Record-return press.

Imports System.Collections.ObjectModel
Imports System.Globalization
Imports Merchandising.ClientCommon.Api
Imports Merchandising.ClientCommon.Mvvm
Imports Merchandising.Contracts.Sales

Namespace ViewModels

    ''' <summary>Records a return against a sale this client itself completed this session.</summary>
    Public NotInheritable Class ReturnsViewModel
        Inherits ObservableObject

        ''' <summary>The three refund methods spec section 10.3 supports, the same set CreateSaleAsync's own payment uses.</summary>
        Public Shared ReadOnly RefundMethods As String() = {"Cash", "Card", "EWallet"}

        Private ReadOnly _client As MerchandisingApiClient

        Private _selectedSale As SaleResponse
        Private _selectedSaleLine As SaleLineResponse
        Private _quantityReturnedText As String = String.Empty
        Private _restocksItem As Boolean = True
        Private _reasonText As String = String.Empty
        Private _selectedRefundMethod As String = "Cash"
        Private _lastReturn As SalesReturnResponse
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
            Me.CompletedSales = completedSales

            RecordReturnCommand = New AsyncRelayCommand(AddressOf RecordReturnAsync,
                                                         Function() Not IsBusy AndAlso SelectedSale IsNot Nothing AndAlso SelectedSaleLine IsNot Nothing)

        End Sub

        ''' <summary>The same instance CheckoutViewModel appends completed sales to - this class's own header.</summary>
        Public ReadOnly Property CompletedSales As ObservableCollection(Of SaleResponse)

        Public ReadOnly Property SaleLines As New ObservableCollection(Of SaleLineResponse)()

        Public ReadOnly Property RecordReturnCommand As AsyncRelayCommand

        Public Property SelectedSale As SaleResponse
            Get
                Return _selectedSale
            End Get
            Set(value As SaleResponse)
                If SetProperty(_selectedSale, value) Then

                    SelectedSaleLine = Nothing
                    SaleLines.Clear()

                    If value IsNot Nothing Then
                        For Each line As SaleLineResponse In value.Lines
                            SaleLines.Add(line)
                        Next
                    End If

                End If
            End Set
        End Property

        Public Property SelectedSaleLine As SaleLineResponse
            Get
                Return _selectedSaleLine
            End Get
            Set(value As SaleLineResponse)
                If SetProperty(_selectedSaleLine, value) AndAlso value IsNot Nothing Then
                    QuantityReturnedText = value.Quantity.ToString(CultureInfo.InvariantCulture)
                End If
            End Set
        End Property

        Public Property QuantityReturnedText As String
            Get
                Return _quantityReturnedText
            End Get
            Set(value As String)
                SetProperty(_quantityReturnedText, value)
            End Set
        End Property

        ''' <summary>Spec section 10.3: whether the returned item is eligible to re-enter stock.</summary>
        Public Property RestocksItem As Boolean
            Get
                Return _restocksItem
            End Get
            Set(value As Boolean)
                SetProperty(_restocksItem, value)
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

        Public Property SelectedRefundMethod As String
            Get
                Return _selectedRefundMethod
            End Get
            Set(value As String)
                SetProperty(_selectedRefundMethod, value)
            End Set
        End Property

        Public Property LastReturn As SalesReturnResponse
            Get
                Return _lastReturn
            End Get
            Private Set(value As SalesReturnResponse)
                If SetProperty(_lastReturn, value) Then
                    RaisePropertyChanged(NameOf(HasLastReturn))
                    RaisePropertyChanged(NameOf(LastReturnSummary))
                End If
            End Set
        End Property

        Public ReadOnly Property HasLastReturn As Boolean
            Get
                Return LastReturn IsNot Nothing
            End Get
        End Property

        ''' <summary>Status is whatever the server assigned - Completed or PendingApproval - never guessed here (P5-11's threshold decides it, not this screen).</summary>
        Public ReadOnly Property LastReturnSummary As String
            Get
                If LastReturn Is Nothing Then
                    Return "(no return recorded yet this session)"
                End If

                Dim refundText As String = If(LastReturn.RefundAmount.HasValue,
                                              String.Format(CultureInfo.CurrentCulture, ", refund {0:0.0000} ({1})", LastReturn.RefundAmount.Value, LastReturn.RefundMethod),
                                              ", refund pending approval")

                Return String.Format(CultureInfo.CurrentCulture,
                                     "Return #{0} against sale #{1}: {2}{3}.",
                                     LastReturn.Id, LastReturn.SaleId, LastReturn.Status, refundText)
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

        Public Async Function RecordReturnAsync() As Task

            If SelectedSale Is Nothing OrElse SelectedSaleLine Is Nothing Then
                ShowValidation("Select a sale and a line before recording a return.")
                Return
            End If

            Dim quantity As Decimal
            If Not Decimal.TryParse(QuantityReturnedText, NumberStyles.Number, CultureInfo.CurrentCulture, quantity) OrElse quantity <= 0D Then
                ShowValidation("Quantity returned must be a positive number.")
                Return
            End If

            If String.IsNullOrWhiteSpace(ReasonText) Then
                ShowValidation("A reason is required.")
                Return
            End If

            Dim lineRequests As New List(Of CreateSalesReturnLineRequest) From {
                New CreateSalesReturnLineRequest With {
                    .SaleLineId = SelectedSaleLine.Id,
                    .QuantityReturned = quantity,
                    .RestocksItem = RestocksItem
                }
            }

            Dim idempotencyKey As String = Guid.NewGuid().ToString("D")

            IsBusy = True

            Try
                Dim result As ApiResult(Of SalesReturnResponse) =
                    Await _client.RecordSalesReturnAsync(SelectedSale.Id, lineRequests, ReasonText, SelectedRefundMethod, idempotencyKey)

                If result.IsSuccess Then

                    LastReturn = result.Value
                    StatusMessage = String.Format(CultureInfo.CurrentCulture,
                                                  "Return #{0} is {1}.", result.Value.Id, result.Value.Status)
                    ResultText = String.Empty
                    CorrelationId = result.CorrelationId
                    LastCallFailed = False

                    ReasonText = String.Empty

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
