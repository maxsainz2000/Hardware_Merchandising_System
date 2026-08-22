#Requires -Version 5.1
<#
.SYNOPSIS
    One-command client setup for a Merchandising System demo workstation:
    preflight, trust the host certificate, resolve MERCH-HOST, verify a real
    HTTPS round trip, and write a baseline report to hand back (ADR-015).

.DESCRIPTION
    Run this ON A CLIENT LAPTOP, not on the API host.

    It is written so that a machine can be set up WITHOUT ANYONE HAVING
    SURVEYED IT FIRST. P0-02 has been blocked on collecting Windows edition,
    build, architecture, resolution, scaling and admin rights from three
    classmates' laptops. This script inverts that dependency: it collects all
    of it itself, reports what is wrong in plain language, and leaves a file
    on the Desktop to send back. The baseline stops being something to gather
    in advance and becomes an output of the setup that had to happen anyway.

    Five stages, in order, each reported pass/fail:

      1. PREFLIGHT   Windows edition/build/architecture, admin rights, the
                     .NET Desktop Runtime, screen resolution and DPI scaling.
                     Read-only. Runs without Administrator.
      2. TRUST       Imports merch-host.cer into LocalMachine\Root so the
                     client can verify the host's certificate. Needs admin.
      3. RESOLVE     Writes the MERCH-HOST hosts-file entry. Needs admin.
      4. VERIFY      DNS resolution, TCP reach on the API port, and a real
                     HTTPS GET of /health WITH CERTIFICATE VALIDATION ON.
                     This last one is the only stage that proves anything:
                     stages 2 and 3 are means, and this is the end.
      5. REPORT      Writes client-baseline-<MACHINE>.txt to the Desktop.

    WHY VALIDATION IS NEVER BYPASSED. It would be trivial to add a callback
    that accepts any certificate and make stage 4 go green on a machine where
    stage 2 silently failed. That would convert the one check that proves the
    trust chain works into a check that proves nothing, and the failure would
    resurface during the presentation instead of here. If stage 4 fails on
    certificate grounds, that is the script working correctly.

    WINDOWS POWERSHELL 5.1 ONLY. This runs on whatever the classmate's laptop
    already has, which on Windows 10 is 5.1 and nothing else. Do not add
    PowerShell 7 syntax (?:, ??, -Parallel, ForEach-Object -Parallel) and do
    not assume pwsh exists. Tested constructs only.

.PARAMETER HostIPv4
    The API host's address on the demo network. Defaults to 192.168.137.1,
    which is the address Windows Internet Connection Sharing assigns to a
    Mobile Hotspot interface on every Windows 10 and 11 machine.

    THIS DEFAULT IS A DELIBERATE REVERSAL of the rule stated in
    capture-client-baseline.ps1, which refuses to default a host address
    because "a wrong-but-plausible address does not fail cleanly." That was
    correct while the address was a lab fact that differed per network. Under
    ADR-015 the host IS the access point, so 192.168.137.1 is a constant of
    the design rather than a property of today's network - and defaulting to
    it is what reduces client setup to one command with nothing to phone home
    about. Override it for the phone-hotspot fallback, where the host is an
    ordinary DHCP client and its address does vary.

.PARAMETER CertificatePath
    Path to merch-host.cer (public certificate, no private key). Defaults to
    the file sitting beside this script, which is where the handover package
    puts it. If the .pfx is passed here by mistake the script refuses: that
    file carries the PRIVATE KEY and must never reach a client.

.PARAMETER Port
    API HTTPS port. Defaults to 8443 to match the Kestrel listener.

.PARAMETER CaptureOnly
    Run stage 1 and stage 5 only. No changes, no Administrator required.
    Use this to collect a machine's baseline before the demo package exists,
    or to answer "will this laptop work?" without touching it.

.EXAMPLE
    # Normal case - host is the access point (ADR-015)
    powershell -ExecutionPolicy Bypass -File .\setup-client.ps1

.EXAMPLE
    # Phone-hotspot fallback - host is a DHCP client at a known address
    powershell -ExecutionPolicy Bypass -File .\setup-client.ps1 -HostIPv4 192.168.43.137

.EXAMPLE
    # "Will my laptop work?" - read-only, no admin needed
    powershell -ExecutionPolicy Bypass -File .\setup-client.ps1 -CaptureOnly
#>
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string] $HostIPv4 = '192.168.137.1',

    [string] $CertificatePath,

    [ValidateRange(1, 65535)]
    [int] $Port = 8443,

    [switch] $CaptureOnly
)

$ErrorActionPreference = 'Stop'

