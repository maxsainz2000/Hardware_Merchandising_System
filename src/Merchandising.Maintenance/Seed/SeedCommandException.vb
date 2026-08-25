' Merchandising.Maintenance.Seed.SeedCommandException
'
' An operator/data problem in a "seed" run - a missing seed file, a product
' referencing a category/brand/unit name that is not in the same file's own
' lists. Distinguished from MySqlException so Program.vb can print the
' actual problem rather than a database-layer message, same shape as
' CreateUserCommandException / SeedDemoCommandException.

Namespace Seed

    Public NotInheritable Class SeedCommandException
        Inherits Exception

        Public Sub New(message As String)
            MyBase.New(message)
        End Sub

    End Class

End Namespace
