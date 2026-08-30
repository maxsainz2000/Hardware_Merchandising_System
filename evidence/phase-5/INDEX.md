# Phase 5 — POS · Evidence Index

**Built at P5-15, 2026-08-30; re-issued at P5-16 after the exit review returned FAIL.**
Continuing `../phase-4/INDEX.md` rather than starting a new register — the same three rules
apply, because an index is only worth as much as its weakest row. **The review found two defects
behind ticked boxes that this file's own §4 was supposed to catch and did not; both are recorded
in §4.6, closed, rather than quietly repaired.**
**Spec:** `documentations/Merchandising System for a Mid-Scale Hardware Store.md` §10.3, §11,
§14, §16, §20, §23
**Plan:** `plan.md` §7 (Phase 5), §10 (risk register mapping)

---

## How to read this file

This index maps every Phase 5 exit criterion and every task card to a file that exists on
disk, and records the gap-register outcome for **G-24** and **G-12's sale component**. The same
three rules as `../phase-4/INDEX.md`:

1. **Every artifact path was checked to exist**, and to be non-empty — every row in §2 was
   checked against `evidence/phase-5/` directly, not assumed from `tasks.md`'s own citation.
2. **No row says "verified informally."** Where a claim rests on inspection rather than
   execution, the row says so and is not counted as proven.
3. **Where the evidence proves something narrower than the row's wording, the row records
   the narrower claim** — marked ⚠ rather than ✅, and explained in §4.

Status marks: ✅ proven by executed evidence · ⚠ proven, with a stated limit on the claim ·
⬜ not proven · 🟡 open, owed before the gate.

---

## 1. Phase 5 exit criteria → artifact

From `tasks.md`'s `Phase 5 exit gate` checklist (mirroring `plan.md` §7 Phase 5 *Exit*).

| # | Criterion | Card | Artifact | Status |
|---|---|---|---|---|
| 1 | End-to-end sale flow passes | P5-07, P5-13 | `p5-07-atomic-sale.txt`, `p5-13-pos-client.txt` §6 | ✅ |
| 2 | End-to-end return flow passes | P5-11, P5-13 | `p5-11-sale-returns.txt`, `p5-13-pos-client.txt` §6 | ✅ |
| 3 | Negative stock impossible under concurrent load | P5-08 | `p5-08-sale-concurrency.txt` | ✅ |
| 4 | Idempotent retry returns the original result, never a second sale | P5-09 | `p5-09-idempotent-sale.txt` | ✅ |
| 5 | Completed sales immutable | P5-10 | `p5-10-sale-immutability.txt` | ✅ |
| 6 | Change calculation exact to the stored precision | P5-01, P5-07 | `p5-01-sale-arithmetic.txt`, `p5-07-atomic-sale.txt` | ✅ |
| 7 | Payment-method wording verified non-authorising, UI and reports | P5-12 | `p5-12-payment-wording.txt` | ⚠ §4.8 |
| 8 | `docs/ui-specification.md` written | P5-14, P5-16 | `p5-14-ui-specification.txt`, `../../docs/ui-specification.md`, `p5-16-gate-remediation.txt` §2 | ✅ *(§4.6 finding 2 — a false citation, corrected and now machine-checked)* |
| 9 | G-24 closed, and G-12's sale component closed, in the gap register | P5-15 | this file, §3 | ✅ |
| 10 | Clean-clone build and both test suites green | P5-15, P5-16 | `p5-16-clean-clone.log` | ✅ |
| — | *(CLAUDE.md §9)* Every task done, no card left 🟡 and rounded up | P5-13, P5-16 | `p5-13-pos-client.txt` §6, `p5-16-gate-remediation.txt` §1 | ✅ *(§4.6 finding 1 — box 5 was rounded up; the missing half now exists)* |

**Criteria 1, 2 and the "every task done" row now round up to ✅.** P5-13's own Done-when box 1
was unticked when this pack began — the deployed API service was found stale (same defect class
P4-15 found), and redeploying needed an elevated shell this pack's session did not have. The
operator ran the redeploy in an elevated shell; §4.4 below records the finding, the remedy, and
the authenticated pass that ran once the redeploy landed.

---

## 2. Task card → evidence