$HostName = 'MERCH-HOST'
$MinimumWindowsBuild = 17763   # Windows 10 1809. Below this, .NET 10 desktop is unsupported.
$script:Findings = New-Object System.Collections.ArrayList
$script:Blockers = 0

function Add-Finding {
    param(
        [string] $Stage,
        [string] $Name,
        [string] $Value,
        [ValidateSet('PASS', 'WARN', 'FAIL', 'INFO')]
        [string] $Status = 'INFO',
        [string] $Advice = ''
    )
    $null = $script:Findings.Add([PSCustomObject]@{
        Stage  = $Stage
        Name   = $Name
        Value  = $Value
        Status = $Status
        Advice = $Advice
    })
    if ($Status -eq 'FAIL') { $script:Blockers++ }

    $colour = 'Gray'
    if ($Status -eq 'PASS') { $colour = 'Green' }
    if ($Status -eq 'WARN') { $colour = 'Yellow' }
    if ($Status -eq 'FAIL') { $colour = 'Red' }

    Write-Host ("  [{0}] {1,-28} {2}" -f $Status, $Name, $Value) -ForegroundColor $colour
    if ($Advice) {
        Write-Host ("         -> {0}" -f $Advice) -ForegroundColor $colour
    }
}

function Test-IsElevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# ---------------------------------------------------------------- stage 1 ---
Write-Host ""
Write-Host "STAGE 1  PREFLIGHT - what this machine is" -ForegroundColor Cyan
Write-Host ""

$os = Get-CimInstance Win32_OperatingSystem
$build = [int] $os.BuildNumber
$isElevated = Test-IsElevated

Add-Finding -Stage 'Preflight' -Name 'Machine name' -Value $env:COMPUTERNAME
Add-Finding -Stage 'Preflight' -Name 'Windows edition' -Value $os.Caption
Add-Finding -Stage 'Preflight' -Name 'Architecture' -Value $os.OSArchitecture -Status $(
    if ($os.OSArchitecture -match '64') { 'PASS' } else { 'FAIL' }
) -Advice $(
    if ($os.OSArchitecture -match '64') { '' }
    else { 'The clients publish win-x64 only. A 32-bit machine cannot run them.' }
)

Add-Finding -Stage 'Preflight' -Name 'Build' -Value ("{0} ({1})" -f $build, $os.Version) -Status $(
    if ($build -ge $MinimumWindowsBuild) { 'PASS' } else { 'FAIL' }
) -Advice $(
    if ($build -ge $MinimumWindowsBuild) { '' }
    else { "Build $build is below $MinimumWindowsBuild (Windows 10 1809), the floor for the .NET 10 desktop runtime. Windows Update, then re-run." }
)

Add-Finding -Stage 'Preflight' -Name 'Administrator rights' -Value $(
    if ($isElevated) { 'yes, this session is elevated' }
    elseif ($CaptureOnly) { 'not elevated - not needed for -CaptureOnly' }
    else { 'NOT elevated' }
) -Status $(
    if ($isElevated -or $CaptureOnly) { 'PASS' } else { 'FAIL' }
) -Advice $(
    if ($isElevated -or $CaptureOnly) { '' }
    else { 'Stages 2 and 3 write to the certificate store and the hosts file. Close this window, right-click PowerShell, Run as Administrator, and re-run. Or use -CaptureOnly to just collect the baseline.' }
)

