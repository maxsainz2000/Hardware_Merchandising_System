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

These are carried, not reopened. **Every one of them is owed at *this* gate** — Phase 6 is where the carrying stops.

---

### 🟡 P0-02 · Windows baseline for every demo workstation — **owed at this gate (ADR-016)**

Three demo workstations unsurveyed. `scripts/setup-client.ps1 -CaptureOnly` collects every field itself. Absorbed naturally by P6-16's clean-installation criterion: doing that on a classmate's laptop *is* the survey. **The one part that still fails late and unfixably:** whether each classmate holds **local administrator rights on their own laptop**. One message, no machine needed — send it before P6-16 is scheduled, not during it.

### 🟡 P0-05 · Demo network rehearsal — **owed at this gate (ADR-016)**

ADR-015 chose the demo topology on capability *readings*, never a cold start. Two questions remain open and could still invalidate an ACCEPTED ADR: whether Mobile Hotspot starts **from cold with nothing to share**, and whether this adapter sustains station + Wi-Fi Direct GO concurrently under load. P6-14 owns it.

### 🟡 P0-07 · Repository structure — **the last box closes in Phase 7**

Two `docs/*.md` remain after this phase's are written: `backup-restore-guide.md` → P6-09; `user-guide.md` → P6-15; `test-plan.md` → Phase 7. The other four are written.

### ⬜ CARRY-04 · Intermittent `MSB3030` on a clean-clone build — **second sighting met; now P6-17**

Threshold reached at the Phase 5 gate (`evidence/phase-5/INDEX.md` §4.5): 9 consecutive deterministic failures under a deep `AppData\Local\Temp` path, against a first-attempt clean build at a plain path, same commit. The prior theory ("the generating task had not run yet") was **ruled out** — the referenced `runtimeconfig.json` was on disk at the moment the copy claimed it was missing. Card raised as **P6-17**.

---

# Phase 6 — Reporting and operations

**Entry:** Phase 5 gate passed. **Closes:** G-22, G-15, G-16.
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

### ⬜ P6-05 · Stock reports
**Spec:** §14 · **Files:** `src/Merchandising.Api/Reporting/`, `src/Merchandising.Infrastructure/Data/ReportRepository.vb`

**Do:** Current stock, low stock, stock movement, and stock adjustment — the last four of the twelve.

**Done when:**
- [ ] All four reconcile through the harness
- [ ] The stock movement report carries the **correlation identifier** spec §14 requires, so a row in a report can be traced to the request that made it
- [ ] Current stock and the movement report agree with each other for every product — this is P4-01's standing ledger assertion asked as a *report* question, and it must give the same answer
- [ ] Low stock reuses P4-11's existing logic rather than restating the threshold comparison
- [ ] **Matrix suite extended**; integration suite green

**Evidence:** `evidence/phase-6/p6-05-stock-reports.txt`

---

## Track C — Export

*Runs after Track B. Files are its own plus the export routes.*

### ⬜ P6-06 · CSV export, and the Excel round trip
**Spec:** §14 · **Decides:** ADR-024 · **Files:** `src/Merchandising.Api/Reporting/CsvExporter.vb`, `src/Merchandising.Api/Controllers/ReportsController.vb`, `src/tests/Merchandising.Tests.Unit/CsvExporterTests.vb`

**Do:** UTF-8, header row, invariant column order, correct escaping, report parameters in the filename or export metadata. Export permissions mirror report permissions exactly.

**Done when:**
- [ ] Escaping is asserted against the values that actually break CSV: an embedded comma, an embedded double quote, an embedded newline, a leading `=`/`+`/`-`/`@` (formula injection), and a UTF-8 name outside ASCII
- [ ] **UTF-8 BOM decision recorded in ADR-024 and tested** — Excel misreads a BOM-less UTF-8 CSV as ANSI and mangles non-ASCII, and "round-trips through Excel without mangling" is a gate criterion, so this is a decision with evidence rather than a default
- [ ] Column order is invariant and asserted against a committed expected header, so a reordered `SELECT` fails the suite rather than a classmate's spreadsheet
- [ ] Export permission for each report **equals** that report's own view permission — asserted per report by the matrix suite, not by inspection
- [ ] The round trip is **performed**: a real export opened in Excel on this machine and read back, with the artifact recorded, not described
- [ ] Decimal values keep their stored scale in the export — no float formatting anywhere near money

