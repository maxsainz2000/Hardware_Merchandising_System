# tasks.md — Phase 3 (Procurement primitives)

**Scope:** current phase only. Regenerated at each phase entry from `plan.md`.
**Rules:** one task = one commit, prefixed with the task ID. Never tick a `Done when` box on a failing test or a partial implementation. Stop conditions are in `CLAUDE.md` §7.

**Legend:** ⬜ not started · 🟡 in progress · ✅ done · 🔴 blocked

---

> ## Phase 2 closed 2026-08-25 — PASS, at commit `9c68d92`
>
> Twelve cards plus a closure pack. Reproduced at the gate rather than read from a log: a fresh clone outside the tree, with **0** `bin`/`obj` directories at clone time, restores eleven projects from nothing, builds at **0 warnings / 0 errors**, and passes guardrails G-A–G-D plus **24/24 unit and 136/136 integration tests, 0 skipped**, against the real pinned MariaDB 10.4.32. Full record: `evidence/phase-2/INDEX.md`.
>
> **One test was changed to get there, and it was not a weakening.** `Backup_Succeeds_WritesDumpWithChecksumThatVerifies` demanded `BackupOutcome.Succeeded` and died on that assertion before reaching a single one of the checksum assertions it exists for — so a *checksum* test failed on any machine without a USB volume labelled `MERCHBACKUP` attached, including all three classmates'. A coverage audit of all four `BackupCommandTests` found that **no test has ever asserted the off-host copy when the volume is present**; that claim rests on `evidence/phase-1/p1-17-backup-success.log`. Accepting `Succeeded` **or** `Partial` therefore cost zero assertion coverage and removed a hardware dependency. **ADR-019** records it and the assertion Phase 6 now owes.
>
> **Three claims are narrower than their wording, and Phase 3 inherits two of them:**
>
> 1. **Only 4 of the policy registry's 29 operations have a live endpoint.** Every cell for those four is asserted, positive and negative. The other 25 are carried by *mechanism*, not by a note: P2-03's matrix suite is data-driven from `PolicyRegistry.Definitions`, so **an endpoint added without a policy fails the suite**. Every Phase 3 card that adds an endpoint extends that suite — that is what the box on each card below is for.
> 2. **G-21 stays open to Phase 4** by `plan.md`'s own assignment of the finalised `database-design.md`. Not a shortfall.
> 3. **G-19 surfaced a real hole with no owner: account recovery.** Carried below.
>
> **Frozen: no phase below may re-litigate a Phase 1 or Phase 2 decision.** The connector, transaction pattern (ADR-006), auth scheme, error envelope (ADR-014), grant model (ADR-013), policy naming and the self-approval mechanism (ADR-017), and the barcode rule (ADR-018) are settled. Phase 3 consumes them.

---

# Carried forward — open items from earlier phases

These are carried, not reopened. Each says which gate now owns it.

---

### 🟡 P0-02 · Windows baseline for every demo workstation — **owed at the Phase 6 gate (ADR-016)**

Three demo workstations unsurveyed. `scripts/setup-client.ps1 -CaptureOnly` collects every field itself. **The one part worth doing today, out of band:** ask each classmate whether they hold **local administrator rights on their own laptop**. One message, no machine needed — it is the only carried item that fails late and unfixably.

### 🟡 P0-05 · Demo network rehearsal — **owed at the Phase 6 gate (ADR-016)**

Two of its boxes need nobody but you and can still invalidate ADR-015: whether Mobile Hotspot starts **from cold with nothing to share**, and whether this adapter sustains station + Wi-Fi Direct GO concurrently under load. ADR-015 was accepted on a capability *reading*, not a cold start.

### 🟡 P0-07 · Repository structure — **the last box closes in Phase 7**

Five `docs/*.md` remain: `api-specification.md` → **Phase 3, P3-08 below**; `database-design.md` finalised → Phase 4; `ui-specification.md` → Phase 5; `backup-restore-guide.md` + `user-guide.md` → Phase 6; `test-plan.md` → Phase 7.

### ⬜ CARRY-01 · Account recovery has no implementation and no card — **unassigned, decision owed**

**Surfaced at the Phase 2 gate** (`evidence/phase-2/INDEX.md` §4.3), not by any card. Spec §23's G-19 remediation names *account recovery* among seven components; six are proven. Lockout self-recovers (`LockedUntilUtc` = `UtcNow` + 15 min), but **a forgotten password has no route**: ADR-017 §4 puts all user management on the `Merchandising.Maintenance` CLI, and that CLI has `create-user` with no `reset-password` and no `unlock-user`. An operator who forgets the Super Admin password has no supported recovery today.

