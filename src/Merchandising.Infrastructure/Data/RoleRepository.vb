' Merchandising.Infrastructure.Data.RoleRepository
'
' Small shared lookups against Roles/UserRoles - factored out because both
' UserRepository (login) and SessionRepository (token validation) need the
' identical "role names for this user" query, and Roles.Id lookup by name is
' needed by the Maintenance create-user command.

Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Data

    Public NotInheritable Class RoleRepository

        ''' <summary>
        ''' Role names currently assigned to <paramref name="userId"/>, most
        ''' recently assigned first. Empty, not null, when the user has none.
        ''' </summary>
        Public Shared Async Function GetRoleNamesAsync(
            connection As MySqlConnection,
            userId As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of IReadOnlyList(Of String))

            Dim roles As New List(Of String)

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT r.Name " &
                    "FROM UserRoles ur " &
                    "JOIN Roles r ON r.Id = ur.RoleId " &
                    "WHERE ur.UserId = @userId " &
                    "ORDER BY ur.AssignedAtUtc DESC;"
                command.Parameters.AddWithValue("@userId", userId)

                Using reader As MySqlDataReader =
                    Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)

                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        roles.Add(reader.GetString(0))
                    End While

                End Using
            End Using

            Return roles

        End Function

        ''' <summary>
        ''' The Id of the role named <paramref name="roleName"/>, or Nothing
        ''' if no such role exists. Roles are seeded once by
        ''' 0001_foundation.sql (spec section 9) and are not created here.
        ''' </summary>
        Public Shared Async Function FindRoleIdByNameAsync(
            connection As MySqlConnection,
            roleName As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Integer?)

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT Id FROM Roles WHERE Name = @name;"
                command.Parameters.AddWithValue("@name", roleName)

                Dim result As Object =
                    Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False)

                If result Is Nothing OrElse result Is DBNull.Value Then
                    Return Nothing
                End If

                Return CInt(result)

            End Using

        End Function

    End Class

End Namespace
