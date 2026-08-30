' Merchandising.Tests.Integration.CashierSessionTests
'
' P5-04: CashierSessionService.{OpenAsync,CloseAsync} and the
' CashierSessionsController routes, proven against the real pinned MariaDB -
' spec section 10.3's "A sale requires an open cashier session."
'
' Role gating (CashierSessions.Manage resolves to the roles PolicyRegistry
' says it does) is AuthorizationMatrixTests' job. This file proves the
' BEHAVIOUR:
'
'   box 1   one cashier cannot hold two open sessions at once. The DB-level
'           mechanism (ADR-018's unique generated column) was already proven
'           by raw concurrent inserts in P5-02's PosSchemaTests
'           (TwoConcurrentOpenSessionInserts_SameCashier_ExactlyOneSucceeds,
'           "success | ERROR 1062"). This file proves the layer ABOVE that:
'           CashierSessionService.OpenAsync, fired twice concurrently for the
'           same cashier, turns that same race into exactly one Created and
'           one controlled AlreadyOpen outcome - never an unhandled
'           exception - the P2-07/P3-02/P4-02 shape applied at the service
'           boundary this card adds.
'   box 2   opening float scale is validated at the API boundary (ADR-004.1)
'           before binding - refused 400, not silently rounded, and the
'           service itself asserts the same precondition when reached
'           directly.
'   box 3   a closed session is immutable (cannot be closed again, NotOpen)
'           and remains fully readable afterwards.
'   box 4   Open and Close each write an audit row carrying the actor and
'           correlation ID.
'   (idempotency) a repeated key on either command replays the original
'           committed response rather than doing the work again (ADR-007).
'
' Fixture users are real, permanent rows (ADR-013: no DELETE grant on
' Users). Sessions/audit rows created here are also never deleted -
' CashierSessions carries no DELETE grant either (db/grants/0013) - so every
' fixture user used to open a session is unique per test (Guid-suffixed) to
' avoid colliding with another test's still-open session under the ADR-018
' unique index.

