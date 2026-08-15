<#
.SYNOPSIS
    Layer 1 guardrail - PreToolUse on Write|Edit. Blocks creation of C# / Razor files.

.DESCRIPTION
    Prevention, not detection. Reads the Claude Code hook payload from stdin, pulls
    .tool_input.file_path, and refuses the write outright if the extension is one this
    project forbids.

    This must stay fast: a pure path check. No tree scan, no build, no file read. A
    guardrail slow enough to be annoying is a guardrail that gets disabled, and a
    disabled guardrail is worse than none.

    Exit 0  = allow.
    Exit 2  = block, and feed stderr back to Claude as the reason.

    Deliberately does NOT depend on jq - jq is a Unix tool and is not installed on the
    development machine. stdin JSON is parsed with ConvertFrom-Json.
#>

$ErrorActionPreference = 'Stop'

# Forbidden by CLAUDE.md section 2. XAML, SQL, PowerShell, JSON and Markdown are permitted
# non-VB artifacts and are not listed here.
$forbiddenExtensions = @('.cs', '.csproj', '.cshtml', '.razor')

try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

    $payload = $raw | ConvertFrom-Json
    $filePath = $payload.tool_input.file_path
    if ([string]::IsNullOrWhiteSpace($filePath)) { exit 0 }

    # Normalise separators so 'src\Foo.cs' and 'src/Foo.cs' are treated identically.
    $normalised = $filePath -replace '\\', '/'
    $extension = [System.IO.Path]::GetExtension($normalised)
    if ([string]::IsNullOrWhiteSpace($extension)) { exit 0 }

    $isForbidden = $forbiddenExtensions | Where-Object {
        [string]::Equals($_, $extension, [StringComparison]::OrdinalIgnoreCase)
    }

    if ($isForbidden) {
        $message = @"
BLOCKED by repository guardrail (Claude Code PreToolUse hook).

  Refused path : $filePath
  Extension    : $extension

All application source in this repository is Visual Basic .NET. This is a binding
course requirement confirmed by the professor (PA-001) - there is no C# escape hatch,
and no exception for scratch files, samples, or generated code. See CLAUDE.md section 2.

Do not work around this: do not rename the file, do not write it to another directory,
and do not disable the hook. If the task genuinely cannot be completed in Visual Basic,
that is a stop condition (CLAUDE.md section 7.1) - report the blocker and stop.
Reporting a blocker is a successful outcome. Guessing is not.
"@
        [Console]::Error.WriteLine($message)
        exit 2
    }

    exit 0
}
catch {
    # A malformed payload must never silently allow a forbidden write, but it also must
    # never wedge the session. Warn on stderr and allow; the Stop hook still sweeps.
    [Console]::Error.WriteLine("block-csharp.ps1 could not parse the hook payload: $($_.Exception.Message)")
    exit 0
}
