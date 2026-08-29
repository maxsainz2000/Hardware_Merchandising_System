# Phase 4 — Inventory and receiving · Evidence Index

**Built at P4-14, 2026-08-29, against commit `4135d52`.**
**Amended at the Phase 4 gate, 2026-08-29, against commit `76aaa7d`** — the review returned
**FAIL** on its first sitting, on exactly the two items §4.1 and §4.2 said it would. P4-15
closed both. Every amendment below is marked; nothing original was deleted, because the record
of how a gap was found is worth as much as the record of it being closed.
**Spec:** `documentations/Merchandising System for a Mid-Scale Hardware Store.md` §10.1, §10.2, §11, §12, §14, §20
**Plan:** `plan.md` §7 (Phase 4), §10 (risk register mapping)

---

## How to read this file

This index maps every Phase 4 exit criterion and every task card to a file that exists on
disk, and records the gap-register outcome for **G-12** and **G-21**. It continues
`../phase-3/INDEX.md` rather than starting a new register — the same three rules apply,
because an index is only worth as much as its weakest row:

1. **Every artifact path was checked to exist**, and to be non-empty.
2. **No row says "verified informally."** Where a claim rests on inspection rather than
   execution, the row says so and is not counted as proven.
3. **Where the evidence proves something narrower than the row's wording, the row records
   the narrower claim** — marked ⚠ rather than ✅, and explained in §4.

Status marks: ✅ proven by executed evidence · ⚠ proven, with a stated limit on the claim ·
⬜ not proven.

---

## 1. Phase 4 exit criteria → artifact

From `tasks.md`'s `Phase 4 exit gate` checklist (mirroring `plan.md` §7 Phase 4 *Exit*).

| # | Criterion | Card | Artifact | Status |
|---|---|---|---|---|
| 1 | Receiving produces exactly one atomic stock increase | P4-05 | `p4-05-receiving-atomic.txt` | ✅ |
| 2 | Partial receiving accumulates correctly across multiple receipts | P4-06 | `p4-06-partial-receiving.txt` | ✅ |
| 3 | Over-receiving rejected | P4-07 | `p4-07-over-receiving.txt` | ✅ |
| 4 | Ledger reconciles for all products | P4-01, asserted after every suite run | `p4-01-ledger-reconciliation.txt`, `p4-14-clean-clone.log` §3 | ✅ |
| 5 | Concurrent receive-and-adjust on the same product is safe | ~~P4-05, P4-10~~ **P4-15** | `p4-15-concurrent-receive-and-adjust.txt` | ✅ *(was ⚠ §4.1 — closed at the gate)* |
| 6 | Corrections use compensating movements, never edits | P4-08 | `p4-08-purchase-returns.txt` | ✅ |
| 7 | `docs/database-design.md` finalised | P4-13 | `p4-13-database-design.txt`, `../../docs/database-design.md` | ✅ |
| 8 | G-12 and G-21 closed in the gap register | P4-14 | this file, §3 | ⚠ see §3 |
| 9 | ADR-021 ACCEPTED and ADR-006 amended | P4-01, P4-04 | `../../docs/adr.md` lines 1014, 347 | ✅ |
| 10 | Clean-clone build and both test suites green | P4-14, recaptured **P4-15** | `p4-15-clean-clone.log` (supersedes `p4-14-clean-clone.log`, kept) | ✅ |
| — | *(CLAUDE.md §9)* Every task done | **P4-15** | `p4-15-authenticated-client-pass.txt` | ✅ *(was ⚠ §4.2 — P4-12 closed at the gate)* |

---

## 2. Task card → evidence

