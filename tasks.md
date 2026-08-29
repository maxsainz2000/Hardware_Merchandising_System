# tasks.md — Phase 4 (Inventory and receiving)

**Scope:** current phase only. Regenerated at each phase entry from `plan.md`.
**Rules:** one task = one commit, prefixed with the task ID. Never tick a `Done when` box on a failing test or a partial implementation. Stop conditions are in `CLAUDE.md` §7.

**Legend:** ⬜ not started · 🟡 in progress · ✅ done · 🔴 blocked

---

> ## Phase 3 closed 2026-08-28 — PASS, at commit `4e0bc09`
>
> Nine cards. A clean clone outside the tree, with **0** `bin`/`obj` directories at clone time, restores eleven projects from nothing, builds at **0 warnings / 0 errors**, and passes guardrails G-A–G-D plus **44/44 unit and 211/211 integration tests, 0 skipped**, against the real pinned MariaDB 10.4.32. **G-23 closed.** Full record: `evidence/phase-3/INDEX.md`.
>
> **The gate returned FAIL on its first sitting, and that is the most useful thing Phase 3 produced.** P3-07's last box — *keyboard navigation and focus order at 1366×768 @ 125%* — was carried to Phase 7 on this sentence in its own evidence file:
>
> > *"the window's default size (1024x680, MinWidth 960, MinHeight 620) fits inside a 1366x768 desktop at 125% scaling (effective ~1093x614 minus taskbar) with room to spare"*
>
> **680 and 620 are both larger than the 614 the same sentence computes.** The arithmetic was right and nobody made the comparison. Behind it sat a real defect: a `MinHeight` above the work area cannot be resized into it, so the status bar carrying every server message and correlation ID would have been permanently off-screen on a 1366×768 demo laptop — the exact hardware three classmates must demonstrate on (ADR-012). Correcting the window size then exposed a second defect the first had masked: a fixed `260` DIP row that left the Lines grid at **0.0 DIP**. Both are now asserted by `ProcurementLayoutTests`, each proven falsifiable against the pre-fix XAML before being trusted, and the traversal itself was performed at 1093.0 × 576.0 DIP rather than deferred.
>
> **The lesson Phase 4 inherits, stated as a rule: a sentence in an evidence file is not a check.** Every quantitative claim a card makes about something a machine could measure must be measured by something that fails when it stops being true. P4-12 carries the same 1366×768 box for the Inventory client — it is the card most likely to repeat this mistake.
>
> **Frozen: no phase below may re-litigate a Phase 1, 2 or 3 decision.** The connector, transaction pattern (ADR-006), auth scheme, error envelope (ADR-014), grant model (ADR-013), policy naming and self-approval (ADR-017), the barcode rule (ADR-018), and the purchase-order status machine (ADR-020) are settled. Phase 4 consumes them.

---

# Carried forward — open items from earlier phases

These are carried, not reopened. Each says which gate now owns it.

---

### 🟡 P0-02 · Windows baseline for every demo workstation — **owed at the Phase 6 gate (ADR-016)**

Three demo workstations unsurveyed. `scripts/setup-client.ps1 -CaptureOnly` collects every field itself. **The one part worth doing today, out of band:** ask each classmate whether they hold **local administrator rights on their own laptop**. One message, no machine needed — it is the only carried item that fails late and unfixably.

### 🟡 P0-05 · Demo network rehearsal — **owed at the Phase 6 gate (ADR-016)**

Two of its boxes need nobody but you and can still invalidate ADR-015: whether Mobile Hotspot starts **from cold with nothing to share**, and whether this adapter sustains station + Wi-Fi Direct GO concurrently under load. ADR-015 was accepted on a capability *reading*, not a cold start.

### 🟡 P0-07 · Repository structure — **the last box closes in Phase 7**

Three `docs/*.md` remain: ~~`api-specification.md` → P3-08~~ **written**; ~~`database-design.md` finalised → P4-13~~ **written**; `ui-specification.md` → Phase 5; `backup-restore-guide.md` + `user-guide.md` → Phase 6; `test-plan.md` → Phase 7.

### ⬜ CARRY-01 · Account recovery has no implementation and no card — **unassigned, decision owed**

**Surfaced at the Phase 2 gate** (`evidence/phase-2/INDEX.md` §4.3). Spec §23's G-19 remediation names *account recovery* among seven components; six are proven. Lockout self-recovers (`LockedUntilUtc` = `UtcNow` + 15 min), but **a forgotten password has no route**: ADR-017 §4 puts all user management on the `Merchandising.Maintenance` CLI, and that CLI has `create-user` with no `reset-password` and no `unlock-user`.

