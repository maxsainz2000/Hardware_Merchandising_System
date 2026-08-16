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

        Dim app = builder.Build()

        app.MapControllers()

        app.Run()

    End Sub

End Module
