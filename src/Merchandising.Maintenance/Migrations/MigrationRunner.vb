' Merchandising.Maintenance.Migrations.MigrationRunner
'
' The mechanism half of ADR-008. Discovers db/migrations/NNNN_*.sql files,
' refuses to proceed if any file already recorded as successfully applied
' has a changed checksum, then applies the remaining files in order - each
' inside its own transaction - and records identifier/checksum/timestamp/
' result in SchemaMigrations.
'
' MUST be constructed with a ConnectionFactory built from
' database.migrator.json (merch_migrator), never database.json (merch_api).
' merch_api holds no DDL privilege at all (ADR-013), so pointing this at the
' API account fails loudly on the first CREATE TABLE rather than silently
' doing nothing - proven by MigrationRunnerTests.
'
' SchemaMigrations is bootstrapped here (CREATE TABLE IF NOT EXISTS), not
' by a numbered migration file - the runner needs it to exist before it can
' even ask "has migration 0001 been applied yet?", which is a chicken/egg
' problem no ordinary migration file can resolve. P1-07's own migration
' list therefore must not try to CREATE this table again without IF NOT
' EXISTS.
'
' Known MariaDB/InnoDB limitation, not fixable here: DDL statements
' (CREATE TABLE, ALTER TABLE, ...) cause an implicit commit and do not
' participate in transaction rollback. A migration file that mixes DDL and
' DML and fails partway through will NOT have its DDL undone by the
' transaction wrapper below - only the DML portion is genuinely
' rollback-safe. This is a property of the database engine, not of this
' runner; see MigrationRunnerTests.FailingMigration_RollsBackAndRecordsFailure
' for a test built around the guarantee that actually holds (pure DML).

Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Tasks
Imports Merchandising.Infrastructure.Data
Imports MySqlConnector

