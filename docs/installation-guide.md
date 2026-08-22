# Installation Guide

**Status:** ⬜ Partial — started at P0-07 with the host-name resolution procedure only. The rest is built up through Phases 1 and 6 (`plan.md` §7). Do not treat this as a complete installation guide yet.

**Scope note.** This system is an **academic prototype**. MariaDB is supplied through XAMPP because the course requires it (ADR-000), and XAMPP is documented by Apache Friends as intended for development environments. Nothing in this guide should be read as a production-readiness claim.

> **Most of §3 is now automated.** `pwsh ./scripts/bootstrap.ps1` performs the database, account, credential, schema, grant and certificate steps in the one order that works, generating a password per account for that installation, and it verifies the append-only guarantee rather than assuming it. Start there; this guide remains the reference for what it does, and the authority for the three things it deliberately leaves to a human:
>
> - **§1 — name resolution.** Per-network, and the certificate is name-only.
> - **§4.2 — client certificate trust.** Per-client machine.
> - **§5 — firewall.** Needs its own elevated step.
>
> The guide is also what you read when bootstrap refuses: every check it makes maps to a section here.

---

## 1. `MERCH-HOST` name resolution

The API is reached by name, not by IP address, because the HTTPS certificate's subject/SAN is issued for the name (ADR-011). A client that connects to `https://192.168.100.165:8443` instead of `https://MERCH-HOST:8443` will get a certificate warning even when everything is configured correctly.

There is no DNS server on this LAN, so the name is resolved with a **hosts file entry on each machine**.

### 1.1 The values

**Under ADR-015 the host address is now a constant, and that is the whole point of the change.** The API host runs Windows Mobile Hotspot and *is* the access point; Windows Internet Connection Sharing pins the hosting interface at `192.168.137.1` on every Windows 10 and 11 machine. The hosts-file entry is therefore written once per client and never revisited — it is no longer an install-time value to be filled in per environment.

| Item | Value |
|---|---|
| Host name | `MERCH-HOST` — **constant.** The certificate SAN is issued for this name, so it never changes between environments |
| Host IP address | **`192.168.137.1`** — constant under ADR-015, because the host is the access point. Varies **only** in the phone-hotspot fallback, where the host is an ordinary DHCP client |
| Hosts file path | `C:\Windows\System32\drivers\etc\hosts` |
| Required rights | **Administrator.** The file is not writable by a standard user. `setup-client.ps1` checks this first and says so plainly rather than half-completing |

> **Why the name is constant and always was.** `MERCH-HOST` is what the HTTPS certificate is issued for, so clients must always connect by name. Connecting to `https://192.168.137.1:8443` produces a certificate warning even when everything else is correct. The certificate deliberately carries **no IP SAN** (ADR-011) so that it stays valid across every network the system runs on; the hosts file is where the mapping lives.

> ⚠️ **Never configure the host by manual static IP.** A Windows static IPv4 is a property of the *adapter*, not of a network profile, so it follows the machine onto every network it joins and breaks all of them. Proven on 2026-08-17. Evidence: `evidence/phase-0/host-ip-reservation.txt` §2. **ADR-015 retires this risk rather than managing it:** `192.168.137.1` is assigned by ICS to the hotspot interface only, and exists only while the hotspot is running — it is not a static address on a roaming adapter.

> **Historical, and no longer load-bearing.** The lab host previously sat at `192.168.100.165`, a ~24-hour DHCP lease that the router happened to hand back after a static address was reverted. That coincidence was a trap: it worked until a lease expiry moved it, and the resulting failure presented as a certificate or firewall problem rather than an addressing one. Recorded because the reasoning still applies to the fallback topology.

### 1.2 Procedure — one command per client

On each **client** laptop, elevated:

```powershell
powershell -ExecutionPolicy Bypass -File .\setup-client.ps1
```

