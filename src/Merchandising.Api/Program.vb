' Merchandising.Api entry point.
'
' MODULE PROGRAM / SUB MAIN, NOT TOP LEVEL STATEMENTS. Top level statements
' are a C# language feature; they do not exist in Visual Basic. Emitting
' `Dim builder = WebApplication.CreateBuilder(args)` at file scope is the
' single most likely way to drift into C# shaped code in this project.
' See CLAUDE.md section 3.
'
' Public, not Friend, on purpose: P1-19 wires WebApplicationFactory, which
' needs this entry point reachable from the integration test assembly. The
' InternalsVisibleTo half of that seam belongs to P1-19, not here.

Imports Merchandising.Api.Middleware
Imports Merchandising.Api.Security
Imports Merchandising.Infrastructure.Data
Imports Microsoft.AspNetCore.Authentication
Imports Microsoft.AspNetCore.Builder
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting

''' <summary>
''' Entry point for the Merchandising API.
''' </summary>
''' <remarks>
''' Deliberately minimal at P1-02. This card exists to prove that a hand
''' authored Visual Basic project on the ASP.NET Core Web SDK builds, starts
''' Kestrel, and serves JSON (ADR-001, rung A). Authentication (P1-08), HTTPS
''' (P1-09), database access (P1-05) and Windows Service hosting (P1-16) each
''' add their own configuration here under their own acceptance checks.
''' </remarks>
Public Module Program

    ''' <summary>
    ''' Builds and runs the ASP.NET Core host.
    ''' </summary>
    ''' <param name="args">
    ''' Command line arguments, passed through to the host builder so the
    ''' standard ASP.NET Core configuration sources continue to work.
    ''' </param>
    Public Sub Main(args As String())

        Dim builder = WebApplication.CreateBuilder(args)

        ' Controller based endpoints, never minimal APIs. Minimal API lambda
        ' chains need multi line Function() ... End Function in Visual Basic
        ' and lean hardest on the Request Delegate Generator, which is a C#
        ' only source generator. Controllers are verbose and reliable, which
        ' is the right trade here. CLAUDE.md section 3.
        builder.Services.AddControllers()

        ' Database access, wired into DI for the first time at P1-08 - P1-05
        ' built ConnectionFactory/DatabaseOptions but nothing yet needed it
        ' through the API's own container. Loaded once at startup, not per
        ' request: the config file rarely changes and a missing/malformed
        ' file should fail the process at boot, not on the first request.
        Dim databaseOptions As DatabaseOptions = DatabaseOptionsLoader.Load()
        builder.Services.AddSingleton(databaseOptions)
        builder.Services.AddSingleton(Of ConnectionFactory)()
        builder.Services.AddScoped(Of AuthService)()

        ' P1-08 / ADR-005: opaque server-side session token, not JWT bearer -
        ' see Merchandising.Infrastructure.vbproj's comment for why. The
        ' scheme name is also what every [Authorize(AuthenticationSchemes:=...)]
        ' attribute in this project names explicitly.
        builder.Services.
            AddAuthentication(SessionAuthenticationHandler.SchemeName).
            AddScheme(Of AuthenticationSchemeOptions, SessionAuthenticationHandler)(
                SessionAuthenticationHandler.SchemeName, Nothing)

        builder.Services.AddAuthorization()

        Dim app = builder.Build()

        ' Correlation Id first, so every downstream handler - including the
        ' authentication handler's own 401/403 bodies - can read it.
        app.UseMiddleware(Of CorrelationIdMiddleware)()

        app.UseAuthentication()
        app.UseAuthorization()

        app.MapControllers()

        app.Run()

    End Sub

End Module
