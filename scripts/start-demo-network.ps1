#Requires -Version 5.1
<#
.SYNOPSIS
    Brings up the demo network on the API host and verifies every condition a
    client needs before the clients are in the room (ADR-015).

.DESCRIPTION
    Run this ON THE API HOST, elevated, before the presentation.

    Under ADR-015 the host laptop IS the access point: it runs Windows Mobile
    Hotspot, the three client laptops join it, and Internet Connection Sharing
    pins the host at 192.168.137.1. That address is a constant of the design,
    not a property of the venue, which is what lets every client carry a fixed
    hosts-file entry written once and never revisited.

    Six checks, in the order a failure would actually bite:

      1. HOTSPOT    Mobile Hotspot is on. Started here if it can be; if
                    Windows refuses, you get the specific reason and the
                    workaround rather than a generic failure.
      2. ADDRESS    The shared interface really is 192.168.137.1.
      3. PROFILE    That network is classified Private, not Public.
      4. FIREWALL   The P1-09 rule exists, is enabled, and would match.
      5. API        Something is listening on 8443.
      6. HANDOUT    Prints the SSID, the passphrase, and the exact hosts line.

    WHY CHECK 3 EXISTS, AND WHY IT IS THE ONE THAT WILL BITE YOU. Windows
    classifies a newly created network as PUBLIC by default. The firewall rule
    from P1-09 is scoped -Profile Private, deliberately, so that the API is
    closed on school and cafe Wi-Fi without anyone having to remember to close
    it. On a freshly created hotspot those two facts combine badly: the rule
    silently does not apply, port 8443 stays shut, and every client reports a
    connection timeout - which reads exactly like a bug in the API and is not
    one. Reclassifying the hotspot to Private is the correct fix. Widening the
    rule to the Public profile would also "work" and would quietly open the
    API on every untrusted network the laptop ever joins again.

.PARAMETER Port
    API HTTPS port to check. Defaults to 8443.

.PARAMETER SkipStart
    Do not try to start the hotspot; only verify what is already running.
    Use this when you have toggled Mobile Hotspot by hand in Settings.

.EXAMPLE
    pwsh ./scripts/start-demo-network.ps1

.EXAMPLE
    # Hotspot already on, just re-run the checks
    pwsh ./scripts/start-demo-network.ps1 -SkipStart
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 65535)]
    [int] $Port = 8443,

    [switch] $SkipStart
)

$ErrorActionPreference = 'Stop'

$IcsHostAddress = '192.168.137.1'
$FirewallRuleName = 'Merchandising API HTTPS (dev, private networks only)'
$script:Problems = 0

function Write-Check {
    param(
        [string] $Name,
        [string] $Value,
        [ValidateSet('PASS', 'WARN', 'FAIL', 'INFO')]
        [string] $Status = 'INFO',
        [string] $Advice = ''
    )
    if ($Status -eq 'FAIL') { $script:Problems++ }
    $colour = 'Gray'
    if ($Status -eq 'PASS') { $colour = 'Green' }
    if ($Status -eq 'WARN') { $colour = 'Yellow' }
    if ($Status -eq 'FAIL') { $colour = 'Red' }
    Write-Host ("  [{0}] {1,-26} {2}" -f $Status, $Name, $Value) -ForegroundColor $colour
    if ($Advice) { Write-Host ("         -> {0}" -f $Advice) -ForegroundColor $colour }
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Warning "This session is not elevated. Starting a hotspot and reclassifying a network both require it. No changes made."
    exit 1
}

# WinRT async in Windows PowerShell 5.1 needs this shim; there is no await.
function Invoke-WinRtAwait {
    param($WinRtTask, $ResultType)
    $asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() |
        Where-Object {
            $_.Name -eq 'AsTask' -and
            $_.GetParameters().Count -eq 1 -and
            $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
        })[0]
    $generic = $asTask.MakeGenericMethod($ResultType)
    $task = $generic.Invoke($null, @($WinRtTask))
    $null = $task.Wait(-1)
    return $task.Result
}

# ------------------------------------------------------------------ 1 -------
Write-Host ""
Write-Host "1  HOTSPOT" -ForegroundColor Cyan

