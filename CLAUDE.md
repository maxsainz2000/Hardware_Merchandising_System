# CLAUDE.md — Agent conventions for the Merchandising System

**Read this file at the start of every session. It overrides your defaults.**

---

## 1. What this project is

A Windows client-server **Merchandising System for a mid-scale hardware store**, built as an academic MVP under binding course constraints.

- **Three WPF desktop clients:** Procurement, Inventory, POS
- **One central API:** ASP.NET Core Web API, hand-authored, running as a Windows Service
- **One database:** MariaDB supplied through XAMPP
- **Target:** .NET 10, Visual Studio 2026, `win-x64`, private LAN, online-only

Authoritative documents, in order of precedence:

1. `documentations/Merchandising System for a Mid-Scale Hardware Store.md` — the spec (what and why)
2. `plan.md` — phases, gates, workflow (how and in what order)
3. `tasks.md` — the current phase's task cards (what to do right now)
4. `docs/adr.md` — every pinned version and irreversible decision

If any two disagree, the higher one wins and the lower one gets corrected.

---

## 2. The language rule — absolute

**All application source code in this repository is Visual Basic .NET.**

This is a binding course requirement, confirmed by the professor. There is **no C# escape hatch**. If you find yourself about to write C#, or to add a package that requires C# source generation, **stop and report a blocker** instead of proceeding.

Permitted non-VB artifacts: XAML markup, SQL migration scripts, PowerShell build scripts, JSON/XML configuration, Markdown documentation. Nothing else.

A build that produces `.cs` or `.csproj` files anywhere under `src/` is a failed build. `scripts/check-no-csharp.ps1` enforces this.

---

## 3. VB gotchas — read before writing any API code

Your training data is overwhelmingly C# ASP.NET Core. These are the places that will trip you.

- **No top-level statements.** Every entry point is `Module Program` with `Sub Main` (or `Async Function Main() As Task`). If you emit `var builder = WebApplication.CreateBuilder(args);` at file scope, you have drifted into C#.
- **Attributes use angle brackets:** `<HttpGet("route")>`, not `[HttpGet("route")]`. Put each attribute on its own line above the member — VB's angle brackets collide visually with XML literals.
- **Source generators are C#-only.** The Request Delegate Generator, `System.Text.Json` source generation, and Native AOT / trimming do **not** work from VB.
  - `<EnableRequestDelegateGenerator>false</EnableRequestDelegateGenerator>` is set in `Directory.Build.props`. Do not remove it.
  - Use reflection-based JSON serialization.
  - Never set `PublishAot` or `PublishTrimmed`.
- **No Razor, Blazor, MVC views, or `.cshtml`.** The API returns JSON only. Never scaffold a view.
- **Prefer controller-based endpoints over minimal APIs.** Minimal-API lambda chains are awkward in VB (multi-line `Function() … End Function`) and depend most heavily on the C#-only source generators. Controllers are verbose and reliable — that is the right trade here.
- **`Option Strict On` and `Option Explicit On`** are set globally in `Directory.Build.props`. Do not override them per-project. Late binding is a compile error, deliberately.
- **Integration tests need a reachable entry point.** `WebApplicationFactory` requires `Program` to be visible — use `Public Module Program` and configure `InternalsVisibleTo` for the test assembly.
- **Multi-line lambdas need explicit terminators:** `Function(x) … End Function`, `Sub(x) … End Sub`.
- **String comparison:** `Option Compare Binary` is set. Use `String.Equals(a, b, StringComparison.OrdinalIgnoreCase)` for case-insensitive comparison, never `=` with `Option Compare Text` assumptions.

---

## 4. Project dependency direction — enforced

```
Domain         ← depends on nothing
Contracts      ← Domain
Infrastructure ← Domain, Contracts
Api            ← Domain, Contracts, Infrastructure
ClientCommon   ← Contracts            ← NEVER Infrastructure
Procurement / Inventory / POS ← ClientCommon, Contracts
Maintenance    ← Infrastructure, Domain
```

**The rule that matters most: `ClientCommon` and the three WPF clients must never reference `Infrastructure`, and must never take a dependency on MySqlConnector or any database package.**

That single rule is what mechanically guarantees a database credential can never reach a client laptop. It is enforced by guardrail G-B, not by good intentions.

---

## 5. Architectural rules that are not negotiable

**The API is authoritative.** Clients may validate input for usability. The API re-validates *everything* that matters, because clients cannot be trusted. Every business rule is implemented server-side exactly once. No client ever writes to the database directly.