| Card | Title | Artifact | Status |
|---|---|---|---|
| P5-01 | Fixed-precision sale arithmetic in Domain 🎯 · Decides ADR-022 | `p5-01-sale-arithmetic.txt` | ✅ |
| P5-02 | Migration 0011 — CashierSessions/Sales/SaleLines/SalePayments, grants 0013 | `p5-02-pos-schema.txt`, `p5-02-pos-grants.txt` | ✅ |
| P5-03 | Migration 0012 — SalesReturns/SalesReturnLines, grants 0014 | `p5-03-returns-schema.txt`, `p5-03-returns-grants.txt` | ✅ |
| P5-04 | Open/close a cashier session | `p5-04-cashier-sessions.txt` | ✅ |
| P5-05 | Daily closing — declared vs. calculated, with variance | `p5-05-daily-closing.txt` | ✅ |
| P5-06 | Fast product lookup by SKU/barcode/name | `p5-06-product-lookup.txt` | ✅ |
| P5-07 | The atomic sale — seven effects, one transaction 🎯 · Closes G-12 (sale component) | `p5-07-atomic-sale.txt` | ✅ |
| P5-08 | Negative stock impossible under concurrent load | `p5-08-sale-concurrency.txt` | ✅ |
| P5-09 | Idempotent retry returns the original sale | `p5-09-idempotent-sale.txt` | ✅ *(tasks.md's own citation corrected — see §4.1)* |
| P5-10 | Completed sales immutable; cancellation only before completion | `p5-10-sale-immutability.txt` | ✅ |
| P5-11 | Sale returns bounded by sold-minus-prior-returns | `p5-11-sale-returns.txt` | ✅ |
| P5-12 | Payment-method wording verified non-authorising — closes G-24 | `p5-12-payment-wording.txt` | ✅ |
| P5-13 | POS WPF client reaches usable state, mouse-free | `p5-13-pos-client.txt` §6 | ✅ |
| P5-14 | `docs/ui-specification.md` — all three clients | `p5-14-ui-specification.txt` | ✅ |
| P5-15 | Closure pack — suite green, clean clone, this index | `p5-15-clean-clone.log`, this file | ✅ |
| P5-16 | The exit review's two findings, closed — access-key assertion, live traversals, citation check | `p5-16-gate-remediation.txt`, `p5-16-pos-focus-order.txt`, `p5-16-inventory-focus-order.txt`, `p5-16-clean-clone.log` | ✅ |

**P5-13 moved 🟡 → ✅ within this closure pack, in the exact P4-15 shape.** Every box this card
could verify without a live redeploy was already green (guardrail G-B, server-side refusal
surfacing, mouse-free/keyboard-navigation assertions, both suites). The one box that needed the
deployed service — the authenticated pass — was found blocked, not silently dropped: the
service was probed directly (`POST /api/v1/cashier-sessions` → 404, binary `LastWriteTime`
2026-08-29 21:01:06, i.e. a Phase 3/4-era build), the operator ran the redeploy in an elevated
shell, and the same route then answered 401 against a build with `LastWriteTime` 2026-08-30
21:18:42. The authenticated pass ran clean against it. See §4.4.

---

## 3. Gap register — the Phase 5 row

Continuing `../phase-4/INDEX.md` §3, which left **G-24** assigned to this phase and recorded
**G-12** as closed for receiving, returns, counts and adjustments — *"sale is Phase 5's own
transaction and G-12's sale component remains that phase's to close."*

| Gap | Mitigation (spec §23) | Phase 5 cards | Artifact | Status now |
|---|---|---|---|---|
| **G-24** · Payment recording could be mistaken for payment authorisation | Label card/e-wallet as operational recording only; exclude terminal/bank integration; test UI wording and reports. | P5-04 (data shape), P5-07 (response wording), P5-11 (return-reversal wording), P5-12 (asserted denylist test, both directions) | `p5-04-cashier-sessions.txt`, `p5-07-atomic-sale.txt`, `p5-11-sale-returns.txt`, `p5-12-payment-wording.txt` | ✅ **closed** |
| **G-12** · Transaction boundaries were described but not operationally specified — sale component | Define transaction contents for the sale transaction specifically; record correlation IDs; use explicit rollback tests. | P5-07 (all seven effects, forced-failure rollback), P5-08 (concurrent-load safety), P5-09 (idempotent retry), P5-10 (immutability) | `p5-07-atomic-sale.txt`, `p5-08-sale-concurrency.txt`, `p5-09-idempotent-sale.txt`, `p5-10-sale-immutability.txt` | ✅ **closed** |

**G-24, closed.** The mitigation names three things — the label, the exclusion, and *tested*
wording — and each has its own artifact:

- **The label exists at every layer that carries a sale total.** P5-04's daily-closing
  breakdown labels card/e-wallet rows **recorded, not authorised**; P5-07's sale response and
  P5-11's return response use the same wording for the same reason.
- **The exclusion is stated where a reader sees it**, not only in the spec: no terminal, bank,
  cash drawer, scale, customer display or receipt printer integration — asserted in
  `p5-12-payment-wording.txt`.
- **The wording is asserted by a denylist test**, not eyeballed: `PaymentWordingTests.vb` scans
  the POS client's XAML and the sales/report contracts for *approved, authorised/authorized,
  accepted, cleared, settled, charged* in card/e-wallet contexts and fails if one appears — and
  the test was **watched fail** against a deliberately introduced "Payment approved" label
  before being trusted (`p5-12-payment-wording.txt` §2, the induced-failure run). A wording test
  that has never fired is exactly as decorative as any other, and this one was proven not to be.

**G-12's sale component, closed.** The same four effects `../phase-4/INDEX.md` §3 required of
receiving, returns, counts and adjustments are proven for the sale transaction specifically:

- **Transaction contents defined and proven atomic** (P5-07): all seven effects — sale header,
  lines, payment record(s), one `StockMovements` row per line, the conditional balance
  decrement, session totals, and the audit row — commit together or not at all, proven by a
  forced-failure test in the P1-12/P2-08/P4-05 shape.
- **Concurrent-load safety** (P5-08): N simultaneous sales of the last unit resolve to exactly
  one success; sale-vs-receive and sale-vs-adjustment races — the exact pairs P4-15 named and
  left open — both fired, both orderings genuinely observed, watched fail against a broken
  guard before being trusted.
- **Idempotency** (P5-09): a repeated key returns the original committed sale byte-for-byte; a
  same-key-different-body retry is refused rather than silently replayed; a concurrent identical
  race resolves to exactly one commit. ADR-007 amended (ADR-007.1) to record the mechanism.
- **The ledger invariant itself** (P4-01's standing assertion, exercised by every Phase 5
  integration test): `SUM(StockMovements) = StockBalances` held after every sale, return, and
  the deliberately-induced drift in P5-08's watched-fail run, which was closed with compensating
  movements only — never an edit.

**What "closed" does not include, and is not claimed to.** G-12's spec §23 wording also names
*price change* (closed at Phase 2, P2-08) and the components Phase 4 already closed. This row
closes the sale component specifically — the last one G-12 named — which completes the gap
across every transaction type the mitigation lists. G-12 as a whole gap is now fully closed
across every phase; this row is the one that finishes it, not the whole of it in isolation.

Gaps G-06, G-08, G-15, G-16, G-17, G-22, G-25, G-26, G-27, G-28, G-30 remain assigned to Phases
6–7 by `plan.md` §7 and §10, and are correctly untouched here. **CARRY-05** (nothing detects a
stale deployment) surfaced at the Phase 4 gate and is explicitly owed at the **Phase 6** gate,
not this one — but this phase's own closure pack was required to confirm the deployed service is
current before believing the client, which is a narrower, immediate obligation distinct from
building the detection mechanism CARRY-05 itself asks for. See §4.4.

---

## 4. Where the claim is narrower than the wording

### 4.1 P5-09's own `tasks.md` citation named a file that does not exist

`tasks.md` line 269 cited `evidence/phase-5/p5-09-sale-idempotency.txt`. The file actually on
disk is `p5-09-idempotent-sale.txt` — a one-word transposition, not a missing artifact; the
content this card's Done-when boxes describe is genuinely present and was read in full for §2
above. **Corrected in `tasks.md` at P5-15**, rather than left as a broken pointer for the next
reader — exactly the class of thing rule 1 in "How to read this file" exists to catch.

### 4.2 P5-08's watched-fail proof, and what it does and does not extend

`p5-08-sale-concurrency.txt` proves sale-vs-sale, sale-vs-receive and sale-vs-adjustment safety,
each with both orderings observed and a genuine watched-fail/revert/green cycle. It does not
claim, and its own §7 says so directly, anything about **idempotent retry safety** (P5-09's own
card) or **completed-sale immutability** (P5-10's own card) — each of those is a separate proof
in its own file, not implied by P5-08's concurrency result. No gap here; recorded because a
reader skimming "negative stock impossible under concurrent load" could otherwise assume it
covers more than it does.

### 4.3 P5-12 explicitly defers G-24's gap-register edit to this card, and the wiring check to P5-13

`p5-12-payment-wording.txt` §4 states outright that it does **not** claim the gap register is
closed (that edit is this card's, per `tasks.md`'s own line 329) and does **not** claim the
POS checkout screen itself binds to the two wording resource keys — only that the strings and
the test proving them are correct exist. P5-13's own evidence (`p5-13-pos-client.txt`) is where
the resource keys are confirmed actually bound in the client's XAML rather than re-typed. Both
halves check out: the keys are referenced from `Merchandising.POS`'s checkout/returns XAML, not
duplicated as literal strings, per that file's own §1.

### 4.4 CARRY-05 — the deployed service was found stale; the redeploy needed elevation, and the operator provided it

`tasks.md`'s P5-15 card requires "the deployed service is current, and the evidence records its
build identity (CARRY-05)." Before touching anything else, this pack probed the live service
directly rather than assuming it was current (the same lesson P5-13 already recorded):

```
Get-CimInstance Win32_Service -Filter "Name='MerchandisingApi'"  ->  Running
POST https://MERCH-HOST:8443/api/v1/cashier-sessions             ->  404 Not Found
(Get-Item ...\Merchandising.Api.exe).LastWriteTime                ->  2026-08-29 21:01:06
```

This is the identical defect class P4-15 found and fixed for Phase 4's routes, now recurring for
Phase 5's: `CashierSessionsController` (P5-04) has existed in source since before this pack
began, so a 404 on its own route means the running binary predates it. `GET /health` still
answers 200 — the service is healthy, just stale, exactly `p5-13-pos-client.txt` §2 already
recorded.

`([Security.Principal.WindowsPrincipal]...).IsInRole(Administrator)` returned `False` for this
session, matching P5-13's own finding. Redeploying requires stopping the service first
(`publish-release.ps1` cannot overwrite a DLL the running process holds open), which requires an
elevated shell. **The operator was given the exact commands** (`Stop-Service` →
`publish-release.ps1` → `install-service.ps1`, the same sequence P5-13's evidence file already
names), ran them in an elevated PowerShell 7 session, and confirmed back. Per CLAUDE.md §7, the
elevation gap was reported as a stop condition rather than worked around, and this pack waited
for the operator's own elevated run rather than attempting one.

**The redeploy landed, and was verified directly rather than trusted on report:**

```
Get-CimInstance Win32_Service -Filter "Name='MerchandisingApi'"        -> Running
(Get-Item ...\Merchandising.Api.exe).LastWriteTime                     -> 2026-08-30 21:18:42
GET  https://MERCH-HOST:8443/health                                   -> 200
POST https://MERCH-HOST:8443/api/v1/cashier-sessions                  -> 401 (was 404)
```

The nine POS operations were then driven through the real `MainViewModel` against the
redeployed service, in the P4-15 shape, from a harness built outside the repository
(`C:\Users\Admin\Documents\cc-p5-15-pos-harness` — deliberately not under this session's own
`AppData\Local\Temp` scratchpad, per §4.5's own finding below) and deleted after the capture.
Full transcript, the fresh Cashier account it signed in with, and the reasoning behind each
assertion are in `p5-13-pos-client.txt` §6–§8. **Exit criteria 1 and 2 and the "every task done"
row all round up to ✅ now**, since the authenticated pass was the only thing keeping any of the
three open.

### 4.5 A finding this pack made in the course of the clean-clone build, not a defect in the tree

Recorded in full in `p5-15-clean-clone.log` §4: building the clean clone under this session's
own deeply-nested `AppData\Local\Temp\claude\...` scratchpad path reproduced CARRY-04's named
MSB3030 copy-race **deterministically** (9 consecutive failures, several different mitigations
tried), while the identical source at the identical commit, cloned instead to a plain
outside-the-repo directory, built clean on the first attempt with the default parallel build.
This is a second sighting by CARRY-04's own stated threshold ("raise a card only on a second
sighting"), and narrows the prior theory rather than only repeating it — the referenced
project's runtimeconfig.json was confirmed present on disk at the moment the copy claimed it was
missing, ruling out "the generating task had not run yet" as this location's cause. **Naming a
card for this is left to Phase 6 planning**, per this pack's own scope (evidence and index, not
build-infrastructure authorship) — the practical mitigation (do not clean-clone-build under a
deep `AppData\Local\Temp` path) is recorded in the log so it is not rediscovered at cost.

### 4.6 The exit review returned FAIL, and this section did not catch either finding

**Added at P5-16, after the gate.** The Phase 5 exit review found two defects behind ticked
boxes with **zero missing artifacts**. Both are recorded here in full because §4's preamble
promised exactly this service and did not deliver it — the pack wrote *"assume this one has a
third"* and then produced a §4 that recorded four narrowings, none of them these.

| | Finding | Where it lived | Closed by |
|---|---|---|---|
| 1 | P5-13's box 5 says *"asserted by a test over TabIndex **and access keys** per screen"*. Only the TabIndex half was implemented; `p5-13-pos-client.txt` quoted the box with an ellipsis that removed the words *and access keys* | `POSLayoutTests`, `p5-13-pos-client.txt` §1 | P5-16 — two new assertions, both watched fail |
| 2 | `docs/ui-specification.md` §4 cited `p5-13-pos-client.txt` §2 for a by-hand focus traversal. §2 of that file is *"THE FINDING — the deployed service is stale"*; the file contains no traversal at all. The *"per client"* claim was also untrue for Inventory | `docs/ui-specification.md` §4 | P5-16 — two live traversals captured, citation repointed, and now machine-checked |

**Finding 2 is the Phase 4 gate's own failure #1 recurring in a new place** — a criterion resting
on a citation that never fired. It survived for the same reason: nothing checked citations. It
cannot recur silently now, because
`UiSpecificationDocumentationTests.EveryEvidencePathTheDocumentCites_ExistsAndContainsWhatItIsCitedFor`
resolves every `evidence/…` path the document names against disk **and** requires the three
traversal transcripts to contain a traversal. It was watched fail against the original broken
citation itself, not an invented one.

**Neither finding was a design defect**, and that is worth stating plainly rather than as
consolation: mouse-free POS genuinely works, and the no-access-keys convention is deliberate,
system-wide, and documented. Both were *claims that outran their evidence*. Full record:
`p5-16-gate-remediation.txt`.

### 4.7 What the review checked and found sound

Recorded because a §4 that lists only defects tells a reader nothing about coverage. The review
independently reproduced, rather than reading from this pack: guardrails G-A–G-D, the build at 0
warnings, 79/79 unit and 465/465 integration against the real MariaDB, the deployed binary's
`LastWriteTime` (2026-08-30 21:18:42, later than every source commit), every `**Status:**` line
in `docs/adr.md`, ADR-022's line number against the template fence, grants `0013`'s INSERT-only
privileges on `sales`/`salelines`/`salepayments`, `p5-08-drift-correction.sql` as INSERT-only,
the full P4-gate→P5-15 test-count chain against each card's own file, and a hand re-run of
P5-12's denylist across the whole of `src/`. All reproduced as claimed.

### 4.8 ⚠ G-24's denylist scope is correct today and will silently narrow in Phase 6

Exit criterion 7 reads *"payment-method wording verified as non-authorising in both UI **and
reports**."* `PaymentWordingTests` scans `src/Merchandising.POS/**/*.xaml` and
`src/Merchandising.Contracts/Sales/*.vb` — which **is** every report-shaped surface that exists
at Phase 5 close (the daily-closing payment-method breakdown, the sale and return responses).
Spec §14's twelve reports are Phase 6 and will land outside that scan, in new directories.

**The claim is therefore sound now and will quietly stop being sound the moment reports exist.**
Not fixed at P5-16 on purpose: widening a scan to directories that do not exist yet asserts
nothing today, and would pass vacuously the moment they appear. **Phase 6 owes the widening as
part of building the reports**, and this row is the reminder.

---

## 5. Test-suite growth across Phase 5

| Point | Unit | Integration | Total |
|---|---|---|---|
| Phase 4 gate (`76aaa7d`, 2026-08-29) | 47 | 348 | 395 |
| P5-15 closure pack (`a6d3b6a`, 2026-08-30) | 79 | 465 | 544 |
| P5-16 gate remediation (2026-08-30) | 82 | 465 | 547 |

Full per-card chain, cross-checked against every card's own cited suite-state line with no gap
and no double-count, is in `p5-15-clean-clone.log` §3. Unit grew by 32: P5-01's sale-arithmetic
tests (+14), P5-12's `PaymentWordingTests` (+5), P5-13's `POSClientTests`/`POSLayoutTests` (+8),
and P5-14's `UiSpecificationDocumentationTests` (+5). Integration grew by 117 across Tracks
A–G — schema/grants (P5-02, P5-03), cashier sessions and closing (P5-04, P5-05), product lookup
(P5-06), the atomic sale and its concurrency/idempotency/immutability proofs (P5-07–P5-10), and
sale returns (P5-11) — all against the real pinned MariaDB 10.4.32, never a substitute. 0
skipped in both suites at every capture this phase.

**P5-16 added the last three unit tests, and they are the two findings' regression protection:**
`POSLayoutTests.NoScreenAuthorsAnAccessKey_SoTabTraversalIsTheWholeKeyboardStory`,
`UiSpecificationDocumentationTests.NoClientWindow_AuthorsAnAccessKey_MatchingSection4sStatedConvention`,
and `UiSpecificationDocumentationTests.EveryEvidencePathTheDocumentCites_ExistsAndContainsWhatItIsCitedFor`.
All three were watched fail before being trusted — the third against the exit review's own
original broken citation rather than an invented one. Integration is unchanged at 465: P5-16
touches no database, no API and no schema.

**No MSB3030 recurrence** in the capture that actually produced this index's own evidence
(`p5-15-clean-clone.log` §2/§3, first attempt, outside `AppData\Local\Temp`) — see §4.5 above
for the recurrence found elsewhere in the course of getting there.

---

## 6. ADR status at the Phase 5 gate

**One entry this phase owed, checked directly rather than assumed:**

- **ADR-022** (`docs/adr.md` line 1084) — *Sale arithmetic rounding is configurable, and a
  narrowing of ADR-004.1* — raised and settled at P5-01. `**Status:** ACCEPTED` at line 1086.
  No other ADR was raised during Phase 5.

**Every `**Status:**` line in `docs/adr.md` outside the template reads `ACCEPTED`** — checked
by pattern match across the whole file. The only `PENDING` string remaining is the placeholder
inside `## Template for new entries`.

**ADR-022 is appended before the template section, not inside its fence** — `## ADR-022` starts
at line 1084, `## Template for new entries` at line 1109. Checked by line number, the exact
regression P2-13 once had to repair — it has not recurred.

---

## 7. What Phase 5 did not touch

Every Phase 1–4 decision `tasks.md`'s own Phase 5 header named as frozen — the connector,
transaction pattern (ADR-006 + P4-04's amendment), auth scheme, error envelope (ADR-014), grant
model (ADR-013), policy naming and self-approval (ADR-017), the barcode rule (ADR-018), the
purchase-order status machine (ADR-020), and the ledger reconciliation assertion (ADR-021) —
was consumed, not re-litigated, across every Phase 5 card.

**Carried, not owed here** (`tasks.md`'s own carried-forward section, unchanged by this phase):
**P0-02** and **P0-05** — Phase 6 gate (ADR-016). **P0-07**'s last document box (`test-plan.md`)
— Phase 7; `backup-restore-guide.md`/`user-guide.md` — Phase 6; the other three are now written.
**CARRY-02** — Phase 6 gate (ADR-019). **CARRY-05** — found at the Phase 4 gate, owed at Phase 6
(§3/§4.4 above record this phase's own narrower obligation to confirm currency, not to build the
detection mechanism). **CARRY-01** (account recovery) — carried through **four** phases without
an owner (2 → 3 → 4 → 5) now; `tasks.md`'s own Phase 5 entry repeats "not a Phase 5 blocker,"
but Phase 6 planning is where this must finally be placed. **CARRY-04** — was a watch item with
one prior sighting; §4.5 above records a second, narrower one; still not a card, but the
threshold its own wording named has now been met twice.
