' Merchandising.Infrastructure.Data.SessionRepository
'
' Persistence for the opaque server-side session token (ADR-005). Only a
' SHA-256 hash of the token ever reaches the database - see
' Merchandising.Api.Security.SessionTokenGenerator for where the token
' itself is created and hashed. Logout is a DELETE, not a soft-revoke
' column, which is why merch_api only needs INSERT and DELETE on this table
' (db/grants/0003_authentication-grants.sql).

Imports Merchandising.Domain.Security
Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Data

    ''' <summary>
    ''' The identity and roles a valid session token resolves to.
    ''' </summary>
    Public NotInheritable Class SessionPrincipal
        Public Property UserId As Integer
        Public Property Username As String = String.Empty
        Public Property Roles As IReadOnlyList(Of String) = Array.Empty(Of String)()
    End Class

    Public NotInheritable Class SessionRepository

        ''' <summary>
        ''' Records a new session. <paramref name="tokenHash"/> is the
        ''' SHA-256 hex digest of the token already handed to the client -
        ''' never the token itself.
        ''' </summary>
        Public Shared Async Function CreateAsync(
            connection As MySqlConnection,
            userId As Integer,
            tokenHash As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of DateTime)

            Dim expiresAtUtc As DateTime = DateTime.UtcNow.Add(AuthenticationPolicy.SessionLifetime)

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO Sessions (TokenHash, UserId, CreatedAtUtc, ExpiresAtUtc) " &
                    "VALUES (@tokenHash, @userId, UTC_TIMESTAMP(6), @expiresAtUtc);"
                command.Parameters.AddWithValue("@tokenHash", tokenHash)
                command.Parameters.AddWithValue("@userId", userId)
                command.Parameters.AddWithValue("@expiresAtUtc", expiresAtUtc)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
            End Using

            Return expiresAtUtc

        End Function

        ''' <summary>
        ''' Resolves a token hash to its owning, still-active user. Returns
        ''' Nothing for an unknown hash, an expired session, or a
        ''' deactivated account - the authentication handler treats all
        ''' three identically as "not authenticated".
        ''' </summary>
        Public Shared Async Function FindActiveByTokenHashAsync(
            connection As MySqlConnection,
            tokenHash As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of SessionPrincipal)

            Dim userId As Integer
            Dim username As String = Nothing

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT s.UserId, u.Username " &
                    "FROM Sessions s " &
                    "JOIN Users u ON u.Id = s.UserId " &
                    "WHERE s.TokenHash = @tokenHash " &
                    "  AND s.ExpiresAtUtc > UTC_TIMESTAMP(6) " &
                    "  AND u.IsActive = 1;"
                command.Parameters.AddWithValue("@tokenHash", tokenHash)

                Using reader As MySqlDataReader =
                    Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)

                    If Not Await reader.ReadAsync(cancellationToken).ConfigureAwait(False) Then
                        Return Nothing
                    End If

                    userId = reader.GetInt32(0)
                    username = reader.GetString(1)

                End Using
            End Using

            Dim roles As IReadOnlyList(Of String) =
                Await RoleRepository.GetRoleNamesAsync(connection, userId, cancellationToken).ConfigureAwait(False)

            Return New SessionPrincipal With {
                .UserId = userId,
                .Username = username,
                .Roles = roles
            }

        End Function

        ''' <summary>
        ''' Deletes the session matching a token hash, if any - logout.
        ''' Deleting a hash that does not exist is not an error: a client
        ''' calling logout twice, or with an already-expired token, must
        ''' still end up with no session, which is already true.
        ''' </summary>
        Public Shared Async Function DeleteByTokenHashAsync(
            connection As MySqlConnection,
            tokenHash As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "DELETE FROM Sessions WHERE TokenHash = @tokenHash;"
                command.Parameters.AddWithValue("@tokenHash", tokenHash)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
            End Using

        End Function

    End Class

End Namespace
