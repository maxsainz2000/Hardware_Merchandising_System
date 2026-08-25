' Merchandising.Domain.Entities.Product
'
' Plain data holder for a Products row (db/migrations/0001_foundation.sql,
' extended at 0006_product-master.sql). No behaviour beyond the data itself,
' matching User.vb's shape - Domain depends on nothing (CLAUDE.md section 4).

Namespace Entities

    ''' <summary>
    ''' A single Products row as it exists after P2-06's migration.
    ''' </summary>
    Public NotInheritable Class Product

        Public Property Id As Integer
        Public Property Sku As String = String.Empty
        Public Property Barcode As String = Nothing
        Public Property Name As String = String.Empty
        Public Property Description As String = Nothing
        Public Property CategoryId As Integer?
        Public Property BrandId As Integer?
        Public Property UnitId As Integer?
        Public Property Price As Decimal
        Public Property Cost As Decimal
        Public Property ReorderLevel As Decimal
        Public Property IsActive As Boolean
        Public Property RowVersion As Long
        Public Property CreatedAtUtc As DateTime
        Public Property UpdatedAtUtc As DateTime

    End Class

End Namespace
