<#
.SYNOPSIS
    Dispatch one Claude Code worker against exactly one task card.

.DESCRIPTION
    Spawns a worker on a registered machine, hands it a brief, and returns a COMPACT
    digest -- never a transcript. The orchestrator's context cost per worker is a few
    hundred tokens regardless of how long the worker ran or how much it read.

    Three transports, chosen by the machine's entry in .claude/fleet/machines.json:
      local  - this machine, headless (-Mode bg) or in a visible terminal (-Mode tty)
      ssh    - second machine, headless, structured JSON back over the pipe
      rc     - Remote Control; NOT dispatched here. This script refuses and tells the
               orchestrator to use SendMessage instead, because an RC session is driven
               by messages, not by a shell.

    The brief is piped in on stdin. That is deliberate: it means no prompt text ever
    passes through a command line, so quotes, newlines, backslashes and SQL fragments
    in a brief can never break the invocation or get mangled by a shell.

.EXAMPLE
    pwsh ./scripts/fleet/dispatch-worker.ps1 -TaskId P2-03 -BriefFile brief.md
    pwsh ./scripts/fleet/dispatch-worker.ps1 -TaskId P2-04 -Machine box2 -Model opus -Effort high -Wait
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9._-]+$')]
    [string] $TaskId,

    # The worker's instruction. Give one of these.
    [string] $Brief,
    [string] $BriefFile,

    [string] $Machine = 'box1',
    [ValidateSet('fable', 'opus', 'sonnet', 'haiku')]
    [string] $Model,
    [ValidateSet('low', 'medium', 'high', 'xhigh', 'max')]
    [string] $Effort,

    # bg  = headless, no window, JSON back (default -- cheapest and scriptable)
    # tty = a real visible terminal window, for work you want to watch
    [ValidateSet('bg', 'tty')]
    [string] $Mode = 'bg',

    [ValidateSet('acceptEdits', 'auto', 'manual', 'plan', 'dontAsk', 'bypassPermissions')]
    [string] $PermissionMode,

    [double] $MaxBudgetUsd,
    [int]    $TimeoutMinutes,

    # Block until the worker finishes and print its report.
    [switch] $Wait,

    # Print the exact command that would run, change nothing.
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot    = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$registryPath = Join-Path $repoRoot '.claude/fleet/machines.json'
$schemaPath   = Join-Path $repoRoot 'scripts/fleet/worker-report.schema.json'

# Refusals the orchestrator hits routinely print ONE line and exit. A bare `throw` makes
# PowerShell echo the offending source line plus a stack frame, which is noise in the one
# context this whole design is trying to keep clean.
function Fail([string] $msg) { Write-Host "REFUSED: $msg"; exit 1 }

if (-not (Test-Path $registryPath)) { Fail "Fleet registry not found: $registryPath" }
$registry = Get-Content -Raw $registryPath | ConvertFrom-Json
$defaults = $registry.defaults

$target = $registry.machines | Where-Object { $_.id -eq $Machine }
if (-not $target) {
    $known = ($registry.machines | ForEach-Object { $_.id }) -join ', '
    Fail "Unknown machine '$Machine'. Registered: $known"
}

# --- Refuse to dispatch to an unprovisioned or message-driven transport -------------
# Silence here would be the worst outcome: a task reported as dispatched that never ran.
if (-not $target.enabled) {
    Fail "Machine '$Machine' is registered but not enabled. $($target.notes)"
}
if ($target.transport -eq 'rc') {
    Fail ("Machine '$Machine' uses Remote Control. RC sessions are driven by messages, " +
           "not by a shell -- use ListAgents to find it, then SendMessage. This script " +
           "does not dispatch to RC on purpose.")
}

# --- Resolve settings: explicit flag > registry default -----------------------------
if (-not $Model)          { $Model          = $defaults.model }
if (-not $Effort)         { $Effort         = $defaults.effort }
if (-not $PermissionMode) { $PermissionMode = $defaults.permissionMode }
if (-not $PSBoundParameters.ContainsKey('MaxBudgetUsd'))  { $MaxBudgetUsd   = $defaults.maxBudgetUsd }
if (-not $PSBoundParameters.ContainsKey('TimeoutMinutes')){ $TimeoutMinutes = $defaults.timeoutMinutes }

