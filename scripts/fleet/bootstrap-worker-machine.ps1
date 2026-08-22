<#
.SYNOPSIS
    Turn a bare Windows machine into a fleet worker box. Run this ON the new machine.

.DESCRIPTION
    Checks the toolchain, clones or updates the repo, installs the git hooks, and runs
    the guardrails. Prints one PASS/FAIL table and exits non-zero if the machine is not
    ready -- so the orchestrator gets a verdict, not a wall of output.

    What it deliberately does NOT check: XAMPP and MariaDB. Only box1 runs the database
    (ADR-013 pins the identities to that host), so a secondary box missing XAMPP is
    correct, not broken. Cards that touch the database are placed on box1 by the
    orchestrator; a worker box never needs a connection string, which is the same reason
    ClientCommon may not reference Infrastructure.

.PARAMETER RepoUrl
    Git remote to clone from. Omit if the repo is already present at -RepoPath.

.PARAMETER RepoPath
    Where the checkout lives (or should live) on THIS machine.

.EXAMPLE
    pwsh ./bootstrap-worker-machine.ps1 -RepoUrl https://github.com/<you>/<repo>.git `
                                        -RepoPath C:/dev/Hardware_Merchandising_System
#>
[CmdletBinding()]
param(
    [string] $RepoUrl,
    [string] $RepoPath = "$HOME/Documents/Hardware_Merchandising_System",
    # Skip the guardrail run (useful if the clone is fresh and you only want the toolchain verdict).
    [switch] $SkipGuardrails
)

$ErrorActionPreference = 'Continue'
$results = [System.Collections.Generic.List[object]]::new()
$fatal   = $false

function Check([string] $name, [scriptblock] $probe, [switch] $Required) {
    $ok = $false; $detail = ''
    try {
        $detail = (& $probe) -join ' '
        $ok = -not [string]::IsNullOrWhiteSpace($detail)
    } catch { $detail = $_.Exception.Message }
    if (-not $ok -and $Required) { $script:fatal = $true }
    $script:results.Add([pscustomobject]@{
        Check  = $name
        Status = $(if ($ok) { 'PASS' } elseif ($Required) { 'FAIL' } else { 'skip' })
        Detail = $detail
    })
    return $ok
}

Write-Host ''
Write-Host "Bootstrapping worker box: $env:COMPUTERNAME"
Write-Host ('=' * 60)

# --- Toolchain ----------------------------------------------------------------------
Check 'PowerShell 7+' { if ($PSVersionTable.PSVersion.Major -ge 7) { $PSVersionTable.PSVersion.ToString() } } -Required | Out-Null
Check 'claude CLI'    { (claude --version) 2>&1 } -Required | Out-Null
Check 'git'           { (git --version)    2>&1 } -Required | Out-Null

# The SDK must match global.json EXACTLY. rollForward is 'disable', so a machine with
# only a newer SDK (10.0.400, say) does not build a bit differently -- it refuses to
# restore at all. Reporting "some 10.x present" would be worse than useless here: it
# would read as PASS on a machine that cannot build. Check for the exact pin.
$requiredSdk = '10.0.301'
$reg = Join-Path $PSScriptRoot '../../.claude/fleet/machines.json'
if (Test-Path $reg) {
    try {
        $d = Get-Content -Raw $reg | ConvertFrom-Json
        if ($d.defaults.requiredSdk) { $requiredSdk = $d.defaults.requiredSdk }
    } catch { }
}

$installedSdks = @(dotnet --list-sdks 2>&1 | ForEach-Object { ($_ -split ' ')[0] })
Check ".NET SDK $requiredSdk (exact)" {
    if ($installedSdks -contains $requiredSdk) { $requiredSdk }
} -Required | Out-Null

