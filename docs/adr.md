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
| API test host package | `Microsoft.AspNetCore.Mvc.Testing` `10.0.9` — matches the `Microsoft.AspNetCore.App` **10.0.9** runtime measured on this machine, deliberately **not** `10.0.11`, which was the newest on NuGet when this was added. Wires `WebApplicationFactory` (P1-19); see `evidence/phase-1/p1-19-test-run.log` for the VB `BC30371` obstacle it ran into | P1-19 |
| Windows Service hosting package | `Microsoft.Extensions.Hosting.WindowsServices` `10.0.9` — same reasoning as the row above: it matches the measured `Microsoft.AspNetCore.App` **10.0.9** runtime rather than whatever is newest on NuGet. Brings `System.ServiceProcess.ServiceController` 10.0.9 transitively. Supplies `AddWindowsService` and the Event Log provider settings (P1-16). This is the API project's **first** `PackageReference` — everything before it arrived through the Web SDK's implicit framework reference | P1-16 |
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
**Decides:** storage precision for money, quantity, and timestamps. Proven live at task P1-07. **PA-003 is closed — see the note below.**

| Kind | Type | Rationale |
|---|---|---|
| Money | `DECIMAL(19,4)` | Exact arithmetic; no floating-point representation error in currency |
| Quantity | `DECIMAL(19,3)` | Hardware stores sell fractional units (metres of cable, kilos of nails) |
| Timestamps | `DATETIME(6)` UTC | Microsecond precision; UTC storage, Asia/Manila display |

**Decision.** As proposed in the baseline, confirmed by measurement rather than assumption: `0.001` and `12345678901234.5678` both round-trip exactly through real `DECIMAL(19,3)`/`DECIMAL(19,4)` columns (`StockBalances.Quantity`, `Products.Price`) on the pinned MariaDB 10.4.32 instance, first at the P0-07 pre-check and again at P1-07 against the real `Products`/`StockBalances` tables. See `evidence/phase-1/p1-07-precision-check.txt`.

**Consequence.** `Double` and `Single` are forbidden for money and quantity everywhere — storage, calculation, and transport. Use `Decimal` in VB.

**PA-003 closed 2026-08-22 — there was never an approval to wait for.** The spec had volunteered this decision as "subject to professor approval," but storage precision violates neither course constraint, so it is this project's decision to make on evidence. It is made: the table above, confirmed by measurement. `p1-07-precision-check.txt` also demonstrates why the decision cannot wait on the database to enforce it: `UPDATE ... SET Cost = 0.19995` against `DECIMAL(19,4)` under `STRICT_TRANS_TABLES` silently stores `0.2000` (`Note 1265`, not an error) — exactly the ADR-004.1 hole. P1-07 is closed with it; the card had been held at 🟡 by that paperwork alone while every technical box was ticked and evidenced.

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

**Status:** ACCEPTED
**Date:** 2026-08-19
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

**Evidence:** `evidence/phase-1/p1-13-concurrency.txt` — two, then ten, simultaneous requests against one unit of stock, run seven times, always produce exactly one success and a final balance of zero.

> **P1-11 progress (2026-08-19) — mechanism implemented and proven; status stays PENDING.** The exact SQL above is live in `StockRepository.TryDecrementAsync` (`src/Merchandising.Infrastructure/Data/StockRepository.vb`), called from `StockService.DecrementAsync` inside one transaction alongside the `StockMovements` and `AuditLogs` inserts (P1-11: `POST /api/v1/inventory/stock/decrement`). Proven so far: the happy path produces exactly one movement/audit row with a correct balance; an insufficient-stock request is rejected with a controlled 409 and writes zero rows; both are backed by an integration test against the real database *and* a live HTTP capture cross-checked directly against the schema (`evidence/phase-1/p1-11-happy-path.txt`, `p1-11-insufficient-stock.txt`). **What this does not yet prove:** the actual concurrency claim this ADR exists for — that two or more simultaneous requests against the same low-stock row cannot both succeed. `READ COMMITTED` is set per-connection (ADR-003.2/P1-05) but, per the Reasoning above, is not what this design relies on; InnoDB row-locking on the conditional `UPDATE` is what should serialize concurrent attempts, and that claim is untested until P1-13 fires real concurrent requests at it. Status stays **PENDING** until then — P1-12 (rollback) and P1-13 (concurrency) are still open.
>
> **P1-12 progress (2026-08-19) — rollback proven; status stays PENDING.** A test-only fault injection point was added to `StockService.DecrementAsync`: an exception thrown after both the `StockMovements` and `AuditLogs` inserts, still inside the open transaction, immediately before `CommitAsync`. It is deliberately uncaught — it propagates out through the enclosing `Using connection`, whose `Dispose()` severs the connection and lets MariaDB roll back whatever was still open, per the Decision above ("else roll back"). Proven: balance, movement row, and audit row are all identically absent before and after the forced failure (`evidence/phase-1/p1-12-rollback.txt`), and the same fault is structurally inert in a Release build — proven empirically by running the identical call under both configurations, not just by inspection. **What this still does not prove:** the concurrency claim. Status stays **PENDING** until P1-13.
>
> **P1-13 (2026-08-19) — concurrency claim proven; status moves to ACCEPTED.** `Decrement_TwoThenTenSimultaneousRequests_ExactlyOneSucceedsEachRound` (`StockDecrementTests.vb`) fires two, then ten, simultaneous `DecrementAsync` calls against a dedicated fixture product holding exactly one unit of stock — each call opens its own connection and transaction, so this is genuine concurrent contention at the database, not a simulation. Run seven times against the real pinned MariaDB instance (fourteen rounds total): every round produced exactly one `Success` and the rest `InsufficientStock`, zero unexpected exceptions, final balance `0.000`, exactly one `StockMovements` row per round — cross-checked directly against the database, not just through the test's own assertions (`evidence/phase-1/p1-13-concurrency.txt`). This is what the Reasoning above predicted: InnoDB row-locking on the conditional `UPDATE` serializes the race; `READ COMMITTED` is set (ADR-003.2/P1-05) but is not what the guarantee depends on. All three legs this ADR needed — mechanism (P1-11), rollback (P1-12), concurrency (P1-13) — are now proven against the real database. **Status: ACCEPTED.**
>
> **P3-03 (2026-08-27) — qualification, not a reversal: `READ COMMITTED` is not actually in force inside a transaction, and this ADR never depended on it.** Measured while implementing purchase-order creation: inside a transaction opened by `connection.BeginTransactionAsync(cancellationToken)`, `SELECT @@tx_isolation` reports **`REPEATABLE-READ`**. `ConnectionFactory` does issue `SET SESSION tx_isolation = 'READ-COMMITTED'` on every connection (ADR-003.2/P1-05) and `ConnectionFactoryTests` asserts that session value truthfully — but **MySqlConnector's `BeginTransaction` sends its own `SET TRANSACTION ISOLATION LEVEL` for the transaction it opens**, which overrides the session setting. Nothing had ever compared the two, because no test read `@@tx_isolation` from *inside* a transaction.
>
> **Why the ACCEPTED status stands.** The Reasoning above says it in as many words — "Isolation level alone is explicitly **not** relied upon." The stock-decrement guarantee rests on InnoDB row-locking on the conditional `UPDATE`, and a locking read sees the latest committed row at either isolation level. P1-13's fourteen rounds proved the real mechanism and would have produced the same result either way. No evidence in this ADR is invalidated.
>
> **What it did break, and where.** A transaction that *reads* before it writes gets a stale snapshot under REPEATABLE READ. `PurchaseOrderNumberGenerator`'s `SELECT MAX(OrderNumber)` could not see order numbers committed after its transaction began, so every retry recomputed the same candidate while the `INSERT` (current, as writes always are) kept colliding: ten concurrent creates failed nine of ten. `PurchaseOrderService` now passes `IsolationLevel.ReadCommitted` explicitly and the test passes. Full measurement, including the captured `tx_isolation` readings, in `evidence/phase-3/p3-03-purchase-orders.txt` §5.
>
> **What is still owed, carried as CARRY-03 in `tasks.md`.** Every other `BeginTransaction` call site still opens REPEATABLE READ, and `ConnectionFactoryTests` still asserts only the session value. The lesson worth keeping is the one CLAUDE.md §6.2 already half-states: setting the isolation level on the **session** is not sufficient — it must be passed at `BeginTransaction`, and asserted from inside a transaction.
>
> **P4-04 (2026-08-29) — CARRY-03 closed. The mechanism, stated explicitly.** `ConnectionFactory.CreateOpenConnectionAsync` issues `SET SESSION tx_isolation = 'READ-COMMITTED'` on the **connection's session**. MySqlConnector's `connection.BeginTransactionAsync(...)` then sends its own `SET TRANSACTION ISOLATION LEVEL` for the **transaction it is about to open**, and that statement wins — it does not read, inherit, or extend the session value; it replaces it, for that transaction only, every time. A session-level `SET` therefore proves nothing about what any later transaction on that connection actually runs under. The only form of this guarantee that holds is passing `IsolationLevel.ReadCommitted` as `BeginTransactionAsync`'s own argument, at every call site, every time — and the only way to prove it held is reading `@@tx_isolation` from a query issued **inside** that transaction, never from the session alone.
>
> Every `BeginTransactionAsync` call site under `src/Merchandising.Api` now passes `IsolationLevel.ReadCommitted` explicitly — `StockService.DecrementAsync`, `PriceChangeService.ChangePriceAsync`, `ProductLifecycleService.SetActiveStateAsync`, `SupplierLifecycleService.SetActiveStateAsync`, `SuppliersController` (create, update), `ProductsController` (create, update), `SystemSettingsController.SetSetting`, and the three `PurchaseOrderService` sites P3-03 had not yet reached (`SubmitAsync`, `ApproveAsync`, `TransitionWithReasonAsync` serving cancel/close) alongside the `CreateAsync` site P3-03 already fixed. The card's own source scan found the task card's enumerated list stale in both directions — it named `MaintenanceController`, which opens no transaction at all, and omitted `SystemSettingsController` and three of `PurchaseOrderService`'s own sites — confirming this ADR's own wording ("every **other** call site", not a fixed list) rather than the card's snapshot. `ConnectionFactoryTests` gained `OpenedConnection_TransactionIsolation_MatchesWhatIsPassedToBeginTransaction`, asserting both halves live against the real database: a transaction opened with no isolation argument reads `REPEATABLE-READ` from inside itself; one opened with `IsolationLevel.ReadCommitted` reads `READ-COMMITTED` from inside itself. `StockDecrementTests` gained a genuine failing-test-first proof at the production call site itself (`Decrement_OpensTransactionAtReadCommittedIsolation`, red under the unfixed code, green after), and the P1-13 concurrency proof was rerun unchanged and still green — this was a text-vs-reality fix, not a behaviour change, exactly as ADR-006's own Reasoning predicted (row-locking on the conditional `UPDATE`, not isolation level, is what the concurrency guarantee has ever depended on). Full measurement: `evidence/phase-4/p4-04-isolation-level.txt`.
>
> **P4-15 (2026-08-29) — the guarantee extended to a second command *pair*, at the Phase 4 gate.** Every concurrency proof this ADR carried until now put one command against *itself*: decrement vs decrement (P1-13), return vs return (P4-08). The Phase 4 exit gate's fifth criterion claims something different — that a **receive** and an **adjustment** contending for the same `StockBalances` row is safe — and the closure pack found no test fired it (`evidence/phase-4/INDEX.md` §4.1). `ReceiveAndAdjustConcurrencyTests` now does: four receives and four adjustments in flight against one product settle to the exact arithmetic total, and a +5.000/−5.000 race lands on one of only two consistent end states with both orderings genuinely observed. Both tests were watched fail against a deliberately broken `StockRepository` — a read-then-write `IncrementAsync` produced the lost update this ADR's conditional write exists to prevent (`Expected:<136.000>. Actual:<106.000>`), and removing the `AND Quantity >= @qty` guard produced a recorded balance of `-3.000`.
>
> **One finding worth carrying into Phase 5, because it will recur wherever this ADR is tested.** The first draft of the ordering test *could not fail*. Two reasons, both structural rather than accidental: a product that has never been received has **no `StockBalances` row at all**, so the conditional write's row-*existence* refuses the decrement and the `Quantity >= @qty` guard is never reached — a test against a fresh product proves the wrong clause; and a **transient** negative balance is erased by a later increment, so no assertion on the final stored balance can see one. The fix generalises: seed a real opening balance so the guard is load-bearing, and assert over `StockMovements`, where `QuantityAfter` is append-only and a momentary negative is permanent. Full record: `evidence/phase-4/p4-15-concurrent-receive-and-adjust.txt`.

---

## ADR-007 · Idempotency for transactional commands

**Status:** ACCEPTED
**Date:** 2026-08-19
**Decides:** how retries avoid creating duplicate sales, receipts, returns, or adjustments. Closes gap G-13.

**Decision.** `IdempotencyKeys` table with a unique constraint on `(Scope, KeyValue)`. Insert-first strategy: the command claims its key before doing work, and the committed response payload is stored and replayed verbatim on repeat.

**Proven at P1-14** against `StockService.DecrementAsync` — the only transactional command that exists in this phase. `IdempotencyStore.TryClaimAsync` inserts the claim as the first statement inside the same transaction as the balance/movement/audit writes; `CompleteAsync` writes the response payload onto that row, still inside the same transaction, immediately before commit. A losing claim means MariaDB's unique-index row lock made a concurrent `INSERT` block until the winning transaction resolved — the same locking mechanism ADR-006/P1-13 already proved, applied here rather than assumed fresh. The loser rolls back its own no-op transaction and reads the winner's committed payload back under `READ COMMITTED` to replay verbatim.

**One friction point, resolved without changing the design.** Because the claim lives inside the same transaction as the rest of the command, an `InsufficientStock` rollback rolls the claim back too — a key from a failed attempt needs no separate expiry/cleanup path to be reusable on a legitimate retry (Done-when box 4). This fell out of the existing transaction boundary rather than requiring new code.

**Evidence.** `evidence/phase-1/p1-14-idempotency.txt` — four new integration tests (`StockDecrementTests.vb`), one per Done-when box, including a 10-concurrent-request same-key round with the raw movement-Id distribution captured (all 10 report the same movement, none error). Full suite: 35/35 integration tests green (4 new), 8/8 unit tests, guardrails pass, 0 warnings.

---

### ADR-007.1 · A same key, a different body, is refused rather than replayed

**Status:** ACCEPTED
**Date:** 2026-08-30
**Amends:** the ADR-007 decision above. "A repeated key returns the original committed result" always meant the same command sent twice; it never covered a caller reusing a key with a genuinely different body, and the original `IdempotencyKeys` schema had no way to even represent that distinction.

