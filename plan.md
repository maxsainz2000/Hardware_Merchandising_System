# Implementation Plan — Merchandising System for a Mid-Scale Hardware Store

**Companion to:** `documentations/Merchandising System for a Mid-Scale Hardware Store.md` (the spec)
**Status:** Planning baseline. No implementation has started.
**Audience:** Max, working with an AI coding agent (Claude Code / Copilot / equivalent).
**Rule of precedence:** The spec defines *what* and *why*. This plan defines *how* and *in what order*. Where they disagree, the spec wins and this plan gets corrected.

---

## 0. How to read and use this plan

This plan assumes the four-file working set:

| File | Role | Churn |
|---|---|---|
| `documentations/Merchandising System for a Mid-Scale Hardware Store.md` | The spec. Requirements, constraints, acceptance criteria. | Rare, deliberate |
| `plan.md` (this file) | Phase sequence, gates, workflow, guardrails. | Per phase |
| `tasks.md` | The *current phase only*, expanded into agent-sized task cards. Regenerated at each phase entry. | Constantly |
| `docs/adr.md` | Architecture Decision Record. Every pinned version and irreversible choice. | On decision |

Only `tasks.md` is worked from day to day. `plan.md` is re-read at phase boundaries. The agent reads `CLAUDE.md` (repo conventions, §4) on every session automatically.

**Deliberate omission:** no dates, no durations. Phases are sequenced by dependency and gated by evidence, not by calendar. A phase is done when its exit evidence exists, not when time has elapsed.

---

## 1. The core strategic decision

The spec correctly identifies that this project has **one dominant risk** and everything else is ordinary work:

> Can an all-Visual-Basic ASP.NET Core Web API be built, published, hosted as a Windows Service, and talk to XAMPP's MariaDB — on .NET 10, in Visual Studio 2026, on the actual classroom machines?

Everything downstream — Procurement, Inventory, POS, reports, backup — is well-understood CRUD-plus-transactions work that will not surprise you. The VB API foundation might. Therefore:

**The plan front-loads all technical uncertainty into Phase 1 and refuses to build features until it is retired.**

This is not caution for its own sake. If the VB API turns out to be unworkable in week six, you lose everything built on top of it. If it turns out to be unworkable in Phase 1, you lose a few sessions and you go to your professor with evidence and a documented exception request while there is still time to change course.

The corollary matters just as much: **once Phase 1 passes, no later phase is allowed to re-litigate a Phase 1 decision.** The connector, the transaction pattern, the auth scheme, the hosting model, the migration mechanism — all frozen and recorded in the ADR. Later phases consume them.

### 1.1 The fallback ladder for the API

Research confirms the primary approach is sound but not template-supported. Microsoft's Visual Basic team lists ASP.NET Core Web API among VB-supported application types, and the ASP.NET Core repo confirms VB is supported while VB templates are not planned. Community work demonstrates a hand-authored `.vbproj` using `Microsoft.NET.Sdk.Web` builds and runs. Plan for it working; have the ladder ready anyway.

| Rung | Approach | Trigger to descend |
|---|---|---|
| **A (primary)** | `.vbproj` with `Sdk="Microsoft.NET.Sdk.Web"`, `OutputType=Exe`, controller-based API, `Module Program` / `Sub Main`. | — |
| **B** | Plain `Sdk="Microsoft.NET.Sdk"` + `<FrameworkReference Include="Microsoft.AspNetCore.App" />`, self-hosted Kestrel via `WebApplication.CreateBuilder`. Loses some Web SDK conveniences (static assets, publish profiles) — none of which this project needs. | A fails on an SDK target that assumes C# and cannot be worked around. |
| **C** | Documented professor-approved exception: a thin non-VB API shim, or a different transport. | A and B both fail with evidence. |

Descending a rung is an ADR entry, not a quiet decision. Rung C requires written approval before any code is written against it.

> **Confirmed by the professor:** the manually authored VB API is acceptable, and XAMPP is genuinely mandatory. This is the constrained answer, not the relaxed one — **no C# escape hatch has been granted and no alternative database is available.** Two consequences follow, and they shape the rest of this plan:
>
> 1. **Rung B is now the real fallback, not rung C.** Rung C would require going *back* to the professor to reverse a decision already made — a much harder conversation than the pre-emptive one. Treat rung B as the last self-service option and prove it works early (see P1-02a) rather than discovering under pressure that it doesn't.
> 2. **The Phase 1 gate keeps its full weight.** Nothing about this answer shrinks Phase 1. Every one of the twenty tasks still earns its place.

### 1.2 VB-specific gotchas to encode up front

These are the things that will burn an AI agent that has been trained mostly on C# ASP.NET Core. They belong in `CLAUDE.md` verbatim so the agent sees them every session.

