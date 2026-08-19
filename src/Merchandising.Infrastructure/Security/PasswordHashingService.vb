' Merchandising.Infrastructure.Security.PasswordHashingService
'
' Wraps Microsoft.AspNetCore.Identity.PasswordHasher(Of TUser) - spec
' section 9's "framework-provided password-hashing implementation".
' Available here through the FrameworkReference added to this project's
' .vbproj at P1-08; see that file's comment for why no NuGet package or
' version pin was needed.
'
' PasswordHasher(Of TUser)'s default (V3) format is salted PBKDF2-HMACSHA256
' and never stores or returns the plaintext password - only the hash string
' goes to Users.PasswordHash.

Imports Merchandising.Domain.Entities
Imports Microsoft.AspNetCore.Identity

Namespace Security

    ''' <summary>
    ''' Hashes and verifies passwords for <see cref="User"/> accounts.
    ''' </summary>
    Public NotInheritable Class PasswordHashingService

        Private ReadOnly _hasher As New PasswordHasher(Of User)()

        ''' <summary>
        ''' Produces a new salted hash for <paramref name="password"/>. The
        ''' caller stores only the returned string, never the password
        ''' itself.
        ''' </summary>
        Public Function HashPassword(password As String) As String

            If password Is Nothing Then
                Throw New ArgumentNullException(NameOf(password))
            End If

            Return _hasher.HashPassword(Nothing, password)

        End Function

        ''' <summary>
        ''' Checks <paramref name="suppliedPassword"/> against a stored hash.
        ''' <see cref="PasswordVerificationResult.SuccessRehashNeeded"/>
        ''' counts as a match here - rehashing on login to pick up an
        ''' improved work factor is a real refinement but not required to
        ''' prove the P1-08 acceptance matrix, and is left for a later task.
        ''' </summary>
        Public Function VerifyPassword(storedHash As String, suppliedPassword As String) As Boolean

            If String.IsNullOrEmpty(storedHash) Then
                Return False
            End If

            Dim result As PasswordVerificationResult =
                _hasher.VerifyHashedPassword(Nothing, storedHash, suppliedPassword)

            Return result = PasswordVerificationResult.Success OrElse
                   result = PasswordVerificationResult.SuccessRehashNeeded

        End Function

    End Class

End Namespace
