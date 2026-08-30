# tasks.md — Phase 5 (POS)

**Scope:** current phase only. Regenerated at each phase entry from `plan.md`.
**Rules:** one task = one commit, prefixed with the task ID. Never tick a `Done when` box on a failing test or a partial implementation. Stop conditions are in `CLAUDE.md` §7.

**Legend:** ⬜ not started · 🟡 in progress · ✅ done · 🔴 blocked

---

> ## Phase 4 closed 2026-08-29 — PASS, at commit `76aaa7d`
>
> Fifteen cards. A clean clone outside the tree, with **0** `bin`/`obj` at clone time, restores eleven projects from nothing, builds at **0 warnings / 0 errors**, and passes guardrails G-A–G-D plus **47/47 unit and 348/348 integration tests, 0 skipped**, against the real pinned MariaDB 10.4.32. **G-12 closed for receiving, returns, counts and adjustments; G-21 closed.** Full record: `evidence/phase-4/INDEX.md`.
>
> **The gate returned FAIL on its first sitting — for the second phase running — and both failures were predicted in writing by the phase's own closure pack.** `evidence/phase-4/INDEX.md` §4.1 and §4.2 named them rather than rounding them up, and the review's job was to refuse to wave them through:
>
> 1. **Exit criterion 5 cited two evidence files that never fired the scenario.** "Concurrent receive-and-adjust on the same product is safe" rested on an argument by analogy from P1-13 and P4-08. P4-15 wrote the test: 4 receives and 4 adjustments in flight settle to the exact arithmetic total, and a +5.000/−5.000 race over 6 rounds lands on one of only two consistent end states with **both orderings genuinely observed**.
> 2. **P4-12 had an open box, and a real defect was sitting under it.** The authenticated pass found the deployed `MerchandisingApi` Windows Service was a **2026-08-28 (Phase 3) build returning 404 for every Phase 4 route**. `WebApplicationFactory` builds the host in-process and can never see a stale deployment; the suite would have passed with no service installed at all.
>
> **Two lessons Phase 5 inherits, stated as rules.**
>
> **A test that has never been watched fail is not a check — and neither is one that *cannot* fail.** P4-15's first draft of the ordering test passed and proved nothing: a never-received product has no `StockBalances` row, so row-*absence* refused the adjustment and the `Quantity >= @qty` guard was never reached; and a *transient* negative balance is erased by the later receive, so no assertion on the final balance could see it. Both were only found by deliberately breaking `StockRepository` and discovering the test stayed green. **Break the mechanism, watch the test fail, then trust it.** Phase 5's concurrency and idempotency cards are where this bites next.
>
> **Green tests say nothing about what is deployed.** Every suite in this repository reaches the API in-process. Phase 5 adds the POS client, which a classmate will run against a *deployed* service on demo day. Redeploy before believing a client-side result, and treat "the service is Running" as unrelated to "the service is current".
>
> **Frozen: no phase below may re-litigate a Phase 1–4 decision.** The connector, transaction pattern (ADR-006, and its P4-04 amendment that isolation must be passed per `BeginTransaction`), auth scheme, error envelope (ADR-014), grant model (ADR-013), policy naming and self-approval (ADR-017), the barcode rule (ADR-018), the purchase-order status machine (ADR-020), and the ledger reconciliation assertion (ADR-021) are settled. Phase 5 consumes them.

---

# Carried forward — open items from earlier phases

These are carried, not reopened. Each says which gate now owns it.

---

### 🟡 P0-02 · Windows baseline for every demo workstation — **owed at the Phase 6 gate (ADR-016)**

Three demo workstations unsurveyed. `scripts/setup-client.ps1 -CaptureOnly` collects every field itself. **The one part worth doing today, out of band:** ask each classmate whether they hold **local administrator rights on their own laptop**. One message, no machine needed — it is the only carried item that fails late and unfixably.

### 🟡 P0-05 · Demo network rehearsal — **owed at the Phase 6 gate (ADR-016)**

Two of its boxes need nobody but you and can still invalidate ADR-015: whether Mobile Hotspot starts **from cold with nothing to share**, and whether this adapter sustains station + Wi-Fi Direct GO concurrently under load. ADR-015 was accepted on a capability *reading*, not a cold start.

### 🟡 P0-07 · Repository structure — **the last box closes in Phase 7**

