' Merchandising.Tests.Unit.ClientBehaviourTests
'
' P1-15 evidence. Three of the card's five acceptance criteria are properties
' of the CLIENT rather than of a network, and those are the three proven here:
'
'   * "Stopping the API produces a clear connection-unavailable state"
'   * "The client refuses to queue or fake the write when offline"  (gap G-26)
'   * "Token never written to disk"
'
' The remaining two are proven elsewhere: the cross-machine round trip needs a
' second machine and stays open on the card, and the ClientCommon reference
' rule is guardrail G-B in scripts/check-no-csharp.ps1.
'
' Each unavailable-state test is paired with a control that reaches a server
' and gets refused. Without that pair, a client that reported "offline" for
' every failure would pass, and that client would be wrong in the way G-26 is
' actually about.

Imports System.Globalization
Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Net.Sockets
Imports System.Text
Imports System.Text.Json
Imports System.Threading.Tasks
Imports Merchandising.ClientCommon.Api
Imports Microsoft.VisualStudio.TestTools.UnitTesting

''' <summary>Client-side behaviour of <c>MerchandisingApiClient</c> (P1-15).</summary>
<TestClass>
Public Class ClientBehaviourTests

    Private Shared ReadOnly ApiRoot As New Uri("https://MERCH-HOST:8443/")

    ''' <summary>A token value distinctive enough to search a filesystem for.</summary>
    Private Const FixtureToken As String = "P1-15-FIXTURE-TOKEN-6f3d9c1a5b7e42d0"

    ''' <summary>
    ''' The connection indicator must not claim "online" before anything has
    ''' been contacted. A green light that means "no evidence either way" is the
    ''' precise failure G-26 describes.
    ''' </summary>
    <TestMethod>
    Public Sub ConnectionState_BeforeAnyCall_IsUnknownRatherThanOnline()

        Using handler As StubHttpMessageHandler = RespondWith(Function(request) Ok(New With {.ok = True}))
            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Assert.AreEqual(ConnectionState.Unknown, client.State)
                Assert.IsFalse(client.IsAuthenticated)

            End Using
        End Using

    End Sub

    ''' <summary>
    ''' Done-when box 2: an API that cannot be reached produces the Unavailable
    ''' outcome and a message naming the cause, not a generic failure.
    ''' </summary>
    <TestMethod>
    Public Async Function ApiUnreachable_ProducesUnavailableStateAndAPlainMessage() As Task

        Dim apiIsDown As Boolean = False

        Using handler As StubHttpMessageHandler = RespondWith(
            Function(request)
                If apiIsDown Then
                    Throw ConnectionRefused()
                End If
                Return Ok(LoginPayload())
            End Function)

            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Dim signIn = Await client.LoginAsync("clerk", "password")
                Assert.IsTrue(signIn.IsSuccess, "Fixture login should succeed while the API is up.")
                Assert.AreEqual(ConnectionState.Online, client.State)

                apiIsDown = True

                Dim decrement = Await client.DecrementStockAsync(1, 1D, "P1-15", Guid.NewGuid().ToString("D"))

                Assert.AreEqual(ApiOutcome.Unavailable, decrement.Outcome)
                Assert.AreEqual(ConnectionState.Unavailable, client.State)
                Assert.IsNull(decrement.Value, "No payload may be invented for a call that never arrived.")
                StringAssert.Contains(decrement.TransportDetail, "not running",
                                      "The operator needs to be told which failure this is.")

            End Using
        End Using

    End Function

    ''' <summary>
    ''' The control for the test above, and the distinction G-26 turns on: a
    ''' server that REFUSES a request is reachable. Reporting that as "offline"
    ''' would tell an operator to check the network when the real answer is in
    ''' the error envelope.
    ''' </summary>
    <TestMethod>
    Public Async Function ServerRefusal_IsReportedAsRejectedAndStaysOnline() As Task

        Using handler As StubHttpMessageHandler = RespondWith(
            Function(request)
                If request.RequestUri.AbsolutePath.EndsWith("login", StringComparison.Ordinal) Then
                    Return Ok(LoginPayload())
                End If
                Return Problem(HttpStatusCode.Conflict, "INSUFFICIENT_STOCK", "Not enough stock on hand.")
            End Function)

            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Await client.LoginAsync("clerk", "password")

                Dim decrement = Await client.DecrementStockAsync(1, 9999D, "P1-15", Guid.NewGuid().ToString("D"))

                Assert.AreEqual(ApiOutcome.Rejected, decrement.Outcome)
                Assert.AreEqual(ConnectionState.Online, client.State,
                                "A refusal is not a connection failure.")
                Assert.AreEqual("INSUFFICIENT_STOCK", decrement.Error.ErrorCode)
                Assert.IsFalse(String.IsNullOrWhiteSpace(decrement.CorrelationId),
                               "A rejection must carry the ID an operator quotes.")

            End Using
        End Using

    End Function

    ''' <summary>
    ''' Done-when box 3, the heart of G-26: a write that failed on an
    ''' unreachable API is not held, not retried in the background, and not
    ''' re-sent when the API returns. Exactly one decrement leaves the client.
    ''' </summary>
    <TestMethod>
    Public Async Function UnreachableWrite_IsNeverQueuedOrReplayedAfterRecovery() As Task

        Dim apiIsDown As Boolean = False

        Using handler As StubHttpMessageHandler = RespondWith(
            Function(request)
                If apiIsDown Then
                    Throw ConnectionRefused()
                End If
                If request.RequestUri.AbsolutePath.EndsWith("login", StringComparison.Ordinal) Then
                    Return Ok(LoginPayload())
                End If
                Return Ok(New With {.username = "clerk", .roles = New String() {"InventoryClerk"}})
            End Function)

            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Await client.LoginAsync("clerk", "password")

                apiIsDown = True
                Dim refused = Await client.DecrementStockAsync(1, 1D, "P1-15", Guid.NewGuid().ToString("D"))
                Assert.AreEqual(ApiOutcome.Unavailable, refused.Outcome)

                ' The API comes back. Nothing the client does from here may
                ' resurrect the failed write.
                apiIsDown = False
                Dim identity = Await client.GetCurrentUserAsync()
                Assert.IsTrue(identity.IsSuccess)
                Assert.AreEqual(ConnectionState.Online, client.State)

                Dim decrementAttempts As Integer =
                    handler.Requests.Where(Function(r) r.Path.EndsWith("stock/decrement", StringComparison.Ordinal)).Count()

                Assert.AreEqual(1, decrementAttempts,
                                "The failed write was re-sent. An online-only client must never queue a write.")

            End Using
        End Using

    End Function

    ''' <summary>
    ''' A protected call with no token is refused locally and never reaches the
    ''' wire - and, importantly, does not move the connection state, because
    ''' nothing was learned about the server.
    ''' </summary>
    <TestMethod>
    Public Async Function ProtectedCallWithoutToken_IsRefusedLocallyAndNeverSent() As Task

        Using handler As StubHttpMessageHandler = RespondWith(Function(request) Ok(New With {.ok = True}))
            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Dim identity = Await client.GetCurrentUserAsync()

                Assert.AreEqual(ApiOutcome.Rejected, identity.Outcome)
                Assert.AreEqual("CLIENT_NOT_AUTHENTICATED", identity.Error.ErrorCode)
                Assert.IsEmpty(handler.Requests, "Nothing should have gone onto the wire.")
                Assert.AreEqual(ConnectionState.Unknown, client.State)

            End Using
        End Using

    End Function

    ''' <summary>
    ''' Correlation-ID propagation, one of the card's named deliverables. ADR-014
    ''' has the API reject any non-canonical value with a controlled 400, so the
    ''' "D" format is a contract rather than a style choice - and each call gets
    ''' its own.
    ''' </summary>
    <TestMethod>
    Public Async Function EveryRequest_CarriesItsOwnCanonicalCorrelationId() As Task

        Using handler As StubHttpMessageHandler = RespondWith(
            Function(request)
                If request.RequestUri.AbsolutePath.EndsWith("login", StringComparison.Ordinal) Then
                    Return Ok(LoginPayload())
                End If
                Return Ok(New With {.username = "clerk", .roles = New String() {"InventoryClerk"}})
            End Function)

            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Await client.LoginAsync("clerk", "password")
                Await client.GetCurrentUserAsync()

                Assert.HasCount(2, handler.Requests)

                For Each recorded In handler.Requests

                    Dim parsed As Guid
                    Assert.IsTrue(Guid.TryParseExact(recorded.CorrelationId, "D", parsed),
                                  $"'{recorded.CorrelationId}' is not a canonical UUID; ADR-014 has the API reject it.")

                Next

                Assert.AreNotEqual(handler.Requests(0).CorrelationId,
                                   handler.Requests(1).CorrelationId,
                                   "Each call needs its own correlation ID or two actions share one audit trail.")

            End Using
        End Using

    End Function

    ''' <summary>The token is presented as a bearer credential once held.</summary>
    <TestMethod>
    Public Async Function AfterSignIn_ProtectedCallsCarryTheBearerToken() As Task

        Using handler As StubHttpMessageHandler = RespondWith(
            Function(request)
                If request.RequestUri.AbsolutePath.EndsWith("login", StringComparison.Ordinal) Then
                    Return Ok(LoginPayload())
                End If
                Return Ok(New With {.username = "clerk", .roles = New String() {"InventoryClerk"}})
            End Function)

            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Await client.LoginAsync("clerk", "password")
                Await client.GetCurrentUserAsync()

                Dim protectedCall = handler.Requests.Last()
                Assert.AreEqual("Bearer " & FixtureToken, protectedCall.Authorization)

                Dim loginCall = handler.Requests.First()
                Assert.AreEqual(String.Empty, loginCall.Authorization,
                                "The login call itself cannot carry a token.")

            End Using
        End Using

    End Function

    ''' <summary>
    ''' Done-when box 4: "token never written to disk".
    '''
    ''' Asserted empirically rather than by inspection. Every file created or
    ''' modified during the sign-in, in any place this application could
    ''' plausibly persist to, is read back and searched for the token value. A
    ''' persistence path added later - a settings file, a cached session, a log
    ''' line that helpfully includes the token - fails this test without anyone
    ''' having to remember to look for it.
    '''
    ''' The scan is TARGETED, and the honest limit is worth stating: it covers
    ''' the application directory in full, the four per-application data
    ''' locations in full, and the top level of LOCALAPPDATA, APPDATA and TEMP.
    ''' It is not a whole-disk proof. Recursing all of AppData turned a
    ''' millisecond test into a multi-minute one, and a test slow enough to be
    ''' skipped proves less than a fast one that is actually run.
    ''' </summary>
    <TestMethod>
    Public Async Function SignIn_WritesTheTokenNowhereOnDisk() As Task

        Dim localAppData As String = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        Dim roamingAppData As String = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        Dim programData As String = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)

        ' Searched all the way down: the app's own directory, and every place
        ' this system is documented to keep per-application state.
        Dim deepRoots As String() = {
            AppContext.BaseDirectory,
            Path.Combine(localAppData, "MerchandisingSystem"),
            Path.Combine(roamingAppData, "MerchandisingSystem"),
            Path.Combine(programData, "MerchandisingSystem"),
            Path.Combine(localAppData, "IsolatedStorage")
        }

        ' Searched one level deep only - enough to catch a stray file dropped
        ' at the root of a user profile folder without walking a whole profile.
        Dim shallowRoots As String() = {localAppData, roamingAppData, Path.GetTempPath()}

        Dim startedUtc As DateTime = DateTime.UtcNow.AddSeconds(-1)

        Using handler As StubHttpMessageHandler = RespondWith(Function(request) Ok(LoginPayload()))
            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Dim signIn = Await client.LoginAsync("clerk", "password")

                Assert.IsTrue(signIn.IsSuccess)
                Assert.IsTrue(client.IsAuthenticated, "The token must be held in memory.")

                Dim offenders As New List(Of String)()

                For Each root As String In deepRoots.Distinct(StringComparer.OrdinalIgnoreCase)
                    offenders.AddRange(FilesTouchedSince(root, startedUtc, recurse:=True).
                                       Where(AddressOf ContainsFixtureToken))
                Next

                For Each root As String In shallowRoots.Distinct(StringComparer.OrdinalIgnoreCase)
                    offenders.AddRange(FilesTouchedSince(root, startedUtc, recurse:=False).
                                       Where(AddressOf ContainsFixtureToken))
                Next

                Assert.IsEmpty(offenders,
                               "The session token reached disk: " & String.Join(", ", offenders))

            End Using
        End Using

    End Function

    ''' <summary>Disposing the client drops the token.</summary>
    <TestMethod>
    Public Async Function Dispose_DiscardsTheHeldToken() As Task

        Using handler As StubHttpMessageHandler = RespondWith(Function(request) Ok(LoginPayload()))

            Dim client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

            Await client.LoginAsync("clerk", "password")
            Assert.IsTrue(client.IsAuthenticated)

            client.Dispose()

            Assert.IsFalse(client.IsAuthenticated, "A disposed client must not still hold a credential.")

        End Using

    End Function

    ''' <summary>Files under <paramref name="root"/> written since a moment, best effort.</summary>
    Private Shared Iterator Function FilesTouchedSince(root As String,
                                                       sinceUtc As DateTime,
                                                       recurse As Boolean) As IEnumerable(Of String)

        If String.IsNullOrWhiteSpace(root) OrElse Not Directory.Exists(root) Then
            Return
        End If

        Dim options As New EnumerationOptions With {
            .RecurseSubdirectories = recurse,
            .IgnoreInaccessible = True,
            .AttributesToSkip = FileAttributes.ReparsePoint
        }

        For Each path As String In Directory.EnumerateFiles(root, "*", options)

            Dim written As DateTime

            Try
                written = File.GetLastWriteTimeUtc(path)
            Catch ex As IOException
                ' A file that cannot be stat'ed cannot be read either. Skipping
                ' it is safe here: it was not written by this test's sign-in.
                Continue For
            Catch ex As UnauthorizedAccessException
                Continue For
            End Try

            If written >= sinceUtc Then
                Yield path
            End If

        Next

    End Function

    ''' <summary>True when the file's bytes contain the fixture token.</summary>
    Private Shared Function ContainsFixtureToken(path As String) As Boolean

        Try
            Dim info As New FileInfo(path)

            ' A token is a short string; nothing plausible hides it in a file
            ' larger than this, and reading gigabytes would make the sweep
            ' useless in practice.
            If info.Length = 0 OrElse info.Length > 8L * 1024L * 1024L Then
                Return False
            End If

            Return File.ReadAllText(path).Contains(FixtureToken, StringComparison.Ordinal)

        Catch ex As IOException
            Return False
        Catch ex As UnauthorizedAccessException
            Return False
        End Try

    End Function

    ''' <summary>Builds a handler from a responder.</summary>
    Private Shared Function RespondWith(responder As Func(Of HttpRequestMessage, HttpResponseMessage)) As StubHttpMessageHandler

        Return New StubHttpMessageHandler(responder)

    End Function

    ''' <summary>The exception Windows raises when nothing is listening.</summary>
    Private Shared Function ConnectionRefused() As HttpRequestException

        Return New HttpRequestException("No connection could be made because the target machine actively refused it.",
                                        New SocketException(CInt(SocketError.ConnectionRefused)))

    End Function

    ''' <summary>A login response carrying the searchable fixture token.</summary>
    Private Shared Function LoginPayload() As Object

        Return New With {
            .token = FixtureToken,
            .expiresAtUtc = DateTime.UtcNow.AddHours(8),
            .username = "clerk",
            .roles = New String() {"InventoryClerk"}
        }

    End Function

    ''' <summary>A 200 carrying <paramref name="payload"/> as JSON.</summary>
    Private Shared Function Ok(payload As Object) As HttpResponseMessage

        Return Json(HttpStatusCode.OK, payload)

    End Function

    ''' <summary>An error status carrying the API's error envelope (ADR-014).</summary>
    Private Shared Function Problem(status As HttpStatusCode, errorCode As String, message As String) As HttpResponseMessage

        Return Json(status, New With {
            .errorCode = errorCode,
            .message = message,
            .correlationId = Guid.NewGuid().ToString("D")
        })

    End Function

    Private Shared Function Json(status As HttpStatusCode, payload As Object) As HttpResponseMessage

        Dim response As New HttpResponseMessage(status) With {
            .Content = New StringContent(JsonSerializer.Serialize(payload, payload.GetType()),
                                         Encoding.UTF8,
                                         "application/json")
        }

        response.Headers.TryAddWithoutValidation(MerchandisingApiClient.CorrelationIdHeaderName,
                                                 Guid.NewGuid().ToString("D"))

        Return response

    End Function

End Class
