' Merchandising.Inventory.ViewModels.SpikeViewModel
'
' The whole of the Phase 1 client spike (P1-15): sign in, call one protected
' endpoint, run one stock decrement, and show the connection state honestly.
'
' Not a feature. There is no product grid, no search, and no second screen -
' those are Phase 2, and building them here would be a defect rather than
' initiative (CLAUDE.md, Phase 1 scope discipline).
'
' The behaviour this class exists to demonstrate is gap G-26: when the API
' cannot be reached, the client says so plainly and the write is REFUSED. It
' is never buffered, never retried in the background, and never rendered as
' though it succeeded. Read ShowResult below - the Unavailable branch writes a
' failure and nothing else.

Imports System.Globalization
Imports Merchandising.ClientCommon.Api
Imports Merchandising.ClientCommon.Mvvm
Imports Merchandising.Contracts.Auth
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Inventory

Namespace ViewModels

    ''' <summary>View model behind the single Phase 1 spike window.</summary>
    Public NotInheritable Class SpikeViewModel
        Inherits ObservableObject

        ''' <summary>
        ''' Where the API lives. A NAME, never an address: the certificate has a
        ''' name-only SAN (ADR-011), so connecting by IP fails validation by
        ''' design. Which machine the name points at is a per-installation hosts
        ''' entry, which is what keeps this package free of any one network's
        ''' addressing (ADR-012).
        ''' </summary>
        Public Const DefaultApiAddress As String = "https://MERCH-HOST:8443/"

        Private ReadOnly _client As MerchandisingApiClient

        Private _username As String = String.Empty
        Private _productId As String = "1"
        Private _quantity As String = "1"
        Private _isBusy As Boolean
        Private _isSignedIn As Boolean
        Private _statusMessage As String = "Not signed in."
        Private _resultText As String = String.Empty
        Private _correlationId As String = String.Empty
        Private _lastCallFailed As Boolean
        Private _connection As ConnectionState = ConnectionState.Unknown

        ''' <summary>Builds the view model over a live API client.</summary>
        Public Sub New(client As MerchandisingApiClient)

            If client Is Nothing Then
                Throw New ArgumentNullException(NameOf(client))
            End If

            _client = client
            AddHandler _client.ConnectionStateChanged, AddressOf OnConnectionStateChanged

        End Sub

        ''' <summary>The API root, shown so a failure names a host the operator recognises.</summary>
        Public ReadOnly Property ApiAddress As String
            Get
                Return _client.BaseAddress.ToString()
            End Get
        End Property

        ''' <summary>Operator's username.</summary>
        Public Property Username As String
            Get
                Return _username
            End Get
            Set(value As String)
                If SetProperty(_username, value) Then
                    RaisePropertyChanged(NameOf(CanSignIn))
                End If
            End Set
        End Property

        ''' <summary>Product to decrement. Text, so a bad value is a validation message rather than a binding failure.</summary>
        Public Property ProductId As String
            Get
                Return _productId
            End Get
            Set(value As String)
                SetProperty(_productId, value)
            End Set
        End Property

        ''' <summary>Quantity to remove.</summary>
        Public Property Quantity As String
            Get
                Return _quantity
            End Get
            Set(value As String)
                SetProperty(_quantity, value)
            End Set
        End Property

        ''' <summary>True while a call is in flight; drives the loading state (spec section 16).</summary>
        Public Property IsBusy As Boolean
            Get
                Return _isBusy
            End Get
            Private Set(value As Boolean)
                If SetProperty(_isBusy, value) Then
                    RaisePropertyChanged(NameOf(CanSignIn))
                    RaisePropertyChanged(NameOf(CanCallProtected))
                End If
            End Set
        End Property

        ''' <summary>True once a session token is held.</summary>
        Public Property IsSignedIn As Boolean
            Get
                Return _isSignedIn
            End Get
            Private Set(value As Boolean)
                If SetProperty(_isSignedIn, value) Then
                    RaisePropertyChanged(NameOf(CanSignIn))
                    RaisePropertyChanged(NameOf(CanCallProtected))
                End If
            End Set
        End Property

        ''' <summary>Sign-in is available when idle and not already signed in.</summary>
        Public ReadOnly Property CanSignIn As Boolean
            Get
                Return Not IsBusy AndAlso Not IsSignedIn AndAlso Not String.IsNullOrWhiteSpace(_username)
            End Get
        End Property

        ''' <summary>Protected calls are available when idle and signed in.</summary>
        Public ReadOnly Property CanCallProtected As Boolean
            Get
                Return Not IsBusy AndAlso IsSignedIn
            End Get
        End Property

        ''' <summary>One-line status, always the plain truth about the last attempt.</summary>
        Public Property StatusMessage As String
            Get
                Return _statusMessage
            End Get
            Private Set(value As String)
                SetProperty(_statusMessage, value)
            End Set
        End Property

        ''' <summary>Detail of the last response, for the demonstration.</summary>
        Public Property ResultText As String
            Get
                Return _resultText
            End Get
            Private Set(value As String)
                SetProperty(_resultText, value)
            End Set
        End Property

        ''' <summary>
        ''' Correlation ID of the last call. Displayed rather than hidden: it is
        ''' the string an operator quotes so the matching AuditLogs row can be
        ''' found (ADR-014).
        ''' </summary>
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

        ''' <summary>True when there is a correlation ID worth showing.</summary>
        Public ReadOnly Property HasCorrelationId As Boolean
            Get
                Return Not String.IsNullOrEmpty(_correlationId)
            End Get
        End Property

        ''' <summary>True when the last call failed, so the result reads as a failure.</summary>
        Public Property LastCallFailed As Boolean
            Get
                Return _lastCallFailed
            End Get
            Private Set(value As Boolean)
                SetProperty(_lastCallFailed, value)
            End Set
        End Property

        ''' <summary>Reachability of the API, bound to the status indicator.</summary>
        Public Property Connection As ConnectionState
            Get
                Return _connection
            End Get
            Private Set(value As ConnectionState)
                If SetProperty(_connection, value) Then
                    RaisePropertyChanged(NameOf(ConnectionText))
                End If
            End Set
        End Property

        ''' <summary>The connection state in words.</summary>
        Public ReadOnly Property ConnectionText As String
            Get
                Select Case Connection
                    Case ConnectionState.Online
                        Return "Connected"
                    Case ConnectionState.Unavailable
                        Return "API unavailable - writes refused"
                    Case Else
                        Return "Not contacted yet"
                End Select
            End Get
        End Property

        ''' <summary>Signs in and stores the token in memory only.</summary>
        Public Async Function SignInAsync(password As String) As Task

            IsBusy = True

            Try
                Dim result As ApiResult(Of LoginResponse) = Await _client.LoginAsync(Username, password)

                If result.IsSuccess Then
                    IsSignedIn = True
                    ShowSuccess(String.Format(CultureInfo.CurrentCulture,
                                              "Signed in as {0} ({1}).",
                                              result.Value.Username,
                                              String.Join(", ", result.Value.Roles)),
                                String.Format(CultureInfo.CurrentCulture,
                                              "Session expires {0:u}. Token held in memory only - never written to disk.",
                                              result.Value.ExpiresAtUtc),
                                result.CorrelationId)
                Else
                    ShowFailure(result)
                End If

            Finally
                IsBusy = False
            End Try

        End Function

        ''' <summary>Calls the protected identity endpoint.</summary>
        Public Async Function CallProtectedEndpointAsync() As Task

            IsBusy = True

            Try
                Dim result As ApiResult(Of MeResponse) = Await _client.GetCurrentUserAsync()

                If result.IsSuccess Then
                    ShowSuccess("GET /api/v1/auth/me succeeded.",
                                String.Format(CultureInfo.CurrentCulture,
                                              "Username: {0}" & Environment.NewLine & "Roles: {1}",
                                              result.Value.Username,
                                              String.Join(", ", result.Value.Roles)),
                                result.CorrelationId)
                Else
                    ShowFailure(result)
                End If

            Finally
                IsBusy = False
            End Try

        End Function

        ''' <summary>
        ''' Runs one stock decrement. Every press generates a NEW idempotency key,
        ''' because each press is a new intent; re-sending the same key is how a
        ''' retry of one intent stays safe (ADR-007), and that is deliberately not
        ''' something this window does behind the operator's back.
        ''' </summary>
        Public Async Function DecrementStockAsync() As Task

            Dim product As Integer
            If Not Integer.TryParse(ProductId, NumberStyles.Integer, CultureInfo.CurrentCulture, product) OrElse product <= 0 Then
                ShowValidation("Product ID must be a positive whole number.")
                Return
            End If

            Dim amount As Decimal
            If Not Decimal.TryParse(Quantity, NumberStyles.Number, CultureInfo.CurrentCulture, amount) OrElse amount <= 0D Then
                ShowValidation("Quantity must be a positive number.")
                Return
            End If

            Dim idempotencyKey As String = Guid.NewGuid().ToString("D")

            IsBusy = True

            Try
                Dim result As ApiResult(Of StockDecrementResponse) =
                    Await _client.DecrementStockAsync(product, amount, "P1-15 client spike", idempotencyKey)

                If result.IsSuccess Then
                    ShowSuccess("Stock decremented.",
                                String.Format(CultureInfo.CurrentCulture,
                                              "Product {0}: {1} -> {2}" & Environment.NewLine &
                                              "Movement ID: {3}" & Environment.NewLine &
                                              "Idempotency key: {4}",
                                              result.Value.ProductId,
                                              result.Value.QuantityBefore,
                                              result.Value.QuantityAfter,
                                              result.Value.MovementId,
                                              idempotencyKey),
                                result.CorrelationId)
                Else
                    ShowFailure(result)
                End If

            Finally
                IsBusy = False
            End Try

        End Function

        ''' <summary>Ends the session and drops the token.</summary>
        Public Async Function SignOutAsync() As Task

            IsBusy = True

            Try
                Dim result As ApiResult(Of MeResponse) = Await _client.LogoutAsync()

                IsSignedIn = False

                If result.IsSuccess Then
                    ShowSuccess("Signed out. Token discarded.", String.Empty, result.CorrelationId)
                Else
                    ShowFailure(result)
                End If

            Finally
                IsBusy = False
            End Try

        End Function

        ''' <summary>Renders a successful call.</summary>
        Private Sub ShowSuccess(status As String, detail As String, correlationId As String)

            LastCallFailed = False
            StatusMessage = status
            ResultText = detail
            CorrelationId = correlationId

        End Sub

        ''' <summary>
        ''' Renders a failed call. The two failure kinds are shown differently on
        ''' purpose: a rejection carries the API's own error code and correlation
        ''' ID, while an unreachable API carries neither - and saying so is the
        ''' honest answer, because the client genuinely does not know whether any
        ''' work happened server-side. It does not guess, and it does not queue.
        ''' </summary>
        Private Sub ShowFailure(Of T As Class)(result As ApiResult(Of T))

            LastCallFailed = True

            If result.Outcome = ApiOutcome.Unavailable Then
                StatusMessage = "The API could not be reached. Nothing was sent and nothing was saved."
                ResultText = result.TransportDetail & Environment.NewLine & Environment.NewLine &
                             "This client is online-only by design. The request has NOT been queued " &
                             "and will NOT be retried automatically - re-run it once the API is back."
                CorrelationId = String.Empty
                Return
            End If

            Dim envelope As ApiErrorResponse = result.[Error]

            StatusMessage = String.Format(CultureInfo.CurrentCulture,
                                          "The API refused the request ({0}).",
                                          envelope.ErrorCode)
            ResultText = envelope.Message
            CorrelationId = result.CorrelationId

        End Sub

        ''' <summary>Renders a client-side validation failure; nothing is sent.</summary>
        Private Sub ShowValidation(message As String)

            LastCallFailed = True
            StatusMessage = "Check the values before sending."
            ResultText = message
            CorrelationId = String.Empty

        End Sub

        Private Sub OnConnectionStateChanged(sender As Object, e As EventArgs)

            Connection = _client.State

        End Sub

    End Class

End Namespace
