' Merchandising.ClientCommon.Mvvm.AsyncRelayCommand
'
' The async counterpart to RelayCommand - see that file's header for why a
' hand-rolled command exists here at all instead of CommunityToolkit.Mvvm's.
'
' Every write this client makes is an API call, so every command that
' matters is asynchronous. ICommand.Execute is declared to return nothing
' (Sub), which is the one place this class cannot avoid an async Sub - the
' same shape CommunityToolkit.Mvvm's own AsyncRelayCommand uses internally
' for the same reason. Re-entrancy is guarded explicitly: a second click
' while a call is in flight is ignored rather than firing a second request,
' because a purchase-order submit or approval is exactly the kind of write
' that must never be sent twice by an impatient double-click (ADR-007 covers
' a repeated CLIENT, not a doubled click generating two distinct requests).
Imports System.Windows.Input

Namespace Mvvm

    ''' <summary>An ICommand over an asynchronous action, with re-entrancy guarded out.</summary>
    Public NotInheritable Class AsyncRelayCommand
        Implements ICommand

        Private ReadOnly _execute As Func(Of Task)
        Private ReadOnly _canExecute As Func(Of Boolean)
        Private _isExecuting As Boolean

        Public Sub New(execute As Func(Of Task), Optional canExecute As Func(Of Boolean) = Nothing)

            If execute Is Nothing Then
                Throw New ArgumentNullException(NameOf(execute))
            End If

            _execute = execute
            _canExecute = canExecute

        End Sub

        Public Custom Event CanExecuteChanged As EventHandler Implements ICommand.CanExecuteChanged

            AddHandler(value As EventHandler)
                AddHandler CommandManager.RequerySuggested, value
            End AddHandler

            RemoveHandler(value As EventHandler)
                RemoveHandler CommandManager.RequerySuggested, value
            End RemoveHandler

            RaiseEvent(sender As Object, e As EventArgs)
            End RaiseEvent

        End Event

        Public Function CanExecute(parameter As Object) As Boolean Implements ICommand.CanExecute
            Return Not _isExecuting AndAlso (_canExecute Is Nothing OrElse _canExecute())
        End Function

        Public Async Sub Execute(parameter As Object) Implements ICommand.Execute

            If _isExecuting Then
                Return
            End If

            _isExecuting = True

            Try
                Await _execute()
            Finally
                _isExecuting = False
            End Try

        End Sub

    End Class

End Namespace
