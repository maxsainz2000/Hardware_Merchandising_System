' Merchandising.ClientCommon.Mvvm.RelayCommand
'
' A hand-rolled ICommand, for the same reason ObservableObject is hand-rolled
' (see that file's header): spec section 6.1 names CommunityToolkit.Mvvm for
' this job, but the package is not yet pinned in docs/adr.md, and CLAUDE.md
' section 6 forbids reaching for an unpinned version. P1-15's MainWindow used
' plain Click handlers instead of a command and said outright that hand-
' rolling command infrastructure "for three buttons in a throwaway spike" was
' scaffolding Phase 1 should refuse. P3-07 is not a spike - the Procurement
' client has enough buttons, each gated by its own enable/disable rule
' (submit only when a Draft is selected, and so on), that repeating that
' logic by hand in every code-behind file is the real scaffolding risk. This
' type is deliberately small and is deleted, along with ObservableObject, the
' day the toolkit is pinned.
'
' CanExecuteChanged forwards to CommandManager.RequerySuggested rather than
' keeping its own subscriber list. That is the ordinary WPF command pattern:
' the command manager already raises RequerySuggested on the input events
' that could plausibly change a button's enabled state (focus change, a
' click, a key press), so every bound button re-evaluates CanExecute without
' this class having to know when its own predicate's inputs changed.

Imports System.Windows.Input

Namespace Mvvm

    ''' <summary>An ICommand over a synchronous action.</summary>
    Public NotInheritable Class RelayCommand
        Implements ICommand

        Private ReadOnly _execute As Action
        Private ReadOnly _canExecute As Func(Of Boolean)

        Public Sub New(execute As Action, Optional canExecute As Func(Of Boolean) = Nothing)

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
            Return _canExecute Is Nothing OrElse _canExecute()
        End Function

        Public Sub Execute(parameter As Object) Implements ICommand.Execute
            _execute()
        End Sub

    End Class

End Namespace