**What changed.** `IdempotencyKeys` gains a nullable `RequestHash CHAR(64)` column (`db/migrations/0013_idempotency-request-hash.sql`) — the same fixed-width hex-digest shape `Sessions.TokenHash` already uses. `IdempotencyStore.TryClaimAsync` gains an `Optional requestHash` parameter, stored on the claiming `INSERT`; on a **losing** claim it also reads the winner's stored hash back as `ExistingRequestHash` (safe, because by the time a claim loses, the winning transaction has already committed — the same guarantee the base ADR already established for `ResponsePayload`). `SaleService.CompleteAsync` is the first, and so far only, consumer: it computes a SHA-256 over the request's meaningful fields (every line's `ProductId`+`Quantity`, the payment method, the tendered amount) before claiming, and a losing claim whose stored hash disagrees is refused (`IdempotencyKeyReused`, `409 IDEMPOTENCY_KEY_REUSED`) rather than replayed.

**Why P5-09, not earlier.** Every idempotency Done-when box before this phase asked "does a retry avoid a duplicate," never "does a *different* request get caught." P5-09 is the first card to require the second question, for the largest transaction in the system.

**Additive, not breaking.** `requestHash` is `Optional` and placed *after* `cancellationToken`, not before it — every existing caller (`StockService`, `ReceivingService`, `AdjustmentService`, `CashierSessionService`, `PurchaseOrderService`, `StockCountService`, `PurchaseReturnService`) passes `cancellationToken` positionally as the 5th argument today; inserting a new parameter ahead of it would have silently reordered every one of those call sites into a type mismatch. None of them pass a hash, none of them are affected, and none needed to change. `ExistingRequestHash` being `Nothing` — a claim made before this migration, or by a scope that never opts in — is **not** treated as a mismatch: there is no evidence of a different body, so the pre-existing "always replay" behavior applies. This is what makes the change safe for keys already in flight at the moment the migration runs, rather than a breaking change disguised as an addition.

**Rejected.**

- **A `RequestHash` comparison scoped outside `IdempotencyKeys` (e.g. a side table, or reconstructing the hash from the stored response).** The response is server-computed output (captured price, computed totals), not the client's input — two different requests can legitimately produce output that hashes the same, and the actual input is not reliably recoverable from it. Storing the input's own hash directly is the only version of this that is actually checking what the rule cares about.
- **Making every scope hash its request immediately.** Rejected as scope creep for this card: nothing else in the Done-when list asks for it, and the mechanism is opt-in per scope by construction, so a later card can extend it without a further migration.

**Evidence.** `evidence/phase-5/p5-09-idempotent-sale.txt`.

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

> **P1-16 result (2026-08-21) — the self-contained choice held, and the "fails silently" reasoning proved to be the right shape of worry, in an unexpected place.** The API is installed and serving as a Windows Service from a self-contained `win-x64` publish (350 files, 106.2 MB — the growth over the 347/105.1 MB measured here is `Microsoft.Extensions.Hosting.WindowsServices` 10.0.9 and its transitive dependency). It survived a real reboot unattended: `evidence/phase-1/p1-16-post-reboot-health.txt`.
>
> The failure class this ADR was written to avoid did not occur — but a structurally identical one nearly did, and it is worth recording next to the original argument. The API and MariaDB are both automatic-start and nothing ordered them. Because the API reads its database configuration from a *file* and does not open a connection until the first request, an API that won that race would have started cleanly, served `/health` with a 200, and reported healthy **with no database behind it** — the failure surfacing later as the first login of the morning. Same signature as the missing-runtime case: a service that looks fine and is not. Closed at P1-16 with a `depend=` on the MariaDB service, detected by image path rather than hardcoded, since under ADR-012 the service's name is a property of someone else's XAMPP installation.
>
> This does **not** close G-16. Nothing here was run on a machine without the .NET runtime — the item below stands.

**Reasoning — Maintenance.** Symmetry with the API is the weaker argument. The stronger one: this utility owns **backup and restore** (P1-17, P1-18). It is the tool you reach for when the system is already broken, and a recovery tool that depends on a correctly installed runtime can fail for the same reason you are running it.

**Reasoning — WPF clients.** Spec §18 is explicit, and nothing measured here contradicts it. Self-contained would cost roughly 150 MB per client across three clients to avoid one documented install step per laptop. The prerequisite is declared by the package itself — `Merchandising.Inventory.runtimeconfig.json` names `Microsoft.WindowsDesktop.App 10.0.0` as a hard requirement — so a client without it fails at launch rather than misbehaving subtly.

**Also established at P1-03:** a Visual Basic **WPF** application publishes `win-x64` and runs (window opens, responds, closes cleanly). That was untested before — P0-07's probe covered the API only, and P1-01 built the clients without publishing or launching one.

**Fixed:** `win-x64` runtime identifier throughout, applied at publish time by `scripts/publish-release.ps1` and never set globally in `Directory.Build.props`. Never `PublishAot` or `PublishTrimmed` — neither works with Visual Basic. The publish script **refuses to run** if either resolves to `true`, which covers the hole G-D cannot see: G-D reads `.vbproj` text and cannot detect `-p:PublishAot=true` passed on a command line.

**Still open — G-16 is begun, not closed.** Its required evidence is a *release manifest* and a *clean-machine installation test*. Neither exists yet:

- **No machine without the .NET runtime has been tested.** The self-contained choice above is reasoned from the spec's condition, not proven by a runtime-free host.
- **The WPF Desktop Runtime prerequisite *check* is unvalidated** and cannot be validated here — this machine has the Desktop Runtime, so a framework-dependent client runs whether or not the check works. Carried to **P1-15**.

  **P1-15 update (2026-08-19): still carried, and now with the client actually built.** `Merchandising.Inventory` is a working framework-dependent WPF client as of P1-15, so there is finally something to run on a runtime-free machine — but this one is not that machine, and the reason is sharper than "it is the dev laptop". This host has SDK `10.0.301`, which brings `Microsoft.WindowsDesktop.App 10.0.9` with it, so the launch failure this item exists to observe **cannot occur here by definition**. Running the client locally would not weakly support the check; it would be silent about it. The item closes on a machine with the Desktop Runtime and **no** SDK, in the same sitting as P1-15's box 1.
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

### ADR-011.1 · The TLS protocol floor is pinned in code, not inherited from the host

**Status:** ACCEPTED
**Date:** 2026-08-21
**Decides:** which TLS versions Kestrel's HTTPS listener will negotiate, and where that decision lives. Resolved at P1-21. Hardens G-07.

**Decision: `SslProtocols.Tls12 Or SslProtocols.Tls13`, set in Visual Basic source, with no configuration key that can override it.** Implemented as `Merchandising.Api.Security.TlsPolicy` and applied from `Program.vb` through the three-argument `UseHttps(path, password, configureOptions)` overload.

**The hole.** P1-09 shipped `listenOptions.UseHttps(pfxPath, password)` — the two-argument overload, no `SslProtocols`. Kestrel then defers to Schannel, so the effective protocol floor was whatever the host operating system permitted. On the development machine that is harmless: its `SCHANNEL\Protocols` key exists but is completely empty — 0 subkeys, 0 values, measured at P1-21 rather than assumed — so every protocol sits at the Windows 11 default.

**Why that is not good enough under ADR-012.** The API does not stay on that machine. It is handed to three classmates and run on a Windows 10 host that nobody in this project configures or inspects, where TLS 1.0 and 1.1 can still be enabled at the Schannel level. "This API speaks modern TLS" would then be a property of someone else's registry rather than of the delivered software — and the failure mode is silent, because a downgraded connection is indistinguishable from a good one at the client.

This is the same argument that put `STRICT_TRANS_TABLES` in `ConnectionFactory` as well as in `my.ini` (ADR-003.2 / P1-05): **a guarantee an unfamiliar host can quietly revoke is not a guarantee.** Both cases resolve the same way — assert it in code, where it ships with the product.

**Why not TLS 1.3 alone.** Considered and rejected. Every client in this system is .NET 10 and would negotiate 1.3 happily, but TLS 1.3 requires Windows 11 or Server 2022 on the Schannel side, and ADR-012 makes the host's Windows version a fact about a classmate's laptop rather than a decision this project gets to make. A 1.3-only listener would refuse every connection on a Windows 10 host. TLS 1.2 is the floor that is still sound and still universally available.

**Why it is not configurable.** No appsettings key, no environment variable, no constructor parameter. The value a host could override is precisely the value this decision exists to take away from the host.

**How the wiring was proven, since no in-process test can prove it.** The `ConfigureKestrel` block is skipped under the `Testing` environment (the P1-19 certificate guard), so `TlsPolicyTests` can prove the policy but not that `Program.vb` calls it. The proof is a controlled A/B against the real published binary: with the pin at `Tls12 Or Tls13` a client offering everything negotiated **TLS 1.3**; with `AllowedProtocols` temporarily narrowed to `Tls12` and nothing else changed, the same client on the same machine was forced down to **TLS 1.2**, and a TLS-1.3-only client was refused with a `HandshakeFailure` alert *sent by Kestrel*. The listener's protocol set demonstrably follows this decision.

**Origin.** Raised by the P1-09 cross-machine lab run, where `merch-laptop` reported `Tls12` against a host-side record of `Tls13` and flagged the mismatch instead of reporting the expected value. That reading was a harness artifact — Windows PowerShell 5.1 runs on .NET Framework, whose `SslStream` does not offer TLS 1.3 — but chasing it is what exposed the unpinned listener behind it. `scripts/probe-tls.ps1` now prints its own runtime on every run and refuses to execute on .NET Framework, so that particular confusion cannot recur silently.

**Evidence:** `evidence/phase-1/p1-21-tls-pinning.txt`

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
- **Escalating XAMPP to the professor as unfit for a paying client.** Drafted and withdrawn once it was established that the business receives nothing. (An earlier revision of this line called the draft "PA-004"; the number was never issued to it and belongs to the performance-targets entry.) ADR-000 stands unaltered, and XAMPP is a positive here: three classmates can install the entire database stack from one download. The "academic prototype" wording throughout the documents remains accurate and stays.
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
- P1-06 must load `database.migrator.json`, not `database.json`. The mechanism half of **ADR-008 was PENDING when this was written** and was P1-06's to resolve — this ADR settles only *which identity* runs it. *(Resolved 2026-08-18: ADR-008 is **ACCEPTED**. Tense corrected at P1-20 because this line was still reading as a live obligation — the closure pack's job is to leave no entry that a reader can mistake for outstanding work.)*
- P1-07 gains a step: run `0002` after the migration, then prove the two denials.
- Passwords are generated per installation and never committed (ADR-012 requirement 6). The `{{...}}` placeholders in `0001` are substituted at install time.

**Rejected.**

- **Granting `merch_api` `DROP`.** Solves P1-06 by widening the account that faces the network. Wrong direction.
- **Enforcing append-only with `BEFORE UPDATE` / `BEFORE DELETE` triggers.** Works, but it is enforcement by code running inside the database rather than by privilege, it is silent about *why* it refused, and `merch_api` held `ALTER` at the time — an account that can drop the trigger is not constrained by it. Privilege is the stronger claim and the one the spec asks for.
- **Leaving the database-level grant and relying on the API never issuing the statement.** That is exactly the "enforced by policy alone" position `CLAUDE.md` §5 rejects, and the failure mode is silent.

**Evidence.** `evidence/phase-1/p1-04a-grant-model-proof.txt` — 13 privilege assertions, including `UPDATE`, `DELETE` and `DROP` on an append-only table all rejected with `ERROR 1142`, `INSERT` accepted, and `merch_migrator` able to tear down what it created. Integration suite re-run green afterwards.

---

### ADR-013.1 · `merch_backup` gains `INSERT` on one table

**Status:** ACCEPTED
**Date:** 2026-08-22
**Amends:** the ADR-013 table above, which describes `merch_backup` as holding `SELECT, LOCK TABLES, SHOW VIEW, EVENT, TRIGGER` and owning *nothing*.

**What changed.** `merch_backup` now also holds `INSERT` on `merchandising.backuplogs`, granted by `db/grants/0004_backup-grants.sql`. This is a real widening of a least-privilege account and is recorded here rather than left to be discovered in `mysql.tables_priv` by whoever next audits the grants.

**Why it was necessary.** P1-17 gives the backup job a ledger (spec §15, "Integrity": record size, checksum, timestamp, source database version, result). The job runs as `merch_backup` from Task Scheduler, entirely outside the API process. With no write privilege anywhere it could not record its own run — and a backup that cannot say whether it worked is the failure mode the whole card exists to prevent.

**Why not the obvious alternatives.**

- **Write the row as `merch_api`.** Rejected. It would mean one job carrying two identities, and the audit trail would say `merch_api` performed a backup it had no part in. ADR-013's value is that each job maps to exactly one account; blurring that to avoid a one-table grant trades a clear model for a smaller number.
- **Write the row as `merch_migrator`.** Worse. That account exists solely to own the schema during a migration run, and reaching for it during ordinary operations is how a DDL-capable credential ends up in a nightly scheduled task.
- **Log only to a file.** Rejected as the *primary* record — spec §12 lists `BackupLogs` as a table — but adopted as the fallback when the database is unreachable, which is precisely the case where a database-only record would be no record at all.

**What is deliberately still withheld.** No `UPDATE` and no `DELETE`, so `BackupLogs` is append-only on the same footing as `StockMovements` and `AuditLogs`: a failed run cannot be edited into a successful one, it gets a second row. No privilege on any other table — proven, not asserted:

```
UPDATE backuplogs  -> ERROR 1142  (denied)
DELETE backuplogs  -> ERROR 1142  (denied)
INSERT auditlogs   -> ERROR 1142  (denied)
```

That third denial is the one worth reading twice. It proves the grant is scoped to `backuplogs` alone rather than quietly widened across the schema, so the backup utility could not write elsewhere even if it were compromised.

**Retention needs no privilege here.** Pruning deletes *files* on disk, not rows, so no `DELETE` is required for it — and the ledger keeps a permanent record of dumps whose files are long gone, which is the right asymmetry.

**Evidence:** `evidence/phase-1/p1-17-backup-success.log` §4.

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

## ADR-015 · Demo network topology — the host is the access point

**Status:** ACCEPTED
**Date:** 2026-08-22
**Supersedes:** ADR-012's §4.2 requirement for self-provided demo network equipment.
**Decides:** what network the three clients and the API host are on during the presentation.

