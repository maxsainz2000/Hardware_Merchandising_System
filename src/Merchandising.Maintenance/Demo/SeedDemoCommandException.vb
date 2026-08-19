' Merchandising.Maintenance.Demo.SeedDemoCommandException
'
' An operator mistake during seed-demo - a missing user, a negative quantity, a
' database in a state this command will not silently repair. Distinguished from
' MySqlException so Program.vb can print the operator's problem rather than a
' database-layer message they cannot act on.

Namespace Demo

    ''' <summary>A seed-demo failure caused by input or by unexpected data.</summary>
    Public Class SeedDemoCommandException
        Inherits Exception

        Public Sub New()
            MyBase.New()
        End Sub

        Public Sub New(message As String)
            MyBase.New(message)
        End Sub

        Public Sub New(message As String, innerException As Exception)
            MyBase.New(message, innerException)
        End Sub

    End Class

End Namespace
