' Merchandising.Tests.Integration.ApiHttpTests
'
' P1-19: the first tests in this project that go through the HTTP pipeline
' rather than calling a service directly.
'
' WHY THIS LAYER EARNS ITS KEEP. Every other integration test here drives
' AuthService or StockService in-process. That is the right way to prove a
' transaction, and it is structurally blind to an entire class of defect:
' routing, model binding, the authentication handler, MIDDLEWARE ORDER, and
' the error envelope are all invisible to it.
'
' P1-10 proved that is not a theoretical worry. Before its fix, an
' unauthenticated POST /api/v1/auth/login carrying an over-long
' X-Correlation-Id returned HTTP 500 as text/plain with a MySqlException, a
' 25-frame stack trace, the internal call chain, and absolute source paths on
' the developer's disk. Program.vb had registered no exception handler at all.
' No service-level test could see it. The tests below stand on that path.
'
' Scope, per Phase 1 discipline: these prove the PIPELINE, not the business
' rules. Stock arithmetic, concurrency, rollback and idempotency are already
' proven against the real database in StockDecrementTests, and re-testing them
' over HTTP would add running time and no information.

Imports System.Net
Imports System.Net.Http
Imports System.Text
Imports System.Text.Json
Imports System.Threading.Tasks
Imports Merchandising.Contracts.Errors
Imports Microsoft.AspNetCore.Mvc.Testing
Imports Microsoft.VisualStudio.TestTools.UnitTesting