Three `docs/*.md` remain: ~~`api-specification.md` → P3-08~~ **written**; ~~`database-design.md` finalised → P4-13~~ **written**; `ui-specification.md` → **P5-14, this phase**; `backup-restore-guide.md` + `user-guide.md` → Phase 6; `test-plan.md` → Phase 7.

### ⬜ CARRY-01 · Account recovery has no implementation and no card — **place it before Phase 6 planning closes**

**Surfaced at the Phase 2 gate** (`evidence/phase-2/INDEX.md` §4.3). Spec §23's G-19 remediation names *account recovery* among seven components; six are proven. Lockout self-recovers (`LockedUntilUtc` = `UtcNow` + 15 min), but **a forgotten password has no route**: ADR-017 §4 puts all user management on the `Merchandising.Maintenance` CLI, and that CLI has `create-user` with no `reset-password` and no `unlock-user`.

> **Now carried through three phases without an owner (2 → 3 → 4), and this is the fourth.** Not a Phase 5 blocker. The natural home is **Phase 6** (operations, beside backup/restore) as a `reset-password` subcommand — roughly one card. **`tasks.md` said at Phase 4 entry "do not let it reach a third." It has. Place it when Phase 6 planning opens, or accept that a classmate who forgets a password on demo day has no route back in.**

### ⬜ CARRY-02 · Off-host backup copy, volume present — **owed at the Phase 6 gate (ADR-019)**

No test asserts `OffHostPath` when a `MERCHBACKUP` volume *is* attached. Artifact-backed only. Phase 6 promotes backup to production quality (`plan.md` §7, closing G-15/G-16) and owes the assertion then.

### ⬜ CARRY-04 · Intermittent `MSB3030` on a clean-clone build — **watch, no owner yet**

**Observed once at the Phase 3 gate**, and not reproduced since across eleven further clean-clone builds spanning three commits (`p3-09`, `p4-14`, `p4-15` logs). `Merchandising.Tests.Unit` references two `OutputType=WinExe` projects, and in a parallel build the referenced executable's `runtimeconfig.json` can be listed as a copy-local item before the task that generates it has run. Pre-existing fragility, not a regression. **Raise a card only on a second sighting.**

### ⬜ CARRY-05 · Nothing detects a stale deployment — **new at the Phase 4 gate, owed at the Phase 6 gate**

**Found by P4-15's authenticated pass** (`evidence/phase-4/p4-15-authenticated-client-pass.txt` §2). The deployed Windows Service was four weeks of work behind the source and served 404 for every Phase 4 route, and **no mechanism in this repository could have noticed**: the integration suite builds the host in-process, and `scripts/install-service.ps1` verifies that *something* is listening on 8443, not that what is listening is current.

> Phase 6 builds health monitoring and release packaging (`plan.md` §7, closing G-15/G-16). A **commit identifier and build timestamp on a health endpoint**, compared against the tree, turns this into a one-line check — and turns "the demo laptop is running last month's build" from a silent failure into a visible one. Recorded, not assigned, because Phase 6 planning has not opened. **This phase must not close without a redeploy before the POS client is believed** — see the Phase 4 closure note's second lesson.

---

# Phase 5 — POS

**Entry:** Phase 4 gate passed. **Closes:** G-24.
**Docs produced:** `docs/ui-specification.md` (all three clients).

*This phase inherits the Phase 1 transaction pattern and the Phase 4 ledger invariant wholesale. Do not invent a second one, and do not weaken the reconciliation assertion to make a sale fit.*

**What already exists, so no card below rebuilds it.** `StockBalances`, the append-only `StockMovements` ledger and `StockRepository.TryDecrementAsync`'s conditional decrement — proven atomic and concurrency-safe at P1-11/P1-13 and again under receive-vs-adjust contention at P4-15. The product master with SKU and optional barcode (`0006`, ADR-018), suppliers (`0007`), purchase orders (`0008`), receiving and purchase returns (`0009`), counts and adjustments (`0010`). The audit pipeline (P2-04), the error envelope (ADR-014), idempotency (ADR-007), `SystemSettings` (P2-06), the ledger reconciliation assertion (P4-01/ADR-021), and **every policy this phase needs is already registered**: `Sales.Create`, `CashierSessions.Manage`, `SalesReturns.Create`, `SalesReturns.ApproveExceptional`, `Stock.Read`, `Products.Read`.