- **No top-level statements.** Every entry point is `Module Program` + `Sub Main` (or `Async Function Main() As Task`). An agent that emits `var builder = WebApplication.CreateBuilder(args);` at file scope has drifted into C#.
- **Attribute syntax is `<HttpGet("route")>`, not `[HttpGet("route")]`.** VB's angle-bracket attributes collide visually with XML literals; keep attributes on their own line above the member.
- **Source generators are C#-only.** The Request Delegate Generator, `System.Text.Json` source generation, and Native AOT/trimming will not work from VB. Set `<EnableRequestDelegateGenerator>false</EnableRequestDelegateGenerator>`, use reflection-based JSON serialization, and do not enable `PublishAot` or `PublishTrimmed`. This is a **hard constraint on the .NET 10 publish configuration** and a likely source of confusing build errors if ignored.
- **No Razor, Blazor, MVC views, or `.cshtml` in VB.** The API returns JSON only. This is already the design — just don't let an agent scaffold a view.
- **Prefer controller-based endpoints over minimal APIs.** Minimal API lambda chains are awkward in VB (multi-line `Function() ... End Function`) and lean hardest on the C#-only source generators. Controllers are boring, verbose, and reliable — the right trade here.
- **`Option Strict On` and `Option Explicit On` globally**, set in `Directory.Build.props` so every project inherits it. This turns a class of late-binding runtime surprises into compile errors.
- **Integration tests need a reachable entry point.** `WebApplicationFactory` needs `Program` visible; use a `Public Module Program` (or a `Public NotInheritable Class Program`) and add `InternalsVisibleTo` for the test assembly.
- **Test frameworks are fine.** Microsoft publishes official VB unit-testing guidance for MSTest, NUnit, and xUnit on .NET. This is not a risk; pick one in Phase 1 and record it.

---

## 2. Solution and repository layout

Single Git repository, single Visual Studio solution. Projects exactly as the spec's §7 table names them — do not rename, the spec is the contract.

```
Hardware_Merchandising_System/
├─ CLAUDE.md                      ← agent conventions, read every session
├─ plan.md                        ← this file
├─ tasks.md                       ← current phase only, regenerated per phase
├─ .gitignore  .editorconfig
├─ Directory.Build.props          ← Option Strict On, TargetFramework, LangVersion, analyzers
├─ Merchandising.sln
├─ documentations/
│  └─ Merchandising System for a Mid-Scale Hardware Store.md   ← the spec
├─ docs/
│  ├─ adr.md                      ← every pinned decision
│  ├─ environment-manifest.md     ← exact versions on host + each client
│  ├─ database-design.md          ├─ api-specification.md
│  ├─ role-permission-matrix.md   ├─ ui-specification.md
│  ├─ installation-guide.md       ├─ backup-restore-guide.md
│  ├─ user-guide.md               └─ test-plan.md
├─ evidence/
│  └─ phase-1/ … phase-7/         ← screenshots, logs, test output, port scans
├─ db/
│  └─ migrations/0001_*.sql …     ← numbered, forward-only, checksummed
├─ scripts/
│  ├─ check-no-csharp.ps1         ← guardrail, runs in every build
│  ├─ run-tests.ps1               └─ publish-release.ps1
└─ src/
   ├─ Merchandising.Domain/       ├─ Merchandising.Contracts/
   ├─ Merchandising.Infrastructure/  ├─ Merchandising.Api/
   ├─ Merchandising.ClientCommon/ ├─ Merchandising.Procurement/
   ├─ Merchandising.Inventory/    ├─ Merchandising.POS/
   ├─ Merchandising.Maintenance/
   └─ tests/
      ├─ Merchandising.Tests.Unit/
      └─ Merchandising.Tests.Integration/
```

**Dependency direction is enforced, not merely intended:**

```
Domain        ← depends on nothing
Contracts     ← Domain
Infrastructure← Domain, Contracts
Api           ← Domain, Contracts, Infrastructure
ClientCommon  ← Contracts            (NEVER Infrastructure — that would put DB code on clients)
Procurement / Inventory / POS ← ClientCommon, Contracts
Maintenance   ← Infrastructure, Domain
```

The line that matters most: **`ClientCommon` must never reference `Infrastructure`.** That single rule is what mechanically prevents a database connection string from ever reaching a client laptop (G-10). Add it to the guardrail script, not just to the documentation.

---

## 3. Guardrails — automated, not aspirational

The spec's constraints only hold if violating them breaks the build. Four checks, wired into `Directory.Build.props` as a pre-build target and into `scripts/check-no-csharp.ps1`:

| # | Guardrail | Mechanism | Guards |
|---|---|---|---|
| G-A | No C# application source anywhere in `src/` | Fail build if any `*.cs` or `*.csproj` exists outside `obj/`, `bin/` | G-01 |
| G-B | `ClientCommon`/client projects never reference `Infrastructure` or any MySql package | Project-reference and package-reference scan | G-10 |
| G-C | No connection strings, passwords, or `Server=` literals in client projects or committed config | Regex scan over `src/Merchandising.{ClientCommon,Procurement,Inventory,POS}/**` | G-10 |
| G-D | `Option Strict On` not overridden per-project | Property scan across all `.vbproj` | Code quality |

Run them in `scripts/check-no-csharp.ps1`; make the agent run it before every commit. A guardrail you have to remember to run is a guardrail you will forget to run during finals week.

---

## 4. `CLAUDE.md` — what the agent must know every session

Create this in Phase 0, before any code. Contents:

