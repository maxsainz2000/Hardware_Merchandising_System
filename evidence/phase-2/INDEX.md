# Phase 2 — Identity and master data · Evidence Index

**Built at P2-13, 2026-08-25, against commit `229748e`.**
**Spec:** `documentations/Merchandising System for a Mid-Scale Hardware Store.md` §9, §10, §11, §12, §17
**Plan:** `plan.md` §7 (Phase 2), §10 (risk register mapping)

---

## How to read this file

This index maps every Phase 2 exit criterion and every task card to a file that exists on
disk, and records the gap-register outcome for **G-19** and **G-21**. It is the artifact
the Phase 2 gate's criterion *"G-19 and G-21 closed in the gap register"* asks for — Phase
1 recorded the same thing in `../phase-1/INDEX.md` §5, and this continues that register
rather than starting a new one.

The three rules from the Phase 1 index apply here unchanged, because an index is only worth
as much as its weakest row:

1. **Every artifact path was checked to exist**, and to be non-empty.
2. **No row says "verified informally."** Where a claim rests on inspection rather than
   execution, the row says so and is not counted as proven.
3. **Where the evidence proves something narrower than the row's wording, the row records
   the narrower claim** — marked ⚠ rather than ✅, and explained in §4.

Status marks: ✅ proven by executed evidence · ⚠ proven, with a stated limit on the claim ·
⬜ not proven.

---

## 1. Phase 2 exit criteria → artifact

From `plan.md` §7 (Phase 2 *Exit*) and the `Phase 2 exit gate` checklist in the Phase 2
`tasks.md`.

| # | Criterion | Card | Artifact | Status |
|---|---|---|---|---|
| 1 | Every cell of the role-permission matrix has a passing test, **including the negative cells** | P2-03 | `p2-03-matrix-results.txt` | ⚠ see §4.1 |
| 2 | Product SKU and barcode uniqueness enforced at the database **and** the API, both proven under concurrency | P2-06, P2-07 | `p2-06-schema.txt`, `p2-06-precision.txt`, `p2-07-uniqueness.txt` | ✅ |
| 3 | A price change writes `PriceHistory` and audit **atomically**, proven by forced failure | P2-08 | `p2-08-atomicity.txt`, `p2-08-price-history.txt` | ✅ |
| 4 | Deactivation preserves historical references; referenced master data cannot be deleted | P2-09 | `p2-09-lifecycle.txt` | ✅ |
| 5 | Seed data loads on a clean database, idempotently | P2-11 | `p2-11-seed-clean-db.log` | ✅ |
| 6 | `docs/role-permission-matrix.md` and `docs/database-design.md` written | P2-12 | `../../docs/role-permission-matrix.md`, `../../docs/database-design.md` | ✅ |
| 7 | G-19 and G-21 closed in the gap register | P2-13 | this file, §3 | ⚠ see §3 |
| 8 | ADR-017 and ADR-018 ACCEPTED | P2-02, P2-06 | `../../docs/adr.md` | ✅ |
| 9 | Clean-clone build and both test suites green | P2-13 | `p2-13-clean-clone.log` | ✅ |

---

## 2. Task card → evidence

| Card | Title | Artifact | Status |
|---|---|---|---|
| P2-01 | Migration 0005 — `PermissionPolicies`, identity model | `p2-01-migration.log`, `p2-01-grants.txt` | ✅ |
| P2-02 | Spec §9 role matrix as authorization policies | `p2-02-policy-registration.txt` | ✅ |
| P2-03 | Every negative cell of the matrix has a passing test | `p2-03-matrix-results.txt` | ⚠ §4.1 |
| P2-04 | Audit becomes one server-side pipeline component 🎯 | `p2-04-audit-pipeline.txt`, `p2-04-rollback-regression.txt` | ✅ |
| P2-05 | `SystemSettings` read/write, audited | `p2-05-settings-audit.txt` | ✅ |
| P2-06 | Migration 0006 — product master tables | `p2-06-schema.txt`, `p2-06-precision.txt` | ✅ |
| P2-07 | Product CRUD, uniqueness at DB **and** API | `p2-07-uniqueness.txt` | ✅ |
| P2-08 | Price change writes `PriceHistory` + audit atomically | `p2-08-atomicity.txt`, `p2-08-price-history.txt` | ✅ |
| P2-09 | Active/inactive lifecycle preserves references | `p2-09-lifecycle.txt` | ✅ |
| P2-10 | Supplier master with the same lifecycle rules | `p2-10-suppliers.txt` | ✅ |
| P2-11 | Seed data loads on a clean database | `p2-11-seed-clean-db.log` | ✅ |
| P2-12 | Phase 2 documents | the two `docs/*.md` | ✅ |
| P2-13 | Closure pack — suite green, clean clone, this index | `p2-13-clean-clone.log`, this file | ✅ |

