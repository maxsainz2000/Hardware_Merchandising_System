<#
.SYNOPSIS
    Read tasks.md and report the next open SCOPE -- a track, and the ordered cards inside it.

.DESCRIPTION
    The fleet's unit of work is a SCOPE, not a card. tasks.md already groups cards into
    `## Track X` sections, and that grouping is not decoration: cards inside a track are
    normally sequential (P2-02 needs P2-01's migration; P2-03 tests P2-02's policies) while
    tracks are the things that are actually independent of each other. Dispatching a card
    at a time therefore splits work that has to stay together and pays a worker startup for
    each piece; dispatching a track hands one worker a boundary that matches the way the
    work really decomposes.

    This script exists so that "which scope is next" is a MECHANICAL answer rather than a
    remembered instruction. It reads:

      - the track headings          -> the scopes
      - the card status emoji        -> what is still open
      - each card's **Files:** line  -> the scope's file set, and therefore independence

    Two scopes may run in parallel only if their file sets are disjoint. That used to be an
    orchestrator's judgement call against a file it had to read in full; here it is computed.

    IT PROPOSES, IT DOES NOT DECIDE. Placement and count remain the orchestrator's call --
    see /orchestrate section 2. In particular the box recommendation is a heuristic over
    file paths and card text, and a card that needs the database while saying so only in
    prose will be recommended wrongly. Read it as a starting point, not a verdict.

.PARAMETER TasksFile
    Defaults to tasks.md at the repo root.

.PARAMETER Max
    How many open scopes to describe. Default 3 -- enough for a wave, short enough that
    calling this never costs the orchestrator much context.

.PARAMETER Json
    Emit the structured object instead of the human digest.

.EXAMPLE
    pwsh ./scripts/fleet/next-scope.ps1
    pwsh ./scripts/fleet/fleet.ps1 -Action next
#>
[CmdletBinding()]
param(
    [string] $TasksFile,
    [int]    $Max = 3,
    [switch] $Json
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $TasksFile) { $TasksFile = Join-Path $repoRoot 'tasks.md' }
if (-not (Test-Path $TasksFile)) { Write-Host "REFUSED: tasks.md not found at $TasksFile"; exit 1 }

$lines = Get-Content -LiteralPath $TasksFile -Encoding utf8

# ---------------------------------------------------------------------------------------
# Parse. A scope is a `## Track X` heading, or the carried-forward section, which is a real
# scope with a real owner even though it has no track heading.
# ---------------------------------------------------------------------------------------

$scopes  = [System.Collections.Generic.List[object]]::new()
$current = $null
$card    = $null

function Close-Card {
    if ($script:card -and $script:current) { $script:current.cards.Add($script:card) | Out-Null }
    $script:card = $null
}

function New-Scope([string] $Name, [string] $Kind) {
    Close-Card
    $s = [pscustomobject]@{
        name = $Name; kind = $Kind
        cards = [System.Collections.Generic.List[object]]::new()
    }
    $script:scopes.Add($s) | Out-Null
    $script:current = $s
}

foreach ($line in $lines) {
    if ($line -match '^#\s+Carried forward') { New-Scope 'Carried forward' 'carried'; continue }
    if ($line -match '^##\s+Track\s+(.+)$')  { New-Scope "Track $($Matches[1].Trim())" 'track'; continue }
    # A `# Phase N` heading ends the carried-forward scope without opening one of its own;
    # its cards live in the tracks that follow.
    if ($line -match '^#\s+Phase\s')         { Close-Card; $current = $null; continue }

    if ($line -match '^###\s+(\S+)\s+(P\d+-\d+)\s+·\s*(.+)$') {
        Close-Card
        if (-not $current) { New-Scope 'Ungrouped' 'ungrouped' }
        $card = [pscustomobject]@{
            id     = $Matches[2]
            title  = ($Matches[3] -replace '\*\*', '').Trim()
            mark   = $Matches[1]
            done   = ($Matches[1] -match '✅')
            files  = [System.Collections.Generic.List[string]]::new()
            body   = [System.Text.StringBuilder]::new()
        }
        continue
    }

    if ($card) {
        [void]$card.body.AppendLine($line)
        if ($line -match '^\*\*Spec:\*\*|^\*\*Files:\*\*|\*\*Files:\*\*') {
            # Files are the backticked tokens after the **Files:** marker on that line.
            $tail = $line -replace '^.*\*\*Files:\*\*', ''
            foreach ($m in [regex]::Matches($tail, '`([^`]+)`')) {
                $v = $m.Groups[1].Value.Trim()
                if ($v -and -not $card.files.Contains($v)) { $card.files.Add($v) | Out-Null }
            }
        }
    }
}
Close-Card

