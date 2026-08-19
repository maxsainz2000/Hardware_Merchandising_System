' Merchandising.Api.Middleware.CorrelationIdMiddleware
'
' Stamps every request with a correlation Id, reusing one the caller
' supplied (X-Correlation-Id) so a client's own retry logic can tie related
' calls together, or minting a new Guid otherwise. Stored on HttpContext.
' Items for the rest of the pipeline (audit rows, error bodies) and echoed
' back as a response header.
'
' First consumer is P1-08's auth audit trail and error responses
' (CLAUDE.md section 5: every error response carries a correlation Id).
' The same Id shape (a Guid) is what StockMovements.CorrelationId and
' IdempotencyKeys.KeyValue already use, so a later task's business commands
' can adopt this middleware instead of minting their own.
'
' P1-10 - VALIDATION ADDED, AND WHY IT IS A SECURITY FIX RATHER THAN TIDYING.
' Until this card the supplied header was trusted verbatim. P1-08 spotted the
' consequence and deferred it (evidence/phase-1/p1-08-log-scan.txt):
' AuditLogs.CorrelationId is CHAR(36) NOT NULL, so under STRICT_TRANS_TABLES
' (CLAUDE.md section 6.3) a longer value raises ERROR 1406 instead of
' truncating - and a MySqlException message names the table and the column.
' A caller could therefore extract schema detail through a 500 by sending
' nothing but a long header. That is precisely what P1-10's box 4 forbids, so
' it is fixed here rather than deferred again.
'
' Requiring a well-formed Guid closes three holes with one rule:
'   over-length      - the ERROR 1406 path above
'   CR/LF injection  - the value is echoed into a RESPONSE header, so a raw
'                      CR/LF is a response-splitting attempt; Kestrel would
'                      throw on write, turning it into another 500
'   non-Guid junk    - would satisfy CHAR(36) and silently pollute the
'                      movement/audit join keys the Track D cards rely on
'
' Conforming clients see no change: Guid.NewGuid().ToString() is the shape
' this system already generates everywhere.

Imports System.Text.Json
Imports Merchandising.Contracts.Errors
Imports Microsoft.AspNetCore.Http

Namespace Middleware

    Public NotInheritable Class CorrelationIdMiddleware

        Public Const HeaderName As String = "X-Correlation-Id"
        Public Const ItemsKey As String = "CorrelationId"

        ''' <summary>Stable error code for a malformed correlation Id.</summary>
        Public Const InvalidCorrelationIdErrorCode As String = "INVALID_CORRELATION_ID"

        Private ReadOnly _next As RequestDelegate

        Public Sub New(nextMiddleware As RequestDelegate)
            _next = nextMiddleware
        End Sub

        Public Async Function InvokeAsync(context As HttpContext) As Task

            Dim supplied As String = context.Request.Headers(HeaderName).ToString()
            Dim correlationId As String

            If String.IsNullOrWhiteSpace(supplied) Then

                ' Absent, or whitespace only, is not an error - it is the
                ' ordinary case for a caller that does not correlate.
                correlationId = Guid.NewGuid().ToString()

            ElseIf IsWellFormedCorrelationId(supplied) Then

                correlationId = supplied

            Else

                Await WriteRejectionAsync(context)
                Return

            End If

            context.Items(ItemsKey) = correlationId
            context.Response.Headers(HeaderName) = correlationId

            Await _next(context)

        End Function

        ''' <summary>
        ''' True only for the canonical 36-character "D" format, which is what
        ''' <c>Guid.NewGuid().ToString()</c> produces and what CHAR(36) holds.
        ''' </summary>
        ''' <remarks>
        ''' <c>Guid.TryParse</c> alone is too permissive here: it also accepts
        ''' the "B", "P", "N" and "X" formats - braces, parentheses, 32
        ''' undashed characters, and a hex-object literal. Those parse, but
        ''' round-trip to a different string than the caller sent, so the Id
        ''' echoed in the response header would not match the one the caller
        ''' quoted. TryParseExact with "D" keeps supplied and stored identical.
        ''' </remarks>
        Private Shared Function IsWellFormedCorrelationId(value As String) As Boolean

            Dim parsed As Guid = Guid.Empty
            Return Guid.TryParseExact(value, "D", parsed) AndAlso parsed <> Guid.Empty

        End Function

        ''' <summary>
        ''' Refuses the request before anything can bind the value as a SQL
        ''' parameter, with a complete envelope of its own (spec section 13).
        ''' </summary>
        Private Shared Async Function WriteRejectionAsync(context As HttpContext) As Task

            ' A freshly minted Id: the supplied one is exactly what is being
            ' refused, so it cannot be reused - but the response still carries
            ' one, because every error response does. It is stamped onto Items
            ' as well, so if anything downstream of this rejection logs, it
            ' logs the same Id the caller was handed.
            Dim correlationId As String = Guid.NewGuid().ToString()
            context.Items(ItemsKey) = correlationId

            context.Response.StatusCode = StatusCodes.Status400BadRequest
            context.Response.ContentType = "application/json; charset=utf-8"
            context.Response.Headers(HeaderName) = correlationId

            ' The rejected value is deliberately NOT quoted back. Echoing it
            ' would reflect attacker-controlled content of arbitrary length
            ' into the response body, which is the other half of the injection
            ' problem this validation exists to stop.
            Dim body As New ApiErrorResponse With {
                .ErrorCode = InvalidCorrelationIdErrorCode,
                .Message = "The supplied correlation ID header is not valid.",
                .CorrelationId = correlationId,
                .Errors = New Dictionary(Of String, String()) From {
                    {HeaderName, New String() {
                        "Must be a UUID in the canonical 36-character form, for example " &
                        "3f2504e0-4f89-41d3-9a0c-0305e82c3301. Omit the header entirely to have one assigned."}}
                }
            }

            ' Reflection-based serialization, never source-generated: the
            ' System.Text.Json generator is C#-only (CLAUDE.md section 3).
            Await context.Response.WriteAsync(JsonSerializer.Serialize(body))

        End Function

    End Class

End Namespace
