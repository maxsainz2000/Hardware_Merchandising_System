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

Imports System.Net
Imports Merchandising.Api.Inventory
Imports Merchandising.Api.Middleware
Imports Merchandising.Api.Security
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Infrastructure.Security
Imports Microsoft.AspNetCore.Authentication
Imports Microsoft.AspNetCore.Builder
Imports Microsoft.AspNetCore.Hosting
Imports Microsoft.AspNetCore.Server.Kestrel.Core
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging

''' <summary>
''' Entry point for the Merchandising API.
''' </summary>
''' <remarks>
''' Deliberately minimal at P1-02. This card exists to prove that a hand
''' authored Visual Basic project on the ASP.NET Core Web SDK builds, starts
''' Kestrel, and serves JSON (ADR-001, rung A). Authentication (P1-08), HTTPS
''' (P1-09), database access (P1-05) and Windows Service hosting (P1-16) each
''' add their own configuration here under their own acceptance checks.
'''
''' P1-09 / ADR-011: Kestrel binds HTTPS on 0.0.0.0:8443 using a certificate
''' loaded from the ACL-protected host configuration file
''' (CertificateOptionsLoader), never from appsettings.json - none exists in
''' this project, see Merchandising.Api.vbproj's comment. In the
''' Development environment only, a second listener binds HTTP on
''' 127.0.0.1 - loopback, never 0.0.0.0, so it can never be reached from
''' another machine regardless of which Wi-Fi this laptop is currently on.
''' NonProductionWarningMiddleware tags every plain-HTTP response so that
''' listener can never be mistaken for the demo one.
''' </remarks>
Public Module Program

    ''' <summary>
    ''' The environment name a test host runs under. Named here, in the
    ''' production entry point, so the one place that behaves differently
    ''' under test is impossible to miss when reading Main.
    ''' </summary>
    Public Const TestingEnvironmentName As String = "Testing"

    ''' <summary>
    ''' Builds and runs the ASP.NET Core host.
    ''' </summary>
    ''' <param name="args">
    ''' Command line arguments, passed through to the host builder so the
    ''' standard ASP.NET Core configuration sources continue to work.
    ''' </param>
    Public Sub Main(args As String())

        Dim builder = WebApplication.CreateBuilder(args)

        ' P1-09 / ADR-011: HTTPS on 8443 for every environment, using a
        ' name-only SAN certificate (MERCH-HOST, no IP) so the same .pfx
        ' stays valid on whichever network this host currently sits on -
        ' home, school, office, or the eventual self-provided demo LAN
        ' (ADR-012). The certificate itself never changes; only the hosts
        ' file entry pointing MERCH-HOST at an address does.
        '
        ' The Development-only HTTP listener is bound to IPAddress.Loopback,
        ' never IPAddress.Any - see spec section 8 "development profile may
        ' use HTTP only on an isolated developer machine". Binding it to
        ' Loopback rather than trusting IsDevelopment() alone means even a
        ' misconfigured firewall on a hostile network cannot expose it.
        '
        ' P1-19: skipped under the "Testing" environment, and only there.
        ' WebApplicationFactory replaces Kestrel with an in-memory TestServer,
        ' but it still EXECUTES this method to build the host - so without
        ' this guard the test host would try to load a .pfx from
        ' %ProgramData% that exists on the developer's machine and on no
        ' clean clone. The tests would pass here and fail everywhere else,
        ' which is worse than failing here.
        '
        ' The guard names one environment explicitly rather than inverting
        ' IsProduction(): a missing certificate in Development, Staging or
        ' Production still fails the process at boot, loudly, which is the
        ' behaviour P1-09 deliberately built. Only a test host that has no
        ' Kestrel to configure is exempt.
        If Not builder.Environment.IsEnvironment(TestingEnvironmentName) Then

            Dim certificateOptions As CertificateOptions = CertificateOptionsLoader.Load()

            builder.WebHost.ConfigureKestrel(
                Sub(kestrelOptions As KestrelServerOptions)

                    kestrelOptions.Listen(
                        IPAddress.Any, 8443,
                        Sub(listenOptions) listenOptions.UseHttps(certificateOptions.PfxPath, certificateOptions.Password))

                    If builder.Environment.IsDevelopment() Then
                        kestrelOptions.Listen(IPAddress.Loopback, 8080)
                    End If

                End Sub)

        End If

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

        ' P1-11 / ADR-006: the atomic stock decrement.
        builder.Services.AddScoped(Of StockService)()

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

        ' P1-10: the exception handler is OUTERMOST, ahead of even the
        ' correlation middleware, so nothing in the pipeline can throw its way
        ' past it - including CorrelationIdMiddleware itself. Before this card
        ' there was no handler at all, which meant a stack trace to the caller
        ' in Development and an empty-bodied 500 in Production; both fail spec
        ' section 13. See the middleware's own header for the full account.
        '
        ' The framework registers DeveloperExceptionPage ahead of any
        ' user middleware in Development and offers no way to remove it from a
        ' WebApplication. That is harmless: it only formats exceptions that
        ' reach it, and this handler - being inside it - has already converted
        ' every one into a controlled response, so it never sees one.
        '
        ' HOW FAR THAT IS PROVEN, precisely: ExceptionHandlingMiddlewareTests
        ' exercises the real middleware and shows it handles rather than
        ' propagates. The end-to-end claim - a live request to a running
        ' Kestrel in Development returning the envelope and not the developer
        ' page - is NOT captured, because inducing an unhandled exception
        ' through the real pipeline needs the test-only fault-injection seam
        ' that P1-12 owns and P1-19's WebApplicationFactory wiring to drive.
        ' See p1-10-denials.txt, "What this file does not prove".
        app.UseMiddleware(Of ExceptionHandlingMiddleware)()

        ' Correlation Id next, so every downstream handler - including the
        ' authentication handler's own 401/403 bodies - can read it.
        app.UseMiddleware(Of CorrelationIdMiddleware)()

        ' P1-09: tags plain-HTTP responses so the loopback-only dev listener
        ' is never mistaken for the HTTPS demo one. A no-op on every HTTPS
        ' request, so it is harmless to leave registered unconditionally.
        app.UseMiddleware(Of NonProductionWarningMiddleware)()

        If builder.Environment.IsDevelopment() Then
            app.Logger.LogWarning(
                "*** Development HTTP profile active on http://127.0.0.1:8080 - loopback only, non-production. " &
                "The production-like listener is https://MERCH-HOST:8443. ***")
        End If

        app.UseAuthentication()
        app.UseAuthorization()

        app.MapControllers()

        app.Run()

    End Sub

End Module