> **Not a Phase 4 blocker, and deliberately not silently assigned.** The natural home is **Phase 6** (operations, beside backup/restore) as a `reset-password` subcommand — roughly one card. Confirm that placement, or place it earlier, **before Phase 6 planning closes**. This is its second phase carried without an owner; do not let it reach a third.

### ⬜ CARRY-02 · Off-host backup copy, volume present — **owed at the Phase 6 gate (ADR-019)**

No test asserts `OffHostPath` when a `MERCHBACKUP` volume *is* attached. Artifact-backed only. Phase 6 promotes backup to production quality (`plan.md` §7, closing G-15/G-16) and owes the assertion then.

### ⬜ CARRY-04 · Intermittent `MSB3030` on a clean-clone build — **watch, no owner yet**

**Observed once at the Phase 3 gate**, then not reproduced in eight further clean-clone builds across two commits (`p3-09-clean-clone.log` §5). `Merchandising.Tests.Unit` references two `OutputType=WinExe` projects, and in a parallel build the referenced executable's `runtimeconfig.json` can be listed as a copy-local item before the task that generates it has run. Both references predate Phase 3, so it is pre-existing fragility, not a regression.

> **Not a card, because nine runs cannot support a rate and the fix belongs to whoever owns build infrastructure.** It matters only because it threatens a classmate's first build from a clean clone (ADR-012), and it is retried by building again. **Raise a card if it recurs** — a second sighting changes it from noise into a pattern.

---

# Phase 4 — Inventory and receiving

**Entry:** Phase 3 gate passed. **Closes:** G-12 (extended to receiving/adjustments), G-21.
**Docs produced:** `docs/database-design.md` finalised.

*This phase inherits the Phase 1 transaction pattern wholesale (`plan.md` §7). Do not invent a second one.*

**What already exists, so no card below rebuilds it.** `StockBalances` and the append-only `StockMovements` ledger (migration `0001`), with the conditional-update decrement proven atomic and concurrency-safe at P1-11/P1-13 — `StockRepository`, `StockMovementWriter`, `StockService` and `InventoryController.DecrementStock` are all live. The product master (`0006`), suppliers (`0007`), purchase orders and `CanTransition` (`0008`, ADR-020). The audit pipeline (P2-04), the error envelope (ADR-014), idempotency (ADR-007), and **every policy this phase needs is already registered**: `Receiving.Prepare`, `Receiving.Confirm`, `PurchaseReturns.Manage`, `StockCounts.Perform`, `Adjustments.Request`, `Adjustments.Approve`, `LowStock.Review`, `Stock.Read`, `Stock.ReviewMovements`.

> ### Five things that will bite in this phase specifically
>
> **1. The reconciliation query is the phase's key design call, and it must exist before anything new writes to the ledger.** `plan.md` §7: *"a reconciliation query that proves `SUM(movements) = balance` for every product, run as an automated test after every integration suite. Ledger drift found in Phase 7 is a nightmare; found automatically in Phase 4 it is a small bug."* That is P4-01, and it is written first and alone for the same reason P3-01 was.
>
> **2. Phase 3 left `PartiallyReceived` and `FullyReceived` reachable by no command. This phase writes them.** The transition table already models them (ADR-020) and **must not be re-decided** — P4-04/P4-05 call `CanTransition` and add no rule of their own. P3-01's closing note is binding: *consume the table, do not re-decide it.*
>
> **3. `CK_PurchaseOrderLines_ReceivedQuantity` already enforces the ordered-quantity bound at the server** (P3-02). Over-receiving is therefore refused by the **database** before any API check runs. P4-06's job is to turn that into a controlled 409 with a stable error code, not to re-implement the bound — and spec §10.1's future over-receiving override would need a **new numbered migration** to relax it.
>
> **4. Next free numbers: migrations `0009`, `0010`; grants `0011`, `0012`.** Install order is always migration → grants; MariaDB 10.4 rejects a table-level `GRANT` naming a table that does not exist (`ERROR 1146`). Table names in grant files are **lowercase**. A new table is `SELECT`-only until its grants file adds writes back per table (ADR-013), and `StockMovements`/`AuditLogs` stay absent from every grants file forever.
>
> **5. Receiving is the first command that reads a line before it writes one — which is exactly what CARRY-03 predicted would bite.** P4-11 fixes the isolation level, and it is sequenced **before** Track C for that reason, not after it.

