' Merchandising.Api.Receiving.PurchaseReturnOutcome
'
' What PurchaseReturnService.RecordAsync can report - the same "outcome
' object, not exception" shape ReceivingOutcome already uses, for the
' identical reason: the service stays free of ASP.NET Core's HTTP types,
' and ReceivingController owns the mapping from an outcome to a status code.
'
' OverReturned carries this card's own stable error code (P4-08) - spec
' section 10.1's "the API rejects a return quantity that exceeds the
' received quantity less prior returns", enforced at the API because no
' single-row CHECK constraint can see an aggregate over prior sibling rows
' (PurchaseReturnRepository's header explains why). InsufficientStock reuses
' the SAME "INSUFFICIENT_STOCK" code InventoryController already uses for
' the P1-11 stock decrement - it is the identical failure mode (a
' conditional UPDATE affected zero rows), just reached from a different
' command: the received stock was already consumed by something else (a
' sale, a prior adjustment) between receiving and this return attempt.

Imports Merchandising.Contracts.Receiving

Namespace Receiving

    ''' <summary>Which of <see cref="PurchaseReturnService.RecordAsync"/>'s outcomes occurred.</summary>
    Public Enum PurchaseReturnOutcomeKind

        ''' <summary>A new purchase return was committed, already Approved.</summary>
        Created

        ''' <summary>The idempotency key was already used; <see cref="PurchaseReturnOutcome.ReplayPayload"/> is the original committed response.</summary>
        Replayed

        ''' <summary>No receipt with the requested Id exists.</summary>
        ReceiptNotFound

        ''' <summary>A line references a ReceiptLineId that does not exist, or that belongs to a different receipt.</summary>
        LineNotFound

        ''' <summary>The return's ReferenceNumber is already used by another purchase return (UQ_PurchaseReturns_ReferenceNumber).</summary>
        DuplicateReferenceNumber

        ''' <summary>Prior returns plus this one would exceed the receipt line's QuantityReceived.</summary>
        OverReturned

        ''' <summary>RemovesStock was requested but StockBalances no longer holds enough of this product to remove - it was consumed by something else since receiving.</summary>
        InsufficientStock

    End Enum

    ''' <summary>The result of attempting to record a purchase return against a receipt.</summary>
    Public NotInheritable Class PurchaseReturnOutcome

        ''' <summary>P4-08's stable error code (ADR-014) for an OverReturned outcome.</summary>
        Public Const OverReturnedErrorCode As String = "PURCHASE_RETURN_QUANTITY_EXCEEDS_AVAILABLE"

        ''' <summary>The same stable code InventoryController already uses for a P1-11 insufficient-stock refusal (this file's header explains why it is reused rather than invented anew).</summary>
        Public Const InsufficientStockErrorCode As String = "INSUFFICIENT_STOCK"

        Public ReadOnly Property Kind As PurchaseReturnOutcomeKind

        ''' <summary>The committed return. Set only when <see cref="Kind"/> is <see cref="PurchaseReturnOutcomeKind.Created"/>.</summary>
        Public ReadOnly Property Response As PurchaseReturnResponse

        ''' <summary>The original committed response, verbatim JSON. Set only when <see cref="Kind"/> is <see cref="PurchaseReturnOutcomeKind.Replayed"/>.</summary>
        Public ReadOnly Property ReplayPayload As String

        ''' <summary>The ReceiptLineId that caused a <c>LineNotFound</c>, <c>OverReturned</c> or <c>InsufficientStock</c> outcome. 0 otherwise.</summary>
        Public ReadOnly Property OffendingReceiptLineId As Integer

        ''' <summary>The stable error code - <see cref="OverReturnedErrorCode"/> or <see cref="InsufficientStockErrorCode"/>. Nothing otherwise.</summary>
        Public ReadOnly Property ErrorCode As String

        Private Sub New(
            kind As PurchaseReturnOutcomeKind,
            response As PurchaseReturnResponse,
            replayPayload As String,
            offendingReceiptLineId As Integer,
            errorCode As String)

            _Kind = kind
            _Response = response
            _ReplayPayload = replayPayload
            _OffendingReceiptLineId = offendingReceiptLineId
            _ErrorCode = errorCode

        End Sub

        Public Shared Function Created(response As PurchaseReturnResponse) As PurchaseReturnOutcome
            Return New PurchaseReturnOutcome(PurchaseReturnOutcomeKind.Created, response, Nothing, 0, Nothing)
        End Function

        Public Shared Function Replayed(replayPayload As String) As PurchaseReturnOutcome
            Return New PurchaseReturnOutcome(PurchaseReturnOutcomeKind.Replayed, Nothing, replayPayload, 0, Nothing)
        End Function

        Public Shared Function ReceiptNotFound() As PurchaseReturnOutcome
            Return New PurchaseReturnOutcome(PurchaseReturnOutcomeKind.ReceiptNotFound, Nothing, Nothing, 0, Nothing)
        End Function

        Public Shared Function LineNotFound(receiptLineId As Integer) As PurchaseReturnOutcome
            Return New PurchaseReturnOutcome(PurchaseReturnOutcomeKind.LineNotFound, Nothing, Nothing, receiptLineId, Nothing)
        End Function

        Public Shared Function DuplicateReferenceNumber() As PurchaseReturnOutcome
            Return New PurchaseReturnOutcome(PurchaseReturnOutcomeKind.DuplicateReferenceNumber, Nothing, Nothing, 0, Nothing)
        End Function

        Public Shared Function OverReturned(receiptLineId As Integer) As PurchaseReturnOutcome
            Return New PurchaseReturnOutcome(PurchaseReturnOutcomeKind.OverReturned, Nothing, Nothing, receiptLineId, OverReturnedErrorCode)
        End Function

        Public Shared Function InsufficientStock(receiptLineId As Integer) As PurchaseReturnOutcome
            Return New PurchaseReturnOutcome(PurchaseReturnOutcomeKind.InsufficientStock, Nothing, Nothing, receiptLineId, InsufficientStockErrorCode)
        End Function

    End Class

End Namespace