# Screen resolution and DPI scaling. The UI baseline is 1366x768 and must stay
# usable at 125% - P0-02 asks for both numbers per workstation, so collect them
# rather than asking a classmate to find them.
try {
    $dpi = (Get-ItemProperty 'HKCU:\Control Panel\Desktop' -Name 'LogPixels' -ErrorAction SilentlyContinue).LogPixels
    if (-not $dpi) { $dpi = 96 }
    $scalePercent = [int] [Math]::Round(($dpi / 96) * 100)

    # Two sources, because the obvious one is not reliable. Win32_VideoController
    # returns null resolutions on hybrid-graphics laptops (this was found by
    # running the script on the dev machine, which is one of them) and the row
    # would have gone silently missing - exactly the P0-02 field we are here to
    # collect. System.Windows.Forms reports DPI-virtualised pixels, so it needs
    # scaling back up by the DPI factor to give the physical panel size.
    $width = 0; $height = 0; $source = ''
    $video = Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue |
             Where-Object { $_.CurrentHorizontalResolution } |
             Select-Object -First 1
    if ($video) {
        $width = [int] $video.CurrentHorizontalResolution
        $height = [int] $video.CurrentVerticalResolution
        $source = 'video controller'
    } else {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue
        $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $width = [int] [Math]::Round($bounds.Width * ($dpi / 96))
        $height = [int] [Math]::Round($bounds.Height * ($dpi / 96))
        $source = 'primary screen, DPI-adjusted'
    }

    if ($width -gt 0) {
        $tooNarrow = $width -lt 1366 -or $height -lt 768
        Add-Finding -Stage 'Preflight' -Name 'Screen resolution' -Value ("{0}x{1} ({2})" -f $width, $height, $source) -Status $(
            if ($tooNarrow) { 'WARN' } else { 'PASS' }
        ) -Advice $(
            if ($tooNarrow) { 'Below the 1366x768 UI baseline. Check the layout on this machine specifically.' } else { '' }
        )
    } else {
        Add-Finding -Stage 'Preflight' -Name 'Screen resolution' -Value 'could not be determined' -Status 'WARN'
    }
    Add-Finding -Stage 'Preflight' -Name 'Display scaling' -Value ("{0}% ({1} DPI)" -f $scalePercent, $dpi) -Status $(
        if ($scalePercent -gt 150) { 'WARN' } else { 'PASS' }
    ) -Advice $(
        if ($scalePercent -gt 150) { 'Above 150%. The UI baseline is tested to 125%; check the layout on this machine specifically.' } else { '' }
    )
} catch {
    Add-Finding -Stage 'Preflight' -Name 'Display' -Value 'could not be read' -Status 'WARN' -Advice $_.Exception.Message
}

# .NET Desktop Runtime. The WPF clients need Microsoft.WindowsDesktop.App 10.x.
$desktopRuntimes = @()
$sharedDir = Join-Path $env:ProgramFiles 'dotnet\shared\Microsoft.WindowsDesktop.App'
if (Test-Path $sharedDir) {
    $desktopRuntimes = Get-ChildItem $sharedDir -Directory | ForEach-Object { $_.Name }
}
$hasNet10Desktop = @($desktopRuntimes | Where-Object { $_ -like '10.*' }).Count -gt 0

Add-Finding -Stage 'Preflight' -Name '.NET Desktop Runtime' -Value $(
    if ($desktopRuntimes.Count) { ($desktopRuntimes -join ', ') } else { 'none installed' }
) -Status $(
    if ($hasNet10Desktop) { 'PASS' } else { 'WARN' }
) -Advice $(
    if ($hasNet10Desktop) { '' }
    else { 'No .NET 10 desktop runtime. Either install it, or use the self-contained client build, which carries its own runtime and needs nothing here.' }
)

# Which network is this machine on? Useful context when a later stage fails.
try {
    $profiles = Get-NetConnectionProfile -ErrorAction SilentlyContinue
    foreach ($prof in $profiles) {
        Add-Finding -Stage 'Preflight' -Name 'Network' -Value ("{0} [{1}, {2}]" -f $prof.Name, $prof.InterfaceAlias, $prof.NetworkCategory)
    }
} catch {
    Add-Finding -Stage 'Preflight' -Name 'Network' -Value 'could not be enumerated' -Status 'INFO'
}

if ($CaptureOnly) {
    Write-Host ""
    Write-Host "-CaptureOnly: stopping before any change is made." -ForegroundColor Cyan
} elseif (-not $isElevated) {
    Write-Host ""
    Write-Host "Not elevated - cannot continue past preflight. Writing the report anyway." -ForegroundColor Red
}

$canContinue = (-not $CaptureOnly) -and $isElevated

