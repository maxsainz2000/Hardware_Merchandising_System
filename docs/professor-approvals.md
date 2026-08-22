# Course Constraints and Project Decisions

**Purpose.** A citable record of the constraints this project is built under, and of the decisions that were once mis-filed as requiring the instructor's approval. Referenced at acceptance (spec §25) and by ADR-000.

**The rule set is two lines long:**

1. **All application source code is Visual Basic .NET.**
2. **The database is MariaDB as supplied by XAMPP.**

Anything that does not violate those two is a decision for this project to make, record, and defend on its own evidence. Filing such a decision as "pending professor approval" does not make it safer — it makes it blocked on someone who was never asked and has no stake in the answer.

**Corrected 2026-08-22.** This file previously listed five `PA-` entries under one heading and treated them alike. Three of them (precision, performance targets, recovery targets) were never approvals at all — they were engineering decisions the spec had voluntarily labelled "subject to professor approval." That label was holding task P1-07 open at 🟡 with every technical box ticked and its evidence captured. The entries keep their `PA-` identifiers, which are cited from `docs/adr.md`, `plan.md` and `tasks.md`, but are now filed under what they actually are.

**What still genuinely belongs to the instructor:** final acceptance and grading of the delivered system (spec §19, §25). That is sign-off on a deliverable, not permission to make a technical choice.

---

## Part 1 — Course constraints

These were handed down. They are not requests that were granted, and there is nothing to "prove approval of" — a constraint is cited, not evidenced.

### PA-001 · Visual Basic .NET is the implementation language

**Status:** 🔒 BINDING CONSTRAINT

All application source in this repository is Visual Basic .NET. There is no C# escape hatch. Permitted non-VB artifacts: XAML markup, SQL migration scripts, PowerShell build scripts, JSON/XML configuration, Markdown documentation.

**On the hand-authored API.** This entry previously recorded a question — "is a manually authored VB ASP.NET Core Web API acceptable, given Visual Studio provides no VB template for it?" — and its answer, yes. On the rule set above, the question dissolves: **a hand-authored VB API does not violate *Visual Basic only*, so it never required permission.** Visual Studio's template coverage is a tooling gap, not a course rule. ADR-001 records how the project is authored; that is a technical record, not an exception.

**Consequence.** Fallback rung C in `plan.md` §1.1 (a non-VB shim) is closed — not because an exception was refused, but because it would violate constraint 1. Rung B remains the last self-service option.

**Enforced by:** guardrail G-A, hooks L1–L4, `scripts/check-no-csharp.ps1`. The constraint is machine-checked on every write and every commit, which is a stronger citation than a screenshot.

---

### PA-002 · MariaDB via XAMPP is the database

**Status:** 🔒 BINDING CONSTRAINT

MariaDB is used only as supplied by XAMPP. No standalone MariaDB, MySQL, or other engine may be substituted — including for testing.

**Consequence.**

- Apache Friends documents XAMPP as intended for development environments. The system is therefore described as an **academic prototype** throughout the documentation and in the final presentation. That is the accurate description of the deliverable, not defensive hedging — and per ADR-012, no business runs this software.
- Hardening is required rather than optional: unused XAMPP components disabled, MariaDB bound to loopback, least-privilege accounts (ADR-013), phpMyAdmin not exposed to the store LAN and never to the public internet.
- XAMPP is a net positive for this delivery model: three classmates can install the entire database stack from one download.

**Enforced by:** ADR-002's pinned versions, measured on this machine, not quoted.

---

## Part 2 — Decisions previously mis-filed as pending approvals

Each of these is now decided on its own evidence and rationale. None of them violates constraint 1 or 2, so none of them needed asking.

### PA-003 · Numeric precision — `DECIMAL(19,4)` money, `DECIMAL(19,3)` quantity

**Status:** ✅ DECIDED — 2026-08-22 · recorded as ADR-004

**Decision.** Keep the baseline. Money `DECIMAL(19,4)`, quantity `DECIMAL(19,3)`, timestamps `DATETIME(6)` UTC.

**Rationale.** Three decimal places on quantity covers what a hardware store actually sells in fractions — grams of nails, centimetres of cable, millilitres of paint tint. Four on money covers sub-centavo unit costs (₱0.1995 per screw) that must survive multiplication before the line total rounds. Nineteen total digits is far more headroom than this store needs and costs nine bytes. Proven by measurement rather than assumption: `0.001` and `12345678901234.5678` both round-trip exactly through the real `StockBalances.Quantity` and `Products.Price` columns on the pinned MariaDB 10.4.32 instance — `evidence/phase-1/p1-07-precision-check.txt`.

**The storage width was never the risk.** Scale enforcement was, and it is already closed: `STRICT_TRANS_TABLES` rounds an over-scale decimal silently (`Note 1265`, not an error), so `Merchandising.Domain.DecimalScaleGuard` rejects or explicitly rounds at the API boundary before any parameter is bound. See ADR-004.1.

