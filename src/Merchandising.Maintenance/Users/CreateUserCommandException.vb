' Merchandising.Maintenance.Users.CreateUserCommandException
'
' A controlled failure of the create-user command - unknown role, duplicate
' username - as opposed to an unexpected MySqlException. Program.vb reports
' both as a clean error line and a non-zero exit code, but keeping this one
' distinct documents which failures are "expected input problems" versus
' "something at the database layer went wrong".

Namespace Users

    Public NotInheritable Class CreateUserCommandException
        Inherits Exception

        Public Sub New(message As String)
            MyBase.New(message)
        End Sub

    End Class

End Namespace
