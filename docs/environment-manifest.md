# Environment Manifest

**Purpose.** Record the exact environment the system is built and accepted on. Closes gap G-30.

The spec is explicit that "works on the developer's machine" is not acceptance. This file is what makes that testable — every version below must be captured from the **actual** classroom machines, not assumed.

**Status:** ⬜ Incomplete — **restructured 2026-08-18 by ADR-012 into lab vs demo.** §1, §2, §3.1 and §4.1 describe the author's development and test lab and are largely complete. §3.2 and §4.2 describe the environment the system is actually demonstrated on — the classmates' three workstations and a self-provided demo LAN — and **neither has been specified or captured.** That, not hardware, is what remains. See §5.
**Last updated:** 2026-08-18 (delivery model reframed — ADR-012)

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

## 2. Reference host — development and integration-test lab

> **This machine is lab equipment, not the deliverable (ADR-012).** It is where the API and database are built and proven. It is **not** "the host" of anything handed over. The demo environment runs on the classmates' machines and on a self-provided demo LAN; nothing in this section travels there. Values here are recorded so that a difference on a demo machine is *visible* rather than assumed away — they are a reference, never a requirement.

| Item | Value | How to capture |
|---|---|---|
| Machine name | `LAPTOP-3HH6OHHE` — author's development machine, serving as the lab's API + database host | `hostname` |
| Windows edition + build | Windows 11 Home Single Language, build 26200 (10.0.26200), 64-bit (x64) | `systeminfo` |
| Lab LAN address (**not a deliverable value**) | **NONE reserved — reverted to DHCP on 2026-08-17.** The static `192.168.100.165/24` was removed because a static address binds to the *adapter*, not to a network, and blocked all connectivity on every other Wi-Fi the laptop joined. **Current lease (2026-08-18, back on `HUAWEI-5G-fP2f 2`): `192.168.100.165/24` — the router handed back the very address the static used, but as a ~24 h lease (`PrefixOrigin=Dhcp`, `ValidLifetime 22:40`), not a reservation.** Treat that as a trap, not as good news: it makes the stale hardcoded `.165` in `installation-guide.md` §1.1 pass today and fail after any lease expiry or router reboot. (Earlier office lease was `192.168.100.123/24`.) Under ADR-012 this no longer needs fixing: the lab host may float on DHCP, because **no delivered artefact may contain a lab address**. A fixed address is required only on the demo rig (§4.2), where it is set directly on equipment we control. See §4 | `Get-NetIPAddress` |
| Host name for clients | `MERCH-HOST` | Chosen — must match the certificate SAN |
| Name resolution method | **hosts file** (no DNS server on this LAN). Procedure written up in `docs/installation-guide.md` §1. **Applied on the host** (`192.168.100.165  MERCH-HOST`, hosts file line 25) and verified resolving 2026-08-18 — this corrects an earlier claim that it had never been applied anywhere. **Not applied on any demo workstation**, correctly — no hosts entry should be written until the demo rig's addressing is fixed (§4.2). A lab address written onto a demo machine is worse than no entry, because it resolves to the wrong thing instead of failing | Per machine |
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
| MariaDB `sql_mode` | `STRICT_TRANS_TABLES,NO_ZERO_IN_DATE,NO_ZERO_DATE,NO_ENGINE_SUBSTITUTION` — fixed at P1-04 (was `NO_ZERO_IN_DATE,NO_ZERO_DATE,NO_ENGINE_SUBSTITUTION` as shipped; `STRICT_TRANS_TABLES` absent, silent truncation demonstrated at P0-07). **Per-connection belt-and-braces also done, at P1-05** — `ConnectionFactory` re-applies it on every connection and `ConnectionFactoryTests` asserts it live, so a reinstalled XAMPP cannot revert the guarantee (ADR-003.2, now fully discharged) | `SELECT @@sql_mode` |
| Other databases on this server | `merchsys_central` exists and is **not part of this project** — pre-existing, origin unknown | `SHOW DATABASES` |
| MariaDB application accounts | **Three, restructured 2026-08-18 (ADR-013).** `merch_migrator`@`localhost` — the only account with DDL, incl. `DROP`; used by `Merchandising.Maintenance` during a migration run and nowhere else. `merch_api`@`localhost` — **no DDL at all**, and at database level **`SELECT` only**, so every table is append-only by default; write privileges are granted per table by `db/grants/0002` after the migration creates them. `merch_backup`@`localhost` — `SELECT, LOCK TABLES, SHOW VIEW, EVENT, TRIGGER`. Reproducible from `db/grants/*.sql`, no longer prose-only | `SHOW GRANTS FOR …` |
| MariaDB `root` account | ⚠️ **No password — accepted lab-only risk, decided 2026-08-18.** Not remotely reachable: `bind-address` is loopback and Apache/phpMyAdmin are stopped (P0-03). Deliberately left as-is because this MariaDB instance also hosts `merchsys_central`, an unrelated project, and phpMyAdmin is wired to `auth_type=config` / `AllowNoPassword=true`; changing it would break that project's tooling for no gain against the ADR-012 threat model. **Setting a root password is a required step in the demo-install bootstrap**, where the instance is fresh and single-purpose. `pma`@`localhost` is likewise password-less and is XAMPP's own control user | `SELECT User,Host FROM mysql.user` |
| Other accounts on this server (**not this project**) | `merchsys_owner`@`%`, `merchsys_sync`@`%`, `merchsys_sync_role`, `vista_app`@`%` — all scoped to `merchsys_central`. Recorded so a future `SHOW GRANTS` sweep does not mistake them for ours. Note they are `%`-host accounts, unlike every account this project creates, which are `localhost`-only | `SELECT User,Host FROM mysql.user` |
| Backup directory path | **`C:\MerchandisingBackups`** — outside the repo, outside `C:\xampp`, outside any application binary directory. Created and write-proven at P0-07 | — |
| Backup directory ACL | Inheritance **disabled**; explicit rules only: `NT AUTHORITY\SYSTEM` FullControl, `BUILTIN\Administrators` FullControl, `LAPTOP-3HH6OHHE\Admin` Modify. The inherited `Authenticated Users: Modify` was **removed** — a dump contains every password hash | `Get-Acl` |
| Off-host backup destination | ⬜ **not chosen.** Under ADR-012 this is a *handover* question, not a personal one: the package must let whoever installs it choose a destination, and the backup path must therefore be configuration rather than a constant. See §5 | — |
| API HTTPS port | 8443 | Chosen |
| Firewall rule for API port | ⬜ not yet created — belongs to P1-09. **Scope it to the demo LAN's subnet, not the lab's**, and disregard the Tailscale interface entirely: ADR-012 puts it out of scope, and hardening against a path that will not attend the presentation buys nothing | `netsh advfirewall firewall show rule` |

