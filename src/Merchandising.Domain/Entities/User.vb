' Merchandising.Domain.Entities.User
'
' Plain data holder for a Users row (db/migrations/0001_foundation.sql,
' 0002_authentication.sql). No behaviour beyond the data itself - hashing,
' lockout decisions and persistence all live outside Domain, which depends
' on nothing (CLAUDE.md section 4).

Namespace Entities

    ''' <summary>
    ''' A single Users row, including the lockout state added at P1-08.
    ''' </summary>
    Public NotInheritable Class User

        Public Property Id As Integer
        Public Property Username As String = String.Empty
        Public Property PasswordHash As String = String.Empty
        Public Property IsActive As Boolean
        Public Property FailedLoginAttempts As Integer
        Public Property LockedUntilUtc As DateTime?
        Public Property Roles As IReadOnlyList(Of String) = Array.Empty(Of String)()

    End Class

End Namespace