# ---------------------------------------------------------------------------------------
# Reduce to open scopes
# ---------------------------------------------------------------------------------------

$open = @()
foreach ($s in $scopes) {
    $openCards = @($s.cards | Where-Object { -not $_.done })
    if (-not $openCards) { continue }

    $files = @()
    foreach ($c in $openCards) { foreach ($f in $c.files) { if ($files -notcontains $f) { $files += $f } } }

    # Placement heuristic. box1 is the only XAMPP/MariaDB host (ADR-013), so anything that
    # touches the schema, a grant, or a test that needs a live database belongs there. The
    # prose test matters as much as the path test: plenty of API cards name no db/ file and
    # still cannot run anywhere else.
    $bodyAll = (($openCards | ForEach-Object { $_.body.ToString() }) -join "`n")
    $needsDb = ($files | Where-Object { $_ -match '^db/' }) -or
               ($bodyAll -match '(?i)mariadb|migration|merch_api|merch_migrator|integration test|grant')
    $box = if ($needsDb) { 'box1' } else { 'box3' }

    $open += [pscustomobject]@{
        scope     = $s.name
        kind      = $s.kind
        cards     = @($openCards | ForEach-Object { $_.id })
        titles    = @($openCards | ForEach-Object { "$($_.id) · $($_.title)" })
        openCount = $openCards.Count
        cardCount = $s.cards.Count
        files     = $files
        suggestBox = $box
        reason    = if ($needsDb) { 'touches the database, a migration, a grant, or a test that needs one -- ADR-013 pins those to box1' }
                    else          { 'no database surface found in its files or card text' }
    }
}

