<#
.SYNOPSIS
    Installs (or removes) the Merchandising API as a Windows Service.

.DESCRIPTION
    P1-16 / spec section 6.4. Registers the published API under a restricted
    virtual service account, configures automatic start and recovery actions,
    creates the Event Log source, and grants that account the narrowest access
    it needs to the ACL-protected configuration directory.

    WHY THIS IS A SCRIPT AND NOT A PROCEDURE IN A DOCUMENT. Under ADR-012 this
    system is installed by three people who did not build it, on machines the
    author does not own, without the author present. Every step below was
    originally a command someone would have had to retype correctly under time
    pressure. A mistyped sc.exe argument produces a service that installs and
    then fails to start, which is the single worst failure mode available on
    presentation day.

    IDEMPOTENT. Running it twice is safe: an existing service is stopped and
    deleted before the new one is registered. That matters because the most
    likely time anyone runs this is the second time, after something went
    wrong the first time.

    Run from an ELEVATED PowerShell 7 session. Registering a service, creating
    an Event Log source, and editing an ACL all require administrator rights.

.PARAMETER PublishDir
    The directory holding the published API - the output of
    scripts/publish-release.ps1. Must contain Merchandising.Api.exe.

    ADR-010 publishes the API SELF-CONTAINED, deliberately: a host with no .NET
    runtime would otherwise give a service that silently fails to start, and
    the classroom host is not this machine.

.PARAMETER Uninstall
    Remove the service, its Event Log source, and its ACL grant, then exit.

.PARAMETER CheckOnly
    P6-10 / CARRY-05. Touches nothing - no service, no ACL, no Event Log.
    Calls the already-running service's /health endpoint, compares its
    commitSha to `git rev-parse HEAD` in this repository, prints both, and
    exits 1 on a mismatch. This is the standalone half of the staleness check
    described below; a normal install runs the same comparison automatically
    as its last step. Does not require an elevated session.

.EXAMPLE
    pwsh -File scripts/install-service.ps1 -PublishDir C:\MerchandisingApi
    pwsh -File scripts/install-service.ps1 -Uninstall
    pwsh -File scripts/install-service.ps1 -CheckOnly
#>

[CmdletBinding()]
param(
    [string] $PublishDir,
    [string] $ConfigDir = "$env:ProgramData\MerchandisingSystem\config",
    [switch] $Uninstall,
    [switch] $CheckOnly
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# THESE FOUR VALUES ARE NOT FREE TO EDIT HERE.
#
# They are compiled into the API as Merchandising.Api.Hosting.WindowsServiceInfo
# and WindowsServiceInfoTests fails the build if this script disagrees with it.
# The service name appears in four places that nothing else relates: sc.exe,
# the Event Log source, the NT SERVICE account whose SID gets the ACL grant,
# and the Get-Service call the post-reboot check makes. Change the Visual Basic
# constant and let the test drive this file - not the other way round.
# ---------------------------------------------------------------------------
$ServiceName  = 'MerchandisingApi'
$DisplayName  = 'Merchandising System API'
$AccountName  = 'NT SERVICE\MerchandisingApi'
$Description  = 'Serves the Merchandising System API over HTTPS on port 8443 for the Procurement, Inventory and POS clients. The store''s database is reachable only through this service.'

function Write-Step { param([string] $Text) Write-Host "`n=== $Text ===" -ForegroundColor Cyan }
function Write-Ok   { param([string] $Text) Write-Host "  OK   $Text" -ForegroundColor Green }
function Write-Info { param([string] $Text) Write-Host "       $Text" -ForegroundColor DarkGray }
function Write-Warn { param([string] $Text) Write-Host "  WARN $Text" -ForegroundColor Yellow }

function Stop-WithError {
    param([string] $Message, [string] $Fix)
    Write-Host "`n  FAIL $Message" -ForegroundColor Red
    if ($Fix) { Write-Host "       $Fix" -ForegroundColor Yellow }
    exit 1
}

# ---------------------------------------------------------------------------
# sc.exe reports failure through the exit code, not through PowerShell's error
# stream, so every call has to be checked. Note sc.exe and NOT sc: in
# PowerShell, `sc` is an alias for Set-Content, and the resulting error message
# is nothing like the actual problem.
#
# sc.exe's argument syntax is also unusual - the space after each `=` is
# REQUIRED, and `binPath= "x"` is one argument pair while `binPath="x"` is not.
# ---------------------------------------------------------------------------
function Invoke-Sc {
    param([string[]] $ScArgs, [string] $What)

    $output = & sc.exe @ScArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        Stop-WithError "$What failed (sc.exe exit $LASTEXITCODE)." ($output -join "`n       ")
    }
    return $output
}

