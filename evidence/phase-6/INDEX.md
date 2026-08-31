# Phase 6 — Reporting and operations · Evidence Index

**Captured** 2026-08-31 on `LAPTOP-3HH6OHHE` · **Card** P6-18 · **Commit at capture** `af9ef1c`

Phase 6 built the twelve reports of spec §14 with a reconciliation harness that compares two
independent code paths, CSV export with a measured Excel round trip, backup and restore promoted
to the control table spec §15 states, build identity on the health endpoint, the release manifest,
and account recovery on the Maintenance CLI. **Fourteen of seventeen cards are closed.** The three
that are not are machine-dependent and moved to Phase 8 by **ADR-030** — §6 below.

---

## How to read this file

Three gates have now been failed by a claim outrunning its evidence, and **the Phase 5 gate had a
perfect artifact-existence record when it failed.** So this index does not claim a file exists. For
every row it claims the file exists **and contains what it is cited for**, and §4 records every
place where the claim is narrower than the wording it is answering.

Two rules were applied mechanically rather than by eye:

1. **Every evidence path cited anywhere in `tasks.md` and in the four Phase 6 documents was
   resolved against disk and read for the claim it is cited for.** 17 paths in `tasks.md`, 7
   cross-phase paths in the documents. Results in §2 and §3.
2. **Every quantitative claim was checked against the number it cites** rather than repeated.
   Where a number disagreed with an earlier document, both are shown — see §5.

---

## 1. Phase 6 exit criteria → artifact

From `plan.md` §7 as amended by ADR-030.

| # | Exit criterion | Cards | Artifact | Status |
|---|---|---|---|---|
| 1 | Every report reconciles to source data | P6-01 – P6-05 | `p6-01-report-definitions.txt`, `p6-02-sales-reports.txt`, `p6-03-returns-and-performance.txt`, `p6-04-procurement-reports.txt`, `p6-05-stock-reports.txt` | ✅ |
| 2 | CSV round-trips through Excel without mangling | P6-06 | `p6-06-csv-export.txt`, `p6-06-excel-roundtrip/` | ✅ |
| 3 | Backup/restore meets the documented RPO/RTO with measured evidence | P6-07, P6-08 | `p6-07-backup-retention.txt`, `p6-08-restore-timed.txt` | ✅ ⚠ §4.2 |
| 4 | G-22 and G-15 closed in the gap register; G-16 recorded as one half closed, one half open | P6-18 | §3 below | ✅ |
| 5 | `report-specification.md`, `backup-restore-guide.md`, `user-guide.md` written; `installation-guide.md` current | P6-01, P6-09, P6-15 | `p6-01-…`, `p6-09-backup-restore-guide.txt`, `p6-15-user-guide.txt`, `p6-11-release-manifest.txt` | ✅ |
| 6 | Clean-clone build and both test suites green | P6-18 | `p6-18-clean-clone.log` | ✅ |
| 7 | Every task done — no card left 🟡 and rounded up | — | §6 | ✅ ⚠ §6 |

**Moved to the Phase 8 gate by ADR-030, struck from this list rather than silently dropped:**
a clean installation on a fresh machine (→ **P8-03**), every demo workstation captured including
admin rights (→ **P8-01**, P0-02), and `ping MERCH-HOST` + HTTPS round trip from every workstation
with the network brought up from cold (→ **P8-02**, P0-05).

### Criterion 6, in full

`p6-18-clean-clone.log`, commit `af9ef1c`, clone at `C:\p6clone` outside the repository:

```
bin/obj at clone : 0 directories - verified by enumeration before building
Command          : pwsh -NoProfile -File <clone>\scripts\run-tests.ps1
G-A..G-D           All guardrails passed.
Build              0 Warning(s)  0 Error(s)         Time Elapsed 00:00:12.80
Unit               Failed: 0, Passed: 140, Skipped: 0, Total: 140
Integration        Failed: 0, Passed: 512, Skipped: 0, Total: 512   (6 m 5 s)
run-tests.ps1 exit code : 0
```

