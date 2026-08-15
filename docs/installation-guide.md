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
| Host IP address | `192.168.100.165` |
| Hosts file path | `C:\Windows\System32\drivers\etc\hosts` |
| Required rights | **Administrator.** The file is not writable by a standard user. |

> ⚠️ **`192.168.100.165` is network-specific.** It is a static address on the Wi-Fi network `HUAWEI-5G-fP2f 2`. On the classroom or store network the host will have a different address and **every** hosts entry below must be redone with the new one. See the environment manifest §4.

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
| Host `LAPTOP-3HH6OHHE` | ❌ **not yet** — needs an elevated shell (see below) | ❌ |
| Client 1 | ❌ no client machine provisioned | ❌ |

> **Host entry outstanding.** At P0-07 the agent session was not elevated and self-elevation was refused by the tooling's permission boundary, so the host's own hosts entry was **not** written. Run §1.2 on the host in an elevated PowerShell. This is a one-line task that removes a step from the day the client laptop arrives; it does **not** close P0-05 on its own.

---

## 2. Still to be written

These sections are owned by later tasks and are deliberately absent rather than stubbed with guesses:

| Section | Owed by |
|---|---|
| MariaDB accounts, grants, and `sql_mode` hardening | P1-04 |
| Connection configuration file location and ACL | P1-05 |
| Certificate installation and client trust procedure | P1-09 |
| Windows Service registration and recovery settings | P1-16 |
| Backup schedule, retention, and off-host destination | P1-17 |
| Restore procedure and maintenance mode | P1-18 |
| Client prerequisites (.NET 10 Desktop Runtime) and client install | Phase 6 |