**Every stock-changing operation is atomic.** It commits all of its effects or none of them. Specifically it must, in one transaction:
1. Apply a **conditional** update (never read-then-write):
   ```sql
   UPDATE StockBalances
      SET Quantity = Quantity - @qty,
          RowVersion = RowVersion + 1,
          UpdatedAtUtc = UTC_TIMESTAMP(6)
    WHERE ProductId = @productId
      AND Quantity >= @qty;
   -- affected rows must be exactly 1, else roll back and return a controlled 409
   ```
2. Insert a `StockMovements` row (delta, quantity before, quantity after, reason, actor, correlation ID)
3. Insert an `AuditLogs` row
4. Verify the affected-row count before returning success

**`StockMovements` and `AuditLogs` are append-only.** Never `UPDATE` or `DELETE` them. Corrections are compensating movements, never edits.

This is enforced **by database grant**, not only by policy, and the mechanism is specific: `merch_api` holds **no** write privilege at the database level, so every table is append-only by default. `UPDATE`/`DELETE` are added back one table at a time in `db/grants/0002_post-migration-grants.sql`, and those two ledgers are deliberately absent from that list. MariaDB has no `DENY` and unions privileges across scopes, so this ordering is the only way the guarantee can be real — see ADR-013. Proven at `evidence/phase-1/p1-04a-grant-model-proof.txt`.

**Money is `DECIMAL(19,4)`. Quantities are `DECIMAL(19,3)`.** Never use `Double`, `Single`, or floating-point arithmetic for money or quantities anywhere — storage, calculation, or transport. Use `Decimal` in VB.

**All timestamps are stored in UTC** (`DATETIME(6)`), displayed in the store time zone (Asia/Manila).

**Every transactional command carries a client-generated idempotency key.** A repeated key returns the original committed result — it never creates a second sale, receipt, return, or adjustment.

**Errors never leak internals.** Every error response has a stable error code, a human-readable message, a correlation ID, and field-level validation detail where applicable. Never a stack trace, SQL text, connection string, or environment detail.

---

## 6. Never invent versions

Package versions, the MariaDB version, the connector version, and the .NET target all come from `docs/adr.md`.

If you need a package or version that is not recorded there, **stop and ask**. Do not pick "latest". Do not guess. A version drift between the dev machine and the classroom host is exactly the class of bug this project cannot afford.

### 6.1 The pins, in context every session

Measured on this machine, not quoted from documentation. Full detail and the evidence behind each row is in `docs/adr.md` ADR-002 and ADR-003.

```
MariaDB       10.4.32 (XAMPP 8.2.12-0) — utf8mb4 / utf8mb4_unicode_ci / InnoDB
              NOT uca1400 — that collation family is MariaDB 11.x only, 0 exist here
Dump tool     C:\xampp\mysql\bin\mysqldump.exe (Ver 10.19 Distrib 10.4.32-MariaDB)
              mariadb-dump.exe does NOT exist in this distribution. Neither does mariadb.exe.
.NET SDK      10.0.301 — runtimes 10.0.9 (AspNetCore, NETCore, WindowsDesktop)
TFMs          net10.0 / net10.0-windows
Visual Studio Community 2026, 18.7.1+11911.148 (ManagedDesktop + NetWeb workloads)
```

**Three database identities, and they are not interchangeable (ADR-013):**

```
merch_migrator  owns the SCHEMA. The only account with DDL (incl. DROP).
                Used by Merchandising.Maintenance during a migration run, and
                nowhere else.
                Config: %ProgramData%\MerchandisingSystem\config\database.migrator.json
merch_api       owns the DATA. NO DDL at all. Database-level SELECT only;
                every write privilege is per-table (db/grants/0002). This is
                what makes StockMovements and AuditLogs append-only.
                Config: ...\config\database.json
merch_backup    SELECT, LOCK TABLES, SHOW VIEW, EVENT, TRIGGER. mysqldump only.
root            used by no application, ever.
```

If you are writing code that creates or alters a table, it runs as
`merch_migrator`. If you are writing code that serves a request, it runs as
`merch_api`. Reaching for the wrong one shows up as `ERROR 1142`, not as a
subtle bug — that is deliberate.

### 6.2 Write SQL for MariaDB 10.4, not MySQL 8 or MariaDB 11

**Your training data skews heavily toward MySQL 8 and MariaDB 11. Features from those versions fail on this server.** These were confirmed by querying the installed instance:

| Do not use | Because | Use instead |
|---|---|---|
| `utf8mb4_uca1400_*` collations | MariaDB 11.x only — zero exist here | `utf8mb4_unicode_ci` |
| `transaction_isolation` | `ERROR 1193 Unknown system variable` on 10.4 | `tx_isolation` |
| `UUID` column type | Added in 10.7 — `ERROR 1064` syntax error here | `CHAR(36)` or `BINARY(16)` |
| `mariadb-dump` / `mariadb` CLI | Not present in this XAMPP build | `mysqldump.exe` / `mysql.exe` |