**Consequence.** `Double` and `Single` are forbidden for money and quantity everywhere — storage, calculation, transport. Use `Decimal` in VB. The P1-07 acceptance box requiring this be "raised with the professor" is removed; the card closes on its technical evidence.

---

### PA-004 · Performance targets and load profile

**Status:** ✅ DECIDED — 2026-08-22 · validate at Phase 7

**Decision.** Retain the concurrency figure, tighten the latency targets, and replace the session-count profile with a contention profile.

| | Target | Hard-fail ceiling |
|---|---|---|
| Ordinary reads | p95 ≤ **300 ms** | 2 s (spec baseline) |
| Stock-changing commands | p95 ≤ **800 ms** | 4 s (spec baseline) |
| Concurrent sessions | 5–10 | — |

**Why tighten.** The original 2 s / 4 s bars are unfalsifiable on this system. A local MariaDB read over a private LAN with a small catalogue lands in tens of milliseconds; passing by fifty times proves nothing on stage and hides a regression that would still "pass." The spec's original figures are retained as the ceiling that constitutes failure, so nothing is weakened — a number that can actually be missed is added above it.

**Load profile — the part that was missing.** "5–10 concurrent sessions" specifies nothing testable: ten idle clients exercise no code. The Phase 7 profile is **contention, not session count**:

- N concurrent stock-changing commands (N = 10) against **the same product row**, issued simultaneously.
- Assertions: stock never goes negative; no duplicate `StockMovements` rows; every loser returns a controlled **409**, never a timeout, a deadlock trace, or a partial commit; the sum of applied deltas reconciles exactly with the closing balance.
- This is the path that exercises the conditional `UPDATE`, the affected-row check, and the idempotency key — the three mechanisms the architecture actually rests on.

**Consequence.** Phase 7 load testing measures the table above against the contention profile. Measured results are recorded as evidence regardless of outcome.

> **Numbering note.** `docs/adr.md` (ADR-012, Rejected) once referred to a withdrawn XAMPP escalation as "draft PA-004." That draft was never filed and the number was never issued to it. `PA-004` is this entry.

---

### PA-005 · Recovery targets

**Status:** ✅ DECIDED — 2026-08-22 · validate at Phase 6

**Decision.**

| | Target |
|---|---|
| RPO | ≤ **24 hours** (one scheduled `mysqldump`) |
| RTO — demonstrated | restore to verified state ≤ **15 minutes**, performed live on the demo host |
| RTO — documented worst case | ≤ **120 minutes** for a full host rebuild |

**Why the split.** RPO ≤ 24 h stands as written: it costs one scheduled dump and it is what a real deployment would want. RTO ≤ 120 minutes does not stand as the primary measure — it is an untestable claim inside a presentation, and per ADR-012 no business accrues data here, so a two-hour recovery window commits to nothing anyone experiences. What must be true is that backup and restore **work and are demonstrable in front of an audience**. Fifteen minutes, measured on the classmates' host from a real dump file, is a claim that can be made on stage and defended. The 120-minute figure is retained as the documented worst case for rebuilding a host from bare Windows.

**Consequence.** Phase 6 records the measured restore time against the 15-minute demonstrated target. The restore is rehearsed on a demo workstation, not on a lab machine (ADR-012).

---

## Part 3 — Genuinely open decisions

Still undecided, and still nobody's approval to give.

### PA-006 · Cash tender rounding at POS

**Status:** ⬜ OPEN — decide before the POS payment slice

**The question.** Line totals compute exactly and round to two decimal places. But ₱0.01 and ₱0.05 coins are effectively out of circulation in Philippine retail, so the amount a drawer can actually take and return does not match the amount the arithmetic produces. Without a stated rule the cashier improvises, and the drawer stops reconciling — visible on stage during the demo.

**Recommendation.**

- Compute and store the sale exactly. Round the **cash amount due** to the nearest increment, and store the difference as its own `CashRoundingAdjustment` amount on the payment so the ledger reconciles to the centavo.
- Make the increment a `SystemSettings` value rather than a constant. Default **₱0.05**; ₱0.25 and ₱1.00 are the other plausible settings and stores genuinely differ.
- Apply the rounding to **cash only**. Non-cash tenders settle exactly and must not be adjusted.
- Round once, against the payable total — never per line.

**Why it is not just a display concern.** The adjustment is real money leaving or entering the drawer. If it is not a stored, named amount, it surfaces later as an unexplained variance in the cash-closing report with no way to attribute it.

---

## Template for new entries

    ## PA-NNN · Short title

    **Status:** ⬜ OPEN | ✅ DECIDED | 🔒 BINDING CONSTRAINT
    **Date:** YYYY-MM-DD

    **Decision.** What was chosen.

    **Rationale.** Why — on evidence, not preference.

    **Consequence.** What changes in the project as a result.

    **Enforced by / Evidence:** guardrail, test, or path under evidence/.

> Before adding an entry, check which of the three parts it belongs in. If it does not violate *Visual Basic only* or *MariaDB via XAMPP only*, it is a Part 2 or Part 3 decision — make it, record the reasoning, and move on.