**Attach:**

- `evidence/phase-0/xampp-services.txt` — only MariaDB running (PowerShell process/service/port evidence substituted for a screenshot, by agreement)
- `evidence/phase-0/mariadb-config.txt` — bind address and port
- `evidence/phase-0/dotnet-info-host.txt` — captured 2026-08-18. This file was listed here from the start but did not exist on disk until then; the lab host and the dev machine are the same physical machine (§1), so the two dumps agree by construction, and the file records that explicitly rather than leaving a dangling reference.

---

## 3. Workstations

**Two different populations of machine, and conflating them is the mistake this section exists to prevent (ADR-012).**

| | §3.1 Lab test workstation | §3.2 Demo workstations |
|---|---|---|
| Whose | The author's | The classmates' |
| Purpose | Prove software behaviour across a real network boundary | Run the system at the presentation |
| Proves | P1-09, P1-10, P1-15 — cert trust, port denial, WPF round trip | P0-02 acceptance |
| Part of the deliverable | **No** | **Yes** |

A second machine — any second machine — is enough to prove the *software* works across a network. It is not enough to record facts about machines that will be present at the presentation. P0-02's acceptance is about the second kind, and cannot be closed with the first.

---

### 3.1 Lab test workstation — `DESKTOP-OUU3M8J` — **not a deliverable**

Acquired 2026-08-18. A Windows 10 desktop, wired by Ethernet to the author's home router. Its value is that it is *not the build machine*: it is where "works on my laptop" gets tested against something that isn't the laptop.

Capture it with `scripts/capture-client-baseline.ps1`, which also runs the P0-03 and P1-04 cross-machine negative tests in one pass.