1. **The one-line project identity** — VB.NET-only WPF + ASP.NET Core + MariaDB academic prototype.
2. **The language rule, stated absolutely** — "All application source is Visual Basic .NET. If you are about to write C#, stop and report a blocker instead."
3. **The VB gotcha list** from §1.2, verbatim.
4. **The dependency direction diagram** from §2.
5. **The API-is-authoritative rule** — clients validate for UX, the API validates for truth; every business rule is implemented server-side exactly once.
6. **The transaction rule** — every stock-changing operation commits all-or-nothing, uses a conditional update, writes a movement row and an audit row, and carries a correlation ID.
7. **Never invent versions.** Package versions, MariaDB version, and .NET version come from `docs/adr.md`. If it is not in the ADR, ask.
8. **Evidence requirement** — a task producing a spec-required behaviour is not done until its evidence file exists under `evidence/phase-N/`.
9. **Definition of done** (§9).
10. **Stop conditions** (§8.3).

---

## 5. Phase 0 — Environment baseline

**Objective:** know exactly what you are building on, before building. The spec's G-30 makes this a gate; treat it as one.

No application code is written in Phase 0.

| Task | Deliverable | Acceptance |
|---|---|---|
| P0-01 | Install VS 2026 with **.NET desktop development** + **ASP.NET and web development** workloads; verify the .NET 10 component. | `dotnet --info` output captured to `docs/environment-manifest.md`. |
| P0-02 | Record Windows edition + build for the host laptop and **each** client laptop. | Table in environment manifest, one row per machine. |
| P0-03 | Install XAMPP on host. Stop and disable Apache, FileZilla, Mercury, Tomcat. Keep MariaDB only. | Services list screenshot; only MariaDB running. |
| P0-04 | Record exact MariaDB version, data directory, config file path, and which dump tool ships with it. **Resolved: 10.4.32, `mysqldump.exe` only — no `mariadb.exe` or `mariadb-dump.exe` in this build, so capture the version via `mysqld.exe --version`.** | Environment manifest + first ADR entry. |
| P0-05 | Reserve host IP on the LAN; choose and record the host name `MERCH-HOST`; confirm resolution from each client (hosts file or DNS). | `ping MERCH-HOST` succeeds from every client; output captured. |
| ~~P0-06~~ | ~~Obtain professor confirmation~~ — **RESOLVED.** Manual VB ASP.NET Core API is acceptable; XAMPP/MariaDB is mandatory, not merely permitted. | Capture the confirmation (email/message//written note) into `docs/professor-approvals.md` so it can be cited at acceptance. Do this even though the answer is known — an undocumented approval is one you cannot point to during sign-off. |
| P0-07 | Initialise repo: folder structure (§2), `.gitignore`, `.editorconfig`, `Directory.Build.props`, `CLAUDE.md` (§4), empty `docs/adr.md`. | `git log` shows initial commit; agent session reads CLAUDE.md correctly. |
| P0-08 | Write `scripts/check-no-csharp.ps1` with guardrails G-A…G-D. | Script passes on empty repo; deliberately drop a `.cs` file and confirm it fails. |

**Exit criteria:** environment manifest complete for host + all clients; professor confirmation recorded in `docs/`; repo scaffolded; guardrail script proven to fail when it should.

**Standing constraint from P0-06:** because both constraints were confirmed as binding, the academic-prototype framing in spec §3 and §29 is not optional hedging — it is the accurate description of what you are delivering. Every document, the presentation, and the cover page state that XAMPP is a course requirement and that the result is not a production-readiness claim. Getting this wording right early costs nothing; retrofitting it into ten finished documents during Phase 7 is miserable.

---

## 6. Phase 1 — Foundation Proof-of-Concept (the gate)

**Objective:** prove all twelve items in spec §24 with captured evidence. Build the thinnest possible vertical slice that exercises every risky seam. Resist every urge to build a real feature here.

**Scope discipline:** one WPF window with two buttons. One product. One protected endpoint. That is enough. If you find yourself designing a product grid in Phase 1, stop.

### 6.1 Task breakdown

Each card: **goal → deliverable → acceptance check → evidence artifact**.

#### Track A — Solution and API skeleton

**P1-01 · Create the solution and all eleven empty VB projects**
Deliverable: `Merchandising.sln` with every project from §2, correct target framework, correct dependency references wired per §2 diagram.
Acceptance: `dotnet build` succeeds; `check-no-csharp.ps1` passes; dependency guardrail G-B passes.
Evidence: `evidence/phase-1/p1-01-build.log`, solution screenshot.

**P1-02 · Hand-author `Merchandising.Api.vbproj` on the Web SDK (rung A)**
Deliverable: `.vbproj` with `Sdk="Microsoft.NET.Sdk.Web"`, `OutputType=Exe`, `EnableRequestDelegateGenerator=false`, no AOT/trim; `Program.vb` with `Module Program` / `Sub Main`; one controller `HealthController` exposing `GET /health` returning `{status, version, utcTime}` and **no** secrets, DB status detail, or environment strings.
Acceptance: `dotnet run` starts Kestrel; `GET /health` returns 200 with expected JSON; **zero C# files in the project**.
Evidence: `p1-02-health-response.txt`, `p1-02-project-file.txt`.
Blocker protocol: if the Web SDK fails on a C#-assuming target, attempt rung B **once**, record the exact error, and log an ADR entry. Do not spend more than one session fighting rung A before descending.

**P1-02a · Rung B insurance spike — conditional, time-boxed to 30 minutes**
Run this **only if P1-02 showed any friction at all** (an SDK target that needed a workaround, a build warning you had to suppress, a property you had to set that isn't in the standard template). If rung A built cleanly on the first attempt, skip this task — proving a fallback you have no reason to need is busywork.
If it *did* show friction, the reasoning changes: friction now means a future SDK or VS update is likely to break rung A outright, and since the professor's answer removed the C# escape hatch, rung B is your last self-service option. Thirty minutes to confirm it exists is cheap.
Deliverable: a scratch branch with `Sdk="Microsoft.NET.Sdk"` + `<FrameworkReference Include="Microsoft.AspNetCore.App" />`, serving the same `/health` endpoint.
Acceptance: `/health` responds, or the failure mode is recorded in the ADR.
Evidence: `p1-02a-rungb-result.txt` (either outcome is a valid result — record it and move on).

**P1-03 · Publish the API as `win-x64` and run the published output**
Deliverable: publish profile / script producing a framework-dependent `win-x64` build; a second run proving self-contained also publishes (so the deployment choice stays open).
Acceptance: published `.exe` runs from a clean folder and serves `/health`.
Evidence: `p1-03-publish.log`, folder listing.

> **Decision point:** P1-02 + P1-03 passing is the moment the project's dominant risk (G-01, G-02) is retired. Everything after this is ordinary engineering. Record the outcome in the ADR before proceeding.

#### Track B — Database and migrations

**P1-04 · MariaDB setup and least-privilege accounts**
Deliverable: database `merchandising`; `merch_api` account with DML + limited DDL, `merch_backup` account with `SELECT, LOCK TABLES, SHOW VIEW, EVENT, TRIGGER` only; root not used by any application; `bind-address` set to loopback; charset `utf8mb4`, engine InnoDB.
Acceptance: API account can read/write but cannot `DROP DATABASE`; backup account cannot write; root login from a client machine fails.
Evidence: `p1-04-grants.txt`, `p1-04-bind-config.txt`.

**P1-05 · Connection layer in `Infrastructure` using MySqlConnector**
Deliverable: connection factory, options binding, config read from an ACL-protected host location outside the binaries; pinned MySqlConnector version.
Acceptance: API connects; connection string appears in **no** committed file and **no** client project (guardrail G-C).
Evidence: `p1-05-connection-test.log`; ADR entry pinning connector + MariaDB versions (**closes G-04**).

**P1-06 · Migration runner in `Merchandising.Maintenance`**
Deliverable: VB console utility that discovers `db/migrations/NNNN_*.sql`, computes a checksum per file, applies unapplied ones in order inside a transaction, and records id/checksum/timestamp/result in `SchemaMigrations`; refuses to run if a previously applied file's checksum has changed.
Acceptance: run twice — second run applies nothing; tamper with an applied file — runner refuses and explains.
Evidence: `p1-06-migration-run.log`, `p1-06-tamper-refusal.log`.

**P1-07 · Migration `0001` — POC schema slice**
Deliverable: `Users`, `Roles`, `UserRoles`, `Products`, `StockBalances`, `StockMovements`, `AuditLogs`, `IdempotencyKeys`, `SystemSettings`, `SchemaMigrations`. Money `DECIMAL(19,4)`, quantity `DECIMAL(19,3)`, all timestamps UTC (`DATETIME(6)`), `StockMovements` and `AuditLogs` append-only by grant.
Acceptance: migration applies clean; a manual `UPDATE` against `AuditLogs` as `merch_api` is **rejected by privilege** (**closes G-20**); decimal precision verified by inserting and reading back `0.001` and `12345678901234.5678`.
Evidence: `p1-07-schema.sql`, `p1-07-audit-immutability.txt`, `p1-07-precision-check.txt`.

#### Track C — Security seam

**P1-08 · Authentication and one protected endpoint**
Deliverable: password hashing via a framework-provided hasher (salted, never plaintext); `POST /api/v1/auth/login` issuing a short-lived bearer token; `GET /api/v1/auth/me`; one protected endpoint gated by a policy; failed-login counter with lockout after five attempts in the configured window.
Acceptance: valid login returns token; protected endpoint returns 401 without token, 403 with wrong role, 200 with right role; six bad passwords trigger lockout; **no password value appears in any log**.
Evidence: `p1-08-auth-matrix.txt` (one row per case), `p1-08-log-scan.txt`.

**P1-09 · HTTPS with a LAN-valid certificate**
Deliverable: Kestrel bound to `https://0.0.0.0:8443`; certificate with subject/SAN covering `MERCH-HOST` (and the reserved IP if the client config will use it); documented trust-installation procedure for client machines; development HTTP profile isolated and visibly labelled non-production.
Acceptance: client reaches `https://MERCH-HOST:8443/health` with no certificate warning after following the trust procedure; an **untrusted** client shows the correct failure and the WPF client surfaces a clear message rather than silently proceeding.
Evidence: `p1-09-cert-details.txt`, `p1-09-client-trust-steps.md`, `p1-09-invalid-cert-behaviour.png` (**closes G-07, G-25**).

**P1-10 · Negative security tests**
Deliverable: from a client laptop — port scan / direct connection attempt to MariaDB's port; unauthenticated call to a protected endpoint; call with a valid token but insufficient role.
Acceptance: DB port unreachable from client; unauthenticated → 401; wrong-role → 403; error bodies contain a correlation ID and **no** stack trace, SQL, or connection detail.
Evidence: `p1-10-port-scan.txt`, `p1-10-denials.txt` (**closes G-03 partially, G-10**).

#### Track D — Transactional core (the second-most-important thing in this phase)

**P1-11 · Atomic stock decrement**
Deliverable: a single API command that, in **one** database transaction: conditionally decrements the balance, inserts a `StockMovements` row (delta, before, after, reason, actor, correlation ID), and inserts an `AuditLogs` row. The conditional update is the mechanism, not an afterthought:

```sql
UPDATE StockBalances
   SET Quantity = Quantity - @qty,
       RowVersion = RowVersion + 1,
       UpdatedAtUtc = UTC_TIMESTAMP(6)
 WHERE ProductId = @productId
   AND Quantity >= @qty;
-- affected rows must be exactly 1, else roll back and return a controlled 409
```

Acceptance: successful decrement produces exactly one movement and one audit row and a correct balance; a request exceeding available stock returns a controlled insufficient-stock response and creates **no** rows.
Evidence: `p1-11-happy-path.txt`, `p1-11-insufficient-stock.txt`.

**P1-12 · Forced-failure rollback proof**
Deliverable: a test-only fault injection point that throws after the movement insert but before commit.
Acceptance: balance, movement, and audit rows are **all** absent afterwards. Not "mostly absent" — a partial commit here is a phase failure.
Evidence: `p1-12-rollback.txt` with before/after row counts (**closes G-12**).

**P1-13 · Concurrency proof**
Deliverable: integration test firing two (then ten) simultaneous decrement requests against a product with exactly one unit of stock.
Acceptance: exactly one succeeds; the rest receive a controlled conflict/insufficient response; final balance is `0`, never negative; movement count is exactly 1.
Evidence: `p1-13-concurrency.txt` with the raw result distribution (**closes G-11**).

**P1-14 · Idempotency proof**
Deliverable: `IdempotencyKeys` table with a unique constraint on `(Scope, KeyValue)`; insert-first strategy; committed response payload stored and replayed on repeat.
Acceptance: the same key sent five times produces **one** movement and five identical responses; a different key produces a second movement.
Evidence: `p1-14-idempotency.txt` (**closes G-13**).

#### Track E — Client seam

**P1-15 · WPF client spike over HTTPS**
Deliverable: `ClientCommon` API client (typed HTTP client, token storage in memory only, connection-state detection, correlation-ID propagation); one WPF window in `Merchandising.Inventory` with: login, call protected endpoint, display result, trigger the stock decrement, and a visible connection-status indicator.
Acceptance: full round trip over HTTPS from a **client laptop**, not the dev machine; stopping the API produces a clear connection-unavailable state and the client refuses to queue or fake the write.
Evidence: `p1-15-client-roundtrip.png`, `p1-15-api-down-state.png` (**closes G-26**).

#### Track F — Hosting and recovery

**P1-16 · Windows Service hosting**
Deliverable: service registration, restricted service identity, filesystem permissions, Windows Event Log writing, service recovery actions configured (restart on failure).
Acceptance: service starts; **host reboots and the API is serving without human action**; a forced process kill triggers automatic restart; events appear in Event Viewer.
Evidence: `p1-16-service-config.txt`, `p1-16-post-reboot-health.txt`, `p1-16-recovery.png` (**closes G-09**).

**P1-17 · Backup via the maintenance utility**
Deliverable: `Merchandising.Maintenance backup` using **`C:\xampp\mysql\bin\mysqldump.exe`** (P0-04 confirmed this distribution ships no `mariadb-dump.exe`; the earlier "`mariadb-dump` preferred" wording named a binary that does not exist here — see ADR-003.1) under the `merch_backup` account; writes to a protected directory outside the binaries and not served by the API; records file size, checksum, timestamp, source DB version, result; copies to a separate physical drive; registered in Windows Task Scheduler for the daily window; failure produces a recorded error and an operational warning.
Acceptance: scheduled run produces a valid dump plus an off-host copy plus a `BackupLogs` row; a deliberately broken run (wrong credentials) records failure and warns rather than failing silently.
Evidence: `p1-17-backup-success.log`, `p1-17-backup-failure.log`, checksum listing (**closes G-15**).

**P1-18 · Restore rehearsal with maintenance mode**
Deliverable: the full seven-step spec §15 procedure — maintenance lock via API, client warning, operator confirmation, offline restore by the maintenance utility, schema validation, health + data verification, lock release. Restore must **not** be executable as an ordinary live API request.
Acceptance: restore completes from the off-host copy; expected users/products/balances present afterwards; **measured RTO recorded**; ordinary writes are rejected while the maintenance lock is held.
Evidence: `p1-18-restore-log.txt`, `p1-18-verification-checklist.md`, measured RTO (**closes G-14, seeds G-08**).

#### Track G — Test harness and closure

**P1-19 · Wire both VB test projects**
Deliverable: `Tests.Unit` and `Tests.Integration` in VB, framework chosen from MSTest / NUnit / xUnit (all three have official Microsoft VB guidance); `InternalsVisibleTo` configured; `scripts/run-tests.ps1`; integration tests run against the **real** pinned MariaDB, never an in-memory substitute.
Acceptance: `run-tests.ps1` executes both suites green; the P1-12/13/14 proofs run as automated tests, not manual steps.
Evidence: `p1-19-test-run.log`; ADR entry recording the framework.

**P1-20 · Phase 1 closure pack**
Deliverable: complete `docs/adr.md` (every pinned version and decision), complete `docs/environment-manifest.md`, an evidence index mapping each of spec §24's twelve steps and seven exit criteria to its artifact.
Acceptance: every §24 row has a linked artifact. No row says "verified informally."
Evidence: `evidence/phase-1/INDEX.md`.

### 6.2 The Phase 1 gate

**Pass** = all twenty tasks complete with evidence, and every spec §24 exit criterion has an artifact.

**Partial fail** = descended to rung B but everything else passes → proceed, with the ADR updated and the professor informed.

**Fail** = rung A and rung B both fail. **Stop.** Do not start Phase 2. Take the recorded errors to your professor and request a documented exception (rung C). The evidence you captured is what makes that conversation go well rather than badly.

---

## 7. Phases 2–7 — feature construction

Once the gate passes, work becomes predictable. Each phase follows the same internal rhythm (§8.1) and produces both code and the spec §20 document that corresponds to it.

### Phase 2 — Identity and master data
**Entry:** Phase 1 gate passed.
**Build:** full `Users`/`Roles`/`UserRoles`/`PermissionPolicies` model; the complete role matrix from spec §9 as ASP.NET Core authorization policies; audit logging as a cross-cutting concern applied to every sensitive operation; `SystemSettings`; product master (`Products`, `Categories`, `Brands`, `Units`, `ProductBarcodes`, `PriceHistory`); suppliers; active/inactive lifecycle; seed data.
**Key design call:** implement audit as a single server-side pipeline component so no future endpoint can forget it. Auditing bolted on per-endpoint is auditing that will have gaps by Phase 5.
**Exit:** every cell of the role-permission matrix has a passing test (including the *negative* cells); product SKU/barcode uniqueness enforced at DB **and** API; price change writes `PriceHistory` + audit atomically; deactivation preserves historical references; seed data loads on a clean database.
**Docs produced:** `role-permission-matrix.md`, `database-design.md` (first complete draft).
**Closes:** G-19, G-21.

### Phase 3 — Procurement primitives
**Entry:** Phase 2 exit. *Procurement precedes receiving deliberately — this is the spec's G-23 reordering; do not merge these phases.*
**Build:** supplier management; purchase orders and lines; the seven-state status machine (`Draft → Submitted → Approved → PartiallyReceived → FullyReceived → Cancelled → Closed`) enforced **server-side**; approval policy with self-approval prohibition; purchase history; cancellation/closure rules. The Procurement WPF client reaches usable state here.
**Key design call:** encode the status machine as an explicit transition table in `Domain` with a single `CanTransition` function, tested exhaustively over all state × action pairs. Scattered `If status = ...` checks across controllers is how invalid transitions leak in.
**Exit:** every legal transition passes and every illegal one is rejected with a stable error code; a user cannot approve their own restricted order; a cancelled order cannot proceed; approvals are attributable and audited.
**Docs produced:** `api-specification.md` (procurement section).

### Phase 4 — Inventory and receiving
**Entry:** Phase 3 exit. This phase inherits the Phase 1 transaction pattern wholesale — do not invent a second one.
**Build:** stock balances and the append-only movement ledger; goods receiving (full and partial) creating receipt + lines + stock-in movements + balance + audit in one transaction; over-receiving rejected by default; purchase returns bounded by received-minus-prior-returns; stock counts with variance and threshold-based approval; adjustments; low-stock logic; reconciliation views. Inventory WPF client reaches usable state.
**Key design call:** a reconciliation query that proves `SUM(movements) = balance` for every product, run as an automated test after every integration suite. Ledger drift found in Phase 7 is a nightmare; found automatically in Phase 4 it is a small bug.
**Exit:** receiving produces exactly one atomic stock increase; partial receiving accumulates correctly across multiple receipts; over-receiving rejected; ledger reconciles for all products; concurrent receive-and-adjust on the same product is safe; corrections use compensating movements, never edits.
**Docs produced:** `database-design.md` finalised.

### Phase 5 — POS
**Entry:** Phase 4 exit.
**Build:** cashier sessions; fast product lookup by SKU/barcode/name; cart; the atomic sale (sale + lines + payments + stock-out + balance + session totals + audit, one transaction); cash change with fixed-precision decimal arithmetic; card/e-wallet **recording** with unmistakable UI and report labelling; returns bounded by sold-minus-prior-returns with stock-eligibility flag; receipt data display; sales history; daily closing with declared-vs-calculated variance.
**Key design call:** sale lines capture the *effective* unit price and cost at sale time. A later price change must not retroactively alter historical sales analysis. This is easy to get right now and very hard to fix later.
**Exit:** end-to-end sale and return flows pass; negative stock impossible under concurrent load; idempotent retry returns the original result rather than a second sale; completed sales immutable; change calculation exact to the stored precision; payment-method wording verified as non-authorising in both UI and reports.
**Docs produced:** `ui-specification.md` (all three clients — the shared visual system is settled once POS forces the hardest layout decisions).
**Closes:** G-24.

### Phase 6 — Reporting and operations
**Entry:** Phase 5 exit.
**Build:** all twelve reports from spec §14 with explicit date-boundary and returns-treatment definitions; CSV export (UTF-8, header row, invariant column order, correct escaping, parameters in filename/metadata); export permissions mirroring report permissions; the backup/restore work from Phase 1 promoted to production quality with retention and off-host rotation; health monitoring; release packaging; installation guide; user guide.
**Key design call:** every report ships with a *reconciliation test* asserting the report total equals the sum from the corresponding detail screen for the same filter. A report that disagrees with the detail screen during the demo is the single most damaging kind of defect in a merchandising system.
**Exit:** every report reconciles to source data; CSV round-trips through Excel without mangling; backup/restore meets the documented RPO/RTO with measured evidence; a clean installation on a fresh machine succeeds from the guide alone.
**Closes:** G-22, G-15, G-16.

### Phase 7 — Hardening and acceptance
**Entry:** Phase 6 exit.
**Build:** security review against the full spec §17 control table; load test at the 5–10 session profile recording p50/p95/error rates for lookup, reports, receiving, and concurrent sales; recovery tests; UI refinement pass across all three clients for consistency, keyboard navigation, focus states, 1366×768 and 125% scaling; POS mouse-free workflow verification; deployment rehearsal including rollback; training; UAT with a real person in each of the five roles; defect correction; sign-off.
**Key design call:** run the load test *early* in Phase 7, not last. If the host laptop cannot sustain the profile you need room to tune indexes and connection pooling.
**Exit:** all critical defects closed; full acceptance suite green; measured performance and RPO/RTO recorded against the spec's targets; professor/store sign-off obtained; every document in spec §20 complete.
**Docs produced:** `test-plan.md` with results, release package manifest, operations-ownership table.
**Closes:** G-06, G-08, G-17, G-27, G-28.

---

## 8. The working loop — how sessions actually run

This is the part that makes the plan executable rather than decorative.

### 8.1 Phase rhythm

```
Phase entry
  ├─ Re-read plan.md for this phase
  ├─ Regenerate tasks.md: expand the phase into task cards
  ├─ Write/extend the migration(s) for the phase's schema
  ├─ ── per task ──────────────────────────────────────────
  │    1. Agent reads CLAUDE.md + spec section + task card
  │    2. Agent restates scope and acceptance in its own words   ← catches misreads before code
  │    3. Server-side rules: write the failing test first
  │    4. Implement (API → Domain/Infrastructure → client last)
  │    5. Run check-no-csharp.ps1 + run-tests.ps1
  │    6. Capture evidence artifact if the task requires one
  │    7. Update tasks.md; commit with the task ID in the message
  ├─ ── end per task ──────────────────────────────────────
  ├─ Phase exit review: every exit criterion → evidence artifact
  └─ ADR entries for anything decided along the way
```

**One task = one commit**, message prefixed with the task ID (`P4-07: partial receiving accumulates across receipts`). When something breaks three weeks later, `git bisect` and a clean task-ID history is the difference between a ten-minute fix and an evening lost.

### 8.2 Task card format for `tasks.md`

```markdown
### P4-07 · Partial receiving accumulates correctly
Spec: §10.1, §11 (Goods received)
Depends on: P4-03, P4-05
Files: src/Merchandising.Api/Controllers/ReceivingController.vb,
       src/Merchandising.Infrastructure/Repositories/ReceiptRepository.vb,
       src/tests/Merchandising.Tests.Integration/ReceivingTests.vb
Do: Receiving a partial quantity moves the PO to PartiallyReceived; a second
    receipt against the same line accumulates; total received may not exceed
    ordered quantity.
Done when:
  - [ ] Two sequential partial receipts sum to the ordered quantity → FullyReceived
  - [ ] Third receipt on a fully-received line is rejected with a stable error code
  - [ ] Each receipt writes its own movement rows; ledger reconciles
  - [ ] Integration test green against pinned MariaDB
  - [ ] check-no-csharp.ps1 passes
Evidence: evidence/phase-4/p4-07-partial-receiving.txt
```

The `Done when` checklist is what makes an agent's work verifiable instead of merely plausible.

### 8.3 Stop conditions — when the agent must halt and ask

Put these in `CLAUDE.md`. An agent that improvises past these is how a project quietly drifts off-spec:

- It is about to write C#, or add a package that requires C# source generation.
- It needs a package or version not recorded in `docs/adr.md`.
- It would put a connection string, credential, or `Infrastructure` reference in a client project.
- A business rule in the spec is ambiguous or two spec sections conflict.
- A test fails in a way that suggests the *design* is wrong rather than the code.
- It is about to modify an already-applied migration file (always write a new one).
- It would delete or update a row in `StockMovements` or `AuditLogs`.
- The task requires a decision the spec assigns to the Foundation POC or the professor.

### 8.4 Working effectively with the agent on VB

- **Paste the working `.vbproj` into context** at the start of any API session. It anchors the agent to VB syntax more reliably than an instruction to "use VB."
- **Ask for the test first** on anything transactional. VB transaction code reviewed against a test you already understand is much safer than VB transaction code reviewed on its own.
- **Review every `Try/Catch`.** Silently swallowed exceptions around transaction boundaries are the highest-consequence bug class in this system.
- **Keep sessions to one task.** Context bloat is the main cause of an agent forgetting the language rule halfway through.
- **Prefer explicit, verbose VB over clever VB.** You will read this code under exam pressure.

---

## 9. Definition of done

**Task:** acceptance checks pass · tests green · guardrails pass · evidence captured if required · `tasks.md` updated · committed with task ID.

**Phase:** every task done · every spec exit criterion has an evidence artifact · phase documents written · ADR updated · a clean-clone build from scratch succeeds.

**Project:** spec §25's acceptance statement satisfied in full, on the intended laptops, on the private LAN, with the academic-prototype limitations visibly stated in the documentation and the presentation.

---

## 10. Risk register mapping

Every gap from spec §23 is assigned to the phase that closes it. Nothing is left to "later."

| Phase | Closes |
|---|---|
| Phase 0 | G-30 (environment baseline), G-29 (academic classification stated up front — reinforced now that XAMPP is confirmed mandatory) |
| Phase 1 | G-01, G-02, G-04, G-05, G-07, G-09, G-10, G-11, G-12, G-13, G-14, G-15, G-20, G-25, G-26; partially G-03 |
| Phase 2 | G-19, G-21 |
| Phase 3 | G-23 (phase ordering itself) |
| Phase 4 | G-12 extended to receiving/adjustments |
| Phase 5 | G-24 |
| Phase 6 | G-22, G-16, G-15 (promoted to production quality) |
| Phase 7 | G-06, G-08, G-17, G-27, G-28 |
| Continuous | G-03 (XAMPP hardening re-verified at each deployment rehearsal) |

**G-18** (reference numbering) is already corrected in the current spec revision.

---

## 11. Open decisions to resolve in Phase 1

These are deliberately unresolved now and must be pinned in `docs/adr.md` before Phase 2 begins. Resolving them earlier means guessing; leaving them later means rework.

| # | Decision | Resolve by | Default lean |
|---|---|---|---|
| D-1 | Web SDK rung A vs B | P1-02 | A. Rung C is effectively closed — the professor has already ruled, so reversing it is a much harder ask than the original question would have been. |
| D-2 | Exact MariaDB + MySqlConnector versions | P1-05 | Whatever XAMPP ships; pin exactly |
| D-3 | Token scheme: JWT bearer vs opaque server-side session | P1-08 | JWT, host-held signing key, expiry matched to a work shift |
| D-4 | Test framework: MSTest / NUnit / xUnit | P1-19 | Any — all have official VB guidance; pick and stop debating |
| D-5 | API deployment: framework-dependent vs self-contained | P1-03 | Self-contained if host runtime installation proves fragile |
| D-6 | Certificate: self-signed with manual trust vs internal CA | P1-09 | Self-signed + documented trust procedure, given classroom scale |
| D-7 | Transaction isolation level | P1-11 | `READ COMMITTED` + conditional update (do not rely on isolation alone) |
| D-8 | Money/quantity precision confirmation with professor | P1-07 | `DECIMAL(19,4)` / `DECIMAL(19,3)` as specified |

---

## 12. What I would do first, concretely

P0-06 is answered: manual VB API acceptable, XAMPP mandatory. Both constraints bind, so the plan proceeds at full weight. The next three actions in order:

1. **P0-01 → P0-05 — establish the environment baseline.** Install the VS 2026 workloads, strip XAMPP down to MariaDB only, and record exact versions for the host and every client. Cheap, unglamorous, and it prevents the "works on my machine" failure that surfaces during UAT when it is expensive.
2. **P0-07/P0-08 — scaffold the repo**, write `CLAUDE.md` and the guardrail script. Roughly one session. Everything afterwards is safer because of it, and the no-C# guardrail matters more now that C# is definitively off the table.
3. **P1-02 — hand-author the VB Web SDK API and hit `/health`.** This is the real test and it is now unavoidable. Until it passes, treat every other plan as provisional.

Do not start Phase 2 until the Phase 1 evidence index is complete. That single piece of discipline is what this whole plan is for.

---

## Sources consulted for the technical risk assessment

- [Visual Basic support planned for .NET 5.0 — Microsoft Visual Basic Blog](https://devblogs.microsoft.com/vbteam/visual-basic-support-planned-for-net-5-0/)
- [Add ASP.NET Core Visual Basic project template — dotnet/aspnetcore #34788](https://github.com/dotnet/aspnetcore/issues/34788)
- [How to create an ASP.NET Core Minimal API with VB.NET (there's no template)](https://swimburger.net/blog/dotnet/create-an-aspdotnet-core-minimal-api-with-vbdotnet)
- [ASP.NET Core Web SDK — Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/razor-pages/web-sdk?view=aspnetcore-9.0)
- [.NET Connector — MariaDB Documentation](https://mariadb.com/docs/connectors/mariadb-connector-net)
- [MySqlConnector for ADO.NET — MariaDB Documentation](https://mariadb.com/docs/connectors/mariadb-connector-net/mariadb-connector-net-guide)
- [Get started with Visual Basic and MSTest — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-visual-basic-with-mstest)
- [Unit testing Visual Basic in .NET Core with dotnet test and NUnit — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-visual-basic-with-nunit)
- [What's new in ASP.NET Core in .NET 10 — Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0?view=aspnetcore-10.0)
