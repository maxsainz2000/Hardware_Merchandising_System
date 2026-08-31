# tasks.md — Phase 6 (Reporting and operations)

**Scope:** current phase only. Regenerated at each phase entry from `plan.md`.
**Rules:** one task = one commit, prefixed with the task ID. Never tick a `Done when` box on a failing test or a partial implementation. Stop conditions are in `CLAUDE.md` §7.

**Legend:** ⬜ not started · 🟡 in progress · ✅ done · 🔴 blocked

---

> ## Phase 5 closed 2026-08-30 — PASS on re-sit, at commit `173fb39`
>
> Sixteen cards. A clean clone outside the tree, with **0** `bin`/`obj` at clone time, builds eleven projects at **0 warnings / 0 errors** and passes guardrails G-A–G-D plus **82/82 unit and 465/465 integration tests, 0 skipped**, against the real pinned MariaDB 10.4.32. **G-24 closed. G-12's sale component closed — which completes G-12 across every transaction type its mitigation names.** Full record: `evidence/phase-5/INDEX.md`.
>
> **The gate returned FAIL on its first sitting — for the third phase running — with *zero missing artifacts*.** Every one of the 21 files existed and was non-empty, every ADR was ACCEPTED, the deployed service was current, and the build and both suites reproduced independently. It failed on two defects behind ticked boxes, both on the keyboard/mouse-free claim:
>
> 1. **P5-13's box 5 said "asserted by a test over TabIndex *and access keys* per screen" and only the TabIndex half existed.** The evidence file quoted the box with an ellipsis that removed exactly the two words it could not prove.
> 2. **`docs/ui-specification.md` §4 cited `p5-13-pos-client.txt` §2 for a by-hand focus traversal.** §2 of that file is *"THE FINDING — the deployed service is stale"*; the file contained no traversal at all, and the "per client" claim was untrue for Inventory as well. This is the Phase 4 gate's own failure #1 recurring in a new place — a criterion resting on a citation that never fired.
>
> P5-16 closed both: the access-key half now exists over all five mechanisms WPF recognises, live traversals were captured for POS and Inventory in the P3-07 shape, and **every evidence path the document cites is now resolved against disk and required to contain what it is cited for** — watched fail against the original broken citation rather than an invented one.
>
> **Three lessons Phase 6 inherits, stated as rules.**
>
> **An artifact that exists is not an artifact that says what you cited it for.** Three gates have now been failed by claims that outran their evidence, and the last one had a perfect artifact-existence record. Phase 6 produces more prose than any phase so far — a report specification, an installation guide, a user guide, a backup runbook — and prose is where this defect lives. **Cite by section, and make something check the citation.**
>
> **An ellipsis inside a quoted acceptance box is a defect signal.** If a box has to be trimmed to match what was built, the box is not met. Write it out in full and mark the gap.
>
> **Nothing in this repository still detects a stale deployment.** It was found by hand at the Phase 4 gate and again at the Phase 5 gate. CARRY-05 is now a Phase 6 card (P6-10), not a note.
>
> **Frozen: no phase below may re-litigate a Phase 1–5 decision.** The connector, transaction pattern (ADR-006 + P4-04's amendment), auth scheme, error envelope (ADR-014), grant model (ADR-013), policy naming and self-approval (ADR-017), the barcode rule (ADR-018), the purchase-order status machine (ADR-020), the ledger reconciliation assertion (ADR-021), and sale rounding (ADR-022) are settled. Phase 6 consumes them.

---

# Carried forward — open items from earlier phases

These are carried, not reopened. **Everything here that does not need a borrowed machine is owed at *this* gate.**

> ## Re-scoped 2026-08-31 by ADR-030 — the machine-dependent half moves to a new Phase 8
>
> Phase 6 reached its gate with **fourteen of seventeen cards closed** (P6-01 – P6-12, P6-15, P6-17) and every open one blocked on the three classmates' laptops, which are not available. ADR-030 adds a final **Phase 8 — Field deployment and acceptance** collecting *all* work that needs those machines, from Phase 6 **and** Phase 7:
>
> | Was | Becomes | Origin |
> |---|---|---|
> | P6-13 · workstation manifest + admin rights | **P8-01** | P0-02 |
> | P6-14 · demo network from cold | **P8-02** | P0-05 |
> | P6-16 · clean install by someone else 🎯 | **P8-03** | ADR-012 |
> | *(Phase 7)* timed demo rehearsal, three clients | **P8-04** | `plan.md` §7 |
> | *(Phase 7)* training + UAT, one person per role | **P8-05** | `plan.md` §7 |
> | *(Phase 7)* UAT defect correction + sign-off | **P8-06** | `plan.md` §7 |
>
> **The order is the point.** ADR-016 rejected carrying this work into a rehearsal phase because a problem found *during* rehearsal has *"no phase left to absorb it."* Phase 8 puts the survey, the cold start and the clean install **ahead** of the rehearsal inside one phase, which is what supplies that room. This answers ADR-016's objection rather than ignoring it.
>
> **G-16 splits:** its release-manifest half closed here at P6-11; its clean-machine installation test closes at P8-03. P6-18 records the split, never the whole.

---

### 🟡 P0-02 · Windows baseline for every demo workstation — **moved to the Phase 8 gate (ADR-030), as P8-01**

Three demo workstations unsurveyed. `scripts/setup-client.ps1 -CaptureOnly` collects every field itself. Absorbed naturally by P8-03's clean-installation criterion: doing that on a classmate's laptop *is* the survey. **The one part that still fails late and unfixably, and that needs no machine at all:** whether each classmate holds **local administrator rights on their own laptop**. One message. ADR-016 flagged it on 2026-08-22 and it is still open — send it now, not when Phase 8 is scheduled.

### 🟡 P0-05 · Demo network rehearsal — **moved to the Phase 8 gate (ADR-030), as P8-02**

ADR-015 chose the demo topology on capability *readings*, never a cold start. Two questions remain open and could still invalidate an ACCEPTED ADR: whether Mobile Hotspot starts **from cold with nothing to share**, and whether this adapter sustains station + Wi-Fi Direct GO concurrently under load. **Neither needs a classmate** — both are answerable on this laptop plus one lab client in about fifteen minutes, and a *no* on either means ADR-015 is wrong and the demo network needs re-planning. Only the per-workstation round trip actually waits for Phase 8.

### 🟡 P0-07 · Repository structure — **the last box closes in Phase 7**

`user-guide.md` written in full (P6-15). `test-plan.md` → Phase 7 is the only one left.

### ✅ CARRY-04 · Intermittent `MSB3030` on a clean-clone build — **closed at P6-17 (ADR-029)**

Threshold reached at the Phase 5 gate (`evidence/phase-5/INDEX.md` §4.5): 9 consecutive deterministic failures under a deep `AppData\Local\Temp` path, against a first-attempt clean build at a plain path, same commit. The prior theory ("the generating task had not run yet") was **ruled out** — the referenced `runtimeconfig.json` was on disk at the moment the copy claimed it was missing.

**Closed at P6-17.** Root cause is `MAX_PATH` with `LongPathsEnabled = 0`, confirmed by controlled test rather than argued: a 260-character copy path failed, a 163-character one under the identical tree built clean. Guard in `run-tests.ps1`, constraint documented in the installation guide, no registry change. **Consumed one card later:** P6-18's clean clone was deliberately made at `C:\p6clone` rather than the deep scratchpad path for exactly this reason, and built at 0 warnings with no recurrence.

---

# Phase 6 — Reporting and operations

**Entry:** Phase 5 gate passed. **Closes:** G-22, G-15, and **G-16's release-manifest half only** — its clean-machine installation test moves to P8-03 (ADR-030).
**Docs produced:** `docs/report-specification.md`, `docs/backup-restore-guide.md`, `docs/user-guide.md`, and `docs/installation-guide.md` brought current.

*This phase inherits every Phase 1–5 decision wholesale. A report that disagrees with the detail screen is the defect this phase exists to prevent; a report that invents a second definition of "net" is how it happens.*

**What already exists, so no card below rebuilds it.** Every transaction table and its data: sales, sale lines, payments, returns, cashier sessions (`0011`/`0012`), receiving, purchase returns, counts, adjustments (`0009`/`0010`), the append-only `StockMovements` ledger and `StockBalances`. The audit pipeline (P2-04), the error envelope (ADR-014), pagination/sorting/filtering definitions (P3-03), the ledger reconciliation assertion (P4-01/ADR-021), `SystemSettings` (P2-06), maintenance mode (P2-09), the backup *mechanism* (P1-06/P1-07) and `Merchandising.Maintenance` itself.

> ### Five things that will bite in this phase specifically
>
> **1. The reconciliation test is the phase's key design call, and it must compare two independent computations.** `plan.md` §7: *"every report ships with a reconciliation test asserting the report total equals the sum from the corresponding detail screen for the same filter."* A test that calls the report's own SQL twice proves nothing. The detail side must come from the **existing** detail endpoint a user can open, so the two answers are computed by different code paths against the same rows.
>
> **2. "Net" is ambiguous and the spec knows it.** Spec §14 requires every report to *"indicate whether returns/cancellations are included or excluded"* and to define date filters in the **store time zone** (Asia/Manila) while every stored timestamp is UTC. A day boundary converted in the wrong direction moves a sale between reports. Define it once, in `docs/report-specification.md`, and make every report cite that definition rather than restate it.
>
> **3. Cost basis is *captured*, never current.** P5-01/P5-07 stored the effective unit price and cost on each sale line precisely so reporting could not drift. A report that joins to `Products.Cost` silently undoes the phase's key design call. That is the defect `plan.md` §7 named as very hard to fix later, arriving one phase later than the card that prevented it.
>
> **4. G-24's denylist does not yet cover a single file this phase will create.** `PaymentWordingTests` scans `src/Merchandising.POS/**/*.xaml` and `src/Merchandising.Contracts/Sales/*.vb` — every report-shaped surface that existed at Phase 5 close. Spec §14's payment-method summary must be *"explicitly labeled as operational recordings, not external settlement confirmation."* **Widen the scan in the same card that creates the directory** (`evidence/phase-5/INDEX.md` §4.8), or the criterion quietly stops being true.
>
> **5. Reports are read-only, and the grant model must keep them that way.** `merch_api` holds database-level `SELECT` and per-table writes only (ADR-013). No report needs a new grant. If a card finds itself writing a grants file, something has gone wrong — say so and stop rather than granting.

---

## Track A — Definitions before queries

*Nothing in Track B may start before this lands: every report cites these definitions rather than restating them.*

### ✅ P6-01 · `docs/report-specification.md` and the reconciliation harness 🎯
**Spec:** §14, §23 (G-22) · **Closes:** G-22 (definition half) · **Decides:** ADR-023
**Files:** `docs/report-specification.md`, `src/Merchandising.Domain/Reporting/`, `src/Merchandising.Contracts/Reporting/`, `src/tests/Merchandising.Tests.Integration/ReportReconciliationHarness.vb`

**Do:** Define, once and in writing, what every report below means: the store-local day boundary and its UTC conversion, returns/cancellation treatment per report, cost basis (captured, never current), rounding, filter semantics inherited from P3-03, and the permission each report requires. Then build the harness that every Track B card plugs into — the reconciliation assertion `plan.md` §7 makes this phase's key design call.

**Done when:**
- [x] Every one of spec §14's twelve reports has a row stating its returns treatment, its date-boundary rule, and its cost basis — no report may define these for itself later
- [x] The store-local → UTC day boundary is stated **as an example with real timestamps**, not as a sentence, and a test asserts a sale at 23:59:59 Asia/Manila lands in the day a cashier would expect and not the UTC one — checked against the actual offset arithmetic rather than assumed: 23:59:59 proves the window's inclusive upper bound; the real early-morning divergence (`plan.md` §7 point 2's actual risk) is proven separately with a 02:00:00 example, both stated in `docs/report-specification.md` §2 and ADR-023 point 2
- [x] The reconciliation harness compares the report total against **the existing detail endpoint's** sum for the same filter — two independent code paths, asserted, never the report's own query run twice
- [x] **Proven falsifiable:** the harness is watched fail against a deliberately off-by-one date boundary and a deliberately current-cost join, before any real report uses it
- [x] ADR-023 records the day-boundary rule and the two-independent-paths requirement, and why a self-comparing reconciliation test is worthless
- [x] Document verified by a drift check in the P5-14/P5-16 shape — **every evidence path it cites is resolved against disk and required to contain what it is cited for** (the Phase 5 gate's finding 2, not repeated)
- [x] States the academic-prototype framing required by `plan.md` §5

**Evidence:** `evidence/phase-6/p6-01-report-definitions.txt`

---

## Track B — The twelve reports

*Sequential. One worker, in order — every card shares `ReportsController`/`ReportService`/`ReportRepository`, and each consumes the last. Each card reconciles or it is not done.*

### ✅ P6-02 · Sales reports, and G-24's denylist widened to cover them
**Spec:** §14, §23 · **Files:** `src/Merchandising.Api/Controllers/ReportsController.vb`, `src/Merchandising.Api/Reporting/`, `src/Merchandising.Infrastructure/Data/ReportRepository.vb`, `src/tests/Merchandising.Tests.Unit/PaymentWordingTests.vb`

**Do:** Daily sales summary, sales by product, sales by cashier, and payment-method summary. The first four of spec §14's twelve, and the ones that carry money.

**Scope addition, per ADR-023 point 6:** no `GET` route existed anywhere for Sales, so this card also adds `GET /api/v1/sales` (`SalesController.SearchSales`) — the detail endpoint all four reports reconcile against — gated by `Reports.View`, not a new `Sales.View` policy.

**Done when:**
- [x] All four reconcile to their detail screens through P6-01's harness, each asserted in its own test
- [x] Cost basis reads the **captured** `SaleLines.Cost`, never `Products.Cost` — asserted by changing the product's cost after the sale and confirming the report is unmoved, the P5-07 test one layer up
- [x] The payment-method summary labels card/e-wallet **recorded, not authorised** (G-24), and **`PaymentWordingTests`' scan set is widened in this card** to the report contracts and API reporting directories this card creates — with the widening watched fail against a planted "Payment approved" label in the new directory
- [x] Every report states its selected date range and its returns treatment in the response, per spec §14 — asserted, not assumed
- [x] **Matrix suite extended** for every route added; integration suite green

**Evidence:** `evidence/phase-6/p6-02-sales-reports.txt`

### ✅ P6-03 · Returns, cancellations, and product performance
**Spec:** §14 · **Files:** `src/Merchandising.Api/Reporting/`, `src/Merchandising.Infrastructure/Data/ReportRepository.vb`

**Do:** Returns and cancellations (identifiers, source sale, product, quantity, reason, actor, approval, stock effect) and the product performance summary (net quantity, net sales value, recorded cost estimate, informational margin estimate, current stock position).

**Scope addition, per docs/report-specification.md §7:** no `GET` route existed for SalesReturns, so this card also adds `GET /api/v1/sales/returns` (`SalesReturnsController.GetReturns`) — the detail endpoint both reports reconcile against — gated by `Reports.View`, the identical reasoning P6-02 recorded for `GET /api/v1/sales`.

**Done when:**
- [x] Both reconcile through the harness
- [x] The margin estimate is labelled **informational** wherever it appears, per spec §14's own wording — this is a merchandising prototype, not an accounting statement, and the label is the whole mitigation
- [x] The returns report distinguishes a return that **restocked** from one that did not (P5-11's `RestocksItem`), because a stock effect of zero is a fact about the item, not a missing row
- [x] Product performance's "current stock position" reads `StockBalances` live while its sales figures respect the selected period — the mixed-temporality trap, asserted
- [x] **Matrix suite extended**; integration suite green

**Evidence:** `evidence/phase-6/p6-03-returns-and-performance.txt`

### ✅ P6-04 · Procurement reports
**Spec:** §14 · **Files:** `src/Merchandising.Api/Reporting/`, `src/Merchandising.Infrastructure/Data/ReportRepository.vb`

**Do:** Purchase-order history (supplier, order number, statuses, ordered quantity/value, received quantity/value, outstanding quantity) and goods-receiving history (receipt, supplier, date, product, ordered/received quantity, responsible user).

**Done when:**
- [x] Both reconcile through the harness against P3-06's existing detail screen (`GET /api/v1/purchase-orders/history`) and against each other, per `docs/report-specification.md` section 7
- [x] Outstanding quantity is computed as ordered minus received **from committed receipt rows**, and a partially-received order across several receipts reports one correct outstanding figure — P4-06's accumulation rule, read back
- [x] The purchase-order status machine (ADR-020) is consumed, not re-implemented — a report that hard-codes a status list fails when the machine changes
- [x] **Matrix suite extended**; integration suite green

Evidence: `evidence/phase-6/p6-04-procurement-reports.txt`. No new detail endpoint was needed this card — `docs/report-specification.md` section 7 had already named `GET /api/v1/purchase-orders/history` (P3-06) as the reconciliation target for both reports.

**Evidence:** `evidence/phase-6/p6-04-procurement-reports.txt`

### ✅ P6-05 · Stock reports
**Spec:** §14 · **Files:** `src/Merchandising.Api/Reporting/`, `src/Merchandising.Infrastructure/Data/ReportRepository.vb`

**Do:** Current stock, low stock, stock movement, and stock adjustment — the last four of the twelve.

**Done when:**
- [x] All four reconcile through the harness
- [x] The stock movement report carries the **correlation identifier** spec §14 requires, so a row in a report can be traced to the request that made it
- [x] Current stock and the movement report agree with each other for every product — this is P4-01's standing ledger assertion asked as a *report* question, and it must give the same answer
- [x] Low stock reuses P4-11's existing logic rather than restating the threshold comparison
- [x] **Matrix suite extended**; integration suite green

**Evidence:** `evidence/phase-6/p6-05-stock-reports.txt`

**Note:** No new detail endpoint was added — docs/report-specification.md §7 names the
existing `GET /api/v1/inventory/stock` and `GET /api/v1/inventory/stock/movements`
(both P4-11) as the reconciliation targets for all four reports. Low-stock's own
report handler calls `StockRepository.SearchLowStockAsync` directly rather than
adding a second WHERE clause, so its harness comparison is against `/stock`
filtered client-side by the same threshold, not against `/low-stock` again (which
would be the same query compared to itself). Stock-adjustment is the first GET
route `StockAdjustments` has ever had; it reconciles an Applied adjustment's
`QuantityVariance` against the exact `StockMovements` row it produced, matched by
`CorrelationId` — two different source tables, never the same query twice.

---

## Track C — Export

*Runs after Track B. Files are its own plus the export routes.*

### ✅ P6-06 · CSV export, and the Excel round trip
**Spec:** §14 · **Decides:** ADR-024 · **Files:** `src/Merchandising.Api/Reporting/CsvExporter.vb`, `src/Merchandising.Api/Controllers/ReportsController.vb`, `src/tests/Merchandising.Tests.Unit/CsvExporterTests.vb`

**Do:** UTF-8, header row, invariant column order, correct escaping, report parameters in the filename or export metadata. Export permissions mirror report permissions exactly.

**Done when:**
- [x] Escaping is asserted against the values that actually break CSV: an embedded comma, an embedded double quote, an embedded newline, a leading `=`/`+`/`-`/`@` (formula injection), and a UTF-8 name outside ASCII
- [x] **UTF-8 BOM decision recorded in ADR-024 and tested** — Excel misreads a BOM-less UTF-8 CSV as ANSI and mangles non-ASCII, and "round-trips through Excel without mangling" is a gate criterion, so this is a decision with evidence rather than a default
- [x] Column order is invariant and asserted against a committed expected header, so a reordered `SELECT` fails the suite rather than a classmate's spreadsheet
- [x] Export permission for each report **equals** that report's own view permission — asserted per report by the matrix suite, not by inspection
- [x] The round trip is **performed**: a real export opened in Excel on this machine and read back, with the artifact recorded, not described
- [x] Decimal values keep their stored scale in the export — no float formatting anywhere near money

**Evidence:** `evidence/phase-6/p6-06-csv-export.txt`, `evidence/phase-6/p6-06-excel-roundtrip/`

---

## Track D — Backup and restore to production quality

*Files disjoint from every other track. Can run in parallel with Tracks B and C.*

### ✅ P6-07 · Retention, integrity, and the off-host copy
**Spec:** §15, §23 (G-15) · **Decides:** ADR-025 · **Files:** `src/Merchandising.Maintenance/Backup/`, `db/`, `scripts/`

**Do:** Promote P1-06/P1-07's mechanism to the control table spec §15 states: configurable retention recorded in `SystemSettings`, integrity record (size, checksum, timestamp, source database version, result), protected directory, failure handling that records the error and requires follow-up, and the periodic off-host copy.

**Done when:**
- [x] Retention is configurable, read from `SystemSettings`, and **deletes the right files** — asserted by creating more backups than the retention count and confirming exactly the oldest surplus is removed, never the newest
- [x] **CARRY-02 closed:** `OffHostPath` is asserted with a `MERCHBACKUP` volume **actually attached**, not artifact-backed. ADR-019's rule stands — the test must not depend on which USB stick is attached, so it asserts behaviour given a volume, and skips loudly rather than silently when none is present
- [x] A failed backup records the error detail and surfaces an operational warning; it never reports success — watched fail against an induced failure (unwritable directory)
- [x] The backup directory is outside the application binaries and not served by any API route — asserted by requesting it through the API and getting a refusal
- [x] ADR-025 records the retention default, the off-host rotation rule, and the failure-handling contract

**Evidence:** `evidence/phase-6/p6-07-backup-retention.txt`

### ✅ P6-08 · Restore, measured against the documented RPO/RTO
**Spec:** §15 (PA-005) · **Files:** `src/Merchandising.Maintenance/Restore/`, `scripts/`

**Do:** The seven-step restore procedure spec §15 defines, exercised end to end through maintenance mode (P2-09), with the **actual recovery time measured**. PA-005 targets RPO ≤ 24 h and a demonstrated RTO ≤ 15 min to verified state on the demo host.

**Done when:**
- [x] A restore is performed end to end against an isolated or controlled database — **verification is by data, not file existence**: expected users, products, balances and recent transactions confirmed present after restart
- [x] The elapsed time is **measured and recorded as a number**, and compared against the 15-minute target explicitly. If it exceeds it, that is a finding to report, not a number to round
- [x] Maintenance mode is entered and released through the real workflow, and the completed restore event is recorded after service recovery, per spec §15 steps 1–7
- [x] The ledger reconciles after restore (ADR-021), asserted — a restore that silently drops movements is the worst possible outcome of this card
- [x] Restore refuses to run against a backup whose recorded checksum does not match, watched fail

**Evidence:** `evidence/phase-6/p6-08-restore-timed.txt`

### ✅ P6-09 · `docs/backup-restore-guide.md`
**Spec:** §15, §20 · **Files:** `docs/backup-restore-guide.md`

**Do:** The runbook an operator who is not the author follows: exact executable path and options (`mysqldump.exe` — `mariadb-dump` does not exist in this XAMPP build, CLAUDE.md §6.1), the backup account (`merch_backup`, never root), schedule, retention, the protected directory, the off-host step, and the full restore procedure with its measured timing.

**Done when:**
- [x] Every command is copy-pasteable and was **run from the document** rather than transcribed — the P2-12/P3-08/P4-13/P5-14 shape
- [x] The three database identities are named correctly per ADR-013, and the guide never tells anyone to use root
- [x] Drift-checked by a test, including **every evidence path it cites resolved against disk and required to contain what it is cited for**
- [x] One of P0-07's remaining documents struck from its list

**Evidence:** `evidence/phase-6/p6-09-backup-restore-guide.txt`

---

## Track E — Health, packaging, and the stale-deployment defect

*Files disjoint from Tracks B, C and D.*

### ✅ P6-10 · Health endpoint carries build identity — **CARRY-05, closed**
**Spec:** §23 (G-16) · **Decides:** ADR-027 · **Files:** `src/Merchandising.Api/Controllers/HealthController.vb`, `scripts/publish-release.ps1`, `scripts/install-service.ps1`

**Do:** The deployed Windows Service was found four weeks stale at the Phase 4 gate and stale again at the Phase 5 gate, and **no mechanism in this repository could have noticed** — the integration suite builds the host in-process, and `install-service.ps1` verifies that *something* is listening on 8443, not that what is listening is current. Put the commit identifier and build timestamp on the health endpoint, and compare it against the tree.

**Done when:**
- [x] `GET /health` returns the commit SHA and build timestamp of the running binary, stamped at publish time — not read from the working tree at request time, which would always agree with itself
- [x] A script compares the deployed identity against `git rev-parse HEAD` and **fails loudly** when they differ, with the two values printed
- [x] **Watched fail:** run the comparison against the currently-deployed build, then make a source commit without redeploying, and confirm the check goes red. This is the whole card — a staleness check that has never gone red detects nothing
- [x] `install-service.ps1` runs the comparison after installing and refuses to report success on a mismatch
- [x] The endpoint leaks nothing beyond commit and timestamp — no paths, no connection details, no environment (CLAUDE.md §5)
- [x] ADR-027 records why in-process integration tests can never catch this class of defect

**Evidence:** `evidence/phase-6/p6-10-build-identity.txt`

### ✅ P6-11 · Release packaging and the runtime manifest
**Spec:** §23 (G-16) · **Closes:** G-16 · **Decides:** ADR-026 · **Files:** `scripts/publish-release.ps1`, `docs/installation-guide.md`

**Do:** G-16's mitigation: name `win-x64`; record the client Desktop Runtime and the host API runtime/self-contained choice; verify at installation. Never `PublishAot` or `PublishTrimmed` — both are C#-only and forbidden (CLAUDE.md §3).

**Done when:**
- [x] The release manifest records, per artifact: target runtime, framework-dependent vs. self-contained, and the exact runtime version required
- [x] The installer **verifies** the required runtime is present before installing and names what is missing when it is not — verified by running it on a machine lacking the Desktop Runtime, or by an equivalent forced check, not assumed
- [x] Guardrail G-D still passes — no `PublishAot`/`PublishTrimmed` crept into any project file
- [x] ADR-026 records the packaging choice per artifact and why

**Evidence:** `evidence/phase-6/p6-11-release-manifest.txt`

---

## Track F — Account recovery

*Files disjoint from Track D's backup subdirectories. Carried since Phase 2 — this is its fourth carry and its last.*

### ✅ P6-12 · `reset-password` and `unlock-user` on the Maintenance CLI — **CARRY-01, closed**
**Spec:** §23 (G-19) · **Decides:** ADR-028 · **Files:** `src/Merchandising.Maintenance/Users/`, `docs/user-guide.md`

**Do:** Spec §23's G-19 remediation names *account recovery* among seven components; six are proven. Lockout self-recovers (`LockedUntilUtc` = now + 15 min), but **a forgotten password has no route at all**. ADR-017 §4 puts user management on this CLI, and the CLI has `create-user` with neither `reset-password` nor `unlock-user`. **Surfaced at the Phase 2 gate and carried through 2 → 3 → 4 → 5.** A classmate who forgets a password on demo day currently has no way back in.

**Done when:**
- [x] `reset-password` sets a new password through the same hashing path `create-user` uses — never a second implementation, and never a plaintext column
- [x] `unlock-user` clears `LockedUntilUtc` and the failed-attempt counter, and is distinct from `reset-password` because the two failures are different
- [x] Both write an `AuditLogs` row naming the operator and the target account — an out-of-band credential change that leaves no trace is worse than no feature
- [x] Both run **only** as the maintenance identity on the host, never through an API route — asserted by confirming no route reaches them
- [x] The user guide documents both, in the words an operator would search for ("forgot password", "locked out")
- [x] ADR-028 records why recovery is CLI-only and what that costs on demo day

**Evidence:** `evidence/phase-6/p6-12-account-recovery.txt`

---

## ~~Track G — The demo environment, proven rather than read~~ → **moved to Phase 8 (ADR-030)**

*Needed the physical machines and could not be dispatched to `box3`. **P6-13 → P8-01**, **P6-14 → P8-02**. Their acceptance boxes travel unchanged and are not re-argued here; `plan.md` §7 Phase 8 carries them. Not deleted, moved — this block is the pointer that stops a future reader concluding they were dropped.*

> **P8-02's first two boxes need no classmate and should not wait for Phase 8's schedule:** Mobile Hotspot from cold with nothing to share, and station + Wi-Fi Direct GO concurrently under load. Author's laptop plus one lab client, ~15 minutes. If either answer is no, **ADR-015 is wrong** — a finding worth having now rather than on demo day.

---

## Track H — Documents and closure

### ✅ P6-15 · `docs/user-guide.md`
**Spec:** §20 · **Files:** `docs/user-guide.md`

**Done when:**
- [x] Covers all five roles and all three clients, task-first (*"take a sale"*, *"receive goods"*, *"close the day"*), not screen-first
- [x] States what the system does **not** do, where a user reads it: no terminal, bank, cash drawer, scale, customer display or receipt printer integration (G-24), and card/e-wallet is recorded, not authorised
- [x] Drift-checked, with every evidence path it cites resolved and required to contain what it is cited for
- [x] The last of P0-07's Phase 6 documents struck from its list

**Evidence:** `evidence/phase-6/p6-15-user-guide.txt`

### ~~P6-16 · Clean installation on a machine the author has never configured~~ → **P8-03 (ADR-030)** 🎯

*ADR-012's promotion travels with it: under the delivery model this is still **the measure of whether the deliverable exists at all**, and it is still the highest-value card in the project. It moves only because it needs a classmate at the keyboard and no classmate is available. Read *fresh machine* strictly — not the author's, and not one the author has ever configured.*

**One box does not move, because it needs no second machine.** The **`README` and the bootstrap script** — XAMPP layout, `my.ini` `sql_mode`, `bind-address`, all three database accounts and their grants, the backup directory, the hosts entry — are the artifacts P8-03 will be *performed from*. Writing them in Phase 7, against `docs/installation-guide.md` as it now stands, is what makes P8-03 a test of the guide rather than a test of the author's memory. `plan.md` §7 Phase 7 now carries them.

### ✅ P6-17 · `MSB3030` under deep build paths — **CARRY-04, second sighting met**
**Spec:** — · **Files:** `Directory.Build.props`, `scripts/run-tests.ps1`, `docs/adr.md`

**Do:** Reproduced deterministically (9 consecutive failures) under a deep `AppData\Local\Temp\claude\...` path while the identical commit built clean at a plain path. The `runtimeconfig.json` was confirmed **present on disk** at the moment the copy claimed it missing, which rules out the original theory. Either fix it or record the constraint where someone will hit it.

**Done when:**
- [x] Root cause identified, or the path-length hypothesis confirmed or refuted by test — a card closed on "we avoid that directory now" without knowing why is a card that reopens in Phase 7
- [x] Either a fix, or a documented constraint in the installation guide **and** a check in `run-tests.ps1` that warns before building from a path that triggers it
- [x] `Merchandising.Tests.Unit`'s references to two `OutputType=WinExe` projects reviewed — that coupling is the thing that makes the copy race possible at all

**Evidence:** `evidence/phase-6/p6-17-msb3030.txt`

### ✅ P6-18 · Phase 6 closure pack
**Spec:** §20 · **Files:** `evidence/phase-6/`

**Done when:**
- [x] Clean clone outside the repository, **0** `bin`/`obj` at clone time, builds at **0 warnings** and passes guardrails plus both suites → `p6-18-clean-clone.log` — commit `af9ef1c`, clone at `C:\p6clone`, `0 Warning(s) 0 Error(s)`, **unit 140/140, integration 512/512, 0 skipped**, `run-tests.ps1` exit 0
- [x] `evidence/phase-6/INDEX.md` maps every exit criterion and every card to an artifact that exists **and contains what it is cited for** — three gates running have now been failed by a claim outrunning its evidence, and the last one had a perfect existence record — 17 paths in this file and 7 cross-phase paths in the four Phase 6 documents, each opened and read for the claim it is cited for (INDEX §2)
- [x] **G-22 and G-15 recorded closed** in the gap register, each against its own artifact — INDEX §3
- [x] **G-16 recorded as its two halves, never as one gap closed** — the release-manifest and runtime-verification half closed here against `p6-11-release-manifest.txt`; the clean-machine installation test **open, owned by P8-03** (ADR-030). A gap register that says "G-16 closed" is this pack's own failure mode — INDEX §3 and §4.3; `docs/installation-guide.md` §7.3's outright "closed" claim corrected at this card
- [x] Every carried item from Phases 0–5 either closed or explicitly re-carried with an owner — CARRY-01, CARRY-02, CARRY-04, CARRY-05 close here; **P0-02 and P0-05 re-carried to the Phase 8 gate under ADR-030**, which is the ADR-016 mechanism used a third time, not a quiet slip — INDEX §6
- [x] **ADR-030 is ACCEPTED and its card-move table matches `plan.md` §7 and this file** — P6-13→P8-01, P6-14→P8-02, P6-16→P8-03, checked in all three places rather than assumed consistent
- [x] Any claim narrower than its wording marked ⚠ and explained; **every quantitative claim checked against the number it cites** — INDEX §4 (six entries) and §5, which corrects Phase 5's 79→82 and Phase 4's 346→348 against their own logs
- [x] Every ADR this phase owed (ADR-023 – ADR-030, and any raised along the way) is ACCEPTED, not PENDING — checked directly against `docs/adr.md` — INDEX §7
- [x] New ADRs appended **before** the *Template for new entries* section — verified by line number: ADR-023–030 at lines 1109–1324, template at 1375
- [x] **The deployed service is current**, verified by P6-10's own build-identity check rather than by hand — found **7 commits stale** and caught by the mechanism in seconds rather than by hand (its first real detection); redeployed and re-verified matching. ⚠ Inherently one commit behind at capture — INDEX §4.4

**Three findings this card made, all recorded rather than smoothed over:** Phase 6 introduced three
analyzer warnings that passed through two cards claiming "suites green" (fixed at `af9ef1c`, watched
fail — INDEX §4.1), and this pack's own first clean-clone run reported 9 false failures by invoking
`dotnet test -c Release` instead of `scripts/run-tests.ps1` (INDEX §4.5).

**Evidence:** `evidence/phase-6/p6-18-clean-clone.log`, `evidence/phase-6/INDEX.md`

---

## Phase 6 exit gate

From `plan.md` §7. Every criterion needs an artifact under `evidence/phase-6/` — a file someone else could read, **containing what it is cited for**.

- [x] Every report reconciles to source data (P6-01 – P6-05) — INDEX §1 row 1
- [x] CSV round-trips through Excel without mangling (P6-06) — real export read back, artifact committed
- [x] Backup/restore meets the documented RPO/RTO with measured evidence (P6-07, P6-08) — **7.17 s to verified state against a 900 s target**; ⚠ database portion, not service restart — INDEX §4.2
- [x] G-22 and G-15 closed in the gap register; **G-16 recorded as one half closed, one half open** (P6-18) — INDEX §3
- [x] `report-specification.md`, `backup-restore-guide.md`, `user-guide.md` written; `installation-guide.md` current (P6-01, P6-09, P6-15)
- [x] Clean-clone build and both test suites green (P6-18) — **140/140 unit, 512/512 integration, 0 warnings, 0 skipped**
- [x] *(CLAUDE.md §9)* Every task done — no card left 🟡 and rounded up — 14 closed; the 3 machine-dependent cards are **moved by ADR-030, not rounded up**, and appear as ⬜ under Phase 8 rather than ticked here

**These boxes are the closure pack's own reading, not a gate result.** `/phase-gate` runs the exit
review and is the only thing that may declare PASS. Three phases running have returned FAIL on their
first sitting with every artifact present, so a ticked list here is an invitation to that review, not
a substitute for it.

**Moved to the Phase 8 gate by ADR-030 — struck from this list, not silently dropped:**

- ~~A clean installation on a fresh machine succeeds from the guide alone~~ → **P8-03**
- ~~Every demo workstation captured, including local administrator rights~~ → **P8-01** *(P0-02)*
- ~~`ping MERCH-HOST` + validated HTTPS round trip from every workstation, network from cold~~ → **P8-02** *(P0-05)*

**Carried, not owed here:** P0-07's structure box closes in **Phase 7** with `test-plan.md`. Of the rest carried from Phases 0–5, four close at **this** gate — CARRY-01 (P6-12), CARRY-02 (P6-07), CARRY-04 (P6-17), CARRY-05 (P6-10) — and two re-carry to the **Phase 8** gate: P0-02 (P8-01) and P0-05 (P8-02).

**Nothing carried into Phase 6 may be carried out of it without an ADR saying why**, in the ADR-016 shape. That rule is intact and was followed: **ADR-030** is the ADR, it names what the deferral costs, and it answers ADR-016's own objection to a later destination rather than talking past it. This is the mechanism's third honest use and remains the only acceptable route.
