# Environment Manifest

**Purpose.** Record the exact environment the system is built and accepted on. Closes gap G-30.

The spec is explicit that "works on the developer's machine" is not acceptance. This file is what makes that testable — every version below must be captured from the **actual** classroom machines, not assumed.

**Status:** ⬜ Incomplete — **no longer hardware-blocked.** A second physical machine now exists (`DESKTOP-OUU3M8J`, Client 1, §3) but has not yet been powered on and measured. What remains needs the machine on the LAN, a router DHCP reservation, and two screenshots — see §5.
**Last updated:** 2026-08-18 (client machine acquired; P0-05 addressing corrected)

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
| Reserved LAN IP | **NONE — reverted to DHCP on 2026-08-17.** The static `192.168.100.165/24` was removed because a static address binds to the *adapter*, not to a network, and blocked all connectivity on every other Wi-Fi the laptop joined. **Current lease (2026-08-18, back on `HUAWEI-5G-fP2f 2`): `192.168.100.165/24` — the router handed back the very address the static used, but as a ~24 h lease (`PrefixOrigin=Dhcp`, `ValidLifetime 22:40`), not a reservation.** Treat that as a trap, not as good news: it makes the stale hardcoded `.165` in `installation-guide.md` §1.1 pass today and fail after any lease expiry or router reboot. (Earlier office lease was `192.168.100.123/24`.) **A stable host address is still required and is still owed by P0-05** — the approved method is now a **router-side MAC DHCP reservation**, not a manual static. See §4 | `Get-NetIPAddress` |
| Host name for clients | `MERCH-HOST` | Chosen — must match the certificate SAN |
| Name resolution method | **hosts file** (no DNS server on this LAN). Procedure written up in `docs/installation-guide.md` §1. **Applied on the host** (`192.168.100.165  MERCH-HOST`, hosts file line 25) and verified resolving 2026-08-18 — this corrects an earlier claim that it had never been applied anywhere. **Not applied on any client**, and must not be until the host address is durable | Per machine |
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

**Status as of 2026-08-18: the first client machine exists but is not yet captured.** A Windows 10 desktop, `DESKTOP-OUU3M8J`, is designated **Client 1** — wired by Ethernet to the same Huawei router. Role split confirmed the same day: the laptop stays dev + API host + MariaDB, the desktop is a pure client. Nothing migrates, so P0-03, P0-04 and P1-04 are not re-opened.

The block below is still **unfilled**, because the machine has not been powered on and measured. Capture it by running `scripts/capture-client-baseline.ps1` on the desktop — that script gathers every field here plus the P0-03 and P1-04 cross-machine negative tests in one pass. Do not tick the P0-02 client acceptance boxes from anything other than real measured output; a copy of the host row is not a substitute.

> ⚠️ **Two things to check first, both of which can invalidate the plan.**
> 1. **Windows 10 build and architecture.** The project targets `win-x64` and .NET 10. A 32-bit OS cannot run the published output at all. Windows 10 also passed end-of-support in October 2025 — acceptable for an academic prototype, but it belongs here as a recorded fact rather than a discovered one.
> 2. **Wired/wireless isolation.** The client is on Ethernet, the host on Wi-Fi. Consumer routers usually bridge the two, but not always. If the client cannot ping the host, every downstream test is meaningless until that is fixed — the capture script gates on this explicitly.

### Client 1 — `DESKTOP-OUU3M8J` — _(role: Procurement / Inventory / POS — not yet assigned)_

