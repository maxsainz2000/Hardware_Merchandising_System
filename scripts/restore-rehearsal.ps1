#Requires -Version 5.1
<#
.SYNOPSIS
    Runs the full spec section 15 restore procedure end to end and times it
    (P1-18). Elevated. DESTRUCTIVE - it replaces the live database.

.DESCRIPTION
    Run this ON THE API HOST, in an elevated session, during a maintenance
    window you have declared.

    THIS SCRIPT REPLACES THE LIVE DATABASE. Everything written since the
    backup you select is gone when it finishes. It asks for confirmation
    before touching anything, and -WhatIf walks the whole procedure without
    executing the destructive steps.

    It exists because the seven steps of spec section 15 are not a sequence
    anyone should be following by hand at the moment they matter. Each of
    these is a step someone forgets under pressure:

      1. Enter maintenance mode, with a reason         (API, SuperAdmin)
      2. Confirm the API is refusing writes            (proven, not assumed)
      3. Stop the API service                          (needs elevation)
      4. Restore the chosen backup                     (merch_migrator)
      5. Verify: schema complete, expected rows present
      6. Start the API service, confirm health
      7. Release maintenance mode - ONLY if 5 passed

    THE MEASURED NUMBER. Total elapsed from step 3 to step 6 is the RTO, and
    it is compared against PA-005's demonstrated target of 15 minutes to a
    verified state. The script prints the real figure whether it hits or
    misses; a miss is information, not something to hide.

    WHY STEP 7 IS CONDITIONAL. Spec section 15 step 7 releases maintenance
    mode "only after verification succeeds". If step 5 fails, this script
    leaves the system IN MAINTENANCE deliberately. That is the safe state: a
    half-restored database that is open for business is worse than one that
    is visibly closed.

.PARAMETER BackupFile
    Dump to restore. Defaults to the newest on the off-host volume, because
    restoring from the off-host copy is what the card actually asks to prove -
    a local-only restore does not exercise the copy that survives losing the
    host.

.PARAMETER OffHostVolumeLabel
    Volume label to search for the backup. Default MERCHBACKUP.

.PARAMETER SuperAdminUsername
    Account used to enter and release maintenance mode. Prompted for its
    password; never taken as a plaintext parameter.

.PARAMETER ApiBaseUrl
    Default https://MERCH-HOST:8443.

.PARAMETER WhatIf
    Walk every step and print what would happen. Nothing is stopped, restored
    or released.

.EXAMPLE
    pwsh ./scripts/restore-rehearsal.ps1 -WhatIf

.EXAMPLE
    pwsh ./scripts/restore-rehearsal.ps1 -SuperAdminUsername admin
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string] $BackupFile,
    [string] $OffHostVolumeLabel = 'MERCHBACKUP',
    [Parameter(Mandatory = $true)] [string] $SuperAdminUsername,

    # Password without a console. Read-Host needs a console handle, and this
    # script is most useful when its output is being captured - at which point
    # the streams are redirected and Read-Host fails with "The handle is
    # invalid" before anything has happened. Create the file once with:
    #     Get-Credential | Export-Clixml $env:USERPROFILE\merch-superadmin.xml
    # It is DPAPI-encrypted to this user on this machine, so it is not a
    # plaintext password sitting on disk, and it never reaches shell history.
    [System.Management.Automation.PSCredential] $Credential,
    [string] $CredentialPath,

    # Transcript. Built in rather than left to Tee-Object for the same reason:
    # piping this script's output is what breaks its own prompt.
    [string] $LogFile,
    [string] $ApiBaseUrl = 'https://MERCH-HOST:8443',
    [string] $ServiceName = 'MerchandisingApi',
    [string] $MaintenanceExe
)

$ErrorActionPreference = 'Stop'

if ($LogFile) {
    # Transcript rather than redirection, so the console stays a console and
    # any prompt this script still needs keeps working.
    Start-Transcript -Path $LogFile -Force | Out-Null
}

function Write-Step { param([string] $Text) Write-Host "`n=== $Text ===" -ForegroundColor Cyan }
function Write-Ok   { param([string] $Text) Write-Host "  [ok]   $Text" -ForegroundColor Green }
function Write-Warn { param([string] $Text) Write-Host "  [warn] $Text" -ForegroundColor Yellow }
function Write-Bad  { param([string] $Text) Write-Host "  [FAIL] $Text" -ForegroundColor Red }

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Warning 'This session is not elevated. Stopping and starting a Windows Service requires it. No changes made.'
    exit 1
}