> **Not a Phase 3 blocker, and deliberately not silently assigned.** The natural home is **Phase 6** (operations, beside backup/restore) as a `reset-password` subcommand on the CLI that already creates accounts — roughly one card. Confirm that placement, or place it earlier, before Phase 6 planning closes.

### ⬜ CARRY-02 · Off-host backup copy, volume present — **owed at the Phase 6 gate (ADR-019)**

No test asserts `OffHostPath` when a `MERCHBACKUP` volume *is* attached. Artifact-backed only. Phase 6 promotes backup to production quality (`plan.md` §7, closing G-15/G-16) and owes the assertion then.

---

# Phase 3 — Procurement primitives

**Entry:** Phase 2 gate passed. **Closes:** G-23.
**Docs produced:** `docs/api-specification.md` (procurement section).

*Procurement precedes receiving deliberately — this is the spec's G-23 reordering. Do not merge these phases.*

**What already exists, so no card below rebuilds it.** `Suppliers` with unique name, lifecycle and full audit (P2-10, migration `0007`) — `plan.md` §7 lists supplier management under Phase 3, but Phase 2 built it early and Phase 3 consumes it. The product master (`0006`). The audit pipeline (P2-04). All 29 policies including `PurchaseOrders.Create/Submit/Approve/Track` and the `SelfApprovalRequirement`/`SelfApprovalHandler` pair (ADR-017 §6), which has unit tests against a fake resource but **no live endpoint yet**. The error envelope, idempotency, and the ADR-006 transaction pattern.

> ### Four things that will bite in this phase specifically
>
> **1. Receiving is Phase 4, but two of the seven states belong to it.** `PartiallyReceived` and `FullyReceived` are reachable only through a receiving command that does not exist yet. The transition *table* must model all seven states now — that is P3-01's whole point — while the *endpoints* that drive those two transitions arrive next phase. A card that builds receiving here has drifted out of scope; a table that omits the two states has to be rewritten in Phase 4.
>
> **2. Self-approval is already built and must be consumed, not reinvented.** ADR-017 §6 is explicit: Phase 3 implements `IOwnershipResource` on `PurchaseOrder` and calls `IAuthorizationService.AuthorizeAsync(User, resource, policyName)`. It does **not** add its own check. `SelfApprovalHandler` calls `context.Fail()` — an absolute veto matching spec §9's "cannot" — so a second mechanism would not merely be redundant, it could contradict it.
>
> **3. Next free numbers: migration `0008`, grants `0010`.** Install order is always migration → grants; MariaDB 10.4 rejects a table-level `GRANT` naming a table that does not exist (`ERROR 1146`). Table names in grant files are **lowercase**. A new table is `SELECT`-only until the grants file adds writes back per table (ADR-013).
>
> **4. Every endpoint added here extends the P2-03 matrix suite.** The suite is data-driven from `PolicyRegistry.Definitions` and will fail on an endpoint with no policy. That is the mechanism carrying 25 untested cells forward, and it only works if each card actually adds its rows.

---

## Track A — The status machine, in Domain, before any endpoint exists

### ✅ P3-01 · The seven-state transition table and a single `CanTransition`

**Spec:** §10.1 · **Closes:** G-23 (begins) · **Decides:** ADR-020
**Files:** `src/Merchandising.Domain/Procurement/`, `src/tests/Merchandising.Tests.Unit/PurchaseOrderTransitionTests.vb`

**Do:** `plan.md` §7 names this the key design call of the phase: *"encode the status machine as an explicit transition table in `Domain` with a single `CanTransition` function, tested exhaustively over all state × action pairs. Scattered `If status = ...` checks across controllers is how invalid transitions leak in."* Pure Domain — no database, no ASP.NET Core, no I/O — so it runs in the unit suite and depends on nothing (`CLAUDE.md` §4).

**Done when:**

- [x] All seven spec §10.1 states modelled: `Draft`, `Submitted`, `Approved`, `PartiallyReceived`, `FullyReceived`, `Cancelled`, `Closed` — including the two Phase 4 drives
- [x] One `CanTransition(from, action)` function is the only place a transition is decided; no `Select Case` on status anywhere else in the solution — ⚠ enforced by a **source-scan lint**, which catches the idiomatic `Select Case … Status` form and *not* an `If order.Status = …` chain or a decision made in SQL. Stated in the test's own summary and in ADR-020; not rounded up to a proof
- [x] The test enumerates **every** state × action pair — computed from the enums, not hand-listed, so a new state cannot be added without the suite growing
- [x] Each illegal pair names a stable error code (ADR-014), not a boolean false — four codes on `PurchaseOrderTransitionErrors`, and an unregistered code fails the suite
- [x] `A cancelled order cannot proceed` and `a fully received order rejects further receiving` are rows in the table, not comments — both asserted over *every* action, not spot-checked
- [x] ADR-020 records the table, the states Phase 4 drives, and why a table beat scattered checks
- [x] Unit suite green; `check-no-csharp.ps1` passes — 37/37 unit (24 → 37), 136/136 integration, G-A–G-D pass, build 0 warnings