**Evidence:** `evidence/phase-6/p6-06-csv-export.txt`, `evidence/phase-6/p6-06-excel-roundtrip/`

---

## Track D — Backup and restore to production quality

*Files disjoint from every other track. Can run in parallel with Tracks B and C.*

### ⬜ P6-07 · Retention, integrity, and the off-host copy
**Spec:** §15, §23 (G-15) · **Decides:** ADR-025 · **Files:** `src/Merchandising.Maintenance/Backup/`, `db/`, `scripts/`

**Do:** Promote P1-06/P1-07's mechanism to the control table spec §15 states: configurable retention recorded in `SystemSettings`, integrity record (size, checksum, timestamp, source database version, result), protected directory, failure handling that records the error and requires follow-up, and the periodic off-host copy.

**Done when:**
- [ ] Retention is configurable, read from `SystemSettings`, and **deletes the right files** — asserted by creating more backups than the retention count and confirming exactly the oldest surplus is removed, never the newest
- [ ] **CARRY-02 closed:** `OffHostPath` is asserted with a `MERCHBACKUP` volume **actually attached**, not artifact-backed. ADR-019's rule stands — the test must not depend on which USB stick is attached, so it asserts behaviour given a volume, and skips loudly rather than silently when none is present
- [ ] A failed backup records the error detail and surfaces an operational warning; it never reports success — watched fail against an induced failure (unwritable directory)
- [ ] The backup directory is outside the application binaries and not served by any API route — asserted by requesting it through the API and getting a refusal
- [ ] ADR-025 records the retention default, the off-host rotation rule, and the failure-handling contract

**Evidence:** `evidence/phase-6/p6-07-backup-retention.txt`

### ⬜ P6-08 · Restore, measured against the documented RPO/RTO
**Spec:** §15 (PA-005) · **Files:** `src/Merchandising.Maintenance/Restore/`, `scripts/`

**Do:** The seven-step restore procedure spec §15 defines, exercised end to end through maintenance mode (P2-09), with the **actual recovery time measured**. PA-005 targets RPO ≤ 24 h and a demonstrated RTO ≤ 15 min to verified state on the demo host.

**Done when:**
- [ ] A restore is performed end to end against an isolated or controlled database — **verification is by data, not file existence**: expected users, products, balances and recent transactions confirmed present after restart
- [ ] The elapsed time is **measured and recorded as a number**, and compared against the 15-minute target explicitly. If it exceeds it, that is a finding to report, not a number to round
- [ ] Maintenance mode is entered and released through the real workflow, and the completed restore event is recorded after service recovery, per spec §15 steps 1–7
- [ ] The ledger reconciles after restore (ADR-021), asserted — a restore that silently drops movements is the worst possible outcome of this card
- [ ] Restore refuses to run against a backup whose recorded checksum does not match, watched fail

**Evidence:** `evidence/phase-6/p6-08-restore-timed.txt`

### ⬜ P6-09 · `docs/backup-restore-guide.md`
**Spec:** §15, §20 · **Files:** `docs/backup-restore-guide.md`

**Do:** The runbook an operator who is not the author follows: exact executable path and options (`mysqldump.exe` — `mariadb-dump` does not exist in this XAMPP build, CLAUDE.md §6.1), the backup account (`merch_backup`, never root), schedule, retention, the protected directory, the off-host step, and the full restore procedure with its measured timing.

**Done when:**
- [ ] Every command is copy-pasteable and was **run from the document** rather than transcribed — the P2-12/P3-08/P4-13/P5-14 shape
- [ ] The three database identities are named correctly per ADR-013, and the guide never tells anyone to use root
- [ ] Drift-checked by a test, including **every evidence path it cites resolved against disk and required to contain what it is cited for**
- [ ] One of P0-07's remaining documents struck from its list

