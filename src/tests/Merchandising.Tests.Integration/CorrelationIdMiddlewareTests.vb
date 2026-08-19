' Merchandising.Tests.Integration.CorrelationIdMiddlewareTests
'
' P1-10 evidence, done-when box 4, second front.
'
' THE DEFECT THIS SUITE CLOSES was found and written down at P1-08 but left
' for "whichever later task hardens general request validation"
' (evidence/phase-1/p1-08-log-scan.txt). It lands on P1-10 because it is not
' merely a validation gap - it is an error-body leak, which is exactly what
' box 4 tests:
'
'   AuditLogs.CorrelationId is CHAR(36) NOT NULL (db/migrations/0001).
'   CorrelationIdMiddleware accepted ANY client-supplied X-Correlation-Id and
'   passed it through to that INSERT. Under STRICT_TRANS_TABLES (CLAUDE.md
'   section 6.3) a longer value raises ERROR 1406 rather than truncating, and
'   a MySqlException's message names the table and the column. So a caller
'   could learn schema detail purely by sending a long header.
'
' Rejecting anything that is not a well-formed Guid closes three holes with
' one rule: over-length (ERROR 1406), CR/LF header injection - the value is
' echoed straight back into a response header - and non-Guid junk reaching
' StockMovements.CorrelationId, which the transactional cards will join on.
'
' The 36-character canonical "D" format is what Guid.NewGuid().ToString()
' already produces everywhere else in this system, so a conforming client
' sees no change at all.

Imports System.IO
Imports System.Text
Imports System.Text.Json
Imports Merchandising.Api.Middleware
Imports Merchandising.Contracts.Errors
Imports Microsoft.AspNetCore.Http
Imports Microsoft.VisualStudio.TestTools.UnitTesting

