# Report specification

**Status:** written at P6-01, Phase 6 Track A. **Decides:** ADR-023.

**Scope note (plan.md §5, spec §3/§29).** This system is an **academic prototype**, built under
binding course constraints (Visual Basic .NET, MariaDB via XAMPP) for a course deliverable, not
a production-readiness claim, and every report this document defines inherits that framing.
Reports here provide **operational information**, per spec §14 —
"reports provide operational information rather than accounting statements" — and nowhere in
this document, or in any report built against it, is a figure to be represented as an audited
accounting statement, a settlement confirmation, or a legal record. Where a value is an
estimate rather than a fact captured from a committed transaction, this document says so, and
the report itself must say so too (see §5).

**Purpose.** Spec §14 requires every report to define its date filter in the store time zone,
show the selected date range, and state whether returns/cancellations are included or
excluded. `plan.md` §7 adds the phase's own key design call: *"every report ships with a
reconciliation test asserting the report total equals the sum from the corresponding detail
screen for the same filter."* This document is the **single place** those decisions get made
for all twelve of spec §14's reports. Track B (P6-02 – P6-05) implements the reports; every
one of those cards cites this document instead of re-deciding any of the questions below.

---

## 1. Scope

Spec §14 names exactly twelve reports. Every row below is one of them; nothing is added and
nothing is dropped. CSV export (P6-06) is a separate concern — this document defines report
*content*, not export encoding.

| # | Report | Track B card |
|---|---|---|
| 1 | Daily sales summary | P6-02 |
| 2 | Sales by product | P6-02 |
| 3 | Sales by cashier | P6-02 |
| 4 | Payment-method summary | P6-02 |
| 5 | Returns and cancellations | P6-03 |
| 6 | Purchase-order history | P6-04 |
| 7 | Goods-receiving history | P6-04 |
| 8 | Current stock | P6-05 |
| 9 | Low-stock report | P6-05 |
| 10 | Stock movement report | P6-05 |
| 11 | Stock-adjustment report | P6-05 |
| 12 | Product performance summary | P6-03 |

---

## 2. The store-local day boundary — with real timestamps

**The rule.** Every timestamp in this system is stored in UTC (`DATETIME(6)`, CLAUDE.md §5).
Every report's date filter is interpreted as a **store-local calendar day**, `Asia/Manila`
(`Merchandising.Domain.StoreTimeZone`, built at P3-06 for the purchase-order history report and
reused here — this document does not reinvent it). `StoreTimeZone.Zone` resolves the IANA id
`Asia/Manila`, `BaseUtcOffset = +08:00`, with **no daylight-saving adjustment rules at all** —
confirmed at P3-06 by inspecting `TimeZoneInfo.AdjustmentRules`, empty. That means the offset
used below is the offset for every calendar date this system will ever report on; there is no
DST transition to special-case.

- `StoreTimeZone.StartOfDayUtc(date)` — the UTC instant at which `date` **begins** in
  `Asia/Manila`: local midnight, converted to UTC by subtracting the offset.
- `StoreTimeZone.EndOfDayUtcExclusive(date)` — `StartOfDayUtc(date.AddDays(1))`, the
  **exclusive** upper bound, so the filter is `CreatedAtUtc >= start AND CreatedAtUtc < end`
  and the whole of the requested local day is covered with nothing from the next one leaking
  in.

**Worked example, store-local date `2026-08-30`:**

| Boundary | Local instant | UTC instant |
|---|---|---|
| `StartOfDayUtc(2026-08-30)` | 2026-08-30 00:00:00 | **2026-08-29 16:00:00** |
| `EndOfDayUtcExclusive(2026-08-30)` | 2026-08-31 00:00:00 | **2026-08-30 16:00:00** |

So the correct UTC window for store-local `2026-08-30` is
`[2026-08-29T16:00:00Z, 2026-08-30T16:00:00Z)`.

