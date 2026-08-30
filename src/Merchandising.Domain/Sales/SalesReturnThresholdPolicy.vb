' Merchandising.Domain.Sales.SalesReturnThresholdPolicy
'
' P5-11 / spec section 9: "policies for sensitive operations such as
' approvals, returns above a threshold". Confirmed with the user at P5-11:
' the threshold is measured against the return's total REFUND VALUE
' (money), not the returned quantity - the schema (0012_sales-returns.sql)
' already defers RefundAmount until the return actually completes, which
' only makes sense if the amount is what decides whether it completes
' directly or waits for SalesReturns.ApproveExceptional. This mirrors
' Merchandising.Domain.Inventory.AdjustmentThresholdPolicy's exact shape,
' kept as its own type rather than reused across modules because the two
' compare different quantities for different reasons - a sales return has
' no sign to take the absolute value of (RefundAmount is always >= 0,
' CK_SalesReturns_RefundAmount).
'
' Pure function, no ASP.NET Core or database dependency - Domain depends on
' nothing (CLAUDE.md section 4).

Namespace Sales

    Public NotInheritable Class SalesReturnThresholdPolicy

        Private Sub New()
        End Sub

        ''' <summary>
        ''' True when <paramref name="refundAmount"/> is at or above
        ''' <paramref name="threshold"/> - the return requires
        ''' SalesReturns.ApproveExceptional rather than completing directly.
        ''' </summary>
        Public Shared Function ExceedsThreshold(refundAmount As Decimal, threshold As Decimal) As Boolean
            Return refundAmount >= threshold
        End Function

    End Class

End Namespace
