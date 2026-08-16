' Merchandising.Infrastructure.Data.ConnectionFactory
'
' Opens MySqlConnector connections to the pinned MariaDB instance (ADR-002)
' and pins two session-level settings on every single connection it hands
' out, belt-and-braces alongside the server-side my.ini change from P1-04:
'
'   - sql_mode gains STRICT_TRANS_TABLES, so a future XAMPP reinstall or a
'     reverted my.ini cannot silently bring back the truncation/rounding
'     behaviour demonstrated at P0-07 (ADR-003.2). Appended via CONCAT
'     rather than overwritten, so whatever modes the server already has
'     stay in effect; duplicate mode names in the list are harmless.
'   - tx_isolation is set to READ COMMITTED explicitly (ADR-006). The
'     server default on MariaDB 10.4 is REPEATABLE-READ, and the variable
'     name is `tx_isolation`, not `transaction_isolation` - the latter
'     does not exist on 10.4 and raises ERROR 1193 (CLAUDE.md section 6.2).

Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Data

    ''' <summary>
    ''' Creates open, session-configured connections to the MariaDB
    ''' database. Server-side only - never referenced from a client
    ''' project (CLAUDE.md section 4, guardrail G-B).
    ''' </summary>
    Public NotInheritable Class ConnectionFactory

        Private ReadOnly _connectionString As String

        ''' <summary>
        ''' Builds a factory bound to the given options. The connection
        ''' string is assembled in memory only and is never logged or
        ''' persisted - see <see cref="MaskedConnectionSummary"/> for a
        ''' safe-to-log alternative.
        ''' </summary>
        Public Sub New(options As DatabaseOptions)

            If options Is Nothing Then
                Throw New ArgumentNullException(NameOf(options))
            End If

            Dim builder As New MySqlConnectionStringBuilder With {
                .Server = options.Host,
                .Port = CUInt(options.Port),
                .Database = options.Database,
                .UserID = options.UserId,
                .Password = options.Password
            }

            _connectionString = builder.ConnectionString

        End Sub

        ''' <summary>
        ''' A connection summary with the credential redacted, safe to write
        ''' to a log or evidence file.
        ''' </summary>
        Public ReadOnly Property MaskedConnectionSummary As String
            Get
                Dim builder As New MySqlConnectionStringBuilder(_connectionString)
                Return $"Server={builder.Server};Port={builder.Port};Database={builder.Database};UserID={builder.UserID};Password=<redacted>"
            End Get
        End Property

        ''' <summary>
        ''' Opens a new connection and applies the two session-level
        ''' settings described above. Callers own disposal.
        ''' </summary>
        Public Async Function CreateOpenConnectionAsync(
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of MySqlConnection)

            Dim connection As New MySqlConnection(_connectionString)
            Dim succeeded As Boolean = False

            Try
                Await connection.OpenAsync(cancellationToken).ConfigureAwait(False)

                Using command As MySqlCommand = connection.CreateCommand()
                    command.CommandText =
                        "SET SESSION sql_mode = CONCAT(@@SESSION.sql_mode, ',STRICT_TRANS_TABLES'); " &
                        "SET SESSION tx_isolation = 'READ-COMMITTED';"
                    Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                End Using

                succeeded = True
                Return connection

            Finally
                ' The connection would otherwise leak if session setup fails
                ' after a successful Open(). VB does not allow Await inside
                ' Finally, so disposal here is the synchronous Dispose(),
                ' not DisposeAsync() - correct because it only ever runs on
                ' the failure path, before the connection is handed to a
                ' caller who might be using it concurrently.
                If Not succeeded Then
                    connection.Dispose()
                End If
            End Try

        End Function

    End Class

End Namespace
