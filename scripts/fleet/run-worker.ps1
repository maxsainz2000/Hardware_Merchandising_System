<#
.SYNOPSIS
    Runs one headless Claude Code worker. Reads the brief from stdin, writes the
    structured report envelope to stdout.

.DESCRIPTION
    This is the innermost layer of the dispatcher, and it exists for one reason:
    quoting. `claude --json-schema` takes inline JSON, not a path, and threading a
    1KB JSON blob plus a multi-line brief through Start-Process (locally) and then
    through ssh (remotely) means three levels of nested quotes and a different shell
    at the far end.

    Instead, both transports invoke THIS file with plain scalar parameters. The schema
    is read from disk on whichever machine is running, and the brief arrives on stdin.
    Nothing that can contain a quote, a newline, or a backslash ever touches a command
    line. A worker box gets its copy the same way it gets everything else -- by pulling the repo.
#>
[CmdletBinding()]
param(
    [string] $Model          = 'sonnet',
    [string] $Effort         = 'high',
    [string] $PermissionMode = 'acceptEdits',
    [double] $MaxBudgetUsd   = 5,
    [string] $Name           = 'worker'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $repoRoot

# Compact so it survives as a single argv entry; ConvertFrom/To also validates it,
# turning a malformed schema into a clear error here rather than inside claude.
$schema = Get-Content -Raw (Join-Path $repoRoot 'scripts/fleet/worker-report.schema.json') |
          ConvertFrom-Json | ConvertTo-Json -Depth 20 -Compress

$prompt = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($prompt)) { throw 'run-worker: no brief on stdin.' }

$prompt | & claude -p `
    --output-format json `
    --json-schema $schema `
    --model $Model `
    --effort $Effort `
    --permission-mode $PermissionMode `
    --max-budget-usd $MaxBudgetUsd `
    --name $Name

exit $LASTEXITCODE
