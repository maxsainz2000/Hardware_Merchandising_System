' Merchandising.Inventory.MainWindow
'
' Code-behind for the P1-15 spike window. It owns three things and nothing
' else: constructing the API client, handing button presses to the view model,
' and disposing the client on close.
'
' Click handlers rather than commands. A RelayCommand implementation would be
' the MVVM-correct answer and is where this goes in Phase 2 - but the toolkit
' that supplies one (CommunityToolkit.Mvvm, spec section 6.1) is not pinned in
' docs/adr.md, and hand-rolling a command infrastructure for three buttons in a
' throwaway spike is the kind of scaffolding Phase 1 is supposed to refuse. All
' the state the window displays is bound; only the verbs are wired here.
'
' The PasswordBox is read directly rather than bound. WPF does not expose
' Password as a dependency property, precisely so a plaintext password does not
' end up sitting in a bindable property, and the usual attached-property
' work-around defeats that on purpose. It is passed straight into the call and
' cleared afterwards.

Imports System.ComponentModel
Imports Merchandising.ClientCommon.Api
Imports Merchandising.Inventory.ViewModels

Class MainWindow

    Private ReadOnly _client As MerchandisingApiClient
    Private ReadOnly _viewModel As SpikeViewModel

    Public Sub New()

        InitializeComponent()

        _client = New MerchandisingApiClient(New Uri(SpikeViewModel.DefaultApiAddress))
        _viewModel = New SpikeViewModel(_client)

        DataContext = _viewModel

    End Sub

    Private Async Sub SignInButton_Click(sender As Object, e As RoutedEventArgs)

        Dim password As String = PasswordBox.Password

        Await _viewModel.SignInAsync(password)

        PasswordBox.Clear()

    End Sub

    Private Async Sub SignOutButton_Click(sender As Object, e As RoutedEventArgs)

        Await _viewModel.SignOutAsync()

    End Sub

    Private Async Sub CallProtectedButton_Click(sender As Object, e As RoutedEventArgs)

        Await _viewModel.CallProtectedEndpointAsync()

    End Sub

    Private Async Sub DecrementButton_Click(sender As Object, e As RoutedEventArgs)

        Await _viewModel.DecrementStockAsync()

    End Sub

    Private Sub MainWindow_Closing(sender As Object, e As CancelEventArgs) Handles Me.Closing

        ' Disposing the client clears the token. Closing the window is the whole
        ' of the logout story when the API cannot be reached.
        _client.Dispose()

    End Sub

End Class
