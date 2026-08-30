' Merchandising.Domain.Sales.PaymentMethod
'
' Spec section 10.3: "The MVP supports cash, card, and e-wallet recording."
' Three methods, all recorded; none of them authorized by this system
' (G-24 - CLAUDE.md section 5's forthcoming, this phase's own gap).
'
' THE ENUM NAME IS THE STABLE IDENTIFIER (ADR-020's principle, the same one
' P3-02/P4-02/P4-03 already applied to PurchaseOrders/PurchaseReturns/
' StockCounts/StockAdjustments Status columns). PosSchemaTests walks
' [Enum].GetNames on this enum and asserts every one of the three is
' accepted by CK_SalePayments_Method, so a method added later without a
' matching migration fails the suite rather than failing in production.
'
' Domain depends on nothing (CLAUDE.md section 4).

Namespace Sales

    ''' <summary>
    ''' The three payment methods spec section 10.3 names. Card and EWallet
    ''' are recorded only - never claimed as externally authorized (G-24).
    ''' </summary>
    Public Enum PaymentMethod

        ''' <summary>Physical cash. The only method with a tendered/change amount.</summary>
        Cash = 1

        ''' <summary>Card payment, recorded - not authorized or cleared by this system.</summary>
        Card = 2

        ''' <summary>E-wallet payment, recorded - not authorized or cleared by this system.</summary>
        EWallet = 3

    End Enum

End Namespace
