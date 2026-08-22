' Merchandising.Maintenance.Restore.RestoreShellOutcome
'
' Raw result of one mysql.exe invocation. Deliberately draws no conclusion:
' "exit code 0" and "the database is usable" are different claims, and
' RestoreCommand is what decides the second one by counting rows.

Namespace Restore

    Public NotInheritable Class RestoreShellOutcome

        Public Sub New(exitCode As Integer, standardError As String)
            _exitCode = exitCode
            _standardError = standardError
        End Sub

        Private ReadOnly _exitCode As Integer
        Private ReadOnly _standardError As String

        Public ReadOnly Property ExitCode As Integer
            Get
                Return _exitCode
            End Get
        End Property

        Public ReadOnly Property StandardError As String
            Get
                Return _standardError
            End Get
        End Property

    End Class

End Namespace