Against the real pinned MariaDB 10.4.32, not a substitute. The clone root is deliberately short:
ADR-029 measured `LongPathsEnabled = 0` on this machine and a 153-character safe-root ceiling, so
a clone under the session scratchpad reproduces P6-17's `MSB3030` instead of testing the tree.
**That is P6-17's own finding consumed one card later, which is what it was recorded for.**

---

## 2. Task card → evidence, with the content check

Every row was opened and read for the specific claim named, not merely stat-ed.

| Card | Evidence | Content check performed | Result |
|---|---|---|---|
| **P6-01** 🎯 | `p6-01-report-definitions.txt` (180 ln) | Both required day-boundary examples present with real timestamps — `23:59:59 Asia/Manila = 15:59:59 UTC` (line 90) **and** the `02:00:00` cross-midnight divergence case (line 91), which is the one that actually bites. Both watched-fails present and named: off-by-one boundary (lines 55, 59) and current-cost join (lines 63, 67). | ✅ |
| **P6-02** | `p6-02-sales-reports.txt` (182 ln) | The `PaymentWordingTests` widening was watched fail against a literal planted `"Payment approved."` in the new directory (lines 45–68), and the plant was removed afterwards. | ✅ |
| **P6-03** | `p6-03-returns-and-performance.txt` (260 ln) | `ProductPerformance_MarginEstimate_IsLabelledInformational` and `ReturnsAndCancellations_DistinguishesRestockingFromNonRestocking` both named with one return carrying `RestocksItem=True` and one `False` (lines 127–132). | ✅ |
| **P6-04** | `p6-04-procurement-reports.txt` (146 ln) | Outstanding computed by summing `ReceiptLines` across several receipts and cross-checked against P3-06's accumulator column — two sources, not one (lines 33–40). | ✅ |
| **P6-05** | `p6-05-stock-reports.txt` (209 ln) | Correlation identifier carried (line 31); current stock vs movement report agreement asserted **for every product**, not one (lines 63–64). | ✅ |
| **P6-06** | `p6-06-csv-export.txt` (211 ln), `p6-06-excel-roundtrip/` (2 files, 9 954 ln CSV + 102 ln transcript) | UTF-8 BOM decision recorded in ADR-024 and asserted; the round trip is a **real** export read back, with the artifact committed, not described. | ✅ |
| **P6-07** | `p6-07-backup-retention.txt` (190 ln) | CARRY-02 closed against an **actually attached** `MERCHBACKUP` volume with the loud-skip rule intact (lines 82–90). Independently confirmed at this pack: the volume is attached at `D:` (8.0 GB), and the clean-clone run reports `Skipped: 0` — so this assertion genuinely executed rather than skipping. | ✅ |
| **P6-08** | `p6-08-restore-timed.txt` (213 ln) | RTO measured as a number: **7.17 s to verified state against a 900 s target (0.8 % of budget)**, with the row counts that verified it (969 users, 8 676 products, 6 566 balances, 16 136 movements, 72 114 audit rows). Checksum-mismatch refusal watched fail (lines 91–94). | ✅ ⚠ §4.2 |
| **P6-09** | `p6-09-backup-restore-guide.txt` (198 ln) | States the "run from the document rather than transcribed" rule is taken literally and shows the runs (lines 20–21). | ✅ |
| **P6-10** | `p6-10-build-identity.txt` (208 ln) | The pre-existing stale deployment read **before** this card's code existed (lines 50–71), and the watched-fail rehearsal in part 2. Independently re-confirmed at this pack — §4.4. | ✅ |
| **P6-11** | `p6-11-release-manifest.txt` (251 ln) | The runtime check forced into a genuine missing-runtime condition on the **unmodified** script via `$env:ProgramFiles = 'C:\NoSuchDir-P6-11-Forced-Check'`, producing `[WARN] .NET Desktop Runtime none installed` (lines 183–197). | ✅ |
| **P6-12** | `p6-12-account-recovery.txt` (178 ln) | `AuditLogs` rows queried directly through `mysql.exe` against the real database rather than through the test harness (lines 142–155); `NoHttpRoute_ReachesResetPasswordOrUnlockUser` passes (line 82). | ✅ |
| **P6-15** | `p6-15-user-guide.txt` (159 ln) | All five roles named individually — Super Admin, Admin, Procurement Officer, Inventory Clerk, Cashier — and all three clients (lines 125–126). | ✅ |
| **P6-17** | `p6-17-msb3030.txt` (264 ln) | Root cause confirmed by controlled test, not argued: 260-character source path at failure, 163-character path building clean, and the four measured suffixes with Procurement's 106 the longest (lines 54–143). | ✅ |
| **P6-18** | `p6-18-clean-clone.log`, this file | — | ✅ |

