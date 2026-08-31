' Merchandising.Maintenance.Users.UnlockUserCommand
'
' P6-12 / ADR-028: the other half of account recovery on the Maintenance CLI.
' Lockout already self-recovers after AuthenticationPolicy.LockoutDuration
' (15 minutes), but nothing before this card could clear it early - an
' operator watching a classmate lock themselves out on demo day had to wait
' out the timer. Runs as merch_migrator, the same identity create-user and
' reset-password already use.
'
' Distinct from ResetPasswordCommand because the two failures are different:
' a locked-out account still has a perfectly good password, and a forgotten
' password does not by itself imply the account is locked. Reuses
' UserRepository.RecordSuccessfulLoginAsync's existing clear-lockout UPDATE
' rather than a second copy of the same SQL.

Imports Merchandising.Domain.Entities
Imports Merchandising.Infrastructure.Data
Imports System.Globalization
Imports System.Runtime.ExceptionServices
Imports MySqlConnector

Namespace Users

    Public NotInheritable Class UnlockUserCommand

        ''' <summary>
        ''' Clears <paramref name="targetUsername"/>'s lockout state, inside a
        ''' single transaction with the AuditLogs row that names the operator
        ''' and the target.
        ''' </summary>
        ''' <returns>The target account's Id.</returns>
        Public Shared Async Function RunAsync(
            connectionFactory As ConnectionFactory,
            operatorUsername As String,
            targetUsername As String) As Task(Of Integer)

            If String.IsNullOrWhiteSpace(operatorUsername) Then
                Throw New UnlockUserCommandException("An operator username is required - every account change has to name who made it.")
            End If

            If String.IsNullOrWhiteSpace(targetUsername) Then
                Throw New UnlockUserCommandException("Target username must not be empty.")
            End If

            Dim correlationId As String = Guid.NewGuid().ToString("D")

            Using connection As MySqlConnection = Await connectionFactory.CreateOpenConnectionAsync().ConfigureAwait(False)

                Dim operatorUser As User = Await UserRepository.FindByUsernameAsync(connection, operatorUsername).ConfigureAwait(False)
                If operatorUser Is Nothing Then
                    Throw New UnlockUserCommandException($"No operator account named '{operatorUsername}'.")
                End If

                Dim targetUser As User = Await UserRepository.FindByUsernameAsync(connection, targetUsername).ConfigureAwait(False)
                If targetUser Is Nothing Then
                    Throw New UnlockUserCommandException($"No user named '{targetUsername}'.")
                End If

                Dim priorState As String =
                    If(targetUser.LockedUntilUtc.HasValue,
                       String.Format(CultureInfo.InvariantCulture, "was locked until {0:O}", targetUser.LockedUntilUtc.Value),
                       "was not locked")

                Dim transaction As MySqlTransaction = Await connection.BeginTransactionAsync().ConfigureAwait(False)

                ' VB cannot Await inside Catch or Finally (BC36943). Rollback
                ' therefore runs after the Try closes, driven by a captured
                ' failure - the same shape CreateUserCommand and
                ' SeedDemoCommand already use.
                Dim committed As Boolean = False
                Dim failure As Exception = Nothing

                Try
                    Await UserRepository.RecordSuccessfulLoginAsync(
                        connection, targetUser.Id, transaction:=transaction).ConfigureAwait(False)

                    Await AuditLogWriter.WriteAsync(
                        connection, operatorUser.Id, "UnlockUser", targetUser.Username, "Success", correlationId,
                        String.Format(CultureInfo.InvariantCulture,
                                      "Account unlocked for '{0}' by operator '{1}' ({2}, {3} failed attempts).",
                                      targetUsername, operatorUsername, priorState, targetUser.FailedLoginAttempts),
                        transaction:=transaction).ConfigureAwait(False)

                    Await transaction.CommitAsync().ConfigureAwait(False)
                    committed = True

                Catch ex As Exception
                    failure = ex
                End Try

                If Not committed Then
                    Try
                        Await transaction.RollbackAsync().ConfigureAwait(False)
                    Catch
                        ' Nothing left to roll back if nothing took effect -
                        ' not swallowing a real failure, failure below is
                        ' what gets reported.
                    End Try
                End If

                Await transaction.DisposeAsync().ConfigureAwait(False)

                If failure IsNot Nothing Then
                    ExceptionDispatchInfo.Capture(failure).Throw()
                End If

                Return targetUser.Id

            End Using

        End Function

    End Class

End Namespace
