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

**Decision:** _(record after P1-02)_

**Reasoning:** _(record the exact build behaviour observed, including any warnings suppressed or properties needed beyond the standard template — this determines whether the P1-02a insurance spike runs)_

**Evidence:** `evidence/phase-1/p1-02-project-file.txt`

---

## ADR-002 · MariaDB and .NET connector versions

**Status:** PENDING — MariaDB rows resolved at P0-04; XAMPP version, .NET connector, and .NET SDK rows still pending P0-03, P1-05, P0-01
**Decides:** the exact database and data-access versions the whole project is built and tested against. Closes gap G-04.

| Item | Value | Source |
|---|---|---|
| XAMPP version | _(record)_ | P0-03 |
| MariaDB server version (`mariadb --version`) | `10.4.32-MariaDB` for Win64/AMD64 (mariadb.org binary distribution) — captured via `mysqld.exe --version`; this XAMPP distro ships no `mariadb.exe` binary | P0-04 |
| Dump tool available (`mariadb-dump` / `mysqldump`) | **`mysqldump.exe`** at `C:\xampp\mysql\bin\mysqldump.exe`, Ver 10.19 Distrib 10.4.32-MariaDB. No `mariadb-dump.exe` exists in this distribution, despite the card's stated preference — P1-17 must build on `mysqldump` | P0-04 |
| MariaDB config file path | `C:\xampp\mysql\bin\my.ini` | P0-04 |
| Data directory | `C:/xampp/mysql/data` | P0-04 |
| Port | `3306` | P0-04 |
| `bind-address` | `127.0.0.1` (loopback) — previously unset, server listened on wildcard `::`; changed and restart-verified | P0-04 |
| .NET connector package | MySqlConnector _(pin exact version)_ | P1-05 |
| .NET SDK version (`dotnet --info`) | _(record)_ | P0-01 |
| Target framework | `net10.0` / `net10.0-windows` | `Directory.Build.props` |

**Reasoning.** MariaDB's own documentation recommends MySqlConnector for MariaDB Server. EF Core is deliberately **not** an MVP dependency — the transaction design uses explicit provider transactions and parameterized ADO.NET, so no unverified EF Core + MariaDB provider combination sits under the correctness guarantees. Closes gap G-05.

**Rejected:** MySql.Data (Oracle connector), EF Core with Pomelo or the Oracle provider.

---

## ADR-003 · Character set, collation, and storage engine

**Status:** PENDING — resolve at task P1-04

**Decision:** _(record — baseline is InnoDB, `utf8mb4`, with the collation confirmed against the installed MariaDB version)_

**Reasoning.** Hardware product names include punctuation and possibly non-ASCII characters. `utf8mb4` avoids a class of silent truncation. InnoDB is required for the transaction and row-locking behaviour the entire correctness design depends on — MyISAM would make ADR-006 unimplementable.

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

**Status:** PENDING — resolve at task P1-19

**Options.** MSTest, NUnit, xUnit — Microsoft publishes official Visual Basic guidance for all three on .NET, so this is a preference rather than a risk.

**Decision:** _(record after P1-19)_

**Constraint.** All test source is Visual Basic. Integration tests run against the real pinned MariaDB, never an in-memory substitute.

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

**Status:** PENDING — resolve at task P1-09
**Decides:** how HTTPS is trusted on client laptops. Closes gaps G-07 and G-25.

**Baseline lean:** self-signed certificate with subject/SAN covering the host name `MERCH-HOST` (and the reserved IP if client configuration will use it), with a documented manual trust-installation procedure per client.

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
