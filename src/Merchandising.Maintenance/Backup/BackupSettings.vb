' Merchandising.Maintenance.Backup.BackupSettings
'
' The operator-tunable half of a backup run, read from SystemSettings
' (spec section 15: "Retention - configurable count, with the configured
' value recorded in SystemSettings"). Seeded by db/migrations/0003_backup.sql
' so a fresh install has safe values before the first run.
'
' Kept in Maintenance rather than Infrastructure on purpose: Infrastructure
' must not depend on Maintenance (CLAUDE.md section 4), so the repository
' hands back raw key/value pairs and this type is assembled here.

Namespace Backup

    ''' <summary>Operator-configurable settings for a backup run.</summary>
    Public NotInheritable Class BackupSettings

        ''' <summary>
        ''' Protected host directory the dump is written to. Outside the
        ''' binaries and never served by the API (spec section 15,
        ''' "Location"). Defaults to the P0-07 directory.
        ''' </summary>
        Public Property Directory As String = "C:\MerchandisingBackups"

        ''' <summary>
        ''' How many dumps to keep. Older ones are deleted after a
        ''' successful run. A value below 1 is treated as "prune nothing" -
        ''' see <see cref="BackupCommand"/>, which will not let a
        ''' misconfigured zero delete every backup on the machine.
        ''' </summary>
        Public Property RetentionCount As Integer = 7

        ''' <summary>
        ''' Volume LABEL of the off-host drive, never a drive letter. A USB
        ''' stick mounts as D: on one machine and F: on the next, and the
        ''' demo runs on three machines nobody has surveyed (ADR-012,
        ''' ADR-015). Matching on the label is what makes one configuration
        ''' correct everywhere.
        ''' </summary>
        Public Property OffHostVolumeLabel As String = "MERCHBACKUP"

        ''' <summary>Subdirectory created on the off-host volume.</summary>
        Public Property OffHostFolderName As String = "MerchandisingBackups"

    End Class

End Namespace