**Context.** Two facts closed off the previous plan. The school Wi-Fi is heavily firewalled and cannot carry a client-server demo. And the self-provided travel router ADR-012 called for was never acquired — it stood as the largest un-mitigated risk to the presentation for four days without moving. The risk it existed to mitigate was **AP client isolation**: venue Wi-Fi commonly blocks station-to-station traffic while leaving internet access intact, which kills a client-server demo minutes before presenting with no fix available on someone else's equipment.

**Decision.** **The API host laptop is itself the access point**, via Windows Mobile Hotspot. The three client laptops join it directly. No router, no venue network, no purchase.

**Why this is not merely cheaper but strictly better than the router it replaces.**

1. **Client isolation cannot apply.** Every client talks to its own default gateway, which *is* the host. There is no station-to-station hop for an AP to block. The entire risk class the demo rig existed to mitigate is removed rather than mitigated.
2. **The host address stops being a variable.** Windows Internet Connection Sharing pins the hotspot interface at **`192.168.137.1`**, deterministically, on every Windows 10 and 11 machine. The `MERCH-HOST` hosts-file entry stops being a per-network value that must be rewritten on four machines each time the network changes, and becomes a fixed line written once per client and never touched again.
3. **Nothing to buy, carry, power, or configure** under time pressure on the day.
4. **No uplink required.** The system is LAN-only; the hotspot needs no working internet for the demo itself to function.

**Measured on this machine, 2026-08-22.** The Intel Wi-Fi 6 AX101 reports `Soft AP: Not supported` — and `netsh wlan show drivers` correspondingly reports `Hosted network supported: No`. **That is not the blocker it appears to be.** It describes the legacy `netsh wlan set hostednetwork` SoftAP path, which Intel dropped. Modern Windows Mobile Hotspot runs over **Wi-Fi Direct GO**, which the same adapter reports as `Supported` (along with `P2P GO on 5 GHz`). `netsh wlan show wirelesscapabilities` is the check that matters; `show drivers` is the one that misleads.

**Fallback, and it is a real one.** If Mobile Hotspot cannot be started on the host, all four machines join a **phone hotspot** instead. The host is then an ordinary DHCP client and its address varies per session, so the constant in consequence 2 is lost — but nothing else is. `scripts/setup-client.ps1` accepts `-HostIPv4` precisely so the fallback needs no code change, only a different argument.

**Consequences.**

- **`setup-client.ps1` defaults `-HostIPv4` to `192.168.137.1`.** This is a deliberate reversal of the rule stated in `capture-client-baseline.ps1`, which refuses to default a host address on the grounds that "a wrong-but-plausible address does not fail cleanly." That reasoning was correct while the address was a *lab fact*. Under this ADR it is a *design constant*, and defaulting to it is what makes the client setup a single command with no value to communicate. The fallback topology is the case where the default is wrong, and there it is overridden explicitly.
- **The firewall rule needs attention it did not previously need.** Windows classifies a newly created hotspot network as **Public**. The P1-09 rule is scoped `-Profile Private`, so on an unclassified hotspot the API is unreachable — presenting as a connection timeout that reads like a code defect. Reclassifying the hotspot network to Private is now part of host-side demo setup, and is deliberately preferred over widening the rule to the Public profile, which would weaken the posture on every network the laptop ever joins.
- **P0-05 is no longer blocked on hardware acquisition.** It is blocked on one rehearsal.
- The manifest's §4.2 stops describing equipment to buy and starts describing a configuration to verify.

**Risks accepted, both closing at the rehearsal.**

- **Mobile Hotspot may refuse to start without an internet connection to share.** Windows asks which connection to share, and with the school Wi-Fi blocked there may be nothing to select. Mitigation if it bites: tether a phone over USB and share *that* adapter — the tether supplies the shareable connection while the Wi-Fi radio serves the hotspot.
- **Concurrent station + Wi-Fi Direct GO on a single radio** is supported by Intel adapters in general but is unverified on this one. If it fails, the host cannot stay on another Wi-Fi network while hosting — which costs nothing for the demo, since no uplink is needed.

**Neither risk is resolvable by reasoning.** The rehearsal is the evidence, and it is now the gating item for P0-05.

---

### ADR-015.1 · A maintenance lock inside the database cannot guard a restore of that database

**Status:** ACCEPTED (practical hole closed) · **deeper fix deferred to Phase 6/7**
**Date:** 2026-08-22
**Found by:** running the P1-18 restore rehearsal for real, not by any test.

**What happened.** `MaintenanceLocks` is a table in `merchandising`. The rehearsal acquired a lock, then restored a dump taken minutes earlier — predating the lock — and the restore erased the row. Step 7's release then failed with `MAINTENANCE_NOT_ACTIVE`, because there was genuinely nothing left to release.

**Why it matters more than it first appears.** With verification passing, the system should reopen anyway, so the outcome was correct by accident. With verification **failing**, `restore-rehearsal.ps1` reports "maintenance deliberately LEFT ON" — while the lock no longer exists. The system would be **open for business while every human involved believed it closed**, which is the precise failure the lock was built to prevent.

**Closed for now** by re-inserting the lock immediately after the restore, while the API service is still stopped, so no window exists in which the API is running and unlocked. It cannot be closed by calling the enter endpoint again after startup: that window *is* the problem.

**The general lesson.** A guard stored inside the resource it guards does not survive that resource being replaced. The same shape would bite any lock, flag or lease kept in the database it protects.

**Deferred, deliberately.** The durable fix is a maintenance flag outside the database — a file the middleware also consults — so no restore can clear it. That is a change to the enforcement path and belongs with Phase 6/7 hardening rather than being bolted on at the end of Phase 1. Recorded here so it is a known decision rather than an oversight someone rediscovers.

**Evidence:** `evidence/phase-1/p1-18-rehearsal-run.txt`, step 7.

---

## ADR-016 · P0-02 and P0-05 carry past the Phase 1 gate to the Phase 6 gate

**Status:** ACCEPTED
**Date:** 2026-08-22
**Decides:** where the two remaining machine-dependent Phase 0 criteria are owed, now that `plan.md` §5's carry-forward rule has been exhausted by Phase 1 arriving with them still open.

**Decision.** The Phase 1 gate closes on its **engineering** criteria. Two criteria carry forward to the **Phase 6** gate, which is where `plan.md` §7 already puts installation, packaging and handover:

| Carried criterion | Owning card | Lands at |
|---|---|---|
| Every demo workstation captured — edition, build, architecture, resolution, scaling, local administrator rights | **P0-02** | Phase 6 gate |
| `ping MERCH-HOST` + validated HTTPS round trip from every demo workstation; demo network rehearsed from cold | **P0-05** | Phase 6 gate |

Neither may be closed with lab hardware. `DESKTOP-G83CCSH` and `DESKTOP-F5LK8MA` remain explicitly **not** substitutes (ADR-012, manifest §3.1).

**Reasoning.** Phase 1 exists to retire technical uncertainty about the VB toolchain, the connector, the transaction pattern, the security seam, the hosting model and recovery. Every one of those is retired with executed evidence, reproduced from a clean clone at commit `41ee833`: guardrails G-A–G-D pass, build 0 warnings / 0 errors, 23 unit and 62 integration tests green against the real pinned MariaDB 10.4.32. Eighteen of spec §24's nineteen rows are proven by artifacts on disk.

The nineteenth — *Planning baseline* — is complete on its decision half (all twenty ADRs ACCEPTED) and incomplete only on its environment half. What is missing is not a proof; it is a **population**. The mechanisms were proven across a real network boundary on two machines that are not the three the system will be demonstrated on.

Phase 6's exit criterion — *"a clean installation on a fresh machine succeeds from the guide alone"*, which ADR-012 already promoted to the measure of whether the deliverable exists at all — **cannot be satisfied without both carried items**. That makes Phase 6 their natural home rather than a convenient one: a clean install on a classmate's laptop *is* the demo-workstation survey and *is* the network rehearsal, performed for real instead of recorded in advance.

**What this costs, recorded because it was argued at the gate and overruled deliberately.** The two items are not the same kind of risk, and bundling them understates one of them:

- **P0-02 is field work.** Information about hardware this project does not own, zero design risk, genuinely deferrable.
- **P0-05 is an unretired assumption.** ADR-015 chose the demo topology on 2026-08-22 and was accepted on `netsh wlan show wirelesscapabilities` reporting `Wi-Fi Direct GO: Supported` — a capability reading, not a cold start. Two questions stay open that no reasoning settles: whether Windows Mobile Hotspot starts with **no connection to share**, and whether this Intel adapter sustains station + Wi-Fi Direct GO **concurrently**. Both are answerable on the author's own laptop plus one lab client, in about fifteen minutes, with no classmate involved. If either answer is no, **ADR-015 is wrong** and the demo network needs re-planning — which is exactly the class of finding Phase 1 exists to surface early.

Deferring it is the author's call and is recorded as such. The consequence is that a topology decision now rides to Phase 6 unverified, and the mitigation is that Phase 6's gate cannot pass without exercising it.

**One risk is worth pulling forward at no cost:** whether each classmate holds **local administrator rights** on their own laptop. It is one message today, it needs no machine in the room, and without it neither the certificate import nor the hosts entry completes — `scripts/setup-client.ps1` reports it in stage 1 rather than failing halfway through stage 2. It is the only carried item that fails late and unfixably.

**Rejected.**

- *Ticking the boxes and declaring PASS.* The manifest's own withdrawn-DHCP-reservation entry refuses exactly this, and `evidence/phase-1/INDEX.md` §4 was written to prevent it. An index that overstates is worse than no index.
- *Starting Phase 2 with the gate left at FAIL.* The gate's authority comes from being enforced. Moving a criterion by amendment preserves it; ignoring a FAIL spends it.
- *Inventing a PARTIAL.* `plan.md` §6.2 defines PARTIAL solely as "descended to rung B, everything else passes". Rung A built clean on the first attempt with zero friction (`evidence/phase-0/p0-07-rung-a-preflight.txt`), so no partial outcome exists to claim.
- *Carrying to Phase 7 instead.* Phase 7 is the demo rehearsal and UAT. Arriving there with unsurveyed machines means discovering an admin-rights or adapter problem during rehearsal, with no phase left to absorb it.

**Precedent.** This is the second application of the same principle, not a new one. `plan.md` §5 already carried these criteria from the Phase 0 gate into Phase 1 on the grounds that behaviour is proved by any second machine while facts are proved only by the machine they are facts about. That distinction is unchanged; only the destination moves.

**Evidence.** `evidence/phase-1/INDEX.md` §4.4 · `evidence/phase-1/p1-20-clean-clone.log` · `docs/environment-manifest.md` §5.

---

## ADR-017 · Policy naming, the matrix's single source of truth, and what user management isn't

**Status:** ACCEPTED
**Date:** 2026-08-25
**Decides:** how spec section 9's role table becomes named ASP.NET Core authorization policies (P2-02), where `docs/role-permission-matrix.md` lives relative to the code, and the naming scheme every future policy follows.

**Decision.**