**P2-01 and P2-03 stood at 🟡 until P2-13.** Both had a single unticked box — *integration
suite green* — held open by one failing test, `Backup_Succeeds_WritesDumpWithChecksumThat-
Verifies`, which is Phase 1 backup code neither card touches. Both cards were correct to
leave it unticked rather than round it up. ADR-019 resolved the underlying defect and
`p2-13-clean-clone.log` §3 carries the green run that closes both boxes: **24/24 unit,
136/136 integration, 0 skipped.**

---

## 3. Gap register — the Phase 2 rows

Continuing `../phase-1/INDEX.md` §5, which left both of these rows open with an explicit
pointer to this phase.

| Gap | Phase 1 left it at | Phase 2 cards | Artifact | Status now |
|---|---|---|---|---|
| **G-19** · Authentication and role controls lacked implementation detail | ⚠ *full role matrix → Phase 2* | P2-02, P2-03, P2-04, P2-05 | `p2-02-policy-registration.txt`, `p2-03-matrix-results.txt`, `p2-04-audit-pipeline.txt`, `../../docs/role-permission-matrix.md` | ⚠ **closed for Phase 2's scope** — one spec §23 component surfaced with no owner, §4.3 |
| **G-21** · Data model lacked invariants and precision | ⚠ *full model → Phase 2/4* | P2-01, P2-06, P2-07, P2-08, P2-09, P2-10, P2-12 | `p2-06-schema.txt`, `p2-06-precision.txt`, `p2-07-uniqueness.txt`, `p2-09-lifecycle.txt`, `../../docs/database-design.md` | ⚠ **→ Phase 4 by `plan.md`'s own assignment**, §4.2 |

Spec §23's remediation wording for each, component by component:

**G-19** — *"Define password hashing, lockout, token/session behavior, policy authorization,
account recovery, and audit rules. Verified by security tests and role-permission matrix."*

| Component | Where | Status |
|---|---|---|
| Password hashing | P1-08 | ✅ |
| Lockout | P1-08 — `AuthenticationPolicy.LockoutDuration` = 15 min, `LockedUntilUtc` set by the same conditional `UPDATE` that counts the attempt | ✅ |
| Token/session behavior | ADR-005, P1-08 | ✅ |
| Policy authorization | P2-02 — 29 named policies, `Area.Action`, single registry (ADR-017) | ✅ |
| **Account recovery** | **nowhere** | ⚠ **§4.3** |
| Audit rules | P2-04 — one pipeline component, in the caller's transaction | ✅ |
| Role-permission matrix | P2-12 — generated from the registry, drift-checked by a unit test | ✅ |

**G-21** — *"Define keys, foreign keys, unique constraints, indexes, decimal precision, time
zone, lifecycle/status rules, and migration checksums. Verified by approved ERD/data
dictionary and integration tests."* Every component has an artifact above. The row stays ⚠
only because `plan.md` §7 assigns the *finalised* `database-design.md` to **Phase 4**, and
P2-12's card says the same in its own wording ("first complete draft … finalised in Phase 4,
not here"). This is the plan operating as written, not a shortfall.

Gaps G-06, G-08, G-15, G-16, G-17, G-22, G-23, G-24, G-25, G-26, G-27, G-28, G-30 remain
assigned to Phases 3–7 by `plan.md` §7 and §10, and are correctly untouched here.

---

## 4. Where the claim is narrower than the wording

### 4.1 Criterion 1 — "every cell" is 4 operations of 29, and the mechanism is what carries the rest

`PolicyRegistry.Definitions` holds **29 operations**. Only **4** have a live HTTP endpoint
at the end of Phase 2: `Diagnostics.AdminPing`, `Maintenance.Perform` (two routes),
`Adjustments.Request`. Every (role, operation) cell for those four is asserted positive and
negative in `p2-03-matrix-results.txt`. The other 25 have no endpoint to authorize against
until Phase 3 and Phase 5 build them.

