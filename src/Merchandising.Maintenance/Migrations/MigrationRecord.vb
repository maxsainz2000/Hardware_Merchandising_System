' Merchandising.Maintenance.Migrations.MigrationRecord
'
' A row read back from the SchemaMigrations table: what the database
' currently believes about one migration's application history.

Namespace Migrations

    Public NotInheritable Class MigrationRecord

        Public Sub New(
            migrationId As String,
            checksum As String,
            appliedAtUtc As DateTime,
            succeeded As Boolean,
            errorMessage As String)

            Me.MigrationId = migrationId
            Me.Checksum = checksum
            Me.AppliedAtUtc = appliedAtUtc
            Me.Succeeded = succeeded
            Me.ErrorMessage = errorMessage

        End Sub

        Public ReadOnly Property MigrationId As String
        Public ReadOnly Property Checksum As String
        Public ReadOnly Property AppliedAtUtc As DateTime
        Public ReadOnly Property Succeeded As Boolean
        Public ReadOnly Property ErrorMessage As String

    End Class

End Namespace
