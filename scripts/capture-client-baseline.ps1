#Requires -Version 5.1
<#
.SYNOPSIS
    Captures a client machine's baseline and runs the cross-machine negative
    tests that P0-02, P0-03, P0-05 and P1-04 have been blocked on.

.DESCRIPTION
    Run this ON THE CLIENT MACHINE, not on the host. It gathers, in one pass:

      P0-02  Windows edition / build / architecture / resolution / DPI scaling
      P0-05  Reachability of the host, and whether MERCH-HOST resolves yet
      P0-03  phpMyAdmin and the XAMPP dashboard must be UNREACHABLE from here
      P1-04  MariaDB's port 3306 must be UNREACHABLE from here

    It is deliberately read-only. It changes nothing, needs no Administrator
    rights, and installs nothing. The hosts-file edit that P0-05 also requires
    is a separate, elevated step -- this script only reports whether it is done.

    Written for Windows PowerShell 5.1 (the Windows 10 default). Do not add
    PowerShell 7 syntax (?:, ??, -Parallel) -- it will not run on the target.

.PARAMETER HostIPv4
    The API host's current LAN address. Defaults to the address the host held
    when this script was written. VERIFY IT FIRST -- the host is on a DHCP
    lease, not a reservation, so this value is not yet durable (P0-05).

.PARAMETER HostTailscaleIPv4
    The host's Tailscale address. The host has a second network path that is
    not the store LAN, so "unreachable from the client" has to hold on both
    or the P0-03 / P1-10 tests prove less than they appear to.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\capture-client-baseline.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\capture-client-baseline.ps1 -HostIPv4 192.168.100.42
#>
[CmdletBinding()]
param(
    [string] $HostIPv4          = '192.168.100.165',
    [string] $HostTailscaleIPv4 = '100.76.155.51',
    [string] $HostName          = 'MERCH-HOST',
    [string] $OutFile           = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'client-baseline.txt')
)

$ErrorActionPreference = 'Continue'
$lines = New-Object System.Collections.Generic.List[string]

function Add-Line { param([string] $Text = '') $script:lines.Add($Text) }

function Add-Section {
    param([string] $Title)
    Add-Line ''
    Add-Line ('=' * 75)
    Add-Line $Title
    Add-Line ('=' * 75)
}

# A test whose EXPECTED result is failure. Reports PASS when the connection is
# refused, because that is the security property we are trying to demonstrate.
function Test-MustBeUnreachable {
    param([string] $Label, [string] $Target, [int] $Port)

    $ok = $false
    try {
        $client = New-Object System.Net.Sockets.TcpClient
        $async  = $client.BeginConnect($Target, $Port, $null, $null)
        $ok     = $async.AsyncWaitHandle.WaitOne(3000, $false) -and $client.Connected
        $client.Close()
    } catch {
        $ok = $false
    }

    if ($ok) {
        Add-Line ("  [FAIL] {0,-46} {1}:{2} ANSWERED -- it must not" -f $Label, $Target, $Port)
    } else {
        Add-Line ("  [PASS] {0,-46} {1}:{2} refused/timed out" -f $Label, $Target, $Port)
    }
}

Add-Line 'CLIENT BASELINE CAPTURE'
Add-Line ("Generated : {0}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'))
Add-Line ("Script    : capture-client-baseline.ps1")
Add-Line ("Host under test : {0} ({1}), Tailscale {2}" -f $HostName, $HostIPv4, $HostTailscaleIPv4)

# ---------------------------------------------------------------- P0-02 ------
Add-Section 'P0-02  Machine identity, Windows baseline, architecture'

Add-Line ("Machine name        : {0}" -f $env:COMPUTERNAME)
Add-Line ("User                : {0}" -f $env:USERNAME)
Add-Line ("PowerShell version  : {0}" -f $PSVersionTable.PSVersion)

try {
    $os = Get-CimInstance Win32_OperatingSystem
    $cs = Get-CimInstance Win32_ComputerSystem
    $ub = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction SilentlyContinue)

    Add-Line ("Windows edition     : {0}" -f $os.Caption)
    Add-Line ("Version / DisplayVer: {0} / {1}" -f $os.Version, $ub.DisplayVersion)
    Add-Line ("Build               : {0}.{1}" -f $ub.CurrentBuild, $ub.UBR)
    Add-Line ("OS architecture     : {0}" -f $os.OSArchitecture)
    Add-Line ("Manufacturer / model: {0} / {1}" -f $cs.Manufacturer, $cs.Model)
    Add-Line ("Logical processors  : {0}" -f $cs.NumberOfLogicalProcessors)
    Add-Line ("Installed RAM       : {0:N1} GB" -f ($cs.TotalPhysicalMemory / 1GB))
    Add-Line ("Install date        : {0}" -f $os.InstallDate)
} catch {
    Add-Line ("  !! Could not read OS info: {0}" -f $_.Exception.Message)
}

