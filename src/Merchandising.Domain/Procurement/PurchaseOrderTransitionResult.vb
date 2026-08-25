' Merchandising.Domain.Procurement.PurchaseOrderTransitionResult
'
' What CanTransition answers with. Carrying the target state means a caller
' that is allowed to proceed does not re-derive where it lands - re-deriving
' is the second decision point this whole card exists to prevent.
'
' A refusal carries no target state at all (To is Nothing), so a caller that
' forgets to check IsAllowed cannot read a plausible-looking status out of a
' refusal. Under Option Strict On, reading .Value on that Nothing throws
' rather than defaulting to Draft.

Namespace Procurement

    ''' <summary>
    ''' The outcome of a single transition decision: either allowed with a
    ''' target status, or refused with a stable error code. Never both, never
    ''' neither.
    ''' </summary>
    Public NotInheritable Class PurchaseOrderTransitionResult

        Private Sub New(isAllowedValue As Boolean,
                        toValue As PurchaseOrderStatus?,
                        errorCodeValue As String)

            _isAllowed = isAllowedValue
            _to = toValue
            _errorCode = errorCodeValue

        End Sub

        Private ReadOnly _isAllowed As Boolean
        Private ReadOnly _to As PurchaseOrderStatus?
        Private ReadOnly _errorCode As String

        ''' <summary>True when the transition is legal.</summary>
        Public ReadOnly Property IsAllowed As Boolean
            Get
                Return _isAllowed
            End Get
        End Property

        ''' <summary>The status the order moves to. Nothing when the transition was refused.</summary>
        Public ReadOnly Property [To] As PurchaseOrderStatus?
            Get
                Return _to
            End Get
        End Property

        ''' <summary>
        ''' The stable error code (PurchaseOrderTransitionErrors) explaining
        ''' the refusal. Empty when the transition was allowed.
        ''' </summary>
        Public ReadOnly Property ErrorCode As String
            Get
                Return _errorCode
            End Get
        End Property

        ''' <summary>Builds an allowed result landing on <paramref name="target"/>.</summary>
        Public Shared Function Allowed(target As PurchaseOrderStatus) As PurchaseOrderTransitionResult
            Return New PurchaseOrderTransitionResult(True, target, String.Empty)
        End Function

        ''' <summary>Builds a refusal naming <paramref name="errorCode"/>.</summary>
        Public Shared Function Refused(errorCode As String) As PurchaseOrderTransitionResult

            If String.IsNullOrWhiteSpace(errorCode) Then
                Throw New ArgumentException(
                    "A refused transition must name a stable error code (ADR-014).",
                    NameOf(errorCode))
            End If

            Return New PurchaseOrderTransitionResult(False, Nothing, errorCode)

        End Function

    End Class

End Namespace