1. **One named policy per operation, not per role.** Spec section 9's five rows are decomposed into ~29 operations, grounded in section 13's API-area breakdown (Products, Suppliers, Procurement, Inventory, POS, Reports) rather than an invented split of section 9's prose — section 13 is where the spec itself already names the individual surfaces. Each operation's policy requires the role set section 9/13 grants it to; a "cell" in P2-02's done-when sense is a (role, operation) pair, and it resolves to Allow exactly when that role is in that policy's `RequireRole` list.
2. **Naming scheme: `Area.Action`, exactly one dot, PascalCase.** `Products.ChangePrice`, `Restore.Perform`. Every name lives as a `Const` in `Merchandising.Domain.Security.PolicyRegistry.Names`, so no caller ever spells one as a string literal.
3. **Single source of truth: `Merchandising.Domain.Security.PolicyRegistry.Definitions`.** Pure data (policy name, description, allowed roles, spec citation) — no ASP.NET Core dependency, keeping Domain's "depends on nothing" rule (CLAUDE.md section 4) intact. Two consumers read it: `Merchandising.Api.Security.AuthorizationPolicyRegistration.Configure` registers the real `AuthorizationOptions` from it at startup, and `Merchandising.Domain.Security.RolePermissionMatrixFormatter.Render` renders `docs/role-permission-matrix.md` from it. `RolePermissionMatrixDocumentationTests` (Merchandising.Tests.Unit) regenerates the document from the registry on every test run and asserts it is byte-for-byte (line-ending-normalized) unchanged — a policy added, removed, or reworded without regenerating the file fails the suite. The document lives at the repository root's `docs/`, not alongside the registry, because it is Phase 2's own deliverable (`plan.md` §7); the registry is what generates it.
4. **User/role management gets no HTTP policy.** Spec section 13's API-area table has no "Users" row at all — accounts are created exclusively through the `Merchandising.Maintenance` CLI's `create-user` (P1-08), never over HTTP. SuperAdmin's "user/role management" responsibility and Admin's "cannot manage Super Admin accounts" restriction therefore have no endpoint to gate in this MVP. A future card that adds a Users HTTP surface adds its own policy and reargues this note; it does not retrofit one here speculatively.
5. **`Restore.Perform` is registered independently of `Configuration.Manage`**, even though both currently list only `SuperAdmin`. Spec section 9: "\[Admin\] cannot restore unless explicitly granted a separate policy" — a later grant to `Restore.Perform` must not also widen `Configuration.Manage`, and only structural separation (two distinct `AddPolicy` calls) guarantees that rather than a comment. `AuthorizationPolicyRegistrationTests.RestorePolicy_IsNotTheSamePolicyObjectAsConfigurationManage` asserts the two resolve to different `AuthorizationPolicy` instances.
6. **Self-approval (`PurchaseOrders.Approve`, `Adjustments.Approve`) is a resource-based `IAuthorizationRequirement`, not a role.** `Merchandising.Api.Security.IOwnershipResource` / `SelfApprovalRequirement` / `SelfApprovalHandler` compare the authenticated actor's `ClaimTypes.NameIdentifier` to the resource's `RequestedByUserId`, calling `context.Fail()` — not merely withholding `Succeed()` — on a match, matching spec section 9's "cannot" wording as an absolute veto. Neither guarded policy has a live endpoint yet; Phase 3 builds `PurchaseOrder`/`StockAdjustment` and is expected to implement `IOwnershipResource` on them and call `IAuthorizationService.AuthorizeAsync(User, resource, policyName)` — it does not add its own self-approval mechanism (`CLAUDE.md`/the P2-02 task card: "Phase 3 and Phase 5 consume these — they do not add their own"). Proven now by `SelfApprovalHandlerTests` calling the handler directly against a fake resource.
7. **Every pre-existing `Roles:="..."` attribute in the codebase was converted**, not just new ones — the done-when box reads as an absolute ("no endpoint... policies only"), not "no new endpoint". `AdminController.Ping` (P1-08's placeholder proof endpoint, not a spec section 9 cell) got a dedicated `Diagnostics.AdminPing` policy with the same effective role set (`Admin`, `SuperAdmin`) rather than being left on a role string or folded into an unrelated policy that would have silently narrowed or widened its behavior. `MaintenanceController.Enter`/`Release` moved to `Maintenance.Perform` (`SuperAdmin`, unchanged). `InventoryController`'s stock-decrement moved to `Adjustments.Request` (`Admin`, `SuperAdmin`, `InventoryClerk`, unchanged) — the closest named operation to what that P1-11 proof endpoint actually does. None of these four changed effective authorization behavior; `pwsh ./scripts/run-tests.ps1` re-passing every pre-existing test (`AuthenticationTests`, `MaintenanceModeTests`, `StockDecrementTests`) is the regression proof.

**Reasoning.** A named-policy-per-operation shape is what lets `docs/role-permission-matrix.md` be generated rather than maintained by hand: a role string scattered across controller attributes has no single place to read "everything Cashier can do" from, but a registry keyed by operation does. Grounding operation granularity in section 13 rather than freehand judgment keeps the decomposition falsifiable against the spec text rather than a matter of taste.

**A real defect surfaced while building this, worth recording because it will recur.** VB is case-insensitive. Three separate places in this task's own code — `PolicyDefinition`'s constructor (`PolicyName = policyName` and three siblings), `PolicyRegistry`'s original `Definitions = definitions` local, and this ADR's own test fixture (`RequestedByUserId = requestedByUserId`) — silently resolved a property assignment to a self-assignment of an identically-named-except-for-case parameter or local, leaving the property `Nothing`/default and producing no compiler warning. `RolePermissionMatrixDocumentationTests` and `SelfApprovalHandlerTests` caught all three before this ADR was written. The fix in every case was to give the parameter/local a genuinely different name, never merely a different case (see `MigrationRunnerTests`' pre-existing "path" vs `System.IO.Path` comment for the earlier instance of this same class of bug). Anyone writing a VB constructor that assigns parameters to identically-spelled properties should name the parameters differently, not just capitalize them differently, and should not trust that a build with 0 warnings means the assignment happened.

**Rejected.**

- *Policy-per-role instead of policy-per-operation.* Five policies (one per role) cannot express "Admin can approve but not their own" or "Cashier can create sales but not change price" — role and operation are genuinely two axes, and collapsing them back to one loses exactly the restrictions section 9 calls out by name.
- *A live document-generation step (a build target or CLI command that writes `docs/role-permission-matrix.md`).* A test that regenerates and compares is simpler, runs on every `run-tests.ps1` invocation for free, and fails loudly in the same place every other regression does, rather than requiring a separate "did you remember to run the generator" discipline.
- *Giving Admin a Users-management policy anyway, to make the "cannot manage Super Admin accounts" restriction meaningful.* Building an authorization policy for an HTTP surface that section 13 does not list is exactly the "design for a hypothetical future requirement" `CLAUDE.md` rules out. The restriction is recorded here as out of scope, not silently dropped.

**Evidence.** `evidence/phase-2/p2-02-policy-registration.txt`.

---

> **P2-04 addendum (2026-08-25) — audit becomes one server-side pipeline component.** `tasks.md` names this card's own ADR as ADR-017, though its subject (audit, not policy naming) sits outside the title above. No other ADR number was given, and the alternative — inventing an unassigned one silently — is exactly the kind of guess CLAUDE.md section 7 rules out; this is recorded here, appended rather than reopening the accepted P2-02 decision above, the same way ADR-006 carries P1-11/12/13's progress notes under a title that predates them.
>
> **Decision.** `Merchandising.Infrastructure.Data.AuditLogWriter.WriteAsync` remains the single insertion point for `AuditLogs` (unchanged since P1-08 — `StockService`, `AuthService`, `MaintenanceController` already routed through it; nothing here needed consolidating). What this card adds is the enforcement mechanism the done-when boxes actually asked for: a "declared intent" flag that `WriteAsync` itself flips, checked by a new global MVC action filter, `Merchandising.Api.Middleware.AuditPipelineFilter`, against a new marker, `Merchandising.Domain.Security.AuditRequiredAttribute`. Every mutating (non-GET) controller action carries `<AuditRequired>` today (`Login`, `Logout`, `MaintenanceController.Enter`/`Release`, `InventoryController.DecrementStock`) — a coverage test (`AuditPipelineTests.EveryMutatingAction_DeclaresAuditRequired`) fails a future mutating action that forgets the attribute, the same shape P2-03's endpoint-discovery test uses for authorization. For any `<AuditRequired>` action, the filter rejects (500 `AUDIT_NOT_RECORDED`) a successful (2xx) result that never called `WriteAsync`, swapping the result inside `IAsyncActionFilter.OnActionExecutionAsync` — strictly before the result is serialized to the response, unlike `ExceptionHandlingMiddleware`, which relies on an exception arriving before the response starts. Non-2xx (denial) results are never forced to audit; several denial paths (`LoginFailed`, `AccountLocked`, `MaintenanceReleaseRefused`) already audit by their own choice, and several others (unknown-username login, `MAINTENANCE_ALREADY_ACTIVE`, `INSUFFICIENT_STOCK`) do not — requiring every denial to audit as well is a separate, larger design question this card's done-when boxes did not ask for.
>
> **A real defect this mechanism caught while building it, worth recording because it is the second instance of ADR-017 point 7's own lesson.** `AuditLogWriter.Declared` was first implemented as a bare `AsyncLocal(Of Boolean)`, flipped to `True` at the end of `WriteAsync`. `AuditPipelineTests.AuditRequired_ActionDeclaresAudit_ResultPassesThroughUnmodified` failed immediately — not a fixture problem, but a live `POST /api/v1/auth/login` returning 500 `AUDIT_NOT_RECORDED` on its ordinary success path. Cause: `AsyncLocal<T>.Value` flows only downward, ancestor to descendant; a descendant (`AuditLogWriter.WriteAsync`, several calls deep inside `AuthService`) assigning a *new* value does not propagate back to the ancestor (`AuditPipelineFilter`) after the ancestor's own `Await` resumes — each `Await` boundary restores the ExecutionContext the ancestor already had, not one a descendant mutated. The fix was not to abandon `AsyncLocal` but to change what it carries: the filter now creates one mutable `DeclarationState` instance and stores a *reference* to it in the `AsyncLocal`; that reference flows downward reliably (the direction `AsyncLocal` is actually good for, and what `ILogger.BeginScope`/`Activity.Current` depend on), and `WriteAsync` mutates a field *on* that shared instance rather than reassigning the `AsyncLocal` itself — ordinary heap mutation the filter observes afterward with no propagation required at all. Full detail in `AuditLogWriter.vb`'s own header.
>
> **Rejected.** Raw ASP.NET Core middleware (rather than an MVC action filter) for the enforcement check — tried first, rejected once it was clear the response for an ordinary controller result (`Ok(...)`, `Conflict(...)`) is already written to the wire (`HttpContext.Response.HasStarted = True`) by the time control returns to an outer middleware's `Await _next(context)`, so there is nothing left to replace. `ExceptionHandlingMiddleware` gets away with post-hoc replacement only because an exception propagates *before* the result is executed. `IAsyncActionFilter.OnActionExecutionAsync` runs in the one window that actually allows a result to be swapped: after the action method returns a result object, before that object is serialized.
>
> **Evidence.** `evidence/phase-2/p2-04-audit-pipeline.txt`, `evidence/phase-2/p2-04-rollback-regression.txt`.

> **P3-04 addendum (2026-08-27) — `IOwnershipResource` moved from `Merchandising.Api.Security` to `Merchandising.Domain.Security`.** Point 6 above says Phase 3 "is expected to implement `IOwnershipResource`" on `PurchaseOrder`/`StockAdjustment`. It could not, as the interface stood: `PurchaseOrder` lives in `Merchandising.Domain.Entities`, and `Merchandising.Api.Security.IOwnershipResource` lived in the Api project — `CLAUDE.md` section 4's dependency direction ("Domain depends on nothing") forbids a Domain type implementing an Api-declared interface, and this is not a guardrail G-B checks (G-B only watches client projects), so it would have compiled clean and been wrong quietly.
>
> **Decision.** `IOwnershipResource.vb` moved to `Merchandising.Domain.Security`, unchanged in content — it declares one read-only `Integer` property and has zero ASP.NET Core dependency, the same category `PolicyRegistry`/`RoleNames`/`PolicyDefinition` already occupy there, distinct from the framework-dependent consumers (`SelfApprovalHandler`, `SelfApprovalRequirement`, `AuthorizationPolicyRegistration`) that stay in `Merchandising.Api.Security`. `Merchandising.Domain.Entities.PurchaseOrder` now implements it directly on its existing `RequestedByUserId` property — nothing new to wire, the property already existed for P3-03's response mapping. Three files touched: the moved interface, `SelfApprovalHandler.vb` (added `Imports Merchandising.Domain.Security`), and `SelfApprovalHandlerTests.vb` (same import added, its own `FakeOwnedResource` fixture unchanged).
>
> **Why this was found now and not at P2-02.** `IOwnershipResource` had no Domain implementer until this card — `SelfApprovalHandlerTests`' `FakeOwnedResource` lived entirely inside the Api-layer test project, so the misplacement never actually crossed the Domain/Api boundary until `PurchaseOrder` needed to implement it for real.
>
> **Behavior change: none.** Same interface, same members, same two consumers. `PurchaseOrdersController.ApprovePurchaseOrder` calls `IAuthorizationService.AuthorizeAsync(User, order, PolicyRegistry.Names.PurchaseOrdersApprove)` with the loaded `Merchandising.Domain.Entities.PurchaseOrder` as the resource, exactly as point 6 always described — resolvable only once the interface sat on the correct side of the dependency graph.
>
> **Evidence.** `evidence/phase-3/p3-04-approval.txt`, `evidence/phase-3/p3-04-self-approval-denied.txt`.

> **P3-05 addendum (2026-08-27) — `PurchaseOrders.Cancel` and `PurchaseOrders.Close`, two policies spec section 13 does not name.** Every prior `PurchaseOrders.*` policy (`Create`, `Submit`, `Approve`, `Track`) decomposes from an endpoint section 13's own table explicitly lists. Cancel and close have no row there at all — section 13's table is headed "representative endpoints", so the gap reads as brevity rather than a deliberate exclusion, but that is a judgement call, not a quote, and ADR-017 point 1's own rule ("grounded in section 13's API-area breakdown... rather than an invented split of section 9's prose") does not by itself resolve it. Confirmed with the user before implementing rather than decided silently (`CLAUDE.md` section 7 item 4: an ambiguous spec business rule is a stop condition).
>
> **Decision.** Both `ProcurementAndAbove` (`SuperAdmin`, `Admin`, `ProcurementOfficer`) — the same role set as `Create`/`Submit`/`Track`. Reasoning offered to the user: spec section 9's Procurement Officer row states "purchase orders, order tracking" as a primary responsibility, and nothing in section 9's restrictions column narrows cancellation or closure the way "purchase approvals" narrows `Approve` to Admin. The user picked this option over `AdminAndAbove` for both, or a split (Procurement cancels, Admin closes).
>
> **What this is not.** Not a section-13 quote the way `Create`/`Submit`/`Approve`/`Track`'s `SpecBasis` strings are — `PolicyRegistry.vb`'s entries for both say so explicitly, rather than dressing up a decision as a citation.
>
> **Evidence.** `evidence/phase-3/p3-05-cancellation.txt`.

> **P5-11 addendum (2026-08-30) — `SalesReturns.ApproveExceptional` gets the self-approval veto point 6 originally said it wouldn't, and a second real defect this correction exposed.** Point 6's own text names only `PurchaseOrders.Approve`/`Adjustments.Approve`; this file's earlier body text (`AuthorizationPolicyRegistration.vb`'s header, written at P2-02 before `SalesReturns` existed) went further and asserted the negative outright: *"SalesReturns.ApproveExceptional has no self-approval rule; nothing requests an exceptional return for itself to approve."* That claim was never re-checked once `PolicyRegistry.vb`'s actual role sets were fixed at P5-02: `SalesReturns.Create` resolves to `CashierAndAbove` (`SuperAdmin`, `Admin`, `Cashier`) and `SalesReturns.ApproveExceptional` resolves to `AdminAndAbove` (`SuperAdmin`, `Admin`) — both sets contain Admin and SuperAdmin, so an Admin who requests their own exceptional return could otherwise approve it themselves, the identical shape point 6 already forbids for the other two policies.
>
> **Decision.** `SalesReturns.ApproveExceptional` joins `SelfApprovalGuardedPolicies` in `AuthorizationPolicyRegistration.vb`. `Merchandising.Domain.Entities.SalesReturn` implements `IOwnershipResource` on its `ReturnedByUserId` property (`Implements IOwnershipResource.RequestedByUserId` — VB's explicit `Implements` clause does not require the names to match, and `ReturnedByUserId` is 0012's own column name, kept rather than renamed to fit the interface). `SalesReturnsController.ApproveExceptional` follows `AdjustmentsController.ApproveAdjustment`'s exact imperative shape: `<Authorize>` only, a role check against `PolicyRegistry.Definitions` before the return is ever loaded, then `IAuthorizationService.AuthorizeAsync(User, salesReturn, SalesReturns.ApproveExceptional)` once it is. `AuthorizationPolicyRegistrationTests.OnlyTheThreeApprovalPolicies_CarrySelfApprovalRequirement` (renamed from `OnlyTheTwoApprovalPolicies_...`) and `AuthorizationMatrixTests`' allowlist/coverage were updated to match.
>
> **A second, independent defect this correction exposed, worth recording because it will recur.** `SalesReturnsController.RejectExceptional` was first written declaratively — `<Authorize(Policy:=PolicyRegistry.Names.SalesReturnsApproveExceptional)>` — the same shape `AdjustmentsController.RejectAdjustment` already uses in production for `Adjustments.Approve`. Once the policy above carried a `SelfApprovalRequirement`, this shape refused EVERY caller with 403, regardless of role: ASP.NET Core's endpoint-routing pipeline evaluates a declarative attribute's policy against whatever resource it supplies internally, never an `IOwnershipResource`-typed one, so `SelfApprovalHandler` (typed `AuthorizationHandler(Of SelfApprovalRequirement, IOwnershipResource)`) never runs, the requirement is never marked `Succeeded` by any handler, and the policy fails closed for everyone. `RejectExceptional_PendingReturn_RejectsWithNoStockOrRefundEffect` caught this immediately — an Admin fixture, unambiguously inside `AdminAndAbove`, got 403 `FORBIDDEN`. The fix: `RejectExceptional` now carries plain `<Authorize>` (authentication only) and repeats `ApproveExceptional`'s own role-membership check verbatim, never reaching `AuthorizeAsync` at all — the same role list, just without the veto, registered in `AuthorizationMatrixTests`' `AuthenticatedNoPolicyAllowlist` alongside it.
>
> **This same defect is very likely already live in `AdjustmentsController.RejectAdjustment`**, unnoticed because no test in this codebase ever drove that action over real HTTP with a role inside `AdjustmentsApprove`'s allowed set expecting success — `AdjustmentTests.vb`'s own `RejectAsync_PendingAdjustment_ChangesNoStock` calls `AdjustmentService.RejectAsync` directly, bypassing the controller and its attribute entirely. Recorded here rather than silently fixed, because `AdjustmentsController.vb` is Phase 4 code outside this card's file list (`CLAUDE.md`: don't expand scope past what was asked) — a fix belongs to whichever future card touches that controller next, or to a standalone bug-fix card if the user wants it sooner.
>
> **Evidence.** `evidence/phase-5/p5-11-sale-returns.txt`.

