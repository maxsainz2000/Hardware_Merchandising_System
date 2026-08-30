' Merchandising.Domain.Entities.SalesReturnLine
'
' Plain data holder for a SalesReturnLines row (db/migrations/0012_sales-returns.sql).
' Domain depends on nothing (CLAUDE.md section 4).
'
' NO UnitPrice PROPERTY STORED ON THE ROW - 0012's own migration comment:
' "the refund value for a line is safely computed by joining SaleLineId to
' SaleLines.UnitPrice rather than re-capturing a value that can never
' disagree with its source." UnitPrice here is a PROJECTION, joined by the
' repository on read (the same arrangement ProductSku/ProductName use),
' never a column this type's constructor is expected to persist.
'
' RestocksItem, not RemovesStock - see 0012's header for why the flag name
' is reversed relative to PurchaseReturnLine's equivalent.

Namespace Entities

    ''' <summary>A single SalesReturnLines row, joined to its SaleLines/Products context for display.</summary>
    Public NotInheritable Class SalesReturnLine

        Public Property Id As Integer

        Public Property SalesReturnId As Integer

        Public Property SaleLineId As Integer

        Public Property ProductId As Integer

        ''' <summary>Joined from Products on read; empty on an instance built for an insert.</summary>
        Public Property ProductSku As String = String.Empty

        ''' <summary>Joined from Products on read; empty on an instance built for an insert.</summary>
        Public Property ProductName As String = String.Empty

        Public Property QuantityReturned As Decimal

        ''' <summary>Joined from SaleLines.UnitPrice on read - never stored on this row (this file's header).</summary>
        Public Property UnitPrice As Decimal

        ''' <summary>True: this line is eligible to re-enter stock (spec section 10.3). False: recorded operationally with no stock effect - e.g. a defective item.</summary>
        Public Property RestocksItem As Boolean

        Public Property CreatedAtUtc As DateTime

    End Class

End Namespace