Namespace Migrations

    Public NotInheritable Class MigrationRunner

        Private ReadOnly _connectionFactory As ConnectionFactory

        Public Sub New(connectionFactory As ConnectionFactory)

            If connectionFactory Is Nothing Then
                Throw New ArgumentNullException(NameOf(connectionFactory))
            End If

            _connectionFactory = connectionFactory

        End Sub

        ''' <summary>
        ''' Discovers and applies every unapplied migration under
        ''' <paramref name="migrationsDirectory"/>, in order.
        ''' </summary>
        ''' <exception cref="MigrationChecksumMismatchException">
        ''' A previously-applied file's checksum no longer matches disk. No
        ''' migration is applied this run when this is thrown.
        ''' </exception>
        ''' <exception cref="MySqlException">
        ''' The connection's account lacks a privilege the run needs (for
        ''' example merch_api lacking DDL) - propagated rather than
        ''' swallowed, so the run fails loudly instead of half-applying.
        ''' </exception>
        Public Async Function RunAsync(
            migrationsDirectory As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of MigrationRunSummary)

            Dim files As IReadOnlyList(Of MigrationFile) = MigrationDiscovery.Discover(migrationsDirectory)

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Await EnsureSchemaMigrationsTableAsync(connection, cancellationToken).ConfigureAwait(False)

                Dim recorded As Dictionary(Of String, MigrationRecord) =
                    Await LoadRecordsAsync(connection, cancellationToken).ConfigureAwait(False)

                RefuseIfAnyAppliedFileWasTampered(files, recorded)

                Dim applications As New List(Of MigrationApplication)

                For Each file As MigrationFile In files

                    Dim record As MigrationRecord = Nothing
                    Dim alreadySucceeded As Boolean =
                        recorded.TryGetValue(file.Identifier, record) AndAlso record.Succeeded

                    If alreadySucceeded Then
                        Continue For
                    End If

                    Dim application As MigrationApplication =
                        Await ApplyMigrationAsync(connection, file, cancellationToken).ConfigureAwait(False)
                    applications.Add(application)

                    If Not application.Succeeded Then
                        ' Stop at the first failure - later migrations may
                        ' depend on this one and must not be attempted out
                        ' of order against a schema that never got here.
                        Exit For
                    End If

                Next

                Return New MigrationRunSummary(applications)

            End Using

        End Function

        Private Shared Sub RefuseIfAnyAppliedFileWasTampered(
            files As IReadOnlyList(Of MigrationFile),
            recorded As Dictionary(Of String, MigrationRecord))

            For Each file As MigrationFile In files

                Dim record As MigrationRecord = Nothing

                If recorded.TryGetValue(file.Identifier, record) AndAlso record.Succeeded Then
                    If Not String.Equals(record.Checksum, file.Checksum, StringComparison.Ordinal) Then
                        Throw New MigrationChecksumMismatchException(file.Identifier, record.Checksum, file.Checksum)
                    End If
                End If

            Next

        End Sub

        Private Shared Async Function EnsureSchemaMigrationsTableAsync(
            connection As MySqlConnection, cancellationToken As CancellationToken) As Task

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "CREATE TABLE IF NOT EXISTS SchemaMigrations (" &
                    "    MigrationId VARCHAR(255) NOT NULL," &
                    "    Checksum CHAR(64) NOT NULL," &
                    "    AppliedAtUtc DATETIME(6) NOT NULL," &
                    "    Succeeded TINYINT(1) NOT NULL," &
                    "    ErrorMessage TEXT NULL," &
                    "    PRIMARY KEY (MigrationId)" &
                    ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;"
                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
            End Using

        End Function

        Private Shared Async Function LoadRecordsAsync(
            connection As MySqlConnection,
            cancellationToken As CancellationToken) As Task(Of Dictionary(Of String, MigrationRecord))

            Dim records As New Dictionary(Of String, MigrationRecord)(StringComparer.Ordinal)

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT MigrationId, Checksum, AppliedAtUtc, Succeeded, ErrorMessage FROM SchemaMigrations;"

                Using reader As MySqlDataReader =
                    Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)

                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)

                        Dim migrationId As String = reader.GetString(0)
                        Dim checksum As String = reader.GetString(1)
                        Dim appliedAtUtc As DateTime = reader.GetDateTime(2)
                        Dim succeeded As Boolean = reader.GetBoolean(3)
                        Dim errorMessage As String = If(reader.IsDBNull(4), Nothing, reader.GetString(4))

                        records(migrationId) =
                            New MigrationRecord(migrationId, checksum, appliedAtUtc, succeeded, errorMessage)

                    End While

                End Using
            End Using

            Return records

        End Function

        Private Shared Async Function ApplyMigrationAsync(
            connection As MySqlConnection,
            file As MigrationFile,
            cancellationToken As CancellationToken) As Task(Of MigrationApplication)

            ' VB does not allow Await inside Catch or Finally (CLAUDE.md
            ' section 3; see ConnectionFactory.vb for the same constraint).
            ' Rollback and disposal are therefore sequenced AFTER the
            ' Try/Catch below rather than inside it - reached unconditionally
            ' since neither branch rethrows.
            Dim transaction As MySqlTransaction =
                Await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(False)
            Dim succeeded As Boolean = False
            Dim errorMessage As String = Nothing
            Dim failed As Boolean = False

            Try
                Using command As MySqlCommand = connection.CreateCommand()
                    command.Transaction = transaction
                    command.CommandText = file.Sql
                    Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                End Using

                Await transaction.CommitAsync(cancellationToken).ConfigureAwait(False)
                succeeded = True

            Catch ex As MySqlException
                errorMessage = ex.Message
                failed = True
            End Try

            If failed Then
                Try
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                Catch rollbackEx As MySqlException
                    ' Expected when file.Sql contained DDL: MariaDB commits
                    ' DDL implicitly, which can leave nothing left for this
                    ' rollback to undo. The original failure captured above
                    ' is what gets recorded - this is not swallowing it.
                End Try
            End If

            Await transaction.DisposeAsync().ConfigureAwait(False)

            Await RecordResultAsync(connection, file, succeeded, errorMessage, cancellationToken).ConfigureAwait(False)

            Return New MigrationApplication(file.Identifier, succeeded, errorMessage)

        End Function

        Private Shared Async Function RecordResultAsync(
            connection As MySqlConnection,
            file As MigrationFile,
            succeeded As Boolean,
            errorMessage As String,
            cancellationToken As CancellationToken) As Task

            Using command As MySqlCommand = connection.CreateCommand()

                command.CommandText =
                    "INSERT INTO SchemaMigrations (MigrationId, Checksum, AppliedAtUtc, Succeeded, ErrorMessage) " &
                    "VALUES (@migrationId, @checksum, UTC_TIMESTAMP(6), @succeeded, @errorMessage) " &
                    "ON DUPLICATE KEY UPDATE " &
                    "    Checksum = VALUES(Checksum), " &
                    "    AppliedAtUtc = VALUES(AppliedAtUtc), " &
                    "    Succeeded = VALUES(Succeeded), " &
                    "    ErrorMessage = VALUES(ErrorMessage);"

                command.Parameters.AddWithValue("@migrationId", file.Identifier)
                command.Parameters.AddWithValue("@checksum", file.Checksum)
                command.Parameters.AddWithValue("@succeeded", succeeded)
                command.Parameters.AddWithValue("@errorMessage", If(CObj(errorMessage), DBNull.Value))

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)

            End Using

        End Function

    End Class

End Namespace