**Evidence:** `evidence/phase-3/p3-01-transition-matrix.txt` — one row per state × action pair, legal and illegal ✅ *rendered from the table by `PurchaseOrderTransitionMatrixFormatter` and asserted equal to the committed file, so it cannot drift (the P2-02 shape)*

> **Two rows the spec does not decide, confirmed with the user rather than assumed.** A `PartiallyReceived` order cannot be **cancelled** — a receipt has already written append-only `StockMovements`, so the route for abandoning the remainder is `Close` (short-close). An `Approved` order with nothing received cannot be **closed** — it is cancelled, which keeps `Closed` = *goods came in* and `Cancelled` = *they never did* distinct in spec §14's history report. Both are recorded in ADR-020 with their reasoning; **P3-05 implements them and must not re-decide them.**

> **Write this card first and alone.** Every later card in the phase consumes it, and it is the one piece that can be fully proven with no database, no HTTP, and no fixture.

---

## Track B — Schema

### ⬜ P3-02 · Migration 0008 — `PurchaseOrders`, `PurchaseOrderLines`, and grants 0010

**Spec:** §10.1, §12 · **Files:** `db/migrations/0008_purchase-orders.sql`, `db/grants/0010_purchase-order-grants.sql`

**Do:** The order header (supplier, order number, status, requested-by, approved-by, timestamps UTC) and its lines (product, ordered quantity, purchase cost, received-to-date). Money `DECIMAL(19,4)`, quantities `DECIMAL(19,3)`, timestamps `DATETIME(6)` UTC, `COLLATE utf8mb4_unicode_ci` stated explicitly on every table.

**Done when:**

- [ ] Both tables created; foreign keys to `Suppliers` and `Products` **prevent** deletion of a referenced row, proven with `ERROR 1451` rather than asserted by inspection
- [ ] `RequestedByUserId` and `ApprovedByUserId` are separate columns — the self-approval rule compares them and cannot if they are one field
- [ ] Order number is unique, enforced at the database, proven by a concurrent double-insert that bypasses any API check (the P2-07 shape)
- [ ] Status stored as a stable identifier the Domain enum maps to — not a display string, not an ordinal that renumbers when a state is added
- [ ] Applies clean as `merch_migrator` on a database already carrying `0001`–`0007`; runner applies it exactly once
- [ ] `db/grants/0010` applied **after**, lowercase table names; no `UPDATE`/`DELETE` granted beyond what a card justifies in writing
- [ ] Round-trip test: `0.001` and `12345678901234.5678` exact through the new decimal columns
- [ ] Integration suite green against pinned MariaDB

**Evidence:** `evidence/phase-3/p3-02-schema.txt`, `evidence/phase-3/p3-02-grants.txt`

> **A purchase order is not a ledger.** Unlike `StockMovements`, `AuditLogs` and `PriceHistory`, a `Draft` order is legitimately editable, so this table does need `UPDATE`. Grant it deliberately and say so in the grants file — the append-only default (ADR-013) is the reason that sentence has to be written rather than assumed.

---

## Track C — The purchase-order lifecycle over HTTP

*Sequential. One worker, in order — each card consumes the last.*

### ⬜ P3-03 · Create and read purchase orders and lines

**Spec:** §10.1, §13 · **Files:** `src/Merchandising.Api/Controllers/PurchaseOrdersController.vb`, `src/Merchandising.Infrastructure/Data/PurchaseOrderRepository.vb`, `src/Merchandising.Contracts/Procurement/`

**Do:** `POST /api/v1/purchase-orders` creating a `Draft` with lines, and the reads that go with it. Spec §13 requires list endpoints to define pagination, maximum page size, sorting, filtering and date-boundary behaviour — define them here rather than in Phase 6 when reports need them.

**Done when:**

