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

**The host address is an install-time value, not a constant of this system (ADR-012).** It differs between the development lab and the demo rig, and a value copied from one into the other resolves to the wrong machine instead of failing cleanly — which is far harder to diagnose under time pressure. Fill this table in per installation.

| Item | Value |
|---|---|
| Host name | `MERCH-HOST` — **constant.** The certificate SAN is issued for this name, so it never changes between environments |
| Host IP address | ⬜ **per install.** Lab: currently `192.168.100.165`, a DHCP lease (see below). Demo rig: to be fixed once the equipment exists — environment manifest §4.2 |
| Hosts file path | `C:\Windows\System32\drivers\etc\hosts` |
| Required rights | **Administrator.** The file is not writable by a standard user. **Confirm each demo workstation's owner actually has local admin** — without it, neither this step nor certificate trust can be completed on their machine |

> ⚠️ **Why the host name is constant but the address is not.** `MERCH-HOST` is what the HTTPS certificate is issued for, so clients must always connect by name. The name-to-address mapping is what changes per environment, and the hosts file is where that change is absorbed. This is the whole reason the manual hosts step is worth its inconvenience — it keeps one certificate valid across every network the system runs on. See ADR-011.

> ⚠️ **The lab address is a DHCP lease and may move without warning.** On 2026-08-17 a manual static was reverted after it followed the laptop onto another network and broke it; on 2026-08-18 the router happened to lease back the very same `192.168.100.165`, with about 24 hours of lifetime. **That coincidence is a trap, not a convenience** — it will keep working right up until a lease expiry or a router reboot moves it, and the resulting failure presents as a certificate or firewall problem rather than an addressing one. Re-check the address before relying on it; never copy it onto a demo machine.

> ⚠️ **Never configure the host by manual static IP.** A Windows static IPv4 is a property of the *adapter*, not of a network profile, so it follows the machine onto every network it joins and breaks all of them. On the demo rig, set the address on the *router* — equipment we control — and leave every adapter on DHCP. Evidence: `evidence/phase-0/host-ip-reservation.txt` §2.

### 1.2 Procedure — run once per machine, host and every client

Open **PowerShell as Administrator** (right-click → *Run as administrator*). Set `$HostIp` to **this installation's** host address from §1.1 — do not paste an address carried over from another environment:

```powershell
$HostIp = "<the API host's address on THIS network>"
Add-Content -Path "$env:SystemRoot\System32\drivers\etc\hosts" -Value "`n# Merchandising System - API host (P0-05)`n$HostIp`tMERCH-HOST"
```

If you prefer to edit by hand: open `C:\Windows\System32\drivers\etc\hosts` in Notepad **started as administrator**, and append this line — the separator must be a tab or spaces, not a comma:

```
<host-ip>	MERCH-HOST
```

### 1.3 Verify — do not skip this

```powershell
ping MERCH-HOST
```

Expected: replies from the address you entered. Then confirm the name resolves through the hosts file rather than by luck — and **check the address in the output matches what you intended**, because a stale entry higher up the file resolves silently to the wrong machine:

```powershell
Resolve-DnsName MERCH-HOST
```

Capture the `ping` output to `evidence/phase-0/ping-<machine-name>.txt`. **Running this on the host proves only that the host can talk to itself.** P0-05 needs it to succeed from each *demo workstation*; a run between two lab machines is worth doing earlier, since it unblocks P1-09/P1-10/P1-15, but it does not close the card (ADR-012).

### 1.4 If it does not resolve

| Symptom | Cause | Fix |
|---|---|---|
| `Ping request could not find host MERCH-HOST` | The entry was written to a copy, or Notepad was not elevated and silently saved elsewhere | Re-open elevated; confirm the line is really in `C:\Windows\System32\drivers\etc\hosts` |
| Resolves but no reply | Host firewall, or the two machines cannot reach each other at layer 2 | Confirm both are on the **same** network and subnet — by network profile name, not by address, since two different networks can share a subnet (it has already happened once on this project) |
| Resolves, no reply, and both machines are demonstrably on the same subnet | **AP client isolation** on the access point — common on school, campus and guest Wi-Fi. Each station reaches the internet; none can reach another | Not fixable without admin access to that equipment. **Use the self-provided demo rig** — environment manifest §4.2. This is the failure this project expects to meet at the venue |
| Resolves to a different address | A stale entry already exists further up the file | Remove the older `MERCH-HOST` line — the first match wins |

### 1.5 Current status

**Lab** — development and integration testing only, not part of the deliverable:

| Machine | Entry added | `ping MERCH-HOST` verified |
|---|---|---|
| Lab host `LAPTOP-3HH6OHHE` | ✅ **yes** — `192.168.100.165  MERCH-HOST` at line 25 of the hosts file | ✅ resolves and replies, verified 2026-08-18 |
| Lab test workstation `DESKTOP-OUU3M8J` | ❌ not yet — machine not yet on the LAN | ❌ |

**Demo environment** — the deliverable. Nothing here can be done until the demo rig exists (manifest §4.2):

| Machine | Entry added | `ping MERCH-HOST` verified |
|---|---|---|
| Demo host | ❌ — rig not yet acquired | ❌ |
| Demo workstation 1 | ❌ | ❌ |
| Demo workstation 2 | ❌ | ❌ |
| Demo workstation 3 | ❌ | ❌ |

> **Correction, 2026-08-18.** This table previously recorded the host entry as *not applied*, on the basis that the P0-07 agent session could not elevate. It had in fact been applied out of band. Found by running `scripts/capture-client-baseline.ps1` on the host as a smoke test — `Resolve-DnsName MERCH-HOST` returned `192.168.100.165` and ping replied. The document was wrong, not the machine.
>
> **This still does not close P0-05.** Resolution on the host proves only that the host can find itself. Under ADR-012 the card closes when each **demo workstation** resolves `MERCH-HOST` on the demo LAN — not when a lab machine does.

---

## 2. Still to be written

These sections are owned by later tasks and are deliberately absent rather than stubbed with guesses:

| Section | Owed by |
|---|---|
| MariaDB accounts, grants, and `sql_mode` hardening | P1-04 — **accounts and grants now scripted, see §3.2** |
| Certificate installation and client trust procedure | P1-09 — **written, see §4. Cross-machine confirmation still open** |
| Windows Service registration and recovery settings | P1-16 |
| Backup schedule, retention, and off-host destination | P1-17 |
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
