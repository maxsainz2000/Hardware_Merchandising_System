' Merchandising.Domain.Sales.CashTender / CashTenderResult
'
' P5-01, spec section 10.3: "the API verifies that the tendered amount is
' at least the sale total and calculates change using fixed-precision
' decimal arithmetic." Tendered < total is a refusal, not a negative
' change - CashTenderResult has no way to represent a negative Change at
' all, so a caller cannot accidentally read one.
'
' Change is tendered - total with no rounding step: both operands are
' already validated at money scale (DECIMAL(19,4)) before the subtraction,
' and the difference of two 4-decimal-place values is exact at 4 decimal
' places, so nothing here can introduce drift.

Namespace Sales

    ''' <summary>The outcome of checking a cash tender against a sale total. Never carries a negative Change - a shortfall is IsAccepted = False with ShortfallAmount set instead.</summary>
    Public NotInheritable Class CashTenderResult

        Public ReadOnly Property IsAccepted As Boolean
        Public ReadOnly Property Change As Decimal
        Public ReadOnly Property ShortfallAmount As Decimal

        ''' <summary>
        ''' Parameters are deliberately not the lowercase form of their
        ''' property (VB is case-insensitive, so e.g. "Change = change"
        ''' would resolve both sides to the same local and silently no-op).
        ''' </summary>
        Private Sub New(accepted As Boolean, changeAmount As Decimal, shortfall As Decimal)
            IsAccepted = accepted
            Change = changeAmount
            ShortfallAmount = shortfall
        End Sub

        Friend Shared Function Accepted(changeAmount As Decimal) As CashTenderResult
            Return New CashTenderResult(True, changeAmount, 0D)
        End Function

        Friend Shared Function Refused(shortfallAmount As Decimal) As CashTenderResult
            Return New CashTenderResult(False, 0D, shortfallAmount)
        End Function

    End Class

    Public NotInheritable Class CashTender

        Private Sub New()
        End Sub

        ''' <summary>Refuses tendered &lt; total rather than returning a negative Change. Both inputs must already be at money scale (ADR-004.1).</summary>
        Public Shared Function ComputeChange(tendered As Decimal, total As Decimal) As CashTenderResult

            DecimalScaleGuard.EnsureMoneyScale(tendered)
            DecimalScaleGuard.EnsureMoneyScale(total)

            If tendered < total Then
                Return CashTenderResult.Refused(total - tendered)
            End If

            Return CashTenderResult.Accepted(tendered - total)

        End Function

    End Class

End Namespace