if ($PermissionMode -eq 'bypassPermissions') {
    Write-Warning ("Dispatching with bypassPermissions. This worker can run any command " +
                   "without asking. Only correct when the user has explicitly approved it " +
                   "for this dispatch.")
}

# --- Fleet cap: the orchestrator's own guard against running away -------------------
$runsRoot = Join-Path $repoRoot '.claude/fleet/runs'
New-Item -ItemType Directory -Force -Path $runsRoot | Out-Null
$live = @(Get-ChildItem $runsRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object {
    $h = Join-Path $_.FullName 'handle.json'
    if (Test-Path $h) {
        $handle = Get-Content -Raw $h | ConvertFrom-Json
        if ($handle.pid -and (Get-Process -Id $handle.pid -ErrorAction SilentlyContinue)) { $handle }
    }
})
if ($live.Count -ge $defaults.maxFleet -and -not $DryRun) {
    $ids = ($live | ForEach-Object { $_.taskId }) -join ', '
    Fail ("Fleet cap reached ($($defaults.maxFleet) live: $ids). Collect or stop a worker " +
           "before dispatching another -- see fleet.ps1 -Action list.")
}

# --- Assemble the brief -------------------------------------------------------------
if ($BriefFile) {
    if (-not (Test-Path $BriefFile)) { Fail "Brief file not found: $BriefFile" }
    $briefBody = Get-Content -Raw $BriefFile
} elseif ($Brief) {
    $briefBody = $Brief
} else {
    Fail "Give -Brief or -BriefFile. A worker without a brief has no scope, and a worker without scope is the thing this whole design exists to prevent."
}

$stamp  = Get-Date -Format 'yyyyMMdd-HHmmss'
$runDir = Join-Path $runsRoot "$TaskId-$stamp"
New-Item -ItemType Directory -Force -Path $runDir | Out-Null

# The worker contract lives in .claude/skills/worker/SKILL.md, on disk, and is invoked
# by name. It is never inlined into the prompt -- that would pay for the same ~900
# tokens on every single dispatch.
$prompt = @"
/worker $TaskId

$briefBody
"@

$briefPath = Join-Path $runDir 'brief.md'
$rawPath   = Join-Path $runDir 'raw.json'
$errPath   = Join-Path $runDir 'stderr.log'
Set-Content -Path $briefPath -Value $prompt -Encoding utf8

# --- Build the runner argument list -------------------------------------------------
# These are plain scalars only. The brief goes over stdin and the JSON schema is read
# from disk by run-worker.ps1, so no quote-bearing value ever reaches a command line.
$runnerArgs = @(
    '-Model',          $Model
    '-Effort',         $Effort
    '-PermissionMode', $PermissionMode
    '-MaxBudgetUsd',   $MaxBudgetUsd
    '-Name',           "worker-$TaskId"
) -join ' '

$handle = [ordered]@{
    taskId    = $TaskId
    machine   = $Machine
    transport = $target.transport
    mode      = $Mode
    model     = $Model
    effort    = $Effort
    runDir    = $runDir
    startedAt = (Get-Date).ToString('o')
    pid       = $null
    timeoutMinutes = $TimeoutMinutes
}

