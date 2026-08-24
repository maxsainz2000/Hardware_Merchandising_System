<#
.SYNOPSIS
    Dispatch one Claude Code worker against one SCOPE -- a track of related cards.

.DESCRIPTION
    Spawns a worker on a registered machine, hands it a brief, and returns a COMPACT
    digest -- never a transcript. The orchestrator's context cost per worker is a few
    hundred tokens regardless of how long the worker ran or how much it read.

    Three transports, chosen by the machine's entry in .claude/fleet/machines.json:
      local  - this machine, headless (-Mode bg) or in a visible terminal (-Mode tty)
      ssh    - worker box. `ssh -t` for a visible tab here (-Mode tty, the default),
               or headless with a structured JSON envelope back (-Mode bg).
      rc     - Remote Control; NOT dispatched here. No machine uses rc now - it survives
               only as a manual fallback if box3's ssh transport ever breaks. This script
               refuses it and tells the orchestrator to use SendMessage instead, because
               an RC session is driven by messages, not by a shell.

    The brief is piped in on stdin. That is deliberate: it means no prompt text ever
    passes through a command line, so quotes, newlines, backslashes and SQL fragments
    in a brief can never break the invocation or get mangled by a shell.

.EXAMPLE
    pwsh ./scripts/fleet/dispatch-worker.ps1 -TaskId P2-03 -BriefFile brief.md
    pwsh ./scripts/fleet/dispatch-worker.ps1 -TaskId P2-04 -Machine box3 -Model opus -Effort high -Wait
