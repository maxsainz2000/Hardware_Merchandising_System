#Requires -Version 5.1
<#
.SYNOPSIS
    Takes a fresh clone of this repository to a running system: database
    accounts, per-installation credentials, schema, grants, and the HTTPS
    certificate.

.DESCRIPTION
    ADR-012 says the deliverable is a handover package, not an installed
    system: three classmates must be able to install and demonstrate this on
    machines the author does not own. Before this script existed every setup
    step lived only as prose in docs/installation-guide.md, which meant a
    clean clone could not be started by anyone who had not read all of it.

    WHAT IT DOES, in the order the order matters:

      1. Preflight        Refuses to start unless the machine can actually
                          finish. Nothing is written until every check passes.
      2. Credentials      Generates a password per account, for THIS
                          installation. ADR-012 requirement 6: no password
                          known to the author may protect a classmate's demo.
      3. Accounts         db/grants/0001 as root - creates the database and
                          the three identities of ADR-013.
      4. Configuration    Writes the ACL-protected config files the API and
                          the maintenance utility read.
      5. Schema           Runs migrations through Merchandising.Maintenance,
                          as merch_migrator.
      6. Table grants     db/grants/0002 as root. MUST follow step 5 -
                          MariaDB 10.4 rejects a table-level GRANT naming a
                          table that does not exist yet (ERROR 1146).
      7. Verify           Proves the append-only guarantee is real by
                          watching UPDATE and DELETE be refused on the two
                          ledger tables.
      8. Certificate      Delegates to create-dev-certificate.ps1.

    WHAT IT DELIBERATELY DOES NOT DO:

      * It does not edit my.ini. Rewriting someone's XAMPP configuration - a
        file shared with whatever else they use XAMPP for - is not a thing a
        setup script should do silently. It CHECKS bind-address and sql_mode
        and tells you what to change. The per-connection
        "SET SESSION sql_mode" applied by ConnectionFactory (P1-05) means a
        host whose my.ini was never hardened is still safe at runtime.
      * It does not touch the hosts file or the firewall. Both need
        elevation of a different kind and both are per-network decisions;
        docs/installation-guide.md sections 1 and 5 own them, and this
        script prints exactly what is left to do.
      * It does not set the MariaDB root password. That is the installer's
        call on their own instance.

.PARAMETER XamppRoot
    Where XAMPP is installed. mysql.exe is located under here. The path is a
    parameter and not a constant because a classmate's XAMPP may not be on
    C: - the same reason ADR-012 requires the dump tool path to be
    configured rather than hardcoded.

