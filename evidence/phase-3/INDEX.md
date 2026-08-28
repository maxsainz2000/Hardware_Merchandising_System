# Phase 3 — Procurement primitives · Evidence Index

**Built at P3-09, 2026-08-28. Revised at the Phase 3 gate the same day, against commit `edc4054`.**
*The gate returned FAIL on its first sitting — P3-07's last box was carried on a claim its own
arithmetic refuted, and a real defect sat behind it. §4.3 is the record. This index describes
the tree after that was fixed.*
**Spec:** `documentations/Merchandising System for a Mid-Scale Hardware Store.md` §9, §10.1, §12, §13, §14, §20
**Plan:** `plan.md` §7 (Phase 3), §10 (risk register mapping)

---

## How to read this file

This index maps every Phase 3 exit criterion and every task card to a file that exists on
disk, and records the gap-register outcome for **G-23**. It continues
`../phase-2/INDEX.md` rather than starting a new register — the same three rules apply,
because an index is only worth as much as its weakest row:

1. **Every artifact path was checked to exist**, and to be non-empty.
2. **No row says "verified informally."** Where a claim rests on inspection rather than
   execution, the row says so and is not counted as proven.
3. **Where the evidence proves something narrower than the row's wording, the row records
   the narrower claim** — marked ⚠ rather than ✅, and explained in §4.

Status marks: ✅ proven by executed evidence · ⚠ proven, with a stated limit on the claim ·
⬜ not proven.

---

## 1. Phase 3 exit criteria → artifact

From `plan.md` §7 (Phase 3 *Exit*) and the `Phase 3 exit gate` checklist in the Phase 3
`tasks.md`.

| # | Criterion | Card | Artifact | Status |
|---|---|---|---|---|
| 1 | Every legal transition passes and every illegal one is rejected with a stable error code | P3-01, P3-04, P3-05 | `p3-01-transition-matrix.txt`, `p3-04-approval.txt`, `p3-05-cancellation.txt` | ✅ |
| 2 | A user cannot approve their own restricted order, proven end-to-end over HTTP | P3-04 | `p3-04-self-approval-denied.txt` | ✅ |
| 3 | A cancelled order cannot proceed | P3-05 | `p3-05-cancellation.txt` | ✅ |
| 4 | Approvals are attributable and audited | P3-04 | `p3-04-approval.txt` | ✅ |
| 5 | `docs/api-specification.md` procurement section written | P3-08 | `p3-08-api-specification.txt`, `../../docs/api-specification.md` | ✅ |
| 6 | G-23 closed in the gap register | P3-09 | this file, §3 | ✅ |
| 7 | ADR-020 ACCEPTED | P3-01 | `../../docs/adr.md` line 950 | ✅ |
| 8 | Clean-clone build and both test suites green | P3-09 | `p3-09-clean-clone.log` | ✅ |
| 9 | *(CLAUDE.md §9)* Every task done — P3-07 was the one open card | P3-07 | `p3-07-focus-order.txt`, §4.3 | ✅ |

---

## 2. Task card → evidence

| Card | Title | Artifact | Status |
|---|---|---|---|
| P3-01 | Seven-state transition table and a single `CanTransition` 🎯 | `p3-01-transition-matrix.txt` | ⚠ §4.1 |
| P3-02 | Migration 0008 — `PurchaseOrders`, `PurchaseOrderLines`, grants 0010 | `p3-02-schema.txt`, `p3-02-grants.txt` | ✅ |
| P3-03 | Create and read purchase orders and lines | `p3-03-purchase-orders.txt` | ⚠ §4.2 |
| P3-04 | Submit and approve, self-approval prohibited 🎯 | `p3-04-approval.txt`, `p3-04-self-approval-denied.txt` | ✅ |
| P3-05 | Cancellation and closure rules | `p3-05-cancellation.txt` | ✅ |
| P3-06 | Purchase history and order tracking | `p3-06-purchase-history.txt` | ✅ |
| P3-07 | Procurement WPF client reaches usable state | `p3-07-procurement-client.txt`, `p3-07-focus-order.txt`, `p3-07-live-clickthrough-2026-08-28/` | ✅ §4.3 |
| P3-08 | `docs/api-specification.md` — procurement section | `p3-08-api-specification.txt` | ✅ |
| P3-09 | Closure pack — suite green, clean clone, this index | `p3-09-clean-clone.log`, this file | ✅ |