#>
[CmdletBinding()]
param(
    # The identifier for this run: a run directory name, a window title, and the argument
    # /worker is invoked with. The fleet's unit of work is a SCOPE -- a track of related
    # cards -- so -Scope is the name that matches what is actually dispatched. -TaskId
    # stays as the parameter name because every run directory, handle and watchdog already
    # written on both boxes uses it, and renaming a key breaks collection of runs in flight.
    [Parameter(Mandatory = $true)]
    [Alias('Scope')]
    [ValidatePattern('^[A-Za-z0-9._-]+$')]
    [string] $TaskId,

    # The worker's instruction. Give one of these.
    [string] $Brief,
    [string] $BriefFile,

    [string] $Machine = 'box1',
    # haiku stays selectable for a deliberate choice, but it is NOT the default and should
    # not be reintroduced as one: effort is unsupported on Haiku 4.5, so -Effort alongside
    # -Model haiku reads as tuning while doing nothing; its context is 200K against 1M for
    # Opus 5 and Sonnet 5; and Sonnet 5 is 2-3x its price, not 10x. See /orchestrate section 2.
    [ValidateSet('fable', 'opus', 'sonnet', 'haiku')]
    [string] $Model,
    [ValidateSet('low', 'medium', 'high', 'xhigh', 'max')]
    [string] $Effort,

    # tty = a real visible terminal tab running the ordinary interactive session (DEFAULT).
    #       Remote workers open a tab HERE over `ssh -t`, so a box3 worker is watched from
    #       box1's screen. The report is written to a file rather than returned on stdout.
    # bg  = headless, no window, JSON envelope back. Cheaper and scriptable, but you see
    #       nothing until it finishes.
    [ValidateSet('tty', 'bg')]
    [string] $Mode = 'tty',

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

# bypassPermissions is the registry default here, approved by the user on 2026-08-23 after
# acceptEdits and auto were both measured stalling a worker on its first shell command. So
# it gets no warning: one that fires on every single dispatch is noise, and noise is how a
# real warning stops being read. The mode is printed on the DISPATCHED line instead, where
# it is visible without crying wolf. What DOES deserve a warning is the reverse - a milder
# mode, which on this fleet means a worker that will stall rather than one that is safer.
if ($PermissionMode -in @('acceptEdits','auto','manual')) {
    Write-Warning ("Dispatching with -PermissionMode $PermissionMode. On this fleet that " +
                   "STALLS the worker at its first Bash/PowerShell prompt -- measured on " +
                   "box3, SMOKE-03 and SMOKE-04. Only do this in tty mode with someone " +
                   "watching the tab.")
}

# --- Fleet cap: the orchestrator's own guard against running away -------------------
$runsRoot = Join-Path $repoRoot '.claude/fleet/runs'
New-Item -ItemType Directory -Force -Path $runsRoot | Out-Null
# Liveness is IDENTITY, not existence. Windows recycles pids within hours, and a recycled
# one made a finished run look live forever -- which here means a permanently consumed slot
# against a ceiling of 2, so box1 would refuse every dispatch for a worker that had exited.
# Measured 2026-08-24: a dead worker's pid came back as an svchost.
function Test-LiveHandle($handle) {
    if (-not $handle.PSObject.Properties['pid'] -or -not $handle.pid) { return $false }
    $p = Get-Process -Id $handle.pid -ErrorAction SilentlyContinue
    if (-not $p) { return $false }
    if ($handle.PSObject.Properties['pidStartedAt'] -and $handle.pidStartedAt) {
        try { return ([Math]::Abs(($p.StartTime - [datetime]$handle.pidStartedAt).TotalSeconds) -le 5) }
        catch { return $false }
    }
    return ($p.ProcessName -in @('pwsh', 'powershell'))
}

$live = @(Get-ChildItem $runsRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object {
    $h = Join-Path $_.FullName 'handle.json'
    if (Test-Path $h) {
        $handle = Get-Content -Raw $h | ConvertFrom-Json
        if (Test-LiveHandle $handle) { $handle }
    }
})
if ($live.Count -ge $defaults.maxFleet -and -not $DryRun) {
    $ids = ($live | ForEach-Object { $_.taskId }) -join ', '
    Fail ("Fleet cap reached ($($defaults.maxFleet) live: $ids). Collect or stop a worker " +
           "before dispatching another -- see fleet.ps1 -Action list.")
}

# Per-machine ceiling. The global cap alone would let every worker in the fleet pile onto
# one box -- which on box3 means four TUIs, four SDK restores and four builds competing for
# 57.8 GB and one CPU, and on box1 means workers competing with the orchestrator session and
# with Visual Studio. maxConcurrent is a CEILING; how many actually run is the
# orchestrator's decision per mission, and this only stops that decision going wrong.
$machineCap = if ($target.PSObject.Properties['maxConcurrent'] -and $target.maxConcurrent) {
                  [int]$target.maxConcurrent
              } else { [int]$defaults.maxFleet }
# --- The database is an exclusive resource -------------------------------------------
# There is exactly one MariaDB, on box1. Two scopes can touch no common FILE and still
# collide through it: one applies a migration while the other's integration tests are
# reading the tables. The failures land in whichever worker read second and look exactly
# like code bugs, which is the worst possible way to lose an afternoon.
#
# So it is enforced here rather than advised in a skill. next-scope.ps1 knows which scopes
# need the database; a scope it does not recognise (a probe, a smoke test) is not a database
# holder and is never blocked by this.
$needsDb = $false
try {
    $lookup = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'next-scope.ps1') -ScopeId $TaskId 2>$null
    $rec    = ((@($lookup) | ForEach-Object { "$_" }) -join "`n") | ConvertFrom-Json
    $needsDb = [bool]($rec.PSObject.Properties.Name -contains 'needsDb' -and $rec.needsDb)
} catch { $needsDb = $false }

if ($needsDb -and -not $DryRun) {
    $dbHolders = @($live | Where-Object {
        $_.PSObject.Properties.Name -contains 'needsDatabase' -and $_.needsDatabase
    })
    if ($dbHolders) {
        $ids = ($dbHolders | ForEach-Object { $_.taskId }) -join ', '
        Fail ("$TaskId needs the database and it is already held by: $ids. There is one MariaDB on " +
              "box1, so two database scopes cannot run at once -- their migrations and integration " +
              "tests would interleave. Collect that worker first, or dispatch a scope that needs no " +
              "database (fleet.ps1 -Action next shows which).")
    }
}