.PARAMETER MySqlRootPassword
    Password for the MariaDB root account. Omit if root has no password (the
    XAMPP default, and the case on the author's lab host).

.PARAMETER Force
    Overwrite an existing installation's configuration files. Refused by
    default: those files hold the only copy of the database passwords for
    this installation, and silently replacing them would orphan a working
    database.

.PARAMETER SkipCertificate
    Skip step 8. Use when the certificate already exists and you are
    re-running the earlier steps.

.EXAMPLE
    pwsh ./scripts/bootstrap.ps1

.EXAMPLE
    pwsh ./scripts/bootstrap.ps1 -XamppRoot 'D:\xampp' -MySqlRootPassword 'secret'
#>

[CmdletBinding()]
param(
    [string] $XamppRoot = 'C:\xampp',
    [string] $MySqlRootUser = 'root',
    [string] $MySqlRootPassword,
    [string] $ConfigRoot = (Join-Path $env:ProgramData 'MerchandisingSystem\config'),
    [switch] $Force,
    [switch] $SkipCertificate
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$mysqlExe = Join-Path $XamppRoot 'mysql\bin\mysql.exe'

function Write-Step  { param([string] $Text) Write-Host "`n=== $Text ===" -ForegroundColor Cyan }
function Write-Ok    { param([string] $Text) Write-Host "  OK   $Text" -ForegroundColor Green }
function Write-Info  { param([string] $Text) Write-Host "       $Text" -ForegroundColor DarkGray }
function Write-Warn  { param([string] $Text) Write-Host "  WARN $Text" -ForegroundColor Yellow }

function Stop-Bootstrap {
    param([string] $Message, [string] $Fix)
    Write-Host "`n  FAIL $Message" -ForegroundColor Red
    if ($Fix) { Write-Host "       $Fix" -ForegroundColor Yellow }
    exit 1
}

# ---------------------------------------------------------------------------
# Runs SQL as root, via stdin.
#
# Via stdin and not a temporary file, deliberately: the accounts script
# carries three plaintext passwords once the placeholders are substituted, and
# a temp file containing them - however briefly - is a copy on disk that
# outlives a crash. Nothing here ever writes credentials anywhere except the
# ACL-protected config directory.
# ---------------------------------------------------------------------------
function Invoke-RootSql {
    param(
        [Parameter(Mandatory)] [string] $Sql,
        [string] $Description = 'SQL'
    )

    $arguments = @('--user', $MySqlRootUser, '--batch', '--silent')
    if ($MySqlRootPassword) { $arguments += "--password=$MySqlRootPassword" }

    $output = $Sql | & $mysqlExe @arguments 2>&1

    if ($LASTEXITCODE -ne 0) {
        Stop-Bootstrap "$Description failed (mysql exit $LASTEXITCODE)." ($output | Out-String)
    }

    return $output
}

# ---------------------------------------------------------------------------
# A password for one account, for this installation only.
#
# Alphabet note: letters and digits only. Not squeamishness - this value is
# interpolated into a SQL string literal AND into a MySqlConnector connection
# string, where ';' and quote characters have meaning. 24 characters of a
# 62-character alphabet is ~143 bits, which is far past anything that matters
# for an account reachable only from loopback.
# ---------------------------------------------------------------------------
function New-InstallationPassword {
    $alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789'
    $bytes = [byte[]]::new(24)
    # RandomNumberGenerator::Fill is .NET Core only and does NOT exist on
    # the .NET Framework that Windows PowerShell 5.1 runs on. This script
    # previously used it with no #Requires line, so a classmate who
    # right-clicked "Run with PowerShell" - which uses 5.1, not pwsh -
    # got a MethodNotFound crash partway through, after preflight and
    # before anything was written. Create()+GetBytes() exists on both, so
    # the bootstrap no longer requires PowerShell 7 to be installed first.
    # Found at P1-17 while reusing this function.
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }

    -join ($bytes | ForEach-Object { $alphabet[$_ % $alphabet.Length] })
}

Write-Host ''
Write-Host 'Merchandising System - bootstrap' -ForegroundColor White
Write-Host 'Fresh clone to running system. Read scripts/bootstrap.ps1 before running it.' -ForegroundColor DarkGray

# ===========================================================================
# 1. PREFLIGHT
#
# Every check runs before anything is written. A setup script that fails
# halfway leaves a machine in a state nobody documented, and the person
# holding it is by definition the person who does not know this system.
# ===========================================================================
Write-Step '1. Preflight'

if ($PSVersionTable.PSVersion.Major -lt 7) {
    Stop-Bootstrap "PowerShell 7+ required; this is $($PSVersionTable.PSVersion)." `
                   'Install PowerShell 7 and re-run with pwsh, not powershell.'
}
Write-Ok "PowerShell $($PSVersionTable.PSVersion)"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Stop-Bootstrap 'This session is not elevated.' `
                   'Creating the ACL-protected config directory under ProgramData needs administrator rights. Re-run from an elevated PowerShell.'
}
Write-Ok 'Elevated session'

$dotnetVersion = (& dotnet --version 2>$null)
if ($LASTEXITCODE -ne 0) {
    Stop-Bootstrap 'The .NET SDK is not on PATH.' 'Install the .NET 10 SDK (see README.md, Prerequisites).'
}
if (-not $dotnetVersion.StartsWith('10.')) {
    Stop-Bootstrap "The .NET SDK is $dotnetVersion; this project targets net10.0 (ADR-002)." `
                   'Install the .NET 10 SDK. Do not retarget the projects.'
}
Write-Ok ".NET SDK $dotnetVersion"

if (-not (Test-Path $mysqlExe)) {
    Stop-Bootstrap "mysql.exe not found at $mysqlExe." `
                   'Pass -XamppRoot with the path to your XAMPP installation. Note this XAMPP build ships mysql.exe, NOT mariadb.exe (ADR-003).'
}
Write-Ok "mysql client at $mysqlExe"

try {
    $serverVersion = (Invoke-RootSql -Sql 'SELECT VERSION();' -Description 'Server version query') | Select-Object -First 1
} catch {
    Stop-Bootstrap 'Could not connect to MariaDB as root.' `
                   'Start MySQL from the XAMPP Control Panel. If root has a password, pass -MySqlRootPassword.'
}
Write-Ok "MariaDB $serverVersion"

if ($serverVersion -notlike '10.4.*') {
    Write-Warn "Pinned version is 10.4.32 (ADR-002); this server reports $serverVersion."
    Write-Info 'The SQL in db/ is written for 10.4 and avoids 10.7+/11.x features. Newer usually works; newer is not what was tested.'
}

# bind-address and sql_mode are CHECKED, never rewritten - see the header.
$bindAddress = (Invoke-RootSql -Sql "SELECT @@bind_address;" -Description 'bind_address query') | Select-Object -First 1
if ($bindAddress -eq '127.0.0.1' -or $bindAddress -eq 'localhost') {
    Write-Ok "bind-address = $bindAddress (loopback only)"
} else {
    Write-Warn "bind-address = '$bindAddress' - the database is reachable from the network."
    Write-Info 'Spec section 17 requires clients to be unable to reach MariaDB directly.'
    Write-Info "Set bind-address=127.0.0.1 in $XamppRoot\mysql\bin\my.ini and restart MySQL."
}

$sqlMode = (Invoke-RootSql -Sql 'SELECT @@GLOBAL.sql_mode;' -Description 'sql_mode query') | Select-Object -First 1
if ($sqlMode -like '*STRICT_TRANS_TABLES*') {
    Write-Ok 'sql_mode includes STRICT_TRANS_TABLES'
} else {
    Write-Warn 'sql_mode does NOT include STRICT_TRANS_TABLES (the XAMPP default weakens it).'
    Write-Info "Add STRICT_TRANS_TABLES to sql_mode in $XamppRoot\mysql\bin\my.ini and restart MySQL."
    Write-Info 'Runtime is still safe: ConnectionFactory sets it per connection (P1-05). This is defence in depth, not a blocker.'
}

$accountsSqlPath = Join-Path $repoRoot 'db\grants\0001_accounts-and-grants.sql'
$tableGrantsSqlPath = Join-Path $repoRoot 'db\grants\0002_post-migration-grants.sql'
foreach ($required in @($accountsSqlPath, $tableGrantsSqlPath)) {
    if (-not (Test-Path $required)) { Stop-Bootstrap "Missing $required." 'Is this a complete clone of the repository?' }
}
Write-Ok 'Grant scripts present'

$databaseConfigPath = Join-Path $ConfigRoot 'database.json'
$migratorConfigPath = Join-Path $ConfigRoot 'database.migrator.json'
$backupConfigPath   = Join-Path $ConfigRoot 'database.backup.json'
$credentialRecordPath = Join-Path $ConfigRoot 'installation-credentials.txt'

if ((Test-Path $databaseConfigPath) -and -not $Force) {
    Stop-Bootstrap "Configuration already exists at $databaseConfigPath." `
                   'This machine already has an installation. Re-run with -Force to replace it - but note that the existing file holds the ONLY copy of that installation database password.'
}

Write-Ok 'Preflight complete - nothing has been written yet'

# ===========================================================================
# 2. CREDENTIALS
# ===========================================================================
Write-Step '2. Generating per-installation credentials'

$migratorPassword = New-InstallationPassword
$apiPassword      = New-InstallationPassword
$backupPassword   = New-InstallationPassword

Write-Ok 'Three passwords generated (merch_migrator, merch_api, merch_backup)'
Write-Info 'Never printed to this console and never committed. They are written only into the ACL-protected config directory in step 4.'

# ===========================================================================
# 3. ACCOUNTS AND DATABASE
# ===========================================================================
Write-Step '3. Creating the database and the three identities (db/grants/0001)'

$accountsSql = (Get-Content -Path $accountsSqlPath -Raw).
    Replace('{{MERCH_MIGRATOR_PASSWORD}}', $migratorPassword).
    Replace('{{MERCH_API_PASSWORD}}',      $apiPassword).
    Replace('{{MERCH_BACKUP_PASSWORD}}',   $backupPassword)

# Matches the token SHAPE, not any pair of braces. The file's own header
# comment says "replace the three {{...}} tokens", so a bare '\{\{' test
# matches the documentation and aborts every run - which is exactly what the
# first version of this guard did.
if ($accountsSql -match '\{\{MERCH_[A-Z_]+\}\}') {
    Stop-Bootstrap 'A {{MERCH_..._PASSWORD}} placeholder survived substitution in 0001_accounts-and-grants.sql.' `
                   'The script expects exactly the three documented tokens. If the file gained a fourth, add it here.'
}

Invoke-RootSql -Sql $accountsSql -Description 'db/grants/0001_accounts-and-grants.sql' | Out-Null
Write-Ok 'Database and accounts created (merch_migrator, merch_api, merch_backup)'
Write-Info 'merch_api holds NO database-level write privilege at this point. That is deliberate: it makes every table append-only by default (ADR-013).'

# ---------------------------------------------------------------------------
# Reconcile the passwords, which 0001 alone cannot do on a re-run.
#
# 0001 uses CREATE USER IF NOT EXISTS. On a fresh instance that sets the
# password generated above and this ALTER changes nothing. On a machine where
# the accounts already exist - a -Force re-install - CREATE USER IF NOT EXISTS
# is a NO-OP and leaves the OLD password in place, while step 4 below writes
# config files holding the NEW one. The installation would look successful and
# then fail to authenticate, at some later moment, for a reason nobody could
# see from the output.
#
# The ALTER belongs here rather than in 0001 because that file is also run by
# hand against a genuinely fresh instance (installation-guide S3.2), where
# CREATE USER is the honest statement of intent.
# ---------------------------------------------------------------------------
$alignPasswords = @"
ALTER USER ``merch_migrator``@``localhost`` IDENTIFIED BY '$migratorPassword';
ALTER USER ``merch_api``@``localhost``      IDENTIFIED BY '$apiPassword';
ALTER USER ``merch_backup``@``localhost``   IDENTIFIED BY '$backupPassword';
FLUSH PRIVILEGES;
"@

Invoke-RootSql -Sql $alignPasswords -Description 'Password reconciliation' | Out-Null
Write-Ok 'Account passwords reconciled with the generated values'

# ===========================================================================
# 4. CONFIGURATION FILES
# ===========================================================================
Write-Step '4. Writing ACL-protected configuration'

New-Item -ItemType Directory -Force -Path $ConfigRoot | Out-Null

# Inheritance is broken and every inherited rule dropped, so a permissive ACL
# further up ProgramData cannot grant Users read access to a file holding a
# database password. Administrators and SYSTEM only.
$acl = Get-Acl -Path $ConfigRoot
$acl.SetAccessRuleProtection($true, $false)
foreach ($existing in @($acl.Access)) { $acl.RemoveAccessRule($existing) | Out-Null }
foreach ($account in @('BUILTIN\Administrators', 'NT AUTHORITY\SYSTEM')) {
    $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new(
        $account, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
}
Set-Acl -Path $ConfigRoot -AclObject $acl
Write-Ok "ACL applied to $ConfigRoot (Administrators and SYSTEM only)"

function Write-DatabaseConfig {
    param([string] $Path, [string] $UserId, [string] $Password)

    @{
        host     = '127.0.0.1'
        port     = 3306
        database = 'merchandising'
        userId   = $UserId
        password = $Password
    } | ConvertTo-Json | Set-Content -Path $Path -Encoding utf8
}

Write-DatabaseConfig -Path $databaseConfigPath -UserId 'merch_api'      -Password $apiPassword
Write-DatabaseConfig -Path $migratorConfigPath -UserId 'merch_migrator' -Password $migratorPassword
Write-DatabaseConfig -Path $backupConfigPath   -UserId 'merch_backup'   -Password $backupPassword
Write-Ok 'database.json, database.migrator.json and database.backup.json written'

# The API service account must NOT be able to read the backup credential.
# merch_backup can SELECT every row in the schema, including every password
# hash, and the process most exposed to the network has no reason to hold
# that. The config directory grants MerchandisingApi read access because
# the API genuinely needs database.json; this one file is carved out of
# that inheritance deliberately (P1-17).
if (Test-Path $backupConfigPath) {
    & icacls $backupConfigPath '/inheritance:d' | Out-Null
    & icacls $backupConfigPath '/remove:g' 'NT SERVICE\MerchandisingApi' | Out-Null
    Write-Ok 'database.backup.json ACL: API service account removed'
}

@"
Merchandising System - installation credentials
Generated $(Get-Date -Format 'u') by scripts/bootstrap.ps1 on $env:COMPUTERNAME

These passwords exist nowhere else. They were generated for THIS installation
and are not known to the author of the system (ADR-012 requirement 6).

  merch_migrator  $migratorPassword
  merch_api       $apiPassword
  merch_backup    $backupPassword

merch_migrator and merch_api are also in database.migrator.json and
database.json beside this file; the applications read those, never this.

All three accounts now have configuration files beside this one; the
applications read those, never this. database.backup.json is additionally
ACL'd so the API service account cannot read it (P1-17).

This file is inside the ACL-protected config directory. Do not copy it into
the repository, an email, or a chat message.
"@ | Set-Content -Path $credentialRecordPath -Encoding utf8

Write-Ok "Credential record written to $credentialRecordPath"

# ===========================================================================
# 5. SCHEMA
# ===========================================================================
Write-Step '5. Applying migrations as merch_migrator'

Push-Location $repoRoot
try {
    & dotnet run --project (Join-Path $repoRoot 'src\Merchandising.Maintenance\Merchandising.Maintenance.vbproj') `
                 --configuration Release -- migrate
    if ($LASTEXITCODE -ne 0) { Stop-Bootstrap 'Migration run failed.' 'The output above is from Merchandising.Maintenance. Nothing was half-applied: a failing migration rolls back and is recorded in SchemaMigrations (P1-06).' }
}
finally {
    Pop-Location
}
Write-Ok 'Schema applied'

# ===========================================================================
# 6. PER-TABLE GRANTS
# ===========================================================================
Write-Step '6. Granting merch_api its per-table writes (db/grants/0002)'

Invoke-RootSql -Sql (Get-Content -Path $tableGrantsSqlPath -Raw) -Description 'db/grants/0002_post-migration-grants.sql' | Out-Null
Write-Ok 'Per-table grants applied'
Write-Info 'stockmovements and auditlogs received INSERT and nothing else. That omission IS the append-only guarantee.'

# ===========================================================================
# 7. VERIFY THE GUARANTEE
#
# The append-only claim is the load-bearing one in this system, so the
# install proves it rather than asserting it. Both statements below must be
# REFUSED. A bootstrap that reported success on a database where auditlogs
# could be rewritten would be worse than no bootstrap at all.
# ===========================================================================
Write-Step '7. Verifying the append-only guarantee'

function Test-Denied {
    param([string] $Sql, [string] $What)

    $arguments = @('--user', 'merch_api', "--password=$apiPassword", '--batch', '--silent', 'merchandising')
    $null = $Sql | & $mysqlExe @arguments 2>&1

    if ($LASTEXITCODE -eq 0) {
        Stop-Bootstrap "$What was ALLOWED. The append-only guarantee is not in force on this installation." `
                       'Check that db/grants/0002 ran as root and that no extra GRANT has widened merch_api. See ADR-013.'
    }

    Write-Ok "$What refused, as required"
}

Test-Denied -Sql 'UPDATE auditlogs SET Action = ''tampered'';'  -What 'UPDATE on auditlogs'
Test-Denied -Sql 'DELETE FROM stockmovements;'                  -What 'DELETE on stockmovements'

# ===========================================================================
# 8. CERTIFICATE
# ===========================================================================
if ($SkipCertificate) {
    Write-Step '8. Certificate - skipped (-SkipCertificate)'
} else {
    Write-Step '8. Generating the HTTPS certificate'
    & (Join-Path $PSScriptRoot 'create-dev-certificate.ps1')
    if ($LASTEXITCODE -ne 0) { Stop-Bootstrap 'Certificate generation failed.' 'See scripts/create-dev-certificate.ps1.' }
    Write-Ok 'Certificate generated and certificate.json written'
}

# ===========================================================================
# DONE
# ===========================================================================
Write-Host "`n=== Installed ===" -ForegroundColor Green
Write-Host @"

The database, accounts, schema, grants and certificate are in place, and the
append-only guarantee was proven rather than assumed.

WHAT IS LEFT, and why this script does not do it:

  1. Name resolution. Add MERCH-HOST to this machine's hosts file. It is a
     per-network decision and the certificate is name-only (ADR-011), so
     clients MUST reach the API by name. See docs/installation-guide.md S1.

  2. Firewall. Run scripts/configure-firewall-dev.ps1 from an elevated
     session to open TCP 8443 on the local subnet only.

  3. Client trust. Each client imports merch-host.cer into its Trusted Root
     store. See docs/installation-guide.md S4.2.

  4. A user to log in as, and something to sell:

       dotnet run --project src/Merchandising.Maintenance -- create-user <name> <password> InventoryClerk
       dotnet run --project src/Merchandising.Maintenance -- seed-demo <name>

Verify the whole thing with:

    pwsh ./scripts/run-tests.ps1

"@ -ForegroundColor Gray

exit 0
