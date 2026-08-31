<#
.SYNOPSIS
    Runs the full verification pass: guardrails, then unit tests, then
    integration tests against the real pinned MariaDB.

.DESCRIPTION
    Integration tests deliberately run against the actual MariaDB instance
    supplied by XAMPP, never an in-memory substitute. The entire point of the
    Phase 1 gate is proving behaviour against the exact database version
    recorded in docs/adr.md - a fake provider would prove nothing.

.PARAMETER SkipIntegration
    Runs guardrails and unit tests only. Use during fast edit loops; never as
    the final check before marking a task done.

.EXAMPLE
    pwsh ./scripts/run-tests.ps1
    pwsh ./scripts/run-tests.ps1 -SkipIntegration
#>

[CmdletBinding()]
param(
    [switch] $SkipIntegration,
    [string] $Configuration = 'Debug',
    [string] $RepoRoot
)

$ErrorActionPreference = 'Stop'

# Same $PSScriptRoot-in-a-param-default defect that broke check-no-csharp.ps1
# on its first run under Windows PowerShell 5.1 at P1-01. This script is
# documented to run under pwsh, where the original form works - but the two
# scripts sit side by side and get copied from each other, so both are fixed.
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $scriptDir = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($scriptDir) -and $MyInvocation.MyCommand.Path) {
        $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    }
    if ([string]::IsNullOrWhiteSpace($scriptDir)) {
        Write-Host 'Cannot determine the location of this script. Pass -RepoRoot explicitly.' -ForegroundColor Red
        exit 1
    }
    $RepoRoot = Split-Path -Parent $scriptDir
}

function Write-Step { param([string] $Text) Write-Host "`n=== $Text ===" -ForegroundColor Cyan }

# P6-17 / ADR-029: MSB3030 ("Could not copy ... because it was not found") on
# Merchandising.Procurement/Inventory/POS/Maintenance's runtimeconfig.json is
# not a build race - it is Windows' 260-character MAX_PATH limit on the Win32
# file APIs MSBuild's Copy task still uses. Confirmed by controlled test at
# P6-17 (evidence/phase-6/p6-17-msb3030.txt): a repository root long enough
# that the longest of those four copies (106 characters, Procurement's own
# runtimeconfig.json landing in Merchandising.Tests.Unit's output) pushes the
# full path to 260 or more fails deterministically, on a machine where
# LongPathsEnabled is not 1 - which is the ordinary state of a fresh Windows
# install. This warns rather than blocks: the ceiling below is a measured-safe
# estimate from four known offenders, not a proof every shorter path is safe.
$msb3030LongestKnownSuffix = 106
$msb3030SafeRootLength = 259 - $msb3030LongestKnownSuffix

$longPathsEnabled = $false
try {
    $longPathsValue = Get-ItemPropertyValue -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem' -Name 'LongPathsEnabled' -ErrorAction Stop
    $longPathsEnabled = ($longPathsValue -eq 1)
} catch {
    $longPathsEnabled = $false
}

if (-not $longPathsEnabled -and $RepoRoot.Length -gt $msb3030SafeRootLength) {
    Write-Host "`n=== WARNING: this repository root may trigger MSB3030 ===" -ForegroundColor Yellow
    Write-Host "Root ($($RepoRoot.Length) chars): $RepoRoot" -ForegroundColor Yellow
    Write-Host 'Long path support is OFF on this machine (LongPathsEnabled != 1) and this root' -ForegroundColor Yellow
    Write-Host 'is long enough that a referenced project runtimeconfig.json copy can land at or' -ForegroundColor Yellow
    Write-Host "past Windows' 260-character MAX_PATH limit. If the build below fails with" -ForegroundColor Yellow
    Write-Host "MSB3030 'could not copy ... because it was not found' even though the file" -ForegroundColor Yellow
    Write-Host 'exists, this is why - see docs/adr.md ADR-029. Fix: build from a shorter path,' -ForegroundColor Yellow
    Write-Host 'outside AppData\Local\Temp.' -ForegroundColor Yellow
}

Push-Location $RepoRoot
try {
    Write-Step 'Guardrails'
    & (Join-Path $PSScriptRoot 'check-no-csharp.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Guardrails failed. Fix these before running tests.' }

    Write-Step 'Build'
    & dotnet build --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

    Write-Step 'Unit tests'
    $unitProject = 'src/tests/Merchandising.Tests.Unit/Merchandising.Tests.Unit.vbproj'
    if (Test-Path $unitProject) {
        & dotnet test $unitProject --configuration $Configuration --no-build --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Unit tests failed.' }
    } else {
        Write-Host 'Unit test project not created yet (task P1-19).' -ForegroundColor Yellow
    }

    if ($SkipIntegration) {
        Write-Host "`nSkipping integration tests (-SkipIntegration)." -ForegroundColor Yellow
        Write-Host 'Do not mark a task done on this result alone.' -ForegroundColor Yellow
    } else {
        Write-Step 'Integration tests (real MariaDB)'
        $integrationProject = 'src/tests/Merchandising.Tests.Integration/Merchandising.Tests.Integration.vbproj'
        if (Test-Path $integrationProject) {
            Write-Host 'Confirm MariaDB is running via the XAMPP control panel before this step.' -ForegroundColor Yellow
            & dotnet test $integrationProject --configuration $Configuration --no-build --nologo
            if ($LASTEXITCODE -ne 0) { throw 'Integration tests failed.' }
        } else {
            Write-Host 'Integration test project not created yet (task P1-19).' -ForegroundColor Yellow
        }
    }

    Write-Host "`nAll checks passed." -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "`n$($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    Pop-Location
}
