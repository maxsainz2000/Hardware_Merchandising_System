' Merchandising.POS.ViewModels.SessionViewModel
'
' P5-13 Session tab: opens and closes the calling cashier's own cashier
' session (P5-04 POST /api/v1/cashier-sessions, P5-05 POST /api/v1/
' cashier-sessions/{id}/close). Spec section 10.3: "A sale requires an open
' cashier session" - CheckoutViewModel does not gate its own Complete-sale
' button on this view model's state (the same "never pre-empt, let the
' server's own refusal surface" posture ReceivingViewModel/AdjustmentsViewModel
' already establish); a sale attempted with no session open comes back as
' SaleOutcome.NoOpenSessionErrorCode, rendered exactly as the API worded it.
'
' AlreadyOpen (a second Open while one is already held) and NotOpen (a second
' Close, or closing a session this cashier never opened) are both ordinary
' Rejected outcomes this view model renders verbatim, never pre-empted -
' AdjustmentsViewModel's identical reasoning for Approve/Reject.

Imports System.Globalization
Imports Merchandising.ClientCommon.Api
Imports Merchandising.ClientCommon.Mvvm
Imports Merchandising.Contracts.Sales

Namespace ViewModels

    ''' <summary>Opens and closes the calling cashier's own cashier session.</summary>
    Public NotInheritable Class SessionViewModel
        Inherits ObservableObject

        Private ReadOnly _client As MerchandisingApiClient

        Private _openingFloatText As String = "0.00"
        Private _declaredCashText As String = String.Empty
        Private _currentSession As CashierSessionResponse
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

            OpenCommand = New AsyncRelayCommand(AddressOf OpenAsync, Function() Not IsBusy AndAlso Not IsSessionOpen)
            CloseCommand = New AsyncRelayCommand(AddressOf CloseAsync, Function() Not IsBusy AndAlso IsSessionOpen)

        End Sub

        Public ReadOnly Property OpenCommand As AsyncRelayCommand
        Public ReadOnly Property CloseCommand As AsyncRelayCommand

        Public Property OpeningFloatText As String
            Get
                Return _openingFloatText
            End Get
            Set(value As String)
                SetProperty(_openingFloatText, value)
            End Set
        End Property

        Public Property DeclaredCashText As String
            Get
                Return _declaredCashText
            End Get
            Set(value As String)
                SetProperty(_declaredCashText, value)
            End Set
        End Property

        Public Property CurrentSession As CashierSessionResponse
            Get
                Return _currentSession
            End Get
            Private Set(value As CashierSessionResponse)
                If SetProperty(_currentSession, value) Then
                    RaisePropertyChanged(NameOf(IsSessionOpen))
                    RaisePropertyChanged(NameOf(SessionDisplay))
                    RaisePropertyChanged(NameOf(SessionId))
                End If
            End Set
        End Property

        ''' <summary>True once Open has succeeded and Close has not yet been confirmed. Checkout and Returns read this only for display - never to gate a command (this class's own header).</summary>
        Public ReadOnly Property IsSessionOpen As Boolean
            Get
                Return CurrentSession IsNot Nothing AndAlso
                       String.Equals(CurrentSession.Status, "Open", StringComparison.Ordinal)
            End Get
        End Property

        ''' <summary>Nothing when no session has ever been opened this run.</summary>
        Public ReadOnly Property SessionId As Integer?
            Get
                Return If(CurrentSession Is Nothing, CType(Nothing, Integer?), CurrentSession.Id)
            End Get
        End Property

        Public ReadOnly Property SessionDisplay As String
            Get
                If CurrentSession Is Nothing Then
                    Return "No session opened yet."
                End If

                If IsSessionOpen Then
                    Return String.Format(CultureInfo.CurrentCulture,
                                         "Session #{0} - Open. Opening float {1:0.00}.",
                                         CurrentSession.Id, CurrentSession.OpeningFloat)
                End If

                Return String.Format(CultureInfo.CurrentCulture,
                                     "Session #{0} - Closed. Declared {1:0.00}, calculated {2:0.00}, variance {3:0.00}.",
                                     CurrentSession.Id, CurrentSession.DeclaredCash, CurrentSession.CalculatedCash, CurrentSession.CashVariance)
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

        Public Async Function OpenAsync() As Task

            Dim openingFloat As Decimal
            If Not Decimal.TryParse(OpeningFloatText, NumberStyles.Number, CultureInfo.CurrentCulture, openingFloat) OrElse openingFloat < 0D Then
                ShowValidation("Opening float must be zero or a positive amount.")
                Return
            End If

            Dim idempotencyKey As String = Guid.NewGuid().ToString("D")

            IsBusy = True

            Try
                Dim result As ApiResult(Of CashierSessionResponse) = Await _client.OpenCashierSessionAsync(openingFloat, idempotencyKey)

                If result.IsSuccess Then

                    CurrentSession = result.Value
                    StatusMessage = String.Format(CultureInfo.CurrentCulture, "Session #{0} opened.", result.Value.Id)
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

        Public Async Function CloseAsync() As Task

            If CurrentSession Is Nothing Then
                Return
            End If

            Dim declaredCash As Decimal
            If Not Decimal.TryParse(DeclaredCashText, NumberStyles.Number, CultureInfo.CurrentCulture, declaredCash) OrElse declaredCash < 0D Then
                ShowValidation("Declared cash must be zero or a positive amount.")
                Return
            End If

            Dim idempotencyKey As String = Guid.NewGuid().ToString("D")

            IsBusy = True

            Try
                Dim result As ApiResult(Of CashierSessionResponse) =
                    Await _client.CloseCashierSessionAsync(CurrentSession.Id, declaredCash, idempotencyKey)

                If result.IsSuccess Then

                    CurrentSession = result.Value
                    StatusMessage = String.Format(CultureInfo.CurrentCulture, "Session #{0} closed.", result.Value.Id)
                    ResultText = String.Empty
                    CorrelationId = result.CorrelationId
                    LastCallFailed = False
                    DeclaredCashText = String.Empty

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