''' <summary>
''' P1-10 box 4: a malformed correlation Id is a controlled 400, not an
''' ERROR 1406 that leaks schema detail through a 500.
''' </summary>
<TestClass>
Public Class CorrelationIdMiddlewareTests

    Private Shared Function CreateContext(bodyBuffer As MemoryStream) As DefaultHttpContext

        Dim context As New DefaultHttpContext()
        context.Response.Body = bodyBuffer
        Return context

    End Function

    Private Shared Function ReadBody(bodyBuffer As MemoryStream) As String

        Return Encoding.UTF8.GetString(bodyBuffer.ToArray())

    End Function

    ''' <summary>
    ''' Asserts the rejection path: 400, next never runs, and the envelope is
    ''' complete despite the supplied Id being unusable.
    ''' </summary>
    Private Shared Async Function AssertRejectedAsync(suppliedCorrelationId As String) As Task

        Using bodyBuffer As New MemoryStream()

            Dim context As DefaultHttpContext = CreateContext(bodyBuffer)
            context.Request.Headers(CorrelationIdMiddleware.HeaderName) = suppliedCorrelationId

            Dim nextWasInvoked As Boolean = False

            Dim middleware As New CorrelationIdMiddleware(
                Function(ctx As HttpContext) As Task
                    nextWasInvoked = True
                    Return Task.CompletedTask
                End Function)

            Await middleware.InvokeAsync(context)

            Assert.AreEqual(StatusCodes.Status400BadRequest, context.Response.StatusCode)

            ' The important half: the request never reached anything that
            ' would have bound it as a SQL parameter.
            Assert.IsFalse(nextWasInvoked, "The pipeline must stop at the malformed correlation Id.")

            Dim body As ApiErrorResponse =
                JsonSerializer.Deserialize(Of ApiErrorResponse)(ReadBody(bodyBuffer))

            Assert.IsNotNull(body)
            Assert.IsFalse(String.IsNullOrWhiteSpace(body.ErrorCode))

            ' A freshly minted Id, because the supplied one is exactly what is
            ' being refused - but an Id all the same (spec section 13).
            Dim parsed As Guid = Guid.Empty
            Assert.IsTrue(
                Guid.TryParse(body.CorrelationId, parsed),
                $"Rejection envelope carried no usable correlation Id: '{body.CorrelationId}'.")

            ' Field-level detail, since this IS a validation failure.
            Assert.IsNotNull(body.Errors)
            Assert.IsTrue(
                body.Errors.ContainsKey(CorrelationIdMiddleware.HeaderName),
                "Validation detail must name the offending header.")

            ' And it must not quote the rejected value back - a caller that
            ' sent a 4 KB header should not get 4 KB reflected at it.
            Assert.IsFalse(
                ReadBody(bodyBuffer).Contains(suppliedCorrelationId, StringComparison.Ordinal),
                "The rejected value must not be echoed back to the caller.")

        End Using

    End Function

    ''' <summary>
    ''' The P1-08 defect itself: 100 characters into a CHAR(36) column.
    ''' </summary>
    <TestMethod>
    Public Async Function OverLengthCorrelationId_IsRejectedWithControlled400() As Task

        Await AssertRejectedAsync(New String("A"c, 100))

    End Function

    ''' <summary>
    ''' Header injection: the value is echoed into a response header, so a
    ''' CR/LF would either split the response or throw inside Kestrel.
    ''' </summary>
    <TestMethod>
    Public Async Function CorrelationIdContainingCrLf_IsRejectedWithControlled400() As Task

        Await AssertRejectedAsync("abc" & vbCrLf & "X-Injected: yes")

    End Function

    ''' <summary>
    ''' Right length, wrong shape - 36 characters that are not a Guid would
    ''' satisfy CHAR(36) and silently pollute the movement/audit join keys.
    ''' </summary>
    <TestMethod>
    Public Async Function CorrelationIdOfCorrectLengthButNotAGuid_IsRejected() As Task

        Await AssertRejectedAsync(New String("z"c, 36))

    End Function

    <TestMethod>
    Public Async Function EmptyCorrelationIdHeader_IsTreatedAsAbsentAndMinted() As Task

        Using bodyBuffer As New MemoryStream()

            Dim context As DefaultHttpContext = CreateContext(bodyBuffer)
            context.Request.Headers(CorrelationIdMiddleware.HeaderName) = "   "

            Dim middleware As New CorrelationIdMiddleware(
                Function(ctx As HttpContext) As Task
                    Return Task.CompletedTask
                End Function)

            Await middleware.InvokeAsync(context)

            Dim stamped As String = CStr(context.Items(CorrelationIdMiddleware.ItemsKey))

            Dim parsed As Guid = Guid.Empty
            Assert.IsTrue(Guid.TryParse(stamped, parsed))
            Assert.AreNotEqual(Guid.Empty, parsed)
            Assert.AreEqual(stamped, context.Response.Headers(CorrelationIdMiddleware.HeaderName).ToString())

        End Using

    End Function

    ''' <summary>
    ''' The behaviour a conforming client depends on: a valid Guid survives
    ''' untouched, so a client's own retry logic can still tie calls together.
    ''' P1-08's auth matrix proved this round-trip live; it is asserted here so
    ''' the new validation cannot silently break it.
    ''' </summary>
    <TestMethod>
    Public Async Function ValidGuidCorrelationId_IsPreservedAndEchoed() As Task

        Using bodyBuffer As New MemoryStream()

            Dim context As DefaultHttpContext = CreateContext(bodyBuffer)
            Dim supplied As String = Guid.NewGuid().ToString()
            context.Request.Headers(CorrelationIdMiddleware.HeaderName) = supplied

            Dim seenByNext As String = Nothing

            Dim middleware As New CorrelationIdMiddleware(
                Function(ctx As HttpContext) As Task
                    seenByNext = CStr(ctx.Items(CorrelationIdMiddleware.ItemsKey))
                    Return Task.CompletedTask
                End Function)

            Await middleware.InvokeAsync(context)

            Assert.AreEqual(supplied, seenByNext)
            Assert.AreEqual(supplied, context.Response.Headers(CorrelationIdMiddleware.HeaderName).ToString())

        End Using

    End Function

End Class
