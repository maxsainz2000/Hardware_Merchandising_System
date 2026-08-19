' Merchandising.Api.Security.AuthService
'
' Orchestrates login and logout: password verification, lockout tracking,
' session issuance/revocation, and the audit trail spec section 9 requires.
' Kept independent of ASP.NET Core's HTTP types (no HttpContext, no
' ActionResult) so it is directly testable against the real database, the
' same way Merchandising.Maintenance.Migrations.MigrationRunner is - see
' AuthenticationTests.vb.

Imports Merchandising.Contracts.Auth
Imports Merchandising.Domain.Security
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Infrastructure.Security
Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Security

    Public NotInheritable Class AuthService

        Private ReadOnly _connectionFactory As ConnectionFactory
        Private ReadOnly _passwordHasher As New PasswordHashingService()

        Public Sub New(connectionFactory As ConnectionFactory)

            If connectionFactory Is Nothing Then
                Throw New ArgumentNullException(NameOf(connectionFactory))
            End If

            _connectionFactory = connectionFactory

        End Sub

        ''' <summary>
        ''' Attempts a login. Never throws for "no such user", "wrong
        ''' password", or "locked" - those are <see cref="LoginOutcome"/>
        ''' values, not exceptions, because AuthController must return a
        ''' specific status code for each rather than a generic failure.
        ''' </summary>
        Public Async Function LoginAsync(
            username As String,
            password As String,
            correlationId As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of LoginOutcome)

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim user = Await UserRepository.FindByUsernameAsync(connection, username, cancellationToken).ConfigureAwait(False)

                ' Same response for "no such account" and "account inactive"
                ' as for "wrong password" below - the client must not be
                ' able to tell them apart (spec section 9).
                If user Is Nothing OrElse Not user.IsActive Then
                    Return LoginOutcome.InvalidCredentials()
                End If

                If user.LockedUntilUtc.HasValue AndAlso user.LockedUntilUtc.Value > DateTime.UtcNow Then
                    Await AuditLogWriter.WriteAsync(
                        connection, user.Id, "LoginRejectedLocked", user.Username, "Denied", correlationId,
                        cancellationToken:=cancellationToken).ConfigureAwait(False)
                    Return LoginOutcome.Locked()
                End If

                If Not _passwordHasher.VerifyPassword(user.PasswordHash, password) Then
                    Dim attempt = Await UserRepository.RecordFailedLoginAsync(connection, user.Id, cancellationToken).ConfigureAwait(False)
                    Dim justLocked As Boolean = attempt.LockedUntilUtc.HasValue

                    Await AuditLogWriter.WriteAsync(
                        connection, user.Id, "LoginFailed", user.Username, "Denied", correlationId,
                        $"Attempt {attempt.Attempts} of {AuthenticationPolicy.MaxFailedLoginAttempts}",
                        cancellationToken).ConfigureAwait(False)

                    If justLocked Then
                        Await AuditLogWriter.WriteAsync(
                            connection, user.Id, "AccountLocked", user.Username, "Locked", correlationId,
                            $"Locked until {attempt.LockedUntilUtc.Value:O} after {attempt.Attempts} failed attempts",
                            cancellationToken).ConfigureAwait(False)
                        Return LoginOutcome.Locked()
                    End If

                    Return LoginOutcome.InvalidCredentials()
                End If

                Await UserRepository.RecordSuccessfulLoginAsync(connection, user.Id, cancellationToken).ConfigureAwait(False)

                Dim token As String = SessionTokenGenerator.NewToken()
                Dim tokenHash As String = SessionTokenGenerator.Hash(token)
                Dim expiresAtUtc As DateTime =
                    Await SessionRepository.CreateAsync(connection, user.Id, tokenHash, cancellationToken).ConfigureAwait(False)

                Await AuditLogWriter.WriteAsync(
                    connection, user.Id, "LoginSucceeded", user.Username, "Success", correlationId,
                    cancellationToken:=cancellationToken).ConfigureAwait(False)

                Return LoginOutcome.Success(New LoginResponse With {
                    .Token = token,
                    .ExpiresAtUtc = expiresAtUtc,
                    .Username = user.Username,
                    .Roles = user.Roles
                })

            End Using

        End Function

        ''' <summary>
        ''' Ends the session <paramref name="token"/> belongs to. Idempotent -
        ''' see SessionRepository.DeleteByTokenHashAsync.
        ''' </summary>
        Public Async Function LogoutAsync(
            token As String,
            userId As Integer,
            username As String,
            correlationId As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim tokenHash As String = SessionTokenGenerator.Hash(token)
                Await SessionRepository.DeleteByTokenHashAsync(connection, tokenHash, cancellationToken).ConfigureAwait(False)

                Await AuditLogWriter.WriteAsync(
                    connection, userId, "Logout", username, "Success", correlationId,
                    cancellationToken:=cancellationToken).ConfigureAwait(False)

            End Using

        End Function

    End Class

End Namespace