---

## ADR-018 · Unique active barcode, without a partial index

**Status:** ACCEPTED
**Date:** 2026-08-25
**Decides:** how spec section 12's "unique active barcode" integrity rule is enforced on `Products.Barcode`, given MariaDB 10.4 has no partial/filtered unique index (added 10.5+) — the gap P1-07 flagged and deliberately did not solve for its one-POC-product slice (`0001_foundation.sql`'s comment; `tasks.md` P0-07-adjacent note pointing here).

**Decision.**

1. **A `VIRTUAL` generated column plus an ordinary `UNIQUE KEY` on it**, not a trigger and not application-level enforcement. `Products.ActiveBarcode GENERATED ALWAYS AS (CASE WHEN IsActive = 1 THEN Barcode ELSE NULL END) VIRTUAL`, with `UNIQUE KEY UQ_Products_ActiveBarcode (ActiveBarcode)`. MariaDB's unique index treats every `NULL` as distinct from every other `NULL` (the same "unique when present" behavior `0001_foundation.sql` already relied on for the old unconditional `UQ_Products_Barcode`), so an inactive product's `ActiveBarcode` is always `NULL` and never collides with anything — active or inactive.
2. **0001's unconditional `UQ_Products_Barcode` is dropped**, replaced by a plain non-unique `IX_Products_Barcode` for ordinary lookup. The old constraint blocked two *inactive* products from ever sharing a barcode value, which spec section 12 does not ask for and which would have blocked the P2-09 reactivation scenario: deactivating a product must free its barcode for reuse by a new active product, proven directly in `evidence/phase-2/p2-06-precision.txt` Part A (deactivate A holding `DUP-BARCODE-1` → insert D with the same barcode, active, succeeds immediately).
3. **`Products.Barcode` stays the single canonical field this rule governs.** The new `ProductBarcodes` table (spec section 12's own separate entity) holds *additional* scannable codes for a product beyond its primary barcode — each globally unique across the table regardless of the owning product's active state, since spec section 12's "unique active barcode" wording names the product's own barcode attribute (echoed in section 11 as "optional barcode"), not a supplementary lookup table.

**Reasoning.** The task card names three honest options and asks that the rejected two be recorded, not merely the winner:

- *Why a generated column, measured, not assumed:* a unique index's duplicate check is InnoDB's own atomic operation inside the `INSERT`/`UPDATE` itself — there is no separate read-then-decide step for the application (or a trigger) to race against. `evidence/phase-2/p2-06-precision.txt` Part A's last section fires two `INSERT`s on the same active barcode from two independent client processes launched together, unawaited between them (the same "fire without awaiting" shape ADR-006/P1-13 used to prove the stock-decrement conditional `UPDATE`) — exactly one lands, the other returns `ERROR 1062` every time. This is the same class of guarantee ADR-006 relies on for stock, just expressed through a unique index instead of a conditional `UPDATE`, because uniqueness (unlike a quantity threshold) is something a unique index alone can express.
- *Why not a trigger:* a `BEFORE INSERT`/`BEFORE UPDATE` trigger enforcing "no other active row has this barcode" still has to run a `SELECT` and then decide — the identical check-then-act shape P1-13 already proved unsafe for stock decrement (two concurrent trigger invocations can both `SELECT` before either has committed a conflicting row, unless the trigger itself takes a table-level lock, which would serialize every product write in the system for a rule that only needs to serialize on one column). A generated column's unique index needs no such workaround because the atomicity is InnoDB's, not the trigger author's.
- *Why not application-level enforcement with a documented race window:* this is precisely what P1-13's own conclusion rules out — "application-level uniqueness under concurrency is precisely what P1-13 proved you cannot assume" (the task card's own wording). Accepting a race window here would mean two concurrent `POST /products` calls could both pass an API-side existence check and both insert an active product against the same barcode, the same class of defect ADR-006 exists to prevent for stock.
- *Why `ProductBarcodes` is a separate concept rather than the enforcement surface:* spec section 12 lists `Products` and `ProductBarcodes` as two distinct entities in the same "Product master" group, and section 11's prose treats a product's barcode as a singular, optional attribute of `Products` itself ("Products contain SKU, name, description, category, brand, unit, optional barcode, ..."). Folding the uniqueness rule into `ProductBarcodes` instead would mean a product's *primary* lookup barcode has no fixed column, complicating every POS/inventory search path (spec section 10: "search by SKU, optional barcode, or product name") for a normalization benefit nothing in the spec asks for at this scale.

**Rejected.**

- *Trigger-based enforcement* — check-then-act, the same race class P1-13 already disproved for a different table; rejected on that precedent rather than re-measuring a known-bad pattern.
- *Application-level enforcement with a documented race window* — explicitly the option the task card and P1-13 both rule out; a real duplicate-active-barcode window is not an acceptable trade for this system's own stated guarantees (CLAUDE.md section 5: "every stock-changing operation is atomic" — the same standard extends to any DB-level uniqueness this project claims).
- *Filtered/partial unique index* (`CREATE INDEX ... WHERE IsActive = 1`) — MariaDB 10.4 does not support it; added in 10.5, and ADR-002/ADR-003 pin this project to the measured 10.4.32 instance, not a later version.
- *Making `ProductBarcodes` the sole home of barcode data, dropping `Products.Barcode` entirely* — considered, and would remove the generated-column/trigger/app-level tradeoff onto a table this migration does not otherwise need to specialize for uniqueness. Not adopted because spec section 11 already treats the primary barcode as a `Products` attribute; revisited only if a later card demonstrates a real need for a product to have more than one *uniqueness-governed* barcode.

**Evidence.** `evidence/phase-2/p2-06-schema.txt`, `evidence/phase-2/p2-06-precision.txt`.

---

## ADR-019 · A checksum test must not depend on which USB stick is attached

**Status:** ACCEPTED
**Date:** 2026-08-25
**Decides:** whether `Backup_Succeeds_WritesDumpWithChecksumThatVerifies` may demand `BackupOutcome.Succeeded`, and where the volume-*present* off-host copy assertion is owed. Raised at the Phase 2 exit gate, which this test was the sole blocker of on the test-suite criterion.

**Decision.**

1. **The test accepts `Succeeded` or `Partial`.** Both mean "dump written, checksum verified"; they differ only in whether the off-host copy happened. `Failed` still fails the test — no usable dump was produced and none of the assertions that follow could hold.
2. **The volume-present off-host copy is artifact-backed, not assertion-backed**, and a real assertion is owed when **Phase 6** promotes backup to production quality with retention and off-host rotation (`plan.md` §7, closing G-15/G-16). Recorded here as an explicit debt rather than left implicit.
3. **Phase 2 closes with no `MERCHBACKUP` volume attached to the host.** That is a deliberate decision, not an oversight, and this entry is where a reader finds out.

**Reasoning.** Measured at the gate, not argued from preference.

- **The failure was reproduced, not read from a log.** `pwsh ./scripts/run-tests.ps1` on `f7bce1b`: guardrails pass, 10 projects at 0 warnings / 0 errors, 24/24 unit, **135/136 integration**. The single failure was this test, and it died at its *first* assertion — `Expected:<Succeeded>. Actual:<Partial>` — before reaching a single one of the checksum assertions that are its purpose.
- **The test's subject is not the off-host copy.** Its own `<summary>` says so: *"a run produces a dump, records its size and checksum, and the checksum recomputed from the file on disk matches what was recorded."* Its assertions are the dump file, the `MariaDB dump` header, the `Database: merchandising` line, the `-- Dump completed` truncation marker, the recomputed SHA-256, and the `BackupLogs` row. Off-host copy is incidental to all of it.
- **`Partial` is a designed outcome, not a degraded pass.** `BackupOutcome.vb` defines it as *"Dump written and verified; off-host copy did not happen"*, and `docs/database-design.md` §`BackupLogs` states the rule directly: `Result` is `Succeeded`/`Partial`/`Failed`, *"not a boolean — a dump that verified but could not copy off-host is neither."* Demanding `Succeeded` in a checksum test contradicted the project's own three-valued model.
- **A coverage audit of `BackupCommandTests` decided it.** The class has four tests. **None** asserts `OffHostPath` is non-null or byte-identical when the volume *is* present — that claim has always rested on `evidence/phase-1/p1-17-backup-success.log` (real `D:`, byte-identical copy, checksums agree), never on an assertion. So this relaxation costs **zero** assertion coverage; it removes a hardware dependency and nothing else.
- **The absent branch stays covered, environment-independently.** `Backup_WhenOffHostVolumeAbsent_ReportsPartialNotSucceeded` points at a deliberately nonexistent label (`NO-SUCH-VOLUME-<guid>`) and so has never depended on what is plugged in. It passes today and is untouched.
- **Portability is the part that would have bitten later.** As written, this suite could not go green on *any* machine without a volume labelled `MERCHBACKUP` — including a classmate's laptop. ADR-012 makes "a clean installation on a fresh machine succeeds from the guide alone" the measure of whether the deliverable exists at all, and a red suite on every fresh machine is squarely against that.

**Rejected.**

- **Attach the USB and re-run.** Closes this one run and does nothing for the next machine; the same failure recurs on every host without the stick. It treats the symptom.
- **Defer by ADR in the ADR-016 shape** — carry the failing test forward to a later gate. More ceremony for a weaker result: it leaves a permanently red suite that every future gate has to re-explain, and it mislabels a miswired test as an unproven claim. ADR-016 deferred things that were genuinely *unproven* (three unsurveyed workstations, a network never brought up cold). This is not that.
- **`Assert.Inconclusive` when no volume is attached.** MSTest reports inconclusive as neither pass nor fail. A suite that silently skips its checksum verification on most machines is worse than one that asserts everything it can — it would have hidden a real checksum regression behind a hardware condition.
- **Delete the test.** The checksum-recomputed-against-the-bytes-on-disk assertion is the entire point of P1-17: *"A checksum that is merely stored proves nothing — it has to be checked against the bytes."*

**What this does NOT prove, stated plainly.** That the off-host copy works when the volume is present. That remains evidenced only by `evidence/phase-1/p1-17-backup-success.log`, captured 2026-08-22 against the real `D:` volume. Phase 6 owes it an assertion.

**Evidence.** `evidence/phase-2/p2-13-clean-clone.log` (the green run this unblocked), `evidence/phase-1/p1-17-backup-success.log` (the artifact the off-host claim now rests on).

---

## ADR-020 · The purchase-order status machine is a table, not a set of checks

**Status:** ACCEPTED
**Date:** 2026-08-25
**Decides:** where a purchase-order transition is decided, what the seven spec §10.1 states may move to, what a refusal returns, and which rows Phase 4 drives. Raised and settled at P3-01.

**Decision.**

1. **One table, one function.** `Merchandising.Domain.Procurement.PurchaseOrderTransitions.Table` holds every legal transition, keyed by `(current status, attempted action)` and valued by the status the order moves to. `CanTransition(from, action)` is the only place a purchase-order transition is decided anywhere in the solution. Controllers, repositories and clients ask it and obey the answer.
2. **The table is closed and defaults to refusal.** A pair absent from `Table` is illegal. Adding a state or an action therefore defaults to *refuse everything*, never to *allow silently* — the safe direction for a machine governing stock and money.
3. **Refusals name a stable error code, never a boolean false** (ADR-014). Four codes, on `PurchaseOrderTransitionErrors`: `PURCHASE_ORDER_CANCELLED`, `PURCHASE_ORDER_CLOSED`, `PURCHASE_ORDER_FULLY_RECEIVED`, `PURCHASE_ORDER_INVALID_TRANSITION`.
4. **Receiving is two actions, not one.** `ReceivePartially` and `ReceiveFully`.
5. **Enum names are the stable identifiers** P3-02 stores. Not ordinals.

**Amended at P3-02 — what point 5 costs to actually hold.** Storing the name is not enough on its own. `PurchaseOrders.Status` is `VARCHAR(20)` under `CHECK (Status IN (…the seven…))`, and the first draft of migration `0008` let that column inherit the table's `utf8mb4_unicode_ci` — which is **case-insensitive**, so `'draft' = 'Draft'` evaluates to `1`, the `CHECK` passes, and MariaDB stores `'draft'` **verbatim**. The column would have held a value no `PurchaseOrderStatus` name matches: the exact drift point 5 exists to prevent, arriving through the collation rather than through an ordinal. The fix is `COLLATE utf8mb4_bin` on that single column, which makes the `CHECK` compare byte for byte. It is the only binary-collated column in the schema, and deliberately so — a binary collation is wrong for human text and exactly right for a machine identifier, matching VB's own `Option Compare Binary` (CLAUDE.md §3). `ENUM(...)` was rejected as the alternative: it carries the ordinal semantics this point rules out. Found by `StatusColumn_RefusesAnOrdinalAndAnUnknownName`, not by review; the repair required rolling back an already-applied migration under §7 stop condition 6 and was authorised rather than assumed. Full record: `evidence/phase-3/p3-02-schema.txt` §1b.

**The table — 11 legal transitions out of 42 pairs.**

