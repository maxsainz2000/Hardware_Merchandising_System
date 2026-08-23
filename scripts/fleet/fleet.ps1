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
    [ValidateSet('list', 'report', 'stop', 'doctor')]
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
                $alive = [bool]($handle.pid -and (Get-Process -Id $handle.pid -ErrorAction SilentlyContinue))
                [pscustomobject]@{
                    TaskId = $handle.taskId; Machine = $handle.machine; Mode = $handle.mode
                    Model  = "$($handle.model)/$($handle.effort)"
                    Alive  = $alive; Pid = $handle.pid; RunDir = $_.FullName
                    Started = $handle.startedAt
                }
            }
        }
}

# Pulls the worker's structured report out of the claude -p envelope.
# --json-schema may hand back either an object or a JSON string in .result;
# both shapes are accepted, and an unparseable result degrades to raw text
# rather than throwing -- a crashed worker still has to be reportable.
function Get-Report($runDir) {
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
        $runs | Select-Object TaskId, Machine, Mode, Model, Alive, Pid, Started |
            Format-Table -AutoSize
        $liveCount = @($runs | Where-Object Alive).Count
        Write-Host "$liveCount live / $($runs.Count) total"
    }

    'report' {
        $runs = @(Get-Runs)
        if ($TaskId) { $runs = @($runs | Where-Object { $_.TaskId -eq $TaskId }) }
        if (-not $runs) { Write-Host "No run found$(if($TaskId){" for $TaskId"})."; break }
        $run = $runs[0]

        if ($run.Alive) {
            Write-Host "RUNNING $($run.TaskId) on $($run.Machine) (pid $($run.Pid)) -- no report yet."
            break
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
            # would be a lie and the run would keep spending budget unattended. bg and ssh runs
            # happen to die anyway when their wrapper goes, but there is no reason to rely on
            # two different behaviours here.
            & taskkill.exe /PID $run.Pid /T /F 2>&1 | Out-Null
            Stop-Process -Id $run.Pid -Force -ErrorAction SilentlyContinue
            Write-Host "STOPPED $($run.TaskId) (pid $($run.Pid), tree)"
        }
    }

    'doctor' {
        $registry = Get-Content -Raw (Join-Path $repoRoot '.claude/fleet/machines.json') | ConvertFrom-Json
        Write-Host ''
        Write-Host 'Fleet transport check'
        Write-Host '---------------------'
        $claude = Get-Command claude -ErrorAction SilentlyContinue
        Write-Host ("  local claude    : " + $(if ($claude) { "OK ($((claude --version) 2>&1))" } else { 'MISSING' }))

        foreach ($m in $registry.machines) {
            Write-Host ''
            Write-Host "  [$($m.id)] transport=$($m.transport) enabled=$($m.enabled)"
            if (-not $m.enabled) { Write-Host "    NOT PROVISIONED - $($m.notes)"; continue }
            switch ($m.transport) {
                'local' {
                    Write-Host ("    repo          : " + $(if (Test-Path $m.repo) { 'OK' } else { "MISSING $($m.repo)" }))
                }
                'ssh' {
                    if (-not $m.sshTarget) { Write-Host '    sshTarget     : NOT SET'; continue }
                    $probe = & ssh -o BatchMode=yes -o ConnectTimeout=5 $m.sshTarget 'claude --version' 2>&1
                    if ($LASTEXITCODE -eq 0) { Write-Host "    ssh + claude  : OK ($probe)" }
                    else { Write-Host "    ssh + claude  : FAIL -> $probe" }
                }
                'rc' { Write-Host '    Remote Control: check with ListAgents in the orchestrator session, not here.' }
            }
        }
        Write-Host ''
    }
}
