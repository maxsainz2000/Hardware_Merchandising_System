' Merchandising.Api.Procurement.PurchaseOrderCreationOutcome
'
' P3-03: what PurchaseOrderService.CreateAsync can report, in the same
' "outcome object, not exception" shape StockDecrementOutcome and
' SupplierLifecycleOutcome already use - the service stays free of ASP.NET
' Core's HTTP types (it has no way to build a 400 body), and
' PurchaseOrdersController owns the mapping from an outcome to a status code.
'
' WHY Replayed IS A SEPARATE KIND FROM Created, AND CARRIES RAW JSON.
' ADR-007 requires a repeated key to return "the original committed result".
' The strongest reading of that is the bytes that were committed, so a replay
' returns the stored payload VERBATIM rather than a re-serialisation of a
' freshly-read order - a row edited by a later card would otherwise make a
' replay quietly disagree with the response the caller originally got. It is
' also a different HTTP status (200, not 201): nothing was created this time.

Imports Merchandising.Contracts.Procurement

Namespace Procurement

    ''' <summary>Which of <see cref="PurchaseOrderService.CreateAsync"/>'s outcomes occurred.</summary>
    Public Enum PurchaseOrderCreationOutcomeKind

        ''' <summary>A new Draft order was committed.</summary>
        Created

        ''' <summary>The idempotency key was already used; <see cref="PurchaseOrderCreationOutcome.ReplayPayload"/> is the original committed response.</summary>
        Replayed

        ''' <summary>No supplier with the requested Id exists.</summary>
        SupplierNotFound

        ''' <summary>The supplier exists but has been deactivated.</summary>
        SupplierInactive

        ''' <summary>A line references a product Id that does not exist.</summary>
        ProductNotFound

        ''' <summary>A line references a product that has been deactivated (spec section 10.2).</summary>
        ProductInactive

        ''' <summary>Every order-number candidate collided. Practically unreachable - see <see cref="PurchaseOrderService"/>.</summary>
        OrderNumberUnavailable

    End Enum

    ''' <summary>
    ''' The result of attempting to create a purchase order.
    ''' </summary>
    Public NotInheritable Class PurchaseOrderCreationOutcome

        Public ReadOnly Property Kind As PurchaseOrderCreationOutcomeKind

        ''' <summary>The committed order. Set only when <see cref="Kind"/> is <see cref="PurchaseOrderCreationOutcomeKind.Created"/>.</summary>
        Public ReadOnly Property Response As PurchaseOrderResponse

        ''' <summary>The original committed response, verbatim JSON. Set only when <see cref="Kind"/> is <see cref="PurchaseOrderCreationOutcomeKind.Replayed"/>.</summary>
        Public ReadOnly Property ReplayPayload As String

        ''' <summary>The product Id that caused a <c>ProductNotFound</c>/<c>ProductInactive</c> outcome; 0 otherwise.</summary>
        Public ReadOnly Property OffendingProductId As Integer

        Private Sub New(
            kind As PurchaseOrderCreationOutcomeKind,
            response As PurchaseOrderResponse,
            replayPayload As String,
            offendingProductId As Integer)

            _Kind = kind
            _Response = response
            _ReplayPayload = replayPayload
            _OffendingProductId = offendingProductId

        End Sub

        Public Shared Function Created(response As PurchaseOrderResponse) As PurchaseOrderCreationOutcome
            Return New PurchaseOrderCreationOutcome(
                PurchaseOrderCreationOutcomeKind.Created, response, Nothing, 0)
        End Function

        Public Shared Function Replayed(replayPayload As String) As PurchaseOrderCreationOutcome
            Return New PurchaseOrderCreationOutcome(
                PurchaseOrderCreationOutcomeKind.Replayed, Nothing, replayPayload, 0)
        End Function

        Public Shared Function SupplierNotFound() As PurchaseOrderCreationOutcome
            Return New PurchaseOrderCreationOutcome(
                PurchaseOrderCreationOutcomeKind.SupplierNotFound, Nothing, Nothing, 0)
        End Function

        Public Shared Function SupplierInactive() As PurchaseOrderCreationOutcome
            Return New PurchaseOrderCreationOutcome(
                PurchaseOrderCreationOutcomeKind.SupplierInactive, Nothing, Nothing, 0)
        End Function

        Public Shared Function ProductNotFound(productId As Integer) As PurchaseOrderCreationOutcome
            Return New PurchaseOrderCreationOutcome(
                PurchaseOrderCreationOutcomeKind.ProductNotFound, Nothing, Nothing, productId)
        End Function

        Public Shared Function ProductInactive(productId As Integer) As PurchaseOrderCreationOutcome
            Return New PurchaseOrderCreationOutcome(
                PurchaseOrderCreationOutcomeKind.ProductInactive, Nothing, Nothing, productId)
        End Function

        Public Shared Function OrderNumberUnavailable() As PurchaseOrderCreationOutcome
            Return New PurchaseOrderCreationOutcome(
                PurchaseOrderCreationOutcomeKind.OrderNumberUnavailable, Nothing, Nothing, 0)
        End Function

    End Class

End Namespace
