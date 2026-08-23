<#
.SYNOPSIS
    Inspect, collect from, and stop Claude Code workers. Companion to dispatch-worker.ps1.

.DESCRIPTION
    -Action list    Live and recent workers, one line each.
    -Action report  Collect one worker's structured report as a COMPACT digest.
    -Action stop    Kill a worker (or all of them).
    -Action doctor  Check every registered transport BEFORE dispatching to it.

    'report' is the context-bloat guard, enforced in tooling rather than in good
    intentions: it prints the parsed report fields and a path to the full log. It
    never prints the transcript. Even a careless orchestrator cannot flood its own
    context through this script.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('list', 'report', 'stop', 'doctor', 'sync')]
    [string] $Action,

    [string] $TaskId,
    [switch] $All,
    # report only: also print the failing-output tail the worker captured.
    [switch] $Verbose_Tail
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$runsRoot = Join-Path $repoRoot '.claude/fleet/runs'
New-Item -ItemType Directory -Force -Path $runsRoot | Out-Null

# Loaded once, at the top, because three actions need it now: doctor probes each transport,
# report scps a tty worker's file back off its machine, and stop reaches over to kill the
# remote tree. Loading it per branch is how one of them ends up referencing $registry in a
# branch that never populated it -- which StrictMode turns into a runtime error exactly when
# someone is trying to stop a runaway worker.
$registry = Get-Content -Raw (Join-Path $repoRoot '.claude/fleet/machines.json') | ConvertFrom-Json

# Returns the property value if present AND non-empty, else $null -- so callers can
# write `if (Get-Prop $o 'x')` without tripping over StrictMode on a missing property.
function Get-Prop($obj, [string] $name) {
    if ($null -eq $obj) { return $null }
    if ($obj -is [hashtable]) { return $(if ($obj.ContainsKey($name)) { $obj[$name] } else { $null }) }
    if ($obj.PSObject.Properties.Name -notcontains $name) { return $null }
    $val = $obj.$name
    if ($val -is [string] -and [string]::IsNullOrWhiteSpace($val)) { return $null }
    return $val
}

function Get-Runs {
    Get-ChildItem $runsRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        ForEach-Object {
            $h = Join-Path $_.FullName 'handle.json'
            if (Test-Path $h) {
                $handle = Get-Content -Raw $h | ConvertFrom-Json
                $hpid  = Get-Prop $handle 'pid'
                $alive = [bool]($hpid -and (Get-Process -Id $hpid -ErrorAction SilentlyContinue))
                # Every optional field goes through Get-Prop. StrictMode turns a missing
                # property into a terminating error, and this function enumerates ALL runs --
                # so one handle.json written by an older dispatcher (no remoteRunDir, no
                # timeoutMinutes) took down list, report AND stop for every run in the
                # directory, not just its own. A stale folder must never be able to brick
                # the tool you would use to clean it up.
                $started = Get-Prop $handle 'startedAt'
                [pscustomobject]@{
                    TaskId = Get-Prop $handle 'taskId'; Machine = Get-Prop $handle 'machine'
                    Mode   = Get-Prop $handle 'mode'
                    Model  = "$(Get-Prop $handle 'model')/$(Get-Prop $handle 'effort')"
                    Alive  = $alive; Pid = Get-Prop $handle 'pid'; RunDir = $_.FullName
                    Started = $started
                    Transport = Get-Prop $handle 'transport'
                    RemoteRunDir = Get-Prop $handle 'remoteRunDir'
                    Mins   = if ($started) { [int]((Get-Date) - [datetime]$started).TotalMinutes } else { $null }
                    Timeout = Get-Prop $handle 'timeoutMinutes'
                }
            }
        }
}