---

## Track A — The ledger invariant, before anything new writes to the ledger

### ✅ P4-01 · `SUM(StockMovements) = StockBalances` for every product, as a standing assertion 🎯

**Spec:** §11, §12 · **Closes:** G-12 (begins) · **Decides:** ADR-021
**Files:** `src/Merchandising.Infrastructure/Data/LedgerReconciliation.vb`, `src/tests/Merchandising.Tests.Integration/LedgerReconciliationTests.vb`

**Do:** `plan.md` §7 names this the key design call of the phase. One query that, for **every** product, sums the movement ledger and compares it to the stored balance, reporting every product that disagrees rather than the first. Wire it so it runs as an assertion after the integration suite, not as a report someone remembers to read.

**Done when:**

- [x] The query covers **every** product, including ones with no movements and ones with no balance row — a product missing from either side is a discrepancy, not a skip
- [x] It reports **all** disagreeing products with expected, actual and delta, not just a boolean or the first failure
- [x] It runs automatically after the integration suite and fails the run on any drift — proven by **deliberately introducing drift** (a movement row with no matching balance change, inserted as `merch_migrator`) and confirming the failure names that product, then removing it. A reconciliation test never proven to fail is decoration. It also proved itself against real data on its first run: it found genuine pre-existing drift in four Phase 1/2 fixture products, caused by test setup helpers resetting `StockBalances` directly with no matching movement — fixed at the fixtures and healed with a one-time compensating correction, never an edit or delete (`evidence/phase-4/p4-01-ledger-reconciliation.txt`, `p4-01-drift-correction.sql`)
- [x] Decimal comparison is exact at the stored scale (`DECIMAL(19,3)`), never a floating-point tolerance
- [x] It is not fooled by an in-flight transaction — the check reads committed state only
- [x] ADR-021 records the query, where it runs, and why an automated assertion beat a report
- [x] Both suites green; guardrails pass; build 0 warnings

**Evidence:** `evidence/phase-4/p4-01-ledger-reconciliation.txt` — including the induced-drift run that proves it fails

> **Write this card first and alone.** Every later card in the phase writes to the ledger, and this is the one thing that tells you whether any of them got it wrong. Building it after the writers is how drift gets baked in and then blessed.

---

## Track B — Schema

*Sequential. Migration then grants, and `0009` before `0010`.*

### ✅ P4-02 · Migration 0009 — `Receipts`, `ReceiptLines`, `PurchaseReturns`, `PurchaseReturnLines`, and grants 0011

**Spec:** §11, §12 · **Files:** `db/migrations/0009_receiving.sql`, `db/grants/0011_receiving-grants.sql`

**Do:** The receipt header (purchase order, received-by, received-at UTC, reference), its lines (purchase-order line, quantity received, cost), and the same shape for purchase returns. Money `DECIMAL(19,4)`, quantities `DECIMAL(19,3)`, timestamps `DATETIME(6)` UTC, `COLLATE utf8mb4_unicode_ci` stated explicitly on every table.

**Done when:**

- [x] All four tables created; foreign keys to `PurchaseOrders`, `PurchaseOrderLines`, `Products` and `Users` **prevent** deletion of a referenced row, proven with `ERROR 1451` rather than asserted by inspection, attempted as `merch_migrator` so the refusal is the constraint and not the grant
- [x] Receipt reference is unique where the spec requires it, proven by a concurrent double-insert that bypasses any API check (the P2-07 / P3-02 shape) — `success | ERROR 1062`
- [x] Applies clean as `merch_migrator` on a database already carrying `0001`–`0008`; runner applies it exactly once
- [x] `db/grants/0011` applied **after**, lowercase table names, no `UPDATE`/`DELETE` beyond what the file argues for in its own header — and **nothing at all** added for `stockmovements` or `auditlogs` (ADR-013)
- [x] Round-trip test: `0.001` and `12345678901234.5678` exact through the new decimal columns, **plus** the declared column types read back from `information_schema` — CLAUDE.md §6.3 means a correctly stored value proves the value and never the column
- [x] Integration suite green against pinned MariaDB

**Evidence:** `evidence/phase-4/p4-02-receiving-schema.txt`, `evidence/phase-4/p4-02-receiving-grants.txt`

### ✅ P4-03 · Migration 0010 — `StockCounts`, `StockCountLines`, `StockAdjustments`, and grants 0012

