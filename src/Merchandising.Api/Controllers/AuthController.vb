' Merchandising.Api.Controllers.AuthController
'
' POST /api/v1/auth/login, GET /api/v1/auth/me, POST /api/v1/auth/logout -
' spec section 13's Authentication row. Login is the only anonymous action
' under /api/v1 (spec section 13: "Anonymous only for login; authenticated
' for others").

Imports System.Linq
Imports System.Security.Claims
Imports Merchandising.Api.Middleware
Imports Merchandising.Api.Security
Imports Merchandising.Contracts.Auth
Imports Merchandising.Contracts.Errors
Imports Merchandising.Domain.Security
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Mvc

Namespace Controllers

    <ApiController>
    <Route("api/v1/auth")>
    Public Class AuthController
        Inherits ControllerBase

        Private ReadOnly _authService As AuthService

        Public Sub New(authService As AuthService)
            _authService = authService
        End Sub

        ''' <summary>
        ''' Authenticates a username/password pair and, on success, issues a
        ''' new session token. Every outcome - including "no such user" and
        ''' "wrong password" - returns the same 401 body, so a caller cannot
        ''' use this endpoint to enumerate valid usernames.
        ''' </summary>
        <AllowAnonymous>
        <AuditRequired>
        <HttpPost("login")>
        Public Async Function Login(<FromBody> request As LoginRequest) As Task(Of IActionResult)

            Dim fieldErrors As New Dictionary(Of String, String())

            If request Is Nothing OrElse String.IsNullOrWhiteSpace(request.Username) Then
                fieldErrors("username") = {"Username is required."}
            End If

            If request Is Nothing OrElse String.IsNullOrWhiteSpace(request.Password) Then
                fieldErrors("password") = {"Password is required."}
            End If

            If fieldErrors.Count > 0 Then
                Return ValidationError(fieldErrors)
            End If

            Dim correlationId As String = HttpContext.GetCorrelationId()

            Dim outcome As LoginOutcome =
                Await _authService.LoginAsync(request.Username, request.Password, correlationId)

            Select Case outcome.Kind

                Case LoginOutcomeKind.Success
                    Return Ok(outcome.Response)

                Case LoginOutcomeKind.Locked
                    Return StatusCode(423, New ApiErrorResponse With {
                        .ErrorCode = "ACCOUNT_LOCKED",
                        .Message = "This account is temporarily locked after too many failed sign-in attempts. Try again later.",
                        .CorrelationId = correlationId
                    })

                Case Else
                    Return Unauthorized(New ApiErrorResponse With {
                        .ErrorCode = "INVALID_CREDENTIALS",
                        .Message = "Username or password is incorrect.",
                        .CorrelationId = correlationId
                    })

            End Select

        End Function

        ''' <summary>
        ''' The identity a valid session token resolves to. Any authenticated
        ''' role may call this - it is not the policy-gated endpoint.
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName)>
        <HttpGet("me")>
        Public Function GetCurrentUser() As IActionResult

            Dim response As New MeResponse With {
                .Username = User.Identity.Name,
                .Roles = User.FindAll(ClaimTypes.Role).Select(Function(c) c.Value).ToList()
            }

            Return Ok(response)

        End Function

        ''' <summary>
        ''' Ends the caller's own session. Requires a valid token to reach
        ''' at all (the [Authorize] below), and deletes exactly that token's
        ''' row - a client can only log itself out, never another session.
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName)>
        <AuditRequired>
        <HttpPost("logout")>
        Public Async Function Logout() As Task(Of IActionResult)

            Dim header As String = Request.Headers.Authorization.ToString()
            Const prefix As String = "Bearer "
            Dim token As String = header.Substring(prefix.Length).Trim()

            Dim userId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))
            Dim username As String = User.Identity.Name
            Dim correlationId As String = HttpContext.GetCorrelationId()

            Await _authService.LogoutAsync(token, userId, username, correlationId)

            Return NoContent()

        End Function

        Private Function ValidationError(fieldErrors As Dictionary(Of String, String())) As IActionResult

            Return BadRequest(New ApiErrorResponse With {
                .ErrorCode = "VALIDATION_FAILED",
                .Message = "Username and password are both required.",
                .CorrelationId = HttpContext.GetCorrelationId(),
                .Errors = fieldErrors
            })

        End Function

    End Class

End Namespace
