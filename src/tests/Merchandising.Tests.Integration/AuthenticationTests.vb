' Merchandising.Tests.Integration.AuthenticationTests
'
' P1-08 evidence: AuthService against the real, pinned MariaDB instance
' (ADR-000, ADR-009) - proves the login/lockout/audit business rules spec
' section 9 requires. HTTP-level status codes (401 / 403 / 200 through
' AuthController and the policy-gated AdminController) are proven separately
' by a live run captured to p1-08-auth-matrix.txt: WebApplicationFactory is
' not wired until P1-19 (see that card's own note in tasks.md), so this
' suite calls AuthService directly, the same shape MigrationRunnerTests
' uses for MigrationRunner.
'
' Test accounts are real, permanent rows, not scratch data dropped in
' TearDown. AuditLogs.ActorUserId is a foreign key to Users, and AuditLogs
' is append-only by grant (CLAUDE.md section 5) - a test that deleted its
' own audit trail would be deleting audit rows, which nothing in this
' project does, including here. A fixed, idempotently-created set of
' "p1_08_fixture_*" accounts is created once and reused across runs; each
' test resets only the lockout state it needs before asserting, never rows.

Imports System.IO
Imports System.Threading.Tasks
Imports Merchandising.Api.Security
Imports Merchandising.Domain.Security
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class AuthenticationTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"

    ''' <summary>Not the account's real production password - a fixed value known only to this suite.</summary>
    Private Const FixturePassword As String = "P1-08 Fixture Passw0rd!"
    Private Const WrongPassword As String = "Definitely-Not-The-Fixture-Password!"

    Private Const LoginFixtureUsername As String = "p1_08_fixture_login"
    Private Const LockoutFixtureUsername As String = "p1_08_fixture_lockout"
    Private Const AdminFixtureUsername As String = "p1_08_fixture_admin"

    Private _apiFactory As ConnectionFactory
    Private _authService As AuthService

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _apiFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        _authService = New AuthService(_apiFactory)

        Await EnsureFixtureUserAsync(LoginFixtureUsername, "Cashier")
        Await EnsureFixtureUserAsync(LockoutFixtureUsername, "Cashier")
        Await EnsureFixtureUserAsync(AdminFixtureUsername, "Admin")

        Await ResetLockoutStateAsync(LoginFixtureUsername)
        Await ResetLockoutStateAsync(LockoutFixtureUsername)

    End Function

    ''' <summary>Done-when box 1: valid login returns a token.</summary>
    <TestMethod>
    Public Async Function Login_ValidCredentials_ReturnsTokenAndRoles() As Task

        Dim correlationId As String = Guid.NewGuid().ToString()

        Dim outcome As LoginOutcome =
            Await _authService.LoginAsync(LoginFixtureUsername, FixturePassword, correlationId)

        Assert.AreEqual(LoginOutcomeKind.Success, outcome.Kind)
        Assert.IsNotNull(outcome.Response)
        Assert.IsFalse(String.IsNullOrWhiteSpace(outcome.Response.Token))
        Assert.AreEqual(LoginFixtureUsername, outcome.Response.Username)
        Assert.Contains("Cashier", outcome.Response.Roles)
        Assert.IsGreaterThan(DateTime.UtcNow, outcome.Response.ExpiresAtUtc)

        ' The session the token names must actually resolve - proves the
        ' token is not merely returned but genuinely usable.
        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()
            Dim tokenHash As String = SessionTokenGenerator.Hash(outcome.Response.Token)
            Dim principal As SessionPrincipal =
                Await SessionRepository.FindActiveByTokenHashAsync(connection, tokenHash)

            Assert.IsNotNull(principal)
            Assert.AreEqual(LoginFixtureUsername, principal.Username)
            Assert.Contains("Cashier", principal.Roles)
        End Using

    End Function

    ''' <summary>Wrong password is rejected without locking on a single attempt.</summary>
    <TestMethod>
    Public Async Function Login_WrongPassword_ReturnsInvalidCredentials() As Task

        Dim correlationId As String = Guid.NewGuid().ToString()

        Dim outcome As LoginOutcome =
            Await _authService.LoginAsync(LoginFixtureUsername, WrongPassword, correlationId)

        Assert.AreEqual(LoginOutcomeKind.InvalidCredentials, outcome.Kind)
        Assert.IsNull(outcome.Response)

    End Function

    ''' <summary>
    ''' Spec section 9: the client must not be able to tell "no such account"
    ''' apart from "wrong password" - both are the same outcome.
    ''' </summary>
    <TestMethod>
    Public Async Function Login_UnknownUsername_SameOutcomeAsWrongPassword() As Task

        Dim correlationId As String = Guid.NewGuid().ToString()
        Dim unknownUsername As String = "p1_08_no_such_user_" & Guid.NewGuid().ToString("N").Substring(0, 8)

        Dim outcome As LoginOutcome =
            Await _authService.LoginAsync(unknownUsername, WrongPassword, correlationId)

        Assert.AreEqual(LoginOutcomeKind.InvalidCredentials, outcome.Kind)

    End Function

    ''' <summary>Done-when box 3: six bad passwords trigger lockout; the lockout event is logged.</summary>
    <TestMethod>
    Public Async Function Login_SixBadPasswords_LocksAccountAndLogsIt() As Task

        Dim correlationId As String = Guid.NewGuid().ToString()

        ' Attempts 1-4: below AuthenticationPolicy.MaxFailedLoginAttempts (5).
        For attemptNumber As Integer = 1 To AuthenticationPolicy.MaxFailedLoginAttempts - 1
            Dim outcome As LoginOutcome =
                Await _authService.LoginAsync(LockoutFixtureUsername, WrongPassword, correlationId)
            Assert.AreEqual(
                LoginOutcomeKind.InvalidCredentials, outcome.Kind,
                $"Attempt {attemptNumber} should not lock the account yet.")
        Next

        ' Attempt 5: crosses the threshold - this is the one that locks.
        Dim lockingOutcome As LoginOutcome =
            Await _authService.LoginAsync(LockoutFixtureUsername, WrongPassword, correlationId)
        Assert.AreEqual(LoginOutcomeKind.Locked, lockingOutcome.Kind)

        ' Attempt 6: the account is already locked, so this is rejected too.
        Dim sixthOutcome As LoginOutcome =
            Await _authService.LoginAsync(LockoutFixtureUsername, WrongPassword, correlationId)
        Assert.AreEqual(LoginOutcomeKind.Locked, sixthOutcome.Kind)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Dim user As Merchandising.Domain.Entities.User =
                Await UserRepository.FindByUsernameAsync(connection, LockoutFixtureUsername)

            Assert.IsTrue(user.LockedUntilUtc.HasValue)
            Assert.IsGreaterThan(DateTime.UtcNow, user.LockedUntilUtc.Value)

            Dim lockEventCount As Long =
                Await CountAuditRowsAsync(connection, correlationId, "AccountLocked", LockoutFixtureUsername)
            Assert.AreEqual(1L, lockEventCount, "Exactly one AccountLocked audit row must be written, at the moment the threshold is crossed.")

        End Using

    End Function

    ''' <summary>An account already locked rejects even the correct password.</summary>
    <TestMethod>
    Public Async Function Login_WhileLocked_RejectsEvenCorrectPassword() As Task

        Dim correlationId As String = Guid.NewGuid().ToString()

        For attemptNumber As Integer = 1 To AuthenticationPolicy.MaxFailedLoginAttempts
            Await _authService.LoginAsync(LockoutFixtureUsername, WrongPassword, correlationId)
        Next

        Dim outcome As LoginOutcome =
            Await _authService.LoginAsync(LockoutFixtureUsername, FixturePassword, correlationId)

        Assert.AreEqual(
            LoginOutcomeKind.Locked, outcome.Kind,
            "The correct password must not unlock the account early - only the lockout window may.")

    End Function

    ''' <summary>A successful login clears prior failed-attempt state.</summary>
    <TestMethod>
    Public Async Function Login_SuccessAfterFailures_ResetsFailedAttemptCounter() As Task

        Dim correlationId As String = Guid.NewGuid().ToString()

        Await _authService.LoginAsync(LoginFixtureUsername, WrongPassword, correlationId)
        Await _authService.LoginAsync(LoginFixtureUsername, WrongPassword, correlationId)

        Dim outcome As LoginOutcome =
            Await _authService.LoginAsync(LoginFixtureUsername, FixturePassword, correlationId)
        Assert.AreEqual(LoginOutcomeKind.Success, outcome.Kind)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()
            Dim user As Merchandising.Domain.Entities.User =
                Await UserRepository.FindByUsernameAsync(connection, LoginFixtureUsername)

            Assert.AreEqual(0, user.FailedLoginAttempts)
            Assert.IsFalse(user.LockedUntilUtc.HasValue)
        End Using

    End Function

    ''' <summary>Logout revokes the session - the token no longer resolves to anyone.</summary>
    <TestMethod>
    Public Async Function Logout_DeletesSession_TokenNoLongerResolves() As Task

        Dim correlationId As String = Guid.NewGuid().ToString()

        Dim loginOutcome As LoginOutcome =
            Await _authService.LoginAsync(LoginFixtureUsername, FixturePassword, correlationId)
        Assert.AreEqual(LoginOutcomeKind.Success, loginOutcome.Kind)

        Dim token As String = loginOutcome.Response.Token
        Dim tokenHash As String = SessionTokenGenerator.Hash(token)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()
            Dim beforeLogout As SessionPrincipal =
                Await SessionRepository.FindActiveByTokenHashAsync(connection, tokenHash)
            Assert.IsNotNull(beforeLogout)
        End Using

        Dim user As Merchandising.Domain.Entities.User
        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()
            user = Await UserRepository.FindByUsernameAsync(connection, LoginFixtureUsername)
        End Using

        Await _authService.LogoutAsync(token, user.Id, LoginFixtureUsername, correlationId)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()
            Dim afterLogout As SessionPrincipal =
                Await SessionRepository.FindActiveByTokenHashAsync(connection, tokenHash)
            Assert.IsNull(afterLogout)
        End Using

    End Function

    ''' <summary>Done-when box: no password value appears in any log - checked here against AuditLogs.</summary>
    <TestMethod>
    Public Async Function AuditLogs_NeverContainThePlaintextPassword() As Task

        Dim correlationId As String = Guid.NewGuid().ToString()
        Dim distinctiveWrongPassword As String = "Distinctive-" & Guid.NewGuid().ToString("N") & "-Password"

        Await _authService.LoginAsync(LoginFixtureUsername, distinctiveWrongPassword, correlationId)
        Await _authService.LoginAsync(LoginFixtureUsername, FixturePassword, correlationId)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT Action, Target, Result, COALESCE(Detail, '') " &
                    "FROM AuditLogs WHERE CorrelationId = @correlationId;"
                command.Parameters.AddWithValue("@correlationId", correlationId)

                Dim rowCount As Integer = 0

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync()
                    While Await reader.ReadAsync()
                        rowCount += 1
                        For columnIndex As Integer = 0 To 3
                            Dim value As String = reader.GetString(columnIndex)
                            Assert.DoesNotContain(
                                distinctiveWrongPassword, value,
                                $"AuditLogs column {columnIndex} contained the plaintext password.")
                            Assert.DoesNotContain(
                                FixturePassword, value,
                                $"AuditLogs column {columnIndex} contained the plaintext password.")
                        Next
                    End While
                End Using

                Assert.IsGreaterThan(0, rowCount, "This test's own login attempts must have written at least one audit row to check.")

            End Using

        End Using

    End Function

    Private Async Function EnsureFixtureUserAsync(username As String, roleName As String) As Task

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()
            Dim existing As Merchandising.Domain.Entities.User =
                Await UserRepository.FindByUsernameAsync(connection, username)

            If existing IsNot Nothing Then
                Return
            End If
        End Using

        Dim migratorFactory As New ConnectionFactory(LoadMigratorOptions())
        Await CreateUserCommand.RunAsync(migratorFactory, username, FixturePassword, roleName)

    End Function

    Private Async Function ResetLockoutStateAsync(username As String) As Task

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()
            Dim user As Merchandising.Domain.Entities.User =
                Await UserRepository.FindByUsernameAsync(connection, username)
            Await UserRepository.RecordSuccessfulLoginAsync(connection, user.Id)
        End Using

    End Function

    Private Async Function CountAuditRowsAsync(
        connection As MySqlConnection, correlationId As String, action As String, target As String) As Task(Of Long)

        Using command As MySqlCommand = connection.CreateCommand()
            command.CommandText =
                "SELECT COUNT(*) FROM AuditLogs " &
                "WHERE CorrelationId = @correlationId AND Action = @action AND Target = @target;"
            command.Parameters.AddWithValue("@correlationId", correlationId)
            command.Parameters.AddWithValue("@action", action)
            command.Parameters.AddWithValue("@target", target)

            Return CLng(Await command.ExecuteScalarAsync())
        End Using

    End Function

    Private Shared Function LoadMigratorOptions() As DatabaseOptions
        Dim migratorConfigPath As String =
            Path.Combine(Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName)
        Return DatabaseOptionsLoader.Load(migratorConfigPath)
    End Function

End Class