Imports System.Collections.Generic
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Net.Http.Json
Imports System.Threading.Tasks
Imports Merchandising.Api.Sales
Imports Merchandising.Contracts.Auth
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Sales
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class CashierSessionTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const FixturePassword As String = "P5-04 Fixture Passw0rd!"
    Private Const FixtureUsernamePrefix As String = "p5_04_fixture_cashier_"

    Private _factory As MerchandisingApiFactory
    Private _connectionFactory As ConnectionFactory
    Private _cashierSessionService As CashierSessionService

    <TestInitialize>
    Public Sub SetUp()
        _factory = New MerchandisingApiFactory()
        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        _cashierSessionService = New CashierSessionService(_connectionFactory)
    End Sub

    <TestCleanup>
    Public Sub TearDown()
        _factory?.Dispose()
    End Sub

    ' -------------------------------------------------------------- box 1

    ''' <summary>A fresh cashier's first Open succeeds - Open, no closer, no idle daily-closing columns set yet.</summary>
    <TestMethod>
    Public Async Function OpenAsync_FreshCashier_CreatesOpenSession() As Task

        Dim cashierUserId As Integer = Await CreateFixtureCashierAsync()

        Dim outcome As CashierSessionOutcome =
            Await _cashierSessionService.OpenAsync(
                cashierUserId, 500.0000D, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(CashierSessionOutcomeKind.Created, outcome.Kind)
        Assert.AreEqual("Open", outcome.Session.Status)
        Assert.AreEqual(cashierUserId, outcome.Session.OpenedByUserId)
        Assert.IsNull(outcome.Session.ClosedByUserId)
        Assert.IsNull(outcome.Session.ClosedAtUtc)
        Assert.IsNull(outcome.Session.DeclaredCash, "P5-05's column - untouched by Open.")
        Assert.IsNull(outcome.Session.CalculatedCash, "P5-05's column - untouched by Open.")
        Assert.IsNull(outcome.Session.CashVariance, "P5-05's column - untouched by Open.")
        Assert.AreEqual(500.0000D, outcome.Session.OpeningFloat)

    End Function

    ''' <summary>Sequential proof: a second Open for the same cashier is refused while the first is still Open.</summary>
    <TestMethod>
    Public Async Function OpenAsync_CashierAlreadyHasOpenSession_ReturnsAlreadyOpen() As Task

        Dim cashierUserId As Integer = Await CreateFixtureCashierAsync()

        Dim first As CashierSessionOutcome =
            Await _cashierSessionService.OpenAsync(cashierUserId, 500.0000D, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(CashierSessionOutcomeKind.Created, first.Kind)

        Dim second As CashierSessionOutcome =
            Await _cashierSessionService.OpenAsync(cashierUserId, 500.0000D, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(CashierSessionOutcomeKind.AlreadyOpen, second.Kind)

    End Function

    ''' <summary>
    ''' Box 1's own shape: two OpenAsync calls fired without awaiting between
    ''' them, on one shared cashier, sharing no idempotency key so neither
    ''' can win by replay. No API check exists to bypass here on purpose -
    ''' the same ADR-018 unique index P5-02 already proved raw is what
    ''' resolves this race; this proves the SERVICE converts it into a
    ''' controlled outcome rather than an unhandled exception.
    ''' </summary>
    <TestMethod>
    Public Async Function TwoConcurrentOpens_SameCashier_ExactlyOneSucceeds() As Task

        Dim cashierUserId As Integer = Await CreateFixtureCashierAsync()

        Dim first As Task(Of CashierSessionOutcome) =
            _cashierSessionService.OpenAsync(cashierUserId, 500.0000D, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Dim second As Task(Of CashierSessionOutcome) =
            _cashierSessionService.OpenAsync(cashierUserId, 500.0000D, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Dim results As CashierSessionOutcome() = Await Task.WhenAll(first, second)

        Console.WriteLine(
            $"P5-04 concurrent open -> {results(0).Kind} | {results(1).Kind}")

        Dim createdCount As Integer = results.Count(Function(r) r.Kind = CashierSessionOutcomeKind.Created)
        Dim alreadyOpenCount As Integer = results.Count(Function(r) r.Kind = CashierSessionOutcomeKind.AlreadyOpen)

        Assert.AreEqual(1, createdCount, "Exactly one of two simultaneous Open calls sharing a cashier must succeed.")
        Assert.AreEqual(1, alreadyOpenCount, "The other must be a controlled AlreadyOpen outcome, translated from ERROR 1062 - never an unhandled exception.")

    End Function

    ''' <summary>Closing frees the cashier to open again - proves the unique index is scoped to Open rows, not to the cashier forever.</summary>
    <TestMethod>
    Public Async Function OpenAsync_AfterPriorSessionCloses_Succeeds() As Task

        Dim cashierUserId As Integer = Await CreateFixtureCashierAsync()

        Dim opened As CashierSessionOutcome =
            Await _cashierSessionService.OpenAsync(cashierUserId, 500.0000D, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(CashierSessionOutcomeKind.Created, opened.Kind)

        Dim closed As CashierSessionOutcome =
            Await _cashierSessionService.CloseAsync(opened.Session.Id, cashierUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(CashierSessionOutcomeKind.Created, closed.Kind)

        Dim reopened As CashierSessionOutcome =
            Await _cashierSessionService.OpenAsync(cashierUserId, 250.0000D, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(CashierSessionOutcomeKind.Created, reopened.Kind, "Opening again must succeed once the prior session is Closed.")

    End Function

    ' -------------------------------------------------------------- box 2

    ''' <summary>ADR-004.1: an over-scale opening float is a hard precondition failure when the service is reached directly.</summary>
    <TestMethod>
    Public Async Function OpenAsync_OverScaleOpeningFloat_Throws() As Task

        Dim cashierUserId As Integer = Await CreateFixtureCashierAsync()

        Await Assert.ThrowsExactlyAsync(Of ArgumentException)(
            Function() _cashierSessionService.OpenAsync(
                cashierUserId, 500.00001D, Guid.NewGuid().ToString(), Guid.NewGuid().ToString()))

    End Function

    ''' <summary>A negative opening float is refused as a hard precondition, the same shape AdjustmentService.RequestAsync refuses a zero variance.</summary>
    <TestMethod>
    Public Async Function OpenAsync_NegativeOpeningFloat_Throws() As Task

        Dim cashierUserId As Integer = Await CreateFixtureCashierAsync()

        Await Assert.ThrowsExactlyAsync(Of ArgumentException)(
            Function() _cashierSessionService.OpenAsync(
                cashierUserId, -1.0000D, Guid.NewGuid().ToString(), Guid.NewGuid().ToString()))

    End Function

    ''' <summary>An over-scale opening float is refused 400 at the HTTP boundary, before it ever reaches the service.</summary>
    <TestMethod>
    Public Async Function OpenCashierSession_OverScaleOpeningFloat_Refused400() As Task

        Dim username As String = Await CreateFixtureCashierUsernameAsync()

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, username)
            Dim body As New OpenCashierSessionRequest With {
                .OpeningFloat = 500.00001D, .IdempotencyKey = Guid.NewGuid().ToString("d")}

            Using response As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/cashier-sessions", token, body)

                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)
                Dim errorBody As ApiErrorResponse = Await response.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual("VALIDATION_FAILED", errorBody.ErrorCode)
                Assert.IsTrue(errorBody.Errors.ContainsKey("openingFloat"))

            End Using

        End Using

    End Function

    ''' <summary>A cashier who already holds an Open session is refused 409 over HTTP.</summary>
    <TestMethod>
    Public Async Function OpenCashierSession_AlreadyOpen_Refused409() As Task

        Dim username As String = Await CreateFixtureCashierUsernameAsync()

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client, username)
            Dim firstBody As New OpenCashierSessionRequest With {
                .OpeningFloat = 500.0000D, .IdempotencyKey = Guid.NewGuid().ToString("d")}

            Using firstResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/cashier-sessions", token, firstBody)
                Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode)
            End Using

            Dim secondBody As New OpenCashierSessionRequest With {
                .OpeningFloat = 500.0000D, .IdempotencyKey = Guid.NewGuid().ToString("d")}

            Using secondResponse As HttpResponseMessage =
                Await SendAsync(client, HttpMethod.Post, "/api/v1/cashier-sessions", token, secondBody)

                Assert.AreEqual(HttpStatusCode.Conflict, secondResponse.StatusCode)
                Dim errorBody As ApiErrorResponse = Await secondResponse.Content.ReadFromJsonAsync(Of ApiErrorResponse)()
                Assert.AreEqual(CashierSessionOutcome.AlreadyOpenErrorCode, errorBody.ErrorCode)

            End Using

        End Using

    End Function

    ' -------------------------------------------------------------- box 3

    ''' <summary>Closing an Open session flips it to Closed, records the closing actor and timestamp, and increments RowVersion.</summary>
    <TestMethod>
    Public Async Function CloseAsync_OpenSession_ClosesAndRecordsActor() As Task

        Dim cashierUserId As Integer = Await CreateFixtureCashierAsync()

        Dim opened As CashierSessionOutcome =
            Await _cashierSessionService.OpenAsync(cashierUserId, 500.0000D, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Dim closed As CashierSessionOutcome =
            Await _cashierSessionService.CloseAsync(opened.Session.Id, cashierUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(CashierSessionOutcomeKind.Created, closed.Kind)
        Assert.AreEqual("Closed", closed.Session.Status)
        Assert.AreEqual(cashierUserId, closed.Session.ClosedByUserId)
        Assert.IsNotNull(closed.Session.ClosedAtUtc)
        Assert.AreEqual(opened.Session.RowVersion + 1L, closed.Session.RowVersion)

    End Function

    ''' <summary>Immutability: a Closed session cannot be closed again.</summary>
    <TestMethod>
    Public Async Function CloseAsync_AlreadyClosed_ReturnsNotOpen() As Task

        Dim cashierUserId As Integer = Await CreateFixtureCashierAsync()

        Dim opened As CashierSessionOutcome =
            Await _cashierSessionService.OpenAsync(cashierUserId, 500.0000D, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Dim firstClose As CashierSessionOutcome =
            Await _cashierSessionService.CloseAsync(opened.Session.Id, cashierUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(CashierSessionOutcomeKind.Created, firstClose.Kind)

        Dim secondClose As CashierSessionOutcome =
            Await _cashierSessionService.CloseAsync(opened.Session.Id, cashierUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Assert.AreEqual(CashierSessionOutcomeKind.NotOpen, secondClose.Kind)

    End Function

    <TestMethod>
    Public Async Function CloseAsync_UnknownId_ReturnsNotFound() As Task

        Dim outcome As CashierSessionOutcome =
            Await _cashierSessionService.CloseAsync(999999999, Await CreateFixtureCashierAsync(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Assert.AreEqual(CashierSessionOutcomeKind.NotFound, outcome.Kind)

    End Function

    ''' <summary>A Closed session remains fully readable, unmoved, through GetAsync (card Done-when box 3).</summary>
    <TestMethod>
    Public Async Function CloseAsync_ClosedSession_RemainsFullyReadable() As Task

        Dim cashierUserId As Integer = Await CreateFixtureCashierAsync()

        Dim opened As CashierSessionOutcome =
            Await _cashierSessionService.OpenAsync(cashierUserId, 500.0000D, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
        Dim closed As CashierSessionOutcome =
            Await _cashierSessionService.CloseAsync(opened.Session.Id, cashierUserId, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Dim read As CashierSessionResponse = Await _cashierSessionService.GetAsync(opened.Session.Id)

        Assert.IsNotNull(read, "A closed session must remain fully readable.")
        Assert.AreEqual("Closed", read.Status)
        Assert.AreEqual(cashierUserId, read.OpenedByUserId)
        Assert.AreEqual(cashierUserId, read.ClosedByUserId)
        Assert.AreEqual(500.0000D, read.OpeningFloat)
        Assert.AreEqual(closed.Session.RowVersion, read.RowVersion)

    End Function

    ' -------------------------------------------------------------- box 4 (audit)

    ''' <summary>Open writes an audit row carrying the actor and correlation ID.</summary>
    <TestMethod>
    Public Async Function OpenAsync_WritesAuditRow() As Task

        Dim cashierUserId As Integer = Await CreateFixtureCashierAsync()
        Dim correlationId As String = Guid.NewGuid().ToString()

        Dim outcome As CashierSessionOutcome =
            Await _cashierSessionService.OpenAsync(cashierUserId, 500.0000D, correlationId, Guid.NewGuid().ToString())
        Assert.AreEqual(CashierSessionOutcomeKind.Created, outcome.Kind)

        Assert.AreEqual(1L, Await CountAuditRowsAsync(correlationId, "CashierSessionOpened", cashierUserId))

    End Function

    ''' <summary>Close writes an audit row carrying the actor and correlation ID.</summary>
    <TestMethod>
    Public Async Function CloseAsync_WritesAuditRow() As Task

        Dim cashierUserId As Integer = Await CreateFixtureCashierAsync()

        Dim opened As CashierSessionOutcome =
            Await _cashierSessionService.OpenAsync(cashierUserId, 500.0000D, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Dim correlationId As String = Guid.NewGuid().ToString()
        Dim closed As CashierSessionOutcome =
            Await _cashierSessionService.CloseAsync(opened.Session.Id, cashierUserId, correlationId, Guid.NewGuid().ToString())
        Assert.AreEqual(CashierSessionOutcomeKind.Created, closed.Kind)

        Assert.AreEqual(1L, Await CountAuditRowsAsync(correlationId, "CashierSessionClosed", cashierUserId))

    End Function

    ' -------------------------------------------------------------- idempotency

    ''' <summary>A repeated idempotency key on Open replays the original committed session rather than opening a second one - and does not collide with the unique index either.</summary>
    <TestMethod>
    Public Async Function OpenAsync_RepeatedIdempotencyKey_ReplaysOriginal() As Task

        Dim cashierUserId As Integer = Await CreateFixtureCashierAsync()
        Dim idempotencyKey As String = Guid.NewGuid().ToString()

        Dim first As CashierSessionOutcome =
            Await _cashierSessionService.OpenAsync(cashierUserId, 500.0000D, Guid.NewGuid().ToString(), idempotencyKey)
        Assert.AreEqual(CashierSessionOutcomeKind.Created, first.Kind)

        Dim second As CashierSessionOutcome =
            Await _cashierSessionService.OpenAsync(cashierUserId, 500.0000D, Guid.NewGuid().ToString(), idempotencyKey)
        Assert.AreEqual(CashierSessionOutcomeKind.Replayed, second.Kind)
        Assert.Contains($"""id"":{first.Session.Id}", second.ReplayPayload)

    End Function

    ''' <summary>A repeated idempotency key on Close replays the original committed close.</summary>
    <TestMethod>
    Public Async Function CloseAsync_RepeatedIdempotencyKey_ReplaysOriginal() As Task

        Dim cashierUserId As Integer = Await CreateFixtureCashierAsync()

        Dim opened As CashierSessionOutcome =
            Await _cashierSessionService.OpenAsync(cashierUserId, 500.0000D, Guid.NewGuid().ToString(), Guid.NewGuid().ToString())

        Dim idempotencyKey As String = Guid.NewGuid().ToString()

        Dim first As CashierSessionOutcome =
            Await _cashierSessionService.CloseAsync(opened.Session.Id, cashierUserId, Guid.NewGuid().ToString(), idempotencyKey)
        Assert.AreEqual(CashierSessionOutcomeKind.Created, first.Kind)

        Dim second As CashierSessionOutcome =
            Await _cashierSessionService.CloseAsync(opened.Session.Id, cashierUserId, Guid.NewGuid().ToString(), idempotencyKey)
        Assert.AreEqual(CashierSessionOutcomeKind.Replayed, second.Kind)

    End Function

    ' --------------------------------------------------------------- helpers (HTTP)

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
        client As HttpClient, method As HttpMethod, path As String, token As String, requestBody As Object) As Task(Of HttpResponseMessage)

        Dim request As New HttpRequestMessage(method, path)

        If token IsNot Nothing Then
            request.Headers.Authorization = New AuthenticationHeaderValue("Bearer", token)
        End If

        If requestBody IsNot Nothing Then
            request.Content = JsonContent.Create(requestBody)
        End If

        Return Await client.SendAsync(request)

    End Function

    ' --------------------------------------------------------------- helpers (fixtures)

    ''' <summary>A fresh cashier user, unique per call - never reused across tests, so no test's Open session collides with another's under the ADR-018 unique index.</summary>
    Private Async Function CreateFixtureCashierAsync() As Task(Of Integer)

        Dim username As String = FixtureUsernamePrefix & Guid.NewGuid().ToString("N").Substring(0, 16)
        Dim migratorFactory As New ConnectionFactory(LoadMigratorOptions())
        Return Await CreateUserCommand.RunAsync(migratorFactory, username, FixturePassword, "Cashier")

    End Function

    Private Async Function CreateFixtureCashierUsernameAsync() As Task(Of String)

        Dim username As String = FixtureUsernamePrefix & Guid.NewGuid().ToString("N").Substring(0, 16)
        Dim migratorFactory As New ConnectionFactory(LoadMigratorOptions())
        Await CreateUserCommand.RunAsync(migratorFactory, username, FixturePassword, "Cashier")
        Return username

    End Function

    Private Async Function CountAuditRowsAsync(correlationId As String, action As String, actorUserId As Integer) As Task(Of Long)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT COUNT(*) FROM AuditLogs WHERE CorrelationId = @correlationId AND Action = @action AND ActorUserId = @actorUserId;"
                command.Parameters.AddWithValue("@correlationId", correlationId)
                command.Parameters.AddWithValue("@action", action)
                command.Parameters.AddWithValue("@actorUserId", actorUserId)
                Return CLng(Await command.ExecuteScalarAsync())
            End Using
        End Using

    End Function

    Private Shared Function LoadMigratorOptions() As DatabaseOptions
        Dim migratorConfigPath As String =
            IO.Path.Combine(IO.Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName)
        Return DatabaseOptionsLoader.Load(migratorConfigPath)
    End Function

End Class