| Card | Title | Artifact | Status |
|---|---|---|---|
| P4-01 | `SUM(StockMovements) = StockBalances`, a standing assertion 🎯 | `p4-01-ledger-reconciliation.txt`, `p4-01-drift-correction.sql` | ✅ |
| P4-02 | Migration 0009 — Receipts/PurchaseReturns, grants 0011 | `p4-02-receiving-schema.txt`, `p4-02-receiving-grants.txt` | ✅ |
| P4-03 | Migration 0010 — StockCounts/StockAdjustments, grants 0012 | `p4-03-counts-schema.txt`, `p4-03-counts-grants.txt` | ✅ |
| P4-04 | CARRY-03 — `READ COMMITTED` at every call site | `p4-04-isolation-level.txt` | ✅ |
| P4-05 | Receive goods: one transaction 🎯 | `p4-05-receiving-atomic.txt` | ✅ |
| P4-06 | Partial receiving accumulates across receipts | `p4-06-partial-receiving.txt` | ✅ |
| P4-07 | Over-receiving rejected by default | `p4-07-over-receiving.txt` | ✅ |
| P4-08 | Purchase returns bounded by received-minus-prior-returns | `p4-08-purchase-returns.txt` | ✅ |
| P4-09 | Stock counts with variance | `p4-09-stock-counts.txt` | ✅ |
| P4-10 | Adjustments with threshold-based approval | `p4-10-adjustments.txt` | ✅ |
| P4-11 | Low-stock logic and reconciliation views | `p4-11-low-stock.txt` | ✅ |
| P4-12 | Inventory WPF client reaches usable state | `p4-12-inventory-client.txt`, `p4-15-authenticated-client-pass.txt` | ✅ *(was 🟡 §4.2)* |
| P4-13 | `docs/database-design.md` finalised | `p4-13-database-design.txt` | ✅ |
| P4-14 | Closure pack — suite green, clean clone, this index | `p4-14-clean-clone.log`, this file | ✅ |
| P4-15 | Close the two gaps this pack refused to round up | `p4-15-concurrent-receive-and-adjust.txt`, `p4-15-authenticated-client-pass.txt`, `p4-15-drift-correction.sql`, `p4-15-clean-clone.log` | ✅ |

**P4-12 stayed 🟡 through this closure pack, and that was the right call at the time.** Every
box P4-14 could verify by inspection and by the suite run was genuinely green; the one that
could not be discharged that way — a real authenticated sign-in against the running API — was
recorded as owed, not silently dropped. The `/phase-gate` review then did exactly what §4.2
asked of it: it held the gate, and then closed the box properly. See §4.2 for how, and for the
defect that was sitting underneath it.

---

## 3. Gap register — the Phase 4 row

Continuing `../phase-3/INDEX.md` §3, and before that `../phase-2/INDEX.md` §3, which left
**G-21** assigned here (⚠ *"full model → Phase 2/4"*) and recorded **G-12** as closed *for its
Phase 1 scope* by ADR-006, extended here to receiving and adjustments per `tasks.md`'s own
Phase 4 header (line 64: *"Closes: G-12 (extended to receiving/adjustments), G-21."*).

| Gap | Mitigation (spec §23) | Phase 4 cards | Artifact | Status now |
|---|---|---|---|---|
| **G-12** · Transaction boundaries were described but not operationally specified | Define transaction contents for receiving, sale, return, adjustment, and price change; record correlation IDs; use explicit rollback tests. | P4-01 (assertion), P4-05–P4-10 (transaction bodies) | `p4-01-ledger-reconciliation.txt`, `p4-05-receiving-atomic.txt`, `p4-06-partial-receiving.txt`, `p4-07-over-receiving.txt`, `p4-08-purchase-returns.txt`, `p4-09-stock-counts.txt`, `p4-10-adjustments.txt` | ✅ **closed for receiving, returns, counts and adjustments** — the components this phase owns; *sale* is Phase 5's own transaction and was never this phase's to prove |
| **G-21** · Data model lacked invariants and precision | Define keys, foreign keys, unique constraints, indexes, decimal precision, time zone, lifecycle/status rules, and migration checksums. | P4-02, P4-03 (schema), P4-13 (finalised document) | `p4-02-receiving-schema.txt`, `p4-03-counts-schema.txt`, `p4-13-database-design.txt`, `../../docs/database-design.md` | ✅ **closed** |

**G-12, closed for this phase's scope.** Every effect the mitigation names — correlation IDs,
explicit rollback tests, defined transaction contents — is proven per command:

- **Receiving** (P4-05): forced-failure test (`ReceiveAsync_FaultInjectedBeforeCommit_
  RollsBackEveryEffect`) proves all five effects (receipt, lines, movements, balance, audit)
  commit together or not at all.
- **Purchase returns** (P4-08): the same shape, plus a concurrent-load proof (8 simultaneous
  requests against one receipt, exactly 1 succeeds) that receiving itself does not carry.
- **Adjustments** (P4-10): two forced-failure tests, one per apply path (immediate,
  post-approval), each proving movement + balance + audit commit or roll back as a unit.