# Pulls the worker's structured report out of the claude -p envelope.
# --json-schema may hand back either an object or a JSON string in .result;
# both shapes are accepted, and an unparseable result degrades to raw text
# rather than throwing -- a crashed worker still has to be reportable.
function Get-Report($runDir) {
    # A tty worker writes its report to a FILE, because its final message goes to a terminal
    # the orchestrator must never read. Prefer that file; fall back to the headless envelope.
    $reportFile = Join-Path $runDir 'report.json'
    if (Test-Path $reportFile) {
        $text = Get-Content -Raw $reportFile
        if (-not [string]::IsNullOrWhiteSpace($text)) {
            # Workers sometimes fence it despite being told not to. Unwrap rather than
            # calling a good run unstructured over three backticks.
            $text = ($text -replace '(?s)^\s*```(?:json)?\s*', '') -replace '(?s)\s*```\s*$', ''
            try { return @{ report = ($text | ConvertFrom-Json); env = $null } }
            catch { return @{ _unparsed = $text } }
        }
    }
    $raw = Join-Path $runDir 'raw.json'
    if (-not (Test-Path $raw)) { return $null }
    $text = Get-Content -Raw $raw
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    try { $env = $text | ConvertFrom-Json } catch { return @{ _unparsed = $text } }
    $payload = if ($env.PSObject.Properties.Name -contains 'result') { $env.result } else { $env }
    if ($payload -is [string]) {
        try { $payload = $payload | ConvertFrom-Json } catch { return @{ _unparsed = $payload; _env = $env } }
    }
    return @{ report = $payload; env = $env }
}

