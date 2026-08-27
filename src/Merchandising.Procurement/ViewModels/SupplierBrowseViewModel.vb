' Merchandising.Procurement.ViewModels.SupplierBrowseViewModel
'
' P3-07 supplier browse: a read-only search over GET /api/v1/suppliers. This
' screen does not create, update, deactivate or reactivate a supplier -
' spec section 10.1 gives Procurement "supplier records" and P2-10 already
' built the full CRUD/lifecycle API for it, but this card's own Done-when
' box names "supplier browse" only. Adding maintenance screens here would be
' scope this card was not asked for.

Imports System.Collections.ObjectModel
Imports System.Globalization
Imports Merchandising.ClientCommon.Api
Imports Merchandising.ClientCommon.Mvvm
Imports Merchandising.Contracts.Suppliers

Namespace ViewModels

    ''' <summary>Read-only supplier search for the Suppliers tab.</summary>
    Public NotInheritable Class SupplierBrowseViewModel
        Inherits ObservableObject

        Private Const PageSize As Integer = 100

        Private ReadOnly _client As MerchandisingApiClient

        Private _searchText As String = String.Empty
        Private _includeInactive As Boolean
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

        Public ReadOnly Property Suppliers As New ObservableCollection(Of SupplierResponse)()

        Public ReadOnly Property SearchCommand As AsyncRelayCommand

        Public Property SearchText As String
            Get
                Return _searchText
            End Get
            Set(value As String)
                SetProperty(_searchText, value)
            End Set
        End Property

        Public Property IncludeInactive As Boolean
            Get
                Return _includeInactive
            End Get
            Set(value As Boolean)
                SetProperty(_includeInactive, value)
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
                Dim result As ApiResult(Of SupplierSearchResponse) =
                    Await _client.SearchSuppliersAsync(SearchText, 1, PageSize, IncludeInactive)

                If result.IsSuccess Then

                    Suppliers.Clear()
                    For Each supplier As SupplierResponse In result.Value.Items
                        Suppliers.Add(supplier)
                    Next

                    TotalCount = result.Value.TotalCount
                    StatusMessage = String.Format(CultureInfo.CurrentCulture, "{0} supplier(s) found.", TotalCount)
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