That single command does the whole client side: preflight (Windows edition, build, architecture, admin rights, .NET desktop runtime, resolution and DPI scaling), imports `merch-host.cer` into Trusted Root, writes the hosts entry, verifies a real HTTPS round trip **with certificate validation on**, and writes `client-baseline-<MACHINE>.txt` to the Desktop.

**That report file is also the P0-02 evidence for that machine.** The baseline stops being something to collect from three classmates in advance and becomes an output of the setup that had to happen anyway. Nobody needs to be asked for their Windows build.

For the phone-hotspot fallback, pass the host's address explicitly:

```powershell
powershell -ExecutionPolicy Bypass -File .\setup-client.ps1 -HostIPv4 192.168.43.137
```

To check whether a laptop is suitable **without changing anything** — no Administrator needed:

```powershell
powershell -ExecutionPolicy Bypass -File .\setup-client.ps1 -CaptureOnly
```

On the **host**, before any client connects, elevated:

```powershell
pwsh ./scripts/start-demo-network.ps1
```

It starts the hotspot, confirms the interface really holds `192.168.137.1`, **reclassifies that network from Public to Private** (see §1.4 — this is the failure most likely to bite), verifies the firewall rule and that the API is listening, and prints the Wi-Fi name and password to read out.

### 1.3 Verify — do not skip this

`setup-client.ps1` stage 4 does all of this and interprets the results. To check by hand:

```powershell
ping MERCH-HOST
Resolve-DnsName MERCH-HOST
```

Expected: replies from `192.168.137.1`. **Check the address in the output matches** — a stale entry higher up the file resolves silently to the wrong machine.

**Running this on the host proves only that the host can talk to itself.** P0-05 needs it to succeed from each *demo workstation*.

### 1.4 If it does not resolve or does not connect

| Symptom | Cause | Fix |
|---|---|---|
| `Ping request could not find host MERCH-HOST` | The entry was written to a copy, or Notepad was not elevated and silently saved elsewhere | Re-run `setup-client.ps1` elevated; it rewrites the entry idempotently |
| Resolves but no reply | Client is not actually joined to the host's hotspot | Check the Wi-Fi network name on the client matches the SSID `start-demo-network.ps1` printed |
| **Resolves and pings, but every client times out on port 8443** | **The hotspot network is classified Public, and the firewall rule is Private-only.** This is the single most likely failure and it reads exactly like a bug in the API | On the host: `pwsh ./scripts/start-demo-network.ps1` — check 3 reclassifies it and re-verifies. Do **not** widen the firewall rule to the Public profile; that would open the API on every untrusted network the laptop ever joins |
| Certificate warning or TLS failure | `merch-host.cer` was never imported, or the client connected by IP instead of by name | Re-run `setup-client.ps1`; connect to `https://MERCH-HOST:8443`, never to the address |
| Resolves to a different address | A stale entry already exists further up the file | `setup-client.ps1` strips prior `MERCH-HOST` lines before writing; re-run it |
| ~~AP client isolation~~ | **Cannot occur under ADR-015.** Clients talk to their own default gateway, which *is* the host — there is no station-to-station hop for an access point to block | Applies only in the phone-hotspot fallback. If it appears there, switch to host-as-access-point |

### 1.5 Current status

**Lab** — development and integration testing only, not part of the deliverable:

| Machine | Entry added | Verified |
|---|---|---|
| Lab host `LAPTOP-3HH6OHHE` | ✅ `192.168.100.165  MERCH-HOST`, hosts line 25 | ✅ resolves and replies, 2026-08-18. **Lab address — superseded for demo use by ADR-015** |
| Lab test workstation `DESKTOP-OUU3M8J` | ❌ not yet | ❌ |

**Demo environment** — the deliverable. No longer blocked on acquiring hardware; blocked on one rehearsal:

| Machine | Entry added | Verified |
|---|---|---|
| Demo host (= access point) | n/a — the host *is* `192.168.137.1` | ⬜ owed at rehearsal |
| Demo workstation 1 | ⬜ via `setup-client.ps1` | ⬜ |
| Demo workstation 2 | ⬜ via `setup-client.ps1` | ⬜ |
| Demo workstation 3 | ⬜ via `setup-client.ps1` | ⬜ |