**Spec:** §10.2, §12 · **Files:** `db/migrations/0010_counts-and-adjustments.sql`, `db/grants/0012_counts-and-adjustments-grants.sql`

**Do:** The count header (status, counted-by, approved-by, timestamps UTC), its lines (product, counted quantity, system quantity at count time, variance), and the adjustment table with its reason, requester, approver and threshold outcome.

**Done when:**

- [x] Variance is **stored as counted-minus-system at the moment of counting**, not recomputed later from a balance that has since moved — the whole point of a count is what was true then
- [x] `RequestedByUserId` and `ApprovedByUserId` are separate columns with separate FKs, approver nullable — the threshold rule compares them and cannot if they are one field. Asserted from `information_schema`, not from reading the migration
- [x] Status stored as a stable identifier the Domain enum maps to — not a display string, not a renumbering ordinal. Every name walked from `[Enum].GetNames`, an ordinal refused. ⚠ **`COLLATE utf8mb4_bin` on that column**, or the `CHECK` accepts `'draft'` under the table's case-insensitive collation and stores it verbatim — the exact defect P3-02 hit and found only because a test looked
- [x] Applies clean as `merch_migrator` on a database carrying `0001`–`0009`; applied exactly once
- [x] `db/grants/0012` applied after, lowercase, justified per table in its own header
- [x] Integration suite green

**Evidence:** `evidence/phase-4/p4-03-counts-schema.txt`, `evidence/phase-4/p4-03-counts-grants.txt`

---

## Track C — The isolation level CARRY-03 predicted would bite

*Runs before Track D. Receiving reads a line before it writes one, which is the case CARRY-03 named.*

### ✅ P4-04 · CARRY-03 — `READ COMMITTED` at every call site, asserted inside a transaction

**Spec:** ADR-006 · **Decides:** ADR-006 amendment
**Files:** `src/Merchandising.Infrastructure/Data/`, `src/Merchandising.Api/Controllers/`, `src/tests/Merchandising.Tests.Integration/ConnectionFactoryTests.vb`

**Do:** Close the divergence measured at P3-03. `ConnectionFactory` issues `SET SESSION tx_isolation = 'READ-COMMITTED'` and `ConnectionFactoryTests` asserts it — but MySqlConnector's `BeginTransaction` sends its own `SET TRANSACTION ISOLATION LEVEL`, so **inside** a transaction the level is `REPEATABLE-READ` everywhere except `PurchaseOrderService`. ADR-006's text and the running system disagree.

**Done when:**

- [x] `IsolationLevel.ReadCommitted` passed explicitly at every `BeginTransaction` call site: `StockService`, `PriceChangeService`, `ProductLifecycleService`, `SupplierLifecycleService`, `SuppliersController`, `ProductsController`, `MaintenanceController` — enumerated from a source scan, so a call site added later is not silently missed ⚠ the card's own list was stale: `MaintenanceController` opens no transaction (nothing to fix); the source scan additionally found and fixed `SystemSettingsController` and three more `PurchaseOrderService` sites the list omitted. See `evidence/phase-4/p4-04-isolation-level.txt` §0
- [x] `ConnectionFactoryTests` gains the assertion it lacks: `@@tx_isolation` read **inside** a transaction, not only on the session. The old session-level assertion stays — both are true and only one was checked
- [x] The existing P1-11/P1-13 concurrency proofs still pass unchanged, demonstrating this is a text-vs-reality fix and not a behaviour change
- [x] ADR-006 amended to state the mechanism explicitly — that a session-level `SET` does **not** survive `BeginTransaction`, and why passing the level per transaction is the only form that holds
- [x] Both suites green

**Evidence:** `evidence/phase-4/p4-04-isolation-level.txt` — `@@tx_isolation` measured inside a transaction at every call site, before and after

> **This is a Phase 1/2 mechanism being corrected, so it is the one card in this phase that touches evidenced work from earlier phases.** If any existing concurrency test changes behaviour rather than merely passing, that is `CLAUDE.md` §7 item 5 — halt and report, do not adjust the test.

---

## Track D — Receiving over HTTP

*Sequential. One worker, in order — each card consumes the last.*

### ✅ P4-05 · Receive goods: receipt + lines + movements + balance + audit, in one transaction 🎯

**Spec:** §10.1, §11 · **Closes:** G-12 · **Files:** `src/Merchandising.Api/Controllers/ReceivingController.vb`, `src/Merchandising.Infrastructure/Data/ReceiptRepository.vb`, `src/Merchandising.Contracts/Receiving/`

