' Merchandising.Maintenance.Backup.BackupOutcome
'
' Three outcomes, not two. "The dump worked but the USB stick was not
' plugged in" is a real and likely nightly result, and it is neither a
' success nor a failure: the local dump is good and worth keeping, but the
' off-host copy that protects against losing the host did not happen.
' Collapsing that into Succeeded hides a degraded backup; collapsing it into
' Failed throws away a usable dump and trains the operator to ignore alerts.
'
' The names match the strings written to BackupLogs.Result exactly - the
' Result column is written with ToString(), so renaming a member here
' silently changes what lands in the database.
'
' VB GOTCHA: `Partial` is a reserved keyword (the Partial Class modifier), so
' it must be written as `[Partial]` at the declaration AND at every use site.
' C# has no such collision, which is why this reads oddly to anyone coming
' from the C# side of ASP.NET Core (CLAUDE.md section 3). Renaming it to
' PartiallySucceeded would avoid the brackets, but 0003_backup.sql is already
' applied and its checksum recorded (ADR-008, stop condition 6), so the
' vocabulary that migration documents is fixed - the code bends, not the
' migration.

Namespace Backup

    ''' <summary>Outcome of one backup run.</summary>
    Public Enum BackupOutcome

        ''' <summary>Dump written, checksum verified, off-host copy made.</summary>
        Succeeded = 0

        ''' <summary>Dump written and verified; off-host copy did not happen.</summary>
        [Partial] = 1

        ''' <summary>No usable dump was produced.</summary>
        Failed = 2

    End Enum

End Namespace