> **What changed, 2026-08-22 (ADR-015).** This section previously required the host address to be filled in per installation, and listed AP client isolation as the failure the project expected to meet at the venue. Both are retired by making the host the access point: the address becomes a constant, and client isolation cannot apply to traffic addressed to the client's own gateway. The school Wi-Fi — heavily firewalled — is not used at all.
>
> **This still does not close P0-05.** It closes on a rehearsal: hotspot up, three clients joined and set up by someone other than the author, a real round trip from each.

---

## 2. Still to be written

These sections are owned by later tasks and are deliberately absent rather than stubbed with guesses:

| Section | Owed by |
|---|---|
| MariaDB accounts, grants, and `sql_mode` hardening | P1-04 — **accounts and grants now scripted, see §3.2** |
| Certificate installation and client trust procedure | P1-09 — **written, see §4. Cross-machine confirmation still open** |
| Windows Service registration and recovery settings | P1-16 |
| Backup schedule, retention, and off-host destination | P1-17 — **written, see §6** |
| Restore procedure and maintenance mode | P1-18 |
| Client prerequisites (.NET 10 Desktop Runtime) and client install | Phase 6 |

---

## 3. Database connection configuration file (P1-05)

The API's database credentials live in a single JSON file **on the host only**, outside the
repository and outside the published binaries. `Merchandising.Infrastructure` reads it at
startup through `DatabaseOptionsLoader`; nothing about it is ever committed (guardrail G-C).

**Path:** `C:\ProgramData\MerchandisingSystem\config\database.json`

`%ProgramData%` was chosen because it is machine-wide (not tied to a user profile that may not
exist yet on a fresh install) and is the conventional location for service configuration on
Windows. `DatabaseOptionsLoader.DefaultConfigPath` resolves it via
`Environment.SpecialFolder.CommonApplicationData`, so it does not need to be hard-coded twice.

**Contents:**

```json
{
  "host": "127.0.0.1",
  "port": 3306,
  "database": "merchandising",
  "userId": "merch_api",
  "password": "<the merch_api password generated at P1-04>"
}
```

Only `host = 127.0.0.1` is correct — `bind-address` is loopback-only (P0-04), and `merch_api`
is scoped `@'localhost'` (P1-04).

**Required ACL, applied once per host:**

```powershell
$dir = "C:\ProgramData\MerchandisingSystem\config"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$acl = Get-Acl $dir
$acl.SetAccessRuleProtection($true, $false)   # disable inheritance, drop inherited rules
$acl.Access | ForEach-Object { $acl.RemoveAccessRule($_) } | Out-Null
foreach ($principal in "BUILTIN\Administrators", "NT AUTHORITY\SYSTEM") {
    $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new(
        $principal, "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow"))
}
# Also grant FullControl to whichever account will run the API (a named user during
# development; the service account once P1-16 registers the Windows Service).
Set-Acl -Path $dir -AclObject $acl
```

The directory must **not** grant `BUILTIN\Users` or `Everyone` — verify with `icacls` after
applying. This is the only place the `merch_api` password exists outside the DBA's own record
of it; treat the file the same way as the MariaDB root password.

**Verification:** `evidence/phase-1/p1-05-connection-test.log` records a live connection
opened through this exact file, with `STRICT_TRANS_TABLES` and `READ COMMITTED` both proven on
the session.

---

## 3.1 The second configuration file — migrations (P1-06, ADR-013)

**Path:** `C:\ProgramData\MerchandisingSystem\config\database.migrator.json`

Same directory, same ACL, same shape — different account:

```json
{
  "host": "127.0.0.1",
  "port": 3306,
  "database": "merchandising",
  "userId": "merch_migrator",
  "password": "<the merch_migrator password generated at install time>"
}
```

