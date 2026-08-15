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
    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

function Write-Step { param([string] $Text) Write-Host "`n=== $Text ===" -ForegroundColor Cyan }

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
