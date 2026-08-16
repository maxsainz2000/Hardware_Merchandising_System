<#
.SYNOPSIS
    Publishes a Merchandising System component as win-x64.

.DESCRIPTION
    Spec section 18 requires the win-x64 runtime identifier to be named
    explicitly rather than inherited. Directory.Build.props deliberately does
    NOT set RuntimeIdentifier globally - applying a RID to class libraries and
    test projects forces RID-specific restore and makes `dotnet test` awkward
    for no benefit - so the RID is applied here, at publish time, and nowhere
    else. That is why this script exists rather than a property in the props
    file.

    WHAT THIS SCRIPT IS NOT, YET. Spec section 18's "Versioning" row wants a
    full release package: version number, commit identifier, migration list,
    release notes, rollback instructions and tested package hashes. This
    script produces a published folder, records its size and the SHA-256 of
    the primary executable, and stops there. The rest is Phase 6/7 packaging
    work. A stub that claimed to produce a release manifest would be worse
    than one that does not pretend to.

    Created at P1-03, which needed both publish modes for ADR-010.

.PARAMETER Component
    One or more of Api, Maintenance, Procurement, Inventory, POS. These are
    the only publishable components - the libraries and test projects are
    consumed by them and are never published on their own.

.PARAMETER Mode
    FrameworkDependent (requires the matching runtime on the target machine)
    or SelfContained (carries its own runtime, ~500x larger). ADR-010 records
    which mode each component ships in.

.PARAMETER OutputRoot
    Defaults to artifacts/publish under the repository root, which .gitignore
    already excludes via the [Pp]ublish/ pattern. The target directory is
    DELETED before publishing so a stale file from an earlier run cannot end
    up in the output or in the reported file count.

.EXAMPLE
    pwsh ./scripts/publish-release.ps1 -Component Api -Mode FrameworkDependent

.EXAMPLE
    pwsh ./scripts/publish-release.ps1 -Component Api,Maintenance -Mode SelfContained
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Api', 'Maintenance', 'Procurement', 'Inventory', 'POS')]
    [string[]] $Component,

    [Parameter(Mandatory = $true)]
    [ValidateSet('FrameworkDependent', 'SelfContained')]
    [string] $Mode,

    [string] $OutputRoot,
    [string] $Configuration = 'Release',
    [string] $RepoRoot
)

$ErrorActionPreference = 'Stop'

# Spec section 18 names this explicitly and ADR-010 fixes it. It is not a
# parameter on purpose: this system targets Windows x64 and nothing else, and
# a RID typo that silently produces a working-but-wrong package is exactly the
# class of deployment bug that surfaces on the classroom machine.
$Rid = 'win-x64'

# Same $PSScriptRoot-in-a-param-default defect that broke check-no-csharp.ps1
# under Windows PowerShell 5.1 at P1-01. Fixed here from the start rather than
# after the fact - these three scripts sit side by side and get copied from
# each other.
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

if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot 'CLAUDE.md'))) {
    Write-Host "'$RepoRoot' does not look like the repository root (no CLAUDE.md)." -ForegroundColor Red
    exit 1
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $RepoRoot 'artifacts/publish'
}

$projectPaths = @{
    'Api'         = 'src/Merchandising.Api/Merchandising.Api.vbproj'
    'Maintenance' = 'src/Merchandising.Maintenance/Merchandising.Maintenance.vbproj'
    'Procurement' = 'src/Merchandising.Procurement/Merchandising.Procurement.vbproj'
    'Inventory'   = 'src/Merchandising.Inventory/Merchandising.Inventory.vbproj'
    'POS'         = 'src/Merchandising.POS/Merchandising.POS.vbproj'
}

$selfContained = ($Mode -eq 'SelfContained')

function Write-Step { param([string] $Text) Write-Host "`n=== $Text ===" -ForegroundColor Cyan }