**Cross-phase paths cited by the four Phase 6 documents — all 7 resolved and non-empty:**
`p1-18-restore-log.txt` (10 677 B), `p1-18-verification-checklist.md` (4 264 B),
`host-ip-reservation.txt` (6 439 B), `p1-05-connection-test.log` (5 245 B),
`p1-09-cert-details.txt` (1 162 B), `p1-09-client-trust-steps.md` (3 871 B),
`p1-09-invalid-cert-behaviour.txt` (3 016 B).

---

## 3. Gap register — the Phase 6 row

Continuing `../phase-5/INDEX.md` §3, which left G-06, G-08, G-15, G-16, G-17, G-22, G-25, G-26,
G-27, G-28 and G-30 assigned to Phases 6–7.

| Gap | Mitigation (spec §23) | Phase 6 cards | Artifact | Status now |
|---|---|---|---|---|
| **G-22** · Report formulas and date boundaries were ambiguous | Define report calculations, returns treatment, cost basis, rounding, time zone, filters, permissions, and reconciliation samples. | P6-01 (definitions + harness), P6-02 – P6-05 (twelve reports), P6-06 (export) | `p6-01-report-definitions.txt` … `p6-06-csv-export.txt` | ✅ **closed** |
| **G-15** · Backup details were incomplete | Define tool, account, schedule, retention, protected folder, checksum, off-host copy, failure alert, and restore test. | P6-07, P6-08, P6-09 | `p6-07-backup-retention.txt`, `p6-08-restore-timed.txt`, `p6-09-backup-restore-guide.txt` | ✅ **closed** |
| **G-16** · Deployment runtime requirements were ambiguous | Name `win-x64`; record client Desktop Runtime and host API runtime/self-contained choice; verify at installation. **Evidence: release manifest *and* clean-machine installation test.** | P6-11 (manifest half) · **P8-03 owns the other half** | `p6-11-release-manifest.txt` | ⚠ **half closed** — §4.3 |

**G-22, closed.** The mitigation names eight things and each has an artifact:
calculations, returns treatment, cost basis, rounding, time zone, filters, permissions and
reconciliation samples are defined **once** in `docs/report-specification.md` and cited rather than
restated by each report. The reconciliation requirement `plan.md` §7 calls this phase's key design
call is met in its strong form: the harness compares the report total against **the existing detail
endpoint a user can open**, so the two answers come from different code paths over the same rows —
never the report's own query run twice. It was **watched fail** against a deliberately off-by-one
date boundary and a deliberately current-cost join *before any real report used it*, which is the
only way a reconciliation harness earns trust.

**G-15, closed.** Tool (`mysqldump.exe` — `mariadb-dump` does not exist in this XAMPP build),
account (`merch_backup`, never root), schedule, retention read from `SystemSettings` and asserted to
delete the oldest surplus rather than the newest, protected directory proven unreachable through the
API, checksum, off-host copy against a really-attached volume, failure alert watched fail against an
unwritable directory, and a restore test measured at 7.17 s. Every named element has its own row.

**G-16 is half closed and is not claimed otherwise.** §4.3.

Gaps G-06, G-08, G-17, G-27, G-28 and G-30 remain assigned to Phase 7, and G-16's installation half
plus G-27's UAT half to Phase 8, by `plan.md` §10 as amended by ADR-030. They are correctly
untouched here.

---

