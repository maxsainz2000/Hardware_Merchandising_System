' Merchandising.Maintenance.Users.CreateUserCommand
'
' Account bootstrap for P1-08 / ADR-005: the runtime API has no
' self-registration endpoint (spec section 9 lists no such thing, and one
' would need its own authorization rule about who may create which role),
' so the first accounts - and any account created outside the eventual
' Admin "manage users" feature - come from here, run as merch_migrator on
' the host, the same identity and connection pattern "migrate" already
' uses.

Imports Merchandising.Infrastructure.Data
Imports Merchandising.Infrastructure.Security
Imports MySqlConnector

Namespace Users

    Public NotInheritable Class CreateUserCommand

        ''' <summary>
        ''' Creates a new active account with exactly one role, inside a
        ''' single transaction. Throws <see cref="CreateUserCommandException"/>
        ''' for an unrecognised role name or a duplicate username - both
        ''' operator mistakes, not database-layer failures.
        ''' </summary>
        ''' <returns>The new user's Id.</returns>
        Public Shared Async Function RunAsync(
            connectionFactory As ConnectionFactory,
            username As String,
            password As String,
            roleName As String) As Task(Of Integer)

            If String.IsNullOrWhiteSpace(username) Then
                Throw New CreateUserCommandException("Username must not be empty.")
            End If

            If String.IsNullOrWhiteSpace(password) Then
                Throw New CreateUserCommandException("Password must not be empty.")
            End If

            Dim hasher As New PasswordHashingService()
            Dim passwordHash As String = hasher.HashPassword(password)

            Using connection As MySqlConnection = Await connectionFactory.CreateOpenConnectionAsync().ConfigureAwait(False)

                Dim roleId As Integer? = Await RoleRepository.FindRoleIdByNameAsync(connection, roleName).ConfigureAwait(False)

                If Not roleId.HasValue Then
                    Throw New CreateUserCommandException(
                        $"No role named '{roleName}'. Seeded roles (0001_foundation.sql): " &
                        "SuperAdmin, Admin, ProcurementOfficer, InventoryClerk, Cashier.")
                End If

                Dim transaction As MySqlTransaction = Await connection.BeginTransactionAsync().ConfigureAwait(False)
                Dim succeeded As Boolean = False
                Dim userId As Integer = 0
                Dim duplicateUsername As Boolean = False

                Try
                    userId = Await UserRepository.CreateAsync(
                        connection, transaction, username, passwordHash, roleId.Value).ConfigureAwait(False)

                    Await transaction.CommitAsync().ConfigureAwait(False)
                    succeeded = True

                Catch ex As MySqlException When ex.ErrorCode = MySqlErrorCode.DuplicateKeyEntry
                    duplicateUsername = True
                End Try

                If Not succeeded Then

                    Try
                        Await transaction.RollbackAsync().ConfigureAwait(False)
                    Catch
                        ' Nothing left to roll back if the INSERT itself
                        ' never took effect - not swallowing a real failure,
                        ' duplicateUsername below is what gets reported.
                    End Try

                End If

                Await transaction.DisposeAsync().ConfigureAwait(False)

                If duplicateUsername Then
                    Throw New CreateUserCommandException($"A user named '{username}' already exists.")
                End If

                Return userId

            End Using

        End Function

    End Class

End Namespace
