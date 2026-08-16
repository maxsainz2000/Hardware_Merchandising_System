# Environment Manifest

**Purpose.** Record the exact environment the system is built and accepted on. Closes gap G-30.

The spec is explicit that "works on the developer's machine" is not acceptance. This file is what makes that testable — every version below must be captured from the **actual** classroom machines, not assumed.

**Status:** ⬜ Incomplete — every item that can be captured **without a second physical machine** is now captured. What remains needs either a client laptop or a decision only Max can make. See §5 for exactly which.
**Last updated:** 2026-08-15 (P0-07 close-out)

---

## 1. Development machine

| Item | Value | How to capture |
|---|---|---|
| Windows edition | Windows 11 Home Single Language | `Get-CimInstance Win32_OperatingSystem` |
| Windows build | 10.0.26200 | `Get-CimInstance Win32_OperatingSystem` |
| Architecture | 64-bit (x64) | `Get-CimInstance Win32_OperatingSystem` |
| Visual Studio version | Visual Studio Community 2026, 18.7.1+11911.148 | `vswhere -all -products *` |
| VS workloads installed | **.NET desktop development: ✅ installed** (`Microsoft.VisualStudio.Workload.ManagedDesktop`). **ASP.NET and web development: ✅ installed** (`Microsoft.VisualStudio.Workload.NetWeb`) — added after the initial P0-01 pass, re-verified. Both workloads now confirmed present. | `vswhere -requires <workloadId>`, cross-checked against `_Instances\d1e3d03c\state.json` (now lists `CoreEditor`, `ManagedDesktop`, `NetWeb`) |
| .NET SDK version | 10.0.301 (commit 96856fd726) | `dotnet --info` |
| .NET runtimes present | AspNetCore.App 8.0.28 / 9.0.17 / **10.0.9**; NETCore.App 8.0.28 / 9.0.17 / **10.0.9**; WindowsDesktop.App 8.0.28 / 9.0.17 / **10.0.9** — a .NET 10 ASP.NET Core runtime and a .NET 10 Desktop runtime are both present | `dotnet --list-runtimes` |
| Git version | git version 2.53.0.windows.3 | `git --version` |
| PowerShell version | 7.6.4 | `$PSVersionTable.PSVersion` |

**Attach:** full `dotnet --info` output at `evidence/phase-0/dotnet-info-dev.txt`.

---

## 2. Host laptop (API + database)