**Why two files rather than one account.** `merch_migrator` owns the schema and is the only
account permitted to `CREATE`, `ALTER` or `DROP` a table. `merch_api` owns the data and holds
**no DDL at all** — it cannot create a table even by accident. `Merchandising.Maintenance`
loads this file during a migration run; the API never does. Passing the wrong file produces
`ERROR 1142` immediately rather than a half-applied schema. Full reasoning: ADR-013.

---

## 3.2 Database accounts and grants — run in this order

The account and privilege setup is scripted, not prose. Run as `root`, from the repository root:

| Step | Script | When |
|---|---|---|
| 1 | `db/grants/0001_accounts-and-grants.sql` | Once, on a fresh MariaDB, **before** any migration |
| 2 | migration `0001_foundation.sql`, via `Merchandising.Maintenance` | After step 1 |
| 3 | `db/grants/0002_post-migration-grants.sql` | **After** step 2 |

**The order is not a preference.** Step 3 grants `merch_api` its write privileges one table at
a time, and MariaDB 10.4 refuses a table-level `GRANT` naming a table that does not exist yet:

```
ERROR 1146 (42S02): Table 'merchandising.does_not_exist_yet' doesn't exist
```

So the per-table grants physically cannot be applied until the migration has created the
tables. Between steps 1 and 3 the API can read but not write — that is expected, not a fault.

**Before running step 1**, replace the three `{{...}}` placeholders with passwords generated
for *this* installation. Per ADR-012 requirement 6, credentials are generated per installation
and never committed: no password known to the author may be the password protecting a
classmate's demo.

**What step 3 deliberately omits is the point of step 3.** `stockmovements` and `auditlogs`
receive `INSERT` and nothing else, which is what makes the ledgers append-only by privilege
rather than by discipline (CLAUDE.md §5). Verify after step 3 — both must fail with
`ERROR 1142`:

```powershell
& "C:\xampp\mysql\bin\mysql.exe" -u merch_api -p merchandising -e "UPDATE auditlogs SET Actor='x';"
& "C:\xampp\mysql\bin\mysql.exe" -u merch_api -p merchandising -e "DELETE FROM stockmovements;"
```

> **On this lab host, MariaDB `root` has no password** — loopback-only, and the instance is
> shared with an unrelated project. **On a demo install this is not acceptable and must be
> set**, because that instance is fresh and single-purpose. See manifest §2.

---

## 4. HTTPS certificate and client trust procedure (P1-09, ADR-011)

**Why the certificate is name-only.** The API host in this project is a laptop that moves
between at least three networks during development — home, school, and office Wi-Fi — and will
later move again to a self-provided demo LAN (ADR-012). A certificate carrying an IP address in
its Subject Alternative Name would be valid on exactly one of those networks and produce a trust
warning on every other one, at whichever moment is least convenient. The certificate instead
carries **only** the DNS name `MERCH-HOST`, which never changes. The thing that changes per
network — the name-to-address mapping — is absorbed entirely by the hosts file (§1), which is
the whole reason that manual step earns its inconvenience. This resolves ADR-011: **decision is
a name-only SAN**, confirming the revised lean rather than the original baseline.

### 4.1 Generating the host's certificate

Run once per installation (fresh host, or a reinstalled XAMPP that lost its config directory).
Does **not** need an elevated session — see the script's own header comment for why:

```powershell
pwsh ./scripts/create-dev-certificate.ps1
```

This writes three things, none of them committed to the repository:

| File | Location | Contains |
|---|---|---|
| `certificate.json` | `C:\ProgramData\MerchandisingSystem\config\` | Path to the `.pfx` + its password. Read by `CertificateOptionsLoader` at API startup. Same ACL as `database.json` (§3). |
| `merch-host.pfx` | `C:\ProgramData\MerchandisingSystem\certs\` | Certificate **and private key**. Host only. Never copy this file anywhere. |
| `merch-host.cer` | `C:\ProgramData\MerchandisingSystem\certs\` | Certificate **only**, no private key. This is the one file every client needs. |

Record the generated certificate's subject/SAN/thumbprint as evidence — see
`evidence/phase-1/p1-09-cert-details.txt` for this installation's values, and confirm the SAN
extension shows exactly `DNS Name=MERCH-HOST` and nothing else before distributing `.cer` to any
client.

### 4.2 Client trust procedure — run once per client

Full procedure, verification steps, a failure table, and a live worked example are in
`evidence/phase-1/p1-09-client-trust-steps.md`. Summary:

1. Get `merch-host.cer` from the host administrator, **never** a `.pfx`.
2. Get the expected thumbprint out of band (phone, in person, chat) — not from the same channel
   that delivered the file. This manual comparison is the entire point of a manual trust step.
3. Import into `Cert:\LocalMachine\Root` (all users, needs admin) or `Cert:\CurrentUser\Root`
   (this account only, no admin needed) — see the evidence file for the exact commands.
4. Verify: `Invoke-WebRequest https://MERCH-HOST:8443/health` returns `200 OK` with **no**
   certificate warning and **no** `-SkipCertificateCheck`.

An untrusted client is expected to fail the TLS handshake with `RemoteCertificateChainErrors` —
that is the system working correctly, not a bug to route around.

### 4.3 The development HTTP listener

`http://127.0.0.1:8080` exists **only** when `ASPNETCORE_ENVIRONMENT=Development`, binds to
loopback only — never `0.0.0.0` — and every response from it carries an
`X-Non-Production-Http` header plus a startup console warning. It cannot be reached from another
machine regardless of firewall state, and it is never used for a demonstration. The
production-like listener is always `https://MERCH-HOST:8443`, in every environment.

### 4.4 Current status

| Machine | Certificate generated | Trust installed | `https://MERCH-HOST:8443/health` verified |
|---|---|---|---|
| Lab host `LAPTOP-3HH6OHHE` | ✅ 2026-08-19, thumbprint `759021AA...849C9` | ✅ (`CurrentUser\Root`, same machine) | ✅ via name-targeted TLS test — `evidence/phase-1/p1-09-invalid-cert-behaviour.txt` |
| Lab test workstation `DESKTOP-OUU3M8J` | — | ❌ not yet on this network | ❌ |
| Demo host / workstations | — | — | — rig not yet acquired (manifest §4.2) |

**A literal second-machine confirmation is still open**, same gap as §1.5 — the mechanism is
proven, a second physical machine following §4.2 is not yet proven.

---

## 5. Firewall — port 8443 (P1-09)

**Rule is scoped dynamically, not to a fixed subnet, for the same reason the certificate is
name-only:** this host's actual subnet changes every time it changes Wi-Fi. A rule hard-coded to
one address range would be wrong everywhere except the network it was written on.

```powershell
pwsh ./scripts/configure-firewall-dev.ps1
```

Requires an elevated (**Run as Administrator**) session — the script checks and refuses to run
otherwise rather than failing halfway through. It creates one inbound rule: TCP 8443,
`-RemoteAddress LocalSubnet` (resolves to whatever subnet the active adapter is on right now),
`-Profile Private` only. On school or office Wi-Fi — typically classified Public by Windows —
this rule simply does not apply, and Windows Firewall's own default-deny keeps 8443 closed with
no manual switching required.

**This is the development rule for this laptop, not the demo rig's firewall configuration.**
The demo host's firewall is configured once, on that hardware, at install time — a fixed subnet
is correct there in a way it is not correct here, because the demo rig (ADR-012) does not move.
That step is not yet written; it belongs in this section once the demo rig exists.

**Not yet run in this session** — this working session is not admin-elevated (same gap noted at
P0-05). Run `scripts/configure-firewall-dev.ps1` from an elevated session and verify with:

```powershell
Get-NetFirewallRule -DisplayName "Merchandising API HTTPS (dev, private networks only)" | Get-NetFirewallPortFilter
```