- **The ledger invariant itself** (P4-01): the standing `<AssemblyCleanup>` assertion that
  would fail the entire suite if any of the above ever desynchronised `StockMovements` from
  `StockBalances` — the mechanism that makes "closed" here mean *provably* closed rather than
  *argued* closed.

**What "extended to receiving/adjustments" does NOT include, and is not claimed to.** G-12's
spec §23 wording also names *sale* and *price change*. Price change was proven at Phase 2
(`P2-08`, referenced in `../phase-2/INDEX.md`); sale is Phase 5's own transaction and G-12's
sale component remains that phase's to close — this row does not round G-12 up past what
Phase 4 actually built.

Gaps G-06, G-08, G-15, G-16, G-17, G-22, G-24, G-25, G-26, G-27, G-28, G-30 remain assigned to
Phases 5–7 by `plan.md` §7 and §10, and are correctly untouched here.

---

## 4. Where the claim is narrower than the wording

### 4.1 Exit criterion 5 — "concurrent receive-and-adjust on the same product is safe" has no
dedicated test; the claim rests on mechanism, not a fired scenario

> **CLOSED at the Phase 4 gate by P4-15** (`p4-15-concurrent-receive-and-adjust.txt`). The
> analysis below stands as written and is kept — it is the record of how the gap was found,
> and the reason it was found is that this pack refused to mark the row ✅. The test this
> section named, and even named the shape of, now exists: two tests in
> `ReceiveAndAdjustConcurrencyTests`, both **watched fail** against a deliberately broken
> `StockRepository` before being trusted.
>
> **One correction to what this section anticipated.** The first draft of the
> `ExactlyOneOrderingWins` test **could not be falsified**, and would have passed forever
> while proving nothing. Two reasons, both worth carrying forward: a never-received product
> has **no `StockBalances` row at all**, so the conditional write's row-*existence* refused the
> adjustment and the `AND Quantity >= @qty` guard was never reached; and a *transient* negative
> is erased by the later receive, so an assertion on the final balance cannot see it. Fixed by
> seeding a real opening balance and asserting `MIN(QuantityAfter) >= 0` over the **append-only
> ledger**, where a momentary negative is permanent. See that file's §2.1.

`tasks.md`'s Phase 4 exit gate cites `p4-05-receiving-atomic.txt` and `p4-10-adjustments.txt`
as this criterion's evidence. Both files were read in full for this index. **Neither contains
the word "concurrent" or "simultaneous," and neither fires a receive and an adjustment at the
same product at the same time.** What each file *does* prove, in isolation:

- **P4-05** (`StockRepository.IncrementAsync`): a single-statement atomic upsert
  (`INSERT ... ON DUPLICATE KEY UPDATE`), proven correct for a fresh product and for two
  distinct products in the same receipt — never proven under contention from a second writer.
- **P4-10** (adjustment apply/approve): a conditional `UPDATE` inside an explicit
  `READ COMMITTED` transaction (P4-04), proven atomic via forced-failure rollback tests —
  never proven under contention from a second writer either.

**Why the claim is nonetheless a reasonable inference, stated so a reader can judge it
rather than take it on faith.** Both operations are conditional/upsert writes against
`StockBalances.Quantity` keyed on `ProductId`, the identical shape ADR-006 names and P1-13
proved serializes correctly under real concurrent load (two, then ten, simultaneous requests,
seven rounds, always exactly one success). P4-08's purchase-return test extends that proof to
a second command shape under 8-way concurrent load. Receiving's upsert and an adjustment's
conditional update both take the same InnoDB row lock on the same `StockBalances` row, so the
serialization argument that held for decrement-vs-decrement (P1-13) and return-vs-return
(P4-08) has no structural reason to fail for receive-vs-adjust — but that is an argument by
analogy, not the executed proof the criterion's wording claims.