Two more that bite silently rather than loudly:

- **The server default collation is `utf8mb4_general_ci`, not `unicode_ci`.** State `COLLATE utf8mb4_unicode_ci` explicitly on every `CREATE DATABASE` and `CREATE TABLE`. Inheriting it gets you the wrong one and a later `Illegal mix of collations` error on a join.
- **The default isolation level is `REPEATABLE-READ`, not `READ COMMITTED`.** ADR-006 requires `READ COMMITTED`; set it explicitly. Never assume it.

### 6.3 `sql_mode` — strict now, but never rely on scale being enforced

**Current state on this machine, verified 2026-08-18:**

```
@@sql_mode = STRICT_TRANS_TABLES,NO_ZERO_IN_DATE,NO_ZERO_DATE,NO_ENGINE_SUBSTITUTION
```

`STRICT_TRANS_TABLES` is **present**, in two places, deliberately:

1. **Server-side** — `my.ini` line 157, fixed at P1-04.
2. **Per connection** — `ConnectionFactory` issues `SET SESSION sql_mode = CONCAT(@@SESSION.sql_mode, ',STRICT_TRANS_TABLES')` on every connection, fixed at P1-05, so a reinstalled or rebuilt XAMPP cannot quietly revert the guarantee. Asserted live by `ConnectionFactoryTests`.

**What that buys you:** column widths and decimal *ranges* are now enforced by the database. `'THIS-SKU-IS-FAR-TOO-LONG'` into a `VARCHAR(8)` raises `ERROR 1406` instead of silently storing `'THIS-SKU'`.

**What it does not buy you — this half is still live and still bites:**

> **Decimal *scale* is NOT enforced, even under strict mode.** An over-scale value is still rounded silently: `1.9999` into a `DECIMAL(19,3)` stores `2.000` and reports success (`Note 1265`, not an error). Rounding to storage scale is the **API's** job, not the database's — see ADR-004.1. Validate scale server-side, at the API boundary, before the parameter is bound. A correctly-scaled stored value proves nothing on its own.

> **XAMPP as shipped is not strict.** The default is `NO_ZERO_IN_DATE,NO_ZERO_DATE,NO_ENGINE_SUBSTITUTION` — XAMPP actively weakens MariaDB 10.4's own default. That matters for handover: a classmate installing XAMPP fresh gets the unsafe setting until the bootstrap applies the `my.ini` change. The per-connection half is what makes a mis-installed host safe anyway.

---

## 7. Stop conditions — halt and ask

Do not improvise past any of these:

1. You are about to write C#, or add a package requiring C# source generation.
2. You need a package or version not recorded in `docs/adr.md`.
3. You would put a connection string, credential, or `Infrastructure` reference into a client project.
4. A business rule in the spec is ambiguous, or two spec sections conflict.
5. A test fails in a way suggesting the **design** is wrong rather than the code.
6. You are about to modify an already-applied migration file. (Always write a new numbered migration instead.)
7. You would `UPDATE` or `DELETE` a row in `StockMovements` or `AuditLogs`.
8. The task requires a decision the spec assigns to the Foundation Proof-of-Concept or to the professor.

**If a hook blocks you, that is the answer.** Do not rename the file, write it somewhere else, or disable the hook. A blocked write is a stop condition that has already been evaluated for you — report it and stop.

Reporting a blocker is a successful outcome. Guessing is not.

---

## 8. Working rhythm

**One task = one commit.** Commit message begins with the task ID:

```
P4-07: partial receiving accumulates across receipts
```

**The per-task loop lives in the `/task` skill** (`.claude/skills/task/SKILL.md`). Invoke it with a task ID — `/task P1-02` — or with no argument to pick up the next open card. It carries the whole loop: read the card and the spec sections it cites, **restate scope and stop for confirmation**, failing test first for server-side rules, implement API → Domain/Infrastructure → client last, run the guardrails and tests, capture evidence, tick the boxes, resolve the ADR entry, commit.

**`/phase-gate`** runs the phase exit review and, on a genuine PASS only, regenerates `tasks.md` for the next phase.

**`/orchestrate`** runs several **scopes** in parallel across this machine and `box3` (see
`.claude/fleet/machines.json`). **A scope is one `## Track` from `tasks.md`** — an ordered
list of cards that only make sense together, because tracks are what is independent of each
other and the cards inside one are not. It dispatches **`/worker`** sessions — one scope
each, one commit per card, one structured report back — and does the deciding itself.
Workers never edit `tasks.md`, never push, and report a blocker rather than guessing. Use it
only when there are genuinely independent tracks; a single track is `/task`, card by card.

