' Merchandising.Maintenance.Migrations.MigrationRunSummary
'
' The outcome of one MigrationRunner.RunAsync call: every migration that was
' actually applied during this run (migrations already recorded as
' succeeded are skipped and never appear here).

Imports System.Collections.Generic
Imports System.Linq

Namespace Migrations

    ''' <summary>One migration file that was applied (or attempted) during a run.</summary>
    Public NotInheritable Class MigrationApplication

        Public Sub New(migrationId As String, succeeded As Boolean, errorMessage As String)
            Me.MigrationId = migrationId
            Me.Succeeded = succeeded
            Me.ErrorMessage = errorMessage
        End Sub

        Public ReadOnly Property MigrationId As String
        Public ReadOnly Property Succeeded As Boolean
        Public ReadOnly Property ErrorMessage As String

    End Class

    ''' <summary>Summary of a completed <see cref="MigrationRunner.RunAsync"/> call.</summary>
    Public NotInheritable Class MigrationRunSummary

        Public Sub New(applied As IReadOnlyList(Of MigrationApplication))
            Me.Applied = applied
        End Sub

        ''' <summary>Migrations applied this run, in the order they were attempted.</summary>
        Public ReadOnly Property Applied As IReadOnlyList(Of MigrationApplication)

        ''' <summary>True if every migration attempted this run succeeded.</summary>
        Public ReadOnly Property AllSucceeded As Boolean
            Get
                Return Applied.All(Function(a) a.Succeeded)
            End Get
        End Property

    End Class

End Namespace