> ### Five things that will bite in this phase specifically
>
> **1. The sale line captures the *effective* price and cost AT SALE TIME, and this is the phase's key design call.** `plan.md` §7: *"A later price change must not retroactively alter historical sales analysis. This is easy to get right now and very hard to fix later."* A sale line that stores a `ProductId` and joins to `Products.Price` for reporting is the defect this warns about, and it will not show up until Phase 6's reports disagree with the receipts. That is P5-01 and P5-07.
>
> **2. Money is `DECIMAL(19,4)` and change calculation must be exact — never `Double`, never `Single`, anywhere.** CLAUDE.md §5. And per CLAUDE.md §6.3, **decimal *scale* is not enforced by the database even under strict mode**: `1.99999` into a `DECIMAL(19,4)` stores `2.0000` and reports success. Validate scale at the API boundary before the parameter is bound (ADR-004.1). A correctly-scaled stored value proves nothing on its own.
>
> **3. The sale is the biggest transaction in the system — seven effects, not five.** Spec §11: sale header, sale lines, payment record(s), stock-out movements, balance changes, cashier-session totals, audit. All seven commit or none do. Receiving's five-effect forced-failure test (P4-05) is the shape; this one has two more ways to be half-done.
>
> **4. Next free numbers: migrations `0011`, `0012`; grants `0013`, `0014`.** Install order is always migration → grants; MariaDB 10.4 rejects a table-level `GRANT` naming a table that does not exist (`ERROR 1146`). Table names in grant files are **lowercase**. A new table is `SELECT`-only until its grants file adds writes back per table (ADR-013), and `StockMovements`/`AuditLogs` stay absent from every grants file forever. **`Sales` and `SalePayments` are append-only too** — a completed sale is never edited (spec §10.3), so argue hard before granting `UPDATE` on either.
>
> **5. G-24 is a wording gap, and wording is the only thing that closes it.** *"Payment recording could be mistaken for payment authorization."* The system must never imply a card or e-wallet payment was authorised by anyone. That is a claim about **UI labels and report column headers**, provable only by reading them — so P5-12 asserts the strings by test, not by looking at a screenshot.

---

## Track A — The money primitive, before anything computes a total

*Independent of the schema. Can run in parallel with Track B.*

### ✅ P5-01 · Fixed-precision sale arithmetic in `Domain`, with no database at all 🎯

**Spec:** §10.3, §11 · **Decides:** ADR-022
**Files:** `src/Merchandising.Domain/Sales/`, `src/tests/Merchandising.Tests.Unit/SaleArithmeticTests.vb`

**Do:** Line totals, sale total, cash tendered vs. total, and change — all `Decimal`, all exact at the stored scale, all in `Domain` where they can be tested exhaustively without a server. `plan.md` §7 names the price-capture rule the phase's key design call; this card builds the type that *carries* the captured price so a later card cannot accidentally re-read it from `Products`.

**Done when:**

- [x] Every value is `Decimal`. No `Double` or `Single` appears anywhere in the file — asserted by a source scan in the test, not by inspection, because this is the rule that is easiest to break by accident and hardest to see
- [x] Rounding is explicit and uses the `currency.roundingPolicy` `SystemSettings` value (P2-06), never an implicit default — and a changed policy changes the result, asserted
- [x] Change = tendered − total, exact to `DECIMAL(19,4)`; tendered < total is a refusal, not a negative change
- [x] Scale validation refuses an over-scale input **before** it could be silently rounded (ADR-004.1, CLAUDE.md §6.3) — proven with `1.99999` against a 4-scale field
- [x] A sale line holds its **captured** unit price and cost as data. A test constructs a line, then changes the product's price, and asserts the line is unmoved — the P5-07 defect, caught in `Domain` where it is cheap
- [x] Boundary cases enumerated, not spot-checked: zero-quantity line refused, exact tender giving `0.0000` change, the largest total the column allows
- [x] ADR-022 records the rounding policy, where rounding happens, and why it is not the database's job
- [x] Unit suite green; guardrails pass; build 0 warnings

**Evidence:** `evidence/phase-5/p5-01-sale-arithmetic.txt`

---

## Track B — Schema

*Sequential. Migration then grants, and `0011` before `0012`.*

### ✅ P5-02 · Migration 0011 — `CashierSessions`, `Sales`, `SaleLines`, `SalePayments`, and grants 0013

**Spec:** §10.3, §11, §12 · **Files:** `db/migrations/0011_pos.sql`, `db/grants/0013_pos-grants.sql`

**Do:** The cashier session (opening float, opened/closed by, timestamps UTC, status), the sale header (session, cashier, total, status, timestamps), its lines (product, quantity, **captured** unit price and cost), and the payment records (method, amount). Money `DECIMAL(19,4)`, quantities `DECIMAL(19,3)`, timestamps `DATETIME(6)` UTC, `COLLATE utf8mb4_unicode_ci` stated explicitly on every table.