**Which scope is next is a command, not a judgement call:** `pwsh ./scripts/fleet/fleet.ps1
-Action next` parses `tasks.md` and returns the open tracks in file order with their cards,
their combined file sets, the collisions between them, and a box recommendation. The
orchestrator asks it rather than reading `tasks.md` itself.

**`/health-check`** audits the fleet itself — transports, the deny list that makes a worker's
invariants walls rather than requests, context isolation, and the external ceilings. Run it
in a **fresh** session at `opus`/`high`, after any edit to the fleet scripts or skills and
before the first mission of a phase. `pwsh ./scripts/fleet/health-check.ps1` runs the
mechanical half with no session at all. It diagnoses and never repairs: a `FAIL` is a
finding to report, and `UNVERIFIED` is an honest result that is never rounded up to a pass.

**Never mark a card done with a failing test, a partial implementation, or an unresolved error.** If blocked, leave it open and say why.

---

## 9. Definition of done

**Task:** acceptance checks pass · tests green · guardrails pass · evidence captured if required · `tasks.md` updated · committed with task ID.

**Phase:** every task done · every spec exit criterion has an evidence artifact under `evidence/phase-N/` · phase documents written · `docs/adr.md` updated · a clean-clone build from scratch succeeds.

---

## 10. Code style

- Explicit and verbose over clever. This code gets read under exam pressure.
- `Try`/`Catch` around transaction boundaries gets reviewed line by line — a silently swallowed exception there is the highest-consequence bug class in this system. Never catch without either handling meaningfully or rethrowing.
- Public members get XML doc comments (`'''`).
- One type per file; file name matches the type name.
- Async all the way down for I/O; never `.Result` or `.Wait()`.
- Parameterized SQL only. String-concatenated SQL is a defect regardless of the input source.

---

## 11. Automated enforcement

Four mechanisms, in order of how early they catch a mistake. They overlap on purpose, because each one has a hole the next one covers.

| # | Mechanism | Fires when | Catches | Does **not** catch |
|---|---|---|---|---|
| L1 | `PreToolUse` on `Write`/`Edit` → `.claude/hooks/block-csharp.ps1` | Claude is about to write a file | A `.cs`, `.csproj`, `.cshtml`, or `.razor` file, **before it exists**. Pure path check, ~0 cost. | Anything about *content*. A credential in a `.vb` file sails straight through. |
| L2 | `PostToolUse` on `Write`/`Edit` → `.claude/hooks/check-vbproj.ps1` | Claude just wrote a `.vbproj` | G-B (client referencing `Infrastructure` or a DB package) and G-D (`Option Strict` / `PublishAot` overridden) — the two guardrails that project files break. | Edits to `.vb`, `.xaml`, `.md`, or anything under `bin/`, `obj/`, `evidence/`, `documentations/`. Skipped deliberately: a full rescan on every `.vb` write is slow enough that you would switch the guardrail off, and a disabled guardrail is worse than none. |
| L3 | `Stop` → `.claude/hooks/stop-guardrails.ps1` | End of every Claude turn | Everything L1 and L2 skipped — G-A through G-D across the whole tree. One run per turn, measured at ~730 ms with `src/` empty. | Anything Claude did not do. It is still a scan of the working tree at a single moment. |
| L4 | `.git/hooks/pre-commit` → `scripts/check-no-csharp.ps1` | Every `git commit` | **Edits Claude never saw.** | Nothing in the guardrail set — but it is bypassable by `git commit --no-verify`, which exists as a deliberate escape hatch. |

**The hole L4 exists to cover.** Claude Code hooks fire only when *Claude* edits a file. You will also be editing in Visual Studio 2026 — the WPF designer, the project properties pages, the NuGet UI — and every one of those edits bypasses Claude Code entirely. A `PublishAot` checkbox ticked in a properties page is invisible to L1–L3. The git hook is the only thing standing between that edit and the repository. **Do not remove it on the grounds that the Claude Code hooks already cover it.** They do not.

Install L4 after any fresh clone:

```powershell
pwsh ./scripts/install-hooks.ps1
```

**Settings files.** `.claude/settings.json` is **committed** — these guardrails apply to everyone working in this repo, including future you. `.claude/settings.local.json` is gitignored and is the right place for machine-specific overrides. Never move a guardrail into the local file to make it stop complaining.

Hooks are loaded at **session start**. If you change `.claude/settings.json` or add a skill, restart the session before trusting it, and never report a hook as verified in the same session that created it.

