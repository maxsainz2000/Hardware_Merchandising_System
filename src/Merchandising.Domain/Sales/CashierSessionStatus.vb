' Merchandising.Domain.Sales.CashierSessionStatus
'
' Spec section 10.3: "A sale requires an open cashier session" and "Cashier
' daily closing records the session totals... declared cash, calculated
' cash, variance, closing user, timestamp." Two states cover the whole
' lifecycle a session in this phase can be in - opened (P5-04) and closed
' (P5-04/P5-05) - so unlike PurchaseOrders/StockCounts this enum carries no
' state a later card has yet to drive.
'
' THE ENUM NAME IS THE STABLE IDENTIFIER (ADR-020's principle). PosSchemaTests
' walks [Enum].GetNames on this enum and asserts every one is accepted by
' CK_CashierSessions_Status.
'
' Domain depends on nothing (CLAUDE.md section 4).

Namespace Sales

    ''' <summary>The two cashier-session statuses spec section 10.3 describes.</summary>
    Public Enum CashierSessionStatus

        ''' <summary>Open. A sale may attach to this session; DeclaredCash/CalculatedCash/CashVariance are not yet set.</summary>
        Open = 1

        ''' <summary>Closed. Immutable from here; DeclaredCash/CalculatedCash/CashVariance/ClosedByUserId/ClosedAtUtc are set.</summary>
        Closed = 2

    End Enum

End Namespace