**Evidence:** `evidence/phase-6/p6-09-backup-restore-guide.txt`

---

## Track E — Health, packaging, and the stale-deployment defect

*Files disjoint from Tracks B, C and D.*

### ⬜ P6-10 · Health endpoint carries build identity — **CARRY-05, found at two gates** 🎯
**Spec:** §23 (G-16) · **Decides:** ADR-027 · **Files:** `src/Merchandising.Api/Controllers/HealthController.vb`, `scripts/publish-release.ps1`, `scripts/install-service.ps1`

**Do:** The deployed Windows Service was found four weeks stale at the Phase 4 gate and stale again at the Phase 5 gate, and **no mechanism in this repository could have noticed** — the integration suite builds the host in-process, and `install-service.ps1` verifies that *something* is listening on 8443, not that what is listening is current. Put the commit identifier and build timestamp on the health endpoint, and compare it against the tree.

**Done when:**
- [ ] `GET /health` returns the commit SHA and build timestamp of the running binary, stamped at publish time — not read from the working tree at request time, which would always agree with itself
- [ ] A script compares the deployed identity against `git rev-parse HEAD` and **fails loudly** when they differ, with the two values printed
- [ ] **Watched fail:** run the comparison against the currently-deployed build, then make a source commit without redeploying, and confirm the check goes red. This is the whole card — a staleness check that has never gone red detects nothing
- [ ] `install-service.ps1` runs the comparison after installing and refuses to report success on a mismatch
- [ ] The endpoint leaks nothing beyond commit and timestamp — no paths, no connection details, no environment (CLAUDE.md §5)
- [ ] ADR-027 records why in-process integration tests can never catch this class of defect

**Evidence:** `evidence/phase-6/p6-10-build-identity.txt`

### ⬜ P6-11 · Release packaging and the runtime manifest
**Spec:** §23 (G-16) · **Closes:** G-16 · **Decides:** ADR-026 · **Files:** `scripts/publish-release.ps1`, `docs/installation-guide.md`

**Do:** G-16's mitigation: name `win-x64`; record the client Desktop Runtime and the host API runtime/self-contained choice; verify at installation. Never `PublishAot` or `PublishTrimmed` — both are C#-only and forbidden (CLAUDE.md §3).

**Done when:**
- [ ] The release manifest records, per artifact: target runtime, framework-dependent vs. self-contained, and the exact runtime version required
- [ ] The installer **verifies** the required runtime is present before installing and names what is missing when it is not — verified by running it on a machine lacking the Desktop Runtime, or by an equivalent forced check, not assumed
- [ ] Guardrail G-D still passes — no `PublishAot`/`PublishTrimmed` crept into any project file
- [ ] ADR-026 records the packaging choice per artifact and why

**Evidence:** `evidence/phase-6/p6-11-release-manifest.txt`

---

## Track F — Account recovery

*Files disjoint from Track D's backup subdirectories. Carried since Phase 2 — this is its fourth carry and its last.*

### ⬜ P6-12 · `reset-password` and `unlock-user` on the Maintenance CLI — **CARRY-01**
**Spec:** §23 (G-19) · **Decides:** ADR-028 · **Files:** `src/Merchandising.Maintenance/Users/`, `docs/user-guide.md`

**Do:** Spec §23's G-19 remediation names *account recovery* among seven components; six are proven. Lockout self-recovers (`LockedUntilUtc` = now + 15 min), but **a forgotten password has no route at all**. ADR-017 §4 puts user management on this CLI, and the CLI has `create-user` with neither `reset-password` nor `unlock-user`. **Surfaced at the Phase 2 gate and carried through 2 → 3 → 4 → 5.** A classmate who forgets a password on demo day currently has no way back in.