## 4. Where the claim is narrower than the wording

### 4.1 Phase 6 introduced three analyzer warnings and no card caught them — found and fixed by this pack

The first Done-when box requires a clean clone that **builds at 0 warnings**. It built at three:

```
UserGuideDocumentationTests.vb(266,13)  MSTEST0037  Use 'Assert.DoesNotContain'  -> P6-15 (a791d88)
UserGuideDocumentationTests.vb(270,13)  MSTEST0037  Use 'Assert.DoesNotContain'  -> P6-15 (a791d88)
CsvExporterTests.vb(394,13)             MSTEST0037  Use 'Assert.HasCount'        -> P6-06 (68e6318)
```

`../phase-5/p5-16-clean-clone.log` §2 records `0 Warning(s)` and `p6-11-release-manifest.txt` line
235 records the same, so these arrived later in Phase 6 and **passed through two cards whose boxes
claimed both suites green** — because "suites green" and "0 warnings" are different claims and only
the first was being checked. Closed at commit `af9ef1c`.

**The fix was watched fail, because a green suite could not have caught the real risk.** Reversing
`Assert.DoesNotContain`'s arguments would search the short substring for the long file text and pass
vacuously — green either way, and the assertion silently dead. The replacement was therefore planted
with a token present in every controller file:

```
Assert.DoesNotContain failed. String '... AdjustmentsController' does contain string 'Imports'.
'substring' expression: '"Imports"', 'value' expression: 'text'.
```

confirming it searches `text` for the substring and not the reverse. The plant was reverted
byte-clean — `git diff` showed only the three intended lines. Both replacement APIs were already in
use in this repository (`AuthenticationTests.vb:263`, `AuthorizationPolicyRegistrationTests.vb:41`),
so the signatures are settled rather than guessed.

### 4.2 ⚠ P6-08's measured RTO covers the database portion, not the full operator window

`p6-08-restore-timed.txt` lines 132–141 states this itself and is quoted rather than paraphrased:
stopping and restarting the API Windows Service, and the operator steps around them, are **not** in
the 7.17 s. `RestoreCommandTests` measures streaming the dump into `mysql.exe` plus verification;
`Restore_ThroughRealMaintenanceWorkflow_RecordsCompletedRestoreEvent` proves the surrounding
maintenance-mode workflow is real end to end but does not stop or restart the service either — that
needs an elevated session, the same boundary P1-18 recorded.

**Why this is recorded as ⚠ rather than a defect.** The card's box asks for the elapsed time
measured, recorded as a number, and compared against the 15-minute target explicitly. It is: 7.17 s
against 900 s. The *exit criterion* is the broader claim, and at 0.8 % of budget the remaining
margin is large — but the number does not cover the whole window and this index will not imply it
does. The residual risk lives in service restart time and operator steps, which **P8-04's timed
rehearsal is the first thing that will measure end to end.**

### 4.3 ⚠ G-16 is half closed — recorded as two halves, never as one gap

G-16's spec §23 evidence column names two artifacts: *"Release manifest and clean-machine
installation test."* The manifest half is closed here at P6-11, with the client runtime check forced
into a genuine failure on the unmodified script. **The clean-machine installation test half is open
and owned by P8-03**, because it needs a machine the author has never configured.

`docs/installation-guide.md` §7.3 previously read *"G-16 is now **closed** jointly by this section
and P6-16's clean-installation criterion"* while that second half had never been performed. That
line was a claim outrunning its evidence of exactly the kind that failed three gates, and it was
corrected at this card to state the split.

### 4.4 CARRY-05's mechanism went red on a real staleness for the first time, at this pack

The deployed service was found stale **again** — seven commits behind:

```
git rev-parse HEAD : 7f7255f
deployed commitSha : aad3662   (P6-10 part 1)
FAIL STALE DEPLOYMENT
```

The difference from the Phase 4 and Phase 5 gates is the whole point of CARRY-05: **this was found
by a script in two seconds, not by hand.** P6-10 was watched fail synthetically; this is its first
detection in anger, and it validates ADR-027's argument that in-process integration tests cannot
catch this class of defect — 512 integration tests passed against a service seven commits stale,
because they never touch it.

