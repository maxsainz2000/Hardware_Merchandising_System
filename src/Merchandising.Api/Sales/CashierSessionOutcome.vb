' Merchandising.Api.Sales.CashierSessionOutcome
'
' What CashierSessionService's two commands (Open/Close) can report - the
' same "outcome object, not exception" shape AdjustmentOutcome/StockCountOutcome
' already use, for the identical reason: the service stays free of ASP.NET
' Core's HTTP types, and CashierSessionsController owns the mapping from an
' outcome to a status code.
'
' AlreadyOpen IS OPEN-ONLY, THE DIRECT TRANSLATION OF THE UNIQUE-INDEX
' VIOLATION (card Done-when box 1). CashierSessionService.OpenAsync catches
' the MySqlException a losing concurrent insert raises and returns this
' instead of letting it propagate - the same "constraint violation becomes a
' controlled outcome" shape IdempotencyStore's own callers already rely on
' for a losing idempotency claim, applied here to a DIFFERENT unique index.

Imports Merchandising.Contracts.Sales

Namespace Sales

    ''' <summary>Which of CashierSessionService's outcomes occurred.</summary>
    Public Enum CashierSessionOutcomeKind

        ''' <summary>A new session was opened, or an existing one was closed.</summary>
        Created

        ''' <summary>The idempotency key was already used; <see cref="CashierSessionOutcome.ReplayPayload"/> is the original committed response.</summary>
        Replayed

        ''' <summary>No session with the requested Id exists. Close only.</summary>
        NotFound

        ''' <summary>The session is not Open - it is already Closed. Close only.</summary>
        NotOpen

        ''' <summary>This cashier already holds an Open session. Open only.</summary>
        AlreadyOpen

    End Enum

    ''' <summary>The result of Open/Close.</summary>
    Public NotInheritable Class CashierSessionOutcome

        ''' <summary>P5-04's stable error code (ADR-014) for a NotOpen outcome.</summary>
        Public Const NotOpenErrorCode As String = "CASHIER_SESSION_NOT_OPEN"

        ''' <summary>P5-04's stable error code (ADR-014) for an AlreadyOpen outcome.</summary>
        Public Const AlreadyOpenErrorCode As String = "CASHIER_SESSION_ALREADY_OPEN"

        Public ReadOnly Property Kind As CashierSessionOutcomeKind

        ''' <summary>The committed session. Set only when <see cref="Kind"/> is <see cref="CashierSessionOutcomeKind.Created"/>.</summary>
        Public ReadOnly Property Session As CashierSessionResponse

        ''' <summary>The original committed response, verbatim JSON. Set only when <see cref="Kind"/> is <see cref="CashierSessionOutcomeKind.Replayed"/>.</summary>
        Public ReadOnly Property ReplayPayload As String

        Private Sub New(kind As CashierSessionOutcomeKind, session As CashierSessionResponse, replayPayload As String)
            _Kind = kind
            _Session = session
            _ReplayPayload = replayPayload
        End Sub

        Public Shared Function Created(session As CashierSessionResponse) As CashierSessionOutcome
            Return New CashierSessionOutcome(CashierSessionOutcomeKind.Created, session, Nothing)
        End Function

        Public Shared Function Replayed(replayPayload As String) As CashierSessionOutcome
            Return New CashierSessionOutcome(CashierSessionOutcomeKind.Replayed, Nothing, replayPayload)
        End Function

        Public Shared Function NotFound() As CashierSessionOutcome
            Return New CashierSessionOutcome(CashierSessionOutcomeKind.NotFound, Nothing, Nothing)
        End Function

        Public Shared Function NotOpen() As CashierSessionOutcome
            Return New CashierSessionOutcome(CashierSessionOutcomeKind.NotOpen, Nothing, Nothing)
        End Function

        Public Shared Function AlreadyOpen() As CashierSessionOutcome
            Return New CashierSessionOutcome(CashierSessionOutcomeKind.AlreadyOpen, Nothing, Nothing)
        End Function

    End Class

End Namespace