**Done when:**
- [ ] `reset-password` sets a new password through the same hashing path `create-user` uses — never a second implementation, and never a plaintext column
- [ ] `unlock-user` clears `LockedUntilUtc` and the failed-attempt counter, and is distinct from `reset-password` because the two failures are different
- [ ] Both write an `AuditLogs` row naming the operator and the target account — an out-of-band credential change that leaves no trace is worse than no feature
- [ ] Both run **only** as the maintenance identity on the host, never through an API route — asserted by confirming no route reaches them
- [ ] The user guide documents both, in the words an operator would search for ("forgot password", "locked out")
- [ ] ADR-028 records why recovery is CLI-only and what that costs on demo day

**Evidence:** `evidence/phase-6/p6-12-account-recovery.txt`

---

## Track G — The demo environment, proven rather than read

*Needs the physical machines. Cannot be dispatched to `box3`.*

### ⬜ P6-13 · Workstation manifest and admin rights — **P0-02, owed here**
**Spec:** §20 · **Files:** `docs/environment-manifest.md`

**Done when:**
- [ ] All three demo workstations captured in §3.2: edition, build, architecture, resolution, scaling, and **local administrator rights**
- [ ] Captured by running `scripts/setup-client.ps1 -CaptureOnly` on each machine, not by asking and transcribing
- [ ] Lab hardware does not satisfy this (ADR-012) — the machines are the classmates' own laptops

**Evidence:** `evidence/phase-6/p6-13-workstation-manifest.txt`

### ⬜ P6-14 · Demo network from cold — **P0-05, owed here**
**Spec:** §20 · **Files:** `docs/environment-manifest.md`, `docs/installation-guide.md`

**Done when:**
- [ ] Mobile Hotspot brought up **from cold, with nothing to share**, and the result recorded either way — this is the open question ADR-015 was accepted without
- [ ] The adapter sustains station + Wi-Fi Direct GO concurrently **under load**, measured
- [ ] `ping MERCH-HOST` and a validated HTTPS round trip from **every** demo workstation
- [ ] If either question invalidates ADR-015, that is a finding and an ADR amendment, not a card to quietly close

**Evidence:** `evidence/phase-6/p6-14-network-cold-start.txt`

---

## Track H — Documents and closure

### ⬜ P6-15 · `docs/user-guide.md`
**Spec:** §20 · **Files:** `docs/user-guide.md`

**Done when:**
- [ ] Covers all five roles and all three clients, task-first (*"take a sale"*, *"receive goods"*, *"close the day"*), not screen-first
- [ ] States what the system does **not** do, where a user reads it: no terminal, bank, cash drawer, scale, customer display or receipt printer integration (G-24), and card/e-wallet is recorded, not authorised
- [ ] Drift-checked, with every evidence path it cites resolved and required to contain what it is cited for
- [ ] The last of P0-07's Phase 6 documents struck from its list

**Evidence:** `evidence/phase-6/p6-15-user-guide.txt`

### ⬜ P6-16 · Clean installation on a machine the author has never configured 🎯
**Spec:** §20, §23 · **Closes:** G-15, G-16 (installation half) · **Files:** `docs/installation-guide.md`, `scripts/`, `README.md`

**Do:** **ADR-012 promotes this above every other criterion in the phase:** under the delivery model it is *the measure of whether the deliverable exists at all* — three classmates must install and demonstrate this system without the author present. Read *fresh machine* strictly: not the author's, and not one the author has ever configured.

**Done when:**
- [ ] A `README` and a bootstrap script exist and perform the setup currently recorded only as prose: XAMPP layout, `my.ini` `sql_mode`, `bind-address`, all three database accounts and their grants, the backup directory, the hosts entry
- [ ] The installation is performed **by someone other than the author**, from the guide alone, with the author not touching the keyboard — the point is the guide, not the outcome
- [ ] Every question that person had to ask is a defect in the guide and is fixed before the box is ticked
- [ ] The result is a working client reaching the API over HTTPS, with a sale completed on it
- [ ] Time taken recorded as a number

**Evidence:** `evidence/phase-6/p6-16-clean-install.txt`

### ⬜ P6-17 · `MSB3030` under deep build paths — **CARRY-04, second sighting met**
**Spec:** — · **Files:** `Directory.Build.props`, `scripts/run-tests.ps1`, `docs/adr.md`