| Item | Value |
|---|---|
| Machine name | `DESKTOP-OUU3M8J` — from the tailnet registration; **confirm on the machine** |
| Windows edition + build | _(record — Windows 10; exact edition / build / DisplayVersion outstanding)_ |
| Architecture | _(**verify x64** — a 32-bit OS cannot run `win-x64` output at all)_ |
| Screen resolution / scaling | _(record — will **not** exercise the 1366×768 @125% baseline; that stays with the laptop)_ |
| .NET Desktop Runtime | _(expect "not installed" — needs 10.0.9 x64 before P1-15. Runtime only: no SDK, no XAMPP)_ |
| LAN connection | Ethernet to the home router — MAC to be recorded |

> ⚠️ **Verify before trusting any cross-machine result: the lab client is wired, the lab host is on 5 GHz Wi-Fi.** Consumer routers normally bridge the two; some isolate the 5 GHz or guest SSID. If the client cannot ping the host, a refused connection in the negative tests is proving the *router's* behaviour, not the system's — the results are void rather than passing. The capture script gates on this explicitly.

---

### 3.2 Demo workstations — the classmates' machines — **the deliverable**

**Status: none captured.** These are the three machines the system will actually be demonstrated on, one per role. They belong to the three classmates and have not been surveyed. **P0-02's client acceptance is about these machines and nothing else** — the desktop in §3.1 is not a substitute, and neither is a copy of the host row.

What has to be collected from each, and the reason each matters:

| Field | Why it can break the demo |
|---|---|
| Architecture | 32-bit cannot run `win-x64` output at all — a hard stop, not a degradation |
| Windows edition + build | Determines whether .NET 10 is supported at all |
| .NET Desktop Runtime | Framework-dependent WPF clients (ADR-010) fail **at launch** without 10.0.9 x64 |
| Resolution + scaling | The UI baseline is 1366×768 at 125%; a machine below that has a layout problem to find early |
| Local admin rights | Needed for the hosts entry and to trust the certificate. **A classmate without admin on their own machine is a blocker discovered late unless asked now** |

Copy the block below once per classmate. Do not fill any of it from assumption.

### Demo workstation 1 — _(classmate, role: Procurement / Inventory / POS)_

| Item | Value |
|---|---|
| Owner (classmate) | _(record)_ |
| Assigned role | _(Procurement / Inventory / POS)_ |
| Machine name | _(record)_ |
| Windows edition + build | _(record)_ |
| Architecture | _(**must be x64**)_ |
| Screen resolution | _(record — baseline 1366×768)_ |
| Display scaling | _(record — must stay usable at 125%)_ |
| .NET Desktop Runtime | _(10.0.9 x64 required before the demo)_ |
| Local administrator rights | _(**ask now** — required for hosts entry and certificate trust)_ |
| Resolves `MERCH-HOST` | ⬜ verified on the demo LAN — attach `ping` output |
| Reaches API over HTTPS | ⬜ verified at task P1-09 |
| **Cannot** reach MariaDB port | ⬜ verified at task P1-10 |
| Certificate trusted | ⬜ verified at task P1-09 |

### Demo workstation 2 — _(duplicate the block)_

### Demo workstation 3 — _(duplicate the block)_

---

## 4. Networks

**Two networks, and only one of them is part of the deliverable (ADR-012).** §4.1 is where the system is built and tested. §4.2 is where it will actually be demonstrated. Everything measured in §4.1 is a *lab fact* — useful for reproducing a test, worthless as a configuration value for anyone else.

---

### 4.1 Lab network — the author's home LAN — **not a deliverable**