| From | Submit | Approve | ReceivePartially | ReceiveFully | Cancel | Close |
|---|---|---|---|---|---|---|
| `Draft` | → Submitted | — | — | — | → Cancelled | — |
| `Submitted` | — | → Approved | — | — | → Cancelled | — |
| `Approved` | — | — | → PartiallyReceived | → FullyReceived | → Cancelled | — |
| `PartiallyReceived` | — | — | → PartiallyReceived | → FullyReceived | — | → Closed |
| `FullyReceived` | — | — | ⛔ over-receiving | ⛔ over-receiving | — | → Closed |
| `Cancelled` | — | — | — | — | — | — |
| `Closed` | — | — | — | — | — | — |

Full rendering, one row per pair with its error code: `evidence/phase-3/p3-01-transition-matrix.txt`.

**Reasoning.**

- **`plan.md` §7 named this the key design call of the phase** and named the failure mode: *"Scattered `If status = ...` checks across controllers is how invalid transitions leak in."* The leak is not that any one check is wrong; it is that the *n*th controller silently omits one. A table makes omission structurally impossible, because the absence of a row is itself the rule.
- **Receiving had to be two actions or the table stops being a function.** A single `Receive` from `Approved` could land in either `PartiallyReceived` or `FullyReceived` depending on quantity, so `(from, action)` would no longer determine the target and the caller would have to re-derive it — reintroducing the second decision point this card exists to remove. Splitting keeps `CanTransition` total and pure: Phase 4's receiving command computes the remaining quantity first, then names which of the two it is performing.
- **Over-receiving needs its own code, not the generic one.** Spec §10.1 anticipates it: *"unless an authorized override policy is later approved."* A future override policy has to key off something; folded into `PURCHASE_ORDER_INVALID_TRANSITION` it would have nothing to key off, and widening that code would widen every other bad transition with it.
- **`Cancelled` and `Closed` get distinct codes** because a caller must be able to tell "this order is dead" from "that action was wrong here" without parsing a message.
- **All seven states are modelled now, two phases before two of them can be reached.** Without the `FullyReceived` rows, Phase 3 could not assert its own over-receiving refusal, and Phase 4 would rewrite the table rather than add a controller.

**The two discretionary rows, and why they went the way they did.** Spec §10.1 is silent on both; neither is a spec conflict, so neither is a §7 stop condition. Both were put to the user at P3-01 and confirmed.

1. **A `PartiallyReceived` order cannot be cancelled — it is short-closed.** A receipt has already written append-only `StockMovements`, and CLAUDE.md §5 forbids ever undoing those. Cancelling would imply an undo that cannot happen; `Close` accepts what arrived and abandons the remainder. Spec §10.1's *"a cancelled order cannot receive goods"* reads naturally as a pre-receipt state.
2. **An `Approved` order with nothing received cannot be closed — it is cancelled.** This keeps the two terminal states meaning distinct things in spec §14's purchase-order history report: `Closed` = goods came in, `Cancelled` = they never did. A `Close` that could mean either would make that report ambiguous at exactly the point it is read.

**Rejected.**

- **`Select Case` on status inside each controller.** The failure mode `plan.md` §7 names. Also unenforceable: nothing fails when the fifth controller forgets a case.
- **A state-pattern class per status.** Seven classes and a factory to express eleven facts, with the machine no longer readable in one place. The whole value here is that a reviewer under exam pressure can see the entire machine at once.
- **Returning `Boolean` from `CanTransition`.** Contradicts ADR-014 and pushes the error-code decision back out to every caller — the scattering, one level up.
- **Deriving the target state in the caller** rather than returning it. A second decision point, which is the thing being removed.
- **Modelling receiving as one action with a quantity argument.** Puts quantity arithmetic into a Domain function that must stay pure and free of the order's lines; Phase 4 owns that arithmetic.
- **Omitting `PartiallyReceived`/`FullyReceived` until Phase 4.** Costs Phase 3 its own over-receiving assertion and guarantees a rewrite.

**Enforcement, and its honest limit.** `PurchaseOrderTransitionTests` enumerates all 42 pairs computed from `[Enum].GetValues`, so a state or action added later grows the suite rather than leaving a hole; the 11 legal rows are additionally written out longhand in the test and reconciled against the production table, so neither is trusted alone. The evidence artifact is *rendered* from the table and asserted equal to the committed file, in the P2-02 generator/verifier shape, so it cannot drift. A source scan fails the suite on a `Select Case` over a status anywhere under `src/` outside the table. **That scan catches the idiomatic form and not an `If order.Status = …` chain, a `Select Case` over an oddly-named local, or a decision made in SQL** — it is a lint, not a proof, and it is documented as such in the test.

**What this does NOT decide.** The self-approval veto is *not* in this table and must not be added to it — ADR-017 §6 owns it, P3-04 applies it through `IAuthorizationService`. Persistence of the status column is P3-02. The receiving endpoints are Phase 4.

**Evidence.** `evidence/phase-3/p3-01-transition-matrix.txt`, plus `PurchaseOrderTransitionTests` (13 tests) in the unit suite.

---

## ADR-021 · Ledger reconciliation is a standing automated assertion, not a report

**Status:** ACCEPTED
**Date:** 2026-08-29
**Decides:** how `SUM(StockMovements) = StockBalances` is checked for every product, where that check runs, and what counts as a discrepancy. Raised and settled at P4-01, first and alone per `plan.md` section 7 — every later Phase 4 card writes to the ledger.

**Decision.**

1. **One query, driven from `Products`.** `LedgerReconciliation.FindDiscrepanciesAsync` (`src/Merchandising.Infrastructure/Data/LedgerReconciliation.vb`) LEFT JOINs a `SUM(Delta) GROUP BY ProductId` subquery over `StockMovements` and `StockBalances` onto every row of `Products`, `COALESCE`s both sides to `0.000`, and returns every product where they disagree — `ProductId`, `ExpectedQuantity` (ledger sum), `ActualQuantity` (balance), `Delta` (`LedgerDiscrepancy.vb`). `Products` is the driving table, not a UNION of the two ledgers, because every `StockMovements`/`StockBalances` row carries a `FOREIGN KEY` to `Products` and products are never deleted (spec section 12) — so `Products` already names every `ProductId` either ledger could reference.
2. **`COALESCE(...,0.000)` on both sides is what makes "missing from either side is a discrepancy, not a skip" true with no special-casing.** A product with movements but no balance row compares a real sum against `0`; a product with a balance but no movements compares `0` against a real balance; a product genuinely untouched by both compares `0 = 0` and is silent.
3. **The inequality runs in SQL, as `DECIMAL(19,3) <> DECIMAL(19,3)`.** MariaDB's `DECIMAL` comparison is exact — this is where "never a floating-point tolerance" is actually enforced, not a property hoped for once the values reach VB.
4. **No explicit transaction is opened.** `ConnectionFactory` already pins every connection's session to `tx_isolation = READ-COMMITTED` (ADR-006), so a single autocommit `SELECT` here can only ever see committed rows without this query needing to know a concurrent transaction exists.
5. **Wired via MSTest `<AssemblyCleanup>`** (`LedgerReconciliationTests.ReconcileLedgerAfterSuiteAsync`), not a report. It runs once, automatically, after every other integration test in the assembly finishes, and calls `Assert.Fail` naming every discrepant product with expected/actual/delta if any exist — `dotnet test` fails the whole run. This is what `plan.md` section 7 means by "found automatically in Phase 4, not Phase 7": nobody has to remember to read anything.
6. **"Removing" an induced discrepancy always means closing the gap with a compensating `StockBalances` write, never editing or deleting the `StockMovements` row that proved detection worked.** `StockMovements` is append-only (CLAUDE.md section 5); section 7 stop condition 7 forbids `UPDATE`/`DELETE` on it regardless of which database account is technically capable (`merch_migrator` does hold that grant at the schema level — db/grants/0001 — the prohibition is a project rule layered on top, not gated by the grant). `LedgerReconciliationTests`'s induced-drift test inserts one unmatched movement as `merch_migrator`, asserts it is named with the exact expected/actual/delta, then reconciles the balance to the new ledger sum — the same "write the movement, then the balance must agree" shape every real command already follows.

**What P4-01 found immediately, and how it was resolved.** The first real run against the pinned database — not a synthetic fixture — found genuine pre-existing drift in four Phase 1/2 test fixture products (`p1_11_fixture_sku`, `p1_13_fixture_sku`, `p2_03_fixture_sku`, `p2_09_fixture_sku`). Root cause: `StockDecrementTests`, `ProductLifecycleTests`, and `AuthorizationMatrixTests` each reuse one fixed fixture product across every integration-test run ever executed, and their setup helpers reset `StockBalances.Quantity` directly — a bare `UPDATE`, or an initial `INSERT` of a nonzero baseline — with no matching movement, before every run. `StockMovements` is append-only, so every historical decrement from every prior run stayed in the ledger while the balance was repeatedly force-reset outside of it; the two had been silently diverging since Phase 1. This is precisely the failure mode the card exists to catch, caught exactly where `plan.md` section 7 predicted it should be. Put to the user, who chose to fix it rather than defer it: the three fixture helpers now write a compensating `StockMovements` row in the same transaction as any direct balance change (and their initial fixture-creation inserts now start every product at `0.000`, matching zero movements, instead of a nonzero baseline with nothing behind it), and a one-time correction (`evidence/phase-4/p4-01-drift-correction.sql`, run once via `merch_migrator` credentials, attributed to the permanent seeded `admin` account) closed the four existing gaps with compensating movements — never an edit or delete. Full detail: `evidence/phase-4/p4-01-ledger-reconciliation.txt`.

**Reasoning.**

- **An assertion beats a report because a report only works if someone remembers to run and read it.** `plan.md` section 7 states the cost asymmetry directly: drift found in Phase 7 is a nightmare, found automatically in Phase 4 it is a small bug. `<AssemblyCleanup>` makes the check unavoidable — it runs every time `dotnet test` runs this project, which `run-tests.ps1` already always does.
- **`Products` as the driving table, rather than a `FULL OUTER JOIN`-shaped UNION of the two ledgers, avoids a construct MariaDB 10.4 does not have** (no native `FULL OUTER JOIN`) while still being provably exhaustive, because of the `FOREIGN KEY` argument in point 1.
- **Finding real drift on the first run, rather than treating it as a test-only inconvenience, is the point of building this card first and alone.** Silently excluding known fixture products from the check, or loosening the comparison to ignore old drift, would have been the exact "found in Phase 7 instead" failure this ADR exists to prevent — just relocated to Phase 4 and then hidden.

**Rejected.**

- **A report/dashboard someone checks periodically.** Exactly the failure mode named in `plan.md` section 7.
- **Comparing values in VB after fetching both sides, rather than in SQL.** Works, but moves the "exact decimal, no tolerance" guarantee out of the database's own type system and into application code that could someday cast through `Double` by accident. Comparing in SQL makes that class of mistake structurally unavailable.
- **A `FULL OUTER JOIN` emulated with `UNION`.** Correct in principle, more complex than driving from `Products`, and loses the direct "every `ProductId` either ledger could reference already appears in `Products`" argument that makes exhaustiveness obvious on inspection.
- **Deleting or editing the pre-existing drift's underlying movement rows to make the numbers agree.** Directly forbidden (CLAUDE.md section 5, section 7 stop condition 7) regardless of which account could technically do it. Every correction here is an `INSERT`.
- **Silently excluding the four already-drifted fixture products from the assertion, or shipping the card with the check red.** Both would have defeated the card's own purpose — the first by hiding a real invariant violation, the second by leaving the ledger un-trustworthy at the exact point the phase begins building on top of it.

**Evidence.** `evidence/phase-4/p4-01-ledger-reconciliation.txt` (induced-drift proof, the real pre-existing-drift discovery and its resolution, and the full green suite run), `evidence/phase-4/p4-01-drift-correction.sql` (the one-time correction), `LedgerReconciliationTests` (3 tests: settled-fixture silence, induced-drift detection-then-healing, and the `<AssemblyCleanup>` itself) in the integration suite.

---

## ADR-022 · Sale arithmetic rounding is configurable, and a narrowing of ADR-004.1

**Status:** ACCEPTED
**Date:** 2026-08-30
**Decides:** where sale-line and sale-total rounding happens, which direction it rounds, and how that squares with ADR-004.1's blanket `AwayFromZero`. Raised and settled at P5-01, first and alone per `plan.md` section 7 — every later Phase 5 card that touches a sale total builds on this.

**Decision.**

1. **Rounding happens once, in `Merchandising.Domain.Sales.SaleLine`, at construction.** `LineTotal = Decimal.Round(quantity * unitPrice, 4, roundingPolicy)` is computed exactly once when a line is built and never recomputed. `SaleTotals.ComputeSaleTotal` sums already-rounded `LineTotal` values with no second rounding step — header and lines agree by construction, the same guarantee ADR-004.1 already requires for every other total in this system.
2. **The rounding direction is the `currency.roundingPolicy` SystemSettings value, resolved through `SaleRoundingPolicy.Parse`.** That setting was registered generically at P2-05 (`SystemSettingRegistry.Keys.CurrencyRoundingPolicy`, values `AwayFromZero` / `ToEven`) with no consumer until this card. `SaleLine`/`CashTender` take a `MidpointRounding` parameter rather than reading the setting themselves — Domain depends on nothing (CLAUDE.md section 4), so a later API-layer card reads the setting row and passes the resolved value in, the same shape `AdjustmentThresholdPolicy` already takes its threshold as a parameter.
3. **Change is never rounded at all.** `CashTender.ComputeChange` subtracts two already-money-scale values (`tendered - total`); the difference of two exact 4-decimal-place `Decimal`s is exact at 4 decimal places by construction, so there is nothing for a rounding policy to do there. `tendered < total` is refused (`CashTenderResult.IsAccepted = False`), never represented as a negative `Change` — the type has no constructor path that can produce one.
4. **Scale validation (ADR-004.1) is unchanged and still runs first.** Every money/quantity value entering `SaleLine`/`CashTender` goes through the existing `DecimalScaleGuard.EnsureMoneyScale`/`EnsureQuantityScale` before anything is rounded — an over-scale client value is still a rejection, never a value this card's rounding logic gets a chance to touch.

**Reasoning — the tension with ADR-004.1, named rather than hidden.** ADR-004.1 mandates `AwayFromZero` everywhere and explicitly *rejects* `MidpointRounding.ToEven`, reasoning specifically about "a printed receipt a customer can add up" — precisely POS's domain. This card's own acceptance criteria require the opposite: the rounding direction must follow the already-registered, previously-unconsumed `currency.roundingPolicy` setting, and a changed policy must provably change a computed total. Per CLAUDE.md section 1's precedence rule, `tasks.md` outranks `docs/adr.md`, so the card's explicit requirement governs. This ADR resolves the tension by scope, not by overturning ADR-004.1 wholesale: **the scale rules (money 4dp, quantity 3dp) and "round once at the boundary, sum from already-rounded components" stand exactly as ADR-004.1 wrote them, and bind this card too.** Only the *rounding direction* for POS sale-line/total arithmetic becomes configurable — everywhere else in the system, `DecimalScaleGuard.RoundMoney`/`RoundQuantity` are untouched and keep defaulting to `AwayFromZero`, so a landed-cost allocation or a price-history rounding elsewhere in the codebase is not silently affected by a store changing this one setting.

