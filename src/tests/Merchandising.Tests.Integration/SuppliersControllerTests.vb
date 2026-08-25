' Merchandising.Tests.Integration.SuppliersControllerTests
'
' P2-10: Supplier CRUD with uniqueness enforced at DB AND API, same shape as
' P2-07's ProductsControllerTests. Role-gating itself (Suppliers.Manage/
' Suppliers.Read resolve to the roles PolicyRegistry says they do) is
' AuthorizationMatrixTests' own generic coverage test's job - this file
' proves the feature behavior: duplicate name rejected at the API layer with
' a usable message, AND at the database layer under real concurrency that
' bypasses the API's own pre-check (done-when box 2); every mutation
' audited (done-when box 4).
'
' Fixture accounts/suppliers are real, permanent rows, the same shape every
' earlier integration suite uses - Suppliers has no DELETE grant for
' merch_api (0009_supplier-grants.sql), so nothing here is cleaned up in
' TearDown; each test uses a fresh Guid-derived name so runs never collide
' with each other or with earlier runs.

Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Net.Http.Json
Imports System.Threading.Tasks
Imports Merchandising.Contracts.Auth
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Suppliers
Imports Merchandising.Domain.Entities
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class SuppliersControllerTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const FixturePassword As String = "P2-10 Fixture Passw0rd!"
    Private Const ProcurementFixtureUsername As String = "p2_10_fixture_procurement"
    Private Const CashierFixtureUsername As String = "p2_10_fixture_cashier"

    Private _factory As MerchandisingApiFactory
    Private _connectionFactory As ConnectionFactory

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _factory = New MerchandisingApiFactory()
        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        Await EnsureFixtureUserAsync(ProcurementFixtureUsername, "ProcurementOfficer")
        Await EnsureFixtureUserAsync(CashierFixtureUsername, "Cashier")

    End Function

    <TestCleanup>
    Public Sub TearDown()
        _factory?.Dispose()
    End Sub

    ''' <summary>Happy path: create, then read it back, then update it.</summary>
    <TestMethod>
    Public Async Function Create_ThenGet_ThenUpdate_RoundTrips() As Task

        Dim name As String = FreshName()

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)

            Dim createResponse As SupplierResponse = Await CreateSupplierAsync(
                client, token, New CreateSupplierRequest With {
                    .Name = name, .ContactName = "Original Contact", .Phone = "0917-000-0000"
                })

            Assert.IsGreaterThan(0, createResponse.Id)
            Assert.AreEqual(name, createResponse.Name)
            Assert.IsTrue(createResponse.IsActive, "A newly created supplier must be Active.")
            Assert.AreEqual(0L, createResponse.RowVersion)

            Using getResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Get, $"/api/v1/suppliers/{createResponse.Id}", token, Nothing)
                Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode)
                Dim fetched As SupplierResponse = Await getResponse.Content.ReadFromJsonAsync(Of SupplierResponse)()
                Assert.AreEqual(name, fetched.Name)
            End Using

            Using updateResponse As HttpResponseMessage =
                Await SendAsync(
                    client, HttpMethod.Put, $"/api/v1/suppliers/{createResponse.Id}", token,
                    New UpdateSupplierRequest With {.Name = name, .ContactName = "Renamed Contact", .Email = "renamed@example.test"})

                Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode)
                Dim updated As SupplierResponse = Await updateResponse.Content.ReadFromJsonAsync(Of SupplierResponse)()
                Assert.AreEqual("Renamed Contact", updated.ContactName)
                Assert.AreEqual("renamed@example.test", updated.Email)
                Assert.AreEqual(1L, updated.RowVersion, "RowVersion must advance on update.")

            End Using

        End Using

    End Function

    ''' <summary>Done-when box 2 (HTTP half): duplicate name rejected with a stable error code and field-level detail.</summary>
    <TestMethod>
    Public Async Function CreateSupplier_DuplicateName_RejectedWithStableErrorCodeAndFieldDetail() As Task

        Dim name As String = FreshName()

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)

            Await CreateSupplierAsync(client, token, New CreateSupplierRequest With {.Name = name})

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/suppliers", token, New CreateSupplierRequest With {.Name = name})

                Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("DUPLICATE_SUPPLIER_NAME", body.ErrorCode)
                Assert.IsTrue(body.Errors IsNot Nothing AndAlso body.Errors.ContainsKey("name"))
                Assert.IsFalse(String.IsNullOrWhiteSpace(body.CorrelationId))

            End Using

        End Using

    End Function

    ''' <summary>Done-when box 2 (DB half): duplicate name rejected by the DATABASE, bypassing the API's own pre-check, under real concurrency.</summary>
    <TestMethod>
    Public Async Function InsertAsync_TwoConcurrentInsertsSameName_ExactlyOneSucceeds() As Task

        Dim name As String = FreshName()

        Dim task1 As Task(Of (Kind As SupplierWriteOutcomeKind, SupplierId As Integer)) = InsertRaceSupplierAsync(name)
        Dim task2 As Task(Of (Kind As SupplierWriteOutcomeKind, SupplierId As Integer)) = InsertRaceSupplierAsync(name)

        Dim results = Await Task.WhenAll(task1, task2)

        Dim successCount As Integer = results.Count(Function(r) r.Kind = SupplierWriteOutcomeKind.Success)
        Dim duplicateCount As Integer = results.Count(Function(r) r.Kind = SupplierWriteOutcomeKind.DuplicateName)

        Console.WriteLine($"P2-10 concurrent name insert -> {results(0).Kind} | {results(1).Kind}")

        Assert.AreEqual(1, successCount, "Exactly one of two simultaneous inserts sharing a name must succeed.")
        Assert.AreEqual(1, duplicateCount, "The other must be reported as DuplicateName, not an unhandled exception.")

    End Function

    ''' <summary>Done-when box 4: a successful create writes an AuditLogs row.</summary>
    <TestMethod>
    Public Async Function CreateSupplier_Success_WritesAuditRow() As Task

        Dim name As String = FreshName()

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)
            Dim correlationId As String = Guid.NewGuid().ToString()

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/suppliers", token,
                    New CreateSupplierRequest With {.Name = name}, correlationId)

                Assert.AreEqual(HttpStatusCode.Created, response.StatusCode)

            End Using

            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
                Dim auditCount As Long = Await CountAuditRowsAsync(connection, correlationId, "SupplierCreated")
                Assert.AreEqual(1L, auditCount, "Exactly one AuditLogs row must be written for a successful create.")
            End Using

        End Using

    End Function

    ''' <summary>Done-when box 4: a successful update also writes an AuditLogs row.</summary>
    <TestMethod>
    Public Async Function UpdateSupplier_Success_WritesAuditRow() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)

            Dim created As SupplierResponse = Await CreateSupplierAsync(client, token, New CreateSupplierRequest With {.Name = FreshName()})

            Dim correlationId As String = Guid.NewGuid().ToString()

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Put, $"/api/v1/suppliers/{created.Id}", token,
                    New UpdateSupplierRequest With {.Name = created.Name, .ContactName = "Audited Update"}, correlationId)

                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode)

            End Using

            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
                Dim auditCount As Long = Await CountAuditRowsAsync(connection, correlationId, "SupplierUpdated")
                Assert.AreEqual(1L, auditCount, "Exactly one AuditLogs row must be written for a successful update.")
            End Using

        End Using

    End Function

    ''' <summary>A supplier not found returns a controlled 404, not an unhandled error.</summary>
    <TestMethod>
    Public Async Function GetSupplier_UnknownId_ReturnsControlled404() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Get, "/api/v1/suppliers/999999999", token, Nothing)

                Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode)
                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("SUPPLIER_NOT_FOUND", body.ErrorCode)

            End Using

        End Using

    End Function

    ''' <summary>Search by name (spec section 12's supplier-name index) finds the fixture supplier it was created with.</summary>
    <TestMethod>
    Public Async Function SearchSuppliers_ByName_FindsIt() As Task

        Dim name As String = FreshName()

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)

            Await CreateSupplierAsync(client, token, New CreateSupplierRequest With {.Name = name})

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Get, $"/api/v1/suppliers?q={name}", token, Nothing)

                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode)
                Dim body As SupplierSearchResponse = Await response.Content.ReadFromJsonAsync(Of SupplierSearchResponse)()
                Assert.IsTrue(body.Items.Any(Function(s) String.Equals(s.Name, name, StringComparison.Ordinal)))

            End Using

        End Using

    End Function

    ''' <summary>Done-when box 3: a Procurement Officer - in Suppliers.Manage's allowed-role list - succeeds.</summary>
    <TestMethod>
    Public Async Function CreateSupplier_AsProcurementOfficer_Succeeds() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, ProcurementFixtureUsername)

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/suppliers", token, New CreateSupplierRequest With {.Name = FreshName()})

                Assert.AreEqual(HttpStatusCode.Created, response.StatusCode)

            End Using

        End Using

    End Function

    ''' <summary>Done-when box 3: a Cashier - not in Suppliers.Manage's allowed-role list - is refused.</summary>
    <TestMethod>
    Public Async Function CreateSupplier_AsCashier_Refused403() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, CashierFixtureUsername)

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/suppliers", token, New CreateSupplierRequest With {.Name = FreshName()})

                Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode)

            End Using

        End Using

    End Function

    ' --------------------------------------------------------------- shared helpers

    Private Shared Function FreshName() As String
        Return "P2-10 Supplier " & Guid.NewGuid().ToString("N").Substring(0, 12)
    End Function

    Private Async Function InsertRaceSupplierAsync(name As String) As Task(Of (Kind As SupplierWriteOutcomeKind, SupplierId As Integer))

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Dim transaction As MySqlTransaction = Await connection.BeginTransactionAsync()

            Dim supplier As New Supplier With {.Name = name}

            Dim result = Await SupplierRepository.InsertAsync(connection, transaction, supplier)

            If result.Kind = SupplierWriteOutcomeKind.Success Then
                Await transaction.CommitAsync()
            Else
                Await transaction.RollbackAsync()
            End If
            Await transaction.DisposeAsync()

            Return result
        End Using

    End Function

    Private Async Function LoginAsync(client As HttpClient, username As String) As Task(Of String)

        Using response As HttpResponseMessage =
            Await client.PostAsJsonAsync("/api/v1/auth/login", New LoginRequest With {.Username = username, .Password = FixturePassword})

            Assert.AreEqual(
                HttpStatusCode.OK, response.StatusCode,
                $"Fixture user '{username}' could not log in. Body: " & Await response.Content.ReadAsStringAsync())

            Dim login As LoginResponse = Await response.Content.ReadFromJsonAsync(Of LoginResponse)()
            Return login.Token

        End Using

    End Function

    Private Async Function CreateSupplierAsync(client As HttpClient, token As String, request As CreateSupplierRequest) As Task(Of SupplierResponse)

        Using response As HttpResponseMessage = Await SendAsync(client, HttpMethod.Post, "/api/v1/suppliers", token, request)
            Assert.AreEqual(
                HttpStatusCode.Created, response.StatusCode,
                "Fixture supplier creation failed. Body: " & Await response.Content.ReadAsStringAsync())
            Return Await response.Content.ReadFromJsonAsync(Of SupplierResponse)()
        End Using

    End Function

    Private Shared Async Function SendAsync(
        client As HttpClient, method As HttpMethod, path As String, token As String, requestBody As Object,
        Optional correlationId As String = Nothing) As Task(Of HttpResponseMessage)

        Dim request As New HttpRequestMessage(method, path)

        If token IsNot Nothing Then
            request.Headers.Authorization = New AuthenticationHeaderValue("Bearer", token)
        End If

        If correlationId IsNot Nothing Then
            request.Headers.Add("X-Correlation-Id", correlationId)
        End If

        If requestBody IsNot Nothing Then
            request.Content = JsonContent.Create(requestBody)
        End If

        Return Await client.SendAsync(request)

    End Function

    Private Async Function EnsureFixtureUserAsync(username As String, roleName As String) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Dim existing As User = Await UserRepository.FindByUsernameAsync(connection, username)
            If existing IsNot Nothing Then
                Return
            End If
        End Using

        Dim migratorFactory As New ConnectionFactory(LoadMigratorOptions())
        Await CreateUserCommand.RunAsync(migratorFactory, username, FixturePassword, roleName)

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