**Done when:**

- [x] All four tables created; foreign keys to `Products`, `Users` and each other **prevent** deletion of a referenced row, proven with `ERROR 1451` rather than asserted by inspection, attempted as `merch_migrator` so the refusal is the constraint and not the grant
- [x] `SaleLines` carries its own `UnitPrice` and `Cost` columns — **not** a join to `Products`. Asserted from `information_schema`, because this is the one schema decision the whole phase's reporting integrity rests on
- [x] Payment method stored as a stable identifier the Domain enum maps to, `COLLATE utf8mb4_bin` on that column so the `CHECK` cannot accept `'cash'` under the table's case-insensitive collation — the P3-02 / P4-03 defect, which has now been found twice and must not be found a third time
- [x] Applies clean as `merch_migrator` on a database already carrying `0001`–`0010`; runner applies it exactly once
- [x] `db/grants/0013` applied **after**, lowercase table names, and **argue per table in its own header why any `UPDATE` exists at all**. A completed sale is never edited (spec §10.3) — nothing at all for `stockmovements` or `auditlogs` (ADR-013)
- [x] Round-trip test: `0.0001` and `123456789012345.6789` exact through the new decimal columns, **plus** the declared column types read back from `information_schema`
- [x] Integration suite green against pinned MariaDB

**Evidence:** `evidence/phase-5/p5-02-pos-schema.txt`, `evidence/phase-5/p5-02-pos-grants.txt`

### ⬜ P5-03 · Migration 0012 — `SalesReturns`, `SalesReturnLines`, and grants 0014

**Spec:** §10.3, §11, §12 · **Files:** `db/migrations/0012_sales-returns.sql`, `db/grants/0014_sales-returns-grants.sql`

**Do:** The return header (original sale, returned-by, approver where exceptional, reason, timestamps UTC) and its lines (original sale line, quantity returned, **stock-eligibility flag**, operational payment-reversal record).

**Done when:**

- [ ] The stock-eligibility flag is a real column, not inferred — a returned item may be damaged and must not re-enter stock, and spec §10.3 requires that recorded per line
- [ ] `ReturnedByUserId` and `ApprovedByUserId` are separate columns with separate FKs, approver nullable — `SalesReturns.ApproveExceptional` is a distinct policy from `SalesReturns.Create` and cannot be compared if they are one field. Asserted from `information_schema`
- [ ] A return line's FK to `SaleLines` prevents deleting a sale line, proven with `ERROR 1451`
- [ ] Applies clean as `merch_migrator` on a database carrying `0001`–`0011`; applied exactly once
- [ ] `db/grants/0014` applied after, lowercase, justified per table in its own header
- [ ] Integration suite green

**Evidence:** `evidence/phase-5/p5-03-returns-schema.txt`, `evidence/phase-5/p5-03-returns-grants.txt`

---

## Track C — Cashier sessions

*Sequential. Runs after Track B. A sale cannot exist without an open session, so this precedes Track D.*

### ⬜ P5-04 · Open and close a cashier session; a sale requires an open one

**Spec:** §10.3 · **Files:** `src/Merchandising.Api/Controllers/CashierSessionsController.vb`, `src/Merchandising.Api/Sales/CashierSessionService.vb`, `src/Merchandising.Infrastructure/Data/CashierSessionRepository.vb`, `src/Merchandising.Contracts/Sales/`

**Do:** `POST /api/v1/cashier-sessions` opens a session with a declared opening float; `POST /api/v1/cashier-sessions/{id}/close` closes it. `CashierSessions.Manage`. The session is the thing a sale attaches to and the thing daily closing reports on.

**Done when:**

- [ ] One cashier cannot hold two open sessions at once — enforced by a **unique index**, proven by a concurrent double-open that bypasses any API check (the P2-07 / P3-02 / P4-02 shape), `success | ERROR 1062`
- [ ] Opening float is validated for scale at the API boundary before binding (ADR-004.1)
- [ ] A closed session is immutable and remains fully readable
- [ ] Open and close each write an audit row with the actor and correlation ID
- [ ] **Matrix suite extended** for every route added — `CashierSessions.Manage` positive and negative cells, 403 not 401/404
- [ ] Integration suite green

**Evidence:** `evidence/phase-5/p5-04-cashier-sessions.txt`