$liveHere = @($live | Where-Object { $_.machine -eq $Machine })
if ($liveHere.Count -ge $machineCap -and -not $DryRun) {
    $ids = ($liveHere | ForEach-Object { $_.taskId }) -join ', '
    Fail ("$Machine is at its concurrency ceiling ($machineCap live: $ids). Place this card " +
          "on another box, or collect one of those first -- see fleet.ps1 -Action list.")
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

# In tty mode the worker's final message goes to a terminal nobody will parse, so the
# report has to land on disk. On a remote box that path is on THAT machine; the report is
# fetched back at collection time.
$remoteRunDir = if ($target.transport -eq 'ssh') { "$($target.repo)/.claude/fleet/runs/$TaskId-$stamp" } else { $null }
$workerReportPath = if ($Mode -ne 'tty') { $null }
                    elseif ($remoteRunDir)  { "$remoteRunDir/report.json" }
                    else                    { (Join-Path $runDir 'report.json').Replace('\','/') }

if ($Mode -eq 'tty') {
    $prompt += @"

---
REPORTING -- read this, it changes how you finish.

You are running in a VISIBLE terminal. Your final message is not captured anywhere the
orchestrator can read, and it will never read this scrollback. So as your LAST action,
write your report object as JSON to exactly this path:

  $workerReportPath

Raw JSON only -- no markdown fence, no commentary around it. Create the folder if it does
not exist. Everything else in the worker contract is unchanged: the same schema, the same
honesty rules, and ``status: done`` still requires guardrails AND tests to have passed.

If you do not write that file, the orchestrator sees this run as having produced nothing,
no matter how well it went on screen.
"@
}

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
    pidStartedAt   = $null
    # Recorded so the NEXT dispatch can see that the one MariaDB is spoken for. The check
    # above reads this off every live run's handle.
    needsDatabase = $needsDb
}

# --- Dispatch -----------------------------------------------------------------------
$handle.reportPath = $workerReportPath
$handle.remoteRunDir = $remoteRunDir

if ($Mode -eq 'tty') {
    # One visible tab per worker, on THIS machine, whether the worker runs here or on a
    # worker box. A remote worker is reached with `ssh -t`, whose ConPTY gives the TUI a
    # real terminal -- so box3's session renders in a tab on box1's screen rather than on
    # a monitor nobody is sitting at.
    if ($target.transport -eq 'ssh') {
        if (-not $target.sshTarget) { Fail "Machine '$Machine' has transport ssh but no sshTarget set." }
        if (-not $target.repo)      { Fail "Machine '$Machine' has transport ssh but no repo path set." }

        $remoteBrief  = "$remoteRunDir/brief.md"
        # The runner is carried WITH the dispatch rather than assumed present. A worker box
        # only gets repo changes when it pulls, so a checkout that is one commit behind this
        # one has no run-worker-tty.ps1 at all -- and the failure surfaces as pwsh saying a
        # file does not exist, inside a tab that closes, which reads as a transport fault.
        # Copying it makes the transport self-carrying: the dispatcher is never older than
        # the runner it invokes.
        $remoteRunner = "$remoteRunDir/run-worker-tty.ps1"
        if (-not $DryRun) {
            # Create the run dir over there, then copy the brief with scp. The brief never
            # goes on a command line -- it is the one value that can contain anything.
            & ssh -o BatchMode=yes $target.sshTarget "pwsh -NoProfile -Command New-Item -ItemType Directory -Force -Path $remoteRunDir" 2>&1 | Out-Null
            & scp -q -o BatchMode=yes $briefPath "$($target.sshTarget):$remoteBrief" 2>&1 | Out-Null
            if ($LASTEXITCODE -ne 0) { Fail "Could not copy the brief to $Machine. Check: ssh $($target.sshTarget) whoami" }
            & scp -q -o BatchMode=yes (Join-Path $repoRoot 'scripts/fleet/run-worker-tty.ps1') "$($target.sshTarget):$remoteRunner" 2>&1 | Out-Null
            if ($LASTEXITCODE -ne 0) { Fail "Could not copy the tty runner to $Machine." }

            # The runner travels; the PROJECT does not. Say so plainly when the worker box
            # is on a different commit, because that decides whether its report means
            # anything -- a card worked against a stale tree can pass its own tests and
            # still not apply here. A warning, not a Fail: dispatching against an older
            # tree is occasionally deliberate, and only the orchestrator knows which.
            $localHead  = (& git -C $repoRoot rev-parse HEAD 2>$null)
            $remoteHead = (& ssh -o BatchMode=yes $target.sshTarget "git -C $($target.repo) rev-parse HEAD" 2>$null)
            if ($localHead -and $remoteHead -and $localHead.Trim() -ne $remoteHead.Trim()) {
                Write-Warning ("$Machine is on a different commit ($($remoteHead.Trim().Substring(0,7))) " +
                               "than this box ($($localHead.Trim().Substring(0,7))). The worker will read " +
                               "the card, the spec and CLAUDE.md as they are THERE. Push and have it pull " +
                               "before dispatching real work.")
            }
        }
        $inner = "ssh -t $($target.sshTarget) pwsh -NoProfile -File $remoteRunner " +
                 "-RepoRoot $($target.repo) -BriefFile $remoteBrief -ReportPath $workerReportPath " +
                 "-Model $Model -Effort $Effort -PermissionMode $PermissionMode -Name worker-$TaskId"
    }
    else {
        $localRunner = (Join-Path $repoRoot 'scripts/fleet/run-worker-tty.ps1').Replace('\','/')
        $localBrief  = $briefPath.Replace('\','/')
        $inner = "pwsh -NoProfile -File $localRunner -RepoRoot '$($target.repo)' -BriefFile $localBrief " +
                 "-ReportPath $workerReportPath -Model $Model -Effort $Effort " +
                 "-PermissionMode $PermissionMode -Name worker-$TaskId"
    }

    # The tab launches a generated .ps1, never `-Command "a; b; c"`: wt.exe parses `;` as
    # ITS OWN argument separator, so a multi-statement command is silently chopped and the
    # tail runs as further wt sub-commands instead of inside the tab.
    $launcher = Join-Path $runDir 'tty-launch.ps1'
    $pidFile  = Join-Path $runDir 'worker.pid'
    @"
`$Host.UI.RawUI.WindowTitle = 'worker-$TaskId'
Set-Location '$repoRoot'
# First action, before anything can go wrong: state which process this tab is. Resolving it
# from the outside by scanning Win32_Process for a matching command line is a race, and when
# it lost the run went UNTRACKED -- list, stop and the fleet cap all ignored it, which is
# exactly how a tty dispatch runs away unbounded. A pid the launcher writes itself cannot
# lose that race.
Set-Content -Path '$pidFile' -Value `$PID -Encoding ascii
$inner
Write-Host ''
Write-Host 'worker-$TaskId finished. This tab stays open so you can read it.' -ForegroundColor DarkGray
"@ | Set-Content -Path $launcher -Encoding utf8

    if ($DryRun) { Write-Host "DRYRUN wt.exe new-tab pwsh -NoExit -File $launcher`n  inner: $inner"; return }

    $p = Start-Process 'wt.exe' -ArgumentList @(
        'new-tab', '--title', "worker-$TaskId", 'pwsh', '-NoProfile', '-NoExit', '-File', $launcher
    ) -PassThru
    # wt.exe hands the tab to the already-running WindowsTerminal and exits within a second,
    # so its pid is worthless: -Action list would call a live worker dead, -Action stop would
    # kill a pid already gone, and the fleet cap would stop counting tty workers -- which is
    # how a tty dispatch runs away unbounded. Resolve the pwsh actually running the launcher.
    $handle.pid = $p.Id
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        # The pid the launcher wrote about itself is authoritative. Only fall back to
        # scanning command lines if that file has not appeared yet.
        if (Test-Path $pidFile) {
            $written = (Get-Content -Raw $pidFile).Trim()
            if ($written -match '^\d+$') { $handle.pid = [int]$written; break }
        }
        $child = Get-CimInstance Win32_Process -Filter "Name='pwsh.exe'" -ErrorAction SilentlyContinue |
                 Where-Object { $_.CommandLine -like '*tty-launch.ps1*' -and
                                $_.CommandLine -like "*$TaskId*" } |
                 Select-Object -First 1
        if ($child) { $handle.pid = [int]$child.ProcessId; break }
        Start-Sleep -Milliseconds 500
    }
    if ($handle.pid -eq $p.Id) {
        # Both routes failed, which now means the tab never started rather than that a scan
        # lost a race. Refuse rather than record a run nothing can see: an untracked run is
        # worse than no run, because the cap stops counting it and a runaway is unbounded.
        Fail ("Could not determine the tty worker's process for $TaskId (no $pidFile and no matching " +
              "pwsh). The tab may not have started. Nothing is tracked for this run -- check the " +
              "Windows Terminal tab, close it if it exists, and dispatch again.")
    }
}
else {
    switch ($target.transport) {

        'local' {
            $workDir = $target.repo
            $runner  = Join-Path $workDir 'scripts/fleet/run-worker.ps1'
            $inner = "Get-Content -Raw '$briefPath' | " +
                     "& pwsh -NoProfile -File '$runner' $runnerArgs " +
                     "1> '$rawPath' 2> '$errPath'"
            if ($DryRun) { Write-Host "DRYRUN pwsh -NoProfile -Command $inner"; return }
            $p = Start-Process 'pwsh' -ArgumentList @('-NoProfile', '-Command', $inner) `
                                      -WindowStyle Hidden -PassThru
            $handle.pid = $p.Id
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
}

# Stamp the process's start time next to its pid. This is what makes a later liveness test
# an identity test rather than an existence test -- without it, a recycled pid keeps a
# finished run "live" forever, holding a slot and making `stop` aim at a stranger.
# Recorded here, once, for every launch path, so no path can forget it.
try {
    $proc = Get-Process -Id $handle.pid -ErrorAction Stop
    $handle.pidStartedAt = $proc.StartTime.ToString('o')
} catch {
    # A worker that finished before we could stamp it is not an error; it is just already
    # done, and an unstamped handle degrades to the name check rather than to a wrong answer.
    $handle.pidStartedAt = $null
}

$handle | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $runDir 'handle.json') -Encoding utf8

if (-not $Wait) {
    # A watchdog, because an async dispatch had NO enforced ceiling of any kind: the hard
    # timeout below only ran under -Wait, and --max-budget-usd only works with --print, so
    # it does nothing in the tty mode that is now the default. A runaway worker was bounded
    # by nothing but somebody noticing.
    #
    # This is deliberately the ONLY kind of limit the fleet uses: an external one the worker
    # cannot perceive. Telling a model it is on a budget changes how it works -- it paces,
    # truncates and settles early -- so efficiency here comes from architecture and from
    # placement, never from an instruction to be frugal. Nothing about this watchdog reaches
    # a prompt.
    #
    # It kills only a worker that is past its deadline AND has produced no report, so a tty
    # worker whose tab is simply still open after finishing is left alone to be read.
    $watch = @"
Start-Sleep -Seconds $($TimeoutMinutes * 60)
`$fleet = '$(Join-Path $repoRoot 'scripts/fleet/fleet.ps1')'
& `$fleet -Action report -TaskId '$TaskId' *> `$null
if (Test-Path '$(Join-Path $runDir 'report.json')') { exit 0 }
if (Get-Process -Id $($handle.pid) -ErrorAction SilentlyContinue) {
    & `$fleet -Action stop -TaskId '$TaskId' *> '$(Join-Path $runDir 'watchdog.log')'
}
"@
    $watchFile = Join-Path $runDir 'watchdog.ps1'
    $watch | Set-Content -Path $watchFile -Encoding utf8
    Start-Process 'pwsh' -ArgumentList @('-NoProfile', '-File', $watchFile) -WindowStyle Hidden | Out-Null

    Write-Host "DISPATCHED $TaskId -> $Machine ($($target.transport)/$Mode) $Model/$Effort  perms=$PermissionMode  pid=$($handle.pid)"
    Write-Host "  ceiling:  $TimeoutMinutes min, then killed if it has produced no report"
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
