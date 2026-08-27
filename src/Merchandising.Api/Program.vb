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
Imports Merchandising.Api.Catalog
Imports Merchandising.Api.Hosting
Imports Merchandising.Api.Inventory
Imports Merchandising.Api.Middleware
Imports Merchandising.Api.Procurement
Imports Merchandising.Api.Security
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Infrastructure.Security
Imports Microsoft.AspNetCore.Authentication
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Builder
Imports Microsoft.AspNetCore.Hosting
Imports Microsoft.AspNetCore.Server.Kestrel.Core
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Hosting.WindowsServices
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Logging.EventLog
Imports System.Runtime.Versioning

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
'''
''' P1-21 / ADR-011.1: that HTTPS listener pins its own protocol floor to
''' TLS 1.2 and 1.3 through TlsPolicy, rather than inheriting whatever the
''' host's Schannel configuration permits. Under ADR-012 the host is a
''' machine nobody in this project inspects, so the floor has to travel with
''' the binary.
''' </remarks>
Public Module Program

    ''' <summary>
    ''' The environment name a test host runs under. Named here, in the
    ''' production entry point, so the one place that behaves differently
    ''' under test is impossible to miss when reading Main.
    ''' </summary>
    Public Const TestingEnvironmentName As String = "Testing"

    ''' <summary>
    ''' Names the Event Log source the service writes under.
    ''' </summary>
    ''' <remarks>
    ''' A named method rather than an inline lambda purely so the platform
    ''' annotation has somewhere to live. CA1416's flow analysis does not
    ''' follow an OperatingSystem.IsWindows() check at the call site into a
    ''' lambda body, so the inline version warned even though it was already
    ''' guarded. Annotating this method states the same fact where the
    ''' analyser can see it, and the guarded call site above satisfies it.
    ''' </remarks>
    <SupportedOSPlatform("windows")>
    Private Sub ConfigureEventLogSource(settings As EventLogSettings)

        settings.SourceName = WindowsServiceInfo.EventLogSourceName

    End Sub

    ''' <summary>
    ''' Builds and runs the ASP.NET Core host.
    ''' </summary>
    ''' <param name="args">
    ''' Command line arguments, passed through to the host builder so the
    ''' standard ASP.NET Core configuration sources continue to work.
    ''' </param>
    Public Sub Main(args As String())

        Dim builder = WebApplication.CreateBuilder(args)

        ' P1-16 / spec section 6.4: run as a Windows Service when Windows
        ' started us as one, and as an ordinary console process otherwise.
        '
        ' SAFE TO CALL UNCONDITIONALLY, and that is worth knowing rather than
        ' guessing. AddWindowsService consults WindowsServiceHelpers.
        ' IsWindowsService() internally and does nothing at all when the
        ' process was not launched by the service control manager - so a
        ' console run, a `dotnet run`, and P1-19's WebApplicationFactory host
        ' are all unaffected. Guarding it with an environment check would add
        ' a second thing that can be configured wrongly for no benefit.
        '
        ' WHAT IT FIXES BEYOND THE LIFETIME. A service is started with
        ' C:\Windows\System32 as its working directory, not the directory the
        ' binary lives in. Without this call the content root would point
        ' there, and anything resolved relative to it would silently read from
        ' the wrong place.
        '
        ' It does NOT, however, turn on Event Log logging - an earlier draft of
        ' this comment claimed it did, and that was wrong. The framework
        ' registers that provider on Windows regardless of whether the process
        ' is a service; see the block below for what actually had to be done
        ' about it.
        builder.Services.AddWindowsService(
            Sub(serviceOptions)
                serviceOptions.ServiceName = WindowsServiceInfo.ServiceName
            End Sub)

        ' The Event Log source. Named explicitly, because the default is the
        ' application name and this project's operator-facing name for the
        ' service is WindowsServiceInfo.ServiceName - the same string used by
        ' sc.exe, by the ACL grant, and by the post-reboot health check. One
        ' name for one thing.
        '
        ' The source must already be registered when the service first writes,
        ' and creating it needs administrator rights, so it is created by
        ' scripts/install-service.ps1 at install time rather than lazily here:
        ' a restricted service account cannot create it, and the failure would
        ' arrive as a missing log rather than as an error.
        '
        ' CONFIGURE, NOT AddEventLog. There is nothing to add: on Windows,
        ' WebApplication.CreateBuilder's default host configuration ALREADY
        ' registers the Event Log logger provider, at Warning level, writing
        ' under the stock ".NET Runtime" source. All this needs to do is give
        ' it the right name. Calling AddEventLog as well would register a
        ' second provider and log everything twice.
        '
        ' THE IsWindowsService() GUARD IS THE LOAD-BEARING PART, and it is here
        ' because P1-16's own post-reboot evidence caught its absence. Without
        ' it this Configure applies to EVERY process built from this entry
        ' point - which includes P1-19's WebApplicationFactory test host. The
        ' integration suite duly wrote seven NonProductionWarningMiddleware
        ' warnings into the host's Application log under the source name
        ' "MerchandisingApi", where they are indistinguishable from entries
        ' written by the actual service. Measured, not theorised: the log went
        ' from 12 entries to 19 across one `run-tests.ps1`.
        '
        ' That is worse than untidy. Box 4 of this card asserts that the
        ' service's events appear in the Event Log, and the diagnostic value of
        ' that channel depends entirely on entries in it having come from the
        ' service. A test run masquerading as the service would send whoever
        ' reads that log after a failed demo looking in the wrong place.
        '
        ' Non-service runs keep the framework's default ".NET Runtime" source,
        ' which always exists on Windows - so nothing has to be created, and a
        ' clean clone with no service ever installed still runs its tests
        ' without touching a source that is not there.
        '
        ' THE OperatingSystem.IsWindows() GUARD IS NOT DEFENSIVE PADDING. This
        ' project targets net10.0, not net10.0-windows, and EventLogSettings is
        ' annotated Windows-only - so without the guard the compiler emits
        ' CA1416 and this build stops being a zero-warning build. Suppressing
        ' the warning, or moving the whole API to net10.0-windows to make it go
        ' away, would both be larger changes that say less. On this system the
        ' condition is always true (spec section 18 fixes win-x64), which is
        ' the point: the guard costs one branch and states a fact the type
        ' system otherwise could not see.
        If OperatingSystem.IsWindows() AndAlso WindowsServiceHelpers.IsWindowsService() Then
            builder.Services.Configure(Of EventLogSettings)(AddressOf ConfigureEventLogSource)
        End If

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

                    ' P1-21 / ADR-011.1: the third argument is the whole point.
                    ' Without AddressOf TlsPolicy.Apply, Kestrel defers its
                    ' protocol floor to the host's Schannel configuration - a
                    ' registry on a machine ADR-012 says nobody here inspects.
                    ' Never call UseHttps in this project without it.
                    kestrelOptions.Listen(
                        IPAddress.Any, 8443,
                        Sub(listenOptions) listenOptions.UseHttps(
                            certificateOptions.PfxPath,
                            certificateOptions.Password,
                            AddressOf TlsPolicy.Apply))

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
        ' P2-04: the audit pipeline's enforcement half. A global action
        ' filter so every [AuditRequired] action is wrapped without any
        ' per-controller wiring - see AuditPipelineFilter's own header for
        ' why this is an MVC filter and not raw middleware.
        builder.Services.AddControllers(
            Sub(mvcOptions) mvcOptions.Filters.Add(Of AuditPipelineFilter)())

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

        ' P2-08 / ADR-006: the atomic price/cost change (Products + PriceHistory + audit).
        builder.Services.AddScoped(Of PriceChangeService)()

        ' P2-09: deactivate/reactivate (Products + audit, atomically).
        builder.Services.AddScoped(Of ProductLifecycleService)()

        ' P2-10: deactivate/reactivate (Suppliers + audit, atomically) - same shape as ProductLifecycleService above.
        builder.Services.AddScoped(Of SupplierLifecycleService)()

        ' P3-03 / ADR-006 + ADR-007: purchase-order creation - the claim-first
        ' transaction that writes the order, its lines and the audit row, or
        ' none of them.
        builder.Services.AddScoped(Of PurchaseOrderService)()

        ' P1-18. Scoped rather than singleton: it takes a connection per call
        ' and holds no state between them.
        builder.Services.AddScoped(Of MaintenanceLockRepository)()

        ' P1-08 / ADR-005: opaque server-side session token, not JWT bearer -
        ' see Merchandising.Infrastructure.vbproj's comment for why. The
        ' scheme name is also what every [Authorize(AuthenticationSchemes:=...)]
        ' attribute in this project names explicitly.
        builder.Services.
            AddAuthentication(SessionAuthenticationHandler.SchemeName).
            AddScheme(Of AuthenticationSchemeOptions, SessionAuthenticationHandler)(
                SessionAuthenticationHandler.SchemeName, Nothing)

        ' P2-02 / ADR-017: every named policy comes from PolicyRegistry.Definitions,
        ' the same list docs/role-permission-matrix.md is rendered from - see
        ' AuthorizationPolicyRegistration's own header.
        builder.Services.AddAuthorization(AddressOf AuthorizationPolicyRegistration.Configure)

        ' PurchaseOrders.Approve and Adjustments.Approve carry a
        ' SelfApprovalRequirement (spec section 9's self-approval
        ' prohibitions); this is what the framework calls to evaluate it.
        builder.Services.AddSingleton(Of IAuthorizationHandler, SelfApprovalHandler)()

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

        ' P1-18: maintenance mode. Placement is deliberate and was arrived at
        ' by a failing test rather than by preference.
        '
        ' BEFORE UseAuthentication, not after. The natural instinct is to
        ' authenticate first so only known callers learn the system is down,
        ' but that produces exactly the wrong answer at the worst time: a
        ' client whose session expired during the maintenance window would be
        ' told "unauthorized" when the truth is "we are mid-restore", sending
        ' whoever is holding the pager to debug the wrong thing. Spec section
        ' 15 step 2 requires the maintenance state be DISPLAYED to connected
        ' clients, so it is not a secret being protected. It is also cheaper:
        ' a request that will be refused should not first cost a session
        ' lookup against the database that is about to be replaced.
        '
        ' AFTER CorrelationIdMiddleware, so the 503 envelope carries a
        ' correlation Id like every other error response (CLAUDE.md section 5).
        app.UseMiddleware(Of MaintenanceModeMiddleware)()

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
