' Merchandising.Api.Middleware.NonProductionWarningMiddleware
'
' P1-09 / spec section 8: "The development profile may use HTTP only on an
' isolated developer machine and must display a non-production warning."
' Tags every plain-HTTP response with a header and a one-line log entry, so
' the dev HTTP listener (loopback only, Development environment only - see
' Program.vb) can never be mistaken for the HTTPS demo listener on 8443 by
' whoever is looking at the traffic. HTTPS responses, including HTTPS
' responses served while running in the Development environment, are never
' tagged - this middleware keys on the request's own scheme, not on the
' hosting environment.

Imports Microsoft.AspNetCore.Http
Imports Microsoft.Extensions.Logging

Namespace Middleware

    Public NotInheritable Class NonProductionWarningMiddleware

        Public Const HeaderName As String = "X-Non-Production-Http"

        Private ReadOnly _next As RequestDelegate
        Private ReadOnly _logger As ILogger(Of NonProductionWarningMiddleware)

        Public Sub New(nextMiddleware As RequestDelegate, logger As ILogger(Of NonProductionWarningMiddleware))
            _next = nextMiddleware
            _logger = logger
        End Sub

        Public Async Function InvokeAsync(context As HttpContext) As Task

            If Not context.Request.IsHttps Then
                context.Response.Headers(HeaderName) = "true - development HTTP profile, never use for a demonstration"
                _logger.LogWarning(
                    "Non-production HTTP request received on {Path}. This listener is loopback only and must never be reached from another machine.",
                    context.Request.Path)
            End If

            Await _next(context)

        End Function

    End Class

End Namespace
