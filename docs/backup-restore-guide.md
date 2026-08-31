# Backup and Restore Guide

**Scope note.** This system is an **academic prototype** delivered as a proposal demo, not a production deployment. MariaDB is supplied through XAMPP because the course requires it (ADR-000). The single-host, single-copy risk this guide exists to cover is real regardless of that framing: the demo laptop is a single point of failure, and a local backup folder alone does not protect against host failure, theft, disk failure, or ransomware (spec §15).

This guide is the spec §20 deliverable "Backup and restore guide": schedule, command, account, retention, off-host copy, maintenance restore, verification checklist, RPO/RTO, and escalation. It is the runbook an operator who is not the author follows — every command below is copy-pasteable, and was run from this document on this machine rather than transcribed from memory (evidence: `evidence/phase-6/p6-09-backup-restore-guide.txt`).

---

## 1. The three identities — and the one that never appears below

| Identity | Used for | Holds | Never used for |
|---|---|---|---|
| `merch_backup` | The nightly dump | `SELECT`, `LOCK TABLES`, `SHOW VIEW`, `EVENT`, `TRIGGER` database-wide, plus `INSERT` on `backuplogs` alone (ADR-013, ADR-013.1) | Restore. It has no DDL and could not recreate a table if it tried |
| `merch_migrator` | Restore | Schema-owner DDL (`CREATE`, `ALTER`, `DROP`, …) scoped to the `merchandising` schema, plus `LOCK TABLES` (`db/grants/0006_restore-grants.sql`) | Serving ordinary requests — that is `merch_api`, and it is never mentioned in this guide because it has no role in backup or restore |
| `root` | **Nothing below.** One-time environment setup only (`db/grants/*.sql`, run once per installation by a human) | Everything | Every command an operator runs on a schedule or under pressure. If a step below ever seems to need root, that is a stop condition (CLAUDE.md §7) — say so and ask, do not substitute root for the identity that is missing a grant |

`Merchandising.Maintenance.exe backup` connects as `merch_backup`. `Merchandising.Maintenance.exe restore` connects as `merch_migrator`. Neither reads `database.json` (the `merch_api` config the running API service uses) — they read `database.backup.json` and `database.migrator.json` respectively, both beside it in `%ProgramData%\MerchandisingSystem\config\` (`docs/installation-guide.md` §3, §3.1).

---

## 2. Backup

### 2.1 What runs, as whom, on what schedule

| Control (spec §15) | Value |
|---|---|
| Schedule | Daily, 22:30 local — "after the store's defined closing or low-activity window" — via Windows Task Scheduler |
| Command | `Merchandising.Maintenance.exe backup` |
| Account | `merch_backup` — never `merch_api`, never root |
| Dump tool | `C:\xampp\mysql\bin\mysqldump.exe`. **`mariadb-dump.exe` and `mariadb.exe` do not exist in this XAMPP distribution** (CLAUDE.md §6.1, ADR pin) — spec §15's "preferably `mariadb-dump` when available" resolves to `mysqldump` here |
| Local destination | `C:\MerchandisingBackups` — outside the repo, outside the application binaries, not served by the API |
| Scheduled task identity | `NT AUTHORITY\SYSTEM`, **not** the API service account (`NT SERVICE\MerchandisingApi`) — a dump contains every password hash in the system, and the process most exposed to the network must not be able to read it |

Register the nightly task once per installation, elevated (`docs/installation-guide.md` §6.3 — not repeated here to avoid two copies drifting apart):

```powershell
pwsh ./scripts/register-backup-task.ps1
Start-ScheduledTask -TaskName 'Merchandising Nightly Backup'
Get-ScheduledTaskInfo -TaskName 'Merchandising Nightly Backup' | Select-Object LastRunTime, LastTaskResult
```

To run a backup by hand — the exact command this guide's own evidence was captured with:

```powershell
& "C:\MerchandisingSystem\Merchandising.Maintenance.exe" backup
```

Real output, this machine, 2026-08-31 (full run in `evidence/phase-6/p6-09-backup-restore-guide.txt` §1):

```
Backup Succeeded (correlation 155a3677-7cbf-4eaa-82a5-9eb710c4279c)
  file        C:\MerchandisingBackups\merchandising-20260831-074012.sql
  size        46840595 bytes
  sha256      bcd9cc1a1f8289533d6ee2ef6b6d20e9c4185987a057feeedddc052ba666ce50
  off-host    D:\MerchandisingBackups\merchandising-20260831-074012.sql
  server      10.4.32-MariaDB
  retention   7 (pruned 16)
  logged      BackupLogs
