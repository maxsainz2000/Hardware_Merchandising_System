' Merchandising.Contracts.Maintenance.EnterMaintenanceRequest
'
' Spec section 15 step 1: "A Super Admin requests maintenance mode through the
' authorized administrative workflow and PROVIDES A REASON." The reason is not
' decoration - it is what connected clients are shown, and what someone reading
' the lock history later has to work from.

Imports System.Text.Json.Serialization

Namespace Maintenance

    Public NotInheritable Class EnterMaintenanceRequest

        <JsonPropertyName("reason")>
        Public Property Reason As String = String.Empty

    End Class

End Namespace
