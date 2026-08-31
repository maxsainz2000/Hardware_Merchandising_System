' Merchandising.Maintenance.Users.ResetPasswordCommandException
'
' A controlled failure of the reset-password command - unknown operator,
' unknown target, empty password - as opposed to an unexpected
' MySqlException. Same "expected input problem" distinction
' CreateUserCommandException documents for create-user.

Namespace Users

    Public NotInheritable Class ResetPasswordCommandException
        Inherits Exception

        Public Sub New(message As String)
            MyBase.New(message)
        End Sub

    End Class

End Namespace