**Rejected.**

- **Leaving `currency.roundingPolicy` unconsumed and hard-coding `AwayFromZero` in Sales too**, matching ADR-004.1 to the letter. Rejected because the P5-01 card's own acceptance criteria require the setting to provably change a computed result — an administered setting with no code path that reads it is exactly the kind of decorative configuration CLAUDE.md's "don't design for hypothetical future requirements" warns against, except here it was already built (P2-05) and this is its first real consumer.
- **Reading the SystemSettings value directly inside `Sales/`.** Would give Domain a dependency on `Infrastructure`/`SystemSettingsRepository`, inverting the dependency direction CLAUDE.md section 4 fixes. The resolved `MidpointRounding` is passed in instead, the same shape every other Domain policy in this codebase already takes its inputs.
- **Rounding the sale total independently of its lines**, or rounding intermediate arithmetic before the final line total. Both are exactly the drift ADR-004.1 already named and forbade; nothing about making the direction configurable licenses reintroducing it.

**Evidence.** `evidence/phase-5/p5-01-sale-arithmetic.txt` — a genuine defect this card's own tests caught before evidence was captured (VB's case-insensitivity turning `ProductId = productId` into a silent no-op), the watched-fail proof for the source-scan test, and the full green guardrails + both suites run.

---

## ADR-023 · Report date boundary and the two-independent-paths reconciliation rule

**Status:** ACCEPTED
**Date:** 2026-08-30
**Decides:** how a report's date filter is converted from store-local calendar days to the UTC instants every stored timestamp actually uses, and what a report's reconciliation test is required to compare. Raised and settled at P6-01, first and alone per `plan.md` section 7 — every Track B report card (P6-02 – P6-05) builds on both halves.

**Decision.**

1. **The day boundary is `Merchandising.Domain.StoreTimeZone`, unchanged from P3-06, cited rather than redefined.** `StartOfDayUtc(date)` / `EndOfDayUtcExclusive(date)` already convert a store-local (`Asia/Manila`) calendar day to the `[start, end)` UTC instant range every report's date filter uses; this card adds no second implementation. `docs/report-specification.md` section 2 states the conversion as a worked example with real timestamps rather than a sentence, per the card's own acceptance criteria — for store-local `2026-08-30`: `StartOfDayUtc = 2026-08-29T16:00:00Z`, `EndOfDayUtcExclusive = 2026-08-30T16:00:00Z`.
2. **The actual cross-midnight risk is early store-local hours, not late ones, and the document says so rather than forcing a false example.** `Asia/Manila` is UTC+8 with no DST (confirmed at P3-06). A store-local `23:59:59` timestamp's UTC instant (`15:59:59` the same date) shares its UTC calendar date with its correct store-local date — a naive `DATE(CreatedAtUtc)` grouping would coincidentally agree there. The actual divergence — where a naive UTC-calendar-date grouping silently misfiles a sale into the wrong day, the exact risk `plan.md` section 7 point 2 names — occurs for store-local `00:00:00`–`07:59:59`: a sale at local `2026-08-30 02:00:00` converts to UTC `2026-08-29 18:00:00`, a different UTC calendar date from its correct store-local one. `Merchandising.Tests.Unit.StoreTimeZoneBoundaryTests` asserts both facts with these exact numbers — the 23:59:59 case proves the window's upper bound is correctly inclusive (guarding an off-by-one in `EndOfDayUtcExclusive`), and the 02:00:00 case proves the divergence a Track B report must actually avoid.
3. **A report's reconciliation test must compare two independently-computed totals, never one query run twice.** `Merchandising.Tests.Integration.ReportReconciliationHarness.Reconciles`/`AssertReconciles` takes two `Decimal` totals supplied by the caller and never fetches either side itself — the report's own query on one side, a client-side sum over the *existing* detail endpoint's `Items` on the other (`docs/report-specification.md` section 7's table names which existing endpoint each report reconciles against). Comparison is exact `Decimal` equality, the same no-tolerance rule ADR-021 already applies to the stock ledger, because every value on both sides is already `DECIMAL(19,4)`/`DECIMAL(19,3)` (ADR-004) with no floating-point arithmetic between storage and the comparison.
4. **Why a self-comparing reconciliation test is worthless, stated plainly.** A test that calls a report's own SQL statement twice and asserts the two calls agree proves only that the statement is deterministic — it would pass on a report that is confidently, consistently wrong (a boundary computed with the wrong offset, or a join against `Products.Cost` instead of a captured cost column, ADR-004.1/P5-07's rule). It can never catch either defect class this phase is most exposed to, because both defects live *inside* the one query being compared to itself.
5. **The harness was proven falsifiable before any real report exists to use it.** Track B's first report card is P6-02; P6-01 builds no report. `Merchandising.Tests.Integration.ReportReconciliationHarnessTests` proves the mechanism with two synthetic scenarios built from real arithmetic instead: the off-by-one boundary (a naive UTC-date report total of 200.0000 against the correct store-local detail total of 350.0000, using the exact `02:00:00`/`14:00:00` worked example from point 2) and the current-cost join (a current-cost report total of 600.0000 against a captured-cost detail total of 500.0000). Both scenarios assert `Reconciles` returns `False` and `AssertReconciles` throws `AssertFailedException` — the same "prove the detector, not merely its existence" split ADR already established by P5-12's `PaymentWordingTests.Denylist_FiresOnTheExactBadPhrase_AndPassesOnTheCorrectedOne`.
6. **A known scope gap, surfaced rather than discovered mid-card.** Only `POST` routes exist today for sales, sales returns, and cashier sessions — no `GET` detail/list endpoint exists for any of them, confirmed by inspection before this card was written. `Purchase-order history` and the stock reports already have a detail endpoint to reconcile against (P3-06/P4-xx); the six sales-side reports (P6-02, P6-03) do not, and `docs/report-specification.md` section 7 records that P6-02/P6-03 must add the relevant detail endpoint as part of building the report — a genuine scope addition to those two cards, not a separately-scoped prerequisite this card was asked to build.

**Reasoning.**