# --- Dispatch -----------------------------------------------------------------------
switch ($target.transport) {

    'local' {
        $workDir = $target.repo
        if ($Mode -eq 'tty') {
            # A visible window. Interactive, so no -p and no structured report: this mode
            # is for work the user wants to watch, and it reports by being watched.
            #
            # The launch goes through a generated .ps1 rather than `-Command "a; b; c"`,
            # because wt.exe parses `;` as ITS OWN argument separator. A multi-statement
            # command handed to `wt new-tab` is silently chopped: the tab receives only the
            # first statement and the rest are executed as further wt sub-commands. That
            # produced a claude parented to WindowsTerminal instead of to the tab, with the
            # brief never reaching the clipboard. A -File launch has no delimiter to trip on
            # and gives the process a stable command line to identify it by.
            $launcher = Join-Path $runDir 'tty-launch.ps1'
            @"
Set-Location '$workDir'
Get-Content -Raw '$briefPath' | Set-Clipboard
Write-Host 'Brief copied to clipboard -- paste with Ctrl+V' -ForegroundColor Cyan
& claude --model $Model --effort $Effort --name worker-$TaskId
"@ | Set-Content -Path $launcher -Encoding utf8

            if ($DryRun) { Write-Host "DRYRUN wt.exe new-tab pwsh -NoExit -File $launcher"; return }
            $p = Start-Process 'wt.exe' -ArgumentList @(
                'new-tab', '--title', "worker-$TaskId", 'pwsh', '-NoProfile', '-NoExit', '-File', $launcher
            ) -PassThru
            # wt.exe hands the tab to the already-running WindowsTerminal and exits within a
            # second, so its pid is worthless: -Action list would call a live worker dead,
            # -Action stop would kill a pid already gone, and the fleet cap would stop
            # counting tty workers -- which is how a tty dispatch runs away unbounded.
            $handle.pid = $p.Id
            $deadline = (Get-Date).AddSeconds(20)
            while ((Get-Date) -lt $deadline) {
                $child = Get-CimInstance Win32_Process -Filter "Name='pwsh.exe'" -ErrorAction SilentlyContinue |
                         Where-Object { $_.CommandLine -like '*tty-launch.ps1*' -and
                                        $_.CommandLine -like "*$TaskId*" } |
                         Select-Object -First 1
                if ($child) { $handle.pid = [int]$child.ProcessId; break }
                Start-Sleep -Milliseconds 500
            }
            if ($handle.pid -eq $p.Id) {
                Write-Warning ("Could not resolve the tty worker's pwsh process. This run is " +
                               "UNTRACKED: list, stop and the fleet cap will all ignore it. " +
                               "Close the tab by hand when you are done with it.")
            }
        }
        else {
            $runner = Join-Path $workDir 'scripts/fleet/run-worker.ps1'
            $inner = "Get-Content -Raw '$briefPath' | " +
                     "& pwsh -NoProfile -File '$runner' $runnerArgs " +
                     "1> '$rawPath' 2> '$errPath'"
            if ($DryRun) { Write-Host "DRYRUN pwsh -NoProfile -Command $inner"; return }
            $p = Start-Process 'pwsh' -ArgumentList @('-NoProfile', '-Command', $inner) `
                                      -WindowStyle Hidden -PassThru
            $handle.pid = $p.Id
        }
    }

    'ssh' {
        if (-not $target.sshTarget) { Fail "Machine '$Machine' has transport ssh but no sshTarget set." }
        if (-not $target.repo)      { Fail "Machine '$Machine' has transport ssh but no repo path set." }
        # Prompt goes over stdin and the runner takes only scalars, so nothing in the
        # brief can break the remote shell regardless of what shell answers over there.
        $remote = "pwsh -NoProfile -File $($target.repo)/scripts/fleet/run-worker.ps1 $runnerArgs"
        $inner  = "Get-Content -Raw '$briefPath' | & ssh -o BatchMode=yes '$($target.sshTarget)' " +
                  "'$remote' 1> '$rawPath' 2> '$errPath'"
        if ($DryRun) { Write-Host "DRYRUN ssh $($target.sshTarget) -> $remote"; return }
        $p = Start-Process 'pwsh' -ArgumentList @('-NoProfile', '-Command', $inner) `
                                  -WindowStyle Hidden -PassThru
        $handle.pid = $p.Id
    }
}

$handle | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $runDir 'handle.json') -Encoding utf8

if (-not $Wait) {
    Write-Host "DISPATCHED $TaskId -> $Machine ($($target.transport)/$Mode) $Model/$Effort  pid=$($handle.pid)"
    Write-Host "  collect:  pwsh ./scripts/fleet/fleet.ps1 -Action report -TaskId $TaskId"
    return
}

# --- Wait, with a hard timeout ------------------------------------------------------
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
while ((Get-Process -Id $handle.pid -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 5
}
if (Get-Process -Id $handle.pid -ErrorAction SilentlyContinue) {
    # Tree kill, for the same reason -Action stop does it: the worker can be a child of the
    # tracked process, and killing only the parent leaves it running past its own timeout.
    & taskkill.exe /PID $handle.pid /T /F 2>&1 | Out-Null
    Stop-Process -Id $handle.pid -Force -ErrorAction SilentlyContinue
    Write-Host "TIMEOUT $TaskId after $TimeoutMinutes min -- worker killed. Logs: $runDir"
    exit 2
}

& (Join-Path $PSScriptRoot 'fleet.ps1') -Action report -TaskId $TaskId
