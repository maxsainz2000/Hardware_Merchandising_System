<#
.SYNOPSIS
    Opens the Merchandising API's HTTPS port (8443) on this host's Windows
    Firewall, scoped so it can never be reached from a network Windows
    doesn't already trust (task P1-09, spec section 17 "Firewall").

.DESCRIPTION
    This host is a laptop that moves between at least three networks during
    development - home, school, and office Wi-Fi - and will later move to a
    fourth, self-provided demo LAN (ADR-012). A rule scoped to one hard-coded
    subnet would be wrong on every network except the one it was written for,
    and worse, would either fail closed everywhere else (breaking the lab
    test workstation at home) or need to be re-edited by hand every time the
    laptop changes networks.

    Instead this rule is scoped two ways at once, both dynamic:
      -Profile Private          Only active while Windows classifies the
                                 current network as Private. School and
                                 office Wi-Fi are typically Public, so on
                                 those networks this rule simply does not
                                 apply and Windows Firewall's own
                                 default-deny keeps 8443 closed - no manual
                                 toggling required.
      -RemoteAddress LocalSubnet Resolves at match-time to whatever subnet
                                 the active adapter is actually on, so it
                                 tracks the laptop across home/office/demo-LAN
                                 without ever naming an address.

    This is the DEVELOPMENT rule for this laptop. It is not the demo rig's
    firewall configuration - that is a separate, install-time step performed
    on the demo host once that hardware exists (ADR-012), and belongs in
    docs/installation-guide.md as part of that install, not hard-coded here.

    Requires an elevated (Run as Administrator) PowerShell session -
    New-NetFirewallRule fails otherwise. If this session is not elevated,
    it reports that and exits without changing anything.

.EXAMPLE
    pwsh ./scripts/configure-firewall-dev.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$ruleName = 'Merchandising API HTTPS (dev, private networks only)'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Warning "This session is not elevated. Re-run in PowerShell started as Administrator - New-NetFirewallRule requires it. No changes made."
    exit 1
}

$existing = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Rule '$ruleName' already exists. Removing before re-creating, so this script stays idempotent."
    $existing | Remove-NetFirewallRule
}

New-NetFirewallRule `
    -DisplayName $ruleName `
    -Direction Inbound `
    -Protocol TCP `
    -LocalPort 8443 `
    -RemoteAddress LocalSubnet `
    -Profile Private `
    -Action Allow | Out-Null

Write-Host "Rule '$ruleName' created: TCP 8443 inbound, LocalSubnet, Private profile only."
Write-Host "Verify with: Get-NetFirewallRule -DisplayName '$ruleName' | Get-NetFirewallPortFilter"
