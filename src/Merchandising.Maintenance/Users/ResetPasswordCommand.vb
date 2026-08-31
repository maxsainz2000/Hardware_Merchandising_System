' Merchandising.Maintenance.Users.ResetPasswordCommand
'
' P6-12 / ADR-028: half of account recovery on the Maintenance CLI - a
' forgotten password has no route at all otherwise (ADR-017 section 4 puts
' all user management here, never over HTTP). Runs as merch_migrator, the
' same identity create-user already uses.
'
' Hashes through the SAME Merchandising.Infrastructure.Security.
' PasswordHashingService create-user uses - never a second hashing
' implementation, and never a plaintext column. The operator (the person
' running this command) must themselves be a known account, resolved to an
' Id so the AuditLogs row names a real actor - the same actor-resolution
' shape SeedDemoCommand already uses for its own "who caused this movement"
' requirement.

Imports Merchandising.Domain.Entities
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Infrastructure.Security
Imports System.Runtime.ExceptionServices
Imports MySqlConnector

Namespace Users

    Public NotInheritable Class ResetPasswordCommand

        ''' <summary>
        ''' Sets a new password for <paramref name="targetUsername"/>, inside a
        ''' single transaction with the AuditLogs row that names the operator
        ''' and the target - an out-of-band credential change that left no
        ''' trace would be worse than no feature at all.
        ''' </summary>
        ''' <returns>The target account's Id.</returns>
        Public Shared Async Function RunAsync(
            connectionFactory As ConnectionFactory,
            operatorUsername As String,
            targetUsername As String,
            newPassword As String) As Task(Of Integer)

            If String.IsNullOrWhiteSpace(operatorUsername) Then
                Throw New ResetPasswordCommandException("An operator username is required - every credential change has to name who made it.")
            End If

            If String.IsNullOrWhiteSpace(targetUsername) Then
                Throw New ResetPasswordCommandException("Target username must not be empty.")
            End If

            If String.IsNullOrWhiteSpace(newPassword) Then
                Throw New ResetPasswordCommandException("New password must not be empty.")
            End If

            Dim hasher As New PasswordHashingService()
            Dim passwordHash As String = hasher.HashPassword(newPassword)
            Dim correlationId As String = Guid.NewGuid().ToString("D")

            Using connection As MySqlConnection = Await connectionFactory.CreateOpenConnectionAsync().ConfigureAwait(False)

                Dim operatorUser As User = Await UserRepository.FindByUsernameAsync(connection, operatorUsername).ConfigureAwait(False)
                If operatorUser Is Nothing Then
                    Throw New ResetPasswordCommandException($"No operator account named '{operatorUsername}'.")
                End If

                Dim targetUser As User = Await UserRepository.FindByUsernameAsync(connection, targetUsername).ConfigureAwait(False)
                If targetUser Is Nothing Then
                    Throw New ResetPasswordCommandException($"No user named '{targetUsername}'.")
                End If

                Dim transaction As MySqlTransaction = Await connection.BeginTransactionAsync().ConfigureAwait(False)

                ' VB cannot Await inside Catch or Finally (BC36943). Rollback
                ' therefore runs after the Try closes, driven by a captured
                ' failure - the same shape CreateUserCommand and
                ' SeedDemoCommand already use.
                Dim committed As Boolean = False
                Dim failure As Exception = Nothing

                Try
                    Dim affected As Integer = Await UserRepository.UpdatePasswordHashAsync(
                        connection, transaction, targetUser.Id, passwordHash).ConfigureAwait(False)

                    If affected <> 1 Then
                        Throw New ResetPasswordCommandException(
                            $"Password reset affected {affected} rows for '{targetUsername}', expected exactly 1. Nothing was committed.")
                    End If

                    Await AuditLogWriter.WriteAsync(
                        connection, operatorUser.Id, "ResetPassword", targetUser.Username, "Success", correlationId,
                        $"Password reset for '{targetUsername}' by operator '{operatorUsername}'.",
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