# ---------------------------------------------------------------- stage 2 ---
if ($canContinue) {
    Write-Host ""
    Write-Host "STAGE 2  TRUST - import the host certificate" -ForegroundColor Cyan
    Write-Host ""

    if (-not $CertificatePath) {
        $CertificatePath = Join-Path $PSScriptRoot 'merch-host.cer'
    }

    if ($CertificatePath -like '*.pfx') {
        Add-Finding -Stage 'Trust' -Name 'Certificate file' -Value 'refused - .pfx supplied' -Status 'FAIL' `
            -Advice 'A .pfx carries the PRIVATE KEY and must never leave the host. Copy merch-host.cer instead, which is the public certificate.'
    } elseif (-not (Test-Path $CertificatePath)) {
        Add-Finding -Stage 'Trust' -Name 'Certificate file' -Value "not found: $CertificatePath" -Status 'FAIL' `
            -Advice 'Copy merch-host.cer from the host next to this script, or pass -CertificatePath.'
    } else {
        try {
            $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 $CertificatePath
            $already = Get-ChildItem Cert:\LocalMachine\Root |
                       Where-Object { $_.Thumbprint -eq $cert.Thumbprint }

            if ($already) {
                Add-Finding -Stage 'Trust' -Name 'Trusted Root' -Value 'already present, unchanged' -Status 'PASS'
            } else {
                $null = Import-Certificate -FilePath $CertificatePath -CertStoreLocation Cert:\LocalMachine\Root
                Add-Finding -Stage 'Trust' -Name 'Trusted Root' -Value 'imported' -Status 'PASS'
            }
            Add-Finding -Stage 'Trust' -Name 'Thumbprint' -Value $cert.Thumbprint
            Add-Finding -Stage 'Trust' -Name 'Certificate expires' -Value $cert.NotAfter.ToString('yyyy-MM-dd') -Status $(
                if ($cert.NotAfter -lt (Get-Date)) { 'FAIL' }
                elseif ($cert.NotAfter -lt (Get-Date).AddDays(30)) { 'WARN' }
                else { 'PASS' }
            ) -Advice $(
                if ($cert.NotAfter -lt (Get-Date)) { 'Already expired. Re-issue on the host with create-dev-certificate.ps1.' }
                elseif ($cert.NotAfter -lt (Get-Date).AddDays(30)) { 'Expires within 30 days - check it outlasts the presentation date.' }
                else { '' }
            )
        } catch {
            Add-Finding -Stage 'Trust' -Name 'Trusted Root' -Value 'import failed' -Status 'FAIL' -Advice $_.Exception.Message
        }
    }
}

# ---------------------------------------------------------------- stage 3 ---
if ($canContinue) {
    Write-Host ""
    Write-Host "STAGE 3  RESOLVE - point MERCH-HOST at the API host" -ForegroundColor Cyan
    Write-Host ""

    $hostsPath = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
    try {
        $backup = "$hostsPath.merch-backup"
        if (-not (Test-Path $backup)) {
            Copy-Item $hostsPath $backup
        }

        # Idempotent: strip any line that maps MERCH-HOST, then append one.
        # Rewriting rather than appending is what makes this safe to run again
        # after the host moves to a different address.
        $lines = Get-Content $hostsPath
        $kept = $lines | Where-Object { $_ -notmatch "\s$HostName\s*$" -and $_ -notmatch "\s$HostName\s" }
        $entry = "{0}`t{1}" -f $HostIPv4, $HostName
        $kept += $entry
        Set-Content -Path $hostsPath -Value $kept -Encoding ASCII

        Add-Finding -Stage 'Resolve' -Name 'hosts entry' -Value $entry -Status 'PASS'
        Add-Finding -Stage 'Resolve' -Name 'hosts backup' -Value $backup

        # The DNS client caches negative lookups; flush or the next stage may
        # fail on a stale miss rather than on anything real.
        try { ipconfig /flushdns | Out-Null } catch { }
    } catch {
        Add-Finding -Stage 'Resolve' -Name 'hosts entry' -Value 'could not be written' -Status 'FAIL' -Advice $_.Exception.Message
    }
}