| Item | Value |
|---|---|
| Machine name | `DESKTOP-OUU3M8J` — known from the tailnet registration; **confirm on the machine**, not from this note |
| Windows edition + build | _(record — Windows 10, exact edition/build/DisplayVersion outstanding)_ |
| Architecture | _(expect x64 — **verify**, this is a hard gate)_ |
| Screen resolution | _(record — baseline target is 1366×768; a desktop monitor will not exercise it)_ |
| Display scaling | _(record — must remain usable at 125%)_ |
| .NET Desktop Runtime | _(expect "not yet installed" — needs 10.0.9 x64 before P1-15. Runtime only: no SDK, no XAMPP, ever)_ |
| LAN connection | Ethernet to the Huawei router — MAC to be recorded |
| Tailscale | Registered as `100.69.76.37`, **offline, last seen ~25 d before 2026-08-18**. Not the store LAN — see §4 |
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
| Host IP reservation method | **DHCP (no reservation yet).** The manual static was reverted on 2026-08-17 — see the Portability row and the change log. **Approved method going forward: a router-side MAC-based DHCP reservation**, which yields a stable address without breaking the adapter on other networks, and simultaneously removes the pool-conflict risk below because the router then knows the address is spoken for. Owed by P0-05 |
| ⚠️ **Address conflict risk** | **Still OPEN as a question, but no longer live**, since no manual address is currently assigned. Live neighbours observed at `.1, .6, .74, .83, .149, .174, .175, .187, .191` — spanning **both sides** of `.165` and reaching `.191`, all with randomised (locally-administered) MACs typical of phones cycling through a DHCP pool. A pool that has issued `.191` very likely includes `.165`. **Max must still check the pool range in the router admin UI** before choosing the reservation address; picking one inside the pool via reservation is fine, picking one inside the pool via static is not. Belongs to P0-05 |
| ⚠️ **Subnet collision with the OJT office network** | **Discovered 2026-08-17 and materially affects deployment planning.** The DILG-Aparri office Wi-Fi (profile **"LNB"**) uses **the same `192.168.100.0/24` subnet and the same `192.168.100.1` gateway** as the home Huawei network. Consequences: (1) the office failure was almost certainly *not* a subnet mismatch — the subnet matched exactly — but either an address conflict at `.165` or, more likely on managed government-office gear, DHCP snooping / IP source guard dropping traffic from an address the network never leased; (2) this reinforces that **a manual static address may be silently blocked on the deployment network**, so a DHCP reservation is the only method that can be relied on; (3) two distinct networks sharing one subnet makes any Tailscale subnet-routing between them ambiguous, and makes "which `192.168.100.x` am I on?" a real question during testing — always confirm by network profile name, not by address |
| Additional network interface | **Tailscale tunnel active — `100.76.155.51/32`.** A second path into and out of the host that is not the store LAN. P1-09 firewall scoping and the P1-10 negative tests must account for it, or those tests prove less than they appear to |
| Host adapter MAC (for the reservation) | `24-EB-16-3F-82-2A` — Wi-Fi, Intel(R) Wi-Fi 6 AX101, interface index 14. This is the MAC the router DHCP reservation binds to |
| ⚠️ **Client is wired, host is wireless** | Client 1 (`DESKTOP-OUU3M8J`) reaches the router by **Ethernet**; the host is on **Wi-Fi** (`HUAWEI-5G-fP2f 2`, 5 GHz). Consumer routers normally bridge LAN and WLAN into one broadcast domain, but some isolate the 5 GHz or guest SSID. **Unverified — verify before drawing any conclusion from a cross-machine test.** If the client cannot ping the host, every P0-03 / P0-05 / P1-04 / P1-10 result is void rather than passing, because a refused connection would be proving the router's isolation rather than our configuration. `scripts/capture-client-baseline.ps1` gates on this explicitly |
| Client 1 Tailscale registration | `100.69.76.37` (`desktop-ouu3m8j`), **offline, last seen ~25 d before 2026-08-18.** Means the negative tests over Tailscale will be meaningful once it is online — a client genuinely on the tailnet, rather than one refused for want of a route |
| Portability | ⚠️ **Nothing in this section transfers — and this was proven the hard way on 2026-08-17.** Subnet, gateway, host address, hosts-file entries and any router reservation are all bound to a specific network and must be redone on the classroom or store network. P0-05 will need re-running at deployment. **Lesson recorded:** a Windows static IPv4 is a property of the *adapter*, not of a Wi-Fi profile, so it follows the laptop onto every network it joins and breaks all of them. Never configure the host by manual static again; use a router reservation, and leave the adapter on DHCP |
| Internet access required | No — the system is LAN-only |

---

## 5. Verification checklist

Phase 0 is not complete until every box is ticked. A box is ticked only when the check was actually executed and its output exists on disk.

- [x] Dev machine captured, with `dotnet --info` attached — `evidence/phase-0/dotnet-info-dev.txt`; Visual Studio 2026 and both required workloads independently confirmed via `vswhere -requires` at `evidence/phase-0/p0-07-visual-studio-workloads.txt`
- [x] Host laptop captured, with MariaDB version and dump tool confirmed — `evidence/phase-0/p0-07-mariadb-10.4-constraints.txt`
- [ ] **Every** client laptop captured — no "same as above" shortcuts → **blocked: no client laptop exists** (P0-02)
- [ ] `ping MERCH-HOST` succeeds from every client, output attached → **no longer hardware-blocked.** Client 1 (`DESKTOP-OUU3M8J`) exists as of 2026-08-18; it is not yet on the LAN. Now gated on the router MAC reservation, not on hardware — the client hosts entry must not be written against a DHCP lease. Host-side entry is applied and verified — see `docs/installation-guide.md` §1.5
- [x] XAMPP stripped to MariaDB only — `evidence/phase-0/xampp-services.txt` (PowerShell process/service/port output substituted for a screenshot, by agreement). *Cross-machine phpMyAdmin unreachability remains unverified — needs a client (P0-03).*
- [x] MariaDB bound to loopback, config excerpt attached — `evidence/phase-0/mariadb-config.txt`
- [ ] Backup directory **and** off-host destination chosen and writable → **half done.** `C:\MerchandisingBackups` created, ACL-restricted and write-proven (`evidence/phase-0/p0-07-network-and-backup.txt`); the **off-host destination is not chosen** and needs a physical drive Max selects. The box stays unticked because the requirement has two halves.
- [x] Versions transferred into `docs/adr.md` ADR-002 — every row populated except the MySqlConnector package, which legitimately belongs to P1-05. ADR-003 is now ACCEPTED with the measured MariaDB 10.4 constraints.