Redeployed to `C:\MerchandisingService\Api-sc-7f7255f` (self-contained, 350 files, manifest
`commitSha` verified `7f7255f`), reinstalled by the operator in an elevated session — the same
boundary `../phase-5/INDEX.md` §4.4 recorded — and re-verified:

```
git rev-parse HEAD : 7f7255f
deployed commitSha : 7f7255f
OK   Deployed build matches the current commit.
```

**Re-verified after the closure commit, so this box is not one commit behind.** A closure pack that
commits makes its own deployment stale by construction, and this one did so twice — `af9ef1c` then
`20479ae`. Rather than tick the box against a superseded commit and caveat it, the build was
republished and reinstalled by the operator in an elevated session after the closure commit, and
checked again:

```
git rev-parse HEAD : 20479ae
deployed commitSha : 20479ae
OK   Deployed build matches the current commit.
```

Service `MerchandisingApi`: Running, StartType Automatic. Published self-contained, 350 files,
`release-manifest.json` `commitSha` = `20479ae`, staged at
`C:\MerchandisingService\Api-sc-20479ae`.

**The one thing that remains true and is not claimed away:** any commit made after this line is
written makes the deployment stale again. That is not a defect to be engineered out — it is why
P6-10 exists, and `scripts/install-service.ps1 -CheckOnly` is the two-second check that answers it
at any moment, including at the gate.

### 4.5 A finding this pack made about its own method, not a defect in the tree

The first clean-clone run reported **9 integration failures out of 512** and they were entirely the
harness's fault. That run used hand-rolled `dotnet build`/`dotnet test` calls with `-c Release`. The
forced-failure rollback proofs are guarded by `#If DEBUG Then` in `StockService`,
`AdjustmentService` and `PriceChangeService` — P1-12 recorded that the fault injection is
"structurally inert in a Release build" as a *feature* — so a Release run fails all eight
`FaultInjected*_RollsBack*` tests plus `Decrement_OpensTransactionAtReadCommittedIsolation`.

`scripts/run-tests.ps1` defaults to `Debug` for exactly this reason, and
`../phase-5/p5-16-clean-clone.log` shows Phase 5 invoked **that script** rather than raw `dotnet`
calls. The run was redone the same way and returned 512/512.

**Recorded because the near-miss is instructive:** a closure pack that had reported those 9 failures
as a tree defect would have raised a false alarm at the gate, and one that had *fixed* them by
removing the `#If DEBUG` guard would have destroyed a real safety property to satisfy a broken
harness. **Use `scripts/run-tests.ps1`. Do not hand-roll the invocation.**

### 4.6 ⚠ P6-06's live-export artifact has drifted in the working tree, and the tree is not clean at the gate

`p6-06-excel-roundtrip/current-stock-live-export.csv` was **already modified and uncommitted when
this pack began** — it is not this card's change. Committed: 8 162 lines. Working tree: 9 954. A
1 793-line addition to an artifact a closed card cites.

**Checked rather than assumed, because "an artifact that exists is not an artifact that says what
you cited it for" applies to a changed artifact too.** Everything P6-06 cites this file for is
byte-identical between the two versions:

- The header row, **including the UTF-8 BOM** and the invariant column order that ADR-024 and
  `CsvExporterTests` assert against a committed expected header.
- The hostile-SKU fixture row, which carries every escaping claim at once:
  `13436,p6_06_fixture_hostile_sku,"'=1+1, ""special"" café",,,True,0.000,0.000,OutOfStock` —
  leading-apostrophe formula-injection mitigation, embedded comma, escaped double quotes, non-ASCII
  `café`, and decimals at their stored scale.

The delta is 1 792 additional ordinary product rows: this is a **live** export, and the integration
suite creates product fixtures on every run, so the underlying `current stock` grows each time it is
re-run. The drift is benign and P6-06's claims are unaffected.