# ---------------------------------------------------------------- stage 4 ---
if ($canContinue) {
    Write-Host ""
    Write-Host "STAGE 4  VERIFY - does it actually work" -ForegroundColor Cyan
    Write-Host ""

    try {
        $resolved = [System.Net.Dns]::GetHostAddresses($HostName) |
                    Where-Object { $_.AddressFamily -eq 'InterNetwork' } |
                    ForEach-Object { $_.IPAddressToString }
        $ok = $resolved -contains $HostIPv4
        Add-Finding -Stage 'Verify' -Name 'MERCH-HOST resolves' -Value ($resolved -join ', ') -Status $(
            if ($ok) { 'PASS' } else { 'FAIL' }
        ) -Advice $(
            if ($ok) { '' } else { "Resolved to something other than $HostIPv4. Another hosts entry or a DNS server is winning." }
        )
    } catch {
        Add-Finding -Stage 'Verify' -Name 'MERCH-HOST resolves' -Value 'no resolution' -Status 'FAIL' -Advice $_.Exception.Message
    }

    # Raw TCP first. Separating reachability from TLS matters: a firewall block
    # and a certificate problem produce very different fixes, and one error
    # message covering both would send you to the wrong one.
    $tcpOpen = $false
    try {
        $client = New-Object System.Net.Sockets.TcpClient
        $async = $client.BeginConnect($HostIPv4, $Port, $null, $null)
        $tcpOpen = $async.AsyncWaitHandle.WaitOne(5000, $false) -and $client.Connected
        $client.Close()
    } catch {
        $tcpOpen = $false
    }
    Add-Finding -Stage 'Verify' -Name "TCP $HostIPv4`:$Port" -Value $(
        if ($tcpOpen) { 'open' } else { 'no route or refused' }
    ) -Status $(
        if ($tcpOpen) { 'PASS' } else { 'FAIL' }
    ) -Advice $(
        if ($tcpOpen) { '' }
        else { "Cannot reach the API port. On the HOST, check: the API is running; the Windows Firewall rule covers this network; and the hotspot network is classified Private, not Public. start-demo-network.ps1 checks all three." }
    )

    if ($tcpOpen) {
        # PowerShell 5.1 negotiates TLS 1.0 by default, which the API refuses
        # (P1-21 pins the floor). Without this line the failure looks like a
        # certificate problem and is not one.
        try {
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        } catch { }

        $healthUrl = "https://{0}:{1}/health" -f $HostName, $Port
        try {
            # Certificate validation deliberately left ON. See .DESCRIPTION.
            $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 15
            Add-Finding -Stage 'Verify' -Name 'HTTPS GET /health' -Value ("{0} {1}" -f [int] $response.StatusCode, $response.StatusDescription) -Status 'PASS'
            Add-Finding -Stage 'Verify' -Name 'Certificate chain' -Value 'validated against Trusted Root' -Status 'PASS'
        } catch {
            $detail = $_.Exception.Message
            $looksLikeTrust = $detail -match 'SSL|trust|certificate|secure channel'
            Add-Finding -Stage 'Verify' -Name 'HTTPS GET /health' -Value 'failed' -Status 'FAIL' -Advice $(
                if ($looksLikeTrust) { "TLS or trust failure - stage 2 did not take effect, or the certificate does not name $HostName. Detail: $detail" }
                else { "Reachable but did not answer. Is the API running? Detail: $detail" }
            )
        }
    }
}

# ---------------------------------------------------------------- stage 5 ---
Write-Host ""
Write-Host "STAGE 5  REPORT" -ForegroundColor Cyan
Write-Host ""

$desktop = [Environment]::GetFolderPath('Desktop')
if (-not $desktop) { $desktop = $PSScriptRoot }
$reportPath = Join-Path $desktop ("client-baseline-{0}.txt" -f $env:COMPUTERNAME)

$report = New-Object System.Text.StringBuilder
$null = $report.AppendLine("Merchandising System - client baseline and setup report")
$null = $report.AppendLine("Generated : {0}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'))
$null = $report.AppendLine("Machine   : {0}" -f $env:COMPUTERNAME)
$null = $report.AppendLine("User      : {0}" -f $env:USERNAME)
$null = $report.AppendLine(("Host IPv4 : {0}   Port: {1}" -f $HostIPv4, $Port))
$null = $report.AppendLine("Mode      : {0}" -f $(if ($CaptureOnly) { 'CaptureOnly (read-only)' } else { 'full setup' }))
$null = $report.AppendLine("")
$null = $report.AppendLine("This file answers P0-02 for this machine. Send it back as-is.")
$null = $report.AppendLine("")

foreach ($stage in @('Preflight', 'Trust', 'Resolve', 'Verify')) {
    $rows = $script:Findings | Where-Object { $_.Stage -eq $stage }
    if (-not $rows) { continue }
    $null = $report.AppendLine("--- $stage ---")
    foreach ($row in $rows) {
        $null = $report.AppendLine(("  [{0}] {1,-28} {2}" -f $row.Status, $row.Name, $row.Value))
        if ($row.Advice) { $null = $report.AppendLine("         -> " + $row.Advice) }
    }
    $null = $report.AppendLine("")
}

Set-Content -Path $reportPath -Value $report.ToString() -Encoding UTF8
Add-Finding -Stage 'Report' -Name 'Written to' -Value $reportPath -Status 'PASS'

Write-Host ""
if ($script:Blockers -eq 0 -and $canContinue) {
    Write-Host "RESULT: this machine is ready. All checks passed." -ForegroundColor Green
} elseif ($CaptureOnly) {
    Write-Host "RESULT: baseline captured. Nothing was changed." -ForegroundColor Cyan
} else {
    Write-Host ("RESULT: {0} blocker(s). Read the [FAIL] lines above - each one names its own fix." -f $script:Blockers) -ForegroundColor Red
}
Write-Host ("Report: {0}" -f $reportPath) -ForegroundColor Cyan
Write-Host ""

if ($script:Blockers -gt 0) { exit 1 }
