Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Json
Imports System.Text
Imports System.Threading.Tasks
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Auth
Imports Merchandising.Contracts.Maintenance
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Backup
Imports Merchandising.Maintenance.Restore
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

''' <summary>
''' P1-18 evidence: the maintenance lock (spec section 15 steps 1, 2 and 7)
''' exercised through the real HTTP pipeline, not by calling a service
''' directly.
''' </summary>
''' <remarks>
''' <para>
''' Going through <see cref="MerchandisingApiFactory"/> is deliberate. The
''' rule under test is "the API rejects ordinary writes while the lock is
''' held", and that is a property of the PIPELINE - middleware order,
''' routing, the error envelope - not of any one service. P1-10 already
''' found a defect that existed only at that layer and that no
''' service-level test could have seen.
''' </para>
''' <para>
''' The lock is acquired by direct INSERT rather than through the endpoint in
''' the tests that check enforcement, so that enforcement is proven
''' independently of the acquire endpoint working. A bug in acquire would
''' otherwise make the enforcement tests pass vacuously.
''' </para>
''' </remarks>
<TestClass>
Public Class MaintenanceModeTests

    Private Const SuperAdminFixtureUsername As String = "p1_18_fixture_superadmin"
    Private Const FixturePassword As String = "P1-18 Fixture Passw0rd!"

    Private _factory As MerchandisingApiFactory
    Private _connectionFactory As ConnectionFactory

    <TestInitialize>
    Public Async Function SetUpAsync() As Task
        _factory = New MerchandisingApiFactory()
        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        Await ReleaseAnyActiveLockAsync()
    End Function

    <TestCleanup>
    Public Sub TearDown()
        ReleaseAnyActiveLockAsync().GetAwaiter().GetResult()
        _factory?.Dispose()
    End Sub

    ''' <summary>
    ''' The core rule. An ordinary write must be refused while the lock is
    ''' held - spec section 15 step 2, "rejects new ordinary writes".
    ''' </summary>
    <TestMethod>
    Public Async Function MaintenanceLock_Held_OrdinaryWriteIsRejected() As Task

        Await AcquireLockDirectlyAsync("P1-18 enforcement test")

        Using client As HttpClient = _factory.CreateClient()

            Dim payload = New With {
                .productId = 1,
                .quantity = 1D,
                .reason = "P1-18 maintenance enforcement probe",
                .idempotencyKey = Guid.NewGuid().ToString("d")
            }

            Using response As HttpResponseMessage =
                Await client.PostAsJsonAsync("/api/v1/inventory/stock/decrement", payload)

                Dim raw As String = Await response.Content.ReadAsStringAsync()

                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode,
                    "An ordinary write was not refused while the maintenance lock was held. Body: " & raw)

                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("MAINTENANCE_MODE", body.ErrorCode)
                Assert.IsFalse(String.IsNullOrWhiteSpace(body.CorrelationId),
                    "The maintenance response carried no correlation ID.")

                ' Spec section 15 step 2 says the client is warned. A client
                ' cannot warn anyone if the reason never reaches it.
                StringAssert.Contains(body.Message, "maintenance",
                    "The maintenance response does not say the system is in maintenance.")
            End Using

        End Using

    End Function

    ''' <summary>
    ''' Reads must keep working. Maintenance mode stops writes so a restore
    ''' can proceed safely; blocking reads as well would take the whole system
    ''' down for no additional safety, and spec section 15 says "ordinary
    ''' writes", not "all traffic".
    ''' </summary>
    <TestMethod>
    Public Async Function MaintenanceLock_Held_ReadsStillSucceed() As Task

        Await AcquireLockDirectlyAsync("P1-18 read-through test")

        Using client As HttpClient = _factory.CreateClient()
            Using response As HttpResponseMessage = Await client.GetAsync("/health")
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
                    "A read was blocked while the maintenance lock was held.")
            End Using
        End Using

    End Function

    ''' <summary>
    ''' With no lock held, the same write must reach the business rule. Without
    ''' this the enforcement test above would pass just as happily against an
    ''' endpoint that was broken for every request.
    ''' </summary>
    <TestMethod>
    Public Async Function NoMaintenanceLock_OrdinaryWriteReachesTheBusinessRule() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim payload = New With {
                .productId = 1,
                .quantity = 1D,
                .reason = "P1-18 no-lock control probe",
                .idempotencyKey = Guid.NewGuid().ToString("d")
            }

            Using response As HttpResponseMessage =
                Await client.PostAsJsonAsync("/api/v1/inventory/stock/decrement", payload)

                ' 401 is the expected answer for an unauthenticated caller.
                ' What matters is that it is NOT 503: the request got past the
                ' maintenance gate and was judged on its own merits.
                Assert.AreNotEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode,
                    "A write was refused as maintenance when no lock was held.")
            End Using

        End Using

    End Function

    ''' <summary>
    ''' Spec section 15 is explicit that restore is NOT executed as a normal
    ''' request inside a live API that depends on the database. This is a
    ''' design constraint, so it is proven rather than asserted - the same way
    ''' P1-17 proved the backup directory unreachable.
    ''' </summary>
    <TestMethod>
    Public Async Function Api_ExposesNoRestoreEndpoint() As Task

        Dim candidatePaths As String() = {
            "/api/v1/admin/restore",
            "/api/v1/maintenance/restore",
            "/api/v1/restore",
            "/restore",
            "/api/v1/admin/maintenance/restore"
        }

        Using client As HttpClient = _factory.CreateClient()
            For Each path As String In candidatePaths

                Using getResponse As HttpResponseMessage = Await client.GetAsync(path)
                    Assert.AreEqual(HttpStatusCode.NotFound, getResponse.StatusCode,
                        $"GET {path} resolved to something. No restore endpoint may exist.")
                End Using

                Using postResponse As HttpResponseMessage =
                    Await client.PostAsJsonAsync(path, New With {.confirm = True})

                    Assert.AreEqual(HttpStatusCode.NotFound, postResponse.StatusCode,
                        $"POST {path} resolved to something. No restore endpoint may exist.")
                End Using

            Next
        End Using

    End Function

    ''' <summary>
    ''' At most one active lock, refused by the DATABASE rather than by an
    ''' application check. A read-then-write "is one already held?" test would
    ''' let two simultaneous requests both succeed - the same race ADR-006
    ''' rejects for stock balances.
    ''' </summary>
    <TestMethod>
    Public Async Function SecondConcurrentLock_IsRefusedByTheDatabase() As Task

        Await AcquireLockDirectlyAsync("P1-18 first lock")

        Dim duplicateRefused As Boolean = False

        Try
            Await AcquireLockDirectlyAsync("P1-18 second lock - must be refused")
        Catch ex As MySqlException When ex.Number = 1062
            duplicateRefused = True
        End Try

        Assert.IsTrue(duplicateRefused,
            "A second active maintenance lock was accepted. The unique index on the generated " &
            "IsActive column is not doing its job.")

    End Function

    ''' <summary>Repository layer on its own, independent of the HTTP pipeline. Kept because it is what localised the CHAR(36)/Guid defect: with only pipeline tests, the fault presented as an opaque 500.</summary>
    <TestMethod>
    Public Async Function Repository_ReadsBackTheActiveLock() As Task

        Await AcquireLockDirectlyAsync("P1-18 repository read-back")

        Dim repository As New MaintenanceLockRepository(_connectionFactory)
        Dim activeLock As MaintenanceLock = Await repository.GetActiveAsync()

        Assert.IsNotNull(activeLock, "Repository returned no active lock though one was inserted.")
        Assert.AreEqual("P1-18 repository read-back", activeLock.Reason)

    End Function

    ''' <summary>
    ''' Spec section 15 step 7: maintenance mode is released ONLY after
    ''' verification succeeds. A release that admits verification did not pass
    ''' must be refused, and the lock must still be held afterwards.
    ''' </summary>
    ''' <remarks>
    ''' This is the rule most worth testing through the full pipeline rather
    ''' than against the repository, because the refusal lives in the
    ''' controller. A repository-level test would happily release the lock and
    ''' prove nothing about what the API does with a False.
    ''' </remarks>
    <TestMethod>
    Public Async Function Release_WithoutPassingVerification_IsRefusedAndLockStaysHeld() As Task

        Await EnsureSuperAdminFixtureAsync()

        Using client As HttpClient = _factory.CreateClient()

            Await AuthenticateAsync(client)

            Using enterResponse As HttpResponseMessage =
                Await client.PostAsJsonAsync("/api/v1/admin/maintenance/enter",
                                             New EnterMaintenanceRequest With {.Reason = "P1-18 release-gate test"})
                Assert.AreEqual(HttpStatusCode.OK, enterResponse.StatusCode,
                    "Could not enter maintenance mode. Body: " & Await enterResponse.Content.ReadAsStringAsync())
            End Using

            ' The refusal.
            Using refused As HttpResponseMessage =
                Await client.PostAsJsonAsync("/api/v1/admin/maintenance/release",
                                             New ReleaseMaintenanceRequest With {.VerificationPassed = False,
                                                                                 .Detail = "Deliberately unverified."})

                Assert.AreEqual(HttpStatusCode.Conflict, refused.StatusCode,
                    "A release without passing verification was accepted.")

                Dim body As ApiErrorResponse = Await refused.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("VERIFICATION_NOT_CONFIRMED", body.ErrorCode)
            End Using

            ' The lock must still be held - a refused release that quietly
            ' released anyway would be the worst of both outcomes.
            Dim stillHeld As MaintenanceLock = Await New MaintenanceLockRepository(_connectionFactory).GetActiveAsync()
            Assert.IsNotNull(stillHeld, "The lock was released despite the release being refused.")

            ' And the honest release works.
            Using released As HttpResponseMessage =
                Await client.PostAsJsonAsync("/api/v1/admin/maintenance/release",
                                             New ReleaseMaintenanceRequest With {.VerificationPassed = True,
                                                                                 .Detail = "P1-18 test verified."})
                Assert.AreEqual(HttpStatusCode.OK, released.StatusCode,
                    "A verified release was refused. Body: " & Await released.Content.ReadAsStringAsync())
            End Using

            Dim afterRelease As MaintenanceLock = Await New MaintenanceLockRepository(_connectionFactory).GetActiveAsync()
            Assert.IsNull(afterRelease, "The lock was still held after a verified release.")

        End Using

    End Function

    ''' <summary>
    ''' The status endpoint must answer while the lock is held - a client that
    ''' cannot ask "are you in maintenance?" during maintenance cannot display
    ''' the warning spec section 15 step 2 requires.
    ''' </summary>
    <TestMethod>
    Public Async Function Status_IsReadableDuringMaintenance_AndCarriesTheReason() As Task

        Await AcquireLockDirectlyAsync("P1-18 status readability")

        Using client As HttpClient = _factory.CreateClient()
            Using response As HttpResponseMessage = Await client.GetAsync("/api/v1/admin/maintenance/status")

                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
                    "The maintenance status endpoint was not readable during maintenance.")

                Dim status As MaintenanceStatusResponse =
                    Await response.Content.ReadFromJsonAsync(Of MaintenanceStatusResponse)()

                Assert.IsTrue(status.InMaintenance)
                Assert.AreEqual("P1-18 status readability", status.Reason)
                Assert.IsFalse(String.IsNullOrWhiteSpace(status.Message),
                    "No client-facing message was returned; clients would have nothing to display.")
            End Using
        End Using

    End Function

    ''' <summary>
    ''' P6-08: spec section 15's seven-step procedure, exercised through the
    ''' REAL workflow rather than asserted piecemeal - a Super Admin enters
    ''' maintenance (steps 1-2), the operator runs the maintenance utility
    ''' (steps 4-6, a real timed backup and restore into the isolated
    ''' <c>merchandising_restoretest</c> schema - the restore mechanism
    ''' itself, including the RTO measurement, is <see cref="RestoreCommandTests"/>'s
    ''' job, not re-proven here), and the API records the completed restore
    ''' event and releases maintenance only after verification (step 7).
    ''' </summary>
    ''' <remarks>
    ''' "Recorded" means findable afterwards, not merely accepted: this test
    ''' reads the release detail back out of <c>MaintenanceLocks</c> rather
    ''' than trusting the 200 OK the release call returned.
    ''' </remarks>
    <TestMethod>
    Public Async Function Restore_ThroughRealMaintenanceWorkflow_RecordsCompletedRestoreEvent() As Task

        Await EnsureSuperAdminFixtureAsync()

        Dim reason As String = "P6-08 restore rehearsal " & Guid.NewGuid().ToString("n").Substring(0, 8)
        Dim workDirectory As String = Path.Combine(Path.GetTempPath(), "merch-restore-workflow-" & Guid.NewGuid().ToString("n"))
        Directory.CreateDirectory(workDirectory)

        Try

            Using client As HttpClient = _factory.CreateClient()

                Await AuthenticateAsync(client)

                ' Steps 1-2: a Super Admin requests maintenance mode through
                ' the authorized administrative workflow, with a reason.
                Using enterResponse As HttpResponseMessage =
                    Await client.PostAsJsonAsync("/api/v1/admin/maintenance/enter",
                                                 New EnterMaintenanceRequest With {.Reason = reason})
                    Assert.AreEqual(HttpStatusCode.OK, enterResponse.StatusCode,
                        "Could not enter maintenance mode. Body: " & Await enterResponse.Content.ReadAsStringAsync())
                End Using

                ' Steps 4-6: the operator runs the maintenance utility with a
                ' real backup file - a real timed backup and restore into the
                ' isolated scratch schema, the same mechanism
                ' RestoreCommandTests proves end to end.
                Dim restoreResult As RestoreResult = Await RunBackupThenRestoreIntoScratchAsync(workDirectory)

                Assert.IsTrue(restoreResult.Succeeded,
                    $"The restore run during this maintenance window did not succeed. Detail: {restoreResult.Detail}")

                ' Step 7: the completed restore event is recorded, and
                ' maintenance is released only after verification succeeds.
                Dim releaseDetail As String =
                    $"P6-08 restore rehearsal verified: elapsed={restoreResult.ElapsedSeconds:F1}s " &
                    $"users={restoreResult.UserCount} products={restoreResult.ProductCount} " &
                    $"balances={restoreResult.StockBalanceCount} movements={restoreResult.StockMovementCount} " &
                    $"audit={restoreResult.AuditLogCount}."

                Using releaseResponse As HttpResponseMessage =
                    Await client.PostAsJsonAsync("/api/v1/admin/maintenance/release",
                                                 New ReleaseMaintenanceRequest With {.VerificationPassed = True,
                                                                                     .Detail = releaseDetail})
                    Assert.AreEqual(HttpStatusCode.OK, releaseResponse.StatusCode,
                        "A verified release was refused. Body: " & Await releaseResponse.Content.ReadAsStringAsync())
                End Using

            End Using

            ' The event is RECORDED, not merely accepted: read it back from
            ' MaintenanceLocks rather than trusting the 200 OK above.
            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
                Using command As MySqlCommand = connection.CreateCommand()
                    command.CommandText = "SELECT Detail FROM MaintenanceLocks WHERE Reason = @reason ORDER BY Id DESC LIMIT 1;"
                    command.Parameters.AddWithValue("@reason", reason)
                    Dim recordedDetail As String = CStr(Await command.ExecuteScalarAsync())
                    StringAssert.Contains(recordedDetail, "elapsed=",
                        "The completed restore event was not recorded in the release detail.")
                End Using
            End Using

        Finally
            If Directory.Exists(workDirectory) Then
                Directory.Delete(workDirectory, recursive:=True)
            End If
        End Try

    End Function

    ''' <summary>
    ''' Runs one real backup of the live database and restores it into the
    ''' isolated <c>merchandising_restoretest</c> schema - never the live one.
    ''' See <see cref="RestoreCommandTests"/>'s class remarks for why a
    ''' filtered copy is required: a <c>--databases</c> dump's own
    ''' <c>CREATE DATABASE</c>/<c>USE</c> lines override any <c>--database=</c>
    ''' redirect otherwise.
    ''' </summary>
    Private Async Function RunBackupThenRestoreIntoScratchAsync(workDirectory As String) As Task(Of RestoreResult)

        Const ScratchDatabaseName As String = "merchandising_restoretest"
        Const MysqlDumpPath As String = "C:\xampp\mysql\bin\mysqldump.exe"
        Const MysqlClientPath As String = "C:\xampp\mysql\bin\mysql.exe"

        Dim backupOptions As DatabaseOptions = DatabaseOptionsLoader.Load(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MerchandisingSystem", "config", "database.backup.json"))

        Dim backupSettings As New BackupSettings With {.Directory = workDirectory, .RetentionCount = 99}

        Dim backupResult As BackupResult =
            Await BackupCommand.ExecuteAsync(backupOptions, backupSettings, MysqlDumpPath, Guid.NewGuid().ToString("d"))

        If backupResult.Outcome = BackupOutcome.Failed Then
            Assert.Fail($"The backup this workflow test depends on failed: {backupResult.Detail}")
        End If

        Dim filteredDumpPath As String = Path.Combine(workDirectory, "filtered-" & Path.GetFileName(backupResult.FilePath))

        Using reader As New StreamReader(backupResult.FilePath)
            Using writer As New StreamWriter(filteredDumpPath, append:=False, New UTF8Encoding(encoderShouldEmitUTF8Identifier:=False))
                Dim line As String = reader.ReadLine()
                While line IsNot Nothing
                    If Not (line.StartsWith("CREATE DATABASE", StringComparison.OrdinalIgnoreCase) OrElse
                            line.StartsWith("USE ", StringComparison.OrdinalIgnoreCase)) Then
                        writer.WriteLine(line)
                    End If
                    line = reader.ReadLine()
                End While
            End Using
        End Using

        Dim migratorOptions As DatabaseOptions = DatabaseOptionsLoader.Load(
            Path.Combine(Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), "database.migrator.json"))

        Return Await RestoreCommand.ExecuteAsync(
            MysqlClientPath, migratorOptions, filteredDumpPath,
            targetDatabase:=ScratchDatabaseName, logDirectory:=workDirectory)

    End Function

    Private Async Function EnsureSuperAdminFixtureAsync() As Task

        Try
            Dim migratorConfig As String = Path.Combine(
                Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), "database.migrator.json")
            Dim migratorFactory As New ConnectionFactory(DatabaseOptionsLoader.Load(migratorConfig))
            Await CreateUserCommand.RunAsync(migratorFactory, SuperAdminFixtureUsername, FixturePassword, "SuperAdmin")
        Catch ex As CreateUserCommandException
            ' Already exists from a previous run. Fixture accounts are
            ' permanent rows, not scratch data - AuditLogs.ActorUserId is a
            ' foreign key to Users and AuditLogs is append-only, so deleting a
            ' fixture user would mean deleting audit rows.
        End Try

    End Function

    Private Async Function AuthenticateAsync(client As HttpClient) As Task

        Using response As HttpResponseMessage =
            Await client.PostAsJsonAsync("/api/v1/auth/login",
                                         New LoginRequest With {.Username = SuperAdminFixtureUsername,
                                                                .Password = FixturePassword})

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
                "Fixture SuperAdmin could not log in. Body: " & Await response.Content.ReadAsStringAsync())

            Dim login As LoginResponse = Await response.Content.ReadFromJsonAsync(Of LoginResponse)()
            client.DefaultRequestHeaders.Add("Authorization", "Bearer " & login.Token)

        End Using

    End Function

    ' ---------------------------------------------------------------- helpers

    Private Async Function AcquireLockDirectlyAsync(reason As String) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO MaintenanceLocks (Reason, RequestedByUserId, AcquiredAtUtc, CorrelationId) " &
                    "VALUES (@reason, (SELECT MIN(Id) FROM Users), UTC_TIMESTAMP(6), @correlationId);"
                command.Parameters.AddWithValue("@reason", reason)
                command.Parameters.AddWithValue("@correlationId", Guid.NewGuid().ToString("d"))
                Await command.ExecuteNonQueryAsync()
            End Using
        End Using

    End Function

    ''' <summary>
    ''' Releases rather than deletes. merch_api holds no DELETE on this table
    ''' by design (db/grants/0005), and a test that deleted its own lock rows
    ''' would be erasing the record that maintenance happened - which is
    ''' exactly what the missing DELETE grant exists to prevent.
    ''' </summary>
    Private Async Function ReleaseAnyActiveLockAsync() As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "UPDATE MaintenanceLocks " &
                    "SET ReleasedAtUtc = UTC_TIMESTAMP(6), VerificationPassed = 1, " &
                    "    Detail = CONCAT(COALESCE(Detail,''), ' [released by P1-18 test teardown]') " &
                    "WHERE ReleasedAtUtc IS NULL;"
                Await command.ExecuteNonQueryAsync()
            End Using
        End Using

    End Function

End Class
