' Merchandising.Inventory.MainWindow
'
' Code-behind for the P4-12 client. Owns exactly three things, the same
' scope Merchandising.Procurement's MainWindow (P3-07) draws for itself:
' constructing the API client, reading the PasswordBox (never bindable - WPF
' does not expose Password as a dependency property), and disposing the
' client on close.
'
' Every other verb in this window is an ICommand (RelayCommand/AsyncRelayCommand,
' Merchandising.ClientCommon.Mvvm) bound straight from XAML - this is a real,
' multi-screen client rather than a three-button spike, so hand-wiring a
' Click handler per button here would be exactly the scattered logic
' RelayCommand's own header was added to avoid.

Imports System.ComponentModel
Imports Merchandising.ClientCommon.Api
Imports Merchandising.Inventory.ViewModels

Class MainWindow

    Private ReadOnly _client As MerchandisingApiClient
    Private ReadOnly _viewModel As MainViewModel

    Public Sub New()

        InitializeComponent()

        _client = New MerchandisingApiClient(New Uri(MainViewModel.DefaultApiAddress))
        _viewModel = New MainViewModel(_client)

        DataContext = _viewModel

    End Sub

    Private Async Sub SignInButton_Click(sender As Object, e As RoutedEventArgs)

        Dim password As String = PasswordBox.Password

        Await _viewModel.SignInAsync(password)

        PasswordBox.Clear()

    End Sub

    Private Sub MainWindow_Closing(sender As Object, e As CancelEventArgs) Handles Me.Closing

        _client.Dispose()

    End Sub

End Class