**The 23:59:59 case, checked rather than assumed.** A sale at store-local `2026-08-30 23:59:59`
converts to UTC `2026-08-30 15:59:59` — inside the window above, correctly attributed to
`2026-08-30`. This is a genuine and worthwhile assertion: it proves the upper bound is
correctly **inclusive** of the last second of the local day (an off-by-one in
`EndOfDayUtcExclusive` — for example computing it from the *same* date instead of `date + 1`
— would wrongly push this sale into the next day's report). It is asserted directly against
`StoreTimeZone` in `Merchandising.Tests.Unit.StoreTimeZoneBoundaryTests`.

**What that example does *not* demonstrate, stated plainly rather than glossed over.** Because
`Asia/Manila` is UTC**+8**, a store-local `23:59:59` timestamp's UTC instant falls on **the
same UTC calendar date** — `2026-08-30 15:59:59` is still `2026-08-30` in UTC. A naive,
incorrect implementation that grouped by the raw UTC calendar date of `CreatedAtUtc` (for
example a bare `DATE(CreatedAtUtc) = '2026-08-30'`, skipping the `Asia/Manila` conversion
entirely) would, for this particular timestamp, happen to agree with the correct answer. **The
divergence `plan.md` §7 point 2 warns about — "a day boundary converted in the wrong direction
moves a sale between reports" — actually occurs for early store-local hours, `00:00:00` through
`07:59:59`.** Worked example: a sale at store-local `2026-08-30 02:00:00` converts to UTC
`2026-08-29 18:00:00`. The correct store-local day is `2026-08-30`; a naive UTC-calendar-date
grouping would read this sale's UTC date as `2026-08-29` and silently drop it from the
`2026-08-30` report (or wrongly credit it to `2026-08-29`'s report instead). This is the actual
failure mode every Track B report's date filter must avoid, and it is what
`ReportReconciliationHarnessTests` (§7 below) exercises with these exact numbers.

**Practical instruction to Track B:** always compute the UTC window through
`StoreTimeZone.StartOfDayUtc`/`EndOfDayUtcExclusive` from a parsed store-local `DateOnly`, the
same way `PurchaseOrdersController.GetPurchaseOrderHistory` and `InventoryController`'s
stock-movement search already do. Never derive a report's date filter from
`DATE(CreatedAtUtc)`, `CreatedAtUtc.Date`, or any other expression that reads the UTC
timestamp's own calendar date directly.

---

## 3. Filter, pagination, and sorting semantics — inherited from P3-03, not restated

Every report's `page`/`pageSize`/`sort` contract is the one already documented in
`docs/api-specification.md` (`GET /api/v1/purchase-orders` and
`GET /api/v1/purchase-orders/history`, P3-03/P3-06): `page` defaults to 1 and clamps up;
`pageSize` defaults to 25, clamps below-1 values to the default, and clamps values above
`maxPageSize` (100) **down**, never honouring them; `sort` is `field` or `field:direction`
against a per-endpoint whitelist, with any other field or direction refused rather than
silently ignored. Track B reports use this contract as-is. A report needing a filter this
contract does not already cover (for example a product or supplier filter) adds that one
parameter; it does not redefine paging or sorting.

Date-range parameters (`fromDate`/`toDate`, each `yyyy-MM-dd`) follow
`PurchaseOrderHistoryResponse`'s existing pattern exactly: each is optional and independent —
omitting one leaves that side unbounded, never defaulting to "today" — and when both are
given, `toDate` before `fromDate` is `VALIDATION_FAILED`.

---

## 4. Returns/cancellations treatment, per report

Spec §14: every report must "indicate whether returns/cancellations are included or excluded."
`Merchandising.Domain.Reporting.ReturnsTreatment` (`Included` / `Excluded`) is the value every
report response states, via `Merchandising.Contracts.Reporting.ReportRangeEnvelope
.ReturnsTreatment` — never left for the reader to infer from the numbers.

| Report | Treatment | Why |
|---|---|---|
| Daily sales summary | **Included** | Spec §14 names it explicitly: "completed sales, completed returns, net sales total." |
| Sales by product | **Included** | Spec §14 explicitly: "quantity sold, returned quantity, net quantity." |
| Sales by cashier | **Included** | Spec §14 explicitly: "completed sales, returns, net value." |
| Payment-method summary | **Excluded** | Spec §14 defines this report as "recorded cash, card, and e-wallet totals" for **sale payments**. A sales return's `RefundMethod` (`Merchandising.Contracts.Sales.SalesReturnResponse`) is a separate recorded fact, reported by the Returns and cancellations report (row 5) rather than netted in here — netting a refund against a sale payment total would require a decision about which SIDE of a still-open cashier session absorbs it, which spec §14 does not make and this document is not the place to invent. |
| Returns and cancellations | **Included** | The report's entire subject is return/cancellation rows; "included" is stated for consistency of the response contract, not because a decision was made — there is nothing to exclude. |
| Purchase-order history | **Excluded** | No sales-returns dimension applies. Purchase *returns to a supplier* are a distinct transaction type (`PurchaseReturnsManage`) that spec §14 does not list as one of the twelve reports; a future report covering them is a new decision, not this document's to make. |
| Goods-receiving history | **Excluded** | Same reasoning as purchase-order history — no sales-returns dimension. |
| Current stock | **Excluded** | A point-in-time balance snapshot; returns already affected the balance through `StockMovements`, and are not separately broken out here. |
| Low-stock report | **Excluded** | Same as current stock — it reads the current balance, which already reflects any return-driven restock. |
| Stock movement report | **Included** | A return that restocks an item (`RestocksItem = True`, P5-11) produces its own `StockMovements` row with its own reason and correlation identifier; the report is a ledger of every movement type and excludes nothing. |
| Stock-adjustment report | **Excluded** | Adjustments and sales returns are distinct transaction types in this data model; an adjustment report row is never produced by a sales return. |
| Product performance summary | **Included** | Spec §14 explicitly: "net quantity, net sales value" — net implies returns are netted into both figures. |

---

## 5. Cost basis — captured, never current

**The rule, stated once.** Every monetary figure in a Track B report that touches a
product's cost reads the **cost value captured on the originating transaction row at the time
of that transaction** — never the product's current `Products.Cost`. This was P5-01/P5-07's key
design call for Sales specifically ("sale lines capture the *effective* unit price and cost at
sale time... easy to get right now and very hard to fix later," `plan.md` §7 Phase 5) and it is
already the pattern for every transaction type that carries a cost, confirmed by inspection
before this card wrote a line of report code:

| Captured cost field | Transaction |
|---|---|
| `Merchandising.Contracts.Sales.SaleLineResponse.Cost` | Sale line, at sale time |
| `Merchandising.Contracts.Procurement.PurchaseOrderHistoryItemResponse` (`OrderedValue`/`ReceivedValue`, from `PurchaseOrderLines.PurchaseCost`) | Purchase order line, at order time |
| `Merchandising.Contracts.Receiving.ReceiptLineResponse.Cost` | Receipt line — the actual received/invoiced cost, which can differ from the order's agreed `PurchaseCost` |
| `Merchandising.Contracts.Receiving.PurchaseReturnLineResponse.Cost` | Purchase return line, copied from the originating receipt |

**A report join that reads `Products.Cost` for any of these figures is a defect**, exactly the
one `plan.md` §7 named as "very hard to fix later" if it ever ships — see the Product
performance summary's "recorded cost estimate" (spec §14) and every Sales-by-product/cashier
figure in Track B: all of them read a captured column, never `Products.Cost`. P6-02's own
acceptance criteria assert this directly by changing a product's cost after a sale and
confirming the report is unmoved.

**"Informational margin estimate."** Spec §14 requires the product performance summary's
margin figure to be labelled **informational** wherever it appears — this is a merchandising
prototype's estimate from captured cost and captured sale price, not an accounting margin
statement (§29). The label is the entire mitigation; P6-03 asserts the literal word appears
next to the figure.

---

## 6. Rounding

Every value a Track B report sums is already at its storage scale — `DECIMAL(19,4)` for money,
`DECIMAL(19,3)` for quantity (ADR-004) — because `DecimalScaleGuard`/`SaleLine` round exactly
once, at the point each row is written (ADR-004.1, ADR-022). A report's `SUM()` over
already-correctly-scaled `DECIMAL` columns in MariaDB stays exact at that scale; **no report
computation introduces a second rounding step.** The one place a report computes a *new*
derived figure not already stored anywhere (the product performance summary's informational
margin estimate) rounds through `DecimalScaleGuard.RoundMoney`, `AwayFromZero` — the same
default ADR-004.1 sets everywhere outside the POS sale-arithmetic carve-out ADR-022 made, which
does not apply here (no report performs POS sale-total arithmetic).

---

## 7. The reconciliation harness

`plan.md` §7's key design call, restated precisely: *"every report ships with a reconciliation
test asserting the report total equals the sum from the corresponding detail screen for the
same filter."* **The detail side must be the existing detail endpoint a user can already open,
summed by the test itself — never the report's own query, run a second time.** A test that
calls one SQL statement twice and compares the answer to itself proves the statement is
deterministic, nothing more; it would pass on a report that is confidently wrong.

`Merchandising.Tests.Integration.ReportReconciliationHarness` is the one place this assertion
is written:

```vb
ReportReconciliationHarness.AssertReconciles(
    reportTotal, detailTotal, "Daily sales summary", "Sales detail sum", "date=2026-08-30")
```

- `Reconciles(reportTotal, detailTotal)` — a pure predicate, exact `Decimal` equality, no
  tolerance (the same rule `LedgerReconciliation`/ADR-021 already applies to the stock
  ledger — every value on both sides is already `DECIMAL(19,4)`/`DECIMAL(19,3)`, so there is
  nothing for a floating-point tolerance to paper over).
- `AssertReconciles(...)` — asserts `Reconciles` is true, with both totals, both labels, and
  the filter under test named in the failure message.

**Which "detail endpoint" a Track B card reconciles against:**

| Report | Detail endpoint |
|---|---|
| Purchase-order history, Goods-receiving history | `GET /api/v1/purchase-orders/history` (existing, P3-06) |
| Current stock, Low-stock, Stock movement, Stock-adjustment | `GET /api/v1/inventory/stock`, `GET /api/v1/inventory/stock/movements` (existing, P3-06/P4-xx) |
| Daily sales summary, Sales by product, Sales by cashier, Payment-method summary, Returns and cancellations, Product performance summary | **No detail endpoint exists yet.** Only `POST` routes exist today for sales (`SalesController`), sales returns (`SalesReturnsController`), and cashier sessions (`CashierSessionsController`) — confirmed by inspection before this card was written; there is no `GET` list/detail route for any of them. **P6-02 and P6-03 must add the detail endpoint each of their reports reconciles against, as part of those cards, not as a separately-scoped prerequisite.** This is a genuine scope addition to those two cards, flagged here rather than discovered mid-card. |

**Proven falsifiable before any real report uses it** (P6-01's own acceptance criterion).
Because no real report exists yet, `ReportReconciliationHarnessTests` proves the mechanism with
two synthetic scenarios built from real arithmetic rather than a live report:

1. **The off-by-one date boundary** — the exact `2026-08-30 02:00:00`/`14:00:00` worked example
   from §2, computed through `StoreTimeZone`. A deliberately wrong "report total" (grouping by
   raw UTC calendar date) is asserted **not** to reconcile against the correct detail total, and
   `AssertReconciles` is asserted to throw `AssertFailedException` when fed the two.
2. **The current-cost join** — two units at captured unit cost 250.0000 (detail total 500.0000)
   against the same two units re-priced at a current unit cost of 300.0000 after a later change
   (report total 600.0000, the §5 defect). Asserted not to reconcile, and `AssertReconciles`
   asserted to throw.

Both scenarios are watched-fail proof that the harness's comparison — not merely its
existence — actually detects the two defect classes this phase is most exposed to.

---

## 8. Permission

Every one of the twelve reports requires `Reports.View` (`PolicyRegistry.Names.ReportsView`,
Admin-and-above — SuperAdmin, Admin). This policy was pre-registered at P2-02 with no live
consumer until Track B; spec §9 assigns reports to Admin, and no report in this document
carries a narrower or wider requirement than that one policy. Per this phase's own note 5: no
report needs a new grant — `merch_api` already holds database-level `SELECT` (ADR-013), which
is everything a read-only report requires.

---

## 9. Evidence

`evidence/phase-6/p6-01-report-definitions.txt` — guardrail run, both test suites green
(including the watched-fail proofs in §7), and this document's own drift check
(`Merchandising.Tests.Unit.ReportSpecificationDocumentationTests`) passing.