| Item | Value |
|---|---|
| Network type | Wi-Fi (**"HUAWEI-5G-fP2f 2"**, 5 GHz) |
| Subnet | `192.168.100.0/24`, gateway `192.168.100.1` (MAC `20-53-83-04-99-D9`, ~3 ms) |
| Router / AP | Huawei, model not identified. Admin UI at `http://192.168.100.1` — port 80 open, 443 and 8080 closed |
| Lab host address | **DHCP lease, no reservation.** Currently `192.168.100.165/24` — the router happened to lease back the same address a reverted static once used, on a ~24 h lifetime. **A coincidence, not a configuration.** See `evidence/phase-0/host-ip-reservation.txt` §3 |
| Lab host adapter MAC | `24-EB-16-3F-82-2A` — Intel(R) Wi-Fi 6 AX101, interface index 14 |
| ⚠️ Lab client is wired, lab host is wireless | The test workstation reaches the router by Ethernet, the host by 5 GHz Wi-Fi. Consumer routers normally bridge these; some isolate the 5 GHz or guest SSID. **Unverified.** If the client cannot ping the host, every P0-03 / P1-04 / P1-10 result is **void rather than passing** — a refused connection would be proving the router's isolation, not the system's configuration. `scripts/capture-client-baseline.ps1` gates on this before running anything else |
| Tailscale (lab only) | Host `100.76.155.51/32`; test workstation registered `100.69.76.37`. **Out of project scope per ADR-012** — a personal tailnet on a personal account, present on no machine that will attend the presentation. Recorded so that a stray result over the tunnel is recognised as an artefact of the lab, not evidence about the system. It must **not** appear in P1-09 firewall scoping or the P1-10 test matrix |
| Router DHCP reservation | **No longer a project task.** Was owed by P0-05 under the old framing; superseded by §4.2, where addressing is controlled directly |
| Historical: OJT office subnet collision | The DILG-Aparri office Wi-Fi (profile "LNB") used the same `192.168.100.0/24` and the same `192.168.100.1` gateway as the home network. Retained for one reason only — it is the evidence that **a manual static address can be silently blocked by managed network equipment** (DHCP snooping / IP source guard), which is why §4.2 does not rely on one |

---

### 4.2 Demo network — self-provided equipment — **the deliverable runs here**

**Status: ⬜ not yet specified or acquired.** This is the single largest un-mitigated risk to the presentation.

**The rule: do not demonstrate on the venue's network.** School, campus and office Wi-Fi commonly enable **AP client isolation**, which permits each station to reach the internet but blocks station-to-station traffic. Every workstation would appear online, and none could reach the API host. The symptom arrives as a connection or certificate error minutes before presenting, and cannot be fixed without administrative access to equipment belonging to someone else.

Bringing the network removes that entire failure class and turns the demo LAN into a constant that can be rehearsed identically every time.

| Item | Decision |
|---|---|
| Equipment | ⬜ **to choose** — a travel router, or an unmanaged switch with short Ethernet runs. A phone hotspot works as fallback but is the weakest option: no wired ports, and isolation behaviour varies by handset |
| Wired vs wireless | **Prefer wired.** No isolation, no roaming, no interference from a room full of phones, and one less variable when something fails during setup |
| Subnet | ⬜ to choose. **Deliberately avoid `192.168.100.0/24`** — it collides with the lab network, and a machine carrying a stale lab config would appear to work while talking to the wrong thing |
| Host address | ⬜ to fix on the demo rig. On self-provided equipment this is set once and stays set; no router-admin negotiation with anyone |
| `MERCH-HOST` resolution | hosts file on the host and all three workstations, per `docs/installation-guide.md` §1, using the §4.2 address — never a lab address |
| Internet access required | **No.** The system is LAN-only. The demo rig needs no uplink at all, which is itself a robustness feature |
| Rehearsal | ⬜ owed before the presentation: full setup from cold on the demo rig, by someone other than the author, timed |
---

## 5. Verification checklist

Phase 0 is not complete until every box is ticked. A box is ticked only when the check was actually executed and its output exists on disk.

- [x] Dev machine captured, with `dotnet --info` attached — `evidence/phase-0/dotnet-info-dev.txt`; Visual Studio 2026 and both required workloads independently confirmed via `vswhere -requires` at `evidence/phase-0/p0-07-visual-studio-workloads.txt`
- [x] Reference lab host captured, with MariaDB version and dump tool confirmed — `evidence/phase-0/p0-07-mariadb-10.4-constraints.txt`
- [ ] **Every demo workstation captured** — no "same as above" shortcuts → **not started.** These are the classmates' three machines (§3.2), none of which has been surveyed. The author's test desktop does **not** satisfy this (P0-02, ADR-012). Ask each classmate for edition, build, architecture, resolution, scaling **and whether they hold local administrator rights** — the last one blocks certificate trust and is cheapest to discover now
- [ ] `ping MERCH-HOST` succeeds from every demo workstation, output attached → **gated on the demo rig existing** (§4.2), not on hardware and no longer on router administration. Lab-side entry is applied and verified — see `docs/installation-guide.md` §1.5. A separate lab run across two machines is worth doing sooner, as it unblocks P1-09/P1-10/P1-15, but it does **not** tick this box
- [x] XAMPP stripped to MariaDB only — `evidence/phase-0/xampp-services.txt` (PowerShell process/service/port output substituted for a screenshot, by agreement). *Cross-machine phpMyAdmin unreachability remains unverified — needs a client (P0-03).*
- [x] MariaDB bound to loopback, config excerpt attached — `evidence/phase-0/mariadb-config.txt`
- [ ] Backup directory **and** off-host destination chosen and writable → **half done.** `C:\MerchandisingBackups` created, ACL-restricted and write-proven (`evidence/phase-0/p0-07-network-and-backup.txt`) — but that is a *lab* path. Under ADR-012 both halves must become install-time configuration, so that a classmate's machine is not required to have a `C:\MerchandisingBackups` the author happened to create.
- [x] Versions transferred into `docs/adr.md` ADR-002 — every row populated except the MySqlConnector package, which legitimately belongs to P1-05. ADR-003 is now ACCEPTED with the measured MariaDB 10.4 constraints.

