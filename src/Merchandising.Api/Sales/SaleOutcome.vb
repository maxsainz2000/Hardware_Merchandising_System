' Merchandising.Api.Sales.SaleOutcome
'
' What SaleService.CompleteAsync can report - the same "outcome object, not
' exception" shape ReceivingOutcome/CashierSessionOutcome already use, for
' the identical reason: the service stays free of ASP.NET Core's HTTP types,
' and SalesController owns the mapping from an outcome to a status code.
'
' FIVE REFUSAL KINDS, EACH ITS OWN STABLE ERROR CODE - spec section 10.3's
' "re-checks product activity, current price, available stock, duplicate
' request state, and payment validity" (P5-07's own Done-when box 1), plus
' the foundational "a sale requires an open cashier session" precondition
' (spec section 10.3's opening sentence) that comes before any of the five:
'
'   NoOpenSession          - the calling user holds no Open CashierSession.
'   ProductNotFound        - a line names a ProductId that does not exist.
'                            A bad REQUEST reference (400), not a state
'                            conflict - the same distinction
'                            ReceivingController's own header draws between
'                            LineNotFound (400) and a status-conflict (409).
'   ProductInactive        - the product exists but is deactivated. The
'                            product reference is well-formed; its CURRENT
'                            STATE forbids the sale (409), the same reasoning
'                            TransitionRefusalMessage's own comment gives for
'                            every other 409 in this codebase.
'   InsufficientStock      - StockRepository.TryDecrementAsync's conditional
'                            UPDATE affected zero rows (409, INSUFFICIENT_STOCK
'                            - InventoryController's existing code, reused
'                            rather than inventing a second one for the same
'                            fact).
'   CashTenderInsufficient - tendered less than the sale total. Carries
'                            ShortfallAmount so the controller's message can
'                            state it, the same shape
'                            Merchandising.Domain.Sales.CashTenderResult
'                            already carries in Domain.
'
' Replayed carries the response NOT ONLY when a new sale was rejected -
' Created/Replayed are ADR-007's usual pair, identical to every other
' claim-first command in this codebase.
'
' IdempotencyKeyReused (P5-09 / ADR-007.1) IS A SIXTH, DISTINCT FROM Replayed.
' A losing idempotency claim whose freshly-computed request hash does not
' match the winning claim's stored hash names a real client bug - the same
' key reused for a DIFFERENT command - and must never be silently replayed
' as if it were a legitimate retry (spec section 11's "return the original
' committed result" only ever meant the same command sent twice).

Imports Merchandising.Contracts.Sales

