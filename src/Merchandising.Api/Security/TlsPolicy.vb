' Merchandising.Api.Security.TlsPolicy
'
' P1-21 / ADR-011.1: the TLS protocol floor of this API is a property of the
' delivered software, not of the host it happens to be installed on.
'
' THE HOLE THIS CLOSES. Program.vb previously called
'
'     listenOptions.UseHttps(pfxPath, password)
'
' with no SslProtocols argument. Kestrel then defers to Schannel, so the
' effective floor is whatever the host operating system permits. On the
' development machine that is harmless - its SCHANNEL\Protocols key exists but
' is completely empty (0 subkeys, 0 values, measured at P1-21), so no protocol
' carries an override and every one sits at the Windows 11 default.
'
' Under ADR-012 the API does not stay on that machine. It is handed to
' classmates and run on a Windows 10 host that nobody in this project
' configures or inspects, where TLS 1.0 and 1.1 can still be enabled at the
' Schannel level. The claim "this API speaks modern TLS" would then be a
' statement about someone else's registry, and its failure would be entirely
' silent: a downgraded connection looks identical to a good one from the
' client side. Nobody would find out during a presentation, which is worse
' than finding out, not better.
'
' This is the same shape as the STRICT_TRANS_TABLES arrangement at P1-05,
' where the guarantee is asserted per connection in ConnectionFactory as well
' as in my.ini, precisely because a reinstalled or unfamiliar host can quietly
' revoke the my.ini half. A guarantee a host can revoke is not a guarantee.
' Set it in code, where it ships with the product.
'
' NOT CONFIGURABLE, DELIBERATELY. There is no appsettings key, no environment
' variable and no constructor parameter here. The value a host could override
' is exactly the value this file exists to take away from the host.

Imports System.Security.Authentication
Imports Microsoft.AspNetCore.Server.Kestrel.Https

Namespace Security

    ''' <summary>
    ''' The TLS protocols Kestrel's HTTPS listener is permitted to negotiate.
    ''' </summary>
    ''' <remarks>
    ''' Applied from <c>Program.vb</c> through the three argument
    ''' <c>UseHttps(path, password, configureOptions)</c> overload. Nothing in
    ''' this project may call <c>UseHttps</c> without it.
    ''' </remarks>
    Public NotInheritable Class TlsPolicy

        ''' <summary>
        ''' TLS 1.2 and TLS 1.3, and nothing else.
        ''' </summary>
        ''' <remarks>
        ''' TLS 1.3 alone was considered and rejected. Every client in this
        ''' system is .NET 10 and would negotiate 1.3 happily, but TLS 1.3
        ''' requires Windows 11 or Server 2022 on the Schannel side, and
        ''' ADR-012 puts the host on a machine whose Windows version is a
        ''' fact about a classmate's laptop rather than a decision this
        ''' project gets to make. A 1.3-only listener would refuse every
        ''' connection on a Windows 10 host - failing loudly, but failing.
        ''' TLS 1.2 is the floor that is still sound and still universally
        ''' available.
        ''' </remarks>
        Public Shared ReadOnly Property AllowedProtocols As SslProtocols
            Get
                Return SslProtocols.Tls12 Or SslProtocols.Tls13
            End Get
        End Property

        ''' <summary>
        ''' Pins <see cref="AllowedProtocols"/> onto a Kestrel HTTPS listener.
        ''' </summary>
        ''' <param name="httpsOptions">
        ''' The listener options supplied by Kestrel. Never <c>Nothing</c> in
        ''' practice; checked anyway, because a silent no-op here would leave
        ''' the listener on OS defaults and look exactly like success.
        ''' </param>
        Public Shared Sub Apply(httpsOptions As HttpsConnectionAdapterOptions)

            If httpsOptions Is Nothing Then
                Throw New ArgumentNullException(NameOf(httpsOptions))
            End If

            httpsOptions.SslProtocols = AllowedProtocols

        End Sub

        ''' <summary>
        ''' Not constructible - this type is policy, not state.
        ''' </summary>
        Private Sub New()
        End Sub

    End Class

End Namespace