**It is still recorded, for two reasons.** The clean-clone run in §1 built from the *committed*
state, so this pack verified the 8 162-line version and not the one on disk. And a phase should not
arrive at its gate with a modified evidence artifact of unknown provenance sitting uncommitted —
whichever version is kept, the tree should be clean before `/phase-gate` runs. **Recommendation:
revert the working-tree change** (`git checkout --` that path), because the committed version is the
one `p6-06-csv-export.txt` describes, the one the clean clone exercised, and the one the expected-header
test was written against; a newer export is a different-but-equally-valid artifact with no reason to
prefer it. That call is the author's, not this pack's, so the file was left untouched.

### 4.7 Three cards left this phase without being done, by recorded decision

P6-13, P6-14 and P6-16 are **not** closed and are not counted as closed anywhere in this index. They
moved to Phase 8 under **ADR-030** — §6.

---

## 5. Test-suite growth across the project

Every figure is the one its own phase's clean-clone log reports, re-read for this index rather than
copied forward.

| Phase | Unit | Integration | Source |
|---|---|---|---|
| 1 | 23 | 62 | `../phase-1/p1-20-clean-clone.log` |
| 2 | 24 | 136 | `../phase-2/p2-13-clean-clone.log` |
| 3 | 44 | 211 | `../phase-3/p3-09-clean-clone.log` |
| 4 | 47 | 348 | `../phase-4/p4-15-clean-clone.log` |
| 5 | 82 | 465 | `../phase-5/p5-16-clean-clone.log` |
| **6** | **140** | **512** | `p6-18-clean-clone.log` |

Phase 6 added **58 unit and 47 integration tests** — it nearly doubled the unit suite, which is what
a phase dominated by reporting, document drift checks and export escaping looks like.

**Two numbers corrected against their sources, per this card's own box:**

- `../phase-5/INDEX.md` §5 says **79** unit. The Phase 5 *final* figure is **82** — P5-16 added three
  assertions after that index was written, and `p5-16-clean-clone.log` line 63 shows 82. This index
  uses 82.
- `../phase-4/INDEX.md` says **346** integration "as of this index"; `p4-15-clean-clone.log` shows
  **348**. This index uses the log.

Neither is a defect — both indexes were accurate when written and both said so. They are recorded
because "every quantitative claim checked against the number it cites" means checking, not trusting.

---

## 6. Cards and carried items at the Phase 6 gate

**Closed, 14:** P6-01, P6-02, P6-03, P6-04, P6-05, P6-06, P6-07, P6-08, P6-09, P6-10, P6-11, P6-12,
P6-15, P6-17, plus this card.

**Not closed, 3 — moved to Phase 8 by ADR-030 (ACCEPTED 2026-08-31):**

| Was | Becomes | Origin | Why it could not close here |
|---|---|---|---|
| P6-13 | **P8-01** | P0-02, via ADR-016 | Needs the three classmates' laptops; lab hardware is explicitly not a substitute (ADR-012) |
| P6-14 | **P8-02** | P0-05, via ADR-016 | Same, for the per-workstation round trip |
| P6-16 🎯 | **P8-03** | ADR-012 | Needs someone other than the author at the keyboard |

**ADR-030's card-move table was checked in all three places** it appears — `docs/adr.md` §ADR-030,
`plan.md` §7 Phase 8, and `tasks.md`'s carried-forward block — and they agree.

**Why this is a recorded deferral and not a quiet slip.** `tasks.md` states that nothing carried into
Phase 6 may be carried out of it without an ADR saying why, in the ADR-016 shape. ADR-030 is that
ADR. It names what the deferral costs, and it answers ADR-016's own rejection of a later destination
rather than talking past it: ADR-016 refused "carry to Phase 7" because a problem found *during* a
rehearsal has *"no phase left to absorb it"*, and Phase 8 supplies that room by ordering the survey,
the cold start and the clean install **ahead** of the rehearsal inside one phase. This is the
mechanism's third honest use.

**Carried items from Phases 0–5:**

