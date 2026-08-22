' Merchandising.Contracts.Maintenance.MaintenanceStatusResponse
'
' What clients poll to decide whether to show the maintenance warning
' (spec section 15 step 2). Readable during maintenance by design - a status
' endpoint that is itself blocked by the state it reports would be useless.

Imports System.Text.Json.Serialization

Namespace Maintenance

    Public NotInheritable Class MaintenanceStatusResponse

        <JsonPropertyName("inMaintenance")>
        Public Property InMaintenance As Boolean

        ''' <summary>Operator-supplied reason. Empty when not in maintenance.</summary>
        <JsonPropertyName("reason")>
        Public Property Reason As String = String.Empty

        ''' <summary>Operator-configurable text for clients to display, from SystemSettings.</summary>
        <JsonPropertyName("message")>
        Public Property Message As String = String.Empty

        ''' <summary>When the lock was taken, UTC. Nothing when not in maintenance.</summary>
        <JsonPropertyName("sinceUtc")>
        Public Property SinceUtc As Date?

    End Class

End Namespace
