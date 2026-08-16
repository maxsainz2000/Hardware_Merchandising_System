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

**Status:** PENDING — resolve at task P1-02
**Decides:** how `Merchandising.Api.vbproj` is constructed, given that no Visual Basic ASP.NET Core template exists.

**Options.**

| Rung | Approach |
|---|---|
| A | `Sdk="Microsoft.NET.Sdk.Web"`, `OutputType=Exe`, controller-based, `Module Program` / `Sub Main` |
| B | `Sdk="Microsoft.NET.Sdk"` + `<FrameworkReference Include="Microsoft.AspNetCore.App" />`, self-hosted Kestrel |

**Decision:** _(record after P1-02 — still PENDING, deliberately)_

**Reasoning:** _(record the exact build behaviour observed, including any warnings suppressed or properties needed beyond the standard template — this determines whether the P1-02a insurance spike runs)_

**Evidence:** `evidence/phase-1/p1-02-project-file.txt`

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

---

## ADR-002 · MariaDB and .NET connector versions

**Status:** PENDING — **one row only.** Every row below is resolved except the MySqlConnector package version, which legitimately belongs to P1-05. Do not read this PENDING as "versions unknown"; read it as "the connector is not chosen yet."
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
| .NET connector package | MySqlConnector — **← the only unresolved row in this ADR** | P1-05 |
| Test framework package | `MSTest` `4.0.2` — **pinned in ADR-009, which is ACCEPTED.** Recorded here so `CLAUDE.md` §6 ("if a version is not in `docs/adr.md`, it must not appear in a `.vbproj`") is satisfied without a hunt | P1-01 |
| .NET SDK version (`dotnet --info`) | `10.0.301` (commit `96856fd726`) | P0-01 |
| .NET runtimes installed | `Microsoft.AspNetCore.App` 8.0.28 / 9.0.17 / **10.0.9** · `Microsoft.NETCore.App` 8.0.28 / 9.0.17 / **10.0.9** · `Microsoft.WindowsDesktop.App` 8.0.28 / 9.0.17 / **10.0.9** | P0-01 |
| Target framework | `net10.0` / `net10.0-windows` | `Directory.Build.props` |
| Visual Studio | Community 2026, `18.7.1+11911.148`, workloads `ManagedDesktop` + `NetWeb` | P0-01, re-verified P0-07 |

**Reasoning.** MariaDB's own documentation recommends MySqlConnector for MariaDB Server. EF Core is deliberately **not** an MVP dependency — the transaction design uses explicit provider transactions and parameterized ADO.NET, so no unverified EF Core + MariaDB provider combination sits under the correctness guarantees. Closes gap G-05.

**Rejected:** MySql.Data (Oracle connector), EF Core with Pomelo or the Oracle provider.

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

---

## ADR-004 · Numeric precision

**Status:** PENDING — confirm with professor at task P1-07

| Kind | Type | Rationale |
|---|---|---|
| Money | `DECIMAL(19,4)` | Exact arithmetic; no floating-point representation error in currency |
| Quantity | `DECIMAL(19,3)` | Hardware stores sell fractional units (metres of cable, kilos of nails) |
| Timestamps | `DATETIME(6)` UTC | Microsecond precision; UTC storage, Asia/Manila display |

**Decision:** _(confirm or adjust after professor review)_

**Consequence.** `Double` and `Single` are forbidden for money and quantity everywhere — storage, calculation, and transport. Use `Decimal` in VB.

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

---

## ADR-005 · Authentication and token scheme

**Status:** PENDING — resolve at task P1-08

**Options.** JWT bearer with a host-held signing key, or an opaque server-side session token stored in the database.

**Decision:** _(record after P1-08)_

**Baseline lean:** JWT bearer, expiry matched to a work shift, signing key stored only in the host's ACL-protected configuration location.

**Fixed regardless of the choice:**

- Passwords stored only as salted hashes via a framework-provided hasher. Never plaintext, never reversible.
- Account lockout after five failed attempts within the configured window.
- Token held in client memory only; cleared on logout or auth failure.
- Authorization enforced by policy in the API on every protected endpoint. UI hiding is not a security control.

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

---

## ADR-007 · Idempotency for transactional commands

**Status:** PENDING — prove at task P1-14
**Decides:** how retries avoid creating duplicate sales, receipts, returns, or adjustments. Closes gap G-13.

**Decision.** `IdempotencyKeys` table with a unique constraint on `(Scope, KeyValue)`. Insert-first strategy: the command claims its key before doing work, and the committed response payload is stored and replayed verbatim on repeat.

---

## ADR-008 · Migration mechanism

**Status:** PENDING — resolve at task P1-06

**Decision.** Numbered, forward-only SQL files in `db/migrations/NNNN_description.sql`, applied by `Merchandising.Maintenance`. Each file is checksummed; `SchemaMigrations` records identifier, checksum, timestamp, and result. The runner refuses to proceed if a previously applied file's checksum has changed.

**Consequence.** Applied migrations are **immutable**. Corrections are new migrations, never edits. This is stop condition 6 in `CLAUDE.md`.

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

**Status:** PENDING — resolve at task P1-03
**Decides:** framework-dependent vs self-contained publishing. Closes gap G-16.

| Component | Decision |
|---|---|
| WPF clients | _(baseline: framework-dependent `win-x64` + .NET 10 Desktop Runtime prerequisite check)_ |
| API Windows Service | _(baseline: self-contained `win-x64` if host runtime installation proves fragile)_ |
| Maintenance utility | _(match the API's choice)_ |

**Fixed:** `win-x64` runtime identifier throughout. Never `PublishAot` or `PublishTrimmed` — neither works with Visual Basic.

---

## ADR-011 · Certificate strategy

**Status:** PENDING — cert strategy resolves at task P1-09; host name choice confirmed at P0-05
**Decides:** how HTTPS is trusted on client laptops. Closes gaps G-07 and G-25.

**Baseline lean:** self-signed certificate with subject/SAN covering the host name `MERCH-HOST` (and the reserved IP if client configuration will use it), with a documented manual trust-installation procedure per client.

**P0-05 note:** host name `MERCH-HOST` confirmed as the name to use (matches the baseline lean above — no reason found to deviate). Host's current LAN address is `192.168.100.165` (static, see environment manifest §4), which the SAN should also cover per the baseline lean, since P1-09 hasn't yet ruled that out. `MERCH-HOST` → `192.168.100.165` resolution is **not yet configured on any client** — no client laptops are provisioned (same gap as P0-02/P0-03).

**Decision:** _(record after P1-09)_

**Fixed:** HTTPS mandatory for production-like operation on port 8443. HTTP permitted only on an isolated developer machine, visibly labelled non-production.

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