```

### 2.2 Retention

Configurable, held in `SystemSettings` rather than in code (spec §15's "Retention" control), seeded by `db/migrations/0003_backup.sql`:

| `SystemSettings` key | Default | Meaning |
|---|---|---|
| `backup.retentionCount` | `7` | Dumps kept, local **and** off-host (ADR-025). Below 1 means *prune nothing* — never *delete everything* |
| `backup.offHostVolumeLabel` | `MERCHBACKUP` | Volume **label**, never a drive letter — a USB stick mounts as `D:` on one demo laptop and `F:` on the next |
| `backup.directory` | `C:\MerchandisingBackups` | Local destination |

Change a value with an `UPDATE` against `SystemSettings`; the next run picks it up. No redeploy needed.

### 2.3 Off-host copy

Spec §15's "Off-host copy" control: a periodic copy to a separate physical drive. `BackupCommand` looks for a **ready** volume labelled `backup.offHostVolumeLabel` (default `MERCHBACKUP`) at the moment the backup runs and copies the dump there automatically — there is no separate off-host step to run. If no such volume is attached, the run is recorded as `Partial`, not `Succeeded`: the dump is good, but it is the only copy that exists, and a scheduled task that reported success for that would be how a system ends up with months of backups that only ever existed on the machine that died.

The USB stick is exFAT, which carries no ACLs, and it contains every password hash on the system. Reformatting to NTFS was considered and rejected — ACLs on removable media are defeated by taking ownership on any machine with administrator rights, so NTFS would buy the *appearance* of protection, not protection. Physical custody of the stick is the real control.

### 2.4 Integrity, security, and failure handling

| Control | How it is met |
|---|---|
| Integrity | `BackupLogs` records file size, SHA-256, both timestamps, source DB version, and result — recomputed from the bytes on disk after the dump finishes, never from a buffer the process still holds in memory |
| Security | `C:\MerchandisingBackups` has inheritance disabled and grants only `SYSTEM`/`Administrators`; `database.backup.json` explicitly excludes the API service account |
| Failure handling | A failed dump (bad exit code, empty file, or a file missing mysqldump's own `-- Dump completed` trailer) is recorded as `Failed` with the reason in `BackupLogs.Detail`, and the partial file is deleted rather than left to be mistaken for a real backup. If `BackupLogs` itself cannot be reached, the outcome still lands in `C:\MerchandisingBackups\backup-failures.log` — there is no path that fails silently |

Read the result:

| `LastTaskResult` | `BackupLogs.Result` | Meaning |
|---|---|---|
| `0` | `Succeeded` | Dump written, checksum verified, off-host copy made |
| `1` | `Partial` | Dump is good and kept, but no off-host copy happened — usually the USB stick was not plugged in. Not a failure, but not a clean success either |
| `1` | `Failed` | No usable dump. Reason in `BackupLogs.Detail`, or in `backup-failures.log` if the database itself was unreachable |

---

## 3. Restore

Restore is a **disruptive maintenance operation**, never a normal API request — the process serving requests cannot coherently survive having its own database replaced underneath it (spec §15). The seven-step procedure below is spec §15's, with each step mapped onto the exact command or endpoint that carries it out.

| # | Spec §15 step | What actually runs |
|---|---|---|
| 1 | Super Admin requests maintenance mode, provides a reason | `POST /api/v1/admin/maintenance/enter` |
| 2 | API writes a maintenance lock, rejects new writes, warns clients | `MaintenanceModeMiddleware` — a `503` envelope with a correlation ID on every ordinary write while the lock is held; `GET /api/v1/admin/maintenance/status` is anonymous, so a client that cannot log in can still see why |
| 3 | Users close clients, operator confirms the window | Manual — no command |
| 4 | Operator runs the maintenance utility on the host with the selected backup file | `Merchandising.Maintenance.exe restore --file <dump.sql> [--target <database>]` |
| 5 | Utility stops/coordinates services, restores, validates schema, records a local log | Handled inside the `restore` command itself — see §3.1–§3.2 |
| 6 | Operator restarts services, runs health/integrity checks, confirms expected data | See the checklist in §3.3 |
| 7 | API records the completed restore event after recovery, releases maintenance mode only after verification succeeds | `POST /api/v1/admin/maintenance/release` |

### Step 1 — enter maintenance mode

```
POST /api/v1/admin/maintenance/enter
Authorization: Bearer <SuperAdmin session token>
Content-Type: application/json

