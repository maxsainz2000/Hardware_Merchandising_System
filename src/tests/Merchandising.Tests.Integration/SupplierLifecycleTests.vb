' Merchandising.Tests.Integration.SupplierLifecycleTests
'
' P2-10: Catalog.SupplierLifecycleService against the real, pinned MariaDB
' instance - proves spec section 12's "master data is deactivated where
' possible" for Suppliers, the same shape P2-09's ProductLifecycleTests
' proves for Products. Role-gating (Suppliers.Manage) is
' AuthorizationMatrixTests' own generic coverage test's job.
'
' UNLIKE P2-09: no table in this phase carries a foreign key to Suppliers
' yet (PurchaseOrders is Phase 3 - spec section 12's own Procurement row),
' so "deactivation preserves references from any existing record" is proven
' here as "the row is never deleted and stays resolvable by Id/history-
' search after deactivation, and reactivation never duplicates it" - not a
' cross-table FK proof. Confirmed with the user before implementing; a real
' FK proof arrives with Phase 3's own PurchaseOrders migration.
'
' Fixture supplier/actor are real, permanent rows, the same shape
' ProductLifecycleTests uses - Suppliers has no DELETE grant for merch_api,
' so nothing here is torn down; each test resets the fixture's IsActive to
' the known baseline (Active) before asserting.

Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Net.Http.Json
Imports System.Threading.Tasks
Imports Merchandising.Api.Catalog
Imports Merchandising.Contracts.Auth
Imports Merchandising.Contracts.Suppliers
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class SupplierLifecycleTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"

    Private Const FixtureName As String = "p2_10_fixture_supplier"
    Private Const FixtureActorUsername As String = "p2_10_fixture_actor"
    Private Const FixtureActorPassword As String = "P2-10 Fixture Passw0rd!"
    Private Const FixtureProcurementUsername As String = "p2_10_fixture_lifecycle_procurement"

    Private _connectionFactory As ConnectionFactory
    Private _lifecycleService As SupplierLifecycleService
    Private _supplierId As Integer
    Private _actorUserId As Integer

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        _lifecycleService = New SupplierLifecycleService(_connectionFactory)

        _supplierId = Await EnsureFixtureSupplierAsync()
        Await ResetActiveStateDirectlyAsync(_supplierId, isActive:=True)
        _actorUserId = Await EnsureFixtureActorAsync()
        Await EnsureFixtureProcurementOfficerAsync()

    End Function

    ''' <summary>Baseline: a successful deactivate flips the flag and audits.</summary>
    <TestMethod>
    Public Async Function DeactivateAsync_Success_SetsInactiveAndAudits() As Task

        Dim correlationId As String = Guid.NewGuid().ToString()

        Dim outcome As SupplierLifecycleOutcome = Await _lifecycleService.DeactivateAsync(_supplierId, _actorUserId, correlationId)

        Assert.AreEqual(SupplierLifecycleOutcomeKind.Success, outcome.Kind)
        Assert.IsFalse(outcome.Response.IsActive)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Assert.IsFalse(Await ReadIsActiveAsync(connection, _supplierId))
            Dim auditCount As Long = Await CountAuditRowsAsync(connection, correlationId, "SupplierDeactivated")
            Assert.AreEqual(1L, auditCount)
        End Using

    End Function

    ''' <summary>Deactivating an already-inactive supplier is refused, not a silent no-op.</summary>
    <TestMethod>
    Public Async Function DeactivateAsync_AlreadyInactive_ReturnsNoChange() As Task

        Await _lifecycleService.DeactivateAsync(_supplierId, _actorUserId, Guid.NewGuid().ToString())

        Dim outcome As SupplierLifecycleOutcome = Await _lifecycleService.DeactivateAsync(_supplierId, _actorUserId, Guid.NewGuid().ToString())

        Assert.AreEqual(SupplierLifecycleOutcomeKind.NoChange, outcome.Kind)

    End Function

    <TestMethod>
    Public Async Function DeactivateAsync_UnknownSupplier_ReturnsSupplierNotFound() As Task

        Dim outcome As SupplierLifecycleOutcome = Await _lifecycleService.DeactivateAsync(999999999, _actorUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(SupplierLifecycleOutcomeKind.SupplierNotFound, outcome.Kind)

    End Function

    ''' <summary>Reactivation restores availability without duplicating the record.</summary>
    <TestMethod>
    Public Async Function ReactivateAsync_Success_RestoresActiveAndAudits_NoDuplicateRow() As Task

        Await _lifecycleService.DeactivateAsync(_supplierId, _actorUserId, Guid.NewGuid().ToString())

        Dim correlationId As String = Guid.NewGuid().ToString()
        Dim outcome As SupplierLifecycleOutcome = Await _lifecycleService.ReactivateAsync(_supplierId, _actorUserId, correlationId)

        Assert.AreEqual(SupplierLifecycleOutcomeKind.Success, outcome.Kind)
        Assert.IsTrue(outcome.Response.IsActive)
        Assert.AreEqual(_supplierId, outcome.Response.Id, "Reactivation must restore the SAME row, not create a new one.")

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            Assert.IsTrue(Await ReadIsActiveAsync(connection, _supplierId))

            Dim rowCount As Long = Await CountSupplierRowsForNameAsync(connection, FixtureName)
            Assert.AreEqual(1L, rowCount, "Reactivation must not duplicate the supplier row.")

            Dim auditCount As Long = Await CountAuditRowsAsync(connection, correlationId, "SupplierReactivated")
            Assert.AreEqual(1L, auditCount)

        End Using

    End Function

    <TestMethod>
    Public Async Function ReactivateAsync_AlreadyActive_ReturnsNoChange() As Task

        Dim outcome As SupplierLifecycleOutcome = Await _lifecycleService.ReactivateAsync(_supplierId, _actorUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(SupplierLifecycleOutcomeKind.NoChange, outcome.Kind)

    End Function

    ''' <summary>
    ''' Done-when box "deactivation preserves references from any existing
    ''' record", proven in the form available today (see this file's own
    ''' header): the row is never deleted - it keeps resolving by Id after
    ''' deactivation, the same GetByIdAsync path a future PurchaseOrders
    ''' history view would join through.
    ''' </summary>
    <TestMethod>
    Public Async Function DeactivateAsync_Success_SupplierStillResolvesById() As Task

        Dim outcome As SupplierLifecycleOutcome = Await _lifecycleService.DeactivateAsync(_supplierId, _actorUserId, Guid.NewGuid().ToString())
        Assert.AreEqual(SupplierLifecycleOutcomeKind.Success, outcome.Kind)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Dim supplier = Await SupplierRepository.GetByIdAsync(connection, _supplierId)
            Assert.IsNotNull(supplier, "GetByIdAsync must still resolve an inactive supplier.")
            Assert.AreEqual(FixtureName, supplier.Name)
            Assert.IsFalse(supplier.IsActive)
        End Using

    End Function

    ''' <summary>Search excludes an inactive supplier by default, includes it when asked - same shape as P2-09's product search proof.</summary>
    <TestMethod>
    Public Async Function SearchSuppliers_DefaultExcludesInactive_IncludeInactiveShowsIt() As Task

        Await _lifecycleService.DeactivateAsync(_supplierId, _actorUserId, Guid.NewGuid().ToString())

        Using factory As New MerchandisingApiFactory()
            Using client As HttpClient = factory.CreateClient()

                Dim token As String = Await LoginAsync(client, FixtureProcurementUsername)

                Using defaultResponse As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Get, $"/api/v1/suppliers?q={FixtureName}", token)
                    Dim defaultBody As SupplierSearchResponse = Await defaultResponse.Content.ReadFromJsonAsync(Of SupplierSearchResponse)()
                    Assert.IsFalse(defaultBody.Items.Any(Function(s) s.Name = FixtureName), "Default search must exclude an inactive supplier.")
                End Using

                Using includeResponse As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Get, $"/api/v1/suppliers?q={FixtureName}&includeInactive=true", token)
                    Dim includeBody As SupplierSearchResponse = Await includeResponse.Content.ReadFromJsonAsync(Of SupplierSearchResponse)()
                    Assert.IsTrue(includeBody.Items.Any(Function(s) s.Name = FixtureName), "includeInactive=true must still surface it.")
                End Using

                Using getResponse As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Get, $"/api/v1/suppliers/{_supplierId}", token)
                    Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode, "GetSupplier by Id must never be filtered by IsActive.")
                End Using

            End Using
        End Using

    End Function

    ''' <summary>End-to-end through the real HTTP pipeline: deactivate, then reactivate, both succeed and both audit.</summary>
    <TestMethod>
    Public Async Function DeactivateThenReactivate_ThroughHttp_BothSucceed() As Task

        Using factory As New MerchandisingApiFactory()
            Using client As HttpClient = factory.CreateClient()

                Dim token As String = Await LoginAsync(client, FixtureProcurementUsername)

                Using deactivateResponse As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Post, $"/api/v1/suppliers/{_supplierId}/deactivate", token)
                    Assert.AreEqual(HttpStatusCode.OK, deactivateResponse.StatusCode)
                    Dim body As SupplierResponse = Await deactivateResponse.Content.ReadFromJsonAsync(Of SupplierResponse)()
                    Assert.IsFalse(body.IsActive)
                End Using

                Using secondDeactivateResponse As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Post, $"/api/v1/suppliers/{_supplierId}/deactivate", token)
                    Assert.AreEqual(HttpStatusCode.BadRequest, secondDeactivateResponse.StatusCode)
                End Using

                Using reactivateResponse As HttpResponseMessage =
                    Await SendAsync(client, HttpMethod.Post, $"/api/v1/suppliers/{_supplierId}/reactivate", token)
                    Assert.AreEqual(HttpStatusCode.OK, reactivateResponse.StatusCode)
                    Dim body As SupplierResponse = Await reactivateResponse.Content.ReadFromJsonAsync(Of SupplierResponse)()
                    Assert.IsTrue(body.IsActive)
                End Using

            End Using
        End Using

    End Function

    ' --------------------------------------------------------------- shared helpers

    Private Async Function LoginAsync(client As HttpClient, username As String) As Task(Of String)

        Using response As HttpResponseMessage =
            Await client.PostAsJsonAsync("/api/v1/auth/login", New LoginRequest With {.Username = username, .Password = FixtureActorPassword})

            Assert.AreEqual(
                HttpStatusCode.OK, response.StatusCode,
                $"Fixture user '{username}' could not log in. Body: " & Await response.Content.ReadAsStringAsync())

            Dim login As LoginResponse = Await response.Content.ReadFromJsonAsync(Of LoginResponse)()
            Return login.Token

        End Using

    End Function

    Private Shared Async Function SendAsync(client As HttpClient, method As HttpMethod, path As String, token As String) As Task(Of HttpResponseMessage)

        Dim request As New HttpRequestMessage(method, path)
        request.Headers.Authorization = New AuthenticationHeaderValue("Bearer", token)
        Return Await client.SendAsync(request)

    End Function

    Private Async Function EnsureFixtureSupplierAsync() As Task(Of Integer)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            Using selectCommand As MySqlCommand = connection.CreateCommand()
                selectCommand.CommandText = "SELECT Id FROM Suppliers WHERE Name = @name;"
                selectCommand.Parameters.AddWithValue("@name", FixtureName)
                Dim existing As Object = Await selectCommand.ExecuteScalarAsync()
                If existing IsNot Nothing Then
                    Return CInt(existing)
                End If
            End Using

            Dim supplierId As Integer

            Using insertCommand As MySqlCommand = connection.CreateCommand()
                insertCommand.CommandText =
                    "INSERT INTO Suppliers (Name, IsActive, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@name, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                insertCommand.Parameters.AddWithValue("@name", FixtureName)
                Await insertCommand.ExecuteNonQueryAsync()
                supplierId = CInt(insertCommand.LastInsertedId)
            End Using

            Return supplierId

        End Using

    End Function

    Private Async Function ResetActiveStateDirectlyAsync(supplierId As Integer, isActive As Boolean) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "UPDATE Suppliers SET IsActive = @isActive, UpdatedAtUtc = UTC_TIMESTAMP(6) WHERE Id = @supplierId;"
                command.Parameters.AddWithValue("@isActive", isActive)
                command.Parameters.AddWithValue("@supplierId", supplierId)
                Await command.ExecuteNonQueryAsync()
            End Using
        End Using

    End Function

    Private Async Function EnsureFixtureActorAsync() As Task(Of Integer)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT Id FROM Users WHERE Username = @username;"
                command.Parameters.AddWithValue("@username", FixtureActorUsername)
                Dim existing As Object = Await command.ExecuteScalarAsync()
                If existing IsNot Nothing Then
                    Return CInt(existing)
                End If
            End Using
        End Using

        Dim migratorFactory As New ConnectionFactory(LoadMigratorOptions())
        Return Await CreateUserCommand.RunAsync(migratorFactory, FixtureActorUsername, FixtureActorPassword, "Admin")

    End Function

    ''' <summary>A second fixture user, same password as the actor, so the HTTP-level tests can log in without a separate constant.</summary>
    Private Async Function EnsureFixtureProcurementOfficerAsync() As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT Id FROM Users WHERE Username = @username;"
                command.Parameters.AddWithValue("@username", FixtureProcurementUsername)
                Dim existing As Object = Await command.ExecuteScalarAsync()
                If existing IsNot Nothing Then
                    Return
                End If
            End Using
        End Using

        Dim migratorFactory As New ConnectionFactory(LoadMigratorOptions())
        Await CreateUserCommand.RunAsync(migratorFactory, FixtureProcurementUsername, FixtureActorPassword, "ProcurementOfficer")

    End Function

    Private Shared Async Function ReadIsActiveAsync(connection As MySqlConnection, supplierId As Integer) As Task(Of Boolean)

        Using command As MySqlCommand = connection.CreateCommand()
            command.CommandText = "SELECT IsActive FROM Suppliers WHERE Id = @supplierId;"
            command.Parameters.AddWithValue("@supplierId", supplierId)
            Return CBool(Await command.ExecuteScalarAsync())
        End Using

    End Function

    Private Shared Async Function CountSupplierRowsForNameAsync(connection As MySqlConnection, name As String) As Task(Of Long)

        Using command As MySqlCommand = connection.CreateCommand()
            command.CommandText = "SELECT COUNT(*) FROM Suppliers WHERE Name = @name;"
            command.Parameters.AddWithValue("@name", name)
            Return CLng(Await command.ExecuteScalarAsync())
        End Using

    End Function

    Private Shared Async Function CountAuditRowsAsync(connection As MySqlConnection, correlationId As String, action As String) As Task(Of Long)

        Using command As MySqlCommand = connection.CreateCommand()
            command.CommandText = "SELECT COUNT(*) FROM AuditLogs WHERE CorrelationId = @correlationId AND Action = @action;"
            command.Parameters.AddWithValue("@correlationId", correlationId)
            command.Parameters.AddWithValue("@action", action)
            Return CLng(Await command.ExecuteScalarAsync())
        End Using

    End Function

    Private Shared Function LoadMigratorOptions() As DatabaseOptions
        Dim migratorConfigPath As String =
            IO.Path.Combine(IO.Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName)
        Return DatabaseOptionsLoader.Load(migratorConfigPath)
    End Function

End Class