---

## 6. Backup (P1-17, spec §15)

### 6.1 What runs, as whom

| | |
|---|---|
| Command | `Merchandising.Maintenance.exe backup` |
| Database identity | `merch_backup` — reads everything, writes only `BackupLogs` (ADR-013.1) |
| Credentials | `%ProgramData%\MerchandisingSystem\config\database.backup.json`, written by `bootstrap.ps1` |
| Dump tool | `C:\xampp\mysql\bin\mysqldump.exe` — **`mariadb-dump.exe` does not exist in this XAMPP build** (P0-04) |
| Local destination | `C:\MerchandisingBackups` — outside the binaries, outside the repo, not served by the API |
| Scheduled task identity | `NT AUTHORITY\SYSTEM`, **not** the API service account |

**Why SYSTEM and not `NT SERVICE\MerchandisingApi`.** A dump contains every password hash in the system. The process most exposed to the network must not be able to read it. `C:\MerchandisingBackups` has inheritance disabled (P0-07) and grants SYSTEM and Administrators only — the API service account is absent from that ACL, and `database.backup.json` has it explicitly removed as well.

### 6.2 Configuration lives in `SystemSettings`, not in code

Seeded by `db/migrations/0003_backup.sql`; change them with an `UPDATE` and the next run picks them up.

| Key | Default | Meaning |
|---|---|---|
| `backup.retentionCount` | `7` | How many dumps to keep. Below 1 means *prune nothing* — a misconfigured row must not be able to delete every backup |
| `backup.offHostVolumeLabel` | `MERCHBACKUP` | Volume **label** of the off-host drive, never a drive letter |
| `backup.directory` | `C:\MerchandisingBackups` | Local destination |

**Label, not letter.** A USB stick mounts as D: here and F: on a classmate's laptop. Matching on the label is what makes one configuration correct on all three demo machines (ADR-012, ADR-015).

### 6.3 Register the nightly job — once per installation, elevated

```powershell
pwsh ./scripts/register-backup-task.ps1
```

Then **prove it runs**, because registration proves nothing about execution:

```powershell
Start-ScheduledTask -TaskName 'Merchandising Nightly Backup'
Get-ScheduledTaskInfo -TaskName 'Merchandising Nightly Backup' | Select-Object LastRunTime, LastTaskResult
```

### 6.4 Reading the result

| `LastTaskResult` | `BackupLogs.Result` | Meaning |
|---|---|---|
| `0` | `Succeeded` | Dump written, checksum verified, off-host copy made |
| `1` | `Partial` | Dump is good and kept, but the off-host copy did not happen — usually the USB stick was not plugged in. **Not a failure, but not a success either** |
| `1` | `Failed` | No usable dump. The reason is in `BackupLogs.Detail`, or in `C:\MerchandisingBackups\backup-failures.log` if the database itself was unreachable |

**A partial run deliberately exits non-zero.** A degraded backup that reports success is how a system ends up with months of dumps that only ever existed on the machine that died.

### 6.5 The USB stick is sensitive — treat it that way

exFAT carries no ACLs, so the dump on the stick is readable by anyone who plugs it in, and it contains every password hash.

Reformatting to NTFS was **considered and rejected**: ACLs on removable media are defeated by taking ownership on any machine with administrator rights, so NTFS would buy the *appearance* of protection rather than protection. The honest control is physical custody of the stick. If confidentiality ever genuinely matters, encrypt the dump — do not change the filesystem and call it solved.

### 6.6 Current status

- ✅ Backup command implemented, tested, and run end to end on this host
- ✅ `merch_backup` credential and `database.backup.json` in place, API service account excluded
- ✅ Off-host copy proven byte-identical on the `MERCHBACKUP` volume
- ⬜ **Scheduled task not yet registered** — needs one elevated command (§6.3). The agent session could not elevate; this is owed to the operator
- ⬜ Restore is P1-18

