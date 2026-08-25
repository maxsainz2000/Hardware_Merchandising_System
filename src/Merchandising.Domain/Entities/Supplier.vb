' Merchandising.Domain.Entities.Supplier
'
' Plain data holder for a Suppliers row (db/migrations/0007_suppliers.sql).
' No behaviour beyond the data itself, matching Product.vb's shape - Domain
' depends on nothing (CLAUDE.md section 4).

Namespace Entities

    ''' <summary>
    ''' A single Suppliers row.
    ''' </summary>
    Public NotInheritable Class Supplier

        Public Property Id As Integer
        Public Property Name As String = String.Empty
        Public Property ContactName As String = Nothing
        Public Property Phone As String = Nothing
        Public Property Email As String = Nothing
        Public Property Address As String = Nothing
        Public Property IsActive As Boolean
        Public Property RowVersion As Long
        Public Property CreatedAtUtc As DateTime
        Public Property UpdatedAtUtc As DateTime

    End Class

End Namespace