function Get-RepoRoot {
    Split-Path -Parent $PSScriptRoot
}

function Get-GitHeadSha {
    param([string] $RepoRoot)

    $sha = (& git -C $RepoRoot rev-parse HEAD 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $sha -notmatch '^[0-9a-f]{40}$') {
        Stop-WithError "Could not resolve 'git rev-parse HEAD' in '$RepoRoot' (got '$sha')." 'Run this script from inside the repository clone.'
    }
    return $sha
}

# ---------------------------------------------------------------------------
# P6-10 / CARRY-05 - THE WHOLE POINT OF THIS CARD.
#
# The Phase 4 and Phase 5 gates both found the deployed service stale, by
# hand, and nothing in this repository could have noticed on its own: the
# integration suite builds the host in-process from the CURRENT tree, so any
# staleness check run through it would always agree with itself. This
# function is the only place that compares what the TREE says against what
# the RUNNING PROCESS says - fed by /health's commitSha, which HealthController
# reads from an AssemblyMetadata value baked in at PUBLISH time
# (Merchandising.Api.vbproj / scripts/publish-release.ps1), never re-read from
# the tree at request time.
# ---------------------------------------------------------------------------
function Test-DeployedBuildIdentity {
    param([string] $RepoRoot)

    $expected = Get-GitHeadSha -RepoRoot $RepoRoot

    try {
        $health = Invoke-RestMethod -Uri 'https://127.0.0.1:8443/health' -SkipCertificateCheck -TimeoutSec 10
    }
    catch {
        Stop-WithError 'Could not reach https://127.0.0.1:8443/health to check build identity.' $_.Exception.Message
    }

    $deployed = $health.commitSha

    Write-Host ''
    Write-Host '=== build identity check ===' -ForegroundColor Cyan
    Write-Host "  git rev-parse HEAD : $expected"
    Write-Host "  deployed commitSha : $deployed"

    if ($deployed -eq 'unknown') {
        Stop-WithError 'The deployed build reports commitSha "unknown" - it was never published through scripts/publish-release.ps1.' `
            'Publish with scripts/publish-release.ps1, which stamps the real commit at publish time, then reinstall.'
    }

    if ($deployed -ne $expected) {
        Stop-WithError 'STALE DEPLOYMENT: the running service was published from a different commit than the current tree.' `
            'Republish (scripts/publish-release.ps1) and reinstall (scripts/install-service.ps1 -PublishDir ...) from the current commit.'
    }

    Write-Ok 'Deployed build matches the current commit.'
}

if ($CheckOnly) {
    Write-Host ''
    Write-Host 'Merchandising System - build identity check (P6-10 / CARRY-05)' -ForegroundColor White
    Write-Host 'Read-only: no service, ACL or Event Log change. Does not require elevation.' -ForegroundColor DarkGray

    Test-DeployedBuildIdentity -RepoRoot (Get-RepoRoot)
    exit 0
}

Write-Host ''
Write-Host 'Merchandising System - Windows Service installer' -ForegroundColor White
Write-Host 'P1-16 / spec section 6.4. Read this script before running it.' -ForegroundColor DarkGray

# ===========================================================================
# 1. PREFLIGHT - everything that can be checked is checked before anything is
#    changed. A setup script that fails halfway leaves the machine in a state
#    nobody documented, held by the person who by definition does not know
#    this system.
# ===========================================================================
Write-Step 'Preflight'

$identity  = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [System.Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Stop-WithError 'This script must run elevated.' 'Start PowerShell with "Run as administrator" and run it again.'
}
Write-Ok 'Running elevated.'

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if (-not $Uninstall) {
    if (-not $PublishDir) {
        Stop-WithError '-PublishDir is required when installing.' 'Publish first: pwsh -File scripts/publish-release.ps1'
    }

    $PublishDir = (Resolve-Path -LiteralPath $PublishDir -ErrorAction SilentlyContinue)?.Path
    if (-not $PublishDir) {
        Stop-WithError 'The -PublishDir path does not exist.' 'Publish first: pwsh -File scripts/publish-release.ps1'
    }

    $exePath = Join-Path $PublishDir 'Merchandising.Api.exe'
    if (-not (Test-Path -LiteralPath $exePath)) {
        Stop-WithError "No Merchandising.Api.exe in '$PublishDir'." 'Point -PublishDir at the published output, not at the source tree.'
    }
    Write-Ok "Binary found: $exePath"

    # The publish directory must NOT be inside the repository. A service whose
    # binPath points into a working tree gets its files replaced by the next
    # build, mid-run, with no warning.
    $repoRoot = Split-Path -Parent $PSScriptRoot
    if ($PublishDir.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Write-Warn "The publish directory is inside the repository ($repoRoot)."
        Write-Info 'A later build will overwrite the running service''s files. Publish somewhere else for a real install.'
    }

    foreach ($required in @('database.json', 'certificate.json')) {
        $path = Join-Path $ConfigDir $required
        if (-not (Test-Path -LiteralPath $path)) {
            Stop-WithError "Missing configuration file: $path" 'Run scripts/bootstrap.ps1 first - it creates the config directory and its contents.'
        }
    }
    Write-Ok "Configuration present in $ConfigDir"
}