| Item | Owner | Status |
|---|---|---|
| **CARRY-01** · no password-reset route | P6-12 | ✅ closed — `p6-12-account-recovery.txt`, ADR-028 |
| **CARRY-02** · off-host copy never asserted against a real volume | P6-07 | ✅ closed — `p6-07-backup-retention.txt`, volume confirmed attached at `D:` |
| **CARRY-04** · intermittent `MSB3030` | P6-17 | ✅ closed — `p6-17-msb3030.txt`, ADR-029 |
| **CARRY-05** · nothing detects a stale deployment | P6-10 | ✅ closed — `p6-10-build-identity.txt`, ADR-027; **and it fired for real at this pack**, §4.4 |
| **P0-02** · demo workstation manifest | → **P8-01** | 🔄 re-carried by ADR-030 |
| **P0-05** · demo network from cold | → **P8-02** | 🔄 re-carried by ADR-030 |
| **P0-07** · repository structure | Phase 7 | 🟡 one box left — `test-plan.md` |

**Two things owed before Phase 8 is scheduled, neither needing a machine in the room**, both carried
forward from ADR-016 (2026-08-22) still open:

1. **Ask each classmate whether they hold local administrator rights on their own laptop.** One
   message. Without it neither the certificate import nor the hosts entry completes. It is the only
   carried item that fails late and unfixably.
2. **Close P8-02's first two boxes on this laptop plus one lab client** — whether Mobile Hotspot
   starts from cold with nothing to share, and whether this adapter sustains station + Wi-Fi Direct
   GO concurrently. ~15 minutes, no classmate needed. **A "no" on either means ADR-015 is wrong** and
   the demo network needs re-planning.

---

## 7. ADR status at the Phase 6 gate

Eight ADRs were owed. All eight are **ACCEPTED**, none PENDING, checked directly against
`docs/adr.md` by line number rather than by memory.

| ADR | Line | Subject | Status |
|---|---|---|---|
| ADR-023 | 1109 | Report date boundary and the two-independent-paths reconciliation rule | ACCEPTED |
| ADR-024 | 1141 | CSV export encoding, escaping, and the export/view permission mirror | ACCEPTED |
| ADR-025 | 1172 | Backup retention, off-host rotation, and the failure-handling contract | ACCEPTED |
| ADR-026 | 1203 | The release manifest, and closing the runtime-verification half of G-16 | ACCEPTED |
| ADR-027 | 1232 | Health endpoint build identity, and why in-process tests cannot prove it | ACCEPTED |
| ADR-028 | 1262 | Account recovery stays CLI-only, and what that costs on demo day | ACCEPTED |
| ADR-029 | 1294 | `MSB3030` root cause: `MAX_PATH`, not a build race | ACCEPTED |
| ADR-030 | 1324 | Machine-dependent field work collects into a new Phase 8 | ACCEPTED |

**All eight are appended before the *Template for new entries* section at line 1375** — verified by
line number, which is the check this box asks for. The remaining `PENDING` strings in `docs/adr.md`
are historical narrative inside ADR-006's progress notes and ADR-009's and ADR-013's prose, not live
statuses.

---

## 8. What Phase 6 did not touch

Stated so no later phase assumes coverage that does not exist.

- **No schema migration was written this phase** beyond the grants file P6-08 needed for its
  restore rehearsal. Reports are read-only, and `merch_api` needed no new privilege — `plan.md` §7's
  own instruction was that a card finding itself writing a grants file should stop and say so.
- **`StockMovements` and `AuditLogs` were never updated or deleted.** Append-only holds by grant, not
  by policy (ADR-013).
- **No client project gained a database dependency.** G-B passes on the clean clone.
- **The three WPF clients received no new screens.** Phase 6's reports are API surface plus export;
  the client-side reporting UI is not in this phase's scope and is not claimed.
- **No load testing.** G-06 is Phase 7's, and nothing here measures p50/p95 under the 5–10 session
  profile.
- **Nothing was proven on a machine other than this one.** Every artifact in this directory was
  produced on `LAPTOP-3HH6OHHE`. That is exactly the gap Phase 8 exists to close, and §6 says so.