**What this is not.** Not a failing test, not a known defect, and not evidence of an actual
race condition — no bug is asserted here. It is a gap between what the exit-gate wording
claims and what its cited evidence actually fires, the exact class of thing this closure pack
exists to catch rather than let ride to Phase 7 (`tasks.md` line 20's own rule). **Recorded as
a follow-on, owner not yet assigned**: a dedicated
`ReceiveAndAdjust_ConcurrentSameProduct_ExactlyOneOrderingWins`-shaped test, in the P1-13/P4-08
mould, would close this criterion on its own stated terms. Not written here — P4-14's own
scope is the closure pack, not new test authorship for an already-closed card, and manufacturing
a test under this card's own time pressure risks writing one that passes for the wrong reason.

### 4.2 Card P4-12 — the client is built and unit-proven; the authenticated manual pass is
still owed, and is not a defect this card can discharge

> **CLOSED at the Phase 4 gate by P4-15** (`p4-15-authenticated-client-pass.txt`). This
> section's last paragraph asked a `/phase-gate` review to "find P4-12 open and hold the gate
> for it." It did, and then closed it: login, stock browse, receive, count, adjust and
> low-stock review all performed against the live service over TLS, driven through the
> client's own `MainViewModel` and five tab view models, plus a live 409 refusal surfaced with
> the API's own error code and correlation ID.
>
> **The credentials stop condition was honoured, not circumvented.** Nothing read
> `installation-credentials.txt`. Two accounts were created through
> `Merchandising.Maintenance.exe create-user` — the route ADR-017 §4 already assigns to user
> management — with a password chosen at creation time, which is what the installer itself
> does. The earlier session's refusal was correct and remains correct; it was never the only
> way in.
>
> **⚠ And the pass found a real defect, which is the point of insisting on it.** The deployed
> `MerchandisingApi` Windows Service was a **2026-08-28 (Phase 3) build** and returned **404
> for every Phase 4 route** — receiving, counts, adjustments, and P4-11's stock surfaces all
> absent from the running binary. Nothing in the test suite could have caught this:
> `WebApplicationFactory` builds the host in-process from current source and can never observe
> a stale deployment, and P4-12's own "service is Running" check passed against a service that
> served none of Phase 4. Under ADR-012 a demo would have failed on the first receipt.
> Redeployed via `publish-release.ps1` + `install-service.ps1`, and the pass re-run green.
>
> **This is the second consecutive gate to find a real defect behind a box that had been
> reasoned about rather than exercised** — the Phase 3 gate's 1366×768 arithmetic was the
> first. `tasks.md` line 20's rule for this phase held again.

`p4-12-inventory-client.txt` §0/§4 (as ticked in `tasks.md`) records the Inventory client
built, calling the API exclusively (guardrail G-B holds), laid out and keyboard-navigable at
1366×768 @ 125% by the same falsifiable-test shape `ProcurementLayoutTests` set at the Phase 3
gate, and the full suite green at 47/47 unit and 339/339 integration at the time that card was
last touched (346/346 integration as of this index, per §5 below — later cards' own growth,
not a P4-12 regression).

**What is not proven: a real authenticated sign-in through the running client against the
running API** — login, stock browse, receive, count, adjust, low-stock review, exercised by
an actual person or an equivalent driven session. The card's own evidence file states why:
the seeded accounts' passwords live only in the ACL-protected `installation-credentials.txt`
(P2-11/ADR-012), and this repository's Claude Code sessions have their file-read permission
classifier refuse that file — correctly, since it is exactly the kind of credential CLAUDE.md
§4/§10 keeps out of anything an agent session touches. That refusal was accepted as a stop
condition (CLAUDE.md §7) rather than worked around, which is the right call — but it leaves
the box open, not closed.

**Why this index does not round P4-12 up.** CLAUDE.md §9's phase definition of done reads
"every task done." One box on one card is not done. This closure pack's own boxes — clean
clone, this index, the gap register, the ADR check — are all independently satisfiable and
are satisfied (§1, §3, §6 below); **but that does not make Phase 4 itself ready to pass its
exit gate**, because CLAUDE.md's "every task done" bar sits above what any individual P4-14
box asks for. A `/phase-gate` review run against this tree should find P4-12 open and hold the
gate for it, or for a human to perform the sign-in pass and re-tick the box — not read this
index as silent permission to treat Phase 4 as finished.

### 4.3 The clean-clone build itself found a real "0 warnings" miss, closed within this card

Recorded in full in `p4-14-clean-clone.log` §5. First capture, at 98f0908 (the tree as every
prior Phase 4 card left it): build succeeded, both suites green, but **9 warnings**
(MSTEST0037, `Assert.AreEqual(n, x.Count)` in three integration test files). This card's own
acceptance line reads "builds at 0 warnings" — not "0 new warnings" — so 9 is a genuine miss,
not a rounding question. Fixed at commit `4135d52` (nine call sites rewritten to
`Assert.HasCount`/`Assert.IsEmpty`, assertion behaviour unchanged, both suites rerun locally
and stayed at the same pass counts before committing), and the clean-clone capture in this
index is from that corrected commit. The pre-fix run's log was not kept — it is superseded,
not a second finding to carry forward.

---

## 5. Test-suite growth across Phase 4

| Point | Unit | Integration | Total |
|---|---|---|---|
| Phase 3 gate (`edc4054`, 2026-08-28) | 44 | 211 | 255 |
| P4-14 closure pack (`4135d52`, 2026-08-29) | 47 | 346 | 393 |
| **Phase 4 gate passed (`76aaa7d`, 2026-08-29)** | **47** | **348** | **395** |

The final +2 are P4-15's `ReceiveAndAdjustConcurrencyTests`, added by the gate review itself.

Unit grew by 3 — `InventoryLayoutTests` (P4-12), extending `ProcurementLayoutTests`' three
assertions (declared size vs. work area, no starved grid at minimum size, ascending TabIndex)
to the Inventory client's window. Integration grew by 135 across Tracks A–E: the ledger
reconciliation assertion itself and its induced-drift proof (P4-01); receiving, partial
receiving, over-receiving and returns (P4-02, P4-05–P4-08); counts, adjustments and low-stock
(P4-03, P4-09–P4-11); and the isolation-level proof (P4-04) — all against the real pinned
MariaDB 10.4.32, never a substitute. 0 skipped in both suites at every capture this phase.

**No MSB3030 recurrence** (CARRY-04, watched since the Phase 3 gate) across this phase's
clean-clone runs, including the two full runs behind `p4-14-clean-clone.log`. One clean run is
not evidence the fragility is gone — CARRY-04 stays a watch item, not a closed one.

---

## 6. ADR status at the Phase 4 gate

**Two entries this phase owed, both checked directly rather than assumed:**

- **ADR-021** (`docs/adr.md` line 1014) — *Ledger reconciliation is a standing automated
  assertion, not a report* — raised and settled at P4-01. `**Status:** ACCEPTED` at line 1016.
- **ADR-006's amendment** (line 347, inside the existing ADR-006 entry, not a new numbered
  ADR) — the P4-04 progress note stating the mechanism explicitly: a session-level
  `tx_isolation` `SET` does not survive `BeginTransaction`, and passing
  `IsolationLevel.ReadCommitted` at every call site is the only form that holds. ADR-006's own
  `**Status:**` line (313) reads `ACCEPTED` throughout — it was never moved to `PENDING` for
  the amendment, consistent with the ADR's own reasoning that the concurrency guarantee never
  depended on isolation level in the first place (row-locking on the conditional write did).

