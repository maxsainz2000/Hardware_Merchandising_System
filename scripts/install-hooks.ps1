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

# Write the hook with LF line endings, unconditionally.
#
# This is not tidiness. .gitattributes marks *.ps1 as eol=crlf, so THIS FILE will be
# checked out with CRLF, and the here-string above therefore carries CRLF line
# endings. Set-Content would then write "#!/bin/sh`r`n", sh would look for an
# interpreter named "/bin/sh<CR>", and the hook would silently stop running - the
# exact failure .gitattributes exists to prevent, reintroduced by the fix for it.
#
# WriteAllText with an explicit LF-normalised body and no BOM is the only form that
# is correct regardless of how this file was checked out. Do not replace it with
# Set-Content.
$hookBody = $hookBody -replace "`r`n", "`n"
if (-not $hookBody.EndsWith("`n")) { $hookBody += "`n" }

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($hookPath, $hookBody, $utf8NoBom)

# Fail loudly rather than installing a hook that cannot run.
$written = [System.IO.File]::ReadAllBytes($hookPath)
$firstNewline = [Array]::IndexOf($written, [byte]10)
if ($firstNewline -lt 1 -or $written[$firstNewline - 1] -eq 13) {
    Write-Error "Refusing to install: the shebang line ended CRLF. sh would not run this hook."
    exit 1
}

# Best effort on Windows; required on Linux/macOS.
if ($IsLinux -or $IsMacOS) {
    & chmod +x $hookPath
}

Write-Host "Installed pre-commit hook at $hookPath" -ForegroundColor Green
Write-Host "Verify it with: git commit --allow-empty -m 'hook test'" -ForegroundColor Yellow
