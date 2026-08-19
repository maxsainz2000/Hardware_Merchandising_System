' Merchandising.Tests.Integration.ExceptionHandlingMiddlewareTests
'
' P1-10 evidence, done-when box 4: "Error bodies carry a correlation ID and
' NO stack trace, SQL, or connection detail" (spec section 13, section 17
' "Input security ... safe error handling", CLAUDE.md section 5).
'
' WHY THIS SUITE EXISTS AT ALL. Before P1-10, Program.vb registered no
' exception handler of any kind. That produced two different failures of the
' same box depending on environment:
'
'   Development - WebApplication auto-registers DeveloperExceptionPage as
'                 the first middleware, so an unhandled exception returned a
'                 full stack trace to the caller.
'   Production  - nothing handled it, so the caller got a bare 500 with an
'                 empty body: no error code, no message, no correlation Id,
'                 which spec section 13 requires on EVERY error response.
'
' WHY THESE LIVE IN THE INTEGRATION PROJECT DESPITE NEEDING NO DATABASE.
' Merchandising.Tests.Unit references Domain and Contracts only, deliberately
' - it is the domain/calculation suite. This middleware is an Api-layer type,
' and this project is the one already wired to Merchandising.Api. Adding an
' Api reference to the unit project to host four tests would drag ASP.NET
' Core, Infrastructure and MySqlConnector into a project whose whole point is
' not having them. These tests are fast and touch no shared state; parallelism
' is off assembly-wide (MSTestSettings.vb) and that costs nothing here.
'
' WHY THE THROWN EXCEPTION CARRIES FAKE SECRETS. MySqlException has no usable
' public constructor, so a real one cannot be built in a test. What actually
' needs proving is narrower and this exercises it exactly: the middleware must
' never copy Exception.Message into the response body. An exception whose
' message contains SQL text, a connection string and a password proves that
' directly - if any of it reaches the caller, the middleware echoed the
' message.

Imports System.IO
Imports System.Text
Imports System.Text.Json
Imports Merchandising.Api.Middleware
Imports Merchandising.Contracts.Errors
Imports Microsoft.AspNetCore.Http
Imports Microsoft.AspNetCore.Http.Features
Imports Microsoft.VisualStudio.TestTools.UnitTesting