Add-Line ''
Add-Line 'Architecture gate -- the project targets win-x64 and .NET 10:'
if ([Environment]::Is64BitOperatingSystem) {
    Add-Line '  [PASS] 64-bit operating system'
} else {
    Add-Line '  [FAIL] 32-bit OS -- win-x64 publish output CANNOT run here. STOP AND REPORT.'
}

# ------------------------------------------------------- P0-02 display -------
Add-Section 'P0-02  Display -- UI baseline is 1366x768 at 125% scaling'

try {
    $mons = @(Get-CimInstance Win32_VideoController |
              Where-Object { $_.CurrentHorizontalResolution })
    if ($mons.Count -eq 0) {
        Add-Line '  (no active video controller reported)'
    }
    foreach ($m in $mons) {
        Add-Line ("Adapter             : {0}" -f $m.Name)
        Add-Line ("Native resolution   : {0} x {1} @ {2} Hz" -f `
                  $m.CurrentHorizontalResolution, $m.CurrentVerticalResolution, $m.CurrentRefreshRate)
    }
} catch {
    Add-Line ("  !! Could not read video controller: {0}" -f $_.Exception.Message)
}

try {
    $dpi = (Get-ItemProperty 'HKCU:\Control Panel\Desktop\WindowMetrics' -Name AppliedDPI -ErrorAction Stop).AppliedDPI
    $pct = [math]::Round(($dpi / 96) * 100)
    Add-Line ("Applied DPI         : {0} ({1}% scaling)" -f $dpi, $pct)
} catch {
    Add-Line '  (AppliedDPI not set in HKCU -- typically means 96 DPI / 100% scaling)'
}

Add-Line ''
Add-Line 'NOTE: a desktop monitor at 100% does NOT exercise the 1366x768 @125%'
Add-Line 'baseline. That test stays with the laptop. Record, do not substitute.'

# ------------------------------------------------------- .NET runtimes -------
Add-Section '.NET runtimes present (a client needs the DESKTOP runtime, not the SDK)'

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) {
    Add-Line ("dotnet found at     : {0}" -f $dotnet.Source)
    Add-Line ''
    Add-Line '--- dotnet --list-runtimes ---'
    (& dotnet --list-runtimes 2>&1) | ForEach-Object { Add-Line ("  {0}" -f $_) }
    Add-Line ''
    Add-Line '--- dotnet --list-sdks ---'
    (& dotnet --list-sdks 2>&1) | ForEach-Object { Add-Line ("  {0}" -f $_) }
    Add-Line ''
    Add-Line 'A client SHOULD have Microsoft.WindowsDesktop.App 10.0.9 and'
    Add-Line 'SHOULD NOT need an SDK. An SDK here is not an error, just unnecessary.'
} else {
    Add-Line 'dotnet is NOT installed on this machine.'
    Add-Line ''
    Add-Line 'Expected at this stage. Before P1-15 (WPF round trip) this client needs'
    Add-Line 'the .NET Desktop Runtime 10.0.9 (x64) -- the runtime, NOT the SDK, and'
    Add-Line 'NOT XAMPP. Keeping the SDK and the database off this machine is part of'
    Add-Line 'what the client/host split is meant to demonstrate.'
}

# ---------------------------------------------------------------- P0-05 ------
Add-Section 'P0-05  Network position and host reachability'

try {
    $adapters = @(Get-NetAdapter | Where-Object { $_.Status -eq 'Up' })
    foreach ($a in $adapters) {
        Add-Line ("Adapter UP          : {0}  [{1}]  MAC {2}  {3}" -f `
                  $a.Name, $a.InterfaceDescription, $a.MacAddress, $a.LinkSpeed)
    }
    Add-Line ''
    Add-Line 'Record the Ethernet MAC above in the manifest. A router DHCP'
    Add-Line 'reservation binds to a MAC, so this is what one would target -- but'
    Add-Line 'only the HOST actually needs a durable address, because only the'
    Add-Line 'host name goes in the certificate SAN. A client does not need one.'
} catch {
    Add-Line ("  !! Could not enumerate adapters: {0}" -f $_.Exception.Message)
}

Add-Line ''
try {
    $cfgs = @(Get-NetIPConfiguration | Where-Object { $_.IPv4Address })
    foreach ($c in $cfgs) {
        $ip = ($c.IPv4Address.IPAddress -join ',')
        $gw = ($c.IPv4DefaultGateway.NextHop -join ',')
        Add-Line ("IPv4 {0,-22} {1,-18} gw {2,-16} profile '{3}'" -f `
                  $c.InterfaceAlias, $ip, $gw, $c.NetProfile.Name)

        foreach ($addr in @(Get-NetIPAddress -InterfaceIndex $c.InterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue)) {
            Add-Line ("     origin {0}/{1}  state {2}  /{3}" -f `
                      $addr.PrefixOrigin, $addr.SuffixOrigin, $addr.AddressState, $addr.PrefixLength)
        }
    }
} catch {
    Add-Line ("  !! Could not read IP configuration: {0}" -f $_.Exception.Message)
}

Add-Line ''
Add-Line '--- Same-subnet gate: can this client reach the host at all? ---'
Add-Line 'If this fails, the router is isolating wired from wireless clients and'
Add-Line 'every downstream test is meaningless until that is fixed.'
Add-Line ''