What makes this safe rather than a hole is P2-03's fourth done-when box: the matrix suite is
**data-driven from the registry itself**, so an endpoint added without a policy fails the
suite rather than passing untested. The obligation is therefore transferred to each future
card by a mechanism, not by a note — which is the same design principle P2-04 applied to
audit. Recorded here so that nobody later reads criterion 1 as "all 29 were proven in Phase
2." They were not, and could not have been.

### 4.2 Criterion 6 / G-21 — `database-design.md` is a first draft by design

`plan.md` §7 assigns the finalised document to Phase 4, after receiving and the movement
ledger exist. The draft covers every table created through migration `0007`.

### 4.3 G-19 — account recovery has no implementation and no card, in any phase

**This surfaced at the gate and is new information.** Of spec §23's seven G-19 components,
six are proven above. *Account recovery* is not, and the honest statement is that no card in
any phase was ever written for it:

- **Lockout self-recovers.** `LockedUntilUtc` is set to `UtcNow + 15 minutes`, so the
  lockout scenario — the common one — needs no operator action. That much is implemented and
  tested.
- **A forgotten password has no path.** ADR-017 §4 places all user management on the
  `Merchandising.Maintenance` CLI rather than any HTTP surface, and that CLI has
  `create-user` but **no `reset-password` and no `unlock-user`** (`Program.vb`'s command
  `Select Case`: `migrate`, `create-user`, `seed-demo`, `seed`, `backup`, `restore`). An
  operator who forgets the Super Admin password today has no supported recovery route.

**This is not counted as a Phase 2 failure**, and the reason is specific: no Phase 2 card
scoped it, `plan.md` §7's Phase 2 *Build* list does not mention it, and treating it as a
blocker would be inventing scope after the fact. It is carried into the Phase 3 `tasks.md`
as an unassigned open item so that it is visible rather than lost. The natural home is
**Phase 6** (operations, alongside backup/restore), as a `reset-password` subcommand on the
same CLI that already creates accounts — but that is a decision, not something this index
gets to make.

### 4.4 The off-host backup copy, volume present

Not proven by any assertion, in this phase or Phase 1 — see `p2-13-clean-clone.log` §4 for
the four-test coverage audit that establishes this, and **ADR-019** for the decision and the
assertion Phase 6 now owes. The claim rests on
`../phase-1/p1-17-backup-success.log`, captured 2026-08-22 against the real `D:` volume.

### 4.5 Clean clone, not clean machine

`p2-13-clean-clone.log` proves a fresh clone builds and tests green **on a host already
configured**. A machine the author has never touched is a Phase 6 exit criterion (ADR-012).
Same limit P1-20 recorded; the claim has not grown.

---

## 5. Test-suite growth across Phase 2

| Point | Unit | Integration | Total |
|---|---|---|---|
| Phase 1 gate (`41ee833`, 2026-08-22) | 23 | 62 | 85 |
| Phase 2 gate (`229748e`, 2026-08-25) | **24** | **136** | **160** |

Integration coverage grew by 74 tests across twelve cards, against the real pinned MariaDB
10.4.32 throughout — never a substitute, never an in-memory provider. 0 skipped in both
suites at the gate: nothing was excluded or marked inconclusive to reach green.

---

## 6. ADR status at the Phase 2 gate

**28 entries — 20 top-level (ADR-000 … ADR-019) plus 8 sub-entries** (003.1, 003.2, 004.1,
009.1, 009.2, 011.1, 013.1, 015.1) — carrying **24** `Status:` lines between them, and every
one reads **ACCEPTED**. No entry is PENDING. The only `PENDING` string in `docs/adr.md` is
the placeholder at line 929, inside the *Template for new entries* fence.

Resolved in this phase: **ADR-017** (policy naming and the matrix's single source of truth),
**ADR-018** (unique active barcode without a partial index), **ADR-019** (the backup
checksum test's hardware dependency).

**A `docs/adr.md` formatting defect was repaired at P2-13**, and is worth recording because
it had been silently in place for four entries: ADR-015 through ADR-018 had been appended
*inside* the ```markdown fence belonging to the *Template for new entries* section, so five
real decisions rendered as a code block rather than as document content. The template now
sits last, in its own fence. Anyone appending ADR-020 should add it **before** the template
section, not before the closing fence.
