<#
.SYNOPSIS
    Repository guardrails for the Merchandising System.

.DESCRIPTION
    Enforces the four constraints that must never be violated:

      G-A  No C# application source anywhere under src/
      G-B  Client projects never reference Infrastructure or any database package
      G-C  No connection strings or credentials in client projects
      G-D  No project overrides Option Strict / Option Explicit

    Exits 0 if all pass, 1 if any fail. Intended to run:
      - as a git pre-commit hook (see scripts/install-hooks.ps1)
      - from Directory.Build.targets during the API build
      - manually before any commit

.EXAMPLE
    pwsh ./scripts/check-no-csharp.ps1
#>

[CmdletBinding()]
param(
    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$failures = New-Object System.Collections.Generic.List[string]

function Add-Failure {
    param([string] $Guardrail, [string] $Message)
    $failures.Add("[$Guardrail] $Message")
}

$srcPath = Join-Path $RepoRoot 'src'
if (-not (Test-Path $srcPath)) {
    Write-Host "src/ not found at '$srcPath' - nothing to check yet." -ForegroundColor Yellow
    exit 0
}

# Files under bin/ and obj/ are build output, not authored source.
function Get-SourceFiles {
    param([string] $Path, [string[]] $Include)
    Get-ChildItem -Path $Path -Recurse -File -Include $Include -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|\.vs|packages|TestResults)[\\/]' }
}

# ---------------------------------------------------------------------------
# G-A  No C# application source
# ---------------------------------------------------------------------------
Write-Host 'G-A  Checking for C# source...' -NoNewline

$csharpFiles = Get-SourceFiles -Path $srcPath -Include '*.cs', '*.csproj', '*.cshtml', '*.razor'
if ($csharpFiles) {
    Write-Host ' FAIL' -ForegroundColor Red
    foreach ($f in $csharpFiles) {
        $rel = $f.FullName.Substring($RepoRoot.Length).TrimStart('\', '/')
        Add-Failure 'G-A' "C# / Razor source is not permitted: $rel"
    }
} else {
    Write-Host ' pass' -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# G-B  Client projects must not reference Infrastructure or database packages
# ---------------------------------------------------------------------------
Write-Host 'G-B  Checking client project references...' -NoNewline

$clientProjectNames = @(
    'Merchandising.ClientCommon',
    'Merchandising.Procurement',
    'Merchandising.Inventory',
    'Merchandising.POS'
)

$forbiddenReferencePatterns = @(
    'Merchandising\.Infrastructure',
    'MySqlConnector',
    'MySql\.Data',
    'MariaDB',
    'Microsoft\.EntityFrameworkCore'
)

$gbFailed = $false
foreach ($name in $clientProjectNames) {
    $projFile = Join-Path $srcPath "$name/$name.vbproj"
    if (-not (Test-Path $projFile)) { continue }

    $content = Get-Content -Path $projFile -Raw
    foreach ($pattern in $forbiddenReferencePatterns) {
        if ($content -match $pattern) {
            $gbFailed = $true
            Add-Failure 'G-B' "$name must not reference '$pattern'. Client projects never touch the database."
        }
    }
}
if ($gbFailed) { Write-Host ' FAIL' -ForegroundColor Red } else { Write-Host ' pass' -ForegroundColor Green }

# ---------------------------------------------------------------------------
# G-C  No credentials or connection strings in client projects
# ---------------------------------------------------------------------------
Write-Host 'G-C  Checking client projects for credentials...' -NoNewline

$secretPatterns = @(
    'Server\s*=\s*[^;"'']+;',
    'Database\s*=\s*[^;"'']+;',
    'Uid\s*=',
    'Pwd\s*=',
    'User\s+Id\s*=',
    'password\s*=\s*["''][^"'']+["'']'
)

$gcFailed = $false
foreach ($name in $clientProjectNames) {
    $projDir = Join-Path $srcPath $name
    if (-not (Test-Path $projDir)) { continue }

    $scanFiles = Get-SourceFiles -Path $projDir -Include '*.vb', '*.xaml', '*.json', '*.config', '*.vbproj'
    foreach ($file in $scanFiles) {
        $text = Get-Content -Path $file.FullName -Raw -ErrorAction SilentlyContinue
        if (-not $text) { continue }

        foreach ($pattern in $secretPatterns) {
            if ($text -match $pattern) {
                $gcFailed = $true
                $rel = $file.FullName.Substring($RepoRoot.Length).TrimStart('\', '/')
                Add-Failure 'G-C' "Possible credential or connection string in client code: $rel (matched /$pattern/)"
            }
        }
    }
}
if ($gcFailed) { Write-Host ' FAIL' -ForegroundColor Red } else { Write-Host ' pass' -ForegroundColor Green }

# ---------------------------------------------------------------------------
# G-D  No project overrides Option Strict / Option Explicit
# ---------------------------------------------------------------------------
Write-Host 'G-D  Checking Option Strict inheritance...' -NoNewline

$gdFailed = $false
$vbProjects = Get-SourceFiles -Path $srcPath -Include '*.vbproj'
foreach ($proj in $vbProjects) {
    $content = Get-Content -Path $proj.FullName -Raw
    $rel = $proj.FullName.Substring($RepoRoot.Length).TrimStart('\', '/')

    if ($content -match '<OptionStrict>\s*Off\s*</OptionStrict>') {
        $gdFailed = $true
        Add-Failure 'G-D' "Option Strict is disabled in $rel. It is set globally in Directory.Build.props and must not be overridden."
    }
    if ($content -match '<OptionExplicit>\s*Off\s*</OptionExplicit>') {
        $gdFailed = $true
        Add-Failure 'G-D' "Option Explicit is disabled in $rel."
    }
    if ($content -match '<PublishAot>\s*true\s*</PublishAot>' -or
        $content -match '<PublishTrimmed>\s*true\s*</PublishTrimmed>') {
        $gdFailed = $true
        Add-Failure 'G-D' "AOT/trimming is enabled in $rel. These do not work with Visual Basic - see CLAUDE.md section 3."
    }
}
if ($gdFailed) { Write-Host ' FAIL' -ForegroundColor Red } else { Write-Host ' pass' -ForegroundColor Green }

# ---------------------------------------------------------------------------
# Result
# ---------------------------------------------------------------------------
Write-Host ''
if ($failures.Count -gt 0) {
    Write-Host "GUARDRAILS FAILED ($($failures.Count) issue(s)):" -ForegroundColor Red
    Write-Host ''
    foreach ($f in $failures) { Write-Host "  $f" -ForegroundColor Red }
    Write-Host ''
    Write-Host 'See CLAUDE.md for the rules these enforce.' -ForegroundColor Yellow
    exit 1
}

Write-Host 'All guardrails passed.' -ForegroundColor Green
exit 0
