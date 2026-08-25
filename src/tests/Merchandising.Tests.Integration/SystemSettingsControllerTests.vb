' Merchandising.Tests.Integration.SystemSettingsControllerTests
'
' P2-05: SystemSettings promoted to a real administered surface (spec
' section 12: configurable currency code and rounding policy; section 17:
' configuration changes audited). Role-gating (write requires SuperAdmin,
' anonymous is refused) is proven separately by AuthorizationMatrixTests'
' ConfigurationManage_MatrixMatchesPolicyRegistry, the same split every
' other Track B endpoint uses. This file proves the feature behavior: an
' unknown or malformed key is rejected before anything is stored, a valid
' write persists and is audited with both the previous and new value, and a
' key nobody has written yet still reads back its registry default.
'
' SystemSettings is an ordinary mutable table, not an append-only ledger
' like AuditLogs/StockMovements - these tests freely overwrite the same
' fixture keys across runs, the same way AuthorizationMatrixTests resets
' its fixture product's stock balance before each positive-cell probe.

Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Net.Http.Json
Imports System.Text.Json
Imports System.Threading.Tasks
Imports Merchandising.Contracts.Auth
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Settings
Imports Merchandising.Domain.Configuration
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class SystemSettingsControllerTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const FixturePassword As String = "P2-05 Fixture Passw0rd!"
    Private Const SuperAdminFixtureUsername As String = "p2_05_fixture_superadmin"

    Private _factory As MerchandisingApiFactory
    Private _connectionFactory As ConnectionFactory

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _factory = New MerchandisingApiFactory()
        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        Await EnsureFixtureUserAsync(SuperAdminFixtureUsername, "SuperAdmin")

    End Function

    <TestCleanup>
    Public Sub TearDown()
        _factory?.Dispose()
    End Sub

    ''' <summary>GET returns every registered key, whether or not it has ever been written.</summary>
    <TestMethod>
    Public Async Function GetSettings_ReturnsEveryRegisteredKey() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, SuperAdminFixtureUsername)

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Get, "/api/v1/admin/settings", token, requestBody:=Nothing)

                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode)

                Dim body As List(Of SystemSettingResponse) =
                    Await response.Content.ReadFromJsonAsync(Of List(Of SystemSettingResponse))()

                For Each definition As SystemSettingDefinition In SystemSettingRegistry.Definitions
                    Assert.IsTrue(
                        body.Exists(Function(s) String.Equals(s.Key, definition.Key, StringComparison.Ordinal)),
                        $"GET /api/v1/admin/settings did not return '{definition.Key}'.")
                Next

            End Using

        End Using

    End Function

    ''' <summary>A key nobody has ever written reads back its registry default.</summary>
    <TestMethod>
    Public Async Function GetSettings_KeyNeverWritten_ReturnsRegistryDefault() As Task

        Await DeleteSettingDirectlyAsync(SystemSettingRegistry.Keys.CurrencyRoundingPolicy)

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, SuperAdminFixtureUsername)

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Get, "/api/v1/admin/settings", token, requestBody:=Nothing)

                Dim body As List(Of SystemSettingResponse) =
                    Await response.Content.ReadFromJsonAsync(Of List(Of SystemSettingResponse))()

                Dim entry As SystemSettingResponse =
                    body.Find(Function(s) String.Equals(s.Key, SystemSettingRegistry.Keys.CurrencyRoundingPolicy, StringComparison.Ordinal))

                Dim definition As SystemSettingDefinition = SystemSettingRegistry.Find(SystemSettingRegistry.Keys.CurrencyRoundingPolicy)

                Assert.AreEqual(definition.DefaultValue, entry.Value)

            End Using

        End Using

    End Function

    ''' <summary>Done-when box 2: a valid write persists and its audit row carries both the previous and new value.</summary>
    <TestMethod>
    Public Async Function UpdateSetting_ValidValue_PersistsAndAuditsPreviousAndNewValue() As Task

        Await WriteSettingDirectlyAsync(SystemSettingRegistry.Keys.CurrencyCode, "AAA")

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, SuperAdminFixtureUsername)
            Dim correlationId As String = Guid.NewGuid().ToString()

            Using response As HttpResponseMessage =
                Await SendAsync(
                    client, HttpMethod.Put, "/api/v1/admin/settings/currency.code", token,
                    New UpdateSystemSettingRequest With {.Value = "BBB"}, correlationId)

                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode)

                Dim body As SystemSettingResponse = Await response.Content.ReadFromJsonAsync(Of SystemSettingResponse)()
                Assert.AreEqual("BBB", body.Value)

            End Using

            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

                Dim storedValue As String = Await ReadSettingValueDirectlyAsync(connection, SystemSettingRegistry.Keys.CurrencyCode)
                Assert.AreEqual("BBB", storedValue, "The new value was not persisted.")

                Dim auditDetail As String = Await ReadAuditDetailAsync(connection, correlationId, "SystemSettingChanged", SystemSettingRegistry.Keys.CurrencyCode)
                Assert.IsNotNull(auditDetail, "No AuditLogs row was written for this change.")
                StringAssert.Contains(auditDetail, "AAA", "Audit detail did not record the previous value.")
                StringAssert.Contains(auditDetail, "BBB", "Audit detail did not record the new value.")

            End Using

        End Using

    End Function

    ''' <summary>Done-when box 4: an unregistered key is rejected before anything is stored.</summary>
    <TestMethod>
    Public Async Function UpdateSetting_UnknownKey_RejectedWithStableErrorCode() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, SuperAdminFixtureUsername)

            Using response As HttpResponseMessage =
                Await SendAsync(
                    client, HttpMethod.Put, "/api/v1/admin/settings/not.a.real.setting", token,
                    New UpdateSystemSettingRequest With {.Value = "anything"})

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)

                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("UNKNOWN_SETTING_KEY", body.ErrorCode)
                Assert.IsFalse(String.IsNullOrWhiteSpace(body.CorrelationId))

            End Using

        End Using

    End Function

    ''' <summary>Done-when box 4: a malformed value for a known key is rejected with field-level detail, not silently stored.</summary>
    <TestMethod>
    Public Async Function UpdateSetting_MalformedCurrencyCode_RejectedWithFieldDetail() As Task

        Await WriteSettingDirectlyAsync(SystemSettingRegistry.Keys.CurrencyCode, "PHP")

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, SuperAdminFixtureUsername)

            Using response As HttpResponseMessage =
                Await SendAsync(
                    client, HttpMethod.Put, "/api/v1/admin/settings/currency.code", token,
                    New UpdateSystemSettingRequest With {.Value = "not-a-code"})

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)

                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("VALIDATION_FAILED", body.ErrorCode)
                Assert.IsTrue(body.Errors IsNot Nothing AndAlso body.Errors.ContainsKey("value"))

            End Using

            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
                Dim storedValue As String = Await ReadSettingValueDirectlyAsync(connection, SystemSettingRegistry.Keys.CurrencyCode)
                Assert.AreEqual("PHP", storedValue, "A malformed value must not overwrite the previously stored one.")
            End Using

        End Using

    End Function

    ''' <summary>Same rejection, for the other registered key's own validator.</summary>
    <TestMethod>
    Public Async Function UpdateSetting_MalformedRoundingPolicy_RejectedWithFieldDetail() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, SuperAdminFixtureUsername)

            Using response As HttpResponseMessage =
                Await SendAsync(
                    client, HttpMethod.Put, "/api/v1/admin/settings/currency.roundingPolicy", token,
                    New UpdateSystemSettingRequest With {.Value = "RoundUp"})

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)

                Dim body As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("VALIDATION_FAILED", body.ErrorCode)

            End Using

        End Using

    End Function

    ' --------------------------------------------------------------- shared helpers

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
            Dim existing As Merchandising.Domain.Entities.User = Await UserRepository.FindByUsernameAsync(connection, username)
            If existing IsNot Nothing Then
                Return
            End If
        End Using

        Dim migratorFactory As New ConnectionFactory(LoadMigratorOptions())
        Await CreateUserCommand.RunAsync(migratorFactory, username, FixturePassword, roleName)

    End Function

    Private Async Function WriteSettingDirectlyAsync(settingKey As String, settingValue As String) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "INSERT INTO SystemSettings (SettingKey, SettingValue, UpdatedAtUtc, UpdatedByUserId) " &
                    "VALUES (@key, @value, UTC_TIMESTAMP(6), NULL) " &
                    "ON DUPLICATE KEY UPDATE SettingValue = VALUES(SettingValue), UpdatedAtUtc = VALUES(UpdatedAtUtc), UpdatedByUserId = NULL;"
                command.Parameters.AddWithValue("@key", settingKey)
                command.Parameters.AddWithValue("@value", settingValue)
                Await command.ExecuteNonQueryAsync()
            End Using
        End Using

    End Function

    ''' <summary>merch_api holds no DELETE on SystemSettings (db/grants/0002 - it is not in the "also needs DELETE" list), so this uses the migrator identity, the same way MaintenanceModeTests reaches for merch_migrator when a test genuinely needs to remove a row.</summary>
    Private Async Function DeleteSettingDirectlyAsync(settingKey As String) As Task

        Dim migratorFactory As New ConnectionFactory(LoadMigratorOptions())
        Using connection As MySqlConnection = Await migratorFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "DELETE FROM SystemSettings WHERE SettingKey = @key;"
                command.Parameters.AddWithValue("@key", settingKey)
                Await command.ExecuteNonQueryAsync()
            End Using
        End Using

    End Function

    Private Shared Async Function ReadSettingValueDirectlyAsync(connection As MySqlConnection, settingKey As String) As Task(Of String)

        Using command As MySqlCommand = connection.CreateCommand()
            command.CommandText = "SELECT SettingValue FROM SystemSettings WHERE SettingKey = @key;"
            command.Parameters.AddWithValue("@key", settingKey)
            Dim value As Object = Await command.ExecuteScalarAsync()
            Return If(value Is Nothing OrElse value Is DBNull.Value, Nothing, CStr(value))
        End Using

    End Function

    Private Shared Async Function ReadAuditDetailAsync(
        connection As MySqlConnection, correlationId As String, action As String, target As String) As Task(Of String)

        Using command As MySqlCommand = connection.CreateCommand()
            command.CommandText =
                "SELECT Detail FROM AuditLogs WHERE CorrelationId = @correlationId AND Action = @action AND Target = @target;"
            command.Parameters.AddWithValue("@correlationId", correlationId)
            command.Parameters.AddWithValue("@action", action)
            command.Parameters.AddWithValue("@target", target)
            Dim value As Object = Await command.ExecuteScalarAsync()
            Return If(value Is Nothing OrElse value Is DBNull.Value, Nothing, CStr(value))
        End Using

    End Function

    Private Shared Function LoadMigratorOptions() As DatabaseOptions
        Dim migratorConfigPath As String =
            IO.Path.Combine(IO.Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName)
        Return DatabaseOptionsLoader.Load(migratorConfigPath)
    End Function

End Class