| Item | Value | How to capture |
|---|---|---|
| Machine name | `LAPTOP-3HH6OHHE` — this is the dev machine, also serving as the host laptop for this solo prototype | `hostname` |
| Windows edition + build | Windows 11 Home Single Language, build 26200 (10.0.26200), 64-bit (x64) | `systeminfo` |
| Reserved LAN IP | `192.168.100.165/24` — **static address on the Wi-Fi adapter, not a router reservation.** `PrefixOrigin=Manual`, `SuffixOrigin=Manual`, `AddressState=Preferred`, DHCP disabled on the interface. ⚠️ Conflict risk not yet cleared — see §4 | `Get-NetIPAddress` |
| Host name for clients | `MERCH-HOST` | Chosen — must match the certificate SAN |
| Name resolution method | **hosts file** (no DNS server on this LAN). Procedure written up in `docs/installation-guide.md` §1. **Not yet applied on any machine, including this host** — needs an elevated shell | Per machine |
| .NET runtimes present | AspNetCore.App 8.0.28 / 9.0.17 / **10.0.9**; NETCore.App 8.0.28 / 9.0.17 / **10.0.9**; WindowsDesktop.App 8.0.28 / 9.0.17 / **10.0.9** — same machine as §1 | `dotnet --list-runtimes` |
| ASP.NET Core runtime present | **Yes — 10.0.9** | `dotnet --list-runtimes` |
| .NET Desktop Runtime present | **Yes — 10.0.9** | `dotnet --list-runtimes` |
| XAMPP version | 8.2.12-0, Windows x64 | `properties.ini` → `base_stack_version` |
| XAMPP install path | `C:\xampp` | — |
| XAMPP components disabled (Apache, FileZilla, Mercury, Tomcat) | Stopped via Control Panel GUI; confirmed not running (only `mysqld.exe`), no autostart registry/Startup-folder entries, ports 80/443/21/25/110/8080 not listening | `evidence/phase-0/xampp-services.txt` |
| MariaDB server version | `10.4.32-MariaDB` (Win64/AMD64) | `mysqld.exe --version` — **`mariadb.exe` does not exist in this build** |
| MariaDB config file path | `C:\xampp\mysql\bin\my.ini` | XAMPP Control Panel → MySQL → Config |
| MariaDB data directory | `C:/xampp/mysql/data` | From config file |
| Dump tool available | **`C:\xampp\mysql\bin\mysqldump.exe`**, `Ver 10.19 Distrib 10.4.32-MariaDB, for Win64 (AMD64)`. **No `mariadb-dump.exe` in this distribution** | `mysqldump --version` |
| MariaDB bind address | `127.0.0.1` (loopback) | From config file |
| MariaDB port | `3306` | From config file |
| MariaDB collation / engine | Server default is `utf8mb4_general_ci` — **not** what we want. Pinned value is `utf8mb4_unicode_ci` on InnoDB, stated explicitly per object (ADR-003) | `SELECT @@collation_server` |
| MariaDB `sql_mode` | `STRICT_TRANS_TABLES,NO_ZERO_IN_DATE,NO_ZERO_DATE,NO_ENGINE_SUBSTITUTION` — fixed at P1-04 (was `NO_ZERO_IN_DATE,NO_ZERO_DATE,NO_ENGINE_SUBSTITUTION` as shipped; `STRICT_TRANS_TABLES` absent, silent truncation demonstrated at P0-07). Per-connection belt-and-braces still owed by P1-05 (ADR-003.2) | `SELECT @@sql_mode` |
| Other databases on this server | `merchsys_central` exists and is **not part of this project** — pre-existing, origin unknown | `SHOW DATABASES` |
| Backup directory path | **`C:\MerchandisingBackups`** — outside the repo, outside `C:\xampp`, outside any application binary directory. Created and write-proven at P0-07 | — |
| Backup directory ACL | Inheritance **disabled**; explicit rules only: `NT AUTHORITY\SYSTEM` FullControl, `BUILTIN\Administrators` FullControl, `LAPTOP-3HH6OHHE\Admin` Modify. The inherited `Authenticated Users: Modify` was **removed** — a dump contains every password hash | `Get-Acl` |
| Off-host backup destination | ⬜ **not chosen** — needs a physical drive Max selects. See §5 | — |
| API HTTPS port | 8443 | Chosen |
| Firewall rule for API port | ⬜ not yet created — belongs to P1-09. Note the active **Tailscale** interface (§4) when scoping "private subnet only" | `netsh advfirewall firewall show rule` |

**Attach:**

- `evidence/phase-0/xampp-services.txt` — only MariaDB running (PowerShell process/service/port evidence substituted for a screenshot, by agreement)
- `evidence/phase-0/mariadb-config.txt` — bind address and port
- `evidence/phase-0/dotnet-info-host.txt`

---

## 3. Client laptops

Copy this block once **per client machine**. Every client is part of the tested system, not an assumption.

**Status as of P0-02 (2026-08-15): no client laptops are provisioned yet.** This is a solo-developer prototype at this stage; the blocks below remain unfilled placeholders until physical/virtual client machines exist. Do not tick the P0-02 client acceptance boxes until real machines are captured here — a copy of the host row is not a substitute.

### Client 1 — _(role: Procurement / Inventory / POS)_

