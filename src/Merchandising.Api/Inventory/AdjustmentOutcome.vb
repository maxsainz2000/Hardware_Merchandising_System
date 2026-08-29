' Merchandising.Api.Inventory.AdjustmentOutcome
'
' What AdjustmentService's three commands (Request/Approve/Reject) can
' report - the same "outcome object, not exception" shape StockCountOutcome,
' ReceivingOutcome and PurchaseReturnOutcome already use, for the identical
' reason: the service stays free of ASP.NET Core's HTTP types, and
' AdjustmentsController owns the mapping from an outcome to a status code.
'
' ONE Kind ENUM FOR ALL THREE COMMANDS, not three - the same StockCountOutcome
' precedent. Replayed is reachable only from RequestAsync (Approve/Reject
' carry no idempotency key - a status transition is safe to retry without
' one, the same reasoning PurchaseOrdersController.ApprovePurchaseOrder's
' header gives). NotPending is reachable only from Approve/Reject (a fresh
' Request never has a prior state to conflict with).

Imports Merchandising.Contracts.Inventory

Namespace Inventory

    ''' <summary>Which of AdjustmentService's outcomes occurred.</summary>
    Public Enum AdjustmentOutcomeKind

        ''' <summary>A new adjustment was requested, approved (and applied), or rejected.</summary>
        Created

        ''' <summary>The idempotency key was already used; <see cref="AdjustmentOutcome.ReplayPayload"/> is the original committed response. Request only.</summary>
        Replayed

        ''' <summary>No adjustment with the requested Id exists.</summary>
        NotFound

        ''' <summary>The adjustment is not Pending - it cannot be approved or rejected again.</summary>
        NotPending

        ''' <summary>Applying a negative variance would take StockBalances below zero - the stock was already consumed by something else since this adjustment was requested (or, for Approve, since it was created).</summary>
        InsufficientStock

        ''' <summary>Request only: the line names a ProductId that does not exist.</summary>
        ProductNotFound

    End Enum

    ''' <summary>The result of Request/Approve/Reject.</summary>
    Public NotInheritable Class AdjustmentOutcome

        ''' <summary>P4-10's stable error code (ADR-014) for a NotPending outcome.</summary>
        Public Const NotPendingErrorCode As String = "ADJUSTMENT_NOT_PENDING"

        ''' <summary>The same stable code InventoryController/PurchaseReturnOutcome already use for an insufficient-stock refusal.</summary>
        Public Const InsufficientStockErrorCode As String = "INSUFFICIENT_STOCK"

        Public ReadOnly Property Kind As AdjustmentOutcomeKind

        ''' <summary>The committed adjustment. Set only when <see cref="Kind"/> is <see cref="AdjustmentOutcomeKind.Created"/>.</summary>
        Public ReadOnly Property Response As AdjustmentResponse

        ''' <summary>The original committed response, verbatim JSON. Set only when <see cref="Kind"/> is <see cref="AdjustmentOutcomeKind.Replayed"/>.</summary>
        Public ReadOnly Property ReplayPayload As String

        Private Sub New(kind As AdjustmentOutcomeKind, response As AdjustmentResponse, replayPayload As String)
            _Kind = kind
            _Response = response
            _ReplayPayload = replayPayload
        End Sub

        Public Shared Function Created(response As AdjustmentResponse) As AdjustmentOutcome
            Return New AdjustmentOutcome(AdjustmentOutcomeKind.Created, response, Nothing)
        End Function

        Public Shared Function Replayed(replayPayload As String) As AdjustmentOutcome
            Return New AdjustmentOutcome(AdjustmentOutcomeKind.Replayed, Nothing, replayPayload)
        End Function

        Public Shared Function NotFound() As AdjustmentOutcome
            Return New AdjustmentOutcome(AdjustmentOutcomeKind.NotFound, Nothing, Nothing)
        End Function

        Public Shared Function NotPending() As AdjustmentOutcome
            Return New AdjustmentOutcome(AdjustmentOutcomeKind.NotPending, Nothing, Nothing)
        End Function

        Public Shared Function InsufficientStock() As AdjustmentOutcome
            Return New AdjustmentOutcome(AdjustmentOutcomeKind.InsufficientStock, Nothing, Nothing)
        End Function

        Public Shared Function ProductNotFound() As AdjustmentOutcome
            Return New AdjustmentOutcome(AdjustmentOutcomeKind.ProductNotFound, Nothing, Nothing)
        End Function

    End Class

End Namespace