**P3-01 stays ⚠ rather than round up to a plain ✅.** It is not a failing test or a partial
implementation — it is fully green — but it records a limit on what its own evidence proves,
in the same spirit P2-01/P2-03 were held at 🟡 in the Phase 2 index rather than silently
rounded up. See §4.

**P3-07 was ⚠ at the first sitting of this gate and is now ✅ — but only after the gate
rejected it.** Its seventh box was carried on a claim that its own arithmetic refuted, and
behind that claim sat a real defect. §4.3 records what was found, what changed, and what
genuinely remains Phase 7's.

---

## 3. Gap register — the Phase 3 row

Continuing `../phase-2/INDEX.md` §3, which left G-23 assigned to this phase.

| Gap | Mitigation (spec §23) | Phase 3 cards | Artifact | Status now |
|---|---|---|---|---|
| **G-23** · Receiving phase preceded procurement dependency | Reorder phases so procurement primitives precede receiving integration | P3-01–P3-06 (structure), P3-09 (closure) | `../../tasks.md` (Phase 3 header, line 67), `../../plan.md` §7 (line 379), `p3-01-transition-matrix.txt`, `p3-04-approval.txt`, `p3-05-cancellation.txt` | ✅ **closed** |

**G-23 closed.** Unlike G-19/G-21 in Phase 2, this gap is not a checklist of components —
it is a single structural claim: *procurement primitives exist and are provable before
receiving is built on top of them.* Two things make that true rather than merely stated:

1. **The reordering itself.** `plan.md` §7 places Phase 3 (Procurement Primitives) before
   Phase 4 (Inventory and Receiving), and Phase 3's own `tasks.md` header states it as a
   deliberate constraint: *"Procurement precedes receiving deliberately — this is the
   spec's G-23 reordering. Do not merge these phases."* That sentence predates this card;
   P3-09 is where it gets recorded as closed rather than merely asserted.