- [ ] A created order starts in `Draft` — the state is assigned server-side and a client-supplied status is rejected, not honoured
- [ ] Lines reference active products; an inactive product is refused with a stable error code (the P2-09 lifecycle rule)
- [ ] Money and quantity scale validated at the API boundary **before** binding (ADR-004.1) — a stored value that looks right proves nothing
- [ ] Idempotency key honoured per ADR-007: a repeated key returns the original committed order, never a second one
- [ ] Pagination, max page size, sort and filter defined and asserted, per spec §13
- [ ] Creation audited through the P2-04 pipeline
- [ ] **Matrix suite extended** for every route added — positive and negative cells, 403 not 401/404
- [ ] Integration suite green

**Evidence:** `evidence/phase-3/p3-03-purchase-orders.txt`

---

### ⬜ P3-04 · Submit and approve, with self-approval prohibited 🎯

**Spec:** §9, §10.1 · **Closes:** G-23 · **Files:** `src/Merchandising.Api/Controllers/PurchaseOrdersController.vb`, `src/Merchandising.Domain/Procurement/`, `src/tests/Merchandising.Tests.Integration/PurchaseOrderApprovalTests.vb`

**Do:** `POST /{id}/submit` and `POST /{id}/approve`, both routed through P3-01's `CanTransition`. **This is the card the phase exists for.** Spec §9: a Procurement Officer *"cannot approve their own purchase order."*

**Done when:**

- [ ] `PurchaseOrder` implements `IOwnershipResource`; approval calls `IAuthorizationService.AuthorizeAsync(User, order, PurchaseOrders.Approve)` — **ADR-017 §6's mechanism, not a new one**
- [ ] A user approving **their own** order is refused **403**, proven end-to-end over HTTP with two real users, not only by the existing handler unit test
- [ ] A different authorized user approving the same order succeeds
- [ ] Approval is **attributable**: `ApprovedByUserId` and an approval timestamp UTC are persisted, and audited through P2-04 with actor, target and correlation ID
- [ ] Every illegal transition into `Submitted`/`Approved` is rejected with the stable error code P3-01 assigned — asserted, not assumed
- [ ] Status change and audit row commit **together or not at all**, proven by a forced-failure test in the P1-12 / P2-08 shape
- [ ] **Matrix suite extended** for both routes
- [ ] Integration suite green

**Evidence:** `evidence/phase-3/p3-04-approval.txt`, `evidence/phase-3/p3-04-self-approval-denied.txt`

> **Stop condition.** If the ADR-017 self-approval mechanism turns out not to fit a real resource — for instance if `AuthorizeAsync` cannot see the order at the point the decision is needed — that is `CLAUDE.md` §7 item 5: the design is wrong, not the code. Halt and report rather than writing a controller `If` beside it.

---

### ⬜ P3-05 · Cancellation and closure rules

**Spec:** §10.1 · **Files:** `src/Merchandising.Api/Controllers/PurchaseOrdersController.vb`, `src/Merchandising.Domain/Procurement/`

**Do:** `POST /{id}/cancel` and `POST /{id}/close`, both through `CanTransition`. Spec §10.1: *"A cancelled order cannot receive goods."* Phase 4 will rely on that being true before it writes a single receiving endpoint.

**Done when:**

- [ ] A cancelled order refuses every subsequent action with a stable error code — enumerated over all actions, not spot-checked
- [ ] Closure rules match P3-01's table exactly; the controller adds no rule of its own
- [ ] A cancelled or closed order is never physically deleted (spec §12) and remains fully readable in history
- [ ] Both transitions audited with actor, reason and correlation ID
- [ ] **Matrix suite extended** for both routes
- [ ] Integration suite green

**Evidence:** `evidence/phase-3/p3-05-cancellation.txt`

---

### ⬜ P3-06 · Purchase history and order tracking

**Spec:** §10.1, §14 · **Files:** `src/Merchandising.Api/Controllers/PurchaseOrdersController.vb`, `src/Merchandising.Infrastructure/Data/PurchaseOrderRepository.vb`

**Do:** The `PurchaseOrders.Track` surface: order history by supplier, status and date range, with ordered quantity/value and outstanding quantity. Spec §14's *Purchase-order history* report reads from this in Phase 6 — shape it so that report reconciles rather than re-queries.

**Done when:**

- [ ] History filters by supplier, status and date range, with date boundaries defined in the **store time zone** and stated in the response (spec §14)
- [ ] Ordered quantity/value and outstanding quantity computed server-side, never by the client
- [ ] A cancelled order appears in history, labelled, rather than vanishing
- [ ] Pagination and max page size consistent with P3-03
- [ ] **Matrix suite extended** — `PurchaseOrders.Track` positive and negative cells
- [ ] Integration suite green

**Evidence:** `evidence/phase-3/p3-06-purchase-history.txt`

---

## Track D — The Procurement client

