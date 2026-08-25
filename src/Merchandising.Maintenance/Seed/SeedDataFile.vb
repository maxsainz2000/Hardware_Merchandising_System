' Merchandising.Maintenance.Seed.SeedDataFile
'
' Plain data holder matching db/seed/seed-data.json - the non-secret catalog
' data (categories, brands, units, products, suppliers) SeedCommand loads and
' makes idempotent against the database. No credentials live in this shape or
' in the file it deserializes - see SeedCommand.vb for where the five test
' accounts' passwords are generated instead (ADR-012 requirement 6: never a
' literal committed to the repository).
'
' Deserialized with JsonSerializer.Deserialize(Of SeedDataFile), reflection-
' based like DatabaseOptionsLoader (CLAUDE.md section 3: source generators
' are C#-only, so this project never opts into System.Text.Json source
' generation).

Namespace Seed

    Public NotInheritable Class SeedDataFile

        Public Property Categories As List(Of String) = New List(Of String)
        Public Property Brands As List(Of String) = New List(Of String)
        Public Property Units As List(Of String) = New List(Of String)
        Public Property Products As List(Of SeedProductDefinition) = New List(Of SeedProductDefinition)
        Public Property Suppliers As List(Of SeedSupplierDefinition) = New List(Of SeedSupplierDefinition)

    End Class

    Public NotInheritable Class SeedProductDefinition

        Public Property Sku As String = String.Empty
        Public Property Name As String = String.Empty
        Public Property Category As String = String.Empty
        Public Property Brand As String = String.Empty
        Public Property Unit As String = String.Empty
        Public Property Price As Decimal
        Public Property Cost As Decimal

    End Class

    Public NotInheritable Class SeedSupplierDefinition

        Public Property Name As String = String.Empty
        Public Property ContactName As String = Nothing
        Public Property Phone As String = Nothing
        Public Property Email As String = Nothing
        Public Property Address As String = Nothing

    End Class

End Namespace
