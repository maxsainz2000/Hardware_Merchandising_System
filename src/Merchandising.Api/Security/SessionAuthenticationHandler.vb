' Merchandising.Api.Security.SessionAuthenticationHandler
'
' The "Session" authentication scheme (ADR-005): reads "Authorization:
' Bearer <token>", hashes the token, and resolves it through
' SessionRepository against Sessions/Users/UserRoles. No JWT parsing, no
' signing key - the token has no meaning outside a database lookup, which
' is exactly the property that makes logout able to genuinely revoke it.
'
' Also owns the 401/403 response bodies (HandleChallengeAsync,
' HandleForbiddenAsync) so an unauthenticated or wrong-role request gets
' the same ApiErrorResponse shape as everything else, not ASP.NET Core's
' bare empty-body default (CLAUDE.md section 5).

Imports System.Globalization
Imports System.Net.Mime
Imports System.Security.Claims
Imports System.Text.Encodings.Web
Imports System.Text.Json
Imports System.Threading.Tasks
Imports Merchandising.Contracts.Errors
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Api.Middleware
Imports Microsoft.AspNetCore.Authentication
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options

Namespace Security

    Public NotInheritable Class SessionAuthenticationHandler
        Inherits AuthenticationHandler(Of AuthenticationSchemeOptions)

        Public Const SchemeName As String = "Session"

        Private ReadOnly _connectionFactory As ConnectionFactory

        Public Sub New(
            options As IOptionsMonitor(Of AuthenticationSchemeOptions),
            logger As ILoggerFactory,
            encoder As UrlEncoder,
            connectionFactory As ConnectionFactory)

            MyBase.New(options, logger, encoder)
            _connectionFactory = connectionFactory

        End Sub

        Protected Overrides Async Function HandleAuthenticateAsync() As Task(Of AuthenticateResult)

            Dim header As String = Request.Headers.Authorization.ToString()

            If String.IsNullOrWhiteSpace(header) Then
                Return AuthenticateResult.NoResult()
            End If

            Const prefix As String = "Bearer "
            If Not header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) Then
                Return AuthenticateResult.NoResult()
            End If

            Dim token As String = header.Substring(prefix.Length).Trim()
            If token.Length = 0 Then
                Return AuthenticateResult.Fail("Empty bearer token.")
            End If

            Dim tokenHash As String = SessionTokenGenerator.Hash(token)

            Using connection = Await _connectionFactory.CreateOpenConnectionAsync().ConfigureAwait(False)

                Dim principal As SessionPrincipal =
                    Await SessionRepository.FindActiveByTokenHashAsync(connection, tokenHash).ConfigureAwait(False)

                If principal Is Nothing Then
                    Return AuthenticateResult.Fail("Invalid or expired session token.")
                End If

                Dim claims As New List(Of Claim) From {
                    New Claim(ClaimTypes.NameIdentifier, principal.UserId.ToString(CultureInfo.InvariantCulture)),
                    New Claim(ClaimTypes.Name, principal.Username)
                }

                For Each roleName As String In principal.Roles
                    claims.Add(New Claim(ClaimTypes.Role, roleName))
                Next

                Dim identity As New ClaimsIdentity(claims, Scheme.Name)
                Dim claimsPrincipal As New ClaimsPrincipal(identity)
                Dim ticket As New AuthenticationTicket(claimsPrincipal, Scheme.Name)

                Return AuthenticateResult.Success(ticket)

            End Using

        End Function

        Protected Overrides Function HandleChallengeAsync(properties As AuthenticationProperties) As Task
            Return WriteErrorAsync(401, "UNAUTHORIZED", "Authentication is required for this endpoint.")
        End Function

        Protected Overrides Function HandleForbiddenAsync(properties As AuthenticationProperties) As Task
            Return WriteErrorAsync(403, "FORBIDDEN", "This account does not hold a role authorized for this endpoint.")
        End Function

        Private Async Function WriteErrorAsync(statusCode As Integer, errorCode As String, message As String) As Task

            Dim body As New ApiErrorResponse With {
                .ErrorCode = errorCode,
                .Message = message,
                .CorrelationId = Context.GetCorrelationId()
            }

            Context.Response.StatusCode = statusCode
            Context.Response.ContentType = MediaTypeNames.Application.Json

            Await JsonSerializer.SerializeAsync(Context.Response.Body, body).ConfigureAwait(False)

        End Function

    End Class

End Namespace
