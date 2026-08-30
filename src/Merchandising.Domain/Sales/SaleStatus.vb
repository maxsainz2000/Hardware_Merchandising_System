' Merchandising.Domain.Sales.SaleStatus
'
' Spec section 10.3: "Completed sales are never edited or deleted. A sale
' may be cancelled only before completion." P5-02 stores the Sales header
' only at completion - the atomic sale (P5-07) creates it, its lines,
' payments, movements, balance changes, session totals and audit row
' together, in one transaction, per spec section 11's atomic-result row.
' There is no persisted draft/in-progress row for a cart to attach to, so
' Cancelled is modelled now but driven by no card in this phase - the same
' StockCounts/StockAdjustments precedent (P4-03) of naming a state before a
' later card can reach it, rather than rewriting the table when one does.
'
' THE ENUM NAME IS THE STABLE IDENTIFIER (ADR-020's principle). PosSchemaTests
' walks [Enum].GetNames on this enum and asserts every one is accepted by
' CK_Sales_Status.
'
' Domain depends on nothing (CLAUDE.md section 4).

Namespace Sales

    ''' <summary>
    ''' The two sale-header statuses spec section 10.3 describes. A sale row
    ''' is only ever inserted already-Completed; Cancelled is reserved for a
    ''' mechanism no card in this phase drives yet.
    ''' </summary>
    Public Enum SaleStatus

        ''' <summary>Committed by the atomic sale transaction (P5-07). Immutable from here.</summary>
        Completed = 1

        ''' <summary>Reserved - not written by any card in this phase.</summary>
        Cancelled = 2

    End Enum

End Namespace