# Independence, computed rather than eyeballed.
#
# Equality is not enough, and assuming it was is a false negative that would have shipped:
# Track C declares the DIRECTORY src/Merchandising.Infrastructure/Data/ while Track B
# declares AuditLogWriter.vb INSIDE it. Those two scopes collide, and comparing strings for
# equality calls them independent -- which is precisely the merge conflict across two
# machines that /orchestrate section 1 says costs far more than a serial run.
function Test-PathOverlap([string] $a, [string] $b) {
    $x = $a.Trim().Replace('\', '/').TrimEnd('/').ToLowerInvariant()
    $y = $b.Trim().Replace('\', '/').TrimEnd('/').ToLowerInvariant()
    if ($x -eq $y) { return $true }
    return $x.StartsWith("$y/") -or $y.StartsWith("$x/")
}

$conflicts = @()
for ($i = 0; $i -lt $open.Count; $i++) {
    for ($j = $i + 1; $j -lt $open.Count; $j++) {
        $shared = @()
        foreach ($f in $open[$i].files) {
            foreach ($g in $open[$j].files) {
                if (Test-PathOverlap $f $g) {
                    $pair = if ($f -eq $g) { $f } else { "$f <-> $g" }
                    if ($shared -notcontains $pair) { $shared += $pair }
                }
            }
        }
        if ($shared) {
            $conflicts += [pscustomobject]@{ a = $open[$i].scope; b = $open[$j].scope; shared = $shared }
        }
    }
}

# Carried-forward cards are open, but each one is owed to a NAMED later gate (ADR-016), so
# they are not what "next" means. They stay visible -- hiding open work is how it gets
# forgotten -- but they sort last and never enter a suggested wave.
$open  = @(@($open | Where-Object { $_.kind -ne 'carried' }) + @($open | Where-Object { $_.kind -eq 'carried' }))
$shown = @($open | Select-Object -First $Max)

if ($Json) {
    [pscustomobject]@{
        tasksFile = $TasksFile
        openScopes = $open
        conflicts  = $conflicts
    } | ConvertTo-Json -Depth 6
    exit 0
}

# ---------------------------------------------------------------------------------------
# Digest
# ---------------------------------------------------------------------------------------

Write-Host ''
if (-not $open) {
    Write-Host '  No open scope in tasks.md -- every card is ticked.' -ForegroundColor Green
    Write-Host '  That is a phase gate question now, not an orchestration one: /phase-gate.' -ForegroundColor DarkGray
    Write-Host ''
    exit 0
}

# A tasks.md with no `## Track` headings has no scopes in it, and lumping every card into
# one enormous "Ungrouped" scope would be a guess dressed as an answer -- a worker would be
# handed the whole phase. Say what is wrong instead. /phase-gate is what regenerates this
# file, and its step 7 is where the grouping is supposed to come from.
$ungrouped = @($open | Where-Object { $_.kind -eq 'ungrouped' })
if ($ungrouped) {
    Write-Host '  tasks.md has cards that sit under no `## Track` heading:' -ForegroundColor Red
    foreach ($u in $ungrouped) { Write-Host "    $($u.cards -join ', ')" -ForegroundColor Red }
    Write-Host ''
    Write-Host '  A track is the fleet''s unit of dispatch, so these cannot be orchestrated as they' -ForegroundColor Yellow
    Write-Host '  stand. Group them under `## Track` headings (see /phase-gate step 7), or run them' -ForegroundColor Yellow
    Write-Host '  one at a time with /task. Not proposing a wave over an ungrouped file.' -ForegroundColor Yellow
    Write-Host ''
    exit 2
}

Write-Host "Open scopes in $([IO.Path]::GetFileName($TasksFile))  ($($open.Count) open, showing $($shown.Count))" -ForegroundColor Cyan
Write-Host ''
foreach ($s in $shown) {
    $tag = if ($s.kind -eq 'carried') { '  [carried forward -- owned by a later gate, take it only if asked]' } else { '' }
    Write-Host "  $($s.scope)$tag" -ForegroundColor White
    Write-Host "    cards : $($s.cards -join ', ')  ($($s.openCount) open of $($s.cardCount))"
    if ($s.files) { Write-Host "    files : $($s.files -join ', ')" -ForegroundColor DarkGray }
    else          { Write-Host "    files : (none declared -- independence cannot be computed for this scope)" -ForegroundColor DarkYellow }
    Write-Host "    place : $($s.suggestBox) -- $($s.reason)" -ForegroundColor DarkGray
    Write-Host ''
}

if ($conflicts) {
    Write-Host '  Not parallel-safe:' -ForegroundColor Yellow
    foreach ($c in $conflicts) {
        Write-Host "    $($c.a)  x  $($c.b)   share: $($c.shared -join ', ')" -ForegroundColor Yellow
    }
    Write-Host ''
}

# A scope with SOME conflict is not thereby unusable -- Track A colliding with Track E says
# nothing about running A beside B. What is wanted is a set that is mutually compatible, so
# build one greedily in file order: take a scope if it collides with nothing already taken.
# Greedy rather than optimal on purpose -- deterministic, explainable, and the orchestrator
# is free to pick a different set from the conflict list printed above.
$safe = @()
foreach ($s in @($shown | Where-Object { $_.kind -ne 'carried' })) {
    $clash = $false
    foreach ($t in $safe) {
        if ($conflicts | Where-Object {
                ($_.a -eq $s.scope -and $_.b -eq $t.scope) -or ($_.a -eq $t.scope -and $_.b -eq $s.scope) }) {
            $clash = $true; break
        }
    }
    if (-not $clash) { $safe += $s }
}
if ($safe.Count -ge 2) {
    Write-Host "  Parallel-safe wave: $(($safe | ForEach-Object { "$($_.scope) -> $($_.suggestBox)" }) -join ' | ')" -ForegroundColor Green
} elseif ($safe.Count -eq 1) {
    Write-Host "  Dispatch one scope: $($safe[0].scope) -> $($safe[0].suggestBox)" -ForegroundColor Green
} else {
    # Everything open either conflicts with something else or is carried forward. Saying so
    # is the useful answer; naming a scope anyway would be the orchestrator's mistake made
    # for it.
    Write-Host '  Nothing here is parallel-safe on its own. Sequence the scopes above, or' -ForegroundColor Yellow
    Write-Host '  take the conflict to the user -- do not dispatch two scopes over one file.' -ForegroundColor Yellow
}
Write-Host ''
Write-Host '  Computed from each card''s **Files:** line. A card that understates its files' -ForegroundColor DarkGray
Write-Host '  understates its conflicts -- this proposes, the orchestrator decides.' -ForegroundColor DarkGray
Write-Host ''
