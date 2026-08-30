' Merchandising.Contracts.Sales.CreateSalePaymentRequest
'
' The one payment record a POST /api/v1/sales body carries - spec section
' 10.3 describes the POS workflow as "record an allowed payment" (singular),
' so this card's request shape is one payment, not a list, even though
' SalePayments (0011_pos.sql) is a one-to-many child table for a future
' card's use. See SaleService's own header for why.
'
' NO Amount. The one payment always covers the sale's full Total - the
' server computes it, never the client (the same reasoning CreateSaleLineRequest's
' header gives for omitting UnitPrice/Cost).
'
' TenderedAmount is REQUIRED for Cash and MUST BE ABSENT for Card/EWallet -
' SalesController's own validation enforces the pairing before the request
' ever reaches SaleService, mirroring CK_SalePayments_CashTenderPairing
' (0011_pos.sql) at the API boundary rather than only at the database.

Imports System.Text.Json.Serialization

Namespace Sales

    Public NotInheritable Class CreateSalePaymentRequest

        ''' <summary>One of "Cash", "Card", "EWallet" - Merchandising.Domain.Sales.PaymentMethod's enum names, the stable identifiers (ADR-020's principle).</summary>
        <JsonPropertyName("method")>
        Public Property Method As String = String.Empty

        ''' <summary>Required for Cash; must be Nothing for Card/EWallet. Validated to DECIMAL(19,4) scale (ADR-004.1) before any parameter is bound.</summary>
        <JsonPropertyName("tenderedAmount")>
        Public Property TenderedAmount As Decimal?

    End Class

End Namespace
