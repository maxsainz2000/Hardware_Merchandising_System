' Merchandising.Domain.Procurement.PurchaseOrderTransitionErrors
'
' The stable error codes a refused transition names. ADR-014: every error
' response carries a stable errorCode, and a caller keys off that code rather
' than off the message. So a refusal here returns a code, never a bare False -
' the controller has nothing left to invent, and P3-08's API specification has
' a finite list to document.
'
' SCREAMING_SNAKE, matching every code already live in the API
' (PRODUCT_NOT_FOUND, SUPPLIER_ALREADY_INACTIVE, INSUFFICIENT_STOCK).
'
' Three of the four exist because spec section 10.1 states the rule in words
' and a caller has to be able to tell the three cases apart:
'   - a cancelled order is dead and no action will ever work again
'   - a closed order is likewise terminal
'   - over-receiving is explicitly anticipated to gain an override policy
'     later ("unless an authorized override policy is later approved"), so it
'     cannot share a code with a generic bad transition or that policy would
'     have nothing to key on
' The fourth is everything else.

Namespace Procurement

    ''' <summary>
    ''' Stable error codes for a refused purchase-order transition (ADR-014).
    ''' </summary>
    Public NotInheritable Class PurchaseOrderTransitionErrors

        ''' <summary>The order is cancelled. Spec section 10.1: a cancelled order cannot proceed.</summary>
        Public Const Cancelled As String = "PURCHASE_ORDER_CANCELLED"

        ''' <summary>The order is closed. Terminal; no further action is accepted.</summary>
        Public Const Closed As String = "PURCHASE_ORDER_CLOSED"

        ''' <summary>
        ''' The order is fully received and further receiving was attempted.
        ''' Spec section 10.1's over-receiving rule; the MVP default is reject.
        ''' </summary>
        Public Const FullyReceived As String = "PURCHASE_ORDER_FULLY_RECEIVED"

        ''' <summary>The action is not legal from the order's current status.</summary>
        Public Const InvalidTransition As String = "PURCHASE_ORDER_INVALID_TRANSITION"

        Private Sub New()
        End Sub

    End Class

End Namespace
