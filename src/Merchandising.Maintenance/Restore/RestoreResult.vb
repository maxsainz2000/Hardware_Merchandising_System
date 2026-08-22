' Merchandising.Maintenance.Restore.RestoreResult
'
' What one restore produced, and what verification found afterwards. The
' counts are here rather than logged and discarded because spec section 15
' step 7 makes releasing maintenance mode conditional on them, and step 6
' asks the operator to confirm specific things are present - "it restored"
' is not the claim being made.

Imports System.Collections.Generic

Namespace Restore

    ''' <summary>Outcome of one restore, including post-restore verification.</summary>
    Public NotInheritable Class RestoreResult

        ''' <summary>
        ''' True only when the schema is complete AND user rows are present.
        ''' A zero exit code from mysql.exe is not sufficient.
        ''' </summary>
        Public Property Succeeded As Boolean

        Public Property StartedAtUtc As Date
        Public Property CompletedAtUtc As Date?

        ''' <summary>
        ''' Restore-to-verified-state duration. This is the number PA-005's
        ''' 15-minute demonstrated RTO target is measured against - the clock
        ''' stops after verification, not after the data lands, because
        ''' "restored" and "confirmed usable" are different claims.
        ''' </summary>
        Public Property ElapsedSeconds As Double

        Public Property DumpPath As String = String.Empty
        Public Property TargetDatabase As String = String.Empty

        ''' <summary>Expected tables that did not appear. Empty on a good restore.</summary>
        Public Property MissingTables As IList(Of String) = New List(Of String)

        Public Property UserCount As Long
        Public Property ProductCount As Long
        Public Property StockBalanceCount As Long
        Public Property StockMovementCount As Long
        Public Property AuditLogCount As Long

        ''' <summary>Error or warning text. Never contains a credential.</summary>
        Public Property Detail As String = String.Empty

    End Class

End Namespace