| Item | Value |
|---|---|
| Machine name | _(record)_ |
| Windows edition + build | _(record)_ |
| Architecture | _(expect x64)_ |
| Screen resolution | _(record — baseline target is 1366×768)_ |
| Display scaling | _(record — must remain usable at 125%)_ |
| .NET Desktop Runtime | _(version, or "not yet installed")_ |
| Resolves `MERCH-HOST` | ⬜ verified — attach `ping` output |
| Reaches API over HTTPS | ⬜ verified at task P1-09 |
| **Cannot** reach MariaDB port | ⬜ verified at task P1-10 |
| Certificate trusted | ⬜ verified at task P1-09 |

### Client 2 — _(duplicate the block)_

### Client 3 — _(duplicate the block)_

---

## 4. Network

| Item | Value |
|---|---|
| Network type | Wi-Fi (network **"HUAWEI-5G-fP2f 2"**) — solo-prototype stand-in for the eventual private store LAN |
| Subnet | `192.168.100.0/24`, gateway `192.168.100.1` (MAC `20-53-83-04-99-D9`, reachable, ~3 ms) |
| Router / AP model | Huawei (exact model not identified). Admin UI confirmed reachable at **`http://192.168.100.1`** — port 80 open, 443 and 8080 closed |
| Host IP reservation method | Static IP configured directly on the host's Wi-Fi adapter (`192.168.100.165/24`, DHCP disabled) via elevated PowerShell — not a router-side DHCP reservation |
| ⚠️ **Address conflict risk** | **OPEN — the single most likely demo-day failure.** The static address is outside the router's knowledge. Live neighbours observed at `.1, .6, .74, .83, .149, .174, .175, .187, .191` — spanning **both sides** of `.165` and reaching `.191`, all with randomised (locally-administered) MACs typical of phones cycling through a DHCP pool. A pool that has issued `.191` very likely includes `.165`. Windows duplicate-address detection reported `Preferred` at assignment time, so there is no conflict *right now* — that is not a guarantee for later. **Max must check the pool range in the router admin UI** and then move the host IP out of the pool, shrink the pool, or convert to a MAC reservation. Belongs to P0-05 |
| Additional network interface | **Tailscale tunnel active — `100.76.155.51/32`.** A second path into and out of the host that is not the store LAN. P1-09 firewall scoping and the P1-10 negative tests must account for it, or those tests prove less than they appear to |
| Portability | ⚠️ **Nothing in this section transfers.** Subnet, gateway, host address, hosts-file entries and any router reservation are all bound to `HUAWEI-5G-fP2f 2` and must be redone on the classroom or store network. P0-05 will need re-running at deployment |
| Internet access required | No — the system is LAN-only |

---

## 5. Verification checklist

Phase 0 is not complete until every box is ticked. A box is ticked only when the check was actually executed and its output exists on disk.

- [x] Dev machine captured, with `dotnet --info` attached — `evidence/phase-0/dotnet-info-dev.txt`; Visual Studio 2026 and both required workloads independently confirmed via `vswhere -requires` at `evidence/phase-0/p0-07-visual-studio-workloads.txt`
- [x] Host laptop captured, with MariaDB version and dump tool confirmed — `evidence/phase-0/p0-07-mariadb-10.4-constraints.txt`
- [ ] **Every** client laptop captured — no "same as above" shortcuts → **blocked: no client laptop exists** (P0-02)
- [ ] `ping MERCH-HOST` succeeds from every client, output attached → **blocked: no client laptop exists** (P0-05). The host's own hosts entry also still needs an elevated shell — see `docs/installation-guide.md` §1.5
- [x] XAMPP stripped to MariaDB only — `evidence/phase-0/xampp-services.txt` (PowerShell process/service/port output substituted for a screenshot, by agreement). *Cross-machine phpMyAdmin unreachability remains unverified — needs a client (P0-03).*
- [x] MariaDB bound to loopback, config excerpt attached — `evidence/phase-0/mariadb-config.txt`
- [ ] Backup directory **and** off-host destination chosen and writable → **half done.** `C:\MerchandisingBackups` created, ACL-restricted and write-proven (`evidence/phase-0/p0-07-network-and-backup.txt`); the **off-host destination is not chosen** and needs a physical drive Max selects. The box stays unticked because the requirement has two halves.
- [x] Versions transferred into `docs/adr.md` ADR-002 — every row populated except the MySqlConnector package, which legitimately belongs to P1-05. ADR-003 is now ACCEPTED with the measured MariaDB 10.4 constraints.