$tethering = $null
try {
    [void][Windows.Networking.Connectivity.NetworkInformation, Windows.Networking.Connectivity, ContentType = WindowsRuntime]
    [void][Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager, Windows.Networking.NetworkOperators, ContentType = WindowsRuntime]

    $profileForSharing = [Windows.Networking.Connectivity.NetworkInformation]::GetInternetConnectionProfile()

    if (-not $profileForSharing) {
        # This is the known risk from ADR-015, and it is worth naming precisely
        # rather than reporting as a generic failure: Mobile Hotspot shares a
        # connection, so with no connection to share Windows has nothing to
        # attach the hotspot to.
        Write-Check -Name 'Connection to share' -Value 'none' -Status 'FAIL' -Advice `
            'Windows Mobile Hotspot shares an existing connection and there is none. Fix: plug a phone in by USB and turn on USB tethering, then re-run - the tether becomes the shared connection while the Wi-Fi radio serves the hotspot. Joining any Wi-Fi, even a firewalled one, also works.'
    } else {
        $tethering = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager]::CreateFromConnectionProfile($profileForSharing)
        $state = $tethering.TetheringOperationalState.ToString()
        Write-Check -Name 'Sharing' -Value $profileForSharing.ProfileName
        Write-Check -Name 'Current state' -Value $state

        if ($state -ne 'On' -and -not $SkipStart) {
            Write-Host "     starting Mobile Hotspot..." -ForegroundColor Gray
            $resultType = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringOperationResult]
            $result = Invoke-WinRtAwait $tethering.StartTetheringAsync() $resultType
            $status = $result.Status.ToString()
            Write-Check -Name 'Start result' -Value $status -Status $(
                if ($status -eq 'Success') { 'PASS' } else { 'FAIL' }
            ) -Advice $(
                if ($status -eq 'Success') { '' }
                else { "Windows refused. Open Settings > Network & Internet > Mobile hotspot and turn it on by hand, then re-run with -SkipStart. Reported: $status" }
            )
        } elseif ($state -eq 'On') {
            Write-Check -Name 'Mobile Hotspot' -Value 'already on' -Status 'PASS'
        }
    }
} catch {
    Write-Check -Name 'Mobile Hotspot' -Value 'could not be controlled from script' -Status 'WARN' -Advice `
        ("Turn it on by hand: Settings > Network & Internet > Mobile hotspot, then re-run with -SkipStart. Detail: " + $_.Exception.Message)
}

# ------------------------------------------------------------------ 2 -------
Write-Host ""
Write-Host "2  ADDRESS" -ForegroundColor Cyan

$icsInterface = $null
try {
    $icsIp = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
             Where-Object { $_.IPAddress -eq $IcsHostAddress } |
             Select-Object -First 1
    if ($icsIp) {
        $icsInterface = $icsIp.InterfaceAlias
        Write-Check -Name 'Host address' -Value ("{0} on '{1}'" -f $IcsHostAddress, $icsInterface) -Status 'PASS'
    } else {
        $present = (Get-NetIPAddress -AddressFamily IPv4 |
                    Where-Object { $_.IPAddress -notlike '127.*' } |
                    ForEach-Object { "$($_.IPAddress) [$($_.InterfaceAlias)]" }) -join ', '
        Write-Check -Name 'Host address' -Value "$IcsHostAddress NOT present" -Status 'FAIL' -Advice `
            "The hotspot interface is not up, so nothing holds the address every client's hosts file points at. Present instead: $present"
    }
} catch {
    Write-Check -Name 'Host address' -Value 'could not be read' -Status 'FAIL' -Advice $_.Exception.Message
}

# ------------------------------------------------------------------ 3 -------
Write-Host ""
Write-Host "3  PROFILE  (the one that will bite you)" -ForegroundColor Cyan

if ($icsInterface) {
    try {
        $netProfile = Get-NetConnectionProfile -InterfaceAlias $icsInterface -ErrorAction Stop
        if ($netProfile.NetworkCategory -eq 'Private') {
            Write-Check -Name 'Network category' -Value 'Private' -Status 'PASS'
        } else {
            Write-Host "     reclassifying '$($netProfile.Name)' from $($netProfile.NetworkCategory) to Private..." -ForegroundColor Yellow
            Set-NetConnectionProfile -InterfaceAlias $icsInterface -NetworkCategory Private
            $after = (Get-NetConnectionProfile -InterfaceAlias $icsInterface).NetworkCategory
            Write-Check -Name 'Network category' -Value $after -Status $(
                if ($after -eq 'Private') { 'PASS' } else { 'FAIL' }
            ) -Advice $(
                if ($after -eq 'Private') { 'Was Public - the firewall rule would not have matched and every client would have timed out.' }
                else { 'Reclassification did not take. The firewall rule will not match and clients cannot reach the API.' }
            )
        }
    } catch {
        Write-Check -Name 'Network category' -Value 'could not be set' -Status 'FAIL' -Advice $_.Exception.Message
    }
} else {
    Write-Check -Name 'Network category' -Value 'skipped - no hotspot interface' -Status 'WARN'
}

# ------------------------------------------------------------------ 4 -------
Write-Host ""
Write-Host "4  FIREWALL" -ForegroundColor Cyan

try {
    $rule = Get-NetFirewallRule -DisplayName $FirewallRuleName -ErrorAction SilentlyContinue
    if (-not $rule) {
        Write-Check -Name 'Rule' -Value 'not found' -Status 'FAIL' -Advice `
            'Run scripts/configure-firewall-dev.ps1 elevated to create it.'
    } else {
        $portFilter = $rule | Get-NetFirewallPortFilter
        $enabled = $rule.Enabled.ToString()
        Write-Check -Name 'Rule' -Value ("enabled={0}, profile={1}, port={2}" -f $enabled, $rule.Profile, $portFilter.LocalPort) -Status $(
            if ($enabled -eq 'True') { 'PASS' } else { 'FAIL' }
        ) -Advice $(
            if ($enabled -eq 'True') { '' } else { 'Rule exists but is disabled.' }
        )
        if ("$($rule.Profile)" -notmatch 'Private|Any') {
            Write-Check -Name 'Rule profile' -Value "$($rule.Profile)" -Status 'FAIL' -Advice `
                'Rule does not cover the Private profile, which is what check 3 just set the hotspot to.'
        }
    }
} catch {
    Write-Check -Name 'Rule' -Value 'could not be read' -Status 'FAIL' -Advice $_.Exception.Message
}

# ------------------------------------------------------------------ 5 -------
Write-Host ""
Write-Host "5  API" -ForegroundColor Cyan

try {
    $listening = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    if ($listening) {
        $addresses = ($listening | ForEach-Object { $_.LocalAddress } | Sort-Object -Unique) -join ', '
        $boundToAll = $addresses -match '0\.0\.0\.0|::'
        Write-Check -Name "Listening on $Port" -Value $addresses -Status $(
            if ($boundToAll) { 'PASS' } else { 'WARN' }
        ) -Advice $(
            if ($boundToAll) { '' }
            else { "Bound to a specific address rather than all interfaces. If that address is not $IcsHostAddress, clients on the hotspot cannot reach it." }
        )
    } else {
        Write-Check -Name "Listening on $Port" -Value 'nothing' -Status 'FAIL' -Advice `
            'The API is not running. Start the Windows Service (P1-16): Start-Service MerchandisingApi'
    }
} catch {
    Write-Check -Name "Listening on $Port" -Value 'could not be read' -Status 'FAIL' -Advice $_.Exception.Message
}

# ------------------------------------------------------------------ 6 -------
Write-Host ""
Write-Host "6  HANDOUT" -ForegroundColor Cyan

if ($tethering) {
    try {
        $ap = $tethering.GetCurrentAccessPointConfiguration()
        Write-Check -Name 'Wi-Fi name (SSID)' -Value $ap.Ssid -Status 'INFO'
        Write-Check -Name 'Wi-Fi password' -Value $ap.Passphrase -Status 'INFO'
        Write-Check -Name 'Clients connected' -Value ("{0} of {1} max" -f $tethering.ClientCount, $tethering.MaxClientCount) -Status 'INFO'
    } catch {
        Write-Check -Name 'Access point config' -Value 'unavailable' -Status 'WARN' -Advice $_.Exception.Message
    }
}

Write-Host ""
Write-Host "  On each client laptop, run (elevated):" -ForegroundColor Cyan
Write-Host ("    powershell -ExecutionPolicy Bypass -File .\setup-client.ps1 -HostIPv4 {0}" -f $IcsHostAddress) -ForegroundColor White
Write-Host ""
Write-Host "  That writes this hosts line for them, so nobody types it by hand:" -ForegroundColor Cyan
Write-Host ("    {0}`tMERCH-HOST" -f $IcsHostAddress) -ForegroundColor White

Write-Host ""
if ($script:Problems -eq 0) {
    Write-Host "RESULT: demo network is up and every client precondition is satisfied." -ForegroundColor Green
} else {
    Write-Host ("RESULT: {0} problem(s). Each [FAIL] above names its own fix." -f $script:Problems) -ForegroundColor Red
}
Write-Host ""

if ($script:Problems -gt 0) { exit 1 }