- **Citing `StoreTimeZone` rather than redefining it is what keeps "one definition of the boundary" true.** P3-06 already built and proved the conversion for the purchase-order history report; a second implementation for Track B would be exactly the kind of drift `docs/report-specification.md`'s own purpose (every report cites this document instead of re-deciding) exists to prevent, moved one level down.
- **Writing the honest 23:59:59-versus-02:00:00 distinction, instead of forcing the card's literal wording into a false claim, is a direct application of the Phase 5 gate's own lesson** (`tasks.md`'s "carried forward" note): three phase gates in a row were failed by a claim that outran its evidence, the last one with every cited artifact existing on disk anyway. A test asserting a 23:59:59 timestamp "lands in the day a cashier would expect and not the UTC one" would have been true only by coincidence in this zone; the document says exactly what is and is not demonstrated by that timestamp, and proves the real divergence with a second, correct example instead.
- **Splitting `Reconciles` (pure predicate) from `AssertReconciles` (throws) mirrors `PaymentWordingTests`'s existing denylist/scan split** for the same reason: a "watched fail" acceptance criterion needs something a test can call directly with a known-bad input and observe fail, not merely a scan that happens to currently pass.

**Rejected.**

- **A second store-local/UTC conversion inside the reporting code, independent of `StoreTimeZone`.** Would create exactly the two-definitions risk this ADR's whole point is to close off, for no benefit — `StoreTimeZone` already handles the one edge case this zone has (no DST) correctly.
- **Using the literal 23:59:59 timestamp as the card's requested "divergence" proof, without checking the arithmetic.** Would have shipped a false claim into `docs/report-specification.md` — the exact defect class (a claim that outran its evidence) three consecutive phase gates were failed by. Checked instead, and both the true and the coincidental cases are stated.
- **A reconciliation harness that also fetches the detail endpoint itself**, so a report test only supplies its own total. Would silently reintroduce a single code path — the harness deciding how to query the "independent" side removes the independence the whole card exists to guarantee; the caller must supply both totals, computed by genuinely separate code.
- **Deferring the missing sales-side detail endpoints to a new, separately-scoped card**, rather than recording them as part of P6-02/P6-03's own scope. Spec section 14 and `plan.md` section 7 assign the twelve reports to Track B by name; splitting the detail-endpoint work into a thirteenth card the tasks.md track structure does not describe would only relocate the discovery to mid-P6-02, which is what this ADR's point 6 exists to avoid.

**Evidence.** `evidence/phase-6/p6-01-report-definitions.txt` — the guardrail run, the watched-fail proof for both synthetic reconciliation scenarios, the store-local-boundary worked-timestamp proof, and the full green guardrails + both suites run (471/471 integration, including a pre-existing unrelated `LowStockAndStockReviewTests` fixture-accumulation defect found and fixed along the way, confirmed pre-existing against unmodified `master`).

---

## ADR-024 · CSV export encoding, escaping, and the export/view permission mirror

**Status:** ACCEPTED
**Date:** 2026-08-31
**Decides:** the UTF-8 BOM question, the formula-injection mitigation, and how a CSV export's permission is guaranteed to equal its report's own view permission. Raised and settled at P6-06, the sole card in Track C.

**Decision.**

1. **Every CSV export begins with a UTF-8 BOM, measured rather than assumed.** Two exports of an identical non-ASCII row (`José Muñoz Ñañez café`), differing only in a leading UTF-8 BOM, were opened through `Workbooks.Open` (the same code path a double-click uses) on this machine's Excel install (`EXCEL.EXE` file version `16.0.20426.20000`, Microsoft 365 Click-to-Run) via COM automation. Both opened correctly — this Excel build auto-detects BOM-less UTF-8. The BOM is written anyway (`CsvExporter.BuildFile`): it is zero cost on a build where auto-detection already works, and the well-documented older-Excel failure mode it exists to guard against — a BOM-less UTF-8 CSV misread against the system ANSI code page, mangling every non-ASCII character — is not something this project can assume the classroom machine is free of. `Encoding.GetBytes` was found, empirically, to never emit the preamble regardless of `UTF8Encoding.encoderShouldEmitUTF8Identifier` (that flag only takes effect through a `StreamWriter`); `CsvExporter.BuildFile` prepends `Encoding.GetPreamble()` explicitly. `CsvExporterTests.BuildFile_AlwaysBeginsWithUtf8Bom` asserts the three BOM bytes directly.
2. **A field whose content begins with `=`, `+`, `-`, or `@` is prefixed with a single leading apostrophe (`'`) before RFC4180 quoting is decided.** Measured, not assumed: literal `=1+1`, `+1+1`, and `-1+1` fields, opened via the same `Workbooks.Open` COM path, were evaluated as live formulas (`Range.HasFormula = True`, values `2`, `2`, `0` respectively) — a genuine CSV-injection exposure. A leading `@` did **not** trigger formula evaluation on this build, but is mitigated anyway, since the card's own Done-when box names all four characters and a different Excel build may behave differently. Prefixing any of the four with `'` produced `HasFormula = False` for all four, whether or not the field was also RFC4180-quoted for an embedded comma. The apostrophe stays **visible** in the cell's displayed text (`Range.Text`) — unlike the same character typed directly into a cell, where Excel hides it. This is the accepted trade-off: a visible leading apostrophe on a would-be-formula value is the price of the value never executing, and it is recorded here rather than left as a surprise. `CsvExporter.EscapeField` implements this; `CsvExporterTests` asserts each of the four characters individually, plus the composed case (a leading `=` with an embedded comma, which is both prefixed and quoted).
3. **Escaping is otherwise RFC4180**: a field is wrapped in double quotes when it contains a comma, a double quote, a CR, or an LF; an embedded double quote is doubled. `Nothing` becomes an empty field, never the literal text "Nothing".
4. **Every one of the twelve spec section 14 reports gets a CSV export**, not only the six already living in `ReportsController` — `ProcurementReportsController` and `InventoryReportsController` each gained their own `.../csv` routes alongside their existing JSON routes. Export permission equals view permission **by construction**: every export action lives in the same controller as its JSON sibling, under the same class-level `<Authorize(Policy:=PolicyRegistry.Names.ReportsView)>` attribute, with no method-level override on either. `CsvExporterTests.EveryReport_ExportPolicyEqualsViewPolicy` proves this per report, by reflection, rather than by inspection — it resolves each of the twelve view/export route pairs' effective `Authorize` policy independently and asserts both are equal to each other and to `Reports.View`, so a future export action that acquired its own, looser policy would fail the suite by name.
5. **A CSV export returns every row matching the caller's filters, never just the on-screen page.** `page`/`pageSize` are accepted on the JSON routes but not on the CSV routes; each export action calls the same `ReportService` method with `page:=1, pageSize:=Integer.MaxValue` — safe because the repository's `OFFSET = (page - 1) * pageSize` is always `0` at `page = 1`, regardless of `pageSize`'s magnitude.
6. **Decimal values are formatted with `Decimal.ToString(CultureInfo.InvariantCulture)` and no format specifier, never routed through `Double`.** `Decimal` already carries the scale it was read from the database with (ADR-004/ADR-004.1), so this reproduces the stored scale exactly — trailing zeros included — without hard-coding a per-column decimal place count. `CsvExporterTests.FormatDecimal_PreservesMoneyScale`/`PreservesQuantityScale` assert this directly.

**Reasoning.**

- **Measuring both decisions against a real Excel install, rather than citing general CSV-injection guidance, follows this project's own standing rule** (CLAUDE.md section 6.2's "measured on this machine, not quoted from documentation," applied here to Excel instead of MariaDB) — general guidance on CSV injection is inconsistent about whether a leading apostrophe survives a file-open versus manual entry, and about which characters Excel actually treats as formula triggers; only measuring settles it for this machine.
- **Building the export/view permission guarantee structurally (same controller, same class attribute) rather than checking it at runtime** is the same reasoning ADR-013's grant model uses for append-only tables: a guarantee that depends on remembering to keep two things in sync is weaker than one where they cannot drift apart without a code change the matrix test would catch.
- **Exporting all matching rows, not the current page**, was a scope decision confirmed with the user before implementation (not written anywhere in `tasks.md`'s literal card text) — an export whose entire purpose is handing someone the full result set would otherwise silently truncate at the 100-row on-screen cap with no visible warning, which is a worse defect than the card not mentioning pagination at all.

**Rejected.**

- **Trusting general CSV-injection advice without measuring it against this machine's actual Excel build.** Would have risked shipping a mitigation whose real behavior (visible apostrophe vs. hidden, which characters actually trigger evaluation) was never confirmed, in a project whose whole discipline is not assuming behavior that can be measured.
- **A single shared `Reports.Export` policy, separate from `Reports.View`.** `docs/report-specification.md` section 8 states plainly that every one of the twelve reports needs exactly `Reports.View` and no report carries a narrower or wider requirement — inventing a second policy purely for export would contradict that section for no behavioral difference, since the two would always be granted to the same roles.
- **Only the six `ReportsController` reports getting a CSV export**, matching the card's literal `Files:` list. Spec section 14's CSV paragraph is not scoped to sales reports, and the card's own Done-when box asks for the permission mirror to be asserted "for each report" (twelve), not a subset — confirmed with the user before implementation.

**Evidence.** `evidence/phase-6/p6-06-csv-export.txt` — the guardrail run and full green test suites. `evidence/phase-6/p6-06-excel-roundtrip/` — the synthetic COM-automation probes behind points 1 and 2 above, the real live-HTTP export (`current-stock-live-export.csv`, 7781 products, one carrying all four hostile characters in its `Name`) captured by `Merchandising.Tests.Integration.CsvExportRoundTripTests`, and the transcript of that same file read back through Excel via COM automation.

---

## ADR-025 · Backup retention, off-host rotation, and the failure-handling contract

**Status:** ACCEPTED
**Date:** 2026-08-31
**Decides:** the retention default and where it lives, whether the off-host copy is rotated the same way as the local one, and what "a failed backup records the error and requires follow-up" actually guarantees end to end. Raised and settled at P6-07, closing CARRY-02 (the debt ADR-019 recorded) and Track D.

**Decision.**

1. **The retention default stays 7, configurable via `SystemSettings.backup.retentionCount`.** Unchanged from P1-17/`0003_backup.sql` - this card promotes the *proof*, not the number. `BackupCommandTests.Backup_PrunesOldestDumpsBeyondRetentionCount` now asserts which files survive, not merely how many: it names the oldest of three produced dumps and asserts specifically that one is gone and the two newer ones are not, closing the gap where a count-only assertion would pass identically for a bug that pruned the wrong end of the list.
2. **The off-host copy is rotated by the same `RetentionCount` as the local directory.** Before this card the off-host folder was never pruned at all - `TryCopyOffHost` copied a new, uniquely-timestamped file on every run and nothing ever came off, so the one drive meant to survive the host's death would have quietly filled up long before that death happened. `BackupCommand.PruneOldDumps` now takes its target directory as a parameter instead of reading `BackupSettings.Directory` directly, and `ExecuteAsync` calls it twice - once for the local directory, once for the off-host folder when a copy was made - rather than inventing a second method or a separate configuration key. One rule, two call sites.
3. **CARRY-02 is closed with a real assertion, not upgraded evidence.** `Backup_WhenOffHostVolumeAttached_CopiesByteIdenticalDump` runs a real backup against a real volume labelled `MERCHBACKUP` (attached to this machine as `D:` for this run) and asserts `Succeeded`, a non-null `OffHostPath`, byte-identical content, and a matching recomputed checksum. `Backup_PrunesOffHostDumpsBeyondRetentionCountToo` proves point 2 the same way. Both **skip loudly** - `Assert.Inconclusive` with an explanatory message - when no such volume is attached, rather than silently passing or being left out of the suite. This is the opposite call from ADR-019, deliberately: ADR-019 rejected `Assert.Inconclusive` for `Backup_Succeeds_WritesDumpWithChecksumThatVerifies` because that test's entire checksum-verification purpose could be, and should be, proven with no volume attached at all - conflating "no USB stick" with "the checksum didn't verify" would have hidden a real regression behind a hardware condition. These two tests have no other purpose than the off-host copy; without a volume there is nothing left in them to assert, so Inconclusive states the true situation instead of inventing a pass.
4. **The failure-handling contract: a failed run always ends in a recorded outcome, with no exception ever escaping `ExecuteAsync` for an operational fault.** Before this card that was true only for a failure mysqldump itself reported (a bad exit code, a truncated file, wrong credentials). It was **not** true for an unwritable backup directory - `MysqlDumpRunner.WriteDefaultsFile` writes the `--defaults-extra-file` credential file into the backup directory before mysqldump ever starts, and a denied ACL on that directory threw `UnauthorizedAccessException` straight out of `ExecuteAsync`, uncaught. Nothing was recorded anywhere: not a `BackupLogs` row, not the local failure-log fallback. That contradicted spec section 15's "Failure handling" control directly, and the class's own prior doc comment even named it as expected behaviour ("Programming errors - a missing mysqldump.exe, an unwritable directory - still throw"), which was simply wrong about what the second case is. `ExecuteAsync` now catches `UnauthorizedAccessException` and `IOException` around directory creation and dump invocation and routes them through the same `FailAsync` path every other failure branch uses, ending in `BackupOutcome.Failed` and a `BackupLogs` row (or the local failure log if the database itself is unreachable). Only a missing `mysqldump.exe` (`FileNotFoundException`) still throws - a setup fault with no directory to record the failure in and no meaningful correlation ID yet.
5. **The induced failure the card names - an unwritable directory - is proven by ACL, not by removing permissions from the process account.** `Backup_WithUnwritableDirectory_ReportsFailureAndNeverClaimsSuccess` adds a `Deny` `FileSystemAccessRule` for the current Windows identity on the test's own temp directory (never the operational `C:\MerchandisingBackups`), runs a backup, asserts `Failed` with a non-empty detail and no dump file left behind, and - because this failure never touches the database connection - additionally asserts the row landed in `BackupLogs`, which the existing wrong-credentials test cannot (there, the same broken credentials that fail the dump also make `BackupLogs` unreachable). The deny rule is removed in a `Finally` block before `TestCleanup` deletes the directory.
6. **"Not served by the API" is now asserted, not only true by the absence of `UseStaticFiles` in `Program.vb`.** `ApiHttpTests.BackupDirectory_RequestedThroughTheApi_Is404` requests three plausible paths (the real default directory name, a `/backups/...` route, and an `/api/v1/backups` route) through the real `WebApplicationFactory` pipeline and asserts 404 on all three - so a static-file handler added later for an unrelated reason (serving generated reports, say) would fail this test unless it explicitly excluded the backup directory, rather than silently starting to serve it.

**Reasoning.**

- **The unwritable-directory gap was found by the card's own acceptance test, not by inspection.** Writing `Backup_WithUnwritableDirectory_ReportsFailureAndNeverClaimsSuccess` against the *original* `BackupCommand` failed with an unhandled `System.UnauthorizedAccessException` thrown from `MysqlDumpRunner.WriteDefaultsFile` - not the mysqldump-exit-code path the test was originally written to exercise. That is exactly the discipline `CLAUDE.md` asks for: a failing test first, watched fail for the right reason, before the production code changes.
- **Applying the local retention rule to the off-host folder, rather than a separate policy**, follows the same reasoning as point 1's parameterized `PruneOldDumps`: `plan.md` names "off-host rotation" as a distinct Phase 6 deliverable from local retention, but nothing in spec section 15 or the task card asks for a *different* count, and inventing one would be a decision with no requirement behind it.
- **Skipping loudly rather than silently for the two volume-dependent tests** matches the card's own wording ("skips loudly rather than silently when none is present") and is the correct application of ADR-019's own distinction, not a reversal of it: ADR-019's rejection of `Assert.Inconclusive` was scoped to a test that could prove its real point without hardware. These two cannot.

**Rejected.**

- **A separate off-host retention count in `SystemSettings`.** Would let an operator configure the off-host folder to grow without bound relative to the local one, defeating the point of rotating it at all, for a distinction nothing in the spec asks for.
- **Catching `Exception` broadly around the dump invocation**, rather than the two specific types. Would have silently swallowed a genuine programming error (a null reference, an argument exception) as if it were an operational backup failure, contradicting `CLAUDE.md` section 10's rule that a caught exception must be handled meaningfully or rethrown - `UnauthorizedAccessException` and `IOException` are the two failure modes an unwritable or full filesystem actually raises; nothing else belongs in this catch.
- **Leaving CARRY-02 evidence-only for another phase.** ADR-019 explicitly assigned this debt to "Phase 6 promotes backup to production quality" - deferring it again would need a new ADR-016-shaped justification this card has no grounds for, since a real `MERCHBACKUP` volume is attached to the machine this phase is being done on.

**Evidence.** `evidence/phase-6/p6-07-backup-retention.txt` - guardrails, full unit suite, and full integration suite (including both CARRY-02 tests run against the real `D:` `MERCHBACKUP` volume, reported Passed rather than Inconclusive).

---

## ADR-027 · Health endpoint build identity, and why in-process tests cannot prove it

**Status:** ACCEPTED
**Date:** 2026-08-31
**Decides:** how the deployed API's commit and build time are exposed and checked, and why `WebApplicationFactory`-based integration tests are structurally unable to catch a stale deployment - CARRY-05, found at the Phase 4 and Phase 5 gates, both times by hand. Raised and settled at P6-10, Track E.

**Decision.**

1. **The commit SHA and build timestamp are baked into the compiled assembly at publish time, as `AssemblyMetadata` attributes - never read from the working tree at request time.** `scripts/publish-release.ps1` resolves `git rev-parse HEAD` and the current UTC time exactly once, before the Api's `dotnet publish` runs, and passes them as `-p:MerchCommitSha=... -p:MerchBuildTimestampUtc=...`. `Merchandising.Api.vbproj` declares matching `AssemblyMetadata` items, defaulting both to the literal `"unknown"` when the properties are not supplied - the case for every ordinary `dotnet build` or `dotnet test`. `HealthController.GetHealth` reads them by reflection, the same pattern already used for `version`, and adds them to the existing anonymous payload as `commitSha` and `buildTimestampUtc`.
2. **This uses the SDK's built-in `GenerateAssemblyInfo` target, not a source generator.** That target is a plain MSBuild step that writes a physical `AssemblyInfo.vb` into `obj/` via the `WriteCodeFragment` task, and has worked identically for VB, C# and F# since SDK-style projects existed - it predates Roslyn incremental source generators entirely. It is unrelated to `EnableRequestDelegateGenerator` and does not reopen CLAUDE.md section 3's C#-source-generator prohibition.
3. **`scripts/install-service.ps1` gained `-CheckOnly`.** Without touching the service, ACL or Event Log, it calls the local `/health`, compares `commitSha` against `git rev-parse HEAD`, prints both values, and exits non-zero on a mismatch (`Test-DeployedBuildIdentity`, reusing the script's existing `Stop-WithError` convention). A normal install runs the identical comparison as its last step and refuses to print "Install complete." on a mismatch.
4. **Why `WebApplicationFactory`-based integration tests can never catch this class of defect.** Every integration test in this repository builds the API host in-process, from the assembly the test run itself just compiled - so any staleness check exercised only through that harness would compare the current tree against a host built from the current tree, and could never disagree with itself. The defect this card closes is specifically the gap *between* what is on disk in the repository and what is running as a separately-deployed Windows Service; a factory that never leaves the test process structurally cannot observe that gap. That is precisely why P4's and P5's full integration suites - 505 passing tests including this phase's own health test - never noticed the deployed service was stale, and why the mechanism instead has to reach out over the real network to the real, separately-published binary (`Test-DeployedBuildIdentity`'s `Invoke-RestMethod` against `127.0.0.1:8443`).

**Reasoning.**

- **Stamping at publish time rather than reading git at request time** is the one property the whole card exists to establish: a value the running process could recompute live from the tree would track the tree, not the binary, and would report "current" forever regardless of whether anyone had redeployed - reproducing exactly the defect being fixed.
- **A commit SHA is safe to expose unauthenticated.** It identifies no person, credential, machine name or filesystem path, and it is already public in every clone of this repository; CLAUDE.md section 5's "leaks nothing beyond commit and timestamp" is satisfied by construction, and `ApiHttpTests.Health_ReturnsBuildIdentityAndNothingElse` asserts the payload's key set is exactly `status`, `version`, `utcTime`, `commitSha`, `buildTimestampUtc` - nothing else can be added to this response without that test failing.
- **The watched-fail rehearsal is the actual proof, not the code.** `Test-DeployedBuildIdentity -CheckOnly` was run against the API already installed on this machine (published at commit `173fb39`, the Phase 5 gate commit) before this task's own commit existed, confirmed red for the right reason (a real, pre-existing stale deployment - exactly the fault this card was raised to close), then republished and reinstalled from this task's own commit and confirmed green; then, without any redeploy, `-CheckOnly` was run again against the parent of this task's commit and confirmed red once more, purely from the commit boundary itself. Full transcript in the evidence file.

**Rejected.**

- **A companion JSON file written beside the executable at publish time, read by the controller at request time**, instead of an assembly attribute. Works, but adds a second artifact that has to travel with the binary and can be edited or go missing independently of it; an `AssemblyMetadata` attribute is part of the same file the process is already running from, and cannot be separated from it by an incomplete copy.
- **SourceLink** (`Microsoft.SourceLink.GitHub` or similar) to populate `SourceRevisionId` automatically. It requires a recognised remote provider and targets `AssemblyInformationalVersionAttribute`'s `+`-suffix specifically, which `ResolveProductVersion` already strips deliberately (see `HealthController.vb`) so the product version does not carry build metadata a client has no use for. Introducing a second, differently-shaped mechanism just for the commit SHA, when a plain MSBuild property passed by the one script that ever publishes this project does the whole job, is not a trade this card needs to make.
- **Reading `git rev-parse HEAD` inside the running API process**, e.g. at startup. Requires `git.exe` and a repository clone to exist on the deployment host, which is not guaranteed and is not this project's dependency to add; and it reports the tree the process happens to be sitting next to, not the commit the running binary was actually built from - the two are only the same immediately after a publish, which is exactly the moment this check is least needed.
- **Only wiring the comparison into `install-service.ps1`'s normal install path**, with no standalone mode. The card's own watched-fail test requires checking staleness *without* redeploying - reinstalling to test whether staleness detection works would always redeploy the very commit being checked against, and could never demonstrate a red result. `-CheckOnly` exists specifically so the check can be run on a schedule, or by hand, against a service nobody is touching.

**Evidence.** `evidence/phase-6/p6-10-build-identity.txt`.

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