{ "reason": "Restoring from the 2026-08-31 22:30 nightly backup" }
```

Requires `Maintenance.Perform` (Super Admin only — narrower than the rest of the admin surface, because this stops the store trading). A second `enter` while one is already active is refused with `409 MAINTENANCE_ALREADY_ACTIVE`, never a silent no-op. This exact request/response shape — field names `reason` in, `inMaintenance`/`reason`/`message`/`sinceUtc` out — is what `MaintenanceController.Enter` and `EnterMaintenanceRequest`/`MaintenanceStatusResponse` actually implement, and is exercised through the real HTTP pipeline (not a mock) by `MaintenanceModeTests`, which passes in this commit's test run (§4 below).

### Steps 4–5 — restore, on the host, with the API service stopped

```powershell
& "C:\MerchandisingSystem\Merchandising.Maintenance.exe" restore --file "C:\MerchandisingBackups\merchandising-20260831-074012.sql"
```

Runs as `merch_migrator`. With no `--target`, it restores into whatever database the dump's own `CREATE DATABASE`/`USE` lines name — ordinarily `merchandising`, the live schema, which is correct for a real disaster recovery.

**`--target <database>` exists for exactly one purpose: testing the restore mechanism itself without touching the live schema** (spec §15's "Verification" control — "test restore against an isolated database"). A dump taken with `mysqldump --databases` carries its own `CREATE DATABASE`/`USE` lines, which **override** any `--target` redirect — restoring "into an isolated copy" while accidentally overwriting the live one is a real trap, found and recorded first at P1-18. Strip those two lines from a **copy** of the dump before redirecting:

```powershell
Get-Content "$dump" | Where-Object { $_ -notmatch '^(CREATE DATABASE|USE )' } | Set-Content "$dump.filtered.sql"
& "C:\MerchandisingSystem\Merchandising.Maintenance.exe" restore --file "$dump.filtered.sql" --target merchandising_restoretest
```

Real output, this machine, 2026-08-31, restoring the backup taken in §2.1 into the isolated `merchandising_restoretest` schema (`db/grants/0015_restore-rehearsal-grants.sql` — full detail `evidence/phase-6/p6-08-restore-timed.txt`):

```
Restoring 'C:\MerchandisingBackups\merchandising-20260831-074012.filtered.sql' into 'merchandising_restoretest' as 'merch_migrator'...
Restore SUCCEEDED
  target      merchandising_restoretest
  dump        C:\MerchandisingBackups\merchandising-20260831-074012.filtered.sql
  elapsed     7.5s (0.12 min) to VERIFIED state
  users       991
  products    8855
  balances    6712
  movements   16472
  audit rows  73258
```

### 3.1 Checksum preflight (P6-08)

Before `mysql.exe` is ever invoked, `restore` looks up the SHA-256 `backup` recorded for the **exact file path** in `BackupLogs` and recomputes the file's actual checksum. A mismatch refuses the run — nothing is touched:

```
Restore FAILED
  ** Refused before starting: the checksum BackupCommand recorded for '...' (bcd9cc1a...)
     does not match the file's current bytes (7ea8bee7...). The dump may be corrupted
     or tampered with. Nothing was changed.
```

Real output from a deliberately corrupted copy of the same dump — `evidence/phase-6/p6-09-backup-restore-guide.txt` §3. **A dump with no matching `BackupLogs` row at all is *not* refused** — that is "unrecorded", not "contradicted", and this system must still be able to restore a dump taken by another tool or moved off a decommissioned host.

### 3.2 Local maintenance log (step 5's "records a local maintenance log")

Every restore attempt — succeeded, failed, or refused at the checksum/privilege preflight — appends one line to `C:\MerchandisingBackups\restore-maintenance.log`, local rather than inside the database being restored (a log written into the schema being replaced would be destroyed by the restore it describes).

### 3.3 Verification checklist (step 6)

Full checklist, automated and manual halves, with pass/fail criteria: **`evidence/phase-1/p1-18-verification-checklist.md`**. Summary:

- **Automated**, run by `restore` itself and reflected in the console output above: all 13 expected tables exist; users, products, balances, movements, and audit rows are present (non-zero, or matching the dump's known counts for an isolated-target rehearsal).
- **Manual**, the operator's own confirmation before releasing: the API answers `GET /health`; a known user can sign in; a protected endpoint answers; a known product's stock balance matches the **backup's** point in time (not the pre-restore live value — see the checklist's own note on this being the mistake people actually make); the movement ledger's newest row is at or before the backup timestamp; `SchemaMigrations` is at the version the running binaries expect; and one write succeeds after release, proving the system is not just restored but usable.

**Do not release maintenance mode if any check fails.** Restore again instead — see §5.

### Step 7 — release maintenance mode

```
POST /api/v1/admin/maintenance/release
Authorization: Bearer <SuperAdmin session token>
Content-Type: application/json

