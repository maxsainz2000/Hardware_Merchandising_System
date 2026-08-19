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

Imports Microsoft.AspNetCore.Http

Namespace Middleware

    Public NotInheritable Class CorrelationIdMiddleware

        Public Const HeaderName As String = "X-Correlation-Id"
        Public Const ItemsKey As String = "CorrelationId"

        Private ReadOnly _next As RequestDelegate

        Public Sub New(nextMiddleware As RequestDelegate)
            _next = nextMiddleware
        End Sub

        Public Async Function InvokeAsync(context As HttpContext) As Task

            Dim correlationId As String = context.Request.Headers(HeaderName).ToString()

            If String.IsNullOrWhiteSpace(correlationId) Then
                correlationId = Guid.NewGuid().ToString()
            End If

            context.Items(ItemsKey) = correlationId
            context.Response.Headers(HeaderName) = correlationId

            Await _next(context)

        End Function

    End Class

End Namespace