if ($installedSdks -and ($installedSdks -notcontains $requiredSdk)) {
    Write-Host ''
    Write-Host "  SDK MISMATCH: this machine has $($installedSdks -join ', ') but global.json"
    Write-Host "  pins $requiredSdk with rollForward=disable. Install $requiredSdk side-by-side"
    Write-Host "  (it coexists with newer SDKs; nothing is removed):"
    Write-Host "    winget install Microsoft.DotNet.SDK.10 --version $requiredSdk"
    Write-Host "  or download the $requiredSdk installer from dotnet.microsoft.com."
}

# --- Repo ---------------------------------------------------------------------------
if (Test-Path (Join-Path $RepoPath '.git')) {
    Write-Host "`nRepo present at $RepoPath -- fetching."
    git -C $RepoPath fetch --all --quiet 2>&1 | Out-Null
    $branch = (git -C $RepoPath rev-parse --abbrev-ref HEAD 2>&1)
    $head   = (git -C $RepoPath log --oneline -n 1 2>&1)
    $results.Add([pscustomobject]@{ Check = 'repo'; Status = 'PASS'; Detail = "$branch @ $head" })
}
elseif ($RepoUrl) {
    Write-Host "`nCloning $RepoUrl -> $RepoPath"
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $RepoPath) | Out-Null
    git clone $RepoUrl $RepoPath 2>&1 | Select-Object -Last 3
    if (Test-Path (Join-Path $RepoPath '.git')) {
        $results.Add([pscustomobject]@{ Check = 'repo'; Status = 'PASS'; Detail = "cloned to $RepoPath" })
    } else {
        $results.Add([pscustomobject]@{ Check = 'repo'; Status = 'FAIL'; Detail = 'clone failed' })
        $fatal = $true
    }
}
else {
    $results.Add([pscustomobject]@{ Check = 'repo'; Status = 'FAIL'; Detail = "not at $RepoPath and no -RepoUrl given" })
    $fatal = $true
}

# --- Hooks and guardrails -----------------------------------------------------------
# L4 (the git pre-commit hook) is per-clone and is NOT installed by cloning -- it lives
# in .git/hooks, which git does not transfer. A fresh worker box without it is exactly
# the hole CLAUDE.md section 11 describes, so install it here every time.
if (-not $fatal) {
    $installer = Join-Path $RepoPath 'scripts/install-hooks.ps1'
    if (Test-Path $installer) {
        & pwsh -NoProfile -File $installer 2>&1 | Out-Null
        $hookPath = Join-Path $RepoPath '.git/hooks/pre-commit'
        $results.Add([pscustomobject]@{
            Check  = 'git pre-commit hook (L4)'
            Status = $(if (Test-Path $hookPath) { 'PASS' } else { 'FAIL' })
            Detail = $hookPath
        })
        if (-not (Test-Path $hookPath)) { $fatal = $true }
    }

    if (-not $SkipGuardrails) {
        $g = Join-Path $RepoPath 'scripts/check-no-csharp.ps1'
        if (Test-Path $g) {
            Push-Location $RepoPath
            $out = & pwsh -NoProfile -File $g 2>&1
            $code = $LASTEXITCODE
            Pop-Location
            $results.Add([pscustomobject]@{
                Check  = 'guardrails G-A..G-D'
                Status = $(if ($code -eq 0) { 'PASS' } else { 'FAIL' })
                Detail = ($out | Select-Object -Last 1)
            })
            if ($code -ne 0) { $fatal = $true }
        }
    }
}

# --- Verdict ------------------------------------------------------------------------
Write-Host ''
$results | Format-Table -AutoSize
Write-Host ''
if ($fatal) {
    Write-Host "NOT READY - $env:COMPUTERNAME cannot take task cards yet. Fix the FAIL rows above."
    exit 1
}
Write-Host "READY - $env:COMPUTERNAME"
Write-Host "  hostname : $env:COMPUTERNAME"
Write-Host "  repo     : $RepoPath"
Write-Host ''
Write-Host "Register it on box1 in .claude/fleet/machines.json, then: fleet.ps1 -Action doctor"
exit 0