''' <summary>API behaviour observed over HTTP, through the real pipeline.</summary>
<TestClass>
Public Class ApiHttpTests

    ''' <summary>The correlation header both sides agree on (ADR-014).</summary>
    Private Const CorrelationHeader As String = "X-Correlation-Id"

    ''' <summary>The plain-HTTP marker (P1-09).</summary>
    Private Const NonProductionHeader As String = "X-Non-Production-Http"

    Private Shared _factory As MerchandisingApiFactory

    <ClassInitialize>
    Public Shared Sub Initialize(context As TestContext)

        _factory = New MerchandisingApiFactory()

    End Sub

    <ClassCleanup>
    Public Shared Sub Cleanup()

        _factory?.Dispose()

    End Sub

    ''' <summary>
    ''' The seam itself: WebApplicationFactory reaches Program, the host
    ''' builds, and a request is routed and served. If this fails, nothing
    ''' else in this file means anything.
    ''' </summary>
    <TestMethod>
    Public Async Function Health_IsServedOverTheRealPipeline() As Task

        Using client As HttpClient = _factory.CreateClient()

            Using response As HttpResponseMessage = Await client.GetAsync("/health")

                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode)
                Assert.AreEqual("application/json", response.Content.Headers.ContentType.MediaType)

            End Using

        End Using

    End Function

    ''' <summary>
    ''' The non-production marker keys on the TRANSPORT, not on the
    ''' environment - NonProductionWarningMiddleware tags any request where
    ''' Request.IsHttps is false. TestServer carries no TLS, so a default
    ''' client is a plain-HTTP request and MUST be tagged.
    '''
    ''' Written the other way round first, asserting the header was absent
    ''' because the host is not Development. That was wrong about the design:
    ''' the middleware never reads the environment, and keying on the scheme
    ''' is the stronger rule, because it tags the actual risk rather than a
    ''' configuration value someone can set incorrectly.
    ''' </summary>
    <TestMethod>
    Public Async Function PlainHttpRequest_IsTaggedNonProduction() As Task

        Using client As HttpClient = _factory.CreateClient()

            Using response As HttpResponseMessage = Await client.GetAsync("/health")

                Assert.IsTrue(response.Headers.Contains(NonProductionHeader),
                              "A plain-HTTP request must be tagged, whatever the environment.")

            End Using

        End Using

    End Function

    ''' <summary>
    ''' The discriminating half: the same pipeline, the same host, a request
    ''' whose scheme is https - and the marker is gone. Without this pair the
    ''' test above would also pass for middleware that tagged every response
    ''' unconditionally, which is exactly the bug that would make the marker
    ''' worthless.
    '''
    ''' P1-09 proved this against live Kestrel listeners; this proves the
    ''' pipeline itself produces it, on a host with no certificate at all.
    ''' </summary>
    <TestMethod>
    Public Async Function HttpsRequest_IsNotTaggedNonProduction() As Task

        Using client As HttpClient = _factory.CreateClient(
            New WebApplicationFactoryClientOptions With {
                .BaseAddress = New Uri("https://merch-host")
            })

            Using response As HttpResponseMessage = Await client.GetAsync("/health")

                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode)
                Assert.IsFalse(response.Headers.Contains(NonProductionHeader),
                               "The non-production marker leaked onto an HTTPS request.")

            End Using

        End Using

    End Function

    ''' <summary>
    ''' Authorization is enforced by the pipeline, not only by the service. A
    ''' protected endpoint with no token is refused before any handler runs.
    ''' </summary>
    <TestMethod>
    Public Async Function ProtectedEndpoint_WithoutToken_Is401() As Task

        Using client As HttpClient = _factory.CreateClient()

            Using response As HttpResponseMessage = Await client.GetAsync("/api/v1/auth/me")

                Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode)

            End Using

        End Using

    End Function

    ''' <summary>A garbage bearer token is refused the same way as no token.</summary>
    <TestMethod>
    Public Async Function ProtectedEndpoint_WithGarbageToken_Is401() As Task

        Using client As HttpClient = _factory.CreateClient()

            client.DefaultRequestHeaders.Add("Authorization", "Bearer not-a-real-token")

            Using response As HttpResponseMessage = Await client.GetAsync("/api/v1/auth/me")

                Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode)

            End Using

        End Using

    End Function

    ''' <summary>
    ''' ADR-014: the response carries a correlation ID whether or not the
    ''' client supplied one. This is what makes an error on a client screen
    ''' findable in AuditLogs.
    ''' </summary>
    <TestMethod>
    Public Async Function Response_CarriesACorrelationIdWhenNoneWasSupplied() As Task

        Using client As HttpClient = _factory.CreateClient()

            Using response As HttpResponseMessage = Await client.GetAsync("/health")

                Dim values As IEnumerable(Of String) = Nothing
                Assert.IsTrue(response.Headers.TryGetValues(CorrelationHeader, values),
                              "Every response must carry a correlation ID.")

                Dim parsed As Guid
                Assert.IsTrue(Guid.TryParseExact(values.First(), "D", parsed),
                              "The server-generated correlation ID must be a canonical UUID.")

            End Using

        End Using

    End Function

    ''' <summary>A client-supplied correlation ID is echoed, not replaced.</summary>
    <TestMethod>
    Public Async Function SuppliedCorrelationId_IsEchoedBack() As Task

        Dim supplied As String = Guid.NewGuid().ToString("D")

        Using client As HttpClient = _factory.CreateClient()

            Using request As New HttpRequestMessage(HttpMethod.Get, "/health")

                request.Headers.TryAddWithoutValidation(CorrelationHeader, supplied)

                Using response As HttpResponseMessage = Await client.SendAsync(request)

                    Dim values As IEnumerable(Of String) = Nothing
                    Assert.IsTrue(response.Headers.TryGetValues(CorrelationHeader, values))
                    Assert.AreEqual(supplied, values.First(),
                                    "A client's own correlation ID must survive, or its retry logic cannot tie calls together.")

                End Using

            End Using

        End Using

    End Function

    ''' <summary>
    ''' The P1-10 regression, at the layer where it actually happened. A
    ''' non-canonical correlation ID is refused with a controlled 400 BEFORE
    ''' the value can reach a SQL parameter - and the body is a conforming
    ''' envelope, not a stack trace.
    ''' </summary>
    <TestMethod>
    Public Async Function MalformedCorrelationId_Is400_WithAConformingEnvelope() As Task

        Dim oversized As String = New String("A"c, 100)

        Using client As HttpClient = _factory.CreateClient()

            Using request As New HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")

                request.Headers.TryAddWithoutValidation(CorrelationHeader, oversized)
                request.Content = New StringContent("{""username"":""x"",""password"":""y""}",
                                                    Encoding.UTF8, "application/json")

                Using response As HttpResponseMessage = Await client.SendAsync(request)

                    Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode)

                    Dim body As String = Await response.Content.ReadAsStringAsync()

                    AssertNoInternalsLeaked(body)

                    Dim envelope As ApiErrorResponse =
                        JsonSerializer.Deserialize(Of ApiErrorResponse)(body,
                            New JsonSerializerOptions With {.PropertyNameCaseInsensitive = True})

                    Assert.IsNotNull(envelope, "The error body must be the ADR-014 envelope.")
                    Assert.IsFalse(String.IsNullOrWhiteSpace(envelope.ErrorCode))
                    Assert.IsFalse(String.IsNullOrWhiteSpace(envelope.CorrelationId))

                End Using

            End Using

        End Using

    End Function

    ''' <summary>
    ''' Wrong credentials produce a conforming envelope and disclose nothing -
    ''' this one reaches the real MariaDB, which is the point of running it
    ''' here rather than in the unit project.
    ''' </summary>
    <TestMethod>
    Public Async Function Login_WithWrongCredentials_ReturnsEnvelopeAndLeaksNothing() As Task

        Using client As HttpClient = _factory.CreateClient()

            Dim payload As New StringContent(
                "{""username"":""no-such-user-p1-19"",""password"":""definitely-wrong""}",
                Encoding.UTF8, "application/json")

            Using response As HttpResponseMessage = Await client.PostAsync("/api/v1/auth/login", payload)

                Assert.AreNotEqual(HttpStatusCode.InternalServerError, response.StatusCode,
                                   "An unknown username must never reach the 500 path.")

                Dim body As String = Await response.Content.ReadAsStringAsync()
                AssertNoInternalsLeaked(body)

            End Using

        End Using

    End Function

    ''' <summary>
    ''' P6-07 / spec section 15, "Location": the backup directory is
    ''' "outside the application binaries and not served by the API".
    ''' Before this test that claim rested on there being no
    ''' UseStaticFiles/route for it anywhere in Program.vb - true by
    ''' inspection, never by assertion. A static-file handler added later
    ''' for an unrelated reason (serving generated reports, say) would start
    ''' serving this directory too unless it were explicitly excluded, and
    ''' only a request-level test would notice.
    ''' </summary>
    <TestMethod>
    Public Async Function BackupDirectory_RequestedThroughTheApi_Is404() As Task

        Using client As HttpClient = _factory.CreateClient()

            ' Shaped after the real default (BackupSettings.Directory =
            ' "C:\MerchandisingBackups") and a couple of plausible route
            ' names an operator-facing feature might have used instead.
            Dim requestPaths As String() = {
                "/MerchandisingBackups/merchandising-20260101-000000.sql",
                "/backups/merchandising-20260101-000000.sql",
                "/api/v1/backups"
            }

            For Each requestPath As String In requestPaths

                Using response As HttpResponseMessage = Await client.GetAsync(requestPath)

                    Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode,
                                    $"'{requestPath}' should be refused with 404 - the API must never serve backup files.")

                End Using

            Next

        End Using

    End Function

    ''' <summary>
    ''' The leak check from P1-10, applied to a response body. Kept short and
    ''' specific: these are the fragments that actually appeared when the
    ''' defect was live.
    ''' </summary>
    Private Shared Sub AssertNoInternalsLeaked(body As String)

        Dim forbidden As String() = {
            "MySqlException",
            "MySqlConnector",
            "Stack trace",
            "   at ",
            "C:\Users\",
            "Merchandising.Api.Controllers",
            "Password=",
            "Server=127.0.0.1"
        }

        For Each fragment As String In forbidden

            Assert.IsFalse(body.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                           $"The response body leaked '{fragment}'. Body: {body}")

        Next

    End Sub

End Class
