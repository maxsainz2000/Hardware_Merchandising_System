' Merchandising.Api.Controllers.AdminController
'
' P1-08's "one policy-gated endpoint" (the card's own wording) - proof that
' role-based authorization is enforced by the API, not by hiding a button
' in a client (spec section 9). Deliberately not a real business endpoint:
' Phase 1 scope discipline is one WPF window, two buttons, one product, one
' protected endpoint, and this is that endpoint, in the same "proof, not
' feature" spirit as HealthController from P1-02. A real Admin-only
' capability arrives with whichever Phase 2 feature actually needs one.

Imports System.Linq
Imports System.Security.Claims
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Mvc
Imports Merchandising.Api.Security

Namespace Controllers

    <ApiController>
    <Route("api/v1/admin")>
    <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Roles:="Admin,SuperAdmin")>
    Public Class AdminController
        Inherits ControllerBase

        ''' <summary>
        ''' Returns 200 only for a caller holding Admin or SuperAdmin - 401
        ''' with no token, 403 for a valid token in some other role. Carries
        ''' nothing sensitive; its only purpose is to be gated.
        ''' </summary>
        <HttpGet("ping")>
        Public Function Ping() As IActionResult

            Dim payload = New With {
                .status = "ok",
                .username = User.Identity.Name,
                .roles = User.FindAll(ClaimTypes.Role).Select(Function(c) c.Value).ToList()
            }

            Return Ok(payload)

        End Function

    End Class

End Namespace