**Two additions to this checklist, found at P0-07 and not anticipated when it was written:**

- [ ] `sql_mode` includes `STRICT_TRANS_TABLES`, server-side **and** per connection → **half done.** Server-side half fixed at P1-04 (`my.ini`, restarted, re-verified — `evidence/phase-1/p1-04-grants.txt`). The per-connection half (belt-and-braces against a future XAMPP reinstall reverting it) is still owed by P1-05, per ADR-003.2.
- [ ] Host address made stable via a **router-side MAC DHCP reservation** (not a manual static), and the DHCP pool range confirmed in the router admin UI → **not done, needs router admin access.** The manual static was reverted on 2026-08-17 after it broke connectivity on other networks. See §4.

---

## 6. Change log

Any change to this environment after Phase 0 goes through a tested change procedure and is recorded here (spec §17, patch policy).

| Date | Component | From | To | Reason | Retested |
|---|---|---|---|---|---|
| 2026-08-15 | MariaDB `bind-address` | unset (wildcard `::`) | `127.0.0.1` | P0-04 — the database must not be reachable from the LAN | ✅ restarted; `Get-NetTCPConnection` confirmed loopback only |
| 2026-08-15 | Host Wi-Fi adapter IPv4 | DHCP | static `192.168.100.165/24` | P0-05 — a stable address for the certificate SAN and client config | ✅ gateway reachable 2/2 |
| 2026-08-17 | Host Wi-Fi adapter IPv4 | static `192.168.100.165/24` | **DHCP (reverted)** | The static address is bound to the adapter, not to a Wi-Fi profile, so it followed the laptop to the OJT office and blocked all connectivity there ("connected, no internet"). Reverted with `Set-NetIPInterface -Dhcp Enabled`, `Remove-NetIPAddress`, `Remove-NetRoute`, `Set-DnsClientServerAddress -ResetServerAddresses`. **No project artefact depended on the address** — P0-05 was already blocked pending a client laptop | ✅ office lease obtained `192.168.100.123/24`, GW/DNS `192.168.100.1`, profile "LNB", internet restored — see `evidence/phase-0/host-ip-reservation.txt` §2 |
| 2026-08-15 | `C:\MerchandisingBackups` | did not exist | created; ACL inheritance disabled, `Authenticated Users: Modify` removed | P0-07 — backups contain every password hash and must not be world-modifiable | ✅ create/read/delete proven after the ACL change |
| 2026-08-15 | `.claude/hooks/stop-guardrails.ps1`, `check-vbproj.ps1` | captured guardrail output with `2>&1` | `*>&1` | P0-07 — `Write-Host` goes to the information stream, so the hooks blocked with an **empty** failure message | ✅ re-run against a planted violation; failing guardrail and path now named |
| 2026-08-15 | `scripts/install-hooks.ps1` | `Set-Content` | LF-normalised `WriteAllText` + shebang assertion | P0-07 — `.gitattributes` marks `*.ps1` as `eol=crlf`, which would have produced a CRLF shebang and silently disabled the git hook | ✅ hook reinstalled, 0 CRLF, blocked a `.cs` commit |
| 2026-08-16 | `C:\xampp\mysql\bin\my.ini` `sql_mode` | `NO_ZERO_IN_DATE,NO_ZERO_DATE,NO_ENGINE_SUBSTITUTION` | `STRICT_TRANS_TABLES,NO_ZERO_IN_DATE,NO_ZERO_DATE,NO_ENGINE_SUBSTITUTION` | P1-04 / ADR-003.2 — server was silently truncating over-long strings and rounding over-precision decimals while reporting success | ✅ restarted (`mysqladmin shutdown` + `mysql_start.bat`), re-ran the ADR-003.2 truncation demo: now `ERROR 1406 (22001)` instead of a silent accept — `evidence/phase-1/p1-04-grants.txt` |
| 2026-08-16 | MariaDB accounts | none for this project (only pre-existing, unrelated `%`-host accounts) | `merchandising` database created (`utf8mb4`/`utf8mb4_unicode_ci`); `merch_api`@`localhost` (DML + CREATE/ALTER/INDEX/REFERENCES, no DROP) and `merch_backup`@`localhost` (`SELECT, LOCK TABLES, SHOW VIEW, EVENT, TRIGGER`) created | P1-04 — least-privilege API and backup accounts, root used by no application | ✅ `merch_api` denied `DROP DATABASE`; `merch_backup` denied `INSERT`/`UPDATE`/`DELETE`/`CREATE`, `SELECT` succeeds — `evidence/phase-1/p1-04-negative-tests.txt` |