**Every `**Status:**` line in `docs/adr.md` outside the template reads `ACCEPTED`** — checked
by pattern match across the whole file (25 status lines outside the template fence, all
`ACCEPTED`, two with a stated qualifier that is itself a resolved state, not a live `PENDING`).
The only `PENDING` string remaining in the file is the placeholder inside `## Template for new
entries`.

**ADR-021 is appended before the template section, not inside its fence** — `## ADR-021`
starts at line 1014, `## Template for new entries` at line 1049. Checked by line number, the
exact regression P2-13 once had to repair (ADR-015–018 rendering as a code block) — it has not
recurred.

---

## 7. What Phase 4 did not touch

The reconciliation query's own design (ADR-021) and the transaction pattern it audits
(ADR-006) are this phase's contributions; everything they consume — the conditional-decrement
mechanism (P1-11/P1-13), the purchase-order status machine and `CanTransition` (P3-01,
ADR-020), the audit pipeline (P2-04), the error envelope (ADR-014), and idempotency (ADR-007)
— is Phase 1–3 work this phase built on top of, under the same "frozen: no phase below may
re-litigate" rule `tasks.md`'s own header states for this phase.

**Carried, not owed here** (`tasks.md`'s own carried-forward section, unchanged by this
phase): **P0-02** and **P0-05** — Phase 6 gate (ADR-016). **P0-07**'s remaining structure box —
Phase 7. **CARRY-02** — Phase 6 gate (ADR-019). **CARRY-04** — a watch item, not a card, unless
it recurs (§5 above). **CARRY-01** (account recovery) — now carried through **three** phases
without an owner (Phase 2 → 3 → 4); `tasks.md` itself flags this as the point past which it
must not be allowed to reach a fourth without Phase 6 planning placing it.
