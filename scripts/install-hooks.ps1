<#
.SYNOPSIS
    Installs the git pre-commit hook that runs the repository guardrails.

.DESCRIPTION
    Run this once after cloning. A guardrail you have to remember to run is a
    guardrail you will forget to run during finals week - the hook removes the
    remembering.

.EXAMPLE
    pwsh ./scripts/install-hooks.ps1
#>

[CmdletBinding()]
param(
    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$hooksDir = Join-Path $RepoRoot '.git/hooks'
if (-not (Test-Path $hooksDir)) {
    Write-Error "No .git/hooks directory found at '$hooksDir'. Run 'git init' first."
    exit 1
}

$hookPath = Join-Path $hooksDir 'pre-commit'

$hookBody = @'
#!/bin/sh
# Merchandising System - guardrails (installed by scripts/install-hooks.ps1)
# Bypass in a genuine emergency with: git commit --no-verify
# If you find yourself bypassing this more than once, the guardrail is telling
# you something real. Fix the cause, not the hook.

echo "Running repository guardrails..."

if command -v pwsh >/dev/null 2>&1; then
    pwsh -NoProfile -ExecutionPolicy Bypass -File "./scripts/check-no-csharp.ps1"
elif command -v powershell >/dev/null 2>&1; then
    powershell -NoProfile -ExecutionPolicy Bypass -File "./scripts/check-no-csharp.ps1"
else
    echo "WARNING: PowerShell not found on PATH - guardrails were NOT run."
    echo "Install PowerShell or run scripts/check-no-csharp.ps1 manually."
    exit 1
fi

status=$?
if [ $status -ne 0 ]; then
    echo ""
    echo "Commit blocked by repository guardrails. See CLAUDE.md."
    exit 1
fi

exit 0
'@

Set-Content -Path $hookPath -Value $hookBody -Encoding ASCII -NoNewline:$false

# Best effort on Windows; required on Linux/macOS.
if ($IsLinux -or $IsMacOS) {
    & chmod +x $hookPath
}

Write-Host "Installed pre-commit hook at $hookPath" -ForegroundColor Green
Write-Host "Verify it with: git commit --allow-empty -m 'hook test'" -ForegroundColor Yellow