**Two additions to this checklist, found at P0-07 and not anticipated when it was written:**

- [x] `sql_mode` includes `STRICT_TRANS_TABLES`, server-side **and** per connection → **both halves done.** Server-side at P1-04 (`my.ini` line 157, restarted, re-verified — `evidence/phase-1/p1-04-grants.txt`); per-connection at P1-05 (`ConnectionFactory`, asserted live by `ConnectionFactoryTests` — `evidence/phase-1/p1-05-connection-test.log`). **Ticked 2026-08-18**, having been left unticked after P1-05 shipped.
- ⊘ ~~Host address made stable via a router-side MAC DHCP reservation~~ → **WITHDRAWN 2026-08-18 by ADR-012 — not done, and no longer required.** Deliberately not ticked: this requirement was removed, not satisfied, and a `[x]` here would misreport the gate. It would have stabilised a *lab* address that no delivered artefact is permitted to contain, and it depended on administering a router that will not be at the presentation. Replaced by §4.2: fix the address on self-provided demo equipment. **The underlying lesson stands** — never configure a host by manual static; the 2026-08-17 failure is recorded in `evidence/phase-0/host-ip-reservation.txt` §2.
- [ ] **Demo network chosen, acquired and rehearsed** (§4.2) → **not started — currently the largest un-mitigated risk to the presentation.** Venue Wi-Fi commonly isolates stations from each other, which breaks a client-server demo in a way that cannot be fixed on the day.

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
| 2026-08-18 | MariaDB account privileges | `merch_api`: `SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, REFERENCES` at **database** level | `merch_api`: **`SELECT` only** at database level. All writes become per-table via `db/grants/0002`, applied after migration 0001 | Health check found that CLAUDE.md §5's "append-only enforced by database grants" was **false on this machine**: a database-level `UPDATE`/`DELETE` grant covers every table including `AuditLogs` and `StockMovements`, MariaDB has no `DENY`, and privileges union across scopes — so P1-07's two acceptance boxes could never have passed. See ADR-013 | ✅ 13/13 privilege assertions pass on scratch tables — `UPDATE`/`DELETE`/`DROP` on an append-only table all rejected with `ERROR 1142`, `INSERT` accepted; `evidence/phase-1/p1-04a-grant-model-proof.txt`. Integration suite re-run green afterwards |
| 2026-08-18 | MariaDB accounts | `merch_api`, `merch_backup` | **+ `merch_migrator`@`localhost`** (`SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, DROP, INDEX, REFERENCES` on `merchandising`.*, **no `GRANT OPTION`**) | P1-06's acceptance requires re-runnable migration tests, and no existing account could tear down a table — `merch_api` was deliberately created without `DROP` at P1-04. Separating schema ownership from data access also removes DDL from the runtime account entirely. Credential at `%ProgramData%\MerchandisingSystem\config\database.migrator.json`, generated per installation, never committed (ADR-012 req. 6) | ✅ `CREATE`/`DROP TABLE` succeed as `merch_migrator` and are rejected for `merch_api`; same evidence file |
| 2026-08-18 | MariaDB `root` password | none | **none — unchanged, deliberately** | Reviewed during the pre-P1-06 health check and left as-is by decision: loopback-only, and the instance is shared with an unrelated project whose phpMyAdmin depends on password-less root. Recorded as an accepted lab risk rather than an oversight, with a required bootstrap step for demo installs | n/a — no change made |
