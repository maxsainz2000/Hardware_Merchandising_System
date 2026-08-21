' Merchandising.Tests.Integration.TlsPolicyTests
'
' P1-21: Kestrel's HTTPS listener must pin its own protocol floor rather than
' inheriting the host operating system's Schannel configuration.
'
' WHAT THIS SUITE CAN AND CANNOT PROVE - read this before trusting a green run.
'
' It proves that TlsPolicy.Apply sets exactly TLS 1.2 and TLS 1.3 on an
' HttpsConnectionAdapterOptions. It does NOT prove that Program.vb calls it.
' It cannot: the whole ConfigureKestrel block is skipped under the "Testing"
' environment (the P1-19 guard that stops a test host loading a .pfx which
' exists on this machine and on no clean clone), so there is no configured
' listener in this process to inspect. The wiring is proven by the live probe
' in evidence/phase-1/p1-21-tls-pinning.txt, from a real .NET 10 client
' against the real Kestrel process, and by nothing in here.
'
' That distinction is the entire reason this card exists. The Tls12 reading in
' p1-09-cross-machine-trust.txt described Windows PowerShell 5.1's SslStream,
' not Kestrel, and chasing it is what exposed the unpinned listener behind it.
' A test that quietly implied more than it measured would be the same mistake
' one layer down.
'
' THE PAIR MATTERS. Asserting only that Apply produces Tls12|Tls13 would also
' pass for a version of this system where SslProtocols already defaulted to
' that value and Apply did nothing at all. The default assertion is what makes
' the second one mean something - same lesson as the X-Non-Production-Http
' pair at P1-19.

Imports System.Security.Authentication
Imports Merchandising.Api.Security
Imports Microsoft.AspNetCore.Server.Kestrel.Https
Imports Microsoft.VisualStudio.TestTools.UnitTesting

''' <summary>
''' P1-21: the TLS protocol floor is a property of the delivered software,
''' not of the host's registry.
''' </summary>
<TestClass>
Public Class TlsPolicyTests

    ''' <summary>
    ''' The half that gives the next test its meaning: unconfigured, Kestrel
    ''' defers to the operating system. <c>SslProtocols.None</c> is not "no
    ''' protocols" - it is "whatever Schannel permits", which under ADR-012 is
    ''' a registry on a machine nobody in this project inspects.
    ''' </summary>
    <TestMethod>
    Public Sub HttpsOptions_WithoutTheProject_DeferToTheOperatingSystem()

        Dim options As New HttpsConnectionAdapterOptions()

        Assert.AreEqual(
            SslProtocols.None, options.SslProtocols,
            "A fresh HttpsConnectionAdapterOptions is expected to leave the " &
            "protocol floor to the OS. If this ever fails, the pin below may " &
            "be asserting a value the framework already supplied, and would " &
            "keep passing if TlsPolicy.Apply were deleted.")

    End Sub

    ''' <summary>
    ''' The pin itself: exactly TLS 1.2 and TLS 1.3, set in code.
    ''' </summary>
    <TestMethod>
    Public Sub Apply_PinsTls12AndTls13()

        Dim options As New HttpsConnectionAdapterOptions()

        TlsPolicy.Apply(options)

        Assert.AreEqual(
            SslProtocols.Tls12 Or SslProtocols.Tls13, options.SslProtocols,
            "Kestrel's HTTPS listener must pin TLS 1.2 and 1.3 explicitly.")

    End Sub

    ''' <summary>
    ''' Stated as a mask rather than as four <c>HasFlag</c> calls: no bit
    ''' outside the allowed pair may be set. That is both stronger and
    ''' cheaper. Stronger, because it also refuses any protocol added to the
    ''' enum after this was written, which named checks never would. Cheaper,
    ''' because naming Ssl2, Ssl3, Tls and Tls11 directly costs four
    ''' obsolescence warnings (SYSLIB0039, BC40000) in a build that is
    ''' otherwise at zero, and a warning nobody can fix is how a warning list
    ''' stops being read.
    ''' </summary>
    <TestMethod>
    Public Sub Apply_LeavesNoObsoleteProtocolEnabled()

        Dim options As New HttpsConnectionAdapterOptions()

        TlsPolicy.Apply(options)

        Dim outsideThePin As SslProtocols =
            options.SslProtocols And Not (SslProtocols.Tls12 Or SslProtocols.Tls13)

        Assert.AreEqual(
            SslProtocols.None, outsideThePin,
            "No protocol outside TLS 1.2/1.3 may be reachable - SSL 2.0, " &
            "SSL 3.0, TLS 1.0 and TLS 1.1 included.")

    End Sub

    ''' <summary>
    ''' The pin is a constant of the package. Nothing reads configuration to
    ''' decide it, so there is no key a host can set to weaken it - the same
    ''' shape as ConnectionFactory's per-connection STRICT_TRANS_TABLES.
    ''' </summary>
    <TestMethod>
    Public Sub AllowedProtocols_IsTheValueApplied()

        Dim options As New HttpsConnectionAdapterOptions()

        TlsPolicy.Apply(options)

        Assert.AreEqual(TlsPolicy.AllowedProtocols, options.SslProtocols)

    End Sub

End Class