{
  "verificationPassed": true,
  "detail": "elapsed=7.5s users=991 products=8855 balances=6712 movements=16472 audit=73258; A1-A6 automated, M1-M7 confirmed."
}
```

`verificationPassed: false` — or omitting it — is **refused** with `409 VERIFICATION_NOT_CONFIRMED`, and the lock stays held. This is deliberate: an operator who genuinely cannot verify must say so and be refused, rather than being allowed to quietly reopen a system whose data they do not trust. `detail` lands in `MaintenanceLocks` and in an append-only `AuditLogs` row — the completed-restore event spec §15 step 7 requires "recorded after service recovery" is this row, confirmed durable (read back by a fresh connection, not trusted from the `200 OK` alone) by `MaintenanceModeTests.Restore_ThroughRealMaintenanceWorkflow_RecordsCompletedRestoreEvent`.

There is **no restore endpoint on the API**, by design — asserted by `MaintenanceModeTests.Api_ExposesNoRestoreEndpoint` — because a restore that ran inside the very process whose database it is replacing cannot coherently survive its own effect.

---

## 4. RPO / RTO (PA-005)

| Target | Value | Demonstrated |
|---|---|---|
| RPO | ≤ 24 hours | Daily backup schedule (§2.1) — the maximum data loss is one day's transactions |
| RTO, to **verified** state, demo host | ≤ 15 minutes (900s) | **7.5s measured**, 0.8% of budget, at this database's current size (~44 MB dump, ~104,000 rows across the five counted tables) — `evidence/phase-6/p6-08-restore-timed.txt` §4 |
| RTO, full host rebuild (worst case) | ≤ 120 minutes, documented only | Not separately measured — this is the "reinstall everything from scratch" case, not the restore mechanism this guide covers |

The clock in `RestoreCommand` stops **after verification**, not after the data lands — "restored" and "confirmed usable" are different claims, and PA-005's target is to the second one. **What the 7.5s figure does not cover:** stopping and restarting the API Windows Service, and the operator steps around them. Those remain manual, un-timed, and owed to an elevated rehearsal session — the same boundary recorded and not yet closed at P1-18 (`evidence/phase-1/p1-18-restore-log.txt`).

---

## 5. Escalation

This is an academic prototype demonstrated by a three-person team, not a staffed operation — spec §20's exit criterion for named role owners (G-28: "Assign responsibility for installation, backup, restore, … and incident escalation") is a **Phase 7** deliverable, not this one. What follows is the procedure, not a name.

- **A checksum mismatch (§3.1) or a failed restore is not retried automatically, and should not be retried by hand either without understanding why it failed first.** The refusal already means nothing was changed — the live database (or the isolated target) is exactly as it was before the attempt. Re-running the same command against the same corrupted file will fail the same way.
- **Preserve the failing artifact.** Do not delete a dump that failed a checksum check or a restore that failed verification until someone has looked at *why* — it is the only evidence of what went wrong. Copy it aside before investigating rather than restoring over it again.
- **A restore that completes but fails verification (§3.3) must not be released.** Restore again from a different, known-good dump (an older retained one, or the off-host copy) rather than releasing a system whose data is not trusted. A closed system is a visible problem someone will fix; an open one silently serving wrong stock figures is not.
- **If the second attempt also fails, stop and escalate to the rest of the team before a third.** This mirrors CLAUDE.md §7's stop conditions for the codebase itself: a repeated failure past the first retry is a sign the fault is in the environment or the procedure, not the specific attempt, and guessing a third time is not a substitute for a second person looking at it.
- **For the demo specifically:** because RPO is 24 hours, the most recent local dump (or the off-host copy if the local one is the thing that failed) is always the fallback, and both are listed with their timestamps in `BackupLogs` — `SELECT * FROM backuplogs ORDER BY Id DESC LIMIT 7;` as `merch_backup` or any identity holding `SELECT`.

---

## 6. Evidence and current status

- ✅ Backup mechanism (`BackupCommand`), retention/off-host rotation (ADR-025), and the unwritable-directory failure gap: P1-17, P6-07 — `evidence/phase-6/p6-07-backup-retention.txt`
- ✅ Restore mechanism (`RestoreCommand`), maintenance-mode workflow, checksum preflight, timed and verified end to end against an isolated schema: P1-18, P6-08 — `evidence/phase-1/p1-18-restore-log.txt`, `evidence/phase-6/p6-08-restore-timed.txt`
- ✅ This guide, drift-checked against the real CLI usage strings, the real Contracts JSON field names, and the real evidence files it cites: `BackupRestoreGuideDocumentationTests` — `evidence/phase-6/p6-09-backup-restore-guide.txt`
- ⬜ **Not yet run:** the single **in-place** rehearsal that stops the live Windows Service, restores over the live database, and restarts it (`scripts/restore-rehearsal.ps1`, P1-18) — still owed an elevated session, same gap recorded and not closed by this card
- ⬜ **Not yet assigned:** named role ownership for backup/restore/incident escalation (G-28) — Phase 7, not this card