### ⬜ P5-05 · Daily closing — declared vs. calculated, with variance

**Spec:** §10.3, §14 · **Files:** `src/Merchandising.Api/Sales/CashierSessionService.vb`, `src/Merchandising.Infrastructure/Data/CashierSessionRepository.vb`

**Do:** Closing records session totals **by payment method**, declared cash, calculated cash, the variance between them, closing user and timestamp. The calculated figure is derived server-side from committed rows and never from a client-supplied total.

**Done when:**

- [ ] Calculated cash is computed from committed `SalePayments` rows, never from a client figure — asserted by sending a wrong client total and confirming it is ignored, not merely by not sending one
- [ ] Variance is stored as declared − calculated **at closing time**, not recomputed later, for the same reason P4-03's count variance is
- [ ] Totals are broken down by payment method, and card/e-wallet rows are labelled as **recorded, not authorised** (G-24 — the wording is P5-12's to assert, the data shape is this card's)
- [ ] A session with no sales closes cleanly with zero totals rather than failing
- [ ] **Matrix suite extended**; integration suite green

**Evidence:** `evidence/phase-5/p5-05-daily-closing.txt`

---

## Track D — The atomic sale

*Sequential. One worker, in order — each card consumes the last. This is the track the phase exists for.*

### ⬜ P5-06 · Fast product lookup by SKU, barcode, or name

**Spec:** §10.3, §13 · **Files:** `src/Merchandising.Api/Controllers/ProductsController.vb`, `src/Merchandising.Infrastructure/Data/ProductRepository.vb`

