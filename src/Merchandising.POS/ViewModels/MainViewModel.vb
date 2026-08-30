' Merchandising.POS.ViewModels.MainViewModel
'
' P5-13: the composition root for the POS client. Owns the one
' MerchandisingApiClient the window uses, the sign-in/sign-out screen, and
' the three workspace tabs (session, checkout, returns) - the same shape
' Merchandising.Procurement.ViewModels.MainViewModel (P3-07) and
' Merchandising.Inventory.ViewModels.MainViewModel (P4-12) already establish
' for those clients.
'
' Session handling is identical to both: the token lives in
' MerchandisingApiClient's InMemoryTokenStore only, is never written to
' disk, and every screen learns the API is unreachable or under maintenance
' from the refusal itself rather than by polling.
'
' CompletedSales IS THE ONE COLLECTION Checkout WRITES TO AND Returns READS
' FROM - constructed here, once, and handed to both constructors so "take a
' return" has something to return against (Checkout's own header explains
' why no server lookup exists for this).

Imports System.Collections.ObjectModel
Imports System.Globalization
Imports Merchandising.ClientCommon.Api
Imports Merchandising.ClientCommon.Mvvm
Imports Merchandising.Contracts.Auth
Imports Merchandising.Contracts.Sales

Namespace ViewModels

    ''' <summary>Composition root and sign-in/sign-out state for the POS client.</summary>
    Public NotInheritable Class MainViewModel
        Inherits ObservableObject

        ''' <summary>Where the API lives - a name, never an address, the same reasoning Procurement/Inventory's own MainViewModel gives (ADR-011's name-only certificate SAN).</summary>
        Public Const DefaultApiAddress As String = "https://MERCH-HOST:8443/"

        Private ReadOnly _client As MerchandisingApiClient

        Private _username As String = String.Empty
        Private _isBusy As Boolean
        Private _isSignedIn As Boolean
        Private _currentUserDisplay As String = String.Empty
        Private _statusMessage As String = "Not signed in."
        Private _resultText As String = String.Empty
        Private _correlationId As String = String.Empty
        Private _lastCallFailed As Boolean
        Private _isInMaintenance As Boolean
        Private _maintenanceMessage As String = String.Empty
        Private _connection As ConnectionState = ConnectionState.Unknown

        Public Sub New(client As MerchandisingApiClient)

            If client Is Nothing Then
                Throw New ArgumentNullException(NameOf(client))
            End If

            _client = client
            AddHandler _client.ConnectionStateChanged, AddressOf OnConnectionStateChanged

            Dim completedSales As New ObservableCollection(Of SaleResponse)()

            Session = New SessionViewModel(_client)
            Checkout = New CheckoutViewModel(_client, completedSales)
            Returns = New ReturnsViewModel(_client, completedSales)

            SignOutCommand = New AsyncRelayCommand(AddressOf SignOutAsync, Function() Not IsBusy AndAlso IsSignedIn)

        End Sub

        Public ReadOnly Property Session As SessionViewModel
        Public ReadOnly Property Checkout As CheckoutViewModel
        Public ReadOnly Property Returns As ReturnsViewModel

        Public ReadOnly Property SignOutCommand As AsyncRelayCommand

        Public ReadOnly Property ApiAddress As String
            Get
                Return _client.BaseAddress.ToString()
            End Get
        End Property

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

        Public Property IsBusy As Boolean
            Get
                Return _isBusy
            End Get
            Private Set(value As Boolean)
                If SetProperty(_isBusy, value) Then
                    RaisePropertyChanged(NameOf(CanSignIn))
                End If
            End Set
        End Property

        Public Property IsSignedIn As Boolean
            Get
                Return _isSignedIn
            End Get
            Private Set(value As Boolean)
                If SetProperty(_isSignedIn, value) Then
                    RaisePropertyChanged(NameOf(CanSignIn))
                End If
            End Set
        End Property

        Public ReadOnly Property CanSignIn As Boolean
            Get
                Return Not IsBusy AndAlso Not IsSignedIn AndAlso Not String.IsNullOrWhiteSpace(_username)
            End Get
        End Property

        ''' <summary>"username (Role1, Role2)" once signed in - shown so every screen states whose session is active.</summary>
        Public Property CurrentUserDisplay As String
            Get
                Return _currentUserDisplay
            End Get
            Private Set(value As String)
                SetProperty(_currentUserDisplay, value)
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

        ''' <summary>The API's own correlation Id for the last call - never invented, quoted verbatim (ADR-014).</summary>
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

        Public Property IsInMaintenance As Boolean
            Get
                Return _isInMaintenance
            End Get
            Private Set(value As Boolean)
                SetProperty(_isInMaintenance, value)
            End Set
        End Property

        Public Property MaintenanceMessage As String
            Get
                Return _maintenanceMessage
            End Get
            Private Set(value As String)
                SetProperty(_maintenanceMessage, value)
            End Set
        End Property

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

        ''' <summary>Signs in. The PasswordBox is read directly by the code-behind and passed here - see MainWindow's own header.</summary>
        Public Async Function SignInAsync(password As String) As Task

            IsBusy = True

            Try
                Dim result As ApiResult(Of LoginResponse) = Await _client.LoginAsync(Username, password)

                If result.IsSuccess Then

                    IsSignedIn = True
                    CurrentUserDisplay = String.Format(CultureInfo.CurrentCulture,
                                                       "{0} ({1})",
                                                       result.Value.Username,
                                                       String.Join(", ", result.Value.Roles))

                    StatusMessage = "Signed in."
                    ResultText = String.Format(CultureInfo.CurrentCulture,
                                               "Session expires {0:u}. Token held in memory only.",
                                               result.Value.ExpiresAtUtc)
                    CorrelationId = result.CorrelationId
                    LastCallFailed = False
                    IsInMaintenance = False

                Else
                    ShowFailure(result)
                End If

            Finally
                IsBusy = False
            End Try

        End Function

        Private Async Function SignOutAsync() As Task

            IsBusy = True

            Try
                Dim result As ApiResult(Of MeResponse) = Await _client.LogoutAsync()

                IsSignedIn = False
                CurrentUserDisplay = String.Empty

                If result.IsSuccess Then
                    StatusMessage = "Signed out. Token discarded."
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
            IsInMaintenance = display.IsMaintenance

            If display.IsMaintenance Then
                MaintenanceMessage = display.MaintenanceMessage
            End If

        End Sub

        Private Sub OnConnectionStateChanged(sender As Object, e As EventArgs)

            Connection = _client.State

        End Sub

    End Class

End Namespace