if (-not $MaintenanceExe) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $MaintenanceExe = Join-Path $repoRoot 'src\Merchandising.Maintenance\bin\Release\net10.0\Merchandising.Maintenance.exe'
}
if (-not (Test-Path $MaintenanceExe)) {
    Write-Bad "Merchandising.Maintenance.exe not found at $MaintenanceExe. Build it, or pass -MaintenanceExe."
    exit 1
}

# --------------------------------------------------------- choose the dump --
Write-Step '0. Selecting the backup to restore'

if (-not $BackupFile) {
    $volume = Get-Volume | Where-Object { $_.FileSystemLabel -eq $OffHostVolumeLabel } | Select-Object -First 1
    if (-not $volume) {
        Write-Bad "No volume labelled '$OffHostVolumeLabel' is attached. Plug in the backup drive, or pass -BackupFile."
        exit 1
    }
    $folder = Join-Path ($volume.DriveLetter + ':\') 'MerchandisingBackups'
    $BackupFile = (Get-ChildItem $folder -Filter '*.sql' | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}

if (-not $BackupFile -or -not (Test-Path $BackupFile)) {
    Write-Bad "No backup file found. Pass -BackupFile explicitly."
    exit 1
}

Write-Ok "Restoring from: $BackupFile"
Write-Ok ("Size: {0:N0} bytes, written {1}" -f (Get-Item $BackupFile).Length, (Get-Item $BackupFile).LastWriteTime)

if (-not $PSCmdlet.ShouldProcess($BackupFile, "REPLACE the live merchandising database with this backup")) {
    Write-Warn 'WhatIf / declined - walking the remaining steps without executing them.'
    $simulate = $true
} else {
    $simulate = $false
}

# ------------------------------------------------------------ credentials --
# Three sources, in order of preference. The prompt is LAST because it is the
# one that cannot work when this script's output is being captured.
if (-not $Credential -and $CredentialPath) {
    if (-not (Test-Path $CredentialPath)) {
        Write-Bad "Credential file not found: $CredentialPath"
        if ($LogFile) { Stop-Transcript | Out-Null }
        exit 1
    }
    $Credential = Import-Clixml $CredentialPath
}

if (-not $Credential) {
    $defaultCredentialPath = Join-Path $env:USERPROFILE 'merch-superadmin.xml'
    if (Test-Path $defaultCredentialPath) {
        $Credential = Import-Clixml $defaultCredentialPath
        Write-Ok "Using saved credential from $defaultCredentialPath"
    }
}

if (-not $Credential) {
    try {
        $securePassword = Read-Host -AsSecureString "Password for SuperAdmin '$SuperAdminUsername'"
        $Credential = New-Object System.Management.Automation.PSCredential($SuperAdminUsername, $securePassword)
    } catch {
        Write-Bad 'Could not prompt for a password - this session has no console handle.'
        Write-Host '  That happens whenever this script output is piped or redirected.' -ForegroundColor Red
        Write-Host '  Save the credential once, then re-run:' -ForegroundColor Red
        Write-Host '' -ForegroundColor Red
        Write-Host '      Get-Credential | Export-Clixml $env:USERPROFILE\merch-superadmin.xml' -ForegroundColor White
        Write-Host '' -ForegroundColor Red
        Write-Host '  Nothing has been changed.' -ForegroundColor Red
        if ($LogFile) { Stop-Transcript | Out-Null }
        exit 1
    }
}

$plainPassword = $Credential.GetNetworkCredential().Password

# ------------------------------------------------- 1. enter maintenance ----
Write-Step '1. Entering maintenance mode'

$loginBody = @{ username = $SuperAdminUsername; password = $plainPassword } | ConvertTo-Json
$login = Invoke-RestMethod -Uri "$ApiBaseUrl/api/v1/auth/login" -Method Post -Body $loginBody -ContentType 'application/json'
$headers = @{ Authorization = "Bearer $($login.token)" }
Write-Ok "Authenticated as $SuperAdminUsername"

$reason = "Restore rehearsal $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
if (-not $simulate) {
    $enterBody = @{ reason = $reason } | ConvertTo-Json
    Invoke-RestMethod -Uri "$ApiBaseUrl/api/v1/admin/maintenance/enter" -Method Post -Headers $headers `
                      -Body $enterBody -ContentType 'application/json' | Out-Null
    Write-Ok "Maintenance mode ON - $reason"
} else {
    Write-Warn 'skipped (simulation)'
}

# ------------------------------------------ 2. confirm writes are refused --
Write-Step '2. Confirming the API refuses ordinary writes'

if (-not $simulate) {
    $probeBody = @{ productId = 1; quantity = 1; reason = 'rehearsal probe'; idempotencyKey = [guid]::NewGuid().ToString() } | ConvertTo-Json
    try {
        Invoke-RestMethod -Uri "$ApiBaseUrl/api/v1/inventory/stock/decrement" -Method Post -Headers $headers `
                          -Body $probeBody -ContentType 'application/json' | Out-Null
        Write-Bad 'A write SUCCEEDED while maintenance mode was on. Stopping - the lock is not doing its job.'
        exit 1
    } catch {
        if ($_.Exception.Response.StatusCode.value__ -eq 503) {
            Write-Ok 'Write correctly refused with 503 MAINTENANCE_MODE'
        } else {
            Write-Bad "Write was refused, but with $($_.Exception.Response.StatusCode.value__), not 503. Investigate before continuing."
            exit 1
        }
    }
} else {
    Write-Warn 'skipped (simulation)'
}

# ------------------------------------------------- 3-6. the timed window ---
Write-Step '3. Stopping the API service  [RTO CLOCK STARTS]'

$clock = [Diagnostics.Stopwatch]::StartNew()

if (-not $simulate) {
    Stop-Service -Name $ServiceName -Force
    (Get-Service -Name $ServiceName).WaitForStatus('Stopped', '00:02:00')
    Write-Ok "Service '$ServiceName' stopped"
} else {
    Write-Warn 'skipped (simulation)'
}

Write-Step '4-5. Restoring and verifying'

$restoreOk = $false
if (-not $simulate) {
    & $MaintenanceExe restore --file $BackupFile
    $restoreOk = ($LASTEXITCODE -eq 0)
    if ($restoreOk) { Write-Ok 'Restore verified - schema complete and expected rows present' }
    else { Write-Bad 'Restore FAILED verification. The system stays in maintenance.' }
} else {
    Write-Warn 'skipped (simulation)'
    $restoreOk = $true
}

Write-Step '6. Starting the API service and confirming health'

if (-not $simulate) {
    Start-Service -Name $ServiceName
    (Get-Service -Name $ServiceName).WaitForStatus('Running', '00:02:00')

    $healthy = $false
    for ($i = 0; $i -lt 30 -and -not $healthy; $i++) {
        try {
            $health = Invoke-WebRequest -Uri "$ApiBaseUrl/health" -UseBasicParsing -TimeoutSec 5
            if ($health.StatusCode -eq 200) { $healthy = $true }
        } catch { Start-Sleep -Seconds 2 }
    }
    if ($healthy) { Write-Ok 'API responding on /health' } else { Write-Bad 'API did not become healthy'; $restoreOk = $false }
} else {
    Write-Warn 'skipped (simulation)'
}

$clock.Stop()
$rtoMinutes = $clock.Elapsed.TotalMinutes

Write-Host ''
Write-Host ('MEASURED RTO: {0:F2} minutes ({1:F1} seconds) to verified state' -f $rtoMinutes, $clock.Elapsed.TotalSeconds) -ForegroundColor White
if ($rtoMinutes -le 15) {
    Write-Ok 'Within the PA-005 demonstrated target of 15 minutes'
} else {
    Write-Warn "ABOVE the PA-005 target of 15 minutes. Record the real number - a miss is information, not a failure to hide."
}

# ----------------------------------------------------- 7. release the lock --
Write-Step '7. Releasing maintenance mode (only if verification passed)'

if ($simulate) {
    Write-Warn 'skipped (simulation)'
} elseif (-not $restoreOk) {
    Write-Bad 'VERIFICATION FAILED - maintenance mode deliberately LEFT ON.'
    Write-Host '  A half-restored database that is open for business is worse than one that is visibly closed.' -ForegroundColor Red
    Write-Host '  Investigate, restore again, then release manually once you can honestly confirm the data.' -ForegroundColor Red
    if ($LogFile) { Stop-Transcript | Out-Null }
    exit 1
} else {
    $login2 = Invoke-RestMethod -Uri "$ApiBaseUrl/api/v1/auth/login" -Method Post -ContentType 'application/json' `
                                -Body (@{ username = $SuperAdminUsername; password = $plainPassword } | ConvertTo-Json)
    $headers2 = @{ Authorization = "Bearer $($login2.token)" }

    $releaseBody = @{
        verificationPassed = $true
        detail = "Restore rehearsal: schema complete, expected rows present. RTO $([math]::Round($rtoMinutes,2)) min."
    } | ConvertTo-Json

    Invoke-RestMethod -Uri "$ApiBaseUrl/api/v1/admin/maintenance/release" -Method Post -Headers $headers2 `
                      -Body $releaseBody -ContentType 'application/json' | Out-Null
    Write-Ok 'Maintenance mode released'
}

Write-Host ''
Write-Host 'Rehearsal complete.' -ForegroundColor Cyan
if ($LogFile) {
    Stop-Transcript | Out-Null
    Write-Host "Transcript written to $LogFile" -ForegroundColor Cyan
}
Write-Host ''