Namespace Sales

    ''' <summary>Which of <see cref="SaleService.CompleteAsync"/>'s outcomes occurred.</summary>
    Public Enum SaleOutcomeKind

        ''' <summary>A new sale was committed - all seven effects, together.</summary>
        Created

        ''' <summary>The idempotency key was already used; <see cref="SaleOutcome.ReplayPayload"/> is the original committed response.</summary>
        Replayed

        ''' <summary>The calling user holds no Open CashierSession.</summary>
        NoOpenSession

        ''' <summary>A line names a ProductId that does not exist.</summary>
        ProductNotFound

        ''' <summary>A line names a product that exists but is Inactive.</summary>
        ProductInactive

        ''' <summary>A line's conditional stock decrement could not be satisfied - not enough available.</summary>
        InsufficientStock

        ''' <summary>Cash tendered was less than the sale total.</summary>
        CashTenderInsufficient

        ''' <summary>The idempotency key was already used, by a DIFFERENT request body - refused, never replayed.</summary>
        IdempotencyKeyReused

    End Enum

    ''' <summary>The result of attempting to complete a sale.</summary>
    Public NotInheritable Class SaleOutcome

        ''' <summary>P5-07's stable error code (ADR-014) for a NoOpenSession outcome.</summary>
        Public Const NoOpenSessionErrorCode As String = "CASHIER_SESSION_REQUIRED"

        ''' <summary>P5-07's stable error code (ADR-014) for a ProductInactive outcome.</summary>
        Public Const ProductInactiveErrorCode As String = "PRODUCT_INACTIVE"

        ''' <summary>Reused from InventoryController's existing code (ADR-014) - the same fact, the same wording, wherever a conditional stock decrement fails.</summary>
        Public Const InsufficientStockErrorCode As String = "INSUFFICIENT_STOCK"

        ''' <summary>P5-07's stable error code (ADR-014) for a CashTenderInsufficient outcome - deliberately distinct from the generic VALIDATION_FAILED bucket, so a caller can branch on "not enough cash" specifically.</summary>
        Public Const CashTenderInsufficientErrorCode As String = "CASH_TENDER_INSUFFICIENT"

        ''' <summary>P5-09's stable error code (ADR-014/ADR-007.1) for an IdempotencyKeyReused outcome.</summary>
        Public Const IdempotencyKeyReusedErrorCode As String = "IDEMPOTENCY_KEY_REUSED"

        Public ReadOnly Property Kind As SaleOutcomeKind

        ''' <summary>The committed sale. Set only when <see cref="Kind"/> is <see cref="SaleOutcomeKind.Created"/>.</summary>
        Public ReadOnly Property Response As SaleResponse

        ''' <summary>The original committed response, verbatim JSON. Set only when <see cref="Kind"/> is <see cref="SaleOutcomeKind.Replayed"/>.</summary>
        Public ReadOnly Property ReplayPayload As String

        ''' <summary>The ProductId that caused a <c>ProductNotFound</c>, <c>ProductInactive</c>, or <c>InsufficientStock</c> outcome. 0 otherwise.</summary>
        Public ReadOnly Property OffendingProductId As Integer

        ''' <summary>Set only when <see cref="Kind"/> is <see cref="SaleOutcomeKind.CashTenderInsufficient"/> - how much more was needed.</summary>
        Public ReadOnly Property ShortfallAmount As Decimal

        Private Sub New(
            kind As SaleOutcomeKind,
            response As SaleResponse,
            replayPayload As String,
            offendingProductId As Integer,
            shortfallAmount As Decimal)

            _Kind = kind
            _Response = response
            _ReplayPayload = replayPayload
            _OffendingProductId = offendingProductId
            _ShortfallAmount = shortfallAmount

        End Sub

        Public Shared Function Created(response As SaleResponse) As SaleOutcome
            Return New SaleOutcome(SaleOutcomeKind.Created, response, Nothing, 0, 0D)
        End Function

        Public Shared Function Replayed(replayPayload As String) As SaleOutcome
            Return New SaleOutcome(SaleOutcomeKind.Replayed, Nothing, replayPayload, 0, 0D)
        End Function

        Public Shared Function NoOpenSession() As SaleOutcome
            Return New SaleOutcome(SaleOutcomeKind.NoOpenSession, Nothing, Nothing, 0, 0D)
        End Function

        Public Shared Function ProductNotFound(productId As Integer) As SaleOutcome
            Return New SaleOutcome(SaleOutcomeKind.ProductNotFound, Nothing, Nothing, productId, 0D)
        End Function

        Public Shared Function ProductInactive(productId As Integer) As SaleOutcome
            Return New SaleOutcome(SaleOutcomeKind.ProductInactive, Nothing, Nothing, productId, 0D)
        End Function

        Public Shared Function InsufficientStock(productId As Integer) As SaleOutcome
            Return New SaleOutcome(SaleOutcomeKind.InsufficientStock, Nothing, Nothing, productId, 0D)
        End Function

        Public Shared Function CashTenderInsufficient(shortfallAmount As Decimal) As SaleOutcome
            Return New SaleOutcome(SaleOutcomeKind.CashTenderInsufficient, Nothing, Nothing, 0, shortfallAmount)
        End Function

        Public Shared Function IdempotencyKeyReused() As SaleOutcome
            Return New SaleOutcome(SaleOutcomeKind.IdempotencyKeyReused, Nothing, Nothing, 0, 0D)
        End Function

    End Class

End Namespace
