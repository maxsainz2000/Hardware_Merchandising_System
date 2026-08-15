<#
.SYNOPSIS
    Layer 3 guardrail - Stop hook. One full sweep per turn.

.DESCRIPTION
    Runs scripts/check-no-csharp.ps1 once, when the turn ends. This catches everything the
    targeted layers deliberately skip - a credential pasted into a client .vb file, a C#
    file that arrived by some route other than the Write tool - at a cost of one run per
    turn instead of one per edit.

    Honours stop_hook_active: if Claude is already continuing because of this hook, exit 0
    rather than blocking again. A guardrail that can loop forever is a broken guardrail.

    Exit 0 = let the turn end. Exit 2 = block stopping and hand stderr back to Claude.

    Set MERCH_HOOK_TIMING=1 to print the elapsed time to stderr.
#>

$ErrorActionPreference = 'Stop'
$started = [System.Diagnostics.Stopwatch]::StartNew()

try {
    $raw = [Console]::In.ReadToEnd()

    if (-not [string]::IsNullOrWhiteSpace($raw)) {
        $payload = $raw | ConvertFrom-Json
        # Already looping once on this hook - do not block a second time.
        if ($payload.stop_hook_active -eq $true) { exit 0 }
    }

    $repoRoot = $env:CLAUDE_PROJECT_DIR
    if ([string]::IsNullOrWhiteSpace($repoRoot)) {
        $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    }

    $guardrails = Join-Path $repoRoot 'scripts/check-no-csharp.ps1'
    if (-not (Test-Path $guardrails)) { exit 0 }

    # *>&1 not 2>&1: check-no-csharp.ps1 reports through Write-Host, which writes to the
    # information stream (6), not stdout or stderr. With 2>&1 the capture came back EMPTY and
    # this hook blocked the turn without saying which guardrail failed. Verified in a live
    # session on 2026-08-15 (P0-07 Part A6) - see evidence/phase-0/p0-08-guardrail-proofs.txt
    # SECTION 8. Do not narrow this redirection.
    $output = & $guardrails -RepoRoot $repoRoot *>&1 | Out-String
    $failed = ($LASTEXITCODE -ne 0)

    $started.Stop()
    if ($env:MERCH_HOOK_TIMING -eq '1') {
        [Console]::Error.WriteLine("stop-guardrails.ps1 elapsed: $($started.ElapsedMilliseconds) ms")
    }

    if ($failed) {
        [Console]::Error.WriteLine(@"
Repository guardrails FAILED at end of turn:

$output
Do not finish the turn on a failing guardrail. Fix the cause, then re-run
scripts/check-no-csharp.ps1. If the failure is legitimate and cannot be fixed, say so
explicitly and report it as a blocker (CLAUDE.md section 7).
"@)
        exit 2
    }

    exit 0
}
catch {
    [Console]::Error.WriteLine("stop-guardrails.ps1 error: $($_.Exception.Message)")
    exit 0
}