2. **The dependency is real, not cosmetic.** P3-01's transition table models all seven
   spec §10.1 states — including `PartiallyReceived` and `FullyReceived`, which Phase 4's
   receiving command will drive — so Phase 4 has a closed, tested table to consume on day
   one rather than needing to invent status handling itself. P3-04's self-approval rule and
   P3-05's cancellation/closure rules are proven server-side, over real HTTP, before a
   single receiving endpoint exists. Phase 4's dependency review is therefore: consume
   `CanTransition`, do not re-decide it (P3-01's own closing note) — a mechanism Phase 4
   inherits rather than a review Phase 4 still owes.

No G-23 component is deferred to a later phase. This is the only gap Phase 3 owns, and it
closes here in full.

Gaps G-06, G-08, G-15, G-16, G-17, G-21 (Phase 2's own carry, per its §4.2), G-22, G-24,
G-25, G-26, G-27, G-28, G-30 remain assigned to Phases 4–7 by `plan.md` §7 and §10, and are
correctly untouched here.

---

## 4. Where the claim is narrower than the wording

### 4.1 P3-01 — "no `Select Case` on status anywhere else" is a lint, not a proof

The done-when box reads *"no `Select Case` on status anywhere else in the solution"* and is
enforced by a source-scan lint over the compiled solution. That lint catches the idiomatic
`Select Case … Status` form. It does **not** catch an `If order.Status = …` chain written
some other way, and it cannot see a transition decision made in SQL. P3-01's own test
summary and ADR-020 both state this rather than let the box read as a stronger guarantee
than the mechanism backing it. No such bypass exists in the Phase 3 codebase today — the
limit is on what the *check* proves, not a known gap in the code.

### 4.2 P3-03 — the isolation-level divergence is real, carried, and does not affect any
proven claim

Measured directly inside a transaction (`p3-03-purchase-orders.txt` §5): MySqlConnector's
`BeginTransaction` sends its own `SET TRANSACTION ISOLATION LEVEL`, overriding the
`SET SESSION` `ConnectionFactory` issues. Only `PurchaseOrderService` passes
`IsolationLevel.ReadCommitted` explicitly at `BeginTransaction`; every other call site
(`StockService`, `PriceChangeService`, `ProductLifecycleService`, `SupplierLifecycleService`,
and three controllers) still opens `REPEATABLE READ` in practice, though `ADR-006`'s text
says `READ COMMITTED`. **No test of theirs fails**, because their correctness rests on
InnoDB row-locking on a conditional `UPDATE` — a locking read is current at either isolation
level, which is why P1-11/P1-13's concurrency proofs stand unchanged. This is a divergence
between ADR-006's text and the running system, not a known-broken guarantee, and it is
carried forward as **CARRY-03** (`tasks.md` line 50) with Phase 4 named as its natural home,
since receiving reads a line before it writes one — the first command that would actually be
bitten by it.

### 4.3 P3-07 — the deferred box was hiding a defect; found and fixed at the gate

**This is the row this gate exists for, so it is written out in full.**

At the first sitting, `p3-07-procurement-client.txt` recorded six of seven done-when boxes
ticked, and deferred the seventh — *"keyboard navigation and focus order work at 1366×768 and
125% scaling"* — to the Phase 7 UI pass. The gate rejected the deferral on two grounds.

**The card says the opposite.** Its parenthetical reads *"the Phase 7 UI pass refines this; it
does not start it"* — Phase 7 refines what Phase 3 built, so deferring the whole box inverted
the instruction.

**The supporting sentence refuted itself.** It claimed the window *"(1024x680, MinWidth 960,
MinHeight 620) fits inside a 1366x768 desktop at 125% scaling (effective ~1093x614 minus
taskbar) with room to spare."* Both 680 and 620 exceed the 614 the same clause computes. The
figures were right; the conclusion was not. WPF lays out in DIPs and `Window.Height` is in
DIPs, so this was not a unit confusion — it was a comparison nobody made.

**What that hid.** `MinHeight` is the value that matters: a minimum taller than the work area
cannot be dragged or resized into it, so the status bar along the bottom — the one carrying
every server message and correlation ID — would have been permanently off-screen on a
1366×768 demo laptop. The three classmates who must demonstrate this system (ADR-012) are
exactly the people who would have hit it.

**Fixed, and the fix found a second defect.** `Height` 680→560 and `MinHeight` 620→520 against
a work area of 1092.8 × **576.0** DIP (1366/1.25 × (768−48)/1.25). With the window corrected,
measurement showed the New Order tab's fixed `260` DIP row could not give way: the Lines grid
arranged to **0.0 DIP** — laid out, focusable, invisible. That row is now proportional with a
minimum.

**Proven, not asserted.** `ProcurementLayoutTests` (3 tests, `Merchandising.Tests.Unit`) holds
the four declared sizes against the computed work area, lays every screen out at the window's
own minimum and fails any grid below 48 DIP, and checks every interactive control has a unique
TabIndex ascending in reading order per screen. Both size and starvation tests were **proven
falsifiable** against the pre-fix XAML before being trusted — the failure messages are quoted
in `p3-07-focus-order.txt` §2. The traversal itself was then performed for real: a window
shown at 1093.0 × 576.0 DIP, walked with `MoveFocus`, all four screens, every visited control
inside the window bounds, ascending TabIndex, Sign out (99) last, and every skipped control
reconciled to a named WPF reason (a disabled command, or an empty grid with no focusable
cell). No unexplained skip.

**⚠ What is genuinely still Phase 7's**, and is not this box: rendering fidelity at a real
125% DPI (glyph hinting, hairline borders), whether the focus rectangle reads clearly against
each background, and traversal with the grids populated. The first two are look questions
rather than reachability questions; the third is `DataGrid`'s own internal cell navigation,
not this window's authoring. The DIP argument in `p3-07-focus-order.txt` §1 is what makes the
fit and order claims valid without setting this workstation to 1366×768 @ 125%; it is stated
there explicitly rather than left implicit, because it is the load-bearing step.

### 4.4 Carried from Phase 2, still open, not a Phase 3 obligation

**CARRY-01** (account recovery — `tasks.md` line 44) and **CARRY-02** (off-host backup copy
with the volume present — `tasks.md` line 56) both surfaced before this phase and remain
unassigned or assigned to Phase 6 respectively. Neither names a Phase 3 card, neither is
listed in Phase 3's *Build* scope (`plan.md` §7), and no Phase 3 card claims to close either
one. Recorded here only so a reader of this index does not mistake their continued
open-ness for something this phase missed.

---

## 5. Test-suite growth across Phase 3

| Point | Unit | Integration | Total |
|---|---|---|---|
| Phase 2 gate (`229748e`, 2026-08-25) | 24 | 136 | 160 |
| Phase 3, first P3-09 capture (`70df014`) | 41 | 211 | 252 |
| Phase 3 gate (`edc4054`, 2026-08-28) | **44** | **211** | **255** |

Unit grew by 17 tests to the first capture — P3-01's transition matrix, computed exhaustively
over every state × action pair from the enums rather than hand-listed, so a new state cannot
be added without the suite growing with it — then by 3 more when this gate reopened P3-07
(`ProcurementLayoutTests`). Integration grew by 75 tests across Tracks B, C and D, against the
real pinned MariaDB 10.4.32 throughout — never a substitute, never an in-memory provider, and
unchanged by the P3-07 fix, because that defect was in client layout and no server-side test
could have caught it. 0 skipped in both suites at the gate.

**One intermittent build failure is recorded rather than omitted.** The first clean-clone
attempt at `edc4054` failed with `MSB3030` copying `runtimeconfig.json` from the two `WinExe`
projects `Merchandising.Tests.Unit` references. It did not reproduce in eight further
clean-clone builds across both commits, and both project references predate this gate, so it
is a pre-existing parallel-build fragility one run exposed — not a regression. Measured
attribution and the reason it is left recorded rather than repaired: `p3-09-clean-clone.log`
§5.

---

## 6. ADR status at the Phase 3 gate

**One entry resolved this phase: ADR-020** (the purchase-order status machine is a table,
not a set of checks), raised and settled at P3-01, `Status: ACCEPTED`.

**21 top-level entries** (`ADR-000`…`ADR-020`) plus **8 sub-entries** (003.1, 003.2, 004.1,
009.1, 009.2, 011.1, 013.1, 015.1) — 29 entries in total. Every `**Status:**` line among them
reads **ACCEPTED**; none reads `PENDING`. The historical mentions of the word "PENDING"
inside ADR-009 and ADR-011 are narration of a *past* resolved state (P1-11/P1-12's progress
notes, and ADR-008's own resolution note), not live status lines. The only current `PENDING`
string in the file is the placeholder inside the *Template for new entries* fence, unchanged
since the Phase 2 gate.

**ADR-020 is appended before the template section, not inside its fence** — `## ADR-020`
starts at line 950, `## Template for new entries` at line 1010, the template's own fence
opens at line 1012. The formatting defect P2-13 repaired (ADR-015–018 rendering as a code
block) has not recurred.

---

## 7. What Phase 3 did not touch

`P2-10` (supplier master) was built early in Phase 2 and is consumed, not rebuilt, here —
see `../phase-2/INDEX.md` for its evidence. The product master (`0006`), the audit pipeline
(P2-04), the 29-policy registry, the error envelope, idempotency, and the ADR-006 transaction
pattern are all Phase 1/2 mechanisms this phase consumed under the "frozen: no phase below
may re-litigate" rule stated in `tasks.md`'s Phase 2 closure note (line 22).