### ⬜ P3-07 · Procurement WPF client reaches usable state

**Spec:** §5, §10.1 · **Files:** `src/Merchandising.Procurement/`, `src/Merchandising.ClientCommon/`

**Do:** `plan.md` §7: *"The Procurement WPF client reaches usable state here."* Supplier list, order creation with lines, submit, approve, cancel, and history — every one of them calling the API, never the database.

**Done when:**

- [ ] Login, supplier browse, order create/submit/approve/cancel and history all work against the running API
- [ ] **Guardrail G-B holds:** no reference to `Infrastructure`, MySqlConnector, or any database package. No connection string anywhere in the project
- [ ] Server-side refusals (403 self-approval, illegal transition) surface as the API's message and error code — the client never invents its own wording or hides the correlation ID
- [ ] Client-side validation is for usability only; every rule is re-checked server-side
- [ ] Keyboard navigation and focus order work at 1366×768 and 125% scaling (the Phase 7 UI pass refines this; it does not start it)
- [ ] Guardrails and both suites green

**Evidence:** `evidence/phase-3/p3-07-procurement-client.txt`

> **Client last, deliberately** (`plan.md` §8.1). Every rule this client touches is already proven server-side by Track C, so a defect found here is a display defect, not a business-logic one.

---

## Track E — Documents and closure

### ⬜ P3-08 · `docs/api-specification.md` — procurement section

**Spec:** §13, §20 · **Files:** `docs/api-specification.md`

**Do:** The first section of the document `plan.md` §7 assigns to this phase. Every procurement endpoint: route, method, authorization policy, request/response shape, error codes, idempotency requirement, and pagination behaviour. Later phases append their own sections.

**Done when:**

- [ ] Every route added in Track C documented with its **policy name**, not a role string
- [ ] Every stable error code listed with the condition that raises it
- [ ] The idempotency requirement stated per write command (spec §13)
- [ ] Pagination, max page size, sorting, filtering and date-boundary behaviour stated for every list endpoint
- [ ] Verified against the running registration rather than transcribed by hand, in the P2-12 shape — a drift check, not a promise
- [ ] States the academic-prototype framing required by `plan.md` §5
- [ ] One of P0-07's five remaining documents struck from its list

**Evidence:** the document

---

### ⬜ P3-09 · Phase 3 closure pack

**Spec:** §20 · **Files:** `evidence/phase-3/`

**Do:** Produce the two artifacts a phase gate always asks for and which no earlier phase had a card for — which is precisely why the Phase 2 gate first returned FAIL. Run a clean clone outside the tree, capture the build and both suites, and write the evidence index mapping every exit criterion and card to a file that exists.

**Done when:**

- [ ] Clean clone outside the repository, **0** `bin`/`obj` at clone time, builds at 0 warnings and passes guardrails plus both suites → `p3-09-clean-clone.log`
- [ ] `evidence/phase-3/INDEX.md` maps every Phase 3 exit criterion and every card to an artifact, continuing `phase-2/INDEX.md`'s register
- [ ] **G-23 recorded closed** in that register
- [ ] Any claim narrower than its wording is marked ⚠ and explained, never rounded up
- [ ] Every ADR this phase owed (ADR-020) is ACCEPTED, not PENDING
- [ ] ADR-020 appended **before** the *Template for new entries* section — not inside its fence, which is how ADR-015–018 ended up rendering as a code block until P2-13 repaired it

**Evidence:** `evidence/phase-3/p3-09-clean-clone.log`, `evidence/phase-3/INDEX.md`

---

## Phase 3 exit gate

From `plan.md` §7. Every criterion needs an artifact under `evidence/phase-3/` — a file someone else could read.

- [ ] Every legal transition passes and every illegal one is rejected with a stable error code (P3-01, P3-04, P3-05)
- [ ] A user cannot approve their own restricted order, proven end-to-end over HTTP (P3-04)
- [ ] A cancelled order cannot proceed (P3-05)
- [ ] Approvals are attributable and audited (P3-04)
- [ ] `docs/api-specification.md` procurement section written (P3-08)
- [ ] G-23 closed in the gap register (P3-09)
- [ ] ADR-020 ACCEPTED
- [ ] Clean-clone build and both test suites green (P3-09)

**Carried, not owed here:** P0-02 and P0-05 belong to the **Phase 6** gate (ADR-016). P0-07's structure box belongs to **Phase 7**. CARRY-02 belongs to **Phase 6** (ADR-019). **CARRY-01 has no owner yet and needs a decision** — it is not a Phase 3 blocker, but do not let Phase 6 planning close without placing it.
