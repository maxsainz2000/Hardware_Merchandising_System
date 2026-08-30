' Merchandising.Api.Sales.SalesReturnOutcome
'
' What SalesReturnService's three commands (Record/ApproveExceptional/
' RejectExceptional) can report - the same "outcome object, not exception"
' shape AdjustmentOutcome/PurchaseReturnOutcome already use, for the
' identical reason: the service stays free of ASP.NET Core's HTTP types,
' and SalesReturnsController owns the mapping from an outcome to a status
' code.
'
' ONE Kind ENUM FOR ALL THREE COMMANDS, not three - the AdjustmentOutcome
' precedent. Replayed/SaleNotFound/LineNotFound/OverReturned are reachable
' only from RecordAsync (ApproveExceptional/RejectExceptional carry no
' idempotency key - a status transition is safe to retry without one).
' NotFound/NotPending are reachable only from ApproveExceptional/
' RejectExceptional (a fresh Record never has a prior state to conflict
' with).

Imports Merchandising.Contracts.Sales

Namespace Sales

    ''' <summary>Which of SalesReturnService's outcomes occurred.</summary>
    Public Enum SalesReturnOutcomeKind

        ''' <summary>A new return was recorded (completed or escalated), or a pending return was approved/rejected.</summary>
        Created

        ''' <summary>The idempotency key was already used; <see cref="SalesReturnOutcome.ReplayPayload"/> is the original committed response. Record only.</summary>
        Replayed

        ''' <summary>No sale with the requested Id exists. Record only.</summary>
        SaleNotFound

        ''' <summary>A line references a SaleLineId that does not exist, or that belongs to a different sale. Record only.</summary>
        LineNotFound

        ''' <summary>Prior (non-Rejected) returns plus this one would exceed the sale line's Quantity. Record only.</summary>
        OverReturned

        ''' <summary>No sales return with the requested Id exists. ApproveExceptional/RejectExceptional only.</summary>
        NotFound

        ''' <summary>The return is not PendingApproval - it cannot be approved or rejected again. ApproveExceptional/RejectExceptional only.</summary>
        NotPending

    End Enum

    ''' <summary>The result of Record/ApproveExceptional/RejectExceptional.</summary>
    Public NotInheritable Class SalesReturnOutcome

        ''' <summary>P5-11's stable error code (ADR-014) for an OverReturned outcome.</summary>
        Public Const OverReturnedErrorCode As String = "SALES_RETURN_QUANTITY_EXCEEDS_AVAILABLE"

        ''' <summary>P5-11's stable error code (ADR-014) for a NotPending outcome.</summary>
        Public Const NotPendingErrorCode As String = "SALES_RETURN_NOT_PENDING"

        Public ReadOnly Property Kind As SalesReturnOutcomeKind

        ''' <summary>The committed/transitioned return. Set only when <see cref="Kind"/> is <see cref="SalesReturnOutcomeKind.Created"/>.</summary>
        Public ReadOnly Property Response As SalesReturnResponse

        ''' <summary>The original committed response, verbatim JSON. Set only when <see cref="Kind"/> is <see cref="SalesReturnOutcomeKind.Replayed"/>.</summary>
        Public ReadOnly Property ReplayPayload As String

        ''' <summary>The SaleLineId that caused a <c>LineNotFound</c> or <c>OverReturned</c> outcome. 0 otherwise.</summary>
        Public ReadOnly Property OffendingSaleLineId As Integer

        Private Sub New(
            kind As SalesReturnOutcomeKind, response As SalesReturnResponse, replayPayload As String, offendingSaleLineId As Integer)

            _Kind = kind
            _Response = response
            _ReplayPayload = replayPayload
            _OffendingSaleLineId = offendingSaleLineId

        End Sub

        Public Shared Function Created(response As SalesReturnResponse) As SalesReturnOutcome
            Return New SalesReturnOutcome(SalesReturnOutcomeKind.Created, response, Nothing, 0)
        End Function

        Public Shared Function Replayed(replayPayload As String) As SalesReturnOutcome
            Return New SalesReturnOutcome(SalesReturnOutcomeKind.Replayed, Nothing, replayPayload, 0)
        End Function

        Public Shared Function SaleNotFound() As SalesReturnOutcome
            Return New SalesReturnOutcome(SalesReturnOutcomeKind.SaleNotFound, Nothing, Nothing, 0)
        End Function

        Public Shared Function LineNotFound(saleLineId As Integer) As SalesReturnOutcome
            Return New SalesReturnOutcome(SalesReturnOutcomeKind.LineNotFound, Nothing, Nothing, saleLineId)
        End Function

        Public Shared Function OverReturned(saleLineId As Integer) As SalesReturnOutcome
            Return New SalesReturnOutcome(SalesReturnOutcomeKind.OverReturned, Nothing, Nothing, saleLineId)
        End Function

        Public Shared Function NotFound() As SalesReturnOutcome
            Return New SalesReturnOutcome(SalesReturnOutcomeKind.NotFound, Nothing, Nothing, 0)
        End Function

        Public Shared Function NotPending() As SalesReturnOutcome
            Return New SalesReturnOutcome(SalesReturnOutcomeKind.NotPending, Nothing, Nothing, 0)
        End Function

    End Class

End Namespace