switch ($Action) {

    'list' {
        $runs = @(Get-Runs)
        if (-not $runs) { Write-Host 'No workers dispatched yet.'; break }
        # Elapsed and OVERDUE are here because a tty worker can STALL rather than fail. It
        # sits at a permission or trust prompt with a flat CPU, holding a fleet slot, and
        # from here that is indistinguishable from working hard -- a headless worker would
        # have had the prompt auto-denied and died loudly. Elapsed-vs-timeout is the only
        # signal the orchestrator gets, so it goes in the default view, not behind a flag.
        $runs | Select-Object TaskId, Machine, Mode, Model, Alive, Pid,
                    @{n='Mins';e={$_.Mins}},
                    @{n='State';e={
                        if (-not $_.Alive) { 'finished' }
                        elseif ($_.Timeout -and $_.Mins -ge $_.Timeout) { 'OVERDUE' }
                        else { 'running' } }} |
            Format-Table -AutoSize
        $liveCount = @($runs | Where-Object Alive).Count
        $overdue   = @($runs | Where-Object { $_.Alive -and $_.Timeout -and $_.Mins -ge $_.Timeout })
        Write-Host "$liveCount live / $($runs.Count) total"
        if ($overdue) {
            Write-Host ''
            Write-Host ("OVERDUE: " + (($overdue | ForEach-Object { $_.TaskId }) -join ', ')) -ForegroundColor Yellow
            Write-Host '  A tty worker that is past its timeout is usually STALLED at a prompt, not busy.' -ForegroundColor Yellow
            Write-Host '  Look at its tab: a trust dialog or a permission prompt blocks it silently and' -ForegroundColor Yellow
            Write-Host '  holds its slot. Answer it there, or stop the run -- do not just wait longer.' -ForegroundColor Yellow
        }
    }

    'report' {
        $runs = @(Get-Runs)
        if ($TaskId) { $runs = @($runs | Where-Object { $_.TaskId -eq $TaskId }) }
        if (-not $runs) { Write-Host "No run found$(if($TaskId){" for $TaskId"})."; break }
        $run = $runs[0]

        # A remote tty worker wrote its report on ITS machine. Bring it back before reading.
        # This runs BEFORE the alive check on purpose -- see below.
        if ($run.RemoteRunDir -and -not (Test-Path (Join-Path $run.RunDir 'report.json'))) {
            $m = $registry.machines | Where-Object { $_.id -eq $run.Machine } | Select-Object -First 1
            if ($m -and $m.sshTarget) {
                & scp -q -o BatchMode=yes "$($m.sshTarget):$($run.RemoteRunDir)/report.json" `
                        (Join-Path $run.RunDir 'report.json') 2>&1 | Out-Null
            }
        }

        # In tty mode the process staying alive is NOT a sign of unfinished work. An
        # interactive session does not exit after its last message -- the tab stays open so
        # it can be read, by design. So "still running" would be true forever and the report
        # would never be collectable. The completion signal for a tty worker is THE REPORT
        # FILE EXISTING, not the process dying. Only treat alive-with-no-report as running.
        $haveReport = Test-Path (Join-Path $run.RunDir 'report.json')
        if ($run.Alive -and -not $haveReport) {
            Write-Host "RUNNING $($run.TaskId) on $($run.Machine) (pid $($run.Pid)) -- no report yet."
            break
        }
        if ($run.Alive -and $haveReport) {
            Write-Host "NOTE $($run.TaskId): report collected, but its tab is still open on $($run.Machine)." -ForegroundColor DarkGray
            Write-Host "     That is normal for tty mode. Close it, or: fleet.ps1 -Action stop -TaskId $($run.TaskId)" -ForegroundColor DarkGray
            Write-Host ''
        }

        $r = Get-Report $run.RunDir
        if (-not $r) {
            Write-Host "NO OUTPUT $($run.TaskId) -- worker produced nothing. Check $($run.RunDir)/stderr.log"
            break
        }
        if ($r.ContainsKey('_unparsed')) {
            Write-Host "UNSTRUCTURED $($run.TaskId) -- worker did not return the report schema."
            Write-Host ($r._unparsed.Substring(0, [Math]::Min(600, $r._unparsed.Length)))
            Write-Host "  full: $($run.RunDir)/raw.json"
            break
        }

        $rep = $r.report

        # The worker can be cut off before it ever writes a report -- budget, turn limit,
        # an API error. The envelope says so and the report does not, so read the envelope
        # FIRST. Rendering that case as [UNKNOWN] tells the orchestrator nothing, and
        # "no status" and "killed at the budget cap" demand completely different responses.
        if ((Get-Prop $r.env 'is_error') -and -not (Get-Prop $rep 'status')) {
            $sub = if (Get-Prop $r.env 'subtype') { $r.env.subtype } else { 'unknown_error' }
            $label = switch ($sub) {
                'error_max_budget_usd' { 'BUDGET EXCEEDED' }
                'error_max_turns'      { 'TURN LIMIT REACHED' }
                default                { "WORKER ERROR ($sub)" }
            }
            Write-Host ''
            Write-Host "[$label] $($run.TaskId)  ($($run.Machine), $($run.Model))" -ForegroundColor Yellow
            Write-Host "  The worker was cut off before it produced a report. Nothing it did is verified."
            Write-Host "  Re-dispatch with a higher limit, or split the card -- do NOT treat this as partial work."
            if (Get-Prop $r.env 'num_turns')      { Write-Host "  turns: $($r.env.num_turns)" }
            if (Get-Prop $r.env 'total_cost_usd') { Write-Host "  cost: `$$([Math]::Round($r.env.total_cost_usd, 3))" }
            Write-Host "  log: $($run.RunDir)"
            Write-Host ''
            break
        }

        $status = if (Get-Prop $rep 'status') { $rep.status.ToUpper() } else { 'UNKNOWN' }
        Write-Host ''
        Write-Host "[$status] $($run.TaskId)  ($($run.Machine), $($run.Model))"
        if (Get-Prop $rep 'summary') { Write-Host "  $($rep.summary)" }

        $v = Get-Prop $rep 'verification'
        if ($v) {
            Write-Host "  guardrails=$($v.guardrails)  tests=$($v.tests)"
            if (Get-Prop $v 'evidence') { Write-Host "  evidence: $($v.evidence)" }
            if ($Verbose_Tail -and (Get-Prop $v 'failureTail')) {
                Write-Host '  --- failure tail ---'; Write-Host $v.failureTail
            }
        }
        if (Get-Prop $rep 'commit')       { Write-Host "  commit: $($rep.commit)" }
        if (Get-Prop $rep 'filesChanged') {
            Write-Host "  files ($($rep.filesChanged.Count)): $(($rep.filesChanged | Select-Object -First 8) -join ', ')"
        }
        if (Get-Prop $rep 'blocker') {
            Write-Host "  BLOCKED on stop condition $($rep.blocker.stopCondition): $($rep.blocker.detail)" -ForegroundColor Yellow
        }
        if (Get-Prop $rep 'needsDecision') {
            Write-Host "  NEEDS DECISION: $($rep.needsDecision)" -ForegroundColor Yellow
        }
        if (Get-Prop $rep 'scopeCreepRefused') {
            Write-Host "  refused (out of scope): $($rep.scopeCreepRefused)"
        }
        if (Get-Prop $r.env 'total_cost_usd') {
            Write-Host "  cost: `$$([Math]::Round($r.env.total_cost_usd, 3))"
        }
        Write-Host "  log: $($run.RunDir)"
        Write-Host ''
    }

    'stop' {
        $runs = @(Get-Runs | Where-Object Alive)
        if ($TaskId) { $runs = @($runs | Where-Object { $_.TaskId -eq $TaskId }) }
        elseif (-not $All) { throw "Give -TaskId <id> or -All." }
        if (-not $runs) { Write-Host 'Nothing live to stop.'; break }
        foreach ($run in $runs) {
            # /T kills the tree, and that is the whole point. Stop-Process on the tracked pid
            # alone kills the shell and leaves the worker running -- for a tty dispatch the
            # claude process is a CHILD of the tracked pwsh and simply outlives it, so "STOPPED"
            # would be a lie and the run would keep spending budget unattended.
            & taskkill.exe /PID $run.Pid /T /F 2>&1 | Out-Null
            Stop-Process -Id $run.Pid -Force -ErrorAction SilentlyContinue
            Write-Host "STOPPED $($run.TaskId) (pid $($run.Pid), tree)"

            # Killing the local ssh client does NOT kill what it started. Measured, not
            # assumed: stopping SMOKE-03 took down the tab here and left claude running on
            # box3, still holding the session and still able to spend. Reach over and kill
            # the remote tree too, matched on this run's directory -- the run stamp is
            # unique, so nothing else can match it.
            if ($run.RemoteRunDir) {
                $m = $registry.machines | Where-Object { $_.id -eq $run.Machine } | Select-Object -First 1
                if ($m -and $m.sshTarget) {
                    $leaf = Split-Path -Leaf $run.RemoteRunDir
                    # Kill by the PID the runner left in its own run directory, then ASK WHAT
                    # SURVIVED and report on that. Two things this avoids, both of which
                    # produced a wrong answer here first:
                    #   The exit code is not usable -- taskkill returns non-zero for reasons
                    #   unrelated to whether the process died, so trusting it printed "may
                    #   still be running" over a worker that was already dead.
                    #   Matching the worker by command line is not usable either -- a query
                    #   mentioning the pattern contains the pattern, so it matches itself and
                    #   its cmd.exe wrapper, and reports a dead worker as alive.
                    # A stop that cries wolf is a stop nobody reads.
                    # Match the RUNNER's signature, not just the run directory. A query that
                    # mentions the run dir has the run dir in its own command line, so a
                    # match on that alone matches the probe itself and its cmd.exe wrapper --
                    # which reported a killed worker as still alive. run-worker-tty.ps1 plus
                    # the run stamp is unique to the worker. /T takes the claude child with
                    # it; claude's own command line carries the brief, not the runner path,
                    # so the tree flag is doing the work here, not the filter.
                    $pidFile = "$($run.RemoteRunDir)/worker.pid"
                    $cmd = "if (Test-Path '$pidFile') { " +
                           "`$wp = (Get-Content -Raw '$pidFile').Trim(); " +
                           "taskkill /PID `$wp /T /F 2>`$null | Out-Null; " +
                           "if (Get-Process -Id `$wp -ErrorAction SilentlyContinue) { 'ALIVE' } else { 'GONE' } " +
                           "} else { 'NOPID' }"
                    $res = (& ssh -o BatchMode=yes -o ConnectTimeout=10 $m.sshTarget "pwsh -NoProfile -Command `"$cmd`"" 2>$null |
                            Where-Object { $_ -match '^(ALIVE|GONE|NOPID)$' } | Select-Object -Last 1)
                    switch ($res) {
                        'GONE'  { Write-Host "  also stopped the remote worker on $($run.Machine)" }
                        'ALIVE' { Write-Warning "$($run.Machine) worker survived the kill. By hand: ssh $($m.sshTarget)" }
                        'NOPID' { Write-Warning ("No worker.pid in $leaf on $($run.Machine) -- it never started, or it " +
                                                 "predates the pid file. Check by hand: ssh $($m.sshTarget)") }
                        default { Write-Warning ("Could not confirm the remote worker on $($run.Machine) is stopped " +
                                                 "(box unreachable). Check: ssh $($m.sshTarget)") }
                    }
                }
            }
        }
    }

    'sync' {
        # Bring a worker box up to this box's HEAD, by git BUNDLE over scp.
        #
        # Why not just `git pull` over there: an SSH session cannot read the box's stored
        # GitHub credential. Git Credential Manager's default wincredman store is bound to
        # an INTERACTIVE desktop logon, so the credential that works when you sit at the
        # box, and worked for the old Remote Control transport, is unreadable to sshd's
        # network logon. It fails as `fatal: Unable to persist credentials` and then a
        # username prompt against /dev/tty that does not exist.
        #
        # Why not git-over-ssh to box3 directly: box3's default SSH shell does not strip the
        # single quotes git wraps the remote path in, so the path arrives literally quoted
        # and git says the repository does not exist. Changing that box's DefaultShell would
        # fix it and break every quoting pattern the dispatcher and doctor rely on.
        #
        # A bundle needs none of it: no credential, no remote shell parsing, no network path
        # to GitHub at all. It is one file, moved by the scp that is already proven, and the
        # worker box fast-forwards from it. A dirty tree over there refuses rather than
        # merging, because a worker's uncommitted work is not this command's to resolve.
        $targets = @($registry.machines | Where-Object { $_.transport -eq 'ssh' -and $_.enabled })
        if ($TaskId) { $targets = @($targets | Where-Object { $_.id -eq $TaskId }) }
        if (-not $targets) { Write-Host 'No enabled ssh machines to sync.'; break }

        $localHead = (& git -C $repoRoot rev-parse HEAD).Trim()
        $branch    = (& git -C $repoRoot rev-parse --abbrev-ref HEAD).Trim()
        $bundle    = Join-Path ([System.IO.Path]::GetTempPath()) "fleet-sync-$([guid]::NewGuid().ToString('N')).bundle"
        & git -C $repoRoot bundle create $bundle $branch 2>&1 | Out-Null
        if (-not (Test-Path $bundle)) { throw "sync: could not create a bundle from $branch." }

        try {
            foreach ($m in $targets) {
                Write-Host ''
                Write-Host "  [$($m.id)] syncing to $($localHead.Substring(0,7)) ($branch)"

                $dirty = (& ssh -o BatchMode=yes $m.sshTarget "git -C $($m.repo) status --porcelain" 2>$null)
                if ($dirty) {
                    Write-Warning ("$($m.id) has uncommitted changes. Not syncing -- commit or discard them " +
                                   "there first. Left alone: " + (($dirty | Select-Object -First 3) -join ' | '))
                    continue
                }

                $remoteBundle = "$($m.repo)/../fleet-sync.bundle"
                & scp -q -o BatchMode=yes $bundle "$($m.sshTarget):$remoteBundle" 2>&1 | Out-Null

                $cmd = "git -C $($m.repo) fetch '$remoteBundle' ${branch}:refs/remotes/origin/$branch --force; " +
                       "git -C $($m.repo) merge --ff-only refs/remotes/origin/$branch; " +
                       "Remove-Item '$remoteBundle' -Force -ErrorAction SilentlyContinue; " +
                       "git -C $($m.repo) rev-parse HEAD"
                $out = (& ssh -o BatchMode=yes $m.sshTarget "pwsh -NoProfile -Command `"$cmd`"" 2>&1)
                $newHead = (@($out) | Where-Object { $_ -match '^[0-9a-f]{40}$' } | Select-Object -Last 1)

                if ($newHead -and $newHead.Trim() -eq $localHead) {
                    Write-Host "    OK - $($m.id) is at $($localHead.Substring(0,7))" -ForegroundColor Green
                } else {
                    Write-Warning ("$($m.id) did NOT reach $($localHead.Substring(0,7)). It is at " +
                                   "'$newHead'. Dispatching there now would work a stale tree.")
                }
            }
        }
        finally { Remove-Item $bundle -Force -ErrorAction SilentlyContinue }
        Write-Host ''
    }

    'doctor' {
        Write-Host ''
        Write-Host 'Fleet transport check'
        Write-Host '---------------------'
        $claude = Get-Command claude -ErrorAction SilentlyContinue
        Write-Host ("  local claude    : " + $(if ($claude) { "OK ($((claude --version) 2>&1))" } else { 'MISSING' }))

        foreach ($m in $registry.machines) {
            Write-Host ''
            Write-Host "  [$($m.id)] transport=$($m.transport) enabled=$($m.enabled)"
            if (-not $m.enabled) { Write-Host "    NOT PROVISIONED - $($m.notes)"; continue }
            # Concurrency is a dispatch constraint now, not just a note, so doctor reports
            # it: a box already at its ceiling is as undispatchable as one with a dead
            # transport, and finding that out from a Fail mid-mission is worse than here.
            $cap  = if ($m.PSObject.Properties['maxConcurrent'] -and $m.maxConcurrent) { [int]$m.maxConcurrent }
                    else { [int]$registry.defaults.maxFleet }
            $here = @(Get-Runs | Where-Object { $_.Machine -eq $m.id -and $_.Alive }).Count
            $verdict = if ($here -ge $cap) { "AT CEILING" } else { "room for $($cap - $here) more" }
            Write-Host "    workers       : $here live / $cap ceiling ($verdict)"
            switch ($m.transport) {
                'local' {
                    Write-Host ("    repo          : " + $(if (Test-Path $m.repo) { 'OK' } else { "MISSING $($m.repo)" }))
                }
                'ssh' {
                    if (-not $m.sshTarget) { Write-Host '    sshTarget     : NOT SET'; continue }
                    $probe = & ssh -o BatchMode=yes -o ConnectTimeout=5 $m.sshTarget 'claude --version' 2>&1
                    $ok = $LASTEXITCODE -eq 0
                    # Keep the client's advisory chatter out of the verdict. A newer OpenSSH
                    # client warns about post-quantum key exchange against an older server; it
                    # is not an error and it is not this check's business, but spliced into the
                    # OK line it makes a healthy box look alarming on every single run.
                    $probe = (@($probe) | Where-Object {
                        $_ -notmatch 'post-quantum|store now, decrypt later|openssh\.com/pq|^\s*\*\*\s*$'
                    }) -join ' '
                    $probe = ($probe -replace '\s+', ' ').Trim()
                    if ($ok) { Write-Host "    ssh + claude  : OK ($probe)" }
                    else { Write-Host "    ssh + claude  : FAIL -> $probe" }
                }
                'rc' { Write-Host '    Remote Control: check with ListAgents in the orchestrator session, not here.' }
            }
        }
        Write-Host ''
    }
}
