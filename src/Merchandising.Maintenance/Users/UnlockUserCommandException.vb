' Merchandising.Maintenance.Users.UnlockUserCommandException
'
' A controlled failure of the unlock-user command - unknown operator or
' unknown target - as opposed to an unexpected MySqlException. Same
' "expected input problem" distinction CreateUserCommandException documents
' for create-user.

Namespace Users

    Public NotInheritable Class UnlockUserCommandException
        Inherits Exception

        Public Sub New(message As String)
            MyBase.New(message)
        End Sub

    End Class

End Namespace
