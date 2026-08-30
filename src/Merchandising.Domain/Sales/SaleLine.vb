' Merchandising.Domain.Sales.SaleLine
'
' P5-01 / plan.md section 7's key design call for this phase: "A later price
' change must not retroactively alter historical sales analysis." A sale
' line that stored a ProductId and joined to Products.Price for reporting
' is exactly the defect that warns about, and it would not show up until
' Phase 6's reports disagree with the receipts (P5-07).
'
' So this type captures its own CapturedUnitPrice/CapturedUnitCost as plain
' data at construction, and nothing on this class can ever re-read
' Products. LineTotal is computed once, here, at construction time - never
' recomputed later from a joined price.
'
' Pure Domain: no database, no ASP.NET Core. Money and quantity are Decimal
' throughout; scale is enforced by the existing DecimalScaleGuard
' (ADR-004.1) and the rounding direction is whichever MidpointRounding the
' caller resolved from the "currency.roundingPolicy" SystemSettings value
' via SaleRoundingPolicy.Parse - never an implicit default.

Namespace Sales

    Public NotInheritable Class SaleLine

        Public ReadOnly Property ProductId As Integer
        Public ReadOnly Property Quantity As Decimal
        Public ReadOnly Property CapturedUnitPrice As Decimal
        Public ReadOnly Property CapturedUnitCost As Decimal

        ''' <summary>Quantity * CapturedUnitPrice, rounded once at construction to DECIMAL(19,4) using the supplied rounding policy. Never recomputed.</summary>
        Public ReadOnly Property LineTotal As Decimal

        ''' <summary>
        ''' Constructor parameters are deliberately NOT spelled as the
        ''' lowercase form of their property (productId vs ProductId, etc.):
        ''' VB is case-insensitive, so "ProductId = productId" would resolve
        ''' both sides to the same local parameter and silently no-op rather
        ''' than set the property. Matches SystemSettingDefinition's
        ''' "settingKey" -&gt; Key convention.
        ''' </summary>
        Public Sub New(saleProductId As Integer, lineQuantity As Decimal, unitPrice As Decimal, unitCost As Decimal, roundingPolicy As MidpointRounding)

            If lineQuantity <= 0D Then
                Throw New ArgumentOutOfRangeException(NameOf(lineQuantity), "A sale line quantity must be greater than zero.")
            End If

            DecimalScaleGuard.EnsureQuantityScale(lineQuantity)
            DecimalScaleGuard.EnsureMoneyScale(unitPrice)
            DecimalScaleGuard.EnsureMoneyScale(unitCost)

            ProductId = saleProductId
            Quantity = lineQuantity
            CapturedUnitPrice = unitPrice
            CapturedUnitCost = unitCost
            LineTotal = Decimal.Round(lineQuantity * unitPrice, DecimalScaleGuard.MoneyScale, roundingPolicy)

        End Sub

    End Class

End Namespace