# ===========================================================================
# 2. REMOVE ANY EXISTING SERVICE
#
# Unconditional, and it runs on the install path too. Re-registering over a
# live service is not supported by sc.exe, and the alternative - detecting
# "is the existing service the same as the one I am about to install" - is a
# comparison nobody can get right for binPath, account, and recovery actions
# simultaneously. Delete and recreate is the honest operation.
# ===========================================================================
if ($existing) {
    Write-Step "Removing existing service '$ServiceName'"

    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
        Write-Ok 'Stopped.'
    }

    Invoke-Sc @('delete', $ServiceName) 'Deleting the existing service' | Out-Null

    # sc.exe delete is asynchronous. The service key lingers until every handle
    # to it closes, and creating a new service with the same name in that
    # window fails with "marked for deletion" - a confusing error that looks
    # like a permissions problem.
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
    }
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Stop-WithError 'The old service is still registered after 30 seconds.' 'Close services.msc and any Event Viewer window, then run this again.'
    }
    Write-Ok 'Removed.'
}
elseif ($Uninstall) {
    Write-Info "No service named '$ServiceName' is registered."
}

if ($Uninstall) {
    Write-Step 'Removing the Event Log source and the ACL grant'

    if ([System.Diagnostics.EventLog]::SourceExists($ServiceName)) {
        [System.Diagnostics.EventLog]::DeleteEventSource($ServiceName)
        Write-Ok "Event Log source '$ServiceName' removed."
    }
    else {
        Write-Info 'No Event Log source to remove.'
    }

    # icacls resolves the account by SID. Once the service is deleted the
    # virtual account no longer resolves by name, so the grant is removed by
    # SID-shaped orphan entry instead - /remove takes the name while it still
    # resolves, and silently does nothing afterwards. Attempt it and report.
    & icacls "$ConfigDir" /remove $AccountName /t /c *>&1 | Out-Null
    Write-Ok "ACL grant for $AccountName removed from $ConfigDir (if it resolved)."

    Write-Host "`nUninstall complete." -ForegroundColor Green
    exit 0
}

# ===========================================================================
# 3. REGISTER THE SERVICE
#
# obj= "NT SERVICE\<name>" with an empty password requests a VIRTUAL SERVICE
# ACCOUNT. Windows creates it implicitly from the service name; there is no
# account to create beforehand, no password to store or hand over, and it is
# not an administrator - which is what spec section 17's "restricted service
# identity where practical" asks for. It also carries its own SID, so the ACL
# grant in step 5 names THIS service rather than every service on the host
# that happens to share a built-in account like NetworkService.
# ===========================================================================
Write-Step "Registering '$ServiceName'"

# ---------------------------------------------------------------------------
# BOOT ORDERING AGAINST MARIADB.
#
# Both services are automatic-start, so at boot they race. The API would win
# often enough to look fine: it reads its database configuration from a FILE at
# startup and never opens a connection until the first request, so it starts,
# serves /health, and reports healthy with no database behind it. The failure
# surfaces later, as the first login of the morning, on the machine of whoever
# is presenting.
#
# DETECTED, NOT HARDCODED. XAMPP registers this service as 'mysql' on this
# host, but the name is a property of how someone installed XAMPP, and under
# ADR-012 that someone is not us. A hardcoded depend= naming a service that
# does not exist would leave our own service unable to start at all - trading a
# short race for a hard failure. So the service is located by its image path
# (mysqld.exe) and its actual name used, with a warning if nothing matches.
# ---------------------------------------------------------------------------
$dbService = Get-CimInstance Win32_Service |
             Where-Object { $_.PathName -match 'mysqld' } |
             Select-Object -First 1

