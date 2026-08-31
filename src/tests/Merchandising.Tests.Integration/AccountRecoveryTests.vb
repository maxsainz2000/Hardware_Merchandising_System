' Merchandising.Tests.Integration.AccountRecoveryTests
'
' P6-12 / ADR-028 evidence: Merchandising.Maintenance.Users.ResetPasswordCommand
' and UnlockUserCommand against the real, pinned MariaDB instance (ADR-000,
' ADR-009) - never a substitute.
'
' Fixture accounts are real, permanent rows (p6_12_fixture_operator,
' p6_12_fixture_target), the same shape every earlier integration suite uses
' (AuthenticationTests, AuthorizationMatrixTests) - AuditLogs.ActorUserId is a
' foreign key to Users and AuditLogs is append-only (CLAUDE.md section 5), so
' a test account is never scratch data to delete. Because the password itself
' is what several tests mutate, SetUp re-baselines it (and lockout state)
' directly before every test rather than assuming the previous test's outcome
' - the account-recovery equivalent of AuthenticationTests' own
' ResetLockoutStateAsync.
'
' Audit-row assertions use a before/after count delta rather than a specific
' CorrelationId, because both commands generate their own correlation id
' internally (there is no login-style caller-supplied one to filter on) - the
' same reasoning that makes a delta the only assertion that stays exact
' across repeated runs against a permanent fixture row.

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks
Imports Merchandising.Api.Security
Imports Merchandising.Domain.Entities
Imports Merchandising.Domain.Security
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.AspNetCore.Mvc.Abstractions
Imports Microsoft.AspNetCore.Mvc.Controllers
Imports Microsoft.AspNetCore.Mvc.Infrastructure
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class AccountRecoveryTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"

    Private Const OperatorFixtureUsername As String = "p6_12_fixture_operator"
    Private Const TargetFixtureUsername As String = "p6_12_fixture_target"

    ''' <summary>Not the accounts' real production passwords - fixed values known only to this suite.</summary>
    Private Const BaselinePassword As String = "P6-12 Fixture Passw0rd!"
    Private Const NewPassword As String = "P6-12 Fixture New Passw0rd!"

    Private _apiFactory As ConnectionFactory
    Private _migratorFactory As ConnectionFactory
    Private _authService As AuthService

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _apiFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        _migratorFactory = New ConnectionFactory(LoadMigratorOptions())
        _authService = New AuthService(_apiFactory)

        Await EnsureFixtureUserAsync(OperatorFixtureUsername, "SuperAdmin")
        Await EnsureFixtureUserAsync(TargetFixtureUsername, "Cashier")

        Await ResetTargetToBaselineAsync()

    End Function

    ''' <summary>Done-when box 1: reset-password sets a new password through the same hashing path create-user uses.</summary>
    <TestMethod>
    Public Async Function ResetPassword_ValidTarget_ChangesPasswordAndAuditsIt() As Task

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Dim operatorUser As User = Await UserRepository.FindByUsernameAsync(connection, OperatorFixtureUsername)
            Dim before As Long = Await CountAuditRowsAsync(connection, "ResetPassword", TargetFixtureUsername, operatorUser.Id)

            Dim returnedId As Integer =
                Await ResetPasswordCommand.RunAsync(_migratorFactory, OperatorFixtureUsername, TargetFixtureUsername, NewPassword)

            Dim target As User = Await UserRepository.FindByUsernameAsync(connection, TargetFixtureUsername)
            Assert.AreEqual(target.Id, returnedId)

            Dim after As Long = Await CountAuditRowsAsync(connection, "ResetPassword", TargetFixtureUsername, operatorUser.Id)
            Assert.AreEqual(before + 1L, after, "reset-password must write exactly one AuditLogs row naming the operator and the target.")

        End Using

        Dim correlationId As String = Guid.NewGuid().ToString()

        Dim withNewPassword As LoginOutcome = Await _authService.LoginAsync(TargetFixtureUsername, NewPassword, correlationId)
        Assert.AreEqual(LoginOutcomeKind.Success, withNewPassword.Kind, "The new password must log in - same hashing path create-user/AuthService already verify.")

        Dim withOldPassword As LoginOutcome = Await _authService.LoginAsync(TargetFixtureUsername, BaselinePassword, correlationId)
        Assert.AreEqual(LoginOutcomeKind.InvalidCredentials, withOldPassword.Kind, "The old password must stop working - this is not a second, additive credential.")

    End Function

    ''' <summary>An unknown target is an operator mistake, not a database error.</summary>
    <TestMethod>
    Public Async Function ResetPassword_UnknownTarget_ThrowsResetPasswordCommandException() As Task

        Dim unknownUsername As String = "p6_12_no_such_user_" & Guid.NewGuid().ToString("N").Substring(0, 8)

        Await Assert.ThrowsExactlyAsync(Of ResetPasswordCommandException)(
            Function() ResetPasswordCommand.RunAsync(_migratorFactory, OperatorFixtureUsername, unknownUsername, NewPassword))

    End Function

    ''' <summary>The operator itself must be a real, resolvable account - the audit row cannot name a ghost.</summary>
    <TestMethod>
    Public Async Function ResetPassword_UnknownOperator_ThrowsResetPasswordCommandException() As Task

        Dim unknownOperator As String = "p6_12_no_such_operator_" & Guid.NewGuid().ToString("N").Substring(0, 8)

        Await Assert.ThrowsExactlyAsync(Of ResetPasswordCommandException)(
            Function() ResetPasswordCommand.RunAsync(_migratorFactory, unknownOperator, TargetFixtureUsername, NewPassword))

    End Function

    ''' <summary>Done-when boxes 2 and 3: unlock-user clears LockedUntilUtc and the failed-attempt counter, and audits it.</summary>
    <TestMethod>
    Public Async Function UnlockUser_LockedAccount_ClearsLockoutAndAuditsIt() As Task

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Dim target As User = Await UserRepository.FindByUsernameAsync(connection, TargetFixtureUsername)

            For attemptNumber As Integer = 1 To AuthenticationPolicy.MaxFailedLoginAttempts
                Await UserRepository.RecordFailedLoginAsync(connection, target.Id)
            Next

            Dim lockedTarget As User = Await UserRepository.FindByUsernameAsync(connection, TargetFixtureUsername)
            Assert.IsTrue(lockedTarget.LockedUntilUtc.HasValue, "This test's own setup must actually lock the account before unlock-user can be proven against it.")

            Dim operatorUser As User = Await UserRepository.FindByUsernameAsync(connection, OperatorFixtureUsername)
            Dim before As Long = Await CountAuditRowsAsync(connection, "UnlockUser", TargetFixtureUsername, operatorUser.Id)

            Dim returnedId As Integer =
                Await UnlockUserCommand.RunAsync(_migratorFactory, OperatorFixtureUsername, TargetFixtureUsername)
            Assert.AreEqual(target.Id, returnedId)

            Dim unlockedTarget As User = Await UserRepository.FindByUsernameAsync(connection, TargetFixtureUsername)
            Assert.IsFalse(unlockedTarget.LockedUntilUtc.HasValue, "LockedUntilUtc must be cleared.")
            Assert.AreEqual(0, unlockedTarget.FailedLoginAttempts, "The failed-attempt counter must be reset, not merely the lock.")

            Dim after As Long = Await CountAuditRowsAsync(connection, "UnlockUser", TargetFixtureUsername, operatorUser.Id)
            Assert.AreEqual(before + 1L, after, "unlock-user must write exactly one AuditLogs row naming the operator and the target.")

        End Using

        Dim correlationId As String = Guid.NewGuid().ToString()
        Dim outcome As LoginOutcome = Await _authService.LoginAsync(TargetFixtureUsername, BaselinePassword, correlationId)
        Assert.AreEqual(LoginOutcomeKind.Success, outcome.Kind, "A correct password must succeed again once the account is unlocked.")

    End Function

    ''' <summary>unlock-user is a no-op on an already-unlocked account, not an error - it still audits the attempt.</summary>
    <TestMethod>
    Public Async Function UnlockUser_AlreadyUnlockedAccount_SucceedsAndAuditsIt() As Task

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()

            Dim target As User = Await UserRepository.FindByUsernameAsync(connection, TargetFixtureUsername)
            Assert.IsFalse(target.LockedUntilUtc.HasValue, "SetUp must leave the target unlocked for this test to prove anything.")

        End Using

        Dim returnedId As Integer =
            Await UnlockUserCommand.RunAsync(_migratorFactory, OperatorFixtureUsername, TargetFixtureUsername)

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()
            Dim target As User = Await UserRepository.FindByUsernameAsync(connection, TargetFixtureUsername)
            Assert.AreEqual(target.Id, returnedId)
            Assert.IsFalse(target.LockedUntilUtc.HasValue)
        End Using

    End Function

    ''' <summary>An unknown target is an operator mistake, not a database error.</summary>
    <TestMethod>
    Public Async Function UnlockUser_UnknownTarget_ThrowsUnlockUserCommandException() As Task

        Dim unknownUsername As String = "p6_12_no_such_user_" & Guid.NewGuid().ToString("N").Substring(0, 8)

        Await Assert.ThrowsExactlyAsync(Of UnlockUserCommandException)(
            Function() UnlockUserCommand.RunAsync(_migratorFactory, OperatorFixtureUsername, unknownUsername))

    End Function

    ''' <summary>
    ''' Done-when box 4: account recovery runs only as the maintenance
    ''' identity, never through an API route. There is no UsersController at
    ''' all (ADR-017 section 4); this asserts that stays true rather than
    ''' trusting it does.
    ''' </summary>
    <TestMethod>
    Public Sub NoHttpRoute_ReachesResetPasswordOrUnlockUser()

        Using factory As New MerchandisingApiFactory()

            Dim provider As IActionDescriptorCollectionProvider =
                factory.Services.GetRequiredService(Of IActionDescriptorCollectionProvider)()

            Dim offending As New List(Of String)

            For Each descriptor As ActionDescriptor In provider.ActionDescriptors.Items

                Dim controllerAction As ControllerActionDescriptor = TryCast(descriptor, ControllerActionDescriptor)
                If controllerAction Is Nothing Then
                    Continue For
                End If

                Dim actionKey As String = $"{controllerAction.ControllerTypeInfo.FullName}.{controllerAction.ActionName}"
                Dim routeTemplate As String = If(descriptor.AttributeRouteInfo?.Template, String.Empty)

                If actionKey.Contains("ResetPassword", StringComparison.OrdinalIgnoreCase) OrElse
                   actionKey.Contains("UnlockUser", StringComparison.OrdinalIgnoreCase) OrElse
                   routeTemplate.Contains("reset-password", StringComparison.OrdinalIgnoreCase) OrElse
                   routeTemplate.Contains("unlock-user", StringComparison.OrdinalIgnoreCase) Then

                    offending.Add($"{actionKey} -> '{routeTemplate}'")

                End If

            Next

            Assert.IsEmpty(
                offending,
                "Account recovery must run only through the Maintenance CLI (ADR-028) - " &
                "no HTTP route may reach reset-password or unlock-user: " & String.Join(vbLf, offending))

            Dim hasUsersController As Boolean =
                provider.ActionDescriptors.Items.
                    OfType(Of ControllerActionDescriptor)().
                    Any(Function(d) d.ControllerTypeInfo.Name = "UsersController")

            Assert.IsFalse(hasUsersController, "ADR-017 section 4: user/role management has no HTTP surface at all.")

        End Using

    End Sub

    ' --------------------------------------------------------------- shared helpers

    Private Async Function EnsureFixtureUserAsync(username As String, roleName As String) As Task

        Using connection As MySqlConnection = Await _apiFactory.CreateOpenConnectionAsync()
            Dim existing As User = Await UserRepository.FindByUsernameAsync(connection, username)
            If existing IsNot Nothing Then
                Return
            End If
        End Using

        Await CreateUserCommand.RunAsync(_migratorFactory, username, BaselinePassword, roleName)

    End Function

    ''' <summary>
    ''' Puts the target fixture account back to a known state before every
    ''' test: BaselinePassword, no lockout. Uses the repository/command
    ''' primitives directly rather than assuming a previous test's ordering.
    ''' </summary>
    Private Async Function ResetTargetToBaselineAsync() As Task

        Using connection As MySqlConnection = Await _migratorFactory.CreateOpenConnectionAsync()

            Dim target As User = Await UserRepository.FindByUsernameAsync(connection, TargetFixtureUsername)

            Dim hasher As New Merchandising.Infrastructure.Security.PasswordHashingService()
            Dim baselineHash As String = hasher.HashPassword(BaselinePassword)

            Dim transaction As MySqlTransaction = Await connection.BeginTransactionAsync()
            Await UserRepository.UpdatePasswordHashAsync(connection, transaction, target.Id, baselineHash)
            Await transaction.CommitAsync()
            Await transaction.DisposeAsync()

            Await UserRepository.RecordSuccessfulLoginAsync(connection, target.Id)

        End Using

    End Function

    Private Async Function CountAuditRowsAsync(
        connection As MySqlConnection, action As String, target As String, actorUserId As Integer) As Task(Of Long)

        Using command As MySqlCommand = connection.CreateCommand()
            command.CommandText =
                "SELECT COUNT(*) FROM AuditLogs " &
                "WHERE Action = @action AND Target = @target AND ActorUserId = @actorUserId;"
            command.Parameters.AddWithValue("@action", action)
            command.Parameters.AddWithValue("@target", target)
            command.Parameters.AddWithValue("@actorUserId", actorUserId)

            Return CLng(Await command.ExecuteScalarAsync())
        End Using

    End Function

    Private Shared Function LoadMigratorOptions() As DatabaseOptions
        Dim migratorConfigPath As String =
            Path.Combine(Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName)
        Return DatabaseOptionsLoader.Load(migratorConfigPath)
    End Function

End Class
