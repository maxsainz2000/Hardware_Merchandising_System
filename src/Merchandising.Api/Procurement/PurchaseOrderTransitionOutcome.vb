' Merchandising.Api.Procurement.PurchaseOrderTransitionOutcome
'
' P3-04: what PurchaseOrderService.SubmitAsync/ApproveAsync can report -
' same "outcome object, not exception" shape as PurchaseOrderCreationOutcome
' (that file's header explains why: the service stays free of ASP.NET Core's
' HTTP types, and PurchaseOrdersController owns the outcome-to-status-code
' mapping).
'
' Refused carries the stable error code PurchaseOrderTransitions.CanTransition
' named (ADR-020/ADR-014) - the controller has nothing left to invent.

Imports Merchandising.Contracts.Procurement

Namespace Procurement

    ''' <summary>Which of a purchase-order status transition's outcomes occurred.</summary>
    Public Enum PurchaseOrderTransitionOutcomeKind

        ''' <summary>The transition was applied and committed.</summary>
        Success

        ''' <summary>No order with the requested Id exists.</summary>
        NotFound

        ''' <summary>The transition table (or the self-approval veto) refused the attempt.</summary>
        Refused

    End Enum

    ''' <summary>The result of attempting a purchase-order status transition.</summary>
    Public NotInheritable Class PurchaseOrderTransitionOutcome

        Public ReadOnly Property Kind As PurchaseOrderTransitionOutcomeKind

        ''' <summary>The order after the transition. Set only when <see cref="Kind"/> is <see cref="PurchaseOrderTransitionOutcomeKind.Success"/>.</summary>
        Public ReadOnly Property Response As PurchaseOrderResponse

        ''' <summary>The stable error code (PurchaseOrderTransitionErrors). Set only when <see cref="Kind"/> is <see cref="PurchaseOrderTransitionOutcomeKind.Refused"/>.</summary>
        Public ReadOnly Property ErrorCode As String

        Private Sub New(kind As PurchaseOrderTransitionOutcomeKind, response As PurchaseOrderResponse, errorCode As String)
            _Kind = kind
            _Response = response
            _ErrorCode = errorCode
        End Sub

        Public Shared Function Success(response As PurchaseOrderResponse) As PurchaseOrderTransitionOutcome
            Return New PurchaseOrderTransitionOutcome(PurchaseOrderTransitionOutcomeKind.Success, response, Nothing)
        End Function

        Public Shared Function NotFound() As PurchaseOrderTransitionOutcome
            Return New PurchaseOrderTransitionOutcome(PurchaseOrderTransitionOutcomeKind.NotFound, Nothing, Nothing)
        End Function

        Public Shared Function Refused(errorCode As String) As PurchaseOrderTransitionOutcome
            Return New PurchaseOrderTransitionOutcome(PurchaseOrderTransitionOutcomeKind.Refused, Nothing, errorCode)
        End Function

    End Class

End Namespace