if ($dbService) {
    Write-Ok "Database service found: '$($dbService.Name)' ($($dbService.StartMode)) - will be declared as a dependency."
    if ($dbService.StartMode -ne 'Auto') {
        Write-Warn "'$($dbService.Name)' is $($dbService.StartMode), not Auto. It will not start on its own at boot."
    }
}
else {
    Write-Warn 'No mysqld-based service found. The API will be registered with no database dependency.'
    Write-Info 'At boot the API may begin serving before MariaDB is ready; the first request needing the database would fail.'
}

# binPath is passed WITH embedded quotes. The SCM stores this string verbatim
# and splits it on whitespace when it launches the process, so an unquoted path
# containing a space registers a service that starts the wrong executable - or
# nothing. No password argument: a virtual service account has none, and
# supplying an empty one is not the same as omitting it.
$createArgs = @(
    'create', $ServiceName,
    'binPath=', "`"$exePath`"",
    'DisplayName=', $DisplayName,
    'start=', 'auto',
    'obj=', $AccountName
)
if ($dbService) { $createArgs += @('depend=', $dbService.Name) }

Invoke-Sc $createArgs 'Creating the service' | Out-Null
Write-Ok "Created, start=auto, running as $AccountName"

# READ BACK WHAT WAS ACTUALLY REGISTERED, rather than trusting the quoting
# above. PowerShell's native-argument passing has changed behaviour across
# versions, and a mis-quoted image path produces a service that installs
# cleanly and then fails to start - discovered, otherwise, only by whoever is
# standing in front of the class. Cheap to check, expensive to miss.
$registeredPath = (Get-CimInstance Win32_Service -Filter "Name='$ServiceName'").PathName
if ($registeredPath -notlike "*$exePath*") {
    Stop-WithError "The registered image path does not contain the expected executable." `
        "Registered: $registeredPath`n       Expected to contain: $exePath"
}
Write-Ok "Image path verified: $registeredPath"

Invoke-Sc @('description', $ServiceName, $Description) 'Setting the description' | Out-Null
Write-Ok 'Description set.'

# ===========================================================================
# 4. RECOVERY ACTIONS
#
# Escalating delays rather than three identical ones: an API that died because
# MariaDB has not finished starting recovers on the first retry, while one
# that died because its certificate is missing must not spin in a restart loop
# filling the Event Log. reset= 86400 means the failure count returns to zero
# after a day without incident.
#
# failureflag= 1 IS THE ONE PEOPLE MISS. Without it the SCM applies recovery
# actions only when the process CRASHES. A .NET host that catches a fatal
# startup error and exits with a non-zero code has, as far as the SCM is
# concerned, stopped normally - and would never be restarted. That is the
# exact shape of the failure this card's third box tests.
# ===========================================================================
Write-Step 'Configuring recovery actions'

Invoke-Sc @(
    'failure', $ServiceName,
    'reset=', '86400',
    'actions=', 'restart/5000/restart/10000/restart/30000'
) 'Setting recovery actions' | Out-Null
Write-Ok 'Restart after 5s, then 10s, then 30s. Failure count resets after 24h.'

Invoke-Sc @('failureflag', $ServiceName, '1') 'Enabling recovery on non-crash failure' | Out-Null
Write-Ok 'Recovery also applies to a non-zero exit, not only to a crash.'

# ===========================================================================
# 5. EVENT LOG SOURCE
#
# Created here, at install time, because creating a source requires
# administrator rights. The service account does not have them, so if the
# source did not exist the first log write would fail - and the symptom would
# be an absent log rather than an error, which is the worst way to lose
# diagnostics on a machine you cannot inspect.
# ===========================================================================
Write-Step 'Registering the Event Log source'

if ([System.Diagnostics.EventLog]::SourceExists($ServiceName)) {
    Write-Info "Source '$ServiceName' already exists."
}
else {
    New-EventLog -LogName Application -Source $ServiceName
    Write-Ok "Source '$ServiceName' created in the Application log."
}

# ===========================================================================
# 6. FILESYSTEM ACCESS - the narrowest grant that works
#
# The config directory is SYSTEM/Administrators only (P0-07 disabled
# inheritance deliberately: database.json holds the merch_api password and a
# dump would contain every password hash). Read and execute, no write: the API
# reads this configuration and has no business changing it.
#
# The virtual account only resolves by name after the service exists, which is
# why this step follows registration rather than preceding it.
# ===========================================================================
Write-Step 'Granting the service account read access to its configuration'