**Do:** Reproduced deterministically (9 consecutive failures) under a deep `AppData\Local\Temp\claude\...` path while the identical commit built clean at a plain path. The `runtimeconfig.json` was confirmed **present on disk** at the moment the copy claimed it missing, which rules out the original theory. Either fix it or record the constraint where someone will hit it.

**Done when:**
- [ ] Root cause identified, or the path-length hypothesis confirmed or refuted by test — a card closed on "we avoid that directory now" without knowing why is a card that reopens in Phase 7
- [ ] Either a fix, or a documented constraint in the installation guide **and** a check in `run-tests.ps1` that warns before building from a path that triggers it
- [ ] `Merchandising.Tests.Unit`'s references to two `OutputType=WinExe` projects reviewed — that coupling is the thing that makes the copy race possible at all

**Evidence:** `evidence/phase-6/p6-17-msb3030.txt`

### ⬜ P6-18 · Phase 6 closure pack
**Spec:** §20 · **Files:** `evidence/phase-6/`

**Done when:**
- [ ] Clean clone outside the repository, **0** `bin`/`obj` at clone time, builds at **0 warnings** and passes guardrails plus both suites → `p6-18-clean-clone.log`
- [ ] `evidence/phase-6/INDEX.md` maps every exit criterion and every card to an artifact that exists **and contains what it is cited for** — three gates running have now been failed by a claim outrunning its evidence, and the last one had a perfect existence record
- [ ] **G-22, G-15 and G-16 recorded closed** in the gap register, each against its own artifact
- [ ] Every carried item from Phases 0–5 either closed or explicitly re-carried with an owner — CARRY-01, CARRY-02, CARRY-04, CARRY-05, P0-02, P0-05 all land here
- [ ] Any claim narrower than its wording marked ⚠ and explained; **every quantitative claim checked against the number it cites**
- [ ] Every ADR this phase owed (ADR-023 – ADR-028, and any raised along the way) is ACCEPTED, not PENDING — checked directly against `docs/adr.md`
- [ ] New ADRs appended **before** the *Template for new entries* section — verified by line number
- [ ] **The deployed service is current**, verified by P6-10's own build-identity check rather than by hand

**Evidence:** `evidence/phase-6/p6-18-clean-clone.log`, `evidence/phase-6/INDEX.md`

---

## Phase 6 exit gate

From `plan.md` §7. Every criterion needs an artifact under `evidence/phase-6/` — a file someone else could read, **containing what it is cited for**.

- [ ] Every report reconciles to source data (P6-01 – P6-05)
- [ ] CSV round-trips through Excel without mangling (P6-06)
- [ ] Backup/restore meets the documented RPO/RTO with measured evidence (P6-07, P6-08)
- [ ] A clean installation on a fresh machine succeeds from the guide alone (P6-16)
- [ ] Every demo workstation captured, including local administrator rights (P6-13 — ADR-016)
- [ ] `ping MERCH-HOST` + validated HTTPS round trip from every workstation, network brought up from cold (P6-14 — ADR-016)
- [ ] G-22, G-15, G-16 closed in the gap register (P6-18)
- [ ] `report-specification.md`, `backup-restore-guide.md`, `user-guide.md` written; `installation-guide.md` current (P6-01, P6-09, P6-15, P6-16)
- [ ] Clean-clone build and both test suites green (P6-18)
- [ ] *(CLAUDE.md §9)* Every task done — no card left 🟡 and rounded up

**Carried, not owed here:** P0-07's structure box closes in **Phase 7** with `test-plan.md`. Everything else carried from Phases 0–5 is owed at **this** gate — P0-02 (P6-13), P0-05 (P6-14), CARRY-01 (P6-12), CARRY-02 (P6-07), CARRY-04 (P6-17), CARRY-05 (P6-10). **Nothing carried into Phase 6 may be carried out of it without an ADR saying why**, in the ADR-016 shape — that mechanism exists, has been used once honestly, and is the only acceptable route.