**Two additions to this checklist, found at P0-07 and not anticipated when it was written:**

- [ ] `sql_mode` includes `STRICT_TRANS_TABLES`, server-side **and** per connection → **half done.** Server-side half fixed at P1-04 (`my.ini`, restarted, re-verified — `evidence/phase-1/p1-04-grants.txt`). The per-connection half (belt-and-braces against a future XAMPP reinstall reverting it) is still owed by P1-05, per ADR-003.2.
- [ ] Host static IP confirmed **outside** the router's DHCP pool → **not done, needs router admin access.** See §4.

---

## 6. Change log

Any change to this environment after Phase 0 goes through a tested change procedure and is recorded here (spec §17, patch policy).

| Date | Component | From | To | Reason | Retested |
|---|---|---|---|---|---|
| 2026-08-15 | MariaDB `bind-address` | unset (wildcard `::`) | `127.0.0.1` | P0-04 — the database must not be reachable from the LAN | ✅ restarted; `Get-NetTCPConnection` confirmed loopback only |
| 2026-08-15 | Host Wi-Fi adapter IPv4 | DHCP | static `192.168.100.165/24` | P0-05 — a stable address for the certificate SAN and client config | ✅ gateway reachable 2/2 |
| 2026-08-15 | `C:\MerchandisingBackups` | did not exist | created; ACL inheritance disabled, `Authenticated Users: Modify` removed | P0-07 — backups contain every password hash and must not be world-modifiable | ✅ create/read/delete proven after the ACL change |
| 2026-08-15 | `.claude/hooks/stop-guardrails.ps1`, `check-vbproj.ps1` | captured guardrail output with `2>&1` | `*>&1` | P0-07 — `Write-Host` goes to the information stream, so the hooks blocked with an **empty** failure message | ✅ re-run against a planted violation; failing guardrail and path now named |
| 2026-08-15 | `scripts/install-hooks.ps1` | `Set-Content` | LF-normalised `WriteAllText` + shebang assertion | P0-07 — `.gitattributes` marks `*.ps1` as `eol=crlf`, which would have produced a CRLF shebang and silently disabled the git hook | ✅ hook reinstalled, 0 CRLF, blocked a `.cs` commit |
| 2026-08-16 | `C:\xampp\mysql\bin\my.ini` `sql_mode` | `NO_ZERO_IN_DATE,NO_ZERO_DATE,NO_ENGINE_SUBSTITUTION` | `STRICT_TRANS_TABLES,NO_ZERO_IN_DATE,NO_ZERO_DATE,NO_ENGINE_SUBSTITUTION` | P1-04 / ADR-003.2 — server was silently truncating over-long strings and rounding over-precision decimals while reporting success | ✅ restarted (`mysqladmin shutdown` + `mysql_start.bat`), re-ran the ADR-003.2 truncation demo: now `ERROR 1406 (22001)` instead of a silent accept — `evidence/phase-1/p1-04-grants.txt` |
| 2026-08-16 | MariaDB accounts | none for this project (only pre-existing, unrelated `%`-host accounts) | `merchandising` database created (`utf8mb4`/`utf8mb4_unicode_ci`); `merch_api`@`localhost` (DML + CREATE/ALTER/INDEX/REFERENCES, no DROP) and `merch_backup`@`localhost` (`SELECT, LOCK TABLES, SHOW VIEW, EVENT, TRIGGER`) created | P1-04 — least-privilege API and backup accounts, root used by no application | ✅ `merch_api` denied `DROP DATABASE`; `merch_backup` denied `INSERT`/`UPDATE`/`DELETE`/`CREATE`, `SELECT` succeeds — `evidence/phase-1/p1-04-negative-tests.txt` |
