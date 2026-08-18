# Installation Guide

**Status:** ⬜ Partial — started at P0-07 with the host-name resolution procedure only. The rest is built up through Phases 1 and 6 (`plan.md` §7). Do not treat this as a complete installation guide yet.

**Scope note.** This system is an **academic prototype**. MariaDB is supplied through XAMPP because the course requires it (ADR-000), and XAMPP is documented by Apache Friends as intended for development environments. Nothing in this guide should be read as a production-readiness claim.

---

## 1. `MERCH-HOST` name resolution

The API is reached by name, not by IP address, because the HTTPS certificate's subject/SAN is issued for the name (ADR-011). A client that connects to `https://192.168.100.165:8443` instead of `https://MERCH-HOST:8443` will get a certificate warning even when everything is configured correctly.

There is no DNS server on this LAN, so the name is resolved with a **hosts file entry on each machine**.

### 1.1 The values

| Item | Value |
|---|---|
| Host name | `MERCH-HOST` |
| Host IP address | `192.168.100.165` — ⚠️ **currently a DHCP lease, not yet durable.** Read the two warnings below before using it. |
| Hosts file path | `C:\Windows\System32\drivers\etc\hosts` |
| Required rights | **Administrator.** The file is not writable by a standard user. |

> ⚠️ **The host address is not settled yet (P0-05 is open).** On 2026-08-17 the manual static was reverted to DHCP — a Windows static IPv4 belongs to the *adapter*, not to a Wi-Fi profile, so it followed the laptop onto other networks and broke them. On 2026-08-18 the router happened to lease back **the same `192.168.100.165`**, with `PrefixOrigin=Dhcp` and about 24 hours of lifetime left.
>
> **That coincidence is a trap, not a convenience.** The value in the table above is correct *today* and will keep working right up until a lease expiry or a router reboot silently moves it — at which point every hosts entry written from this table points at the wrong machine, and the failure looks like a certificate or firewall problem rather than an addressing one.
>
> **Do not write hosts entries from this table until the address is durable.** Durable means a **router-side MAC DHCP reservation** on the host's Wi-Fi adapter (`24-EB-16-3F-82-2A`), created in the Huawei admin UI at `http://192.168.100.1`. Never a manual static again — see the environment manifest §4 for why.

> ⚠️ **Nothing here is portable.** The address, the subnet, the reservation and every hosts entry below are bound to one specific network. On the classroom or store network all of it must be redone. See the environment manifest §4.

### 1.2 Procedure — run once per machine, host and every client

Open **PowerShell as Administrator** (right-click → *Run as administrator*), then paste:

```powershell
Add-Content -Path "$env:SystemRoot\System32\drivers\etc\hosts" -Value "`n# Merchandising System - API host (P0-05)`n192.168.100.165`tMERCH-HOST"
```

If you prefer to edit by hand: open `C:\Windows\System32\drivers\etc\hosts` in Notepad **started as administrator**, and append this line — the separator must be a tab or spaces, not a comma:

```
192.168.100.165	MERCH-HOST
```

### 1.3 Verify — do not skip this

```powershell
ping MERCH-HOST
```

Expected: replies from `192.168.100.165`. Then confirm the name resolves through the hosts file rather than by luck:

```powershell
Resolve-DnsName MERCH-HOST
```

Capture the `ping` output to `evidence/phase-0/ping-<machine-name>.txt`. **P0-05 is not complete until this succeeds from a client machine** — running it on the host proves only that the host can talk to itself.

### 1.4 If it does not resolve

| Symptom | Cause | Fix |
|---|---|---|
| `Ping request could not find host MERCH-HOST` | The entry was written to a copy, or Notepad was not elevated and silently saved elsewhere | Re-open elevated; confirm the line is really in `C:\Windows\System32\drivers\etc\hosts` |
| Resolves but no reply | Host firewall, or the two machines are on different networks/VLANs | Confirm both are on `HUAWEI-5G-fP2f 2` and on the `192.168.100.0/24` subnet |
| Resolves to a different address | A stale entry already exists further up the file | Remove the older `MERCH-HOST` line — the first match wins |

### 1.5 Current status

| Machine | Entry added | `ping MERCH-HOST` verified |
|---|---|---|
| Host `LAPTOP-3HH6OHHE` | ✅ **yes** — `192.168.100.165  MERCH-HOST` present at line 25 of the hosts file | ✅ resolves and replies, verified 2026-08-18 |
| Client 1 `DESKTOP-OUU3M8J` | ❌ not yet — machine not yet on the LAN | ❌ |

> **Correction, 2026-08-18.** This table previously recorded the host entry as *not applied*, on the basis that the P0-07 agent session could not elevate. It had in fact been applied out of band. Found by running `scripts/capture-client-baseline.ps1` on the host as a smoke test — `Resolve-DnsName MERCH-HOST` returned `192.168.100.165` and ping replied. The document was wrong, not the machine.
>
> **This still does not close P0-05.** Resolution on the host proves only that the host can find itself. The card requires resolution *from a client*, and the client entry must not be written until the host address is durable — see the warnings in §1.1.

---

## 2. Still to be written

These sections are owned by later tasks and are deliberately absent rather than stubbed with guesses:

| Section | Owed by |
|---|---|
| MariaDB accounts, grants, and `sql_mode` hardening | P1-04 |
| Certificate installation and client trust procedure | P1-09 |
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