**Do:** One lookup a cashier can drive from the keyboard: exact SKU, exact barcode (ADR-018's rule stands — do not re-decide it), or partial name. Shapes the query the POS client types into.

**Done when:**

- [ ] Exact SKU and exact barcode match before any partial-name match, so a scanned barcode never returns a list
- [ ] Inactive products are excluded by default and the response says so — an inactive product must be refused by the sale command anyway (spec §11), and a cashier should not be able to add one to a cart in the first place
- [ ] Available stock is returned with each hit, so the client shows it without a second call
- [ ] Pagination, max page size, sorting and filtering consistent with P3-03's definitions
- [ ] Measured: the lookup returns within a stated budget against the seeded catalogue, with the number recorded rather than described
- [ ] **Matrix suite extended** — `Products.Read`; integration suite green

**Evidence:** `evidence/phase-5/p5-06-product-lookup.txt`

### ⬜ P5-07 · The atomic sale — seven effects, one transaction 🎯

**Spec:** §10.3, §11 · **Closes:** G-12 (sale component) · **Files:** `src/Merchandising.Api/Controllers/SalesController.vb`, `src/Merchandising.Api/Sales/SaleService.vb`, `src/Merchandising.Infrastructure/Data/SaleRepository.vb`, `src/Merchandising.Contracts/Sales/`

**Do:** `POST /api/v1/sales` against an open cashier session. **This is the card the phase exists for.** One transaction commits the sale header, its lines, the payment record(s), one `StockMovements` row per line, the conditional balance decrement, the session totals, and the audit row — or none of them.

**Done when:**

- [ ] The API re-checks **everything** before committing: product activity, current price, available stock, duplicate request state, payment validity (spec §10.3). Each re-check has its own test, and each has its own stable error code
- [ ] Each sale line stores the **effective unit price and cost at sale time**, taken server-side. A test changes the product's price after the sale and asserts the line is unmoved — the defect `plan.md` §7 names as very hard to fix later
- [ ] Stock decrements use ADR-006's conditional update with the affected-row count verified before returning success — never a read-then-write, and never a new second pattern
- [ ] All **seven** effects commit together or not at all, proven by a **forced-failure test** in the P1-12 / P2-08 / P4-05 shape
- [ ] For cash, tendered < total is refused with a stable code; change is exact per P5-01 and stored, not recomputed on read
- [ ] Card/e-wallet is **recorded**, and the response says recorded — never "approved", "authorised", or "accepted" (G-24)
- [ ] The P4-01 reconciliation passes after the sale, asserted **in this card's own test**, not only by the suite-wide fixture
- [ ] **Matrix suite extended** — `Sales.Create` positive and negative cells; integration suite green

**Evidence:** `evidence/phase-5/p5-07-atomic-sale.txt`

### ⬜ P5-08 · Negative stock impossible under concurrent load

**Spec:** §11 · **Files:** `src/tests/Merchandising.Tests.Integration/SaleConcurrencyTests.vb`

**Do:** The exit criterion, fired rather than argued. Concurrent sales of the same product cannot oversell it, and a sale racing a receive or an adjustment cannot produce a negative balance or ledger drift.

**Done when:**

- [ ] N simultaneous sales of the last remaining unit: exactly one succeeds, every other gets the controlled insufficient-stock outcome, never an unexpected exception (the P1-13 / P4-08 / P4-15 shape — launch every task, then await)
- [ ] Sale racing **receive**, and sale racing **adjustment**, both fired at one product — the pairs P4-15 explicitly did not claim and left to this phase (`p4-15-concurrent-receive-and-adjust.txt` §6)
- [ ] **No movement row ever records a `QuantityAfter` below zero**, asserted over the append-only ledger — not only that the final balance is non-negative. P4-15 §2.1(b): a transient negative is erased by a later movement and the final balance cannot see it
- [ ] **Watched fail.** Break the conditional decrement's guard, confirm the tests fail naming the negative, revert, confirm green — and heal any drift the broken run wrote with **compensating movements only**, never an edit or delete (CLAUDE.md §7 item 7; `p4-15-drift-correction.sql` is the precedent and the shape)
- [ ] Both orderings genuinely observed in the mixed races, and the distribution printed — a test that only ever sees one ordering asserts one path and claims two
- [ ] Integration suite green

**Evidence:** `evidence/phase-5/p5-08-sale-concurrency.txt` — including the induced-failure run

### ⬜ P5-09 · Idempotent retry returns the original sale, never a second one

**Spec:** §11 · **Files:** `src/Merchandising.Api/Sales/SaleService.vb`

**Do:** ADR-007's idempotency, applied to the largest transaction in the system. A repeated key returns the original committed sale — not a second sale, and not a second stock decrement.

**Done when:**

- [ ] A repeated key returns the original committed payload byte-for-byte, asserted on the response, not merely on the row count
- [ ] Exactly one stock decrement, one movement row, and one session-total update exist afterwards — asserted on all three, because a duplicate that only shows up in session totals is the one a demo would notice
- [ ] A key reused with a **different** body is refused with a stable code rather than silently replaying — a client bug must not look like success
- [ ] Two simultaneous requests carrying the same key: exactly one commits, the other replays, neither errors
- [ ] Integration suite green

**Evidence:** `evidence/phase-5/p5-09-sale-idempotency.txt`

### ⬜ P5-10 · Completed sales are immutable; cancellation only before completion

**Spec:** §10.3 · **Files:** `src/Merchandising.Api/Sales/SaleService.vb`, `db/grants/0013_pos-grants.sql`

**Do:** A completed sale is never edited or deleted. A sale may be cancelled only before completion. Corrections after completion are **returns** (P5-11), never edits.

**Done when:**

- [ ] Immutability is enforced **by database grant** where it can be, not only by policy — the `merch_api` account's write privileges on `Sales` and `SalePayments` argued explicitly in the grants file header, with the refusal proven as `ERROR 1142` by calling the production write path directly, the P4-07 / P4-08 shape
- [ ] Every route that could mutate a completed sale is refused with a stable code, asserted **over every completed state**, not spot-checked
- [ ] Cancelling a completed sale is refused; cancelling an in-progress one succeeds and leaves no stock effect
- [ ] **Matrix suite extended**; integration suite green

**Evidence:** `evidence/phase-5/p5-10-sale-immutability.txt`

---

## Track E — Sale returns

*Runs after Track D. Files overlap with Track D's service, not with Track F or G.*

### ⬜ P5-11 · Sale returns bounded by sold-minus-prior-returns, with the stock-eligibility flag

**Spec:** §10.3, §11 · **Files:** `src/Merchandising.Api/Controllers/SalesReturnsController.vb`, `src/Merchandising.Api/Sales/SalesReturnService.vb`, `src/Merchandising.Infrastructure/Data/SalesReturnRepository.vb`

**Do:** A return identifies the **original sale line**, cannot exceed quantity sold less prior returns, records whether the item may re-enter stock, and records the payment reversal **operationally** — no bank or terminal reversal happens.

**Done when:**

- [ ] The bound is computed **server-side** from committed rows, never from a client figure — the P4-08 rule, which also means the request contract carries no product or price field at all
- [ ] Returning more than sold-minus-prior-returns is refused with a stable code, including when two prior partial returns together exhaust the bound
- [ ] `RestocksItem = True` writes a stock-in movement and moves the balance; `False` writes **no movement at all** and the bound still accounts for it — both asserted, the P4-08 `RemovesStock` shape
- [ ] Concurrent returns against the same sale line cannot exceed the bound — proven under real concurrent load, not by two sequential calls
- [ ] The payment-reversal record says **recorded, not reversed at a bank** (G-24)
- [ ] `SalesReturns.ApproveExceptional` is a genuinely separate path with a second actor, reusing ADR-017 §6's `IOwnershipResource` mechanism, **not a new check** (the P3-04 / P4-10 precedent is binding)
- [ ] The P4-01 reconciliation passes after every return
- [ ] **Matrix suite extended** — `SalesReturns.Create` and `SalesReturns.ApproveExceptional`; integration suite green

**Evidence:** `evidence/phase-5/p5-11-sale-returns.txt`

---

## Track F — G-24: recording is not authorisation

*Files disjoint from every other track. Can run in parallel with Track E or G.*

### ⬜ P5-12 · Payment-method wording verified non-authorising, in UI and in reports

**Spec:** §10.3, §23 · **Closes:** G-24 · **Files:** `src/Merchandising.POS/`, `src/Merchandising.Contracts/Sales/`, `src/tests/Merchandising.Tests.Unit/PaymentWordingTests.vb`

**Do:** G-24's mitigation is *"label card/e-wallet as operational recording only; exclude terminal/bank integration; test UI wording and reports."* The last four words are the card: **assert the strings**, because a wording gap is invisible to every other kind of test.

**Done when:**

- [ ] A test scans the POS client's XAML and the sales/report contracts for a denylist of authorising words — *approved, authorised/authorized, accepted, cleared, settled, charged* — applied to card/e-wallet contexts, and **fails** if one appears
- [ ] **Proven falsifiable**: the denylist test is watched fail against a deliberately introduced "Payment approved" label before being trusted. A wording test that has never fired is exactly as decorative as any other
- [ ] The affirmative wording exists too — the UI and the response both state that card/e-wallet is **recorded** and not authorised, asserted rather than assumed absent
- [ ] The exclusion is stated where a user reads it, not only in the spec: no terminal, bank, cash drawer, scale, customer display or receipt printer integration
- [ ] **G-24 recorded closed** in the gap register at P5-15
- [ ] Unit suite green

**Evidence:** `evidence/phase-5/p5-12-payment-wording.txt`

---

## Track G — The POS client

### ⬜ P5-13 · POS WPF client reaches usable state, mouse-free

**Spec:** §10.3, §16 · **Files:** `src/Merchandising.POS/`, `src/Merchandising.ClientCommon/`

**Do:** `plan.md` §7: the POS client. Sign in, open a session, look up a product, build a cart, take a payment, complete the sale, show receipt data, take a return, close the session — every one calling the API, never the database.

**Done when:**

- [ ] All nine operations work against the **running, redeployed** API — an authenticated pass, in the P4-15 shape (`evidence/phase-4/p4-15-authenticated-client-pass.txt`). ⚠ **Redeploy the service first and record the deployed build's identity in the evidence.** CARRY-05: a green suite says nothing about what is deployed, and the Phase 4 gate found exactly that
- [ ] **Guardrail G-B holds:** no reference to `Infrastructure`, MySqlConnector, or any database package. No connection string anywhere in the project
- [ ] Server-side refusals (insufficient stock 409, tendered-below-total, closed session, over-return) surface as the API's message and error code — the client never invents its own wording or hides the correlation ID
- [ ] Client-side validation is for usability only; every rule is re-checked server-side
- [ ] **The whole sale workflow is completable without a mouse**, asserted by a test over TabIndex and access keys per screen, not by a sentence — `plan.md` §7 makes mouse-free POS its own Phase 7 criterion, and it is far cheaper to build in now
- [ ] **Keyboard navigation and focus order work at 1366×768 and 125% scaling — asserted by a test**, extending `ProcurementLayoutTests` / `InventoryLayoutTests`' three assertions to this window. **Check the arithmetic against the 1092.8 × 576.0 DIP work area**: the Phase 3 gate found a window whose own evidence file computed a number larger than the space it claimed to fit
- [ ] Guardrails and both suites green

**Evidence:** `evidence/phase-5/p5-13-pos-client.txt`

> **Client last, deliberately** (`plan.md` §8.1). Every rule this client touches is already proven server-side by Tracks C, D and E, so a defect found here is a display defect, not a business-logic one.

---

## Track H — Documents and closure

### ⬜ P5-14 · `docs/ui-specification.md` — all three clients

**Spec:** §16, §20 · **Files:** `docs/ui-specification.md`

**Do:** `plan.md` §7 places this here on purpose: *"the shared visual system is settled once POS forces the hardest layout decisions."* Document the settled system across Procurement, Inventory and POS — layout grid, typography, the status bar that carries every server message and correlation ID, error presentation, focus and keyboard conventions, and the 1366×768 @ 125% constraint.

**Done when:**

- [ ] Every convention is stated as a rule a fourth screen could be built from, not as a description of what three screens happen to do
- [ ] The 1366×768 @ 125% work area is stated **as a number** (1092.8 × 576.0 DIP), with the rule that a window's `MinWidth`/`MinHeight` must fit inside it — the Phase 3 gate's defect, written down so it cannot recur by forgetting
- [ ] Error and refusal presentation documented: the API's wording verbatim, the correlation ID always visible, never a client-invented message
- [ ] Verified against the running clients rather than transcribed by hand, in the P2-12 / P3-08 / P4-13 shape — a drift check that fails when the XAML moves
- [ ] States the academic-prototype framing required by `plan.md` §5
- [ ] One of P0-07's remaining documents struck from its list

**Evidence:** `evidence/phase-5/p5-14-ui-specification.txt`

### ⬜ P5-15 · Phase 5 closure pack

**Spec:** §20 · **Files:** `evidence/phase-5/`

**Do:** Run a clean clone outside the tree, capture the build and both suites, and write the evidence index mapping every exit criterion and card to a file that exists.

**Done when:**

- [ ] Clean clone outside the repository, **0** `bin`/`obj` at clone time, builds at **0 warnings** and passes guardrails plus both suites → `p5-15-clean-clone.log`
- [ ] `evidence/phase-5/INDEX.md` maps every Phase 5 exit criterion and every card to an artifact, continuing `phase-4/INDEX.md`'s register
- [ ] **G-24 recorded closed** in that register, and **G-12's sale component** recorded closed — the one Phase 4 explicitly left open and named as this phase's
- [ ] Any claim narrower than its wording is marked ⚠ and explained, never rounded up — **and every quantitative claim is checked against the number it cites.** Two gates running have found a real defect behind a box that was reasoned about rather than exercised; assume this one has a third
- [ ] Every ADR this phase owed (ADR-022, and any raised along the way) is ACCEPTED, not PENDING — checked directly against `docs/adr.md`, not assumed
- [ ] New ADRs appended **before** the *Template for new entries* section, not inside its fence — verified by line number
- [ ] **The deployed service is current**, and the evidence records its build identity (CARRY-05). A closure pack that certifies source nobody is running certifies nothing a classmate will see

**Evidence:** `evidence/phase-5/p5-15-clean-clone.log`, `evidence/phase-5/INDEX.md`

---

## Phase 5 exit gate

From `plan.md` §7. Every criterion needs an artifact under `evidence/phase-5/` — a file someone else could read.

- [ ] End-to-end sale flow passes (P5-07, P5-13)
- [ ] End-to-end return flow passes (P5-11, P5-13)
- [ ] Negative stock impossible under concurrent load (P5-08)
- [ ] Idempotent retry returns the original result rather than a second sale (P5-09)
- [ ] Completed sales immutable (P5-10)
- [ ] Change calculation exact to the stored precision (P5-01, P5-07)
- [ ] Payment-method wording verified as non-authorising in both UI and reports (P5-12)
- [ ] `docs/ui-specification.md` written (P5-14)
- [ ] G-24 closed, and G-12's sale component closed, in the gap register (P5-15)
- [ ] Clean-clone build and both test suites green (P5-15)
- [ ] *(CLAUDE.md §9)* Every task done — no card left 🟡 and rounded up

**Carried, not owed here:** P0-02 and P0-05 belong to the **Phase 6** gate (ADR-016). P0-07's structure box belongs to **Phase 7** — but one of its documents, `ui-specification.md`, *is* owed here as P5-14. CARRY-02 belongs to **Phase 6** (ADR-019). CARRY-04 is a watch item and becomes a card only on a second sighting. **CARRY-01 has now been carried through three phases without an owner and must be placed when Phase 6 planning opens.** **CARRY-05 is new** and belongs to Phase 6's health monitoring — but its consequence binds *this* phase at P5-13 and P5-15: redeploy before believing any client-side result.
