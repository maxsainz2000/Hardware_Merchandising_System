# Environment Manifest

**Purpose.** Record the exact environment the system is built and accepted on. Closes gap G-30.

The spec is explicit that "works on the developer's machine" is not acceptance. This file is what makes that testable — every version below must be captured from the **actual** classroom machines, not assumed.

**Status:** ⬜ Incomplete — Phase 0 in progress
**Last updated:** _(date)_

---

## 1. Development machine

| Item | Value | How to capture |
|---|---|---|
| Windows edition | _(record)_ | `winver` or `systeminfo` |
| Windows build | _(record)_ | `systeminfo \| findstr /B /C:"OS"` |
| Architecture | _(expect x64)_ | `systeminfo` |
| Visual Studio version | _(record full build number)_ | Help → About Microsoft Visual Studio |
| VS workloads installed | .NET desktop development; ASP.NET and web development | VS Installer → Modify |
| .NET SDK version | _(record)_ | `dotnet --info` |
| .NET runtimes present | _(record all listed)_ | `dotnet --list-runtimes` |
| Git version | _(record)_ | `git --version` |
| PowerShell version | _(record)_ | `$PSVersionTable.PSVersion` |

**Attach:** full `dotnet --info` output to `evidence/phase-0/dotnet-info-dev.txt`.

---

## 2. Host laptop (API + database)

| Item | Value | How to capture |
|---|---|---|
| Machine name | _(record)_ | `hostname` |
| Windows edition + build | _(record)_ | `systeminfo` |
| Reserved LAN IP | _(record)_ | Router DHCP reservation or static config |
| Host name for clients | `MERCH-HOST` | Chosen — must match the certificate SAN |
| Name resolution method | _(hosts file / DNS)_ | Record which, per client |
| .NET runtimes present | _(record)_ | `dotnet --list-runtimes` |
| ASP.NET Core runtime present | _(yes/no + version)_ | `dotnet --list-runtimes` |
| .NET Desktop Runtime present | _(yes/no + version)_ | `dotnet --list-runtimes` |
| XAMPP version | _(record)_ | XAMPP Control Panel → About |
| XAMPP install path | _(record)_ | — |
| MariaDB server version | _(record)_ | `mariadb --version` |
| MariaDB config file path | _(record — usually my.ini)_ | XAMPP Control Panel → MySQL → Config |
| MariaDB data directory | _(record)_ | From config file |
| Dump tool available | _(`mariadb-dump` / `mysqldump` + version)_ | `mariadb-dump --version` |
| MariaDB bind address | _(expect loopback only)_ | From config file |
| MariaDB port | _(record — default 3306)_ | From config file |
| XAMPP components disabled | Apache, FileZilla, Mercury, Tomcat | XAMPP Control Panel screenshot |
| Backup directory path | _(record — must be outside the binaries)_ | — |
| Off-host backup destination | _(record — separate physical drive or approved cloud)_ | — |
| API HTTPS port | 8443 | Chosen |
| Firewall rule for API port | _(record rule name + allowed subnet)_ | `netsh advfirewall firewall show rule` |

**Attach:**

- `evidence/phase-0/xampp-services.png` — only MariaDB running
- `evidence/phase-0/mariadb-config.txt` — bind address and port
- `evidence/phase-0/dotnet-info-host.txt`

---

## 3. Client laptops

Copy this block once **per client machine**. Every client is part of the tested system, not an assumption.

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
| Network type | Private store LAN / Wi-Fi |
| Subnet | _(record)_ |
| Router / AP model | _(record)_ |
| Host IP reservation method | _(static / DHCP reservation)_ |
| Internet access required | No — the system is LAN-only |

---

## 5. Verification checklist

Phase 0 is not complete until every box is ticked.

- [ ] Dev machine captured, with `dotnet --info` attached
- [ ] Host laptop captured, with MariaDB version and dump tool confirmed
- [ ] **Every** client laptop captured — no "same as above" shortcuts
- [ ] `ping MERCH-HOST` succeeds from every client, output attached
- [ ] XAMPP stripped to MariaDB only, screenshot attached
- [ ] MariaDB bound to loopback, config excerpt attached
- [ ] Backup directory and off-host destination chosen and writable
- [ ] Versions transferred into `docs/adr.md` ADR-002

---

## 6. Change log

Any change to this environment after Phase 0 goes through a tested change procedure and is recorded here (spec §17, patch policy).

| Date | Component | From | To | Reason | Retested |
|---|---|---|---|---|---|
| | | | | | |
