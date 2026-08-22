' Merchandising.Maintenance.Backup.BackupResult
'
' Everything one backup run produced. This is what becomes a BackupLogs row
' and what decides the process exit code, so it carries the failure detail
' as prominently as the success fields - spec section 15's "Failure handling"
' control requires error details to be recorded and an operational warning
' raised, and a result type that only models success cannot do that.

Namespace Backup

    ''' <summary>Outcome and artefacts of one backup run.</summary>
    Public NotInheritable Class BackupResult

        ''' <summary>What happened, in three states rather than two.</summary>
        Public Property Outcome As BackupOutcome

        ''' <summary>UTC start time. Recorded even when the run fails.</summary>
        Public Property StartedAtUtc As Date

        ''' <summary>UTC completion time, or Nothing if the run did not complete.</summary>
        Public Property CompletedAtUtc As Date?

        ''' <summary>Full path of the local dump, or Nothing when none was produced.</summary>
        Public Property FilePath As String

        ''' <summary>Size of the dump in bytes, or Nothing when none was produced.</summary>
        Public Property SizeBytes As Long?

        ''' <summary>
        ''' Lower-case hex SHA-256 of the dump. Recorded only after being
        ''' recomputed from the file on disk, never from a buffer held in
        ''' memory - a checksum of what we meant to write proves nothing
        ''' about what actually landed.
        ''' </summary>
        Public Property Sha256 As String

        ''' <summary>Path of the off-host copy, or Nothing when it did not happen.</summary>
        Public Property OffHostPath As String

        ''' <summary>Server version string the dump came from.</summary>
        Public Property SourceDbVersion As String

        ''' <summary>Retention count in force for this run.</summary>
        Public Property RetentionCount As Integer

        ''' <summary>How many older dumps were deleted after this run.</summary>
        Public Property PrunedFileCount As Integer

        ''' <summary>Correlation ID tying this run to its BackupLogs row and log lines.</summary>
        Public Property CorrelationId As String

        ''' <summary>
        ''' Human-readable detail. On failure this is the error; on a partial
        ''' run it is the warning explaining what did not happen. Never
        ''' contains the credential - the defaults file that holds it is
        ''' never echoed into here.
        ''' </summary>
        Public Property Detail As String

        ''' <summary>
        ''' Whether the BackupLogs row was actually written. False means the
        ''' database could not be reached, which is itself a reportable
        ''' condition rather than something to shrug off: the run then falls
        ''' back to the local failure log.
        ''' </summary>
        Public Property LoggedToDatabase As Boolean

        ''' <summary>
        ''' Process exit code this result should produce. Zero only for a
        ''' clean success; a partial run exits non-zero so a scheduled task
        ''' shows as failed rather than quietly degrading for months.
        ''' </summary>
        Public ReadOnly Property ExitCode As Integer
            Get
                Return If(Outcome = BackupOutcome.Succeeded, 0, 1)
            End Get
        End Property

    End Class

End Namespace
