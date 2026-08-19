# Architecture Decision Record

Every pinned version and irreversible choice for the Merchandising System.

**This file is the single source of truth for versions.** If a package or version is not recorded here, it must not appear in a `.vbproj`. See `CLAUDE.md` §6.

**Format:** each decision gets an ID, a status, the decision itself, the reasoning, and the alternatives rejected. Status is one of `PENDING` · `ACCEPTED` · `SUPERSEDED`.

---

## ADR-000 · Course constraints are binding

**Status:** ACCEPTED
**Date:** 2026-08-14
**Decides:** whether Visual Basic and XAMPP are genuinely required.

**Decision.** All application source is Visual Basic .NET. MariaDB is supplied through XAMPP. Both confirmed by the professor as binding requirements, not preferences.

**Consequences.**

- No C# is permitted anywhere in `src/`, including test code. Guardrail G-A enforces this.
- No alternative database (standalone MariaDB, SQL Server, PostgreSQL, SQLite) may be substituted, including "just for testing".
- The system is an **academic prototype**, not a production deployment. XAMPP is documented by Apache Friends as intended for development environments. Every document, the cover page, and the final presentation must state this. Retrofitting the wording later is far more work than writing it correctly now.
- Fallback rung C from `plan.md` §1.1 (a professor-approved exception permitting non-VB code) is effectively closed — it would require reversing a decision already made. Rung B is the last self-service option.

**Evidence:** see `docs/professor-approvals.md`.

---

## ADR-001 · API project SDK — rung A vs rung B

**Status:** ACCEPTED
**Date:** 2026-08-16
**Decides:** how `Merchandising.Api.vbproj` is constructed, given that no Visual Basic ASP.NET Core template exists.

**Options.**

| Rung | Approach |
|---|---|
| A | `Sdk="Microsoft.NET.Sdk.Web"`, `OutputType=Exe`, controller-based, `Module Program` / `Sub Main` |
| B | `Sdk="Microsoft.NET.Sdk"` + `<FrameworkReference Include="Microsoft.AspNetCore.App" />`, self-hosted Kestrel |

**Decision. RUNG A.** `Merchandising.Api.vbproj` is a hand-authored project on `Sdk="Microsoft.NET.Sdk.Web"` with `OutputType=Exe`, a `Public Module Program` / `Public Sub Main` entry point, and controller-based endpoints. **Rung B was not attempted, because it is only reached when rung A fails, and rung A did not fail.**

**This retires the project's dominant risk.** Spec §6.2 built the whole plan around an unproven assumption — that a Visual Basic project could sit on an SDK whose tooling is written for C#, with no template to fall back on. It can, and now it has, in-repo.

**Reasoning — the exact behaviour observed at P1-02.**

| Question | Result |
|---|---|
| Does the Web SDK accept a hand-authored `.vbproj`? | Yes, unmodified. No target overridden, no `Import` added, no property set to route around a C#-assuming target. |
| Build result, API project alone (`--no-incremental`) | **0 warnings, 0 errors**, exit 0 |
| Build result, all eleven projects | **0 warnings, 0 errors**, exit 0. `Merchandising.Tests.Integration` builds against the API as an `Exe` without complaint, so P1-19's `WebApplicationFactory` seam is not blocked. |
| Was anything suppressed to get 0 warnings? | No. There is no `NoWarn` anywhere in the repository, and `WarningsAsErrors` is in force for `BC42104`, `BC42030`, `BC42016`. |
| Properties needed beyond the ordinary? | **None.** The file sets exactly three: `OutputType`, `RootNamespace`, `TargetFramework`. |
| Does `Module Program` / `Sub Main` work as an ASP.NET Core entry point? | Yes. Kestrel started, `stderr` empty. |
| Do angle-bracket attributes route? | Yes — `<ApiController>` / `<Route("health")>` / `<HttpGet>` produced `{action = "GetHealth", controller = "Health"}`. |
| Does reflection-based JSON handle VB? | Yes. A **VB anonymous type** (`VB$AnonymousType_0`) serialised correctly via `ObjectResultExecutor` — the required combination, since source generation is C#-only. |
| Did the SDK generate any C#? | **No.** Zero `.cs`/`.csproj` under the project including `obj/`; two generated intermediates, both `.vb`. Guardrail G-A will not fight the build. |

**The project file does not restate `EnableRequestDelegateGenerator`, `PublishAot` or `PublishTrimmed`** — all three are inherited from `Directory.Build.props`. Proven in two halves: `dotnet msbuild -getProperty:` evaluated `EnableRequestDelegateGenerator=false` against the project, and a text search showed the only occurrence of the name in the `.vbproj` is inside a comment explaining its absence. Restating it locally would have satisfied a naive reading of the acceptance box while proving the opposite of what it asks.

**Friction: NONE.** All four P1-02a triggers checked explicitly — no workaround, no suppressed warning, no non-standard property, and no behavioural difference from the P0-07 pre-flight probe. **P1-02a is therefore skipped**, on a recorded audit rather than on the pre-flight's prediction alone.

**One build did fail, and it is deliberately not counted as friction.** The first attempt died with `MSB4025: An XML comment cannot contain '--'` — a run of hyphens used as a separator inside the new project file's comment block. It failed at XML parse time, before SDK resolution, restore or compilation, and would have failed identically on the plain `Microsoft.NET.Sdk`. It says nothing about rung A. Logging it as friction would have triggered the insurance spike against a fallback there is no reason to need, on the strength of a typo in a comment.

It is worth recording for a different reason: **this is the third occurrence of that same defect.** P1-01 found it in `Directory.Build.props`, where it had broken every MSBuild invocation in the repository and survived the whole of Phase 0 undetected, then reintroduced it twice while fixing it. The warning written at P1-01 lived only in the file that had already been bitten, so it did not prevent a fresh occurrence in a new file. The note is now repeated in `Merchandising.Api.vbproj` itself.

**Rejected: rung B** (`Sdk="Microsoft.NET.Sdk"` + `<FrameworkReference Include="Microsoft.AspNetCore.App" />`). Not on its merits — it was never tested, and does not need to be. It exists as insurance against rung A failing, and it costs a hand-managed framework reference, a hand-built Kestrel host, and divergence from every piece of ASP.NET Core documentation the team will read. With rung A clean at 0 warnings, taking that on would be pure cost. **If a future SDK update breaks rung A, rung B is still the next step and PA-001 grants no C# escape hatch** — ADR-000 records that rung C is effectively closed.