& icacls "$ConfigDir" /grant "${AccountName}:(OI)(CI)(RX)" /c *>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Stop-WithError "Could not grant $AccountName access to $ConfigDir." 'The service will start and then fail to read database.json.'
}
Write-Ok "$ConfigDir - read and execute, inherited by contents."

# The .pfx usually lives beside the config directory rather than inside it, so
# its ACL is separate. Read the path from certificate.json rather than assuming
# it: P1-09 made that path configurable, and guessing it here would produce a
# service that starts everywhere except where someone moved the certificate.
$certConfig = Get-Content (Join-Path $ConfigDir 'certificate.json') -Raw | ConvertFrom-Json
$pfxPath = $certConfig.pfxPath

if ($pfxPath -and (Test-Path -LiteralPath $pfxPath)) {
    & icacls "$pfxPath" /grant "${AccountName}:(R)" /c *>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Stop-WithError "Could not grant $AccountName read access to $pfxPath." 'Kestrel will fail to bind the HTTPS listener.'
    }
    Write-Ok "$pfxPath - read."

    # ------------------------------------------------------------------
    # AND TAKE IT AWAY FROM EVERYONE ELSE.
    #
    # Found at P1-16 by reading the ACL back instead of assuming it: the
    # .pfx inherited BUILTIN\Users:(I)(RX) from its parent directory, so
    # every local account on the host could read the file holding the
    # server's private key. P0-07 disabled inheritance on the config
    # directory for exactly this reason and the certs directory beside it
    # never got the same treatment.
    #
    # The key is password-protected and that password lives in
    # certificate.json, which IS restricted - so this was defence in depth
    # rather than an open door. Defence in depth is the point: under
    # ADR-012 the host is a shared laptop belonging to someone else, and
    # "the other account on this machine cannot read the private key" is a
    # cheaper guarantee to make than to explain.
    #
    # Inheritance is disabled with /inheritance:d, which COPIES the
    # inherited entries first - so SYSTEM and Administrators keep their
    # access and only the removal below actually changes anything.
    # ------------------------------------------------------------------
    & icacls "$pfxPath" /inheritance:d *>&1 | Out-Null
    & icacls "$pfxPath" /remove:g 'BUILTIN\Users' *>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Warn "Could not remove BUILTIN\Users from $pfxPath. Check the ACL by hand."
    }
    else {
        Write-Ok "$pfxPath - inheritance broken, BUILTIN\Users removed."
    }
}
else {
    Write-Warn "certificate.json points at '$pfxPath', which does not exist. HTTPS will fail at startup."
}

# ===========================================================================
# 7. START, AND PROVE IT SERVES
#
# "The service reached Running" is not the same claim as "the API is serving".
# A host that fails during startup can leave the SCM reporting Running for
# several seconds. The health request is what actually closes the loop.
# ===========================================================================
Write-Step 'Starting'

Start-Service -Name $ServiceName
$service = Get-Service -Name $ServiceName
$service.WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
Write-Ok "Service status: $($service.Status)"

$listening = $false
$deadline = (Get-Date).AddSeconds(30)
while (-not $listening -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    try {
        $probe = [System.Net.Sockets.TcpClient]::new()
        $probe.Connect('127.0.0.1', 8443)
        $probe.Dispose()
        $listening = $true
    }
    catch { }
}

if (-not $listening) {
    Stop-WithError 'The service is Running but nothing is listening on 8443.' `
        "Check the Application event log, source '$ServiceName' - that is where a startup failure is reported."
}
Write-Ok 'Listening on 8443.'

# ===========================================================================
# 8. BUILD IDENTITY - P6-10 / CARRY-05
#
# "The service reached Running and answers on 8443" is not "the service is
# CURRENT" - that was found the hard way at the Phase 4 and Phase 5 gates,
# both times by hand, four weeks stale. Stop-WithError inside
# Test-DeployedBuildIdentity exits non-zero on a mismatch, so this install is
# never reported successful while serving a different commit than the one on
# disk.
# ===========================================================================
Write-Step 'Verifying build identity'
Test-DeployedBuildIdentity -RepoRoot (Get-RepoRoot)

Write-Host ''
Write-Host 'Install complete.' -ForegroundColor Green
Write-Info "Verify TLS:      pwsh -File scripts/probe-tls.ps1 -ConnectHost 127.0.0.1 -TlsName MERCH-HOST"
Write-Info "Read its log:    Get-EventLog -LogName Application -Source $ServiceName -Newest 20"
Write-Info "Recovery config: sc.exe qfailure $ServiceName"