$ping = Test-Connection -ComputerName $HostIPv4 -Count 4 -ErrorAction SilentlyContinue
if ($ping) {
    $avg = ($ping | Measure-Object -Property ResponseTime -Average).Average
    Add-Line ("  [PASS] ping {0} -- {1}/4 replies, avg {2} ms" -f $HostIPv4, $ping.Count, [math]::Round($avg))
} else {
    Add-Line ("  [FAIL] ping {0} -- no reply." -f $HostIPv4)
    Add-Line '         Could be: host firewall blocking ICMP, wired/wireless isolation,'
    Add-Line '         or the host no longer holds this address (it is on a DHCP LEASE,'
    Add-Line '         not a reservation). Check the host address before concluding.'
}

Add-Line ''
Add-Line ("--- Does '{0}' resolve yet? (hosts-file entry, P0-05) ---" -f $HostName)
try {
    $res = Resolve-DnsName -Name $HostName -ErrorAction Stop
    foreach ($r in $res) {
        Add-Line ("  resolved -> {0}" -f $r.IPAddress)
    }
    $pingName = Test-Connection -ComputerName $HostName -Count 2 -ErrorAction SilentlyContinue
    if ($pingName) {
        Add-Line ("  [PASS] ping {0} succeeded -- P0-05 acceptance met from this client" -f $HostName)
    } else {
        Add-Line ("  [WARN] {0} resolves but does not answer ping" -f $HostName)
    }
} catch {
    Add-Line ("  [PENDING] '{0}' does not resolve -- hosts entry not applied here yet." -f $HostName)
    Add-Line '  This is expected on a first run. Apply it in an ELEVATED PowerShell'
    Add-Line '  using the procedure in docs/installation-guide.md section 1.2, then'
    Add-Line '  re-run this script. Do not add the entry until the host address is'
    Add-Line '  durable (a router reservation), or you will hardcode a stale lease.'
}

# ---------------------------------------------------- P0-03 / P1-04 ----------
Add-Section 'P0-03 + P1-04  Negative tests -- ALL of these MUST fail to connect'

Add-Line 'The database and phpMyAdmin must be unreachable from a client machine.'
Add-Line 'A PASS here means the connection was refused. A FAIL means it answered.'
Add-Line ''
Add-Line ("--- Over the LAN ({0}) ---" -f $HostIPv4)
Test-MustBeUnreachable -Label 'P1-04  MariaDB 3306'              -Target $HostIPv4 -Port 3306
Test-MustBeUnreachable -Label 'P0-03  XAMPP dashboard / Apache 80' -Target $HostIPv4 -Port 80
Test-MustBeUnreachable -Label 'P0-03  Apache TLS 443'              -Target $HostIPv4 -Port 443
Test-MustBeUnreachable -Label 'P0-03  Apache alt 8080'             -Target $HostIPv4 -Port 8080

Add-Line ''
Add-Line ("--- Over Tailscale ({0}) -- the host's SECOND path ---" -f $HostTailscaleIPv4)
Add-Line 'If this client is not on the tailnet these will refuse for the wrong'
Add-Line 'reason (no route), which proves nothing. Note which case applies.'
Test-MustBeUnreachable -Label 'P1-04  MariaDB 3306 via Tailscale'  -Target $HostTailscaleIPv4 -Port 3306
Test-MustBeUnreachable -Label 'P0-03  Apache 80 via Tailscale'     -Target $HostTailscaleIPv4 -Port 80

Add-Line ''
Add-Line '--- phpMyAdmin over HTTP (belt and braces on top of the port check) ---'
foreach ($url in @("http://$HostIPv4/phpmyadmin/", "http://$HostIPv4/", "http://$HostIPv4/dashboard/")) {
    try {
        $resp = Invoke-WebRequest -Uri $url -TimeoutSec 5 -UseBasicParsing -ErrorAction Stop
        Add-Line ("  [FAIL] {0} answered HTTP {1} -- it must not" -f $url, $resp.StatusCode)
    } catch {
        Add-Line ("  [PASS] {0} unreachable" -f $url)
    }
}

Add-Line ''
Add-Line 'On P1-04 "root login from a client fails": with 3306 refused at the TCP'
Add-Line 'layer there is nothing to authenticate against, which is the stronger'
Add-Line 'result -- the credential never reaches a login prompt. No MySQL client'
Add-Line 'needs to be installed here, and none should be.'

# ------------------------------------------------------------- output --------
Add-Line ''
Add-Line ('=' * 75)
Add-Line 'END OF CAPTURE'
Add-Line ('=' * 75)

$text = $lines -join [Environment]::NewLine
Write-Output $text

try {
    Set-Content -Path $OutFile -Value $text -Encoding UTF8
    Write-Output ''
    Write-Output ("Saved to: {0}" -f $OutFile)
} catch {
    Write-Output ''
    Write-Output ("Could not write {0}: {1}" -f $OutFile, $_.Exception.Message)
    Write-Output 'Copy the console output above instead.'
}
