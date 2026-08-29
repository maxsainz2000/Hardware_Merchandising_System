' Merchandising.Api.Receiving.ReceivingOutcome
'
' What ReceivingService.ReceiveAsync can report - the same "outcome object,
' not exception" shape PurchaseOrderCreationOutcome and
' PurchaseOrderTransitionOutcome already use, for the identical reason: the
' service stays free of ASP.NET Core's HTTP types, and ReceivingController
' owns the mapping from an outcome to a status code.
'
' Refused carries the stable error code PurchaseOrderTransitions.CanTransition
' named (ADR-020/ADR-014) - the controller has nothing left to invent for that
' case. LineNotFound and DuplicateReferenceNumber are this card's own, because
' neither is a purchase-order transition refusal - one is a bad line
' reference, the other is a reference-number collision.

Imports Merchandising.Contracts.Receiving

Namespace Receiving

    ''' <summary>Which of <see cref="ReceivingService.ReceiveAsync"/>'s outcomes occurred.</summary>
    Public Enum ReceivingOutcomeKind

        ''' <summary>A new receipt was committed, and the order moved to PartiallyReceived or FullyReceived.</summary>
        Created

        ''' <summary>The idempotency key was already used; <see cref="ReceivingOutcome.ReplayPayload"/> is the original committed response.</summary>
        Replayed

        ''' <summary>No purchase order with the requested Id exists.</summary>
        PurchaseOrderNotFound

        ''' <summary>A line references a PurchaseOrderLineId that does not exist, or that belongs to a different order.</summary>
        LineNotFound

        ''' <summary>The receipt's ReferenceNumber is already used by another receipt (UQ_Receipts_ReferenceNumber).</summary>
        DuplicateReferenceNumber

        ''' <summary>PurchaseOrderTransitions.CanTransition refused the receiving action for the order's current status.</summary>
        Refused

    End Enum

    ''' <summary>The result of attempting to receive goods against a purchase order.</summary>
    Public NotInheritable Class ReceivingOutcome

        Public ReadOnly Property Kind As ReceivingOutcomeKind

        ''' <summary>The committed receipt. Set only when <see cref="Kind"/> is <see cref="ReceivingOutcomeKind.Created"/>.</summary>
        Public ReadOnly Property Response As ReceiptResponse

        ''' <summary>The original committed response, verbatim JSON. Set only when <see cref="Kind"/> is <see cref="ReceivingOutcomeKind.Replayed"/>.</summary>
        Public ReadOnly Property ReplayPayload As String

        ''' <summary>The PurchaseOrderLineId that caused a <c>LineNotFound</c> outcome. 0 otherwise.</summary>
        Public ReadOnly Property OffendingPurchaseOrderLineId As Integer

        ''' <summary>The stable error code (PurchaseOrderTransitionErrors). Set only when <see cref="Kind"/> is <see cref="ReceivingOutcomeKind.Refused"/>.</summary>
        Public ReadOnly Property ErrorCode As String

        Private Sub New(
            kind As ReceivingOutcomeKind,
            response As ReceiptResponse,
            replayPayload As String,
            offendingPurchaseOrderLineId As Integer,
            errorCode As String)

            _Kind = kind
            _Response = response
            _ReplayPayload = replayPayload
            _OffendingPurchaseOrderLineId = offendingPurchaseOrderLineId
            _ErrorCode = errorCode

        End Sub

        Public Shared Function Created(response As ReceiptResponse) As ReceivingOutcome
            Return New ReceivingOutcome(ReceivingOutcomeKind.Created, response, Nothing, 0, Nothing)
        End Function

        Public Shared Function Replayed(replayPayload As String) As ReceivingOutcome
            Return New ReceivingOutcome(ReceivingOutcomeKind.Replayed, Nothing, replayPayload, 0, Nothing)
        End Function

        Public Shared Function PurchaseOrderNotFound() As ReceivingOutcome
            Return New ReceivingOutcome(ReceivingOutcomeKind.PurchaseOrderNotFound, Nothing, Nothing, 0, Nothing)
        End Function

        Public Shared Function LineNotFound(purchaseOrderLineId As Integer) As ReceivingOutcome
            Return New ReceivingOutcome(ReceivingOutcomeKind.LineNotFound, Nothing, Nothing, purchaseOrderLineId, Nothing)
        End Function

        Public Shared Function DuplicateReferenceNumber() As ReceivingOutcome
            Return New ReceivingOutcome(ReceivingOutcomeKind.DuplicateReferenceNumber, Nothing, Nothing, 0, Nothing)
        End Function

        Public Shared Function Refused(errorCode As String) As ReceivingOutcome
            Return New ReceivingOutcome(ReceivingOutcomeKind.Refused, Nothing, Nothing, 0, errorCode)
        End Function

    End Class

End Namespace
