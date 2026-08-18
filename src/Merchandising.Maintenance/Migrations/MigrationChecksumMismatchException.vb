' Merchandising.Maintenance.Migrations.MigrationChecksumMismatchException
'
' Raised when a migration file already recorded as successfully applied no
' longer matches its recorded checksum. This is the refusal named in
' ADR-008 and CLAUDE.md stop condition 6: applied migrations are immutable,
' and a changed file is refused rather than silently re-applied or skipped.

Namespace Migrations

    Public NotInheritable Class MigrationChecksumMismatchException
        Inherits Exception

        Public Sub New(migrationId As String, recordedChecksum As String, currentChecksum As String)

            MyBase.New(
                $"Migration '{migrationId}' was already applied with checksum " &
                $"'{recordedChecksum}', but the file on disk now checksums to " &
                $"'{currentChecksum}'. Applied migrations are immutable - write a new " &
                "migration instead of editing this one.")

            Me.MigrationId = migrationId
            Me.RecordedChecksum = recordedChecksum
            Me.CurrentChecksum = currentChecksum

        End Sub

        Public ReadOnly Property MigrationId As String
        Public ReadOnly Property RecordedChecksum As String
        Public ReadOnly Property CurrentChecksum As String

    End Class

End Namespace
