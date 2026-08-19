' Merchandising.ClientCommon.Mvvm.ObservableObject
'
' The INotifyPropertyChanged base every view model in every client inherits.
'
' Spec section 6.1 names CommunityToolkit.Mvvm for this job, and that is still
' the intended destination. It is NOT used here because the package is not
' recorded in docs/adr.md, and CLAUDE.md section 6 forbids introducing a
' version that is not pinned - "stop and ask" rather than reach for latest.
' Pinning the toolkit is a real decision with a source-generator question
' attached (its ObservableProperty/RelayCommand generators are C#-only, so the
' VB story needs measuring before it is promised), and it belongs to Phase 2
' where the first real screen exists to justify it.
'
' Thirty lines of hand-written notification costs nothing and blocks nothing.
' When the toolkit is pinned, this type is deleted and view models change
' their base class.

Imports System.ComponentModel
Imports System.Runtime.CompilerServices

Namespace Mvvm

    ''' <summary>Minimal change-notification base for view models.</summary>
    Public MustInherit Class ObservableObject
        Implements INotifyPropertyChanged

        ''' <summary>Raised when a bound property changes value.</summary>
        Public Event PropertyChanged As PropertyChangedEventHandler _
            Implements INotifyPropertyChanged.PropertyChanged

        ''' <summary>
        ''' Assigns <paramref name="field"/> and notifies, but only when the
        ''' value actually changed - a redundant notification re-renders bindings
        ''' for nothing.
        ''' </summary>
        ''' <returns>True when the value changed.</returns>
        Protected Function SetProperty(Of T)(ByRef field As T,
                                             value As T,
                                             <CallerMemberName> Optional propertyName As String = Nothing) As Boolean

            If EqualityComparer(Of T).Default.Equals(field, value) Then
                Return False
            End If

            field = value
            RaisePropertyChanged(propertyName)
            Return True

        End Function

        ''' <summary>Notifies that <paramref name="propertyName"/> changed.</summary>
        Protected Sub RaisePropertyChanged(<CallerMemberName> Optional propertyName As String = Nothing)

            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(propertyName))

        End Sub

    End Class

End Namespace