**Do:** `POST /api/v1/receipts` against an `Approved` purchase order. **This is the card the phase exists for.** One transaction commits the receipt, its lines, one `StockMovements` row per line, the conditional balance update, and the audit row — or none of them.

**Done when:**

- [x] Exactly **one** atomic stock increase per line, using the ADR-006 conditional update with the affected-row count verified before returning success — never a read-then-write ⚠ an increase has no insufficiency case to verify a row count against; `StockRepository.IncrementAsync` is the equivalent one-statement atomic upsert (`INSERT ... ON DUPLICATE KEY UPDATE`), needed because a product can reach receiving with no prior `StockBalances` row at all
- [x] Status moves through `CanTransition` only (ADR-020); the controller adds no status rule of its own
- [x] Receiving against a `Cancelled`, `Draft` or `Submitted` order is refused with the stable error code P3-01 assigned — asserted over every non-receivable state, not spot-checked (`ReceiveAsync_EveryNonReceivableStatus_RefusedWithStableCode`: Draft, Submitted, Cancelled, Closed, FullyReceived)
- [x] Idempotency key honoured per ADR-007: a repeated key returns the original committed receipt, never a second stock increase
- [x] All five effects commit together or not at all, proven by a **forced-failure test** in the P1-12 / P2-08 shape
- [x] The P4-01 reconciliation passes after the receipt, and is asserted **in this card's own test**, not only by the suite-wide fixture
- [x] **Matrix suite extended** for every route added — positive and negative cells, 403 not 401/404
- [x] Integration suite green (267/267, including the AssemblyCleanup ledger reconciliation)

**Evidence:** `evidence/phase-4/p4-05-receiving-atomic.txt`

### ✅ P4-06 · Partial receiving accumulates across receipts

**Spec:** §10.1, §11 · **Files:** `src/Merchandising.Api/Controllers/ReceivingController.vb`, `src/Merchandising.Infrastructure/Data/ReceiptRepository.vb`

**Do:** A partial quantity moves the order to `PartiallyReceived`; a second receipt against the same line accumulates; reaching the ordered quantity moves it to `FullyReceived`. Both states are the ones Phase 3 modelled and could not drive.

**Done when:**

