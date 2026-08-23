<#
.SYNOPSIS
    Runs one Claude Code worker as a VISIBLE, interactive session. Reads the brief from a
    file and hands it to claude as the opening prompt.

.DESCRIPTION
    The counterpart to run-worker.ps1. That one runs headless and returns a JSON envelope
    on stdout; this one runs the ordinary interactive TUI so the work can be watched.

    Two things make that possible without giving up the structured report:

    1. The brief is read from a FILE and passed as a single argv entry. PowerShell hands
       arguments to a native exe directly, so a brief containing quotes, newlines, dollar
       signs or SQL fragments cannot be re-parsed by anything. This is the same reason
       run-worker.ps1 takes its brief on stdin -- nothing quote-bearing on a command line.

    2. The worker's final message is NOT captured in an interactive session; the terminal
       scrollback is all there is, and the orchestrator must never read that. So the brief
       (composed by dispatch-worker.ps1) instructs the worker to WRITE its report object to
       -ReportPath as its last action. The orchestrator reads that file instead.

    Runs on box1 directly, or on a worker box under `ssh -t`, where the ConPTY gives the
    TUI a real terminal and the session renders in a tab on the orchestrator's screen.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $BriefFile,
    [Parameter(Mandatory = $true)] [string] $ReportPath,
    [string] $Model          = 'sonnet',
    [string] $Effort         = 'high',
    [string] $PermissionMode = 'acceptEdits',
    [string] $Name           = 'worker',

    # Where the worker must START. Passed explicitly because this file does not always run
    # from scripts/fleet: a remote dispatch copies it into the run directory, so deriving
    # the root from $PSScriptRoot would put the worker in .claude/fleet and it would look
    # for tasks.md, the spec and its own skill from the wrong place.
    [string] $RepoRoot
)

$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) { $RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }
if (-not (Test-Path (Join-Path $RepoRoot 'CLAUDE.md'))) {
    # Starting outside the checkout is not a small problem: no project CLAUDE.md loads, so
    # the worker does not know VB-only, the MariaDB 10.4 dialect limits, or that the ledgers
    # are append-only -- and /worker does not resolve. Refuse rather than run a session that
    # looks fine and is working without any of the project's rules.
    throw "run-worker-tty: '$RepoRoot' is not the repo root (no CLAUDE.md). Refusing to start a worker outside the checkout."
}
Set-Location $RepoRoot

if (-not (Test-Path $BriefFile)) { throw "run-worker-tty: brief not found at $BriefFile" }
$brief = Get-Content -Raw $BriefFile
if ([string]::IsNullOrWhiteSpace($brief)) { throw 'run-worker-tty: brief is empty.' }

# Make sure the worker can write the report even if nothing has created the folder yet --
# on a remote box the run directory belongs to that machine, not to the orchestrator.
$reportDir = Split-Path -Parent $ReportPath
if ($reportDir -and -not (Test-Path $reportDir)) {
    New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
}

# Leave our PID next to the report so the orchestrator can stop this worker precisely.
# The alternative -- having the orchestrator find us by matching our command line over ssh
# -- cannot be made to work: any query that mentions the pattern CONTAINS the pattern, so
# the search matches itself and its own cmd.exe wrapper. That reported a killed worker as
# still alive. A pid on disk has nothing to collide with.
if ($reportDir) { "$PID" | Set-Content -Path (Join-Path $reportDir 'worker.pid') -Encoding ascii }

Write-Host ''
Write-Host "  worker $Name  --  $Model/$Effort  --  $env:COMPUTERNAME" -ForegroundColor Cyan
Write-Host "  report goes to: $ReportPath" -ForegroundColor DarkGray
Write-Host "  watch it work. Ctrl-C or closing this tab stops the worker." -ForegroundColor DarkGray
Write-Host ''

& claude `
    --model $Model `
    --effort $Effort `
    --permission-mode $PermissionMode `
    --name $Name `
    $brief

$code = $LASTEXITCODE

if (Test-Path $ReportPath) {
    Write-Host ''
    Write-Host "  report written: $ReportPath" -ForegroundColor Green
} else {
    # Say so loudly rather than letting the tab close on a green-looking session. A worker
    # that never wrote its report has told the orchestrator nothing, and "the tab looked
    # fine" is not a result anyone can act on.
    Write-Host ''
    Write-Host "  NO REPORT WRITTEN at $ReportPath" -ForegroundColor Red
    Write-Host "  The orchestrator will see this run as producing nothing." -ForegroundColor Red
}

exit $code
