<#
.SYNOPSIS
    Layer 2 guardrail - PostToolUse on Write|Edit. Targeted detection on project files only.

.DESCRIPTION
    Runs the full scripts/check-no-csharp.ps1 sweep, but ONLY when the file just written
    was a .vbproj.

    The reasoning: project files are what break G-B (a client project referencing
    Infrastructure or a database package) and G-D (Option Strict / PublishAot overridden).
    Those edits are rare, so a full scan is cheap. Running the same scan after every .vb
    or .xaml write would spawn PowerShell and re-walk the tree dozens of times per task -
    slow enough that the guardrail gets switched off, which is the failure mode this whole
    arrangement exists to avoid.

    Everything this layer skips is still caught by the Stop hook at the end of the turn.

    Exit 0 = fine. Exit 2 = failure, stderr is fed back to Claude.
#>

$ErrorActionPreference = 'Stop'

try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

    $payload = $raw | ConvertFrom-Json
    $filePath = $payload.tool_input.file_path
    if ([string]::IsNullOrWhiteSpace($filePath)) { exit 0 }

    $normalised = $filePath -replace '\\', '/'

    # Build output and non-source trees are never worth a scan.
    if ($normalised -match '(?i)/(bin|obj|\.vs|packages|TestResults|evidence|documentations)/') { exit 0 }

    # Only project files trigger the sweep. .vb, .xaml, .md, .sql and friends fall through
    # to the Stop hook.
    $extension = [System.IO.Path]::GetExtension($normalised)
    if (-not [string]::Equals($extension, '.vbproj', [StringComparison]::OrdinalIgnoreCase)) { exit 0 }

    $repoRoot = $env:CLAUDE_PROJECT_DIR
    if ([string]::IsNullOrWhiteSpace($repoRoot)) {
        $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    }

    $guardrails = Join-Path $repoRoot 'scripts/check-no-csharp.ps1'
    if (-not (Test-Path $guardrails)) {
        [Console]::Error.WriteLine("check-vbproj.ps1: guardrail script not found at '$guardrails'.")
        exit 0
    }

    $output = & $guardrails -RepoRoot $repoRoot 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        [Console]::Error.WriteLine(@"
Project-file guardrail FAILED after editing:
  $filePath

$output
A .vbproj edit broke one of the repository guardrails. Fix the project file rather than
weakening the check - these are the rules that keep a database credential off a client
laptop (CLAUDE.md sections 4 and 5).
"@)
        exit 2
    }

    exit 0
}
catch {
    [Console]::Error.WriteLine("check-vbproj.ps1 error: $($_.Exception.Message)")
    exit 0
}
