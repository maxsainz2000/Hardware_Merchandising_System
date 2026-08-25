' Merchandising.Maintenance.Seed.SeedResult
'
' What a "seed" run did, in the same spirit as SeedDemoResult: the command is
' re-runnable, and a second run that quietly reported success would be
' indistinguishable from a first one. Created/Skipped counts let Program.vb
' tell the operator "clean database populated" apart from "already seeded,
' nothing to do" - both are success, but only one produced new credentials.

Namespace Seed

    ''' <summary>A single test account this run created. Password is plaintext, in memory only.</summary>
    Public NotInheritable Class SeededUserCredential

        Public Sub New(username As String, password As String, roleName As String)
            Me.Username = username
            Me.Password = password
            Me.RoleName = roleName
        End Sub

        Public ReadOnly Property Username As String
        Public ReadOnly Property Password As String
        Public ReadOnly Property RoleName As String

    End Class

    ''' <summary>Outcome of one <c>seed</c> run.</summary>
    Public NotInheritable Class SeedResult

        Public Sub New(categoriesCreated As Integer, categoriesSkipped As Integer,
                       brandsCreated As Integer, brandsSkipped As Integer,
                       unitsCreated As Integer, unitsSkipped As Integer,
                       productsCreated As Integer, productsSkipped As Integer,
                       suppliersCreated As Integer, suppliersSkipped As Integer,
                       usersCreated As IReadOnlyList(Of SeededUserCredential),
                       usersSkipped As Integer)

            Me.CategoriesCreated = categoriesCreated
            Me.CategoriesSkipped = categoriesSkipped
            Me.BrandsCreated = brandsCreated
            Me.BrandsSkipped = brandsSkipped
            Me.UnitsCreated = unitsCreated
            Me.UnitsSkipped = unitsSkipped
            Me.ProductsCreated = productsCreated
            Me.ProductsSkipped = productsSkipped
            Me.SuppliersCreated = suppliersCreated
            Me.SuppliersSkipped = suppliersSkipped
            Me.UsersCreated = usersCreated
            Me.UsersSkipped = usersSkipped

        End Sub

        Public ReadOnly Property CategoriesCreated As Integer
        Public ReadOnly Property CategoriesSkipped As Integer
        Public ReadOnly Property BrandsCreated As Integer
        Public ReadOnly Property BrandsSkipped As Integer
        Public ReadOnly Property UnitsCreated As Integer
        Public ReadOnly Property UnitsSkipped As Integer
        Public ReadOnly Property ProductsCreated As Integer
        Public ReadOnly Property ProductsSkipped As Integer
        Public ReadOnly Property SuppliersCreated As Integer
        Public ReadOnly Property SuppliersSkipped As Integer

        ''' <summary>Test accounts created THIS run, with plaintext passwords. Empty on a repeat run.</summary>
        Public ReadOnly Property UsersCreated As IReadOnlyList(Of SeededUserCredential)

        ''' <summary>Of the five role accounts, how many already existed.</summary>
        Public ReadOnly Property UsersSkipped As Integer

    End Class

End Namespace
