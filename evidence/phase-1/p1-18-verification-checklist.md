# P1-18 · Post-restore verification checklist

**What this is.** The list an operator works through at spec §15 step 6, before releasing maintenance mode at step 7. It exists because "the restore finished" and "the system is usable" are different claims, and step 7 makes releasing conditional on the second one.

**Who signs it.** The person running the restore. The API will not release maintenance mode without an explicit `verificationPassed: true` — a release admitting verification did not pass is refused with `409 VERIFICATION_NOT_CONFIRMED` and the lock stays held.

---

## Automated — `Merchandising.Maintenance.exe restore` does these itself

These run as part of the restore and set its exit code. A non-zero exit means at least one failed, and the rehearsal script will not release the lock.

| # | Check | How | Baseline 2026-08-22 |
|---|---|---|---|
| A1 | All 13 expected tables exist | `information_schema.tables`, by name | ✅ 13/13 |
| A2 | Users present | `COUNT(*)` | ✅ 8 |
| A3 | Products present | `COUNT(*)` | ✅ 3 |
| A4 | Stock balances present | `COUNT(*)` | ✅ 3 |
| A5 | Stock movements present | `COUNT(*)` | ✅ 221 |
| A6 | Audit rows present | `COUNT(*)` | ✅ 966 |

> **A1 is the one that has already caught something.** The first isolated restore failed on it: `maintenancelocks` was missing, because the dump predated migration 0004. Every row of data was fine; the schema was one table short. A restore that only counted rows would have reported success.

---

## Manual — the operator confirms these

Row counts prove data arrived. They do not prove the system *works*.

| # | Check | How | Pass when |
|---|---|---|---|
| M1 | API answers after restart | `GET /health` | 200 |
| M2 | A known user can sign in | WPF client, real credentials | Token issued, roles correct |
| M3 | A protected endpoint answers | `GET /api/v1/auth/me` | 200 with expected roles |
| M4 | Stock balance matches expectation | Compare one known product against its pre-restore value | Matches the backup's point in time, not the pre-restore live value |
| M5 | Movement ledger is contiguous | Newest `StockMovements` rows | Latest movement is at or before the backup timestamp |
| M6 | Schema version is right | `SchemaMigrations` | Highest applied migration matches what the binaries expect |
| M7 | Writes work again | One decrement through the client after release | Succeeds and creates a movement |

> **M4 is the one people get wrong.** After a restore the balance should match the **backup's** point in time, not what it was five minutes ago. If it matches the pre-restore live value, the restore did not take — a real possibility when a dump carrying its own `USE` statement is restored into what was meant to be an isolated database.

> **M6 matters more than it looks.** If the restored `SchemaMigrations` is behind the running binaries, the API is talking to a schema it does not expect. Run `Merchandising.Maintenance.exe migrate` before releasing, and note it in the release detail.

---

## Sign-off

Recorded in `MaintenanceLocks` on release, and in an append-only `AuditLogs` row that cannot be edited afterwards:

```json
POST /api/v1/admin/maintenance/release
{
  "verificationPassed": true,
  "detail": "A1-A6 automated; M1-M7 confirmed by <name>. RTO <n> min."
}
```

**If any check fails, do not release.** Say so, leave the system in maintenance, and restore again. `restore-rehearsal.ps1` does this for you: on a failed verification it leaves maintenance mode ON and exits non-zero.

The reasoning is worth stating plainly, because the pressure at that moment runs the other way: a half-restored database that is open for business is worse than one that is visibly closed. A closed system is an obvious problem someone will fix. An open one serving wrong stock figures is a silent one that reaches customers.

---

## Status

- ✅ **A1–A6 exercised** — see `p1-18-restore-log.txt` §2 and §3. A1 caught a real gap on the first attempt.
- ✅ **Release-gate behaviour proven** — `Release_WithoutPassingVerification_IsRefusedAndLockStaysHeld`.
- ⬜ **M1–M7 not yet run** — they belong to the in-place rehearsal, which needs an elevated session (see `p1-18-restore-log.txt`, "What is still owed").