Push-Location $RepoRoot
try {
    foreach ($name in $Component) {

        $project = $projectPaths[$name]
        if (-not (Test-Path $project)) { throw "Project not found: $project" }

        Write-Step "$name  |  $Mode  |  $Rid"

        # ------------------------------------------------------------------
        # Refuse to publish with AOT or trimming enabled.
        #
        # Neither works with Visual Basic (CLAUDE.md section 3), and guardrail
        # G-D only reads .vbproj TEXT - so it cannot see `-p:PublishAot=true`
        # passed on a command line, or a value injected through an environment
        # variable or a Directory.Build.props edit made after G-D last ran.
        # This checks the value MSBuild actually resolved for this publish,
        # which is the only number that decides what gets built.
        # ------------------------------------------------------------------
        foreach ($prop in 'PublishAot', 'PublishTrimmed') {
            $resolved = (& dotnet msbuild $project "-getProperty:$prop" `
                    "-p:RuntimeIdentifier=$Rid" `
                    "-p:SelfContained=$($selfContained.ToString().ToLowerInvariant())" `
                    "-p:Configuration=$Configuration" -nologo | Out-String).Trim()

            if ($resolved -eq 'true') {
                throw "$prop resolved to true for $name. Neither AOT nor trimming works with Visual Basic - see CLAUDE.md section 3 and ADR-010. Refusing to publish."
            }
            Write-Host ("  {0,-16} = {1}" -f $prop, $(if ($resolved) { $resolved } else { '(unset)' }))
        }

        $outDir = Join-Path $OutputRoot "$name-$(if ($selfContained) { 'sc' } else { 'fd' })"

        # Delete first. A stale file from an earlier run would otherwise sit in
        # the output and corrupt both the package and the file count below.
        if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
        New-Item -ItemType Directory -Force -Path $outDir | Out-Null

        & dotnet publish $project `
            --configuration $Configuration `
            --runtime $Rid `
            --self-contained $selfContained.ToString().ToLowerInvariant() `
            --output $outDir `
            --nologo

        if ($LASTEXITCODE -ne 0) { throw "Publish failed for $name ($Mode)." }

        $files = Get-ChildItem -Path $outDir -Recurse -File
        $bytes = ($files | Measure-Object -Property Length -Sum).Sum

        # ------------------------------------------------------------------
        # HASH THE PACKAGE, NOT THE EXECUTABLE.
        #
        # Spec section 18 wants "tested package hashes". The obvious
        # implementation - hash Merchandising.<name>.exe - is worse than
        # useless here, and P1-03 caught it by looking at the output:
        #
        #   Api-fd\Merchandising.Api.exe  BBBFD640...  162,816 bytes
        #   Api-sc\Merchandising.Api.exe  BBBFD640...  162,816 bytes   IDENTICAL
        #   Api-fd\Merchandising.Api.dll  0CC44E4A...    7,680 bytes
        #   Api-sc\Merchandising.Api.dll  0CC44E4A...    7,680 bytes   IDENTICAL
        #
        # The .exe is only the apphost shim and the .dll is byte-identical
        # because Deterministic=true. A 0.2 MB package that REQUIRES a runtime
        # on the host and a 105 MB package that CARRIES one would have been
        # reported as the same artifact. The two differ in runtimeconfig.json
        # ("frameworks" vs "includedFrameworks") and in ~330 runtime files -
        # none of which an executable hash observes.
        #
        # So the identity is the hash of every file's hash plus its relative
        # path, sorted. Paths are included because a package that moved a file
        # is a different package, and sorted so the result does not depend on
        # directory enumeration order.
        # ------------------------------------------------------------------
        $entries = $files |
            ForEach-Object {
                $rel = $_.FullName.Substring($outDir.Length).TrimStart('\', '/').Replace('\', '/')
                [PSCustomObject]@{
                    Path = $rel
                    Hash = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash
                }
            } |
            Sort-Object Path

        $manifestText = ($entries | ForEach-Object { "$($_.Hash)  $($_.Path)" }) -join "`n"
        $packageHash = [BitConverter]::ToString(
            [System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($manifestText))
        ).Replace('-', '')

        # Written BESIDE the package, never inside it - a manifest stored in
        # the directory it describes changes the thing it is describing.
        $manifestPath = "$outDir.sha256"
        Set-Content -Path $manifestPath -Value $manifestText -Encoding UTF8

        Write-Host ''
        Write-Host "  output    : $outDir"
        Write-Host "  files     : $($files.Count)"
        Write-Host ("  size      : {0:N1} MB" -f ($bytes / 1MB))
        Write-Host "  package   : $packageHash"
        Write-Host "  manifest  : $(Split-Path -Leaf $manifestPath)"
    }

    Write-Host "`nPublish complete." -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "`n$($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    Pop-Location
}
