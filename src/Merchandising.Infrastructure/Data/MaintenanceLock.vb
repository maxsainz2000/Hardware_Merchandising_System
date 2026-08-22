' Merchandising.Infrastructure.Data.MaintenanceLock
'
' One row of MaintenanceLocks, as read back. Carries only what a caller needs
' to decide whether to refuse a write and what to tell the operator - not the
' release columns, because a lock that has been read as "active" has none.

Namespace Data

    ''' <summary>An active maintenance lock.</summary>
    Public NotInheritable Class MaintenanceLock

        Public Property Id As Integer

        ''' <summary>
        ''' Operator-supplied reason, required at acquisition. This reaches
        ''' clients in the refusal message: spec section 15 step 2 asks for a
        ''' warning to be displayed, and "under maintenance" without a reason
        ''' is not much of a warning.
        ''' </summary>
        Public Property Reason As String = String.Empty

        Public Property RequestedByUserId As Integer

        Public Property AcquiredAtUtc As Date

        Public Property CorrelationId As String = String.Empty

    End Class

End Namespace
