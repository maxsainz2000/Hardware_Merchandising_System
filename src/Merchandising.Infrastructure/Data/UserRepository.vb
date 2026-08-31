' Merchandising.Infrastructure.Data.UserRepository
'
' Reads and writes Users, plus the lockout state added at P1-08
' (FailedLoginAttempts, LockedUntilUtc) and the password-hash update added at
' P6-12 for the Maintenance CLI's reset-password/unlock-user commands. Every
' method takes an already-open MySqlConnection rather than owning one,
' matching MigrationRunner's shape - callers control the connection lifetime
' and any surrounding transaction.

Imports Merchandising.Domain.Entities
Imports Merchandising.Domain.Security
Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Data

    Public NotInheritable Class UserRepository

        ''' <summary>
        ''' Looks up an active-or-inactive account by username, with its
        ''' current roles. Returns Nothing if no such username exists -
        ''' callers must not distinguish "no such user" from "wrong
        ''' password" in what they tell the client (spec section 9).
        ''' </summary>
        Public Shared Async Function FindByUsernameAsync(
            connection As MySqlConnection,
            username As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of User)

            Dim user As User = Nothing

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT Id, Username, PasswordHash, IsActive, FailedLoginAttempts, LockedUntilUtc " &
                    "FROM Users WHERE Username = @username;"
                command.Parameters.AddWithValue("@username", username)

                Using reader As MySqlDataReader =
                    Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)

                    If Await reader.ReadAsync(cancellationToken).ConfigureAwait(False) Then
                        user = New User With {
                            .Id = reader.GetInt32(0),
                            .Username = reader.GetString(1),
                            .PasswordHash = reader.GetString(2),
                            .IsActive = reader.GetBoolean(3),
                            .FailedLoginAttempts = reader.GetInt32(4),
                            .LockedUntilUtc = If(reader.IsDBNull(5), CType(Nothing, DateTime?), reader.GetDateTime(5))
                        }
                    End If

                End Using
            End Using

            If user Is Nothing Then
                Return Nothing
            End If

            user.Roles = Await RoleRepository.GetRoleNamesAsync(connection, user.Id, cancellationToken).ConfigureAwait(False)

            Return user

        End Function

        ''' <summary>
        ''' Records a failed login: increments FailedLoginAttempts, and - in
        ''' the same statement - sets LockedUntilUtc when the threshold is
        ''' reached. Returns the resulting attempt count and lock time so
        ''' the caller can tell whether this call is the one that just
        ''' locked the account (for the audit row spec section 9 requires).
        ''' </summary>
        ''' <remarks>
        ''' <b>Column order in the SET list is load-bearing, not cosmetic.</b>
        ''' MariaDB evaluates a single-table UPDATE's SET assignments left to
        ''' right, and a later assignment that references a column already
        ''' reassigned earlier in the same statement sees the NEW value, not
        ''' the pre-update one - confirmed empirically on this server (P1-08:
        ''' with FailedLoginAttempts reassigned first, the CASE's own
        ''' "FailedLoginAttempts + 1" read the just-incremented value and
        ''' locked accounts one attempt early, at 4 instead of 5). Computing
        ''' LockedUntilUtc's CASE before FailedLoginAttempts is reassigned is
        ''' what makes it read the true pre-update count. Reordering these
        ''' two assignments "for readability" reintroduces that bug.
        ''' </remarks>
        Public Shared Async Function RecordFailedLoginAsync(
            connection As MySqlConnection,
            userId As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Attempts As Integer, LockedUntilUtc As DateTime?))

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "UPDATE Users " &
                    "   SET LockedUntilUtc = CASE " &
                    "           WHEN FailedLoginAttempts + 1 >= @maxAttempts THEN @lockedUntilUtc " &
                    "           ELSE LockedUntilUtc " &
                    "       END, " &
                    "       FailedLoginAttempts = FailedLoginAttempts + 1, " &
                    "       UpdatedAtUtc = UTC_TIMESTAMP(6) " &
                    " WHERE Id = @userId;"
                command.Parameters.AddWithValue("@maxAttempts", AuthenticationPolicy.MaxFailedLoginAttempts)
                command.Parameters.AddWithValue("@lockedUntilUtc", DateTime.UtcNow.Add(AuthenticationPolicy.LockoutDuration))
                command.Parameters.AddWithValue("@userId", userId)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
            End Using

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT FailedLoginAttempts, LockedUntilUtc FROM Users WHERE Id = @userId;"
                command.Parameters.AddWithValue("@userId", userId)

                Using reader As MySqlDataReader =
                    Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)

                    Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)

                    Dim attempts As Integer = reader.GetInt32(0)
                    Dim lockedUntilUtc As DateTime? =
                        If(reader.IsDBNull(1), CType(Nothing, DateTime?), reader.GetDateTime(1))

                    Return (attempts, lockedUntilUtc)

                End Using
            End Using

        End Function

        ''' <summary>
        ''' Resets lockout state after a successful login, or (P6-12) as the
        ''' effect of the Maintenance CLI's <c>unlock-user</c> command.
        ''' </summary>
        ''' <param name="transaction">
        ''' Optional, added at P6-12 so <c>UnlockUserCommand</c> can commit
        ''' this update and its AuditLogs row atomically - the same reason
        ''' <see cref="AuditLogWriter.WriteAsync"/> carries the identical
        ''' parameter (P1-11). Nothing (the default) keeps every pre-existing
        ''' call site - AuthService's post-login reset - unchanged: it runs
        ''' outside any explicit transaction, exactly as before this
        ''' parameter existed.
        ''' </param>
        Public Shared Async Function RecordSuccessfulLoginAsync(
            connection As MySqlConnection,
            userId As Integer,
            Optional cancellationToken As CancellationToken = Nothing,
            Optional transaction As MySqlTransaction = Nothing) As Task

            Using command As MySqlCommand = connection.CreateCommand()
                If transaction IsNot Nothing Then
                    command.Transaction = transaction
                End If
                command.CommandText =
                    "UPDATE Users " &
                    "   SET FailedLoginAttempts = 0, " &
                    "       LockedUntilUtc = NULL, " &
                    "       UpdatedAtUtc = UTC_TIMESTAMP(6) " &
                    " WHERE Id = @userId;"
                command.Parameters.AddWithValue("@userId", userId)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
            End Using

        End Function

        ''' <summary>
        ''' Sets a new password hash for an existing account - the Maintenance
        ''' CLI's <c>reset-password</c> command (P6-12). The caller hashes the
        ''' new password through the same <c>PasswordHashingService</c>
        ''' <c>create-user</c> already uses; this method only persists the
        ''' result. Runs inside <paramref name="transaction"/> so the update
        ''' and the caller's AuditLogs row commit or roll back together.
        ''' </summary>
        ''' <returns>The number of rows affected - callers verify this is 1.</returns>
        Public Shared Async Function UpdatePasswordHashAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            userId As Integer,
            passwordHash As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Integer)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "UPDATE Users " &
                    "   SET PasswordHash = @passwordHash, " &
                    "       UpdatedAtUtc = UTC_TIMESTAMP(6) " &
                    " WHERE Id = @userId;"
                command.Parameters.AddWithValue("@passwordHash", passwordHash)
                command.Parameters.AddWithValue("@userId", userId)

                Return Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
            End Using

        End Function

        ''' <summary>
        ''' Creates a new account with one role assigned, inside the given
        ''' transaction. Used by Merchandising.Maintenance's create-user
        ''' command (merch_migrator identity) - merch_api never gets INSERT
        ''' on UserRoles beyond what 0002_post-migration-grants.sql already
        ''' allows for its own role-assignment feature, which does not exist
        ''' yet.
        ''' </summary>
        ''' <returns>The new user's Id.</returns>
        Public Shared Async Function CreateAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            username As String,
            passwordHash As String,
            roleId As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Integer)

            Dim userId As Integer

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO Users (Username, PasswordHash, IsActive, FailedLoginAttempts, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@username, @passwordHash, 1, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@username", username)
                command.Parameters.AddWithValue("@passwordHash", passwordHash)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                userId = CInt(command.LastInsertedId)
            End Using

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO UserRoles (UserId, RoleId, AssignedAtUtc, AssignedByUserId) " &
                    "VALUES (@userId, @roleId, UTC_TIMESTAMP(6), NULL);"
                command.Parameters.AddWithValue("@userId", userId)
                command.Parameters.AddWithValue("@roleId", roleId)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
            End Using

            Return userId

        End Function

    End Class

End Namespace
