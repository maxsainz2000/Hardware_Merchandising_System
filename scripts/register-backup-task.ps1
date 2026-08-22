#Requires -Version 5.1
<#
.SYNOPSIS
    Registers the nightly Merchandising backup in Windows Task Scheduler
    (P1-17, spec section 15 "Schedule").

.DESCRIPTION
    Run this ON THE API HOST, elevated, once per installation.

    WHY THE TASK RUNS AS SYSTEM AND NOT AS THE API SERVICE ACCOUNT.
    The API runs as the virtual account NT SERVICE\MerchandisingApi (P1-16).
    That account must NOT be able to read the backup directory: a dump
    contains every password hash in the system, and the process most exposed
    to the network should not be able to read it. Verified on this host —
    C:\MerchandisingBackups grants SYSTEM and Administrators full control and
    does not name MerchandisingApi at all, and inheritance is disabled there
    (P0-07), so the account cannot acquire access by accident later.

    SYSTEM is used because it already holds the rights the job needs, needs no
    password stored anywhere, and cannot log on interactively. A dedicated
    service account would mean a fourth credential to generate, protect, hand
    over and rotate, for no security gained over SYSTEM in a single-host
    academic prototype (ADR-012).

    WHY -RunLevel Highest IS NOT SET. SYSTEM is already the highest privilege
    on the machine; the flag is meaningless for it and setting it would
    suggest a privilege requirement that does not exist.

    WHAT THIS DOES NOT DO. It does not verify that a backup succeeds. Run
    the task once by hand afterwards, or run the utility directly, and check
    the exit code and the BackupLogs row. Registration proving nothing about
    execution is exactly the gap that lets a scheduled backup rot unnoticed.

.PARAMETER MaintenanceExe
    Full path to the published Merchandising.Maintenance.exe. Defaults to the
    Release publish output beside this repository.

.PARAMETER At
    Time of day to run, 24-hour "HH:mm". Defaults to 22:30 — spec section 15
    asks for "daily after the store's defined closing or low-activity window".

.PARAMETER TaskName
    Scheduled task name. Defaults to "Merchandising Nightly Backup".

.EXAMPLE
    pwsh ./scripts/register-backup-task.ps1 -MaintenanceExe 'C:\MerchandisingSystem\Merchandising.Maintenance.exe'

.EXAMPLE
    # Register, then actually prove it runs
    pwsh ./scripts/register-backup-task.ps1
    Start-ScheduledTask -TaskName 'Merchandising Nightly Backup'
#>
[CmdletBinding()]
param(
    [string] $MaintenanceExe,

    [ValidatePattern('^([01]\d|2[0-3]):[0-5]\d$')]
    [string] $At = '22:30',

    [string] $TaskName = 'Merchandising Nightly Backup'
)

$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principalCheck = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principalCheck.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Warning 'This session is not elevated. Registering a SYSTEM scheduled task requires it. No changes made.'
    exit 1
}

if (-not $MaintenanceExe) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $MaintenanceExe = Join-Path $repoRoot 'src\Merchandising.Maintenance\bin\Release\net10.0\Merchandising.Maintenance.exe'
}

if (-not (Test-Path $MaintenanceExe)) {
    Write-Error @"
Merchandising.Maintenance.exe not found at:
  $MaintenanceExe

Build or publish it first, or pass -MaintenanceExe with the real path. The
task is deliberately NOT registered against a path that does not exist: a
scheduled task pointing at a missing executable fails every night and looks
like a backup problem rather than a setup problem.
"@
    exit 1
}

$workingDirectory = Split-Path -Parent $MaintenanceExe

Write-Host ''
Write-Host 'Registering the nightly backup task' -ForegroundColor Cyan
Write-Host "  executable  $MaintenanceExe"
Write-Host "  runs at     $At daily"
Write-Host "  as          NT AUTHORITY\SYSTEM"
Write-Host ''

$action = New-ScheduledTaskAction -Execute $MaintenanceExe -Argument 'backup' -WorkingDirectory $workingDirectory
$trigger = New-ScheduledTaskTrigger -Daily -At $At
$taskPrincipal = New-ScheduledTaskPrincipal -UserId 'NT AUTHORITY\SYSTEM' -LogonType ServiceAccount

# StartWhenAvailable matters on a laptop: it is the difference between a
# missed night being caught up on the next boot and being silently skipped
# forever. A demo host is not a server and will not be awake at 22:30 every
# night.
$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -DontStopOnIdleEnd `
    -ExecutionTimeLimit (New-TimeSpan -Hours 1) `
    -MultipleInstances IgnoreNew

$existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Task '$TaskName' already exists - unregistering first so this script stays idempotent." -ForegroundColor Yellow
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $action `
    -Trigger $trigger `
    -Principal $taskPrincipal `
    -Settings $settings `
    -Description 'Nightly logical backup of the merchandising database (P1-17, spec section 15). Dumps as merch_backup via mysqldump, verifies the checksum, copies to the off-host volume by label, prunes to the retention count in SystemSettings, and records the outcome in BackupLogs.' | Out-Null

Write-Host "Registered '$TaskName'." -ForegroundColor Green
Write-Host ''
Write-Host 'Registration proves nothing about execution. Run it once now:' -ForegroundColor Cyan
Write-Host "    Start-ScheduledTask -TaskName '$TaskName'" -ForegroundColor White
Write-Host "    Get-ScheduledTaskInfo -TaskName '$TaskName' | Select-Object LastRunTime, LastTaskResult" -ForegroundColor White
Write-Host ''
Write-Host 'LastTaskResult 0 means a clean success. 1 means the backup ran and' -ForegroundColor DarkGray
Write-Host 'reported a problem - most often the off-host USB volume not being' -ForegroundColor DarkGray
Write-Host 'attached, which is a PARTIAL backup, not a failed one. Check the' -ForegroundColor DarkGray
Write-Host 'most recent BackupLogs row for which it was.' -ForegroundColor DarkGray
Write-Host ''
