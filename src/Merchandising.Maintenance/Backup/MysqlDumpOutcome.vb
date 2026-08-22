' Merchandising.Maintenance.Backup.MysqlDumpOutcome
'
' What mysqldump reported. Deliberately dumb: it carries the exit code and
' stderr and draws no conclusion from them, because "exit code 0" and "a
' usable backup exists" are different claims and conflating them is the
' failure mode this card exists to prevent.

Namespace Backup

    ''' <summary>Raw result of one mysqldump invocation.</summary>
    Public NotInheritable Class MysqlDumpOutcome

        Public Sub New(exitCode As Integer, standardError As String)
            _exitCode = exitCode
            _standardError = standardError
        End Sub

        Private ReadOnly _exitCode As Integer
        Private ReadOnly _standardError As String

        ''' <summary>Process exit code. Zero means mysqldump did not report an error.</summary>
        Public ReadOnly Property ExitCode As Integer
            Get
                Return _exitCode
            End Get
        End Property

        ''' <summary>Whatever mysqldump wrote to stderr, including warnings on a successful run.</summary>
        Public ReadOnly Property StandardError As String
            Get
                Return _standardError
            End Get
        End Property

    End Class

End Namespace
