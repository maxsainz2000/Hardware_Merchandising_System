<#
.SYNOPSIS
    Repository guardrails for the Merchandising System.

.DESCRIPTION
    Enforces the four constraints that must never be violated:

      G-A  No C# application source anywhere under src/
      G-B  Client projects never reference Infrastructure or any database package
      G-C  No connection strings or credentials in client projects
      G-D  No project overrides Option Strict / Option Explicit

    On XML comments: G-B and G-D ignore them, G-C does not. That asymmetry
    is deliberate. G-B and G-D look for things that must take effect to do
    any harm, and nothing takes effect from inside a comment. G-C looks for
    credentials, and a credential in a comment is still in the repository.
    See the Remove-XmlComments header below.

    Exits 0 if all pass, 1 if any fail. Intended to run:
      - as a git pre-commit hook (see scripts/install-hooks.ps1)
      - from Directory.Build.targets during the API build
      - manually before any commit

.EXAMPLE
    pwsh ./scripts/check-no-csharp.ps1
#>

[CmdletBinding()]
param(
    [string] $RepoRoot
)

$ErrorActionPreference = 'Stop'
$failures = New-Object System.Collections.Generic.List[string]

function Add-Failure {
    param([string] $Guardrail, [string] $Message)
    $failures.Add("[$Guardrail] $Message")
}

# ---------------------------------------------------------------------------
# Repository root resolution
#
# FIXED AT P1-01, on this script's first execution under Windows PowerShell.
# $RepoRoot was previously a param() default of (Split-Path -Parent
# $PSScriptRoot). Under pwsh that works. Under Windows PowerShell 5.1 invoked
# as `powershell -File <script>` - which is exactly how Directory.Build.targets
# calls this script, deliberately, for machines with no pwsh installed -
# $PSScriptRoot is empty at param-binding time, Split-Path rejects the empty
# string, and the whole build fails with MSB3073.
#
# The loud failure was the lucky outcome. The dangerous one is one step away:
# had $RepoRoot merely ended up empty rather than throwing, $srcPath would have
# resolved relative to MSBuild's working directory, src/ would not have been
# found there, and this script would have printed "nothing to check yet" and
# EXITED 0. A guardrail that passes because it is looking in the wrong place is
# worse than no guardrail, because it is trusted.
#
# So: resolve the root through a fallback chain, and then prove it really is
# the repository root before trusting any result derived from it.
# ---------------------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $scriptDir = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($scriptDir) -and $MyInvocation.MyCommand.Path) {
        $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    }
    if ([string]::IsNullOrWhiteSpace($scriptDir)) {
        Write-Host 'Cannot determine the location of this script. Pass -RepoRoot explicitly.' -ForegroundColor Red
        exit 1
    }
    $RepoRoot = Split-Path -Parent $scriptDir
}

if ([string]::IsNullOrWhiteSpace($RepoRoot) -or -not (Test-Path -LiteralPath $RepoRoot)) {
    Write-Host "Repository root '$RepoRoot' does not exist. Refusing to report a pass." -ForegroundColor Red
    exit 1
}

# Marker check. CLAUDE.md sits at the repository root and nowhere else, so its
# absence means we resolved to the wrong directory. Fail rather than pass.
if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot 'CLAUDE.md'))) {
    Write-Host "'$RepoRoot' does not look like the repository root (no CLAUDE.md)." -ForegroundColor Red
    Write-Host 'Refusing to report a pass from a directory that may not be the repository.' -ForegroundColor Red
    exit 1
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
# XML comment handling  (added at P1-01a)
#
# WHY THIS EXISTS. G-B and G-D matched raw project-file text, so they could
# not tell a real reference from the rule written down in a comment. Writing
#
#     <!-- Never reference <the connector package>. -->
#
# inside a client .vbproj FAILED THE BUILD: the guardrail read its own
# prohibition as a violation. Found at P1-01 while documenting the rule in
# Merchandising.ClientCommon.vbproj, which is precisely the file where that
# rule most deserves to be written down.
#
# WHY IT IS SAFE. G-B and G-D both check for things that must TAKE EFFECT to
# matter: a ProjectReference, a PackageReference, a PublishAot property. None
# of those can take effect from inside a comment, because MSBuild never sees
# them. So ignoring comments cannot hide a real violation - there is no
# evasion route here, only false positives to remove.
#
# WHY G-C DELIBERATELY DOES NOT USE THIS. G-C looks for credentials, and a
# connection string sitting in a comment is a real leaked credential. It is
# in the repository, it is in the history, and it is readable by anyone with
# the clone. G-C must keep scanning comments. The asymmetry is the point, and
# it is proven by a negative test in
# evidence/phase-1/p1-01a-guardrail-comment-fix.txt.
#
# Newlines inside each comment are preserved so reported line numbers stay
# accurate. An unterminated '<!--' matches nothing and is therefore left in
# place to be scanned - failing closed, which is the correct direction.
# ---------------------------------------------------------------------------
function Remove-XmlComments {
    param([string] $Text)

    if ([string]::IsNullOrEmpty($Text)) { return $Text }

    $result = New-Object System.Text.StringBuilder
    $lastIndex = 0

    foreach ($match in [regex]::Matches($Text, '<!--[\s\S]*?-->')) {
        $null = $result.Append($Text.Substring($lastIndex, $match.Index - $lastIndex))
        $newlineCount = ([regex]::Matches($match.Value, "`n")).Count
        if ($newlineCount -gt 0) { $null = $result.Append("`n" * $newlineCount) }
        $lastIndex = $match.Index + $match.Length
    }

    $null = $result.Append($Text.Substring($lastIndex))
    return $result.ToString()
}

# Returns the 1-based line number of every match, so a failure message can
# point at the offending line instead of just naming the pattern.
function Get-MatchLineNumbers {
    param([string] $Text, [string] $Pattern)

    $lineNumbers = @()
    foreach ($match in [regex]::Matches($Text, $Pattern)) {
        $lineNumbers += ($Text.Substring(0, $match.Index) -split "`n").Count
    }

    # A single line can match more than once - a ProjectReference path repeats
    # the project name in both the directory and the file name, for instance.
    # Report each line once.
    return @($lineNumbers | Sort-Object -Unique)
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

    # Comments are stripped: a reference cannot take effect from inside one.
    # See the Remove-XmlComments header for why this cannot hide a violation.
    $content = Remove-XmlComments (Get-Content -Path $projFile -Raw)

    foreach ($pattern in $forbiddenReferencePatterns) {
        $lineNumbers = @(Get-MatchLineNumbers -Text $content -Pattern $pattern)
        if ($lineNumbers.Count -gt 0) {
            $gbFailed = $true
            $where = "line$(if ($lineNumbers.Count -gt 1) { 's' }) $($lineNumbers -join ', ')"
            Add-Failure 'G-B' "$name must not reference '$pattern' ($where). Client projects never touch the database."
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
    # Comments stripped for the same reason as G-B: an MSBuild property has no
    # effect from inside a comment, so scanning comments can only ever produce
    # a false positive. This matters here because the natural way to warn the
    # next reader is to name the property in a comment saying not to set it.
    $content = Remove-XmlComments (Get-Content -Path $proj.FullName -Raw)
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
