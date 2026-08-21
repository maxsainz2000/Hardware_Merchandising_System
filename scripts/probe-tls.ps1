<#
.SYNOPSIS
    Reports the TLS protocol a live Merchandising API listener actually negotiates.

.DESCRIPTION
    P1-21 evidence tool. Opens a raw TCP connection, runs a TLS handshake through
    System.Net.Security.SslStream, and prints what was negotiated.

    WHY THIS SCRIPT EXISTS AND WHY IT MUST RUN UNDER PWSH 7.

    P1-09 measured the negotiated protocol with Windows PowerShell 5.1 and read
    back Tls12. That was a property of the HARNESS, not of the product: Windows
    PowerShell 5.1 runs on .NET Framework, whose SslStream does not offer TLS 1.3
    through AuthenticateAsClient. The figure was filed with a warning, and chasing
    the discrepancy is what exposed the unpinned Kestrel listener that P1-21 fixed.

    So this script prints its own runtime first, every time. A protocol reading
    whose client runtime is unknown is not a measurement, and the fastest way for
    that mistake to recur is for the next reader to have no way of telling which
    shell produced the number in front of them. Refuses to run on .NET Framework.

.PARAMETER ConnectHost
    The TCP target. Defaults to MERCH-HOST. Use 127.0.0.1 together with -TlsName
    MERCH-HOST to measure the listener from the host itself without depending on
    whatever address MERCH-HOST currently resolves to.

.PARAMETER TlsName
    The name presented in SNI and validated against the certificate. Must stay
    MERCH-HOST: ADR-011 fixed a name-only SAN, so any other value fails with
    RemoteCertificateNameMismatch by design.

.PARAMETER ClientProtocols
    What the CLIENT offers. Default None means "whatever this client's OS
    permits", which is the honest way to ask what the server prefers. Set it to
    Tls12 or Tls13 to prove a specific protocol is individually reachable.

.EXAMPLE
    pwsh ./scripts/probe-tls.ps1
    pwsh ./scripts/probe-tls.ps1 -ConnectHost 127.0.0.1 -ClientProtocols Tls12
#>

[CmdletBinding()]
param(
    [string] $ConnectHost = 'MERCH-HOST',
    [int]    $Port = 8443,
    [string] $TlsName = 'MERCH-HOST',
    [System.Security.Authentication.SslProtocols] $ClientProtocols = [System.Security.Authentication.SslProtocols]::None
)

$ErrorActionPreference = 'Stop'

$framework = [System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription

Write-Host '=== client runtime (read this before believing any figure below) ==='
Write-Host "  PowerShell      : $($PSVersionTable.PSVersion)"
Write-Host "  Edition         : $($PSVersionTable.PSEdition)"
Write-Host "  .NET runtime    : $framework"
Write-Host "  OS              : $([System.Environment]::OSVersion.VersionString)"

if ($framework -like '*.NET Framework*') {
    throw ".NET Framework detected. Its SslStream cannot negotiate TLS 1.3, so any reading from it understates the server. Run this under pwsh 7, not Windows PowerShell 5.1."
}

Write-Host ''
Write-Host '=== target ==='
Write-Host "  TCP endpoint    : ${ConnectHost}:${Port}"
Write-Host "  TLS server name : $TlsName"
Write-Host "  Client offers   : $ClientProtocols   (None = this client's OS defaults)"

$policyErrors = 'not reached'
$callback = {
    param($senderObject, $certificate, $chain, $sslPolicyErrors)
    $script:policyErrors = $sslPolicyErrors.ToString()
    # Deliberately permissive: this probe measures the negotiated PROTOCOL.
    # Whether the chain is trusted is P1-09's question and is already answered in
    # evidence/phase-1/p1-09-invalid-cert-behaviour.txt. Returning $false here
    # would abort the handshake before SslProtocol is readable, which would make
    # the tool unable to do its one job on a machine that has not imported the
    # certificate yet.
    return $true
}

$tcp = $null
$ssl = $null
try {
    $tcp = [System.Net.Sockets.TcpClient]::new()
    $tcp.Connect($ConnectHost, $Port)

    $ssl = [System.Net.Security.SslStream]::new($tcp.GetStream(), $false, $callback)
    $ssl.AuthenticateAsClient($TlsName, $null, $ClientProtocols, $false)

    Write-Host ''
    Write-Host '=== negotiated ==='
    Write-Host "  TLS protocol    : $($ssl.SslProtocol)"
    Write-Host "  Cipher suite    : $($ssl.NegotiatedCipherSuite)"
    Write-Host "  Cipher algorithm: $($ssl.CipherAlgorithm) $($ssl.CipherStrength)-bit"
    Write-Host "  SslPolicyErrors : $policyErrors"
    Write-Host "  Cert subject    : $($ssl.RemoteCertificate.Subject)"
    Write-Host "  Cert thumbprint : $(([System.Security.Cryptography.X509Certificates.X509Certificate2]$ssl.RemoteCertificate).Thumbprint)"
    Write-Host ''
    Write-Host 'HANDSHAKE SUCCEEDED'
}
catch {
    Write-Host ''
    Write-Host '=== handshake failed ==='
    Write-Host "  Exception       : $($_.Exception.GetType().FullName)"
    Write-Host "  Message         : $($_.Exception.Message)"
    if ($_.Exception.InnerException) {
        Write-Host "  Inner           : $($_.Exception.InnerException.Message)"
    }
    Write-Host "  SslPolicyErrors : $policyErrors"
    Write-Host ''
    Write-Host 'HANDSHAKE FAILED'
    exit 1
}
finally {
    if ($ssl) { $ssl.Dispose() }
    if ($tcp) { $tcp.Dispose() }
}