**Evidence:** `evidence/phase-1/p1-02-project-file.txt` (project file, inheritance proof, build output, zero-C# scan, friction audit), `evidence/phase-1/p1-02-health-response.txt` (Kestrel startup, `GET /health` 200, response and header disclosure check).

---

**P0-07 pre-flight note — advance information, NOT a decision.**

A disposable rung A probe was built, run and published **outside the repository** on 2026-08-15 and then deleted. It is recorded here because it changes what P1-02 should expect, but it **does not resolve this ADR** — P1-02 runs in-repo, under `Directory.Build.props`, alongside ten sibling projects, and records the decision itself.

What the probe established:

- A hand-authored `.vbproj` on `Sdk="Microsoft.NET.Sdk.Web"` with `OutputType=Exe` and `TargetFramework=net10.0` **built clean on the first attempt: 0 warnings, 0 errors.**
- `Module Program` / `Sub Main` works as an ASP.NET Core entry point. `dotnet run` started Kestrel; `GET /health` returned `200` with `{"status":"ok","version":"0.0.1-probe","utcTime":"…"}`.
- Controller-based routing, attribute routing and DI all work from Visual Basic.
- Reflection-based `System.Text.Json` correctly serialised a **VB anonymous type** (`VB$AnonymousType_0`) — the exact combination required, since source generation is C#-only.
- Both `win-x64` publishes succeeded and **both published executables were run and served `/health`**: framework-dependent (7 files, 0.2 MB) and self-contained (341 files, 105.0 MB).
- **The Web SDK generated zero `.cs` files** for a VB project — 4 `.vb` intermediates in `obj/`, no C# anywhere. Guardrail G-A will not fight the build.

**Friction: none.** `<EnableRequestDelegateGenerator>false</EnableRequestDelegateGenerator>` was removed and the project rebuilt from scratch — it still built clean, because the Request Delegate Generator only applies to minimal APIs and this design uses controllers. The property is therefore **defence in depth, not a workaround**. Keep it set; do not count it as friction.

**Consequence for P1-02a:** on this evidence the insurance spike should be **skipped**, per its own trigger condition. If P1-02 in-repo behaves differently from the probe, *that difference is the friction* and P1-02a triggers on it.

**Evidence:** `evidence/phase-0/p0-07-rung-a-preflight.txt`

> **Outcome, recorded at P1-02 (2026-08-16).** The probe held. In-repo behaviour matched it on every point checked — same 0/0 build, same `200`, same `VB$AnonymousType_0` serialisation path, same zero generated C# — with `Directory.Build.props` and the `MerchGuardrails` target additionally in play and neither changing the outcome. **P1-02a skipped**, on P1-02's own friction audit rather than on this prediction. The probe's value was that it moved the project's dominant risk from "unknown" to "very likely fine" a day before Phase 1 needed the answer; it did not, and could not, resolve the ADR.

---

## ADR-002 · MariaDB and .NET connector versions

**Status:** ACCEPTED — every row resolved at P1-05.
**Decides:** the exact database and data-access versions the whole project is built and tested against. Closes gap G-04.

| Item | Value | Source |
|---|---|---|
| XAMPP version | `8.2.12-0`, Windows x64 (`properties.ini` → `base_stack_version`), installed at `C:\xampp` | P0-03 |
| MariaDB server version (`mariadb --version`) | `10.4.32-MariaDB` for Win64/AMD64 (mariadb.org binary distribution) — captured via `mysqld.exe --version`; this XAMPP distro ships no `mariadb.exe` binary | P0-04 |
| Dump tool available (`mariadb-dump` / `mysqldump`) | **`mysqldump.exe`** at `C:\xampp\mysql\bin\mysqldump.exe`, Ver 10.19 Distrib 10.4.32-MariaDB. No `mariadb-dump.exe` exists in this distribution, despite the card's stated preference — P1-17 must build on `mysqldump` | P0-04 |
| MariaDB config file path | `C:\xampp\mysql\bin\my.ini` | P0-04 |
| Data directory | `C:/xampp/mysql/data` | P0-04 |
| Port | `3306` | P0-04 |
| `bind-address` | `127.0.0.1` (loopback) — previously unset, server listened on wildcard `::`; changed and restart-verified | P0-04 |
| Character set / collation / engine | `utf8mb4` / `utf8mb4_unicode_ci` / InnoDB — **see ADR-003, which is ACCEPTED and carries the measured 10.4 constraints** | P0-07 |
| `sql_mode` as shipped | `NO_ZERO_IN_DATE,NO_ZERO_DATE,NO_ENGINE_SUBSTITUTION` — **not strict**, must be corrected. See ADR-003.2 | P0-07 |
| .NET connector package | `MySqlConnector` `2.6.2` — latest stable on NuGet 2026-08-16, chosen with Max, no preview/RC | P1-05 |
| Test framework package | `MSTest` `4.0.2` — **pinned in ADR-009, which is ACCEPTED.** Recorded here so `CLAUDE.md` §6 ("if a version is not in `docs/adr.md`, it must not appear in a `.vbproj`") is satisfied without a hunt | P1-01 |
| .NET SDK version (`dotnet --info`) | `10.0.301` (commit `96856fd726`) | P0-01 |
| .NET runtimes installed | `Microsoft.AspNetCore.App` 8.0.28 / 9.0.17 / **10.0.9** · `Microsoft.NETCore.App` 8.0.28 / 9.0.17 / **10.0.9** · `Microsoft.WindowsDesktop.App` 8.0.28 / 9.0.17 / **10.0.9** | P0-01 |
| Target framework | `net10.0` / `net10.0-windows` | `Directory.Build.props` |
| Visual Studio | Community 2026, `18.7.1+11911.148`, workloads `ManagedDesktop` + `NetWeb` | P0-01, re-verified P0-07 |

**Reasoning.** MariaDB's own documentation recommends MySqlConnector for MariaDB Server. EF Core is deliberately **not** an MVP dependency — the transaction design uses explicit provider transactions and parameterized ADO.NET, so no unverified EF Core + MariaDB provider combination sits under the correctness guarantees. Closes gap G-05.

**Rejected:** MySql.Data (Oracle connector), EF Core with Pomelo or the Oracle provider.

> **Result, P1-05.** `MySqlConnector 2.6.2` pinned in both `Merchandising.Infrastructure.vbproj`
> and `Merchandising.Tests.Integration.vbproj`. `ConnectionFactory` opens every connection
> through `MySqlConnectionStringBuilder` and, on every connection, issues
> `SET SESSION sql_mode = CONCAT(@@SESSION.sql_mode, ',STRICT_TRANS_TABLES')` and
> `SET SESSION tx_isolation = 'READ-COMMITTED'` — belt-and-braces with the P1-04 `my.ini`
> change (ADR-003.2) and the ADR-006 isolation requirement, using the 10.4 variable name
> `tx_isolation` (`transaction_isolation` does not exist on this server). Both settings proven
> live against the real `merchandising` database as `merch_api`; see
> `evidence/phase-1/p1-05-connection-test.log`. The connection string itself is assembled only
> from an ACL-protected host file at `C:\ProgramData\MerchandisingSystem\config\database.json`,
> outside the repo and outside the binaries — never a committed file (G-C, confirmed clean).

---

## ADR-003 · Character set, collation, storage engine, and MariaDB 10.4 server constraints

**Status:** ACCEPTED
**Date:** 2026-08-15
**Decides:** the exact charset, collation, and engine every migration declares, plus the MariaDB 10.4.32 server behaviours that SQL written against MySQL 8 or MariaDB 11 will get wrong.

**Decision.**

| Item | Pinned value |
|---|---|
| Character set | `utf8mb4` |
| Collation | `utf8mb4_unicode_ci` — **stated explicitly on every `CREATE DATABASE` and `CREATE TABLE`** |
| Storage engine | `InnoDB` — stated explicitly |
| Row format | `DYNAMIC` (server default here; do not rely on it silently) |

Every migration declares all four. Never inherit them from the server.

**Reasoning.** Hardware product names include punctuation and possibly non-ASCII characters; `utf8mb4` avoids a class of silent truncation. InnoDB is required for the transaction and row-locking behaviour the whole correctness design depends on — MyISAM would make ADR-006 unimplementable.

The collation must be stated explicitly because **the server default is not the value we want**: `@@collation_server` is `utf8mb4_general_ci`, not `utf8mb4_unicode_ci`. A `CREATE TABLE` that omits `COLLATE` silently gets `general_ci`, and a schema with mixed collations produces `Illegal mix of collations` errors on joins that will look inexplicable months later.

---

### ADR-003.1 · MariaDB 10.4 constraints — measured, not assumed

All values below were captured from the installed server on 2026-08-15, not read from documentation. Training data skews heavily toward MySQL 8 and MariaDB 11; every row here is a place that skew produces SQL which fails at apply time.

| Fact | Measured value | Consequence |
|---|---|---|
| Server version | `10.4.32-MariaDB` | — |
| `uca1400` collation family | **0 collations exist** (`SELECT COUNT(*) FROM information_schema.COLLATIONS WHERE COLLATION_NAME LIKE '%uca1400%'` → `0`) | `utf8mb4_uca1400_ai_ci` is **MariaDB 11.x only**. A migration using it fails at apply time. Never reach for it. |
| `utf8mb4_unicode_ci` | present | The collation we pin. |
| `@@innodb_default_row_format` | `dynamic` | Index key prefix limit is 3072 bytes. `VARCHAR(255)` utf8mb4 = 1020 bytes → a unique index on SKU or barcode is safe with ~3× headroom. **Verified by building the real table**, not calculated. |
| `@@innodb_page_size` | `16384` | Confirms the 3072-byte limit applies. |
| `@@lower_case_table_names` | `1` (Windows) | Table names are folded to **lowercase** and compared case-insensitively. `CREATE TABLE StockBalances` is stored as `stockbalances`. Harmless here — the system is Windows-only per spec — but it means a schema dumped from this host would break on a case-sensitive Linux server. Recorded so it is never discovered by accident. |
| Isolation variable name | `tx_isolation` — **`transaction_isolation` does not exist** (`ERROR 1193 Unknown system variable`) | `transaction_isolation` is the MySQL 8 / MariaDB 11.1+ spelling. Code or scripts using it fail on this server. |
| Default isolation level | `REPEATABLE-READ` | ADR-006 specifies `READ COMMITTED`. It is **not** the default and must be set explicitly per connection or in config. Do not assume. |
| `UUID` column type | does not exist (`ERROR 1064` syntax error) | Added in MariaDB 10.7. Use `CHAR(36)` or `BINARY(16)` for correlation and idempotency keys. |
| Dump tool | `C:\xampp\mysql\bin\mysqldump.exe`, `Ver 10.19 Distrib 10.4.32-MariaDB, for Win64 (AMD64)` | **`mariadb-dump.exe` does not exist in this distribution**, nor does `mariadb.exe`. P1-17 builds on `mysqldump.exe`. Any document preferring `mariadb-dump` is wrong and is corrected. |
| `innodb_buffer_pool_size` | `16M` | XAMPP default, very small. Not a Phase 1 problem; revisit before the Phase 7 load test. |

**Decimal precision verified on this server.** A `DECIMAL(19,4)` / `DECIMAL(19,3)` table round-tripped `12345678901234.5678` and `0.001` exactly, with a `DATETIME(6)` UTC timestamp. ADR-004's types are implementable here as written.

---

### ADR-003.2 · `sql_mode` is NOT strict on this server — must be fixed at P1-04

**This is the most consequential finding of the P0-07 close-out and it is not cosmetic.**

XAMPP's `C:\xampp\mysql\bin\my.ini` line 157 sets:

```ini
sql_mode=NO_ZERO_IN_DATE,NO_ZERO_DATE,NO_ENGINE_SUBSTITUTION
```

`STRICT_TRANS_TABLES` is **absent**. XAMPP has actively weakened MariaDB 10.4's own default, which does include it. Without strict mode the server silently coerces bad data instead of rejecting it. Demonstrated on this server:

```
-- Non-strict (as shipped):
INSERT INTO t VALUES ('THIS-SKU-IS-FAR-TOO-LONG', 1.9999);   -- into VARCHAR(8), DECIMAL(19,3)
  stored Sku=[THIS-SKU]   stored Qty=[2.000]                  -- accepted, silently mangled

-- With STRICT_TRANS_TABLES:
  ERROR 1406 (22001): Data too long for column 'Sku' at row 1  -- rejected
```

A truncated SKU and a silently rounded quantity, with no error returned to the API. In a merchandising system whose entire value proposition is an accurate stock ledger, that is a correctness failure the application layer cannot see and the audit log would faithfully record as success.

**Decision.** The API must not depend on the server being strict, and must not silently benefit from it either. Both of the following, belt and braces:

1. **Set it server-side** at P1-04: add `STRICT_TRANS_TABLES` to `sql_mode` in `my.ini` and restart. This is an environment change — record it in the environment manifest change log.
2. **Set it per connection** in the `Infrastructure` connection factory at P1-05, so a rebuilt or reinstalled XAMPP cannot quietly revert the guarantee. The connection is the only thing the application controls.

Column widths and decimal **ranges** are then enforced by the database, which is where they belong — an application-only check is one forgotten `Try/Catch` away from being no check at all.

> **Correction — decimal *scale* is not covered by this.** An earlier reading of this ADR claimed strict mode enforces decimal scales too. It does not: over-scale values are still rounded silently under `STRICT_TRANS_TABLES`. That gap is closed in the API instead — see **ADR-004.1**.

**Rejected:** relying on API-side validation alone. The API re-validates everything that matters (CLAUDE.md §5), but a defence that exists in exactly one layer is not a defence, and the failure mode here is silent.

**Evidence.** `evidence/phase-0/p0-07-mariadb-10.4-constraints.txt`

> **Item 1 (server-side) done at P1-04 (2026-08-16).** `STRICT_TRANS_TABLES` added to `sql_mode` in `C:\xampp\mysql\bin\my.ini`, server restarted (`mysqladmin shutdown` + `mysql_start.bat` — `mysql_stop.bat` itself did not reliably terminate the process), and the truncation demo re-run: `'THIS-SKU-IS-FAR-TOO-LONG'` into `VARCHAR(8)` now raises `ERROR 1406 (22001)` instead of silently storing `'THIS-SKU'`. **This does not close the decimal-scale half** — `1.9999` rounding to `2.000` on an over-scale insert is still silent under strict mode, by design (see ADR-004.1, which correctly assigns that half to the API, not the database). Evidence: `evidence/phase-1/p1-04-grants.txt`. **Item 2 (per-connection) done at P1-05 (2026-08-16) — this ADR is now fully discharged.** `ConnectionFactory` issues `SET SESSION sql_mode = CONCAT(@@SESSION.sql_mode, ',STRICT_TRANS_TABLES')` on every connection opened, and `ConnectionFactoryTests.OpenedConnection_HasStrictModeAndReadCommittedIsolation` asserts it live against the real server on every test run. Evidence: `evidence/phase-1/p1-05-connection-test.log`. **Corrected 2026-08-18** — this note previously still read "remains owed by P1-05" after P1-05 had shipped, and contradicted ADR-002's own P1-05 result note four sections above it in this same file.

---

## ADR-004 · Numeric precision

**Status:** ACCEPTED
**Date:** 2026-08-18
**Decides:** storage precision for money, quantity, and timestamps. Proven live at task P1-07. **PA-003 (formal professor confirmation) is still outstanding — see the note below; the decision itself is not blocked on it.**

| Kind | Type | Rationale |
|---|---|---|
| Money | `DECIMAL(19,4)` | Exact arithmetic; no floating-point representation error in currency |
| Quantity | `DECIMAL(19,3)` | Hardware stores sell fractional units (metres of cable, kilos of nails) |
| Timestamps | `DATETIME(6)` UTC | Microsecond precision; UTC storage, Asia/Manila display |

**Decision.** As proposed in the baseline, confirmed by measurement rather than assumption: `0.001` and `12345678901234.5678` both round-trip exactly through real `DECIMAL(19,3)`/`DECIMAL(19,4)` columns (`StockBalances.Quantity`, `Products.Price`) on the pinned MariaDB 10.4.32 instance, first at the P0-07 pre-check and again at P1-07 against the real `Products`/`StockBalances` tables. See `evidence/phase-1/p1-07-precision-check.txt`.

**Consequence.** `Double` and `Single` are forbidden for money and quantity everywhere — storage, calculation, and transport. Use `Decimal` in VB.

**Resolved like ADR-000 at P0-06: the decision is settled on its own evidence, the professor-approval paperwork is separate and still open.** `p1-07-precision-check.txt` demonstrates why the decision cannot simply wait on the database to enforce it: `UPDATE ... SET Cost = 0.19995` against `DECIMAL(19,4)` under `STRICT_TRANS_TABLES` silently stores `0.2000` (`Note 1265`, not an error) — exactly the ADR-004.1 hole. **PA-003 has not been raised with the professor yet** — that is Max's action, tracked the same way PA-001/PA-002 were at P0-06, and `tasks.md`'s P1-07 card stays 🟡 until it happens.

---

### ADR-004.1 · Rounding to storage scale is the API's job, not the database's

**`STRICT_TRANS_TABLES` does not close the decimal-scale hole.** ADR-003.2 fixes silent *string truncation* and makes over-**range** decimals an error. It does **not** make over-**scale** decimals an error: rounding a value to the column's declared scale stays a `Note`-level diagnostic under every `sql_mode`, strict included. This is long-standing upstream behaviour — MySQL feature request **#87678** ("Strict mode should reject, not round, over-precision decimals") is still open, and MariaDB inherits it.

So the demonstration in ADR-003.2 splits in two once P1-04 lands:

```
INSERT INTO t VALUES ('THIS-SKU-IS-FAR-TOO-LONG', 1.9999);   -- VARCHAR(8), DECIMAL(19,3)
  with STRICT_TRANS_TABLES:
    Sku  → ERROR 1406, rejected                              -- fixed by ADR-003.2
    Qty  → stored 2.000, Note 1265, "success"                -- NOT fixed. Still silent.
```

**Why this matters more for money than the truncation case did.** `DECIMAL(19,4)` holds four decimal places. Any computed value with more — a line total from a fractional quantity (`2.5 m × ₱13.3333`), a percentage discount, a VAT-inclusive unwind, a landed-cost allocation split across receipt lines — arrives at the database with more precision than the column can hold and is rounded on the way in, without an error.

The failure mode is not a wrong-looking number. It is **drift**: the API computes an order total in full `Decimal` precision and stores it, then stores each line value rounded independently. The header total and `SUM(line values)` now differ by cents. Nothing reports an error, both figures look plausible, and the discrepancy surfaces on a reconciliation report long after the movements and audit rows have been written and made immutable. There is no corrective edit available — `StockMovements` and `AuditLogs` are append-only.

**Decision.** The database is **never** trusted to round. Every money and quantity value is validated and **explicitly rounded to its storage scale in the API, before it reaches the parameter**:

- Money → **4** decimal places · Quantity → **3** decimal places.
- One policy, applied everywhere: **`Decimal.Round(value, scale, MidpointRounding.AwayFromZero)`** — half-up, matching invoice and receipt arithmetic as a cashier would check it by hand. `MidpointRounding.ToEven` (banker's rounding) is .NET's *default* and is **rejected** here: it is correct for statistics and wrong for a printed receipt a customer can add up.
- Rounding happens **once**, at the boundary where a value is persisted or returned. Intermediate arithmetic stays at full `Decimal` precision — rounding each intermediate step reintroduces the same drift by a different route.
- **Totals are derived from already-rounded components**, never rounded independently of them. A header total is the sum of the rounded line values, so header and lines are equal by construction rather than by coincidence.
- Client-supplied values exceeding the storage scale are a **validation failure** returning a controlled 400 with field-level detail — not a value to quietly round on the caller's behalf. Only values the API itself computes are rounded.

**Consequence.** `Infrastructure` may assume every `Decimal` parameter it binds is already at storage scale. A `Note 1265` from the server is therefore evidence of an API defect, not a routine event.

**Rejected:** leaving rounding to the database now that strict mode is on. Strict mode does not do it, and even if a future MariaDB made it an error, an error at the `INSERT` is the wrong place to discover a rounding question — the transaction is already open, the movement row already written, and the only answer available at that point is a rollback.

**Verification required:** P1-07 and P1-11 each carry an integration test that inserts an over-scale value and asserts the API rejected or explicitly rounded it. A test that merely observes the stored value is correct at the declared scale proves nothing — the silent rounding produces exactly that result.

> **P1-11 done (2026-08-19).** `StockDecrementTests.Decrement_OverScaleQuantity_RejectedBeforeAnyRowWritten` asserts `StockService.DecrementAsync` throws `ArgumentException` for a 4-decimal-place quantity before any connection opens, against the real database — a genuine integration test this time, not P1-07's justified unit-test workaround, since P1-11 has a real service method to call. Also proven at the HTTP boundary: a live request with `quantity:1.9999` returns 400 `VALIDATION_FAILED` with field-level detail. Evidence: `evidence/phase-1/p1-11-decimal-scale.txt`.

---

## ADR-005 · Authentication and token scheme

**Status:** ACCEPTED
**Date:** 2026-08-19
**Decides:** the token scheme for `POST /api/v1/auth/login`, `GET /api/v1/auth/me`, `POST /api/v1/auth/logout`, and the policy-gated `GET /api/v1/admin/ping`. Closes gap G-19 (begins).

**Decision: opaque server-side session token, not JWT bearer.** `SessionTokenGenerator` mints 256 bits of randomness (`RandomNumberGenerator`, Base64Url-encoded) as the bearer value handed to the client. Only its SHA-256 hash is ever written to the database (`Sessions.TokenHash`), so a database read alone — a backup file, a compromised `merch_backup` account — cannot be turned back into a usable token. `SessionAuthenticationHandler` resolves `Authorization: Bearer <token>` by hashing it and looking it up in `Sessions` on every request; there is no signature to verify and nothing to parse.

**Reversed from the baseline lean, and here is why.** The baseline in this entry's original text was JWT bearer with a host-held signing key. Two things changed that once P1-08 actually reached for the packages:

- `Microsoft.AspNetCore.Authentication.JwtBearer` and `System.IdentityModel.Tokens.Jwt` are **not** present in the installed `Microsoft.AspNetCore.App 10.0.9` shared framework (confirmed by listing it directly). Either package would have needed a `PackageReference` with a version not recorded anywhere in this document — stop condition 2 in CLAUDE.md §7.
- `Microsoft.Extensions.Identity.Core` — which is where `PasswordHasher(Of TUser)` lives, spec §9's "framework-provided password-hashing implementation" — **is** already in that shared framework. `Merchandising.Infrastructure.vbproj` reaches it with a plain `<FrameworkReference Include="Microsoft.AspNetCore.App" />`, no new package, no version to pin. The opaque-token path needed nothing JWT would have required anyway.

**A property JWT would not have given for free: real revocation.** `POST /api/v1/auth/logout` deletes the matching `Sessions` row (`SessionRepository.DeleteByTokenHashAsync`) — the token stops working immediately, proven live in `evidence/phase-1/p1-08-auth-matrix.txt` PART B case 12. A JWT is valid until its signature-checked expiry no matter what the server does afterward, unless a denylist is added — which is a database table anyway, at which point the signature is bearing no weight the database lookup wasn't already doing.

**Session lifetime:** 8 hours (`AuthenticationPolicy.SessionLifetime`), matching the "expiry matched to a work shift" reasoning the baseline lean already established — that reasoning survived the JWT-vs-opaque reversal even though it no longer needs an expiry *claim* to carry it; `Sessions.ExpiresAtUtc` is checked directly by `SessionRepository.FindActiveByTokenHashAsync`.

**Confirmed, unchanged from the baseline:**

- Passwords stored only as salted hashes via `PasswordHasher(Of TUser)` (PBKDF2-HMACSHA256, V3 format) — never plaintext, never reversible.
- Account lockout after five failed attempts (`AuthenticationPolicy.MaxFailedLoginAttempts`) within a 15-minute window (`AuthenticationPolicy.LockoutDuration`); the locking attempt and the lockout event are both audited.
- Token held in client memory only; the server never has the plaintext token to hold anywhere but the HTTP response that hands it out once.
- Authorization enforced by ASP.NET Core policy (`<Authorize(Roles:="Admin,SuperAdmin")>` on `AdminController`) in the API on every protected endpoint — proven with a real 401/403/200 matrix, not by hiding a button.

**Friction encountered:** one real defect, not a scheme problem. `UserRepository.RecordFailedLoginAsync`'s original single-statement `UPDATE` read `FailedLoginAttempts` twice in the same `SET` list; MariaDB's left-to-right assignment evaluation meant the second read saw the already-incremented value, locking accounts one attempt early (at 4, not 5). Caught by `Login_SixBadPasswords_LocksAccountAndLogsIt` failing for the right reason on its first run — see `evidence/phase-1/p1-08-auth-matrix.txt` for the failure, the fix, and the confirmation that 15/15 integration tests and the live HTTP matrix both pass afterward.

**Evidence:** `evidence/phase-1/p1-08-auth-matrix.txt`, `evidence/phase-1/p1-08-log-scan.txt`

---

## ADR-006 · Concurrency control for stock changes

**Status:** PENDING — prove at tasks P1-11 through P1-13
**Decides:** how the system guarantees stock can never go negative under concurrent sales. Closes gaps G-11 and G-12.

**Decision.** Server-side **conditional update** plus affected-row verification, inside an explicit transaction at `READ COMMITTED`:

```sql
UPDATE StockBalances
   SET Quantity = Quantity - @qty,
       RowVersion = RowVersion + 1,
       UpdatedAtUtc = UTC_TIMESTAMP(6)
 WHERE ProductId = @productId
   AND Quantity >= @qty;
-- affected rows must be exactly 1, else roll back and return a controlled 409
```

**Reasoning.** Read-then-write has a race window no isolation level closes cheaply. The conditional update pushes the check into the same atomic statement as the mutation, so the loser of a race simply affects zero rows and is rolled back with a controlled response. Isolation level alone is explicitly **not** relied upon.

**Evidence required:** `p1-13-concurrency.txt` showing ten simultaneous requests against one unit of stock produce exactly one success and a final balance of zero.

> **P1-11 progress (2026-08-19) — mechanism implemented and proven; status stays PENDING.** The exact SQL above is live in `StockRepository.TryDecrementAsync` (`src/Merchandising.Infrastructure/Data/StockRepository.vb`), called from `StockService.DecrementAsync` inside one transaction alongside the `StockMovements` and `AuditLogs` inserts (P1-11: `POST /api/v1/inventory/stock/decrement`). Proven so far: the happy path produces exactly one movement/audit row with a correct balance; an insufficient-stock request is rejected with a controlled 409 and writes zero rows; both are backed by an integration test against the real database *and* a live HTTP capture cross-checked directly against the schema (`evidence/phase-1/p1-11-happy-path.txt`, `p1-11-insufficient-stock.txt`). **What this does not yet prove:** the actual concurrency claim this ADR exists for — that two or more simultaneous requests against the same low-stock row cannot both succeed. `READ COMMITTED` is set per-connection (ADR-003.2/P1-05) but, per the Reasoning above, is not what this design relies on; InnoDB row-locking on the conditional `UPDATE` is what should serialize concurrent attempts, and that claim is untested until P1-13 fires real concurrent requests at it. Status stays **PENDING** until then — P1-12 (rollback) and P1-13 (concurrency) are still open.

---

## ADR-007 · Idempotency for transactional commands

**Status:** PENDING — prove at task P1-14
**Decides:** how retries avoid creating duplicate sales, receipts, returns, or adjustments. Closes gap G-13.

**Decision.** `IdempotencyKeys` table with a unique constraint on `(Scope, KeyValue)`. Insert-first strategy: the command claims its key before doing work, and the committed response payload is stored and replayed verbatim on repeat.

---

## ADR-008 · Migration mechanism

**Status:** ACCEPTED
**Date:** 2026-08-18
**Decides:** the migration mechanism — discovery, checksumming, transaction boundary and failure recording. **The identity question was already settled at ADR-013: the runner connects as `merch_migrator`, not `merch_api`.**

**Decision.** Numbered, forward-only SQL files in `db/migrations/NNNN_description.sql`, discovered and applied by `MigrationRunner` (`src/Merchandising.Maintenance/Migrations/`). Each file is SHA-256 checksummed at discovery time. Unapplied files are applied in ascending order, each inside its own transaction. `SchemaMigrations` records identifier, checksum, UTC timestamp, and result (success/failure + error message) for every attempt. If a previously successfully-applied file's checksum no longer matches disk, the entire run is refused before anything is applied — a clear error, not a silent skip.

**`SchemaMigrations` is bootstrapped by the runner itself** (`CREATE TABLE IF NOT EXISTS`), not by a numbered migration — the runner needs it to exist before it can determine whether migration `0001` has already run, which no ordinary migration file can resolve for itself. P1-07's own migration list names `SchemaMigrations` as one of the tables `0001_foundation.sql` creates; that statement must use `IF NOT EXISTS` (or be dropped) so it doesn't collide with what the runner already created.

**Known limitation, not fixable here.** DDL statements (`CREATE TABLE`, `ALTER TABLE`, ...) cause an implicit commit in MariaDB/InnoDB and do not participate in transaction rollback. "A failing migration rolls back" is therefore a real guarantee for DML, and only partial for a migration that mixes DDL and DML. This is a property of the database engine, documented in `MigrationRunner.vb` and in the P1-06 task-card result note, not something this mechanism can paper over.

**Consequence.** Applied migrations are **immutable**. Corrections are new migrations, never edits. This is stop condition 6 in `CLAUDE.md`.

**Rejected.** A migration file itself creating `SchemaMigrations` — rejected because the runner cannot know whether *that* migration already ran without the table already existing; standard tooling (Flyway, DbUp) bootstraps its own tracking table for the same reason.

**Evidence.** `evidence/phase-1/p1-06-migration-run.log`, `evidence/phase-1/p1-06-tamper-refusal.log` — `MigrationRunnerTests.vb`, one test per acceptance box, all green against the real pinned MariaDB instance.

---

## ADR-009 · Test framework

**Status:** ACCEPTED
**Date:** 2026-08-16
**Decides:** which test framework both VB test projects are built on. Resolved **early, at P1-01** rather than P1-19, because P1-01 creates both test projects and `dotnet new` cannot create a test project without naming a framework — leaving this PENDING would have meant either deferring two of the eleven projects or picking a framework informally and back-filling the ADR.

**Decision.** **MSTest**, package `MSTest` version **4.0.2** (single metapackage; the template pulls no other test package).

**Reason (one line).** It is the in-box Microsoft-authored VB template with the smallest version surface — one `MSTest` metapackage against NUnit's five packages and xUnit's four — and its attribute-driven `<AssemblyInitialize>` / `<ClassInitialize>` fixtures are plain shared `Sub`s, avoiding the generic-interface (`IClassFixture(Of T)`) and multi-line-lambda plumbing that `CLAUDE.md` §3 flags as awkward in VB and that the real-MariaDB integration fixtures would lean on hardest.

**Constraint.** All test source is Visual Basic. Integration tests run against the real pinned MariaDB, never an in-memory substitute.

---

### ADR-009.1 · All three candidates were measured on this machine, not compared from documentation

Generated outside the repository on 2026-08-16 and then deleted, under a `Directory.Build.props` mirroring the repo's (`Option Strict On`, `WarningsAsErrors`, `GenerateDocumentationFile`):

| Template | `dotnet new … -lang VB` | Packages pinned | `dotnet test` under `Option Strict On` |
|---|---|---|---|
| `mstest` (Microsoft) | generates | `MSTest 4.0.2` | **1 passed, 0 warnings, 0 errors** |
| `nunit` (community — Aleksei Kharlov) | generates | `NUnit 4.3.2`, `NUnit3TestAdapter 5.0.0`, `NUnit.Analyzers 4.7.0`, `Microsoft.NET.Test.Sdk 17.14.0`, `coverlet.collector 6.0.4` | **1 passed** |
| `xunit` (Microsoft) | generates | `xunit 2.9.3`, `xunit.runner.visualstudio 3.1.4`, `Microsoft.NET.Test.Sdk 17.14.1`, `coverlet.collector 6.0.4` | **1 passed** |

**None of the three is a risk.** All emit clean VB, all build with zero warnings under `Option Strict On`, all run. The choice is therefore genuinely a preference, exactly as this ADR's original wording assumed — the difference is that it is now a preference backed by measurement.

Also verified: `dotnet test <proj> --configuration Debug --no-build --nologo` — the exact invocation in `scripts/run-tests.ps1` — works against the MSTest VB project. MSTest 4.0.2 still runs through the VSTest bridge on this SDK, so `run-tests.ps1` needs no change.

**Rejected.** NUnit — its VB project template is community-authored rather than Microsoft-authored, and it pins five packages where MSTest pins one. xUnit — no assembly- or class-level setup attributes; shared fixtures require implementing `IClassFixture(Of T)` / `ICollectionFixture(Of T)` and its assertion idiom leans on `Assert.Throws(Of T)(Function() … End Function)`, both of which are heavier in VB than in C#. Neither loses on capability.

---

### ADR-009.2 · Two things the MSTest VB template gets wrong — fix them at P1-01

The generated `.vbproj` is a C# project file with the language swapped. P1-01 must correct it, not accept it as emitted:

```xml
<TargetFramework>net10.0</TargetFramework>   <!-- hard-coded; must become $(MerchNetTfm) -->
<ImplicitUsings>enable</ImplicitUsings>      <!-- C#-only concept, inert in VB — delete -->
<Nullable>enable</Nullable>                  <!-- C#-only concept, inert in VB — delete -->
<Using Include="Microsoft.VisualStudio.TestTools.UnitTesting" />  <!-- C#-only item; VB's
     equivalent is <Import>. Harmless only because the template also writes a file-level
     `Imports` line in every .vb it generates. Delete it. -->
```

Second, the template writes `MSTestSettings.vb` containing:

```vb
<Assembly: Parallelize(Scope:=ExecutionScope.MethodLevel)>
```

**That is actively wrong for the integration suite.** Method-level parallelism runs tests concurrently against the one real MariaDB instance, which shares state between them — it would make P1-12's rollback proof and P1-14's idempotency proof flaky for reasons that have nothing to do with the code under test. The integration project must set `<Assembly: DoNotParallelize>` instead. (P1-13's concurrency proof creates its own concurrency inside a single test; it does not want the runner supplying more.) The unit project may keep method-level parallelism.

**Confirmed at P1-01, plus one more the template audit missed.** Both defects above appeared exactly as predicted. A third did not: **both MSTest templates wrap their generated test class in a `Namespace` block matching the project's own `RootNamespace`**, so `Merchandising.Tests.Unit` would have resolved as `Merchandising.Tests.Unit.Merchandising.Tests.Unit`. The generated `Test1.vb` files were replaced rather than patched — they asserted nothing at all. (`dotnet new wpflib -lang VB` has an unrelated defect of the same character: a duplicated `<RootNamespace>` element.)

**Treat every `dotnet new … -lang VB` output as a draft.** These templates are maintained against C# and translated; the translation is not clean. Read what they emit before adding it to the solution.

**Evidence.** Recorded at P1-01 into `evidence/phase-1/p1-01-build.log`.

---

## ADR-010 · Deployment model

**Status:** ACCEPTED
**Date:** 2026-08-16
**Decides:** framework-dependent vs self-contained publishing. **Begins** closing gap G-16 — it does not close it (see *Still open* below).

| Component | Decision | Measured |
|---|---|---|
| **API Windows Service** | **Self-contained `win-x64`** | 347 files, 105.1 MB |
| **Maintenance utility** | **Self-contained `win-x64`** | 202 files, 76.6 MB |
| **WPF clients** | **Framework-dependent `win-x64`** + .NET 10 Desktop Runtime prerequisite | 15 files, 0.2 MB |

Both modes were published **and run** before deciding. The framework-dependent API is 17 files / 0.2 MB — a factor of roughly 525 smaller — and both served `GET /health` from a folder copied outside the repository with no build tree and no source in it.

**Reasoning — API.** Spec §18 permits framework-dependent *"only with verified host runtime"*. This host does have `Microsoft.AspNetCore.App 10.0.9` — **but only because the .NET SDK is installed on it**, since the host laptop is currently also the development machine. A classroom or store host built from scratch has no SDK and therefore no ASP.NET Core runtime, so the condition is not met for the deployment this decision is actually for. Reading today's dev machine as "the host" would be verifying the wrong computer.

P1-16 raises the stakes: the API runs as a **Windows Service** with automatic restart and must serve after an unattended reboot. A missing runtime there is not an error message someone reads and fixes — it is a service that silently fails to start, discovered when a client cannot connect, plausibly mid-demonstration. 105 MB on a laptop is an acceptable price for removing that entire failure class.

**Reasoning — Maintenance.** Symmetry with the API is the weaker argument. The stronger one: this utility owns **backup and restore** (P1-17, P1-18). It is the tool you reach for when the system is already broken, and a recovery tool that depends on a correctly installed runtime can fail for the same reason you are running it.

**Reasoning — WPF clients.** Spec §18 is explicit, and nothing measured here contradicts it. Self-contained would cost roughly 150 MB per client across three clients to avoid one documented install step per laptop. The prerequisite is declared by the package itself — `Merchandising.Inventory.runtimeconfig.json` names `Microsoft.WindowsDesktop.App 10.0.0` as a hard requirement — so a client without it fails at launch rather than misbehaving subtly.

**Also established at P1-03:** a Visual Basic **WPF** application publishes `win-x64` and runs (window opens, responds, closes cleanly). That was untested before — P0-07's probe covered the API only, and P1-01 built the clients without publishing or launching one.

**Fixed:** `win-x64` runtime identifier throughout, applied at publish time by `scripts/publish-release.ps1` and never set globally in `Directory.Build.props`. Never `PublishAot` or `PublishTrimmed` — neither works with Visual Basic. The publish script **refuses to run** if either resolves to `true`, which covers the hole G-D cannot see: G-D reads `.vbproj` text and cannot detect `-p:PublishAot=true` passed on a command line.

**Still open — G-16 is begun, not closed.** Its required evidence is a *release manifest* and a *clean-machine installation test*. Neither exists yet:

- **No machine without the .NET runtime has been tested.** The self-contained choice above is reasoned from the spec's condition, not proven by a runtime-free host.
- **The WPF Desktop Runtime prerequisite *check* is unvalidated** and cannot be validated here — this machine has the Desktop Runtime, so a framework-dependent client runs whether or not the check works. Carried to **P1-15**.
- **The full release manifest** (version, commit identifier, migration list, release notes, rollback instructions, package hashes) is Phase 6/7. `publish-release.ps1` produces a package hash only, and says so in its own header.

**Rejected: framework-dependent API.** It works today and is 525× smaller, but it is only safe on a host whose runtime someone has deliberately installed and verified — and it fails in the worst available way, as a service that does not start, on a host where nobody did.

**Evidence:** `evidence/phase-1/p1-03-publish.log`

---

## ADR-011 · Certificate strategy

**Status:** ACCEPTED
**Date:** 2026-08-19
**Decides:** how HTTPS is trusted on client laptops. Closes gaps G-07 and G-25.

**Baseline lean:** self-signed certificate with subject/SAN covering the host name `MERCH-HOST` (and the reserved IP if client configuration will use it), with a documented manual trust-installation procedure per client.

**P0-05 note:** host name `MERCH-HOST` confirmed as the name to use (matches the baseline lean above — no reason found to deviate).

**Revised 2026-08-18 by ADR-012 — the baseline lean's "and the reserved IP" clause is now actively harmful.** An IP address in the SAN binds the certificate to one network. Under ADR-012 the same package must run on the author's lab LAN, on a self-provided demo rig, and on whatever the venue provides — three different subnets, at least. A certificate carrying `192.168.100.165` would be valid in exactly one of those and produce a trust warning in the others, at the moment it matters most.

**Therefore P1-09 should lean to a name-only SAN** covering `MERCH-HOST`, with the address supplied per install through the hosts file. That keeps the certificate a constant of the *package* rather than a property of one network, and it is the reason the hosts-file approach is worth its manual step. Confirm or overturn this at P1-09 — it is a lean, not yet the decision.

Resolution is configured on the lab host and on **no** demo workstation, which is correct: no hosts entry should be written until the demo rig's addressing is fixed.

**Decision, confirmed at P1-09: name-only SAN.** The lean stands, and a second reason arrived before the demo LAN even entered the picture: the development machine itself is a laptop that moves between home, school, and office Wi-Fi in the ordinary course of building this system, each with its own subnet. An IP-bound SAN would have generated a fresh trust warning on every network change *during development*, not just at the demo. `scripts/create-dev-certificate.ps1` generates a self-signed certificate with subject `CN=MERCH-HOST` and a single SAN entry, `DNS Name=MERCH-HOST` — confirmed by direct inspection of the generated certificate, not assumed: `evidence/phase-1/p1-09-cert-details.txt`. `New-SelfSignedCertificate -DnsName MERCH-HOST` alone was sufficient; no `-IPAddress` parameter is present anywhere in the script.

**Trust mechanism, proven live.** `evidence/phase-1/p1-09-invalid-cert-behaviour.txt` walks a real client through the full cycle against the real Kestrel process: before trust is installed, the TLS handshake fails with `RemoteCertificateChainErrors`; after importing `merch-host.cer` into the client's Trusted Root store, the identical connection succeeds over TLS 1.3 with `SslPolicyErrors: None`. A separate check confirms the name-only decision is actually enforced and not just documented: connecting to the same trusted certificate by **IP address** instead of `MERCH-HOST` still fails, with `RemoteCertificateNameMismatch`.

**Friction encountered, and what is still open.** The evidence above ran on one machine — the lab host acting as its own client via a raw `SslStream` targeting the name `MERCH-HOST` over a loopback socket — because the lab test workstation was not on this network during this task (same gap already tracked at `installation-guide.md` §1.5). That proves the trust *mechanism* correctly, but a literal second physical machine following `evidence/phase-1/p1-09-client-trust-steps.md` has not yet happened and stays an open item on the P1-09 task card, not on this ADR: the certificate strategy itself is decided.

**Fixed:** HTTPS mandatory for production-like operation on port 8443. HTTP permitted only on an isolated developer machine, visibly labelled non-production — implemented as a loopback-only (`127.0.0.1`) listener active only when `ASPNETCORE_ENVIRONMENT=Development`, with an `X-Non-Production-Http` response header and a startup console warning (`Merchandising.Api.Middleware.NonProductionWarningMiddleware`).

**Evidence:** `evidence/phase-1/p1-09-cert-details.txt`, `evidence/phase-1/p1-09-client-trust-steps.md`, `evidence/phase-1/p1-09-invalid-cert-behaviour.txt`

---

## ADR-012 · Delivery model — who receives this system, and on whose hardware

**Status:** ACCEPTED
**Date:** 2026-08-18
**Decides:** what the deliverable actually is, and which machines it must run on. Reframes the acceptance criteria of P0-02, P0-03, P0-05, P1-09, P1-10 and P1-15. Does **not** alter any architectural rule.

**Decision.**

1. **The deliverable is a handover package, not an installed system.** Three classmates are paying for this work. They will present it as a system proposal for a mid-scale hardware store, and the proposed system must already be functional at presentation time. There is **no deployment to the business.** The store is the *subject* of the proposal, not a site.
2. **Nothing required to run this system may exist only on the author's hardware or only in the author's head.** The package must be installable and demonstrable by three people who did not build it, on machines the author does not own, on a network the author does not control.
3. **The author's laptop and desktop are a development and integration-test lab.** They are not "the host" and not "Client 1". They stand in for the demo environment; they are never part of it.
4. **Topology is unchanged from the spec:** one API host and three WPF workstations (Procurement, Inventory, POS), one database, one LAN. One business, three workstations — not three deployments.
5. **The demo LAN is self-provided equipment**, not the venue's network. See *Reasoning* — this is a hard requirement, not a convenience.
6. **Credentials are generated per installation** and never committed. No password known to the author may be the password protecting a classmate's demo.

**Reasoning.**

*Why the lab/demo distinction is load-bearing.* Phase 0 recorded the environment of one laptop — machine name, a Wi-Fi SSID, a router, a MAC address, a Tailscale tunnel — as though those were properties of the system. They are properties of one developer's house. Every one of them is absent at presentation time. The manifest's own Portability row already said "nothing in this section transfers", and 2026-08-17 proved it the hard way when a static IP followed the laptop to another network and broke it. This ADR makes the distinction structural instead of a warning note.

*Why the demo network must be self-provided.* The presentation happens on a school or venue network nobody here administers. **AP client isolation is common on such networks and blocks station-to-station traffic** — every workstation reaches the internet, none can reach the API host. The failure surfaces as a connection or certificate error minutes before presenting, and is unfixable without administrative access to equipment belonging to someone else. A travel router, an unmanaged switch, or a phone hotspot removes the entire failure class and makes the demo LAN a rehearsable constant. It also retires the router-admin dependency and the DHCP-reservation problem that blocked P0-05.

*Why Tailscale leaves scope.* It is a personal tailnet on a personal account. It exists on the author's machines and on no machine that will be present at the demo. Leaving it in the P1-09 firewall scoping and the P1-10 test matrix would mean hardening against a path that will not be there, while the real second path — whatever the venue's network provides — goes untested. It remains a documented fact about the **lab**, and nothing more.

*Why this does not disturb the architecture.* Every rule in `CLAUDE.md` §4 and §5 is machine-agnostic: the dependency direction, atomicity, append-only ledgers, idempotency, decimal precision, server-side authority. Verified at the time of writing: `src/` contains no hardcoded address, credential or XAMPP path — `DatabaseOptions.vb` is options-driven throughout and its `127.0.0.1` default is a deliberate loopback constraint, not a personal value. **The contamination was entirely in documentation and evidence.**

*What this raises in priority without adding scope.* Phase 6 already owns release packaging, the installation guide and the exit criterion "a clean installation on a fresh machine succeeds from the guide alone" (`plan.md` §7). That criterion was one of many; under this ADR it is the primary measure of whether the deliverable exists at all. Similarly `CLAUDE.md` §9's "a clean-clone build from scratch succeeds" stops being a gate checkbox and becomes the product quality. **Currently unmet:** there is no README, no bootstrap script, and every setup step (XAMPP layout, `my.ini` `sql_mode`, `bind-address`, both database accounts and grants, the backup directory ACL, the hosts entry) was performed by hand and recorded only as prose. A classmate cloning this repository today cannot start it. That gap is now the highest-value non-architectural work in the project, and it cannot be closed before P1-06 and P1-07 exist for a bootstrap script to invoke.

**Rejected.**

- **Treating the author's laptop as the host.** Recommended earlier on 2026-08-18 and withdrawn the same day. The grounds were that the laptop already carried XAMPP, the accounts and the evidence, and that a laptop travels well. Both assume the deliverable runs on the author's hardware, which requirement 2 forbids.
- **Escalating XAMPP to the professor as unfit for a paying client (draft PA-004).** Drafted and withdrawn once it was established that the business receives nothing. ADR-000 stands unaltered, and XAMPP is a positive here: three classmates can install the entire database stack from one download. The "academic prototype" wording throughout the documents remains accurate and stays.
- **Per-site commissioning records, multi-tenancy, per-site licensing, Data Privacy Act obligations, and Windows 10 end-of-support as a security liability.** All scoped to a business running the software against real personnel and sales records. No business runs it. All dropped.
- **Rewriting git history to remove the author's personal data.** Deferred, not rejected. The repository carries home network topology, a MAC address, hostnames, tailnet addresses and the author's email on every commit. Scrubbing forward and curating the handover is likely cheaper than a rewrite, but the decision is the author's and is not yet made.

**Consequences.**

- P0-02's per-client manifest blocks describe **demo workstations**, and are filled from the classmates' machines, not from the author's desktop. The desktop is recorded separately as lab equipment.
- P0-05 no longer depends on router administration. The host address becomes an install-time variable on self-provided equipment.
- P1-09, P1-10 and P1-15 are provable **in the lab** on any two machines, because they test software behaviour across a real network boundary rather than facts about a site. They are no longer hardware-blocked.
- P1-17's dump-tool path (ADR-003.1, `C:\xampp\mysql\bin\mysqldump.exe`) must be **configured, not hardcoded**, so a differently-installed XAMPP on a classmate's machine does not break backup.

**Evidence.** `docs/environment-manifest.md` §2–§4 (lab vs demo split), `scripts/capture-client-baseline.ps1`.

---

## ADR-013 · Database identities and the append-only grant model

**Status:** ACCEPTED
**Date:** 2026-08-18
**Decides:** how many database accounts exist, what each may do, and how `StockMovements` and `AuditLogs` are made append-only *by the server* rather than by discipline. Supersedes the single-account grant shape created at P1-04. Unblocks P1-06 and makes P1-07's acceptance achievable.

**Decision.**

| Identity | Owns | Privileges on `merchandising` |
|---|---|---|
| `merch_migrator`@`localhost` | the **schema** | `SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, DROP, INDEX, REFERENCES` at database level. **No `GRANT OPTION`.** |
| `merch_api`@`localhost` | the **data** | `SELECT` at database level, and nothing else. Every write privilege is granted **per table** by `db/grants/0002_post-migration-grants.sql`. |
| `merch_backup`@`localhost` | nothing | `SELECT, LOCK TABLES, SHOW VIEW, EVENT, TRIGGER`. Unchanged from P1-04. |
| `root` | — | Used by no application, ever. Unchanged from P1-04. |

`merch_migrator` is used by `Merchandising.Maintenance` during a migration run and at no other time. The API never holds a DDL privilege.

**Reasoning — why P1-04's shape could not work.**

P1-04 created one application account holding `SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, REFERENCES` **on `merchandising`.\***. That satisfied "least privilege" read casually, and it is what `CLAUDE.md` §5 and `plan.md` §251 assumed when they promised `AuditLogs` would reject an `UPDATE` from `merch_api`.

It cannot. **MariaDB unions privileges across scopes and has no `DENY`.** A database-level `GRANT UPDATE ON merchandising.*` applies to every table in the schema, and no table-level grant can subtract from it — a table-level grant only ever adds. Measured directly rather than reasoned about:

```
mysql> SELECT Db,User,Update_priv,Delete_priv FROM mysql.db WHERE User='merch_api';
   merchandising | merch_api | Y | Y
mysql> SELECT * FROM mysql.tables_priv WHERE User='merch_api';
   Empty set
```

So P1-07's two acceptance boxes — *"`UPDATE` on `AuditLogs` as `merch_api` is rejected by privilege"* and the matching `DELETE` on `StockMovements` — were **unachievable as written**, and `CLAUDE.md` §5's "enforced by database grants as well as by policy" was a claim the machine did not support. This was found by the pre-P1-06 health check, by querying the grant tables rather than by reading the ADR.

**The fix is an inversion: append-only becomes the default, not the exception.**

Grant `merch_api` no write privilege at the database level at all. Then every table it can reach is append-only until something explicitly says otherwise, and `0002` says otherwise one table at a time. The two ledgers are simply absent from that file. A future table added by a migration is append-only until someone deliberately grants it more — which is the correct direction for the failure to point.

**Ordering constraint, measured.** MariaDB 10.4 **rejects a table-level `GRANT` naming a table that does not exist**:

```
mysql> GRANT UPDATE ON merchandising.does_not_exist_yet TO '_granttest'@'localhost';
ERROR 1146 (42S02): Table 'merchandising.does_not_exist_yet' doesn't exist
```

The per-table grants therefore cannot be pre-staged. They are a separate, numbered file applied **after** migration `0001` creates the tables. That is why `db/grants/` has two files rather than one, and why the install order is: `0001_accounts-and-grants.sql` → migration `0001_foundation.sql` → `0002_post-migration-grants.sql`.

**Reasoning — why a separate migrator identity.**

P1-04 deliberately withheld `DROP` from `merch_api`, confirmed with Max at the time. That was the right call and is kept. But `DatabaseOptionsLoader` exposes exactly one identity, so it was also the *only* identity available to the migration runner — and P1-06's acceptance requires running the runner repeatedly (*"second run applies nothing"*, *"a failing migration rolls back"*). Without `DROP` there is no way to reset the schema between runs, and `merch_api` cannot `CREATE DATABASE` either, so a throwaway scratch schema was not available as a workaround. P1-06 would have stalled on its second test.

Adding `DROP` to `merch_api` would have solved that by making the runtime account strictly more dangerous. Splitting the identity solves it while making the runtime account strictly **less** dangerous: `merch_api` now has no DDL whatsoever, where before it could `CREATE` and `ALTER` tables at will.

`GRANT OPTION` is withheld from `merch_migrator` so that a migration can never widen anyone's privileges, including its own. `0002` is an install step run by root, not a migration.

**Consequences.**

- `db/grants/0001_accounts-and-grants.sql` and `0002_post-migration-grants.sql` are the reproducible source of truth. The account setup is no longer recorded only as prose, which is a direct down-payment on ADR-012's handover requirement.
- Migration files themselves must never contain `GRANT`. Privilege changes are numbered grant scripts.
- P1-06 must load `database.migrator.json`, not `database.json`. The mechanism half of **ADR-008 remains PENDING** and is still P1-06's to resolve — this ADR settles only *which identity* runs it.
- P1-07 gains a step: run `0002` after the migration, then prove the two denials.
- Passwords are generated per installation and never committed (ADR-012 requirement 6). The `{{...}}` placeholders in `0001` are substituted at install time.

**Rejected.**

- **Granting `merch_api` `DROP`.** Solves P1-06 by widening the account that faces the network. Wrong direction.
- **Enforcing append-only with `BEFORE UPDATE` / `BEFORE DELETE` triggers.** Works, but it is enforcement by code running inside the database rather than by privilege, it is silent about *why* it refused, and `merch_api` held `ALTER` at the time — an account that can drop the trigger is not constrained by it. Privilege is the stronger claim and the one the spec asks for.
- **Leaving the database-level grant and relying on the API never issuing the statement.** That is exactly the "enforced by policy alone" position `CLAUDE.md` §5 rejects, and the failure mode is silent.

**Evidence.** `evidence/phase-1/p1-04a-grant-model-proof.txt` — 13 privilege assertions, including `UPDATE`, `DELETE` and `DROP` on an append-only table all rejected with `ERROR 1142`, `INSERT` accepted, and `merch_migrator` able to tear down what it created. Integration suite re-run green afterwards.

---

---

## ADR-014 · Error envelope and the correlation-ID contract

**Status:** ACCEPTED
**Date:** 2026-08-19
**Decides:** what an API error response is guaranteed to contain, what it is guaranteed never to contain, and what a client may send as `X-Correlation-Id`. Closes the error-model half of gaps G-03 and G-10. Raised and settled at P1-10.

**Decision.**

1. **Every** error response is a JSON `ApiErrorResponse` — stable `errorCode`, human-readable `message`, `correlationId`, and `errors` field detail where applicable. No exceptions, including responses produced by unhandled exceptions.
2. `ExceptionHandlingMiddleware` is registered **outermost**, ahead of correlation-ID handling, and converts any unhandled exception into that envelope with `errorCode = INTERNAL_ERROR`. The original exception is logged server-side in full against the same correlation ID handed to the caller, and nothing about it — type, message, stack, path — reaches the client.
3. A client-supplied `X-Correlation-Id` **must** be a UUID in the canonical 36-character `D` form. Anything else is rejected with a controlled `400 INVALID_CORRELATION_ID` before the value is bound as a SQL parameter. Omitting the header is always valid and causes one to be minted.

**Reasoning.**

Rule 3 is not input hygiene, it is the fix for a measured information leak. `AuditLogs.CorrelationId` is `CHAR(36) NOT NULL`, so under `STRICT_TRANS_TABLES` an over-length value raises `ERROR 1406` rather than truncating, and MySqlConnector's exception message names the table and column. With no exception handler in place, that message was returned verbatim to the caller together with a full stack trace and absolute source paths — **reachable unauthenticated**, because the failure occurs while writing the audit row for a login *attempt*. Captured before and after in the evidence below.

Requiring the canonical form rather than accepting anything `Guid.TryParse` allows closes three holes with one rule: over-length (the `ERROR 1406` path), CR/LF injection (the value is echoed into a *response* header), and non-UUID junk reaching the `StockMovements.CorrelationId` join key the Track D cards depend on. `TryParseExact` with `"D"` also guarantees the echoed ID is byte-identical to the one the caller sent — the `B`, `P`, `N` and `X` formats parse but round-trip to a different string, so a caller quoting its own ID to an operator would not match the logs.

Rules 1 and 2 are the general case. Rule 3 alone would have fixed this bug and left the *class* of bug open; the handler means the next unanticipated exception cannot leak either.

**Rejected.**

- *ASP.NET Core `ProblemDetails` / `UseExceptionHandler`.* Would have worked, but this project already has `ApiErrorResponse` as its published shape (P1-08), and two error envelopes on one API is worse than a hand-written handler.
- *Accepting any `Guid.TryParse` form.* Rejected for the round-trip-identity reason above.
- *Truncating an over-length ID to 36 characters instead of refusing it.* Silently changes an identifier whose entire purpose is to match across systems.
- *Leaving correlation-ID validation to "a later request-validation task",* which is where P1-08 put it. It is an error-body leak, so it belongs to the card that tests error bodies.

**Consequence for clients.** No conforming client changes: `Guid.NewGuid().ToString()` already produces the accepted form, and that is what this system generates everywhere. `ClientCommon`'s HTTP client (P1-15) must not invent its own ID format.

**Evidence.** `evidence/phase-1/p1-10-denials.txt` (PART A holds the before/after capture), plus `ExceptionHandlingMiddlewareTests` and `CorrelationIdMiddlewareTests`.

---

## Template for new entries

```markdown
## ADR-NNN · Short title

**Status:** PENDING | ACCEPTED | SUPERSEDED by ADR-MMM
**Date:** YYYY-MM-DD
**Decides:** the question this settles.

**Decision.** What was chosen.

**Reasoning.** Why, including what was measured or observed.

**Rejected.** What else was considered and why it lost.

**Evidence.** Path under evidence/.
```