- [x] Two sequential partial receipts sum to the ordered quantity → `FullyReceived`, asserted on the stored `ReceivedQuantity`, not inferred from the status
- [x] Three or more receipts accumulate correctly, including a final one that exactly closes the line
- [x] Each receipt writes its **own** movement rows; the ledger reconciles after every one
- [x] Mixed lines behave independently — one line fully received and another partially leaves the order `PartiallyReceived`
- [x] **Matrix suite extended**; integration suite green ⚠ no new route exists for this card (same `POST /api/v1/receipts` P4-05's `ReceivingConfirm_MatrixMatchesPolicyRegistry` already covers); extended instead with an HTTP round-trip test proving accumulation over the live authorized path, not only the direct-service tests

**Evidence:** `evidence/phase-4/p4-06-partial-receiving.txt`

### ✅ P4-07 · Over-receiving rejected by default

**Spec:** §10.1, §11 · **Files:** `src/Merchandising.Api/Controllers/ReceivingController.vb`, `src/Merchandising.Infrastructure/Data/ReceiptRepository.vb`

**Do:** Total received may not exceed ordered. `CK_PurchaseOrderLines_ReceivedQuantity` already enforces this at the server (P3-02) — this card turns the constraint violation into a controlled response, and proves the constraint is what actually stops it.

**Done when:**

- [x] A receipt exceeding the ordered quantity is refused with a stable error code and a 409, never a 500 and never a leaked SQL message (ADR-014)
- [x] A third receipt on a fully-received line is refused with the same code — proven on a still-`PartiallyReceived` order (a second line deliberately left open), the case a `CanTransition` status refusal cannot catch on its own
- [x] **The database is proven to be the thing refusing it**: `PurchaseOrderRepository.IncrementReceivedQuantityAsync` — the production write method — called directly, bypassing the new API guard entirely, still fails as `ERROR 4025` from the `CHECK`
- [x] The refusal leaves **no** partial trace — no receipt row, no movement, no balance change; asserted by re-reading all three
- [x] **Matrix suite extended**; integration suite green ⚠ no new route exists for this card (same `POST /api/v1/receipts` route P4-05's matrix test already covers)

**Evidence:** `evidence/phase-4/p4-07-over-receiving.txt`

### ✅ P4-08 · Purchase returns bounded by received-minus-prior-returns

**Spec:** §10.1, §11 · **Files:** `src/Merchandising.Api/Controllers/ReceivingController.vb`, `src/Merchandising.Infrastructure/Data/PurchaseReturnRepository.vb`

**Do:** A return to a supplier is bounded by what was received less what has already been returned, and writes a stock-out movement in the same transactional shape as receiving.

**Done when:**

- [x] The bound is computed **server-side** from committed rows, never from a client-supplied figure — `RecordPurchaseReturnLineRequest` carries no `ProductId`/`Cost` field at all; both come from the locked `ReceiptLines` row
- [x] Returning more than received-minus-prior-returns is refused with a stable error code, including when two prior partial returns together exhaust the balance
- [x] Each return writes its own `StockMovements` row; corrections are **compensating movements, never edits** (CLAUDE.md §5) — asserted by confirming no `UPDATE`/`DELETE` reaches the ledger (fresh `ERROR 1142` proof, not only relying on the Phase-1 evidence)
- [x] Concurrent returns against the same receipt cannot oversell the bound — proven under real concurrent load in the P1-13 shape (8 simultaneous requests, exactly 1 succeeds), not by two sequential calls
- [x] The P4-01 reconciliation passes after every return
- [x] **Matrix suite extended**; integration suite green — a genuinely new route this time (`PurchaseReturnsManage_MatrixMatchesPolicyRegistry`), unlike P4-06/P4-07

**Design decision made and recorded:** a purchase return is a single atomic command (`RequestedByUserId = ApprovedByUserId`, `Status` always committed as `Approved`), not a two-actor request/approve workflow — `PurchaseReturns.Manage` is one policy, not a split pair, and ADR-017 §6 names no self-approval veto for returns. `Requested`/`Rejected` stay reachable in the schema, unused by this phase. Full reasoning in the evidence file.

**Evidence:** `evidence/phase-4/p4-08-purchase-returns.txt`

---

## Track E — Counts, adjustments, and low stock

*Sequential. Files overlap with each other but not with Track D.*

### ✅ P4-09 · Stock counts with variance

**Spec:** §10.2, §11 · **Files:** `src/Merchandising.Api/Controllers/StockCountsController.vb`, `src/Merchandising.Infrastructure/Data/StockCountRepository.vb`, `src/Merchandising.Contracts/Inventory/`

**Do:** Open a count, record counted quantities per product, compute variance against the system quantity **at the moment of counting**, and close it. The count itself changes no stock — that is the adjustment's job (P4-10).

**Done when:**

- [x] Variance is computed and stored server-side at count time; a later balance change does not retroactively alter a recorded variance
- [x] A count in progress does not block sales or receiving on the same product
- [x] Counted quantity scale validated at the API boundary **before** binding (ADR-004.1) — a stored value that looks right proves nothing
- [x] A closed count is immutable and remains fully readable
- [x] **Matrix suite extended** — `StockCounts.Perform` positive and negative cells; integration suite green

**Evidence:** `evidence/phase-4/p4-09-stock-counts.txt`

### ✅ P4-10 · Adjustments with threshold-based approval

**Spec:** §10.2, §11 · **Files:** `src/Merchandising.Api/Controllers/AdjustmentsController.vb`, `src/Merchandising.Infrastructure/Data/AdjustmentRepository.vb`, `src/Merchandising.Domain/Inventory/`

**Do:** An adjustment applies a variance to stock. Below the configured threshold it applies directly; at or above it requires a second person's approval. The threshold lives in `SystemSettings`, not in a constant.

**Done when:**

- [x] The threshold is read from `SystemSettings` and a changed setting changes the outcome, asserted rather than assumed
- [x] An adjustment at or above the threshold **cannot** be approved by its own requester — reusing ADR-017 §6's `IOwnershipResource` / `AuthorizeAsync` mechanism, **not a new check** (the P3-04 precedent is binding here)
- [x] Applying an adjustment writes movement + balance + audit in one transaction, proven by a forced-failure test
- [x] A rejected or pending adjustment changes no stock — asserted on the balance and the ledger
- [x] The P4-01 reconciliation passes after every applied adjustment
- [x] **Matrix suite extended** — `Adjustments.Request` and `Adjustments.Approve`; integration suite green

**Evidence:** `evidence/phase-4/p4-10-adjustments.txt`

### ✅ P4-11 · Low-stock logic and reconciliation views

**Spec:** §10.2, §14 · **Files:** `src/Merchandising.Api/Controllers/InventoryController.vb`, `src/Merchandising.Infrastructure/Data/StockRepository.vb`

**Do:** The `LowStock.Review` and `Stock.ReviewMovements` surfaces: products at or below reorder level, and the movement history that explains a balance. Spec §14's reports read from these in Phase 6 — shape them so those reports reconcile rather than re-query.

**Done when:**

- [x] Low-stock threshold is per product and read from the product master, never a global constant
- [x] The movement history for a product **sums to its current balance** — the same invariant P4-01 asserts, now exposed through the API so the Phase 6 report and the detail screen cannot disagree
- [x] Pagination, max page size, sorting and filtering consistent with P3-03's definitions
- [x] Date boundaries defined in the **store time zone** and stated in the response (spec §14)
- [x] **Matrix suite extended** — `LowStock.Review`, `Stock.Read`, `Stock.ReviewMovements`; integration suite green

**Evidence:** `evidence/phase-4/p4-11-low-stock.txt`

---

## Track F — The Inventory client

### 🟡 P4-12 · Inventory WPF client reaches usable state

**Spec:** §10.2, §16 · **Files:** `src/Merchandising.Inventory/`, `src/Merchandising.ClientCommon/`

**Do:** `plan.md` §7: *"Inventory WPF client reaches usable state."* Stock browse, receiving against an approved order, counts, adjustments and low-stock review — every one calling the API, never the database. P1-15's spike window is replaced, not extended.

**Done when:**

- [ ] Login, stock browse, receive, count, adjust and low-stock review all work against the running API — **owed: an authenticated manual pass.** Everything the client itself does is built and unit-proven (`evidence/phase-4/p4-12-inventory-client.txt` §0/§4): the exe launches and stays responsive against the live, running `MerchandisingApi` service, but this session could not complete a real sign-in — the seeded accounts' passwords live only in the ACL-protected `installation-credentials.txt` (P2-11/ADR-012), and reading that file was refused by this session's own permission classifier and accepted as a stop condition (CLAUDE.md §7) rather than worked around
- [x] **Guardrail G-B holds:** no reference to `Infrastructure`, MySqlConnector, or any database package. No connection string anywhere in the project
- [x] Server-side refusals (over-receiving 409, adjustment threshold 403, illegal transition) surface as the API's message and error code — the client never invents its own wording or hides the correlation ID
- [x] Client-side validation is for usability only; every rule is re-checked server-side
- [x] **Keyboard navigation and focus order work at 1366×768 and 125% scaling — asserted by a test, not by a sentence.** Extended `ProcurementLayoutTests`' three assertions to this window (`InventoryLayoutTests.vb`): declared sizes against the 1092.8 × 576.0 DIP work area, every screen laid out at the window's own minimum with no grid starved, and unique TabIndex ascending in reading order per screen. All three passed on the first run
- [x] Guardrails and both suites green — 47/47 unit, 339/339 integration, 0 guardrail failures

**A known, frozen permission gap, recorded rather than silently patched around:** `PurchaseOrders.Track` (needed to look up an order before receiving against it) is `ProcurementAndAbove`; `Receiving.Confirm` is `InventoryAndAbove`. Only `admin`/`superadmin` sit in both groups, so a pure `inventoryclerk` sign-in can confirm a receipt but not look one up first. Phase 2's policy matrix is frozen — this card does not change it. Detail in the evidence file §2.

**Evidence:** `evidence/phase-4/p4-12-inventory-client.txt`

> **Client last, deliberately** (`plan.md` §8.1). Every rule this client touches is already proven server-side by Tracks D and E, so a defect found here is a display defect, not a business-logic one.

---

## Track G — Documents and closure

### ✅ P4-13 · `docs/database-design.md` finalised

**Spec:** §12, §20 · **Closes:** G-21 · **Files:** `docs/database-design.md`

**Do:** Finalise the document Phase 2 drafted, now that the schema is complete through migration `0010`. Every table, column, type, constraint, index and foreign key, with the grant posture per table and the reason for each `UPDATE`/`DELETE` that exists.

**Done when:**

- [x] Every table through `0010` documented with its exact declared types, read from `information_schema` rather than transcribed from the migrations
- [x] The append-only guarantee stated per table, naming `StockMovements` and `AuditLogs` as the two that hold **no** write-back grant, with ADR-013's ordering argument
- [x] Every foreign key and its delete behaviour listed
- [x] Verified against the running database rather than transcribed by hand, in the P2-12 / P3-08 shape — a drift check that fails when the schema moves (`DatabaseDesignDocumentationTests.vb`, 7 tests, watched fail on the undocumented `Receipts` table before the doc was written)
- [x] States the academic-prototype framing required by `plan.md` §5
- [x] One of P0-07's remaining documents struck from its list
- [x] **G-21 recorded closed** in the gap register (stated in the document's own header; `evidence/phase-4/INDEX.md` records it formally at P4-14)

**Evidence:** `evidence/phase-4/p4-13-database-design.txt`

### ✅ P4-14 · Phase 4 closure pack

**Spec:** §20 · **Files:** `evidence/phase-4/`

**Do:** Run a clean clone outside the tree, capture the build and both suites, and write the evidence index mapping every exit criterion and card to a file that exists.

**Done when:**

- [x] Clean clone outside the repository, **0** `bin`/`obj` at clone time, builds at 0 warnings and passes guardrails plus both suites → `p4-14-clean-clone.log` ⚠ first capture at `98f0908` built at **9** warnings (MSTEST0037); fixed at `4135d52` (mechanical `Assert.HasCount`/`Assert.IsEmpty` rewrite, behaviour unchanged) and recaptured — see log §5
- [x] `evidence/phase-4/INDEX.md` maps every Phase 4 exit criterion and every card to an artifact, continuing `phase-3/INDEX.md`'s register
- [x] **G-12 and G-21 recorded closed** in that register — G-12 closed **for the components this phase owns** (receiving, returns, counts, adjustments); its *sale* component stays Phase 5's, stated explicitly rather than rounded up (INDEX.md §3)
- [x] Any claim narrower than its wording is marked ⚠ and explained, never rounded up — **and every quantitative claim is checked against the number it cites**, which is precisely what the Phase 3 gate caught. Found here: exit criterion 5 ("concurrent receive-and-adjust... safe") cites `p4-05`/`p4-10`, neither of which fires that scenario — marked ⚠, not ✅ (INDEX.md §4.1); and **P4-12 stays 🟡**, not rounded to done, because its own card still has one open box (INDEX.md §4.2)
- [x] Every ADR this phase owed (ADR-021, and the ADR-006 amendment from P4-04) is ACCEPTED, not PENDING — checked directly against `docs/adr.md`, not assumed
- [x] New ADRs appended **before** the *Template for new entries* section, not inside its fence — verified by line number (`## ADR-021` at 1014, `## Template for new entries` at 1049), the check P2-13 had to add after ADR-015–018 rendered as a code block

**Evidence:** `evidence/phase-4/p4-14-clean-clone.log`, `evidence/phase-4/INDEX.md`

> **This card's own boxes are all satisfied — that is not the same claim as "Phase 4 may exit."** `INDEX.md` §4.1 and §4.2 record two real gaps a `/phase-gate` review still needs to see: exit criterion 5's evidence doesn't fire the scenario it names, and P4-12 has one open box (an authenticated manual pass, blocked on a credentials file this session correctly refused to read). Neither is this card's to close.

---

## Phase 4 exit gate

From `plan.md` §7. Every criterion needs an artifact under `evidence/phase-4/` — a file someone else could read.

- [ ] Receiving produces exactly one atomic stock increase (P4-05)
- [ ] Partial receiving accumulates correctly across multiple receipts (P4-06)
- [ ] Over-receiving rejected (P4-07)
- [ ] Ledger reconciles for all products (P4-01, asserted after every suite run)
- [ ] Concurrent receive-and-adjust on the same product is safe (P4-05, P4-10)
- [ ] Corrections use compensating movements, never edits (P4-08)
- [ ] `docs/database-design.md` finalised (P4-13)
- [ ] G-12 and G-21 closed in the gap register (P4-14)
- [ ] ADR-021 ACCEPTED and ADR-006 amended (P4-01, P4-04)
- [ ] Clean-clone build and both test suites green (P4-14)

**Carried, not owed here:** P0-02 and P0-05 belong to the **Phase 6** gate (ADR-016). P0-07's structure box belongs to **Phase 7**. CARRY-02 belongs to **Phase 6** (ADR-019). CARRY-04 is a watch item with no owner and becomes a card only on a second sighting. **CARRY-01 has now been carried through two phases without an owner** — it is not a Phase 4 blocker, but do not let Phase 6 planning close without placing it.
