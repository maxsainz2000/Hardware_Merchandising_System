' Merchandising.Api.Inventory.StockCountOutcome
'
' What StockCountService's three commands (Open/RecordLine/Close) can
' report - the same "outcome object, not exception" shape ReceivingOutcome
' and PurchaseReturnOutcome already use, for the identical reason: the
' service stays free of ASP.NET Core's HTTP types, and StockCountsController
' owns the mapping from an outcome to a status code.
'
' ONE Kind ENUM FOR ALL THREE COMMANDS, not three. Open never fails once its
' idempotency key is claimed (there is nothing to look up first - contrast
' RecordLine/Close, which both need a session to already exist), so it only
' ever produces Created or Replayed; the controller action for Open never
' reaches the other cases. This mirrors the schema's own choice to model one
' StockCountStatus enum across a session's whole lifecycle rather than a
' type per transition.

Imports Merchandising.Contracts.Inventory

Namespace Inventory

    ''' <summary>Which of StockCountService's outcomes occurred.</summary>
    Public Enum StockCountOutcomeKind

        ''' <summary>A new session was opened, or a new line/close was committed.</summary>
        Created

        ''' <summary>The idempotency key was already used; <see cref="StockCountOutcome.ReplayPayload"/> is the original committed response.</summary>
        Replayed

        ''' <summary>No stock-count session with the requested Id exists.</summary>
        NotFound

        ''' <summary>The session is not Open - a line cannot be recorded, or the session cannot be closed again.</summary>
        NotOpen

        ''' <summary>The line names a ProductId that does not exist.</summary>
        ProductNotFound

    End Enum

    ''' <summary>The result of Open/RecordLine/Close.</summary>
    Public NotInheritable Class StockCountOutcome

        ''' <summary>P4-09's stable error code (ADR-014) for a NotOpen outcome.</summary>
        Public Const NotOpenErrorCode As String = "STOCK_COUNT_NOT_OPEN"

        Public ReadOnly Property Kind As StockCountOutcomeKind

        ''' <summary>The committed session. Set only when <see cref="Kind"/> is <see cref="StockCountOutcomeKind.Created"/> for Open or Close.</summary>
        Public ReadOnly Property Session As StockCountResponse

        ''' <summary>The committed line. Set only when <see cref="Kind"/> is <see cref="StockCountOutcomeKind.Created"/> for RecordLine.</summary>
        Public ReadOnly Property Line As StockCountLineResponse

        ''' <summary>The original committed response, verbatim JSON. Set only when <see cref="Kind"/> is <see cref="StockCountOutcomeKind.Replayed"/>.</summary>
        Public ReadOnly Property ReplayPayload As String

        Private Sub New(
            kind As StockCountOutcomeKind,
            session As StockCountResponse,
            line As StockCountLineResponse,
            replayPayload As String)

            _Kind = kind
            _Session = session
            _Line = line
            _ReplayPayload = replayPayload

        End Sub

        Public Shared Function CreatedSession(session As StockCountResponse) As StockCountOutcome
            Return New StockCountOutcome(StockCountOutcomeKind.Created, session, Nothing, Nothing)
        End Function

        Public Shared Function CreatedLine(line As StockCountLineResponse) As StockCountOutcome
            Return New StockCountOutcome(StockCountOutcomeKind.Created, Nothing, line, Nothing)
        End Function

        Public Shared Function Replayed(replayPayload As String) As StockCountOutcome
            Return New StockCountOutcome(StockCountOutcomeKind.Replayed, Nothing, Nothing, replayPayload)
        End Function

        Public Shared Function NotFound() As StockCountOutcome
            Return New StockCountOutcome(StockCountOutcomeKind.NotFound, Nothing, Nothing, Nothing)
        End Function

        Public Shared Function NotOpen() As StockCountOutcome
            Return New StockCountOutcome(StockCountOutcomeKind.NotOpen, Nothing, Nothing, Nothing)
        End Function

        Public Shared Function ProductNotFound() As StockCountOutcome
            Return New StockCountOutcome(StockCountOutcomeKind.ProductNotFound, Nothing, Nothing, Nothing)
        End Function

    End Class

End Namespace