''' <summary>
''' P1-10 box 4: proves the API's error envelope is safe and complete on the
''' unhandled-exception path.
''' </summary>
<TestClass>
Public Class ExceptionHandlingMiddlewareTests

    ''' <summary>
    ''' Every fragment below is something a real MySqlException or .NET
    ''' exception would carry, and none of it may ever reach a caller.
    ''' </summary>
    Private Const LeakyExceptionMessage As String =
        "INSERT INTO AuditLogs (CorrelationId) VALUES (@correlationId) failed: " &
        "Data too long for column 'CorrelationId' at row 1; " &
        "connection Server=127.0.0.1;Port=3306;Database=merchandising;Uid=merch_api;Pwd=S3cr3t-Api-Passw0rd;"

    ''' <summary>
    ''' Builds a context whose response body is a readable MemoryStream.
    ''' DefaultHttpContext's body is Stream.Null, which discards writes.
    ''' </summary>
    Private Shared Function CreateContext(bodyBuffer As MemoryStream) As DefaultHttpContext

        Dim context As New DefaultHttpContext()
        context.Response.Body = bodyBuffer
        Return context

    End Function

    Private Shared Function ReadBody(bodyBuffer As MemoryStream) As String

        Return Encoding.UTF8.GetString(bodyBuffer.ToArray())

    End Function

    ''' <summary>
    ''' A response feature that reports the response as already begun.
    ''' </summary>
    ''' <remarks>
    ''' DefaultHttpContext cannot express this on its own: its response
    ''' feature hard-codes <c>HasStarted = False</c>, and writing to a
    ''' MemoryStream body never flips it, because nothing real is on a wire.
    ''' The first version of this suite wrote-then-flushed and asserted a
    ''' rethrow; it failed with "no exception was thrown" - the TEST was
    ''' wrong, not the middleware. Modelling the state explicitly is the only
    ''' honest way to exercise the branch without a live server.
    ''' </remarks>
    Private NotInheritable Class StartedResponseFeature
        Inherits HttpResponseFeature

        Public Overrides ReadOnly Property HasStarted As Boolean
            Get
                Return True
            End Get
        End Property

    End Class

    ''' <summary>
    ''' Builds a context whose response is already on the wire.
    ''' </summary>
    Private Shared Function CreateStartedContext(bodyBuffer As MemoryStream) As DefaultHttpContext

        Dim features As New FeatureCollection()
        features.Set(Of IHttpRequestFeature)(New HttpRequestFeature())
        features.Set(Of IHttpResponseFeature)(New StartedResponseFeature())
        features.Set(Of IHttpResponseBodyFeature)(New StreamResponseBodyFeature(bodyBuffer))

        Return New DefaultHttpContext(features)

    End Function

    ''' <summary>
    ''' Box 4, first half: an unhandled exception still produces a COMPLETE
    ''' envelope - stable error code, human-readable message, correlation Id.
    ''' Before P1-10 this returned an empty body in Production.
    ''' </summary>
    <TestMethod>
    Public Async Function UnhandledException_ReturnsCompleteErrorEnvelope() As Task

        Using bodyBuffer As New MemoryStream()

            Dim context As DefaultHttpContext = CreateContext(bodyBuffer)
            Dim expectedCorrelationId As String = Guid.NewGuid().ToString()
            context.Items(CorrelationIdMiddleware.ItemsKey) = expectedCorrelationId

            Dim middleware As New ExceptionHandlingMiddleware(
                Function(ctx As HttpContext) As Task
                    Throw New InvalidOperationException("boom")
                End Function)

            Await middleware.InvokeAsync(context)

            Assert.AreEqual(StatusCodes.Status500InternalServerError, context.Response.StatusCode)
            Assert.AreEqual("application/json; charset=utf-8", context.Response.ContentType)

            Dim body As ApiErrorResponse =
                JsonSerializer.Deserialize(Of ApiErrorResponse)(ReadBody(bodyBuffer))

            Assert.IsNotNull(body)
            Assert.IsFalse(String.IsNullOrWhiteSpace(body.ErrorCode))
            Assert.IsFalse(String.IsNullOrWhiteSpace(body.Message))

            ' The caller must be able to quote one Id back to the operator and
            ' have it match the server log - so it is the SAME Id the
            ' correlation middleware already put on the response header, not a
            ' second one minted here.
            Assert.AreEqual(expectedCorrelationId, body.CorrelationId)

        End Using

    End Function

    ''' <summary>
    ''' Box 4, second half - the one that matters for G-03 and G-10: no stack
    ''' trace, no SQL, no connection detail, no password, not even the
    ''' exception type name.
    ''' </summary>
    <TestMethod>
    Public Async Function UnhandledException_BodyLeaksNoInternalDetail() As Task

        Using bodyBuffer As New MemoryStream()

            Dim context As DefaultHttpContext = CreateContext(bodyBuffer)
            context.Items(CorrelationIdMiddleware.ItemsKey) = Guid.NewGuid().ToString()

            Dim middleware As New ExceptionHandlingMiddleware(
                Function(ctx As HttpContext) As Task
                    Throw New InvalidOperationException(LeakyExceptionMessage)
                End Function)

            Await middleware.InvokeAsync(context)

            Dim body As String = ReadBody(bodyBuffer)

            Dim forbiddenFragments As String() = {
                "INSERT INTO", "AuditLogs", "CorrelationId' at row",
                "Server=", "Port=3306", "Database=merchandising",
                "Uid=merch_api", "Pwd=", "S3cr3t-Api-Passw0rd",
                "InvalidOperationException", "Merchandising.Tests", "   at ", ".vb:line"
            }

            For Each fragment As String In forbiddenFragments
                Assert.IsFalse(
                    body.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                    $"Error body leaked '{fragment}'. Full body: {body}")
            Next

        End Using

    End Function

    ''' <summary>
    ''' The middleware must be invisible on the success path - it wraps every
    ''' request, so a bug here would corrupt every response in the system.
    ''' </summary>
    <TestMethod>
    Public Async Function SuccessfulRequest_PassesThroughUntouched() As Task

        Using bodyBuffer As New MemoryStream()

            Dim context As DefaultHttpContext = CreateContext(bodyBuffer)

            Dim middleware As New ExceptionHandlingMiddleware(
                Async Function(ctx As HttpContext) As Task
                    ctx.Response.StatusCode = StatusCodes.Status200OK
                    ctx.Response.ContentType = "application/json; charset=utf-8"
                    Await ctx.Response.WriteAsync("{""status"":""ok""}")
                End Function)

            Await middleware.InvokeAsync(context)

            Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode)
            Assert.AreEqual("{""status"":""ok""}", ReadBody(bodyBuffer))

        End Using

    End Function

    ''' <summary>
    ''' If the response has already begun, headers are gone and a status code
    ''' can no longer be set. Writing an error envelope on top of a partly
    ''' sent body would produce a corrupt response that LOOKS like a success -
    ''' worse than the failure it is reporting. Rethrow and let the host abort
    ''' the connection, which is the honest signal.
    ''' </summary>
    <TestMethod>
    Public Async Function ExceptionAfterResponseStarted_RethrowsRatherThanCorrupting() As Task

        Using bodyBuffer As New MemoryStream()

            Dim context As DefaultHttpContext = CreateStartedContext(bodyBuffer)

            Dim middleware As New ExceptionHandlingMiddleware(
                Async Function(ctx As HttpContext) As Task
                    Await ctx.Response.WriteAsync("{""partial"":true")
                    Throw New InvalidOperationException(LeakyExceptionMessage)
                End Function)

            Dim thrown As InvalidOperationException =
                Await Assert.ThrowsExactlyAsync(Of InvalidOperationException)(
                    Function() middleware.InvokeAsync(context))

            Assert.AreEqual(LeakyExceptionMessage, thrown.Message)

            ' And it must not have appended an envelope to the partial body.
            Assert.IsFalse(ReadBody(bodyBuffer).Contains("errorCode", StringComparison.Ordinal))

        End Using

    End Function

    ''' <summary>
    ''' The handler is the outermost middleware, so it can run before
    ''' CorrelationIdMiddleware has stamped an Id - for instance if that
    ''' middleware itself throws. The envelope still has to carry one, because
    ''' spec section 13 says every error response does.
    ''' </summary>
    <TestMethod>
    Public Async Function UnhandledException_WithNoCorrelationIdStamped_StillCarriesOne() As Task

        Using bodyBuffer As New MemoryStream()

            Dim context As DefaultHttpContext = CreateContext(bodyBuffer)

            Dim middleware As New ExceptionHandlingMiddleware(
                Function(ctx As HttpContext) As Task
                    Throw New InvalidOperationException("thrown before any Id was stamped")
                End Function)

            Await middleware.InvokeAsync(context)

            Dim body As ApiErrorResponse =
                JsonSerializer.Deserialize(Of ApiErrorResponse)(ReadBody(bodyBuffer))

            Assert.IsNotNull(body)

            Dim parsed As Guid = Guid.Empty
            Assert.IsTrue(
                Guid.TryParse(body.CorrelationId, parsed),
                $"Expected a minted Guid, got '{body.CorrelationId}'.")
            Assert.AreNotEqual(Guid.Empty, parsed)

        End Using

    End Function

End Class
