<#
.SYNOPSIS
    Generates the host's HTTPS certificate for the Merchandising API
    (task P1-09, ADR-011) and writes the ACL-protected configuration file
    Merchandising.Infrastructure.Security.CertificateOptionsLoader reads.

.DESCRIPTION
    Creates a self-signed certificate whose subject/SAN is the single DNS
    name MERCH-HOST - deliberately no IP address in the SAN. An IP-bound SAN
    would tie the certificate to one network; this host moves between at
    least three (home, school, office Wi-Fi, and eventually a self-provided
    demo LAN per ADR-012), so the certificate must be a constant of the
    package, not a property of whichever network it happens to be on today.
    The address-to-name mapping that changes per network is absorbed by the
    hosts file instead (docs/installation-guide.md section 1).

    Two artifacts come out of this:
      - merch-host.pfx   Certificate + PRIVATE KEY. Stays on the host only,
                          inside the ACL-protected config directory. Never
                          copied to a client, never committed.
      - merch-host.cer    Public certificate only, no private key. This is
                          the file every client imports into its Trusted
                          Root store as part of the trust procedure
                          (docs/installation-guide.md section 4).

    Does not require an elevated session: CertificateOptionsLoader's config
    directory (%ProgramData%\MerchandisingSystem\config) was already ACL'd
    to this account by the P1-05 setup, and New-SelfSignedCertificate does
    not need admin rights when targeting Cert:\CurrentUser\My.

.PARAMETER PfxPassword
    Password protecting the exported .pfx. Generated randomly if omitted -
    per ADR-012 requirement 6, no password known to the author may be the
    password protecting a classmate's demo, so do not hard-code one here.

.EXAMPLE
    pwsh ./scripts/create-dev-certificate.ps1
#>

[CmdletBinding()]
param(
    [string] $PfxPassword
)

$ErrorActionPreference = 'Stop'

$dnsName = 'MERCH-HOST'
$configDir = Join-Path $env:ProgramData 'MerchandisingSystem\config'
$certDir = Join-Path $env:ProgramData 'MerchandisingSystem\certs'
$pfxPath = Join-Path $certDir 'merch-host.pfx'
$cerPath = Join-Path $certDir 'merch-host.cer'
$configPath = Join-Path $configDir 'certificate.json'

if (-not $PfxPassword) {
    $bytes = New-Object byte[] 24
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $PfxPassword = [Convert]::ToBase64String($bytes)
}
$securePassword = ConvertTo-SecureString -String $PfxPassword -Force -AsPlainText

New-Item -ItemType Directory -Force -Path $configDir | Out-Null
New-Item -ItemType Directory -Force -Path $certDir | Out-Null

Write-Host "Generating self-signed certificate for '$dnsName' (name-only SAN, no IP - ADR-011)..."

# Name-only: only -DnsName is supplied, so New-SelfSignedCertificate emits a
# single dNSName SAN entry and nothing else. Do not add -IPAddress here.
$cert = New-SelfSignedCertificate `
    -DnsName $dnsName `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -NotAfter (Get-Date).AddYears(2) `
    -KeyExportPolicy Exportable `
    -KeyUsage DigitalSignature, KeyEncipherment `
    -Type SSLServerAuthentication `
    -FriendlyName 'Merchandising System - MERCH-HOST'

Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null
Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT | Out-Null

# The CurrentUser\My copy served its purpose (letting New-SelfSignedCertificate
# run unelevated); the .pfx is now the single source of truth Kestrel loads,
# so remove the store copy rather than leaving a second, easily-forgotten
# private key lying around.
Remove-Item -Path "Cert:\CurrentUser\My\$($cert.Thumbprint)" -Force

$configJson = [ordered]@{
    pfxPath  = $pfxPath
    password = $PfxPassword
} | ConvertTo-Json
Set-Content -Path $configPath -Value $configJson -NoNewline

Write-Host ""
Write-Host "Done."
Write-Host "  Subject          : $($cert.Subject)"
Write-Host "  Thumbprint       : $($cert.Thumbprint)"
Write-Host "  Not after (UTC)  : $($cert.NotAfter.ToUniversalTime())"
Write-Host "  Private key (.pfx, host only, never distribute): $pfxPath"
Write-Host "  Public cert (.cer, distribute to every client)  : $cerPath"
Write-Host "  API configuration written to                    : $configPath"
Write-Host ""
Write-Host "Next: follow docs/installation-guide.md section 4 to import $cerPath into each client's Trusted Root store."
