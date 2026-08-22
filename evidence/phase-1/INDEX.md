# Phase 1 — Foundation Proof-of-Concept · Evidence Index

**Built at P1-20, 2026-08-22, against commit `98ef732`.**
**Spec:** `documentations/Merchandising System for a Mid-Scale Hardware Store.md` §24
**Plan:** `plan.md` §6.2

---

## How to read this file

Spec §24 defines the Foundation Proof-of-Concept as **twelve demonstration steps** and
**seven exit criteria**. This index maps every one of those nineteen rows to a file that
exists on disk in this directory, plus the task card that produced it.

Three rules were applied while writing it, because an index is only worth as much as its
weakest row:

1. **Every artifact path was checked to exist.** No row points at a file that was planned
   but never written.
2. **No row says "verified informally."** Spec §24's own instruction, and `plan.md` §6.2's
   acceptance condition. Where a claim rests on inspection rather than execution, the row
   says so in full and is not counted as proven.
3. **Where the evidence proves something narrower than the row's wording, the row records
   the narrower claim.** Three rows do this (§24 steps 6 and 12, and the *Recovery
   viability* criterion). They are marked ⚠ rather than ✅ and are explained in §4.

Status marks: ✅ proven by executed evidence · ⚠ proven, with a stated limit on the claim ·
⬜ not proven.

---

## 1. The twelve demonstration steps (§24)

| # | Step, as the spec words it | Evidence in this directory | Card | Status |
|---|---|---|---|---|
| 1 | Create and load the solution with no C# application source | `p1-01-build.log` (eleven VB projects, solution loads and builds) · `p1-20-clean-clone.log` (guardrail G-A passes on a fresh clone) · `../phase-0/p0-08-guardrail-proofs.txt` (the guardrails were proven to **fail** on planted violations before being trusted to pass) | P1-01, P0-08 | ✅ |
| 2 | Build the Visual Basic ASP.NET Core Web SDK API and run a health endpoint | `p1-02-project-file.txt` (the hand-authored Web SDK project file and the inheritance proof) · `p1-02-health-response.txt` (Kestrel starts, `GET /health` responds, and what the response withholds) · `p1-03-publish.log` (`win-x64` publish, framework-dependent **and** self-contained, both run from a clean folder) | P1-02, P1-03 | ✅ |
| 3 | Connect the API to the exact MariaDB instance supplied through XAMPP | `p1-05-connection-test.log` (MySqlConnector against 10.4.32 via XAMPP, as `merch_api`; `READ COMMITTED` and `STRICT_TRANS_TABLES` asserted live per connection) · `p1-04-grants.txt` (server-side `sql_mode` fix, `my.ini` line 157) | P1-05, P1-04 | ✅ |
| 4 | Apply a numbered SQL migration and record it in `SchemaMigrations` | `p1-06-migration-run.log` (0001 and 0002 applied in order, each recorded with identifier, checksum, UTC timestamp and result) · `p1-06-tamper-refusal.log` (an edited already-applied file refuses the **entire** run before applying anything — a clear error, not a silent skip) | P1-06 | ✅ |
| 5 | Create a test user, authenticate, and call one protected endpoint | `p1-08-auth-matrix.txt` (the full accept/reject matrix over the protected endpoint) · `p1-08-log-scan.txt` (no password value appears in any log) | P1-08 | ✅ |
| 6 | Connect one WPF client to the API over HTTPS and display the result | `p1-15-cross-machine-roundtrip.txt` (round trip from `DESKTOP-F5LK8MA` over HTTPS across a real network boundary) · `p1-15-client-behaviour.txt` (offline/error states) · `p1-15-runtime-free-launch.txt` (what the framework-dependent client does on a machine with **no** .NET runtime — ADR-010) · `p1-09-cross-machine-trust.txt` (certificate trusted on two client machines) | P1-15, P1-09 | ⚠ see §4.1 |
| 7 | Execute a stock decrement that creates a movement and audit record in one transaction | `p1-11-happy-path.txt` (exactly one movement row, one audit row, correct balance, affected-row count verified before success, correlation ID on both rows) · `p1-11-insufficient-stock.txt` (controlled 409, zero rows written) · `p1-11-decimal-scale.txt` (over-scale quantity rejected at the API boundary before binding — ADR-004.1) | P1-11 | ✅ |
| 8 | Force a failure and prove that the sale/movement/balance/audit changes roll back together | `p1-12-rollback.txt` (balance, movement and audit rows identically absent before and after the forced failure; before/after counts recorded; the fault injection point proven structurally inert in a Release build by running the identical call under both configurations) | P1-12 | ✅ |
| 9 | Execute two concurrent stock-changing requests and prove that the result cannot become negative or duplicate | `p1-13-concurrency.txt` (two, then ten, simultaneous requests against one unit of stock, seven rounds each: exactly one success per round, final balance `0.000`, never negative, exactly one movement row — cross-checked against the database, not only through the test's assertions) · `p1-14-idempotency.txt` (a repeated idempotency key returns the original committed result and creates no second transaction) | P1-13, P1-14 | ✅ |
| 10 | Publish the API and install it as a Windows Service; reboot the host and verify recovery | `p1-16-service-config.txt` (installed, running under its service identity) · `p1-16-post-reboot-health.txt` (host rebooted, API serving **unattended** — no console session behind it) · `p1-16-recovery.txt` (forced process kill triggers automatic restart) | P1-16 | ✅ |
| 11 | Run the scheduled-backup utility, copy a backup to a separate location, and restore it through the maintenance procedure | `p1-17-backup-success.log` (scheduled task fires end to end against the real `mysqldump.exe`) · `p1-17-backup-failure.log` (the failure paths, proven to fail loudly) · `p1-18-restore-log.txt` (restore under maintenance mode; measured **RTO 3.0 s**) · `p1-18-rehearsal-run.txt` (full rehearsal transcript) · `p1-18-verification-checklist.md` (the post-restore checks that gate releasing maintenance mode) | P1-17, P1-18 | ⚠ see §4.3 |
| 12 | Verify that a client laptop cannot connect to MariaDB and that an unauthorized API request is rejected | `p1-10-port-scan.txt` (port 3306 not reachable) · `p1-10-cross-machine-denials.txt` (root refused from another machine; phpMyAdmin unreachable — observed from two real client machines) · `p1-10-host-state-during-test.txt` (host-side counterpart, so a refused connection is attributable to configuration and not to the host being down) · `p1-10-denials.txt` (unauthorized API requests rejected; error bodies carry no internals) | P1-10, P1-04, P0-03 | ⚠ see §4.2 |

---

## 2. The seven exit criteria (§24)

| Exit criterion | Required evidence, as the spec words it | Artifact | Status |
|---|---|---|---|
| **All-Visual-Basic source** | Project/source inventory and build output show no C# application files | `p1-01-build.log` · `p1-01a-guardrail-comment-fix.txt` (the guardrails were corrected so G-B and G-D ignore XML comments while G-C does not — a false pass is worse than no guardrail) · `p1-20-clean-clone.log` (G-A through G-D pass on a fresh clone, 0 warnings) · `../phase-0/p0-08-guardrail-proofs.txt` | ✅ |
| **API viability** | Manual project builds, runs, publishes, and responds to a protected request | `p1-02-project-file.txt` · `p1-02-health-response.txt` · `p1-03-publish.log` · `p1-08-auth-matrix.txt` | ✅ |
| **Database viability** | Exact MariaDB/provider combination connects, migrates, commits, and rolls back | `p1-05-connection-test.log` · `p1-06-migration-run.log` · `p1-07-precision-check.txt` (money `DECIMAL(19,4)`, quantity `DECIMAL(19,3)`, timestamps `DATETIME(6)` UTC as built, not as intended) · `p1-11-happy-path.txt` (commit) · `p1-12-rollback.txt` (rollback) · `p1-20-clean-clone.log` (62 integration tests against the real instance from a fresh clone) | ✅ |
| **Security boundary** | HTTPS works; credentials are host-only; client database-port connection fails; unauthorized requests fail | `p1-09-cert-details.txt` · `p1-09-cross-machine-trust.txt` · `p1-09-invalid-cert-behaviour.txt` · `p1-09-firewall-before.txt` / `p1-09-firewall-after.txt` (the rule scoped to Private, with the before state kept) · `p1-21-tls-pinning.txt` (TLS floor pinned **in code**, proven by A/B against the real published binary) · `p1-10-*` (all four) · `p1-04-negative-tests.txt` · `p1-04a-grant-model-proof.txt` (13 privilege assertions; `UPDATE`/`DELETE`/`DROP` on an append-only table all refused with `ERROR 1142`) · `p1-07-audit-immutability.txt` · `p1-08-log-scan.txt` | ✅ |
| **Service viability** | Windows Service starts after reboot, logs correctly, and recovers from a controlled stop | `p1-16-service-config.txt` · `p1-16-post-reboot-health.txt` · `p1-16-recovery.txt` | ✅ |
| **Recovery viability** | Backup file is valid, off-host copy exists, restore succeeds, and data verification passes | `p1-17-backup-success.log` · `p1-17-backup-failure.log` · `p1-18-restore-log.txt` · `p1-18-rehearsal-run.txt` · `p1-18-verification-checklist.md` | ⚠ see §4.3 |
| **Planning baseline** | Provider, project setup, API language, migration, runtime, certificate, transaction, and recovery decisions are recorded | `../../docs/adr.md` — twenty entries, **all ACCEPTED**, itemised in §3 below · `../../docs/environment-manifest.md` (see §4.4 for what it still lacks) · `../phase-0/PHASE-0-READINESS.md` | ⚠ see §4.4 |

---

## 3. Decision ledger — `docs/adr.md`

Every entry is **ACCEPTED**. No entry is PENDING; the only `PENDING` string in the file is
the status placeholder in the blank template at the bottom.

| ADR | Decides | Proven by |
|---|---|---|
| ADR-000 | Course constraints are binding (VB.NET, XAMPP) | — constraint, not a measurement |
| ADR-001 | API project SDK — rung A (hand-authored Web SDK) | `p1-02-project-file.txt` |
| ADR-002 | MariaDB 10.4.32 and MySqlConnector versions | `../phase-0/p0-07-mariadb-10.4-constraints.txt`, `p1-05-connection-test.log` |
| ADR-003 (+.1, .2) | Charset, collation, engine, MariaDB 10.4 constraints, `sql_mode` | `p1-04-grants.txt`, `p1-05-connection-test.log` |
| ADR-004 (+.1) | Numeric precision; rounding to scale is the API's job | `p1-07-precision-check.txt`, `p1-11-decimal-scale.txt` |
| ADR-005 | Authentication and token scheme | `p1-08-auth-matrix.txt` |
| ADR-006 | Conditional-update concurrency control | `p1-11-*`, `p1-12-rollback.txt`, `p1-13-concurrency.txt` |
| ADR-007 | Idempotency for transactional commands | `p1-14-idempotency.txt` |
| ADR-008 | Migration mechanism (checksum, tamper refusal) | `p1-06-migration-run.log`, `p1-06-tamper-refusal.log` |
| ADR-009 (+.1, .2) | Test framework, measured on this machine | `p1-19-test-run.log` |
| ADR-010 | Deployment model | `p1-03-publish.log`, `p1-15-runtime-free-launch.txt` |
| ADR-011 (+.1) | Certificate strategy; TLS floor pinned in code | `p1-09-cert-details.txt`, `p1-21-tls-pinning.txt` |
| ADR-012 | Delivery model — three classmates, their hardware | `../../docs/professor-approvals.md` |
| ADR-013 (+.1) | Three database identities; append-only by grant | `p1-04a-grant-model-proof.txt`, `p1-07-audit-immutability.txt` |
| ADR-014 | Error envelope and correlation-ID contract | `p1-10-denials.txt` |
| ADR-015 (+.1) | Demo topology — the host is the access point | ⬜ **specified, rehearsal owed** — see §4.4 |

---

## 4. What this index does **not** claim

This section exists because the difference between Phase 1 and a Phase 1 that merely looks
finished lives entirely here.

### 4.1 The WPF round trip was proven on lab machines, not on the delivered machines

§24 step 6 asks for one WPF client connecting over HTTPS. That is proven, twice, across a
real network boundary — including on a machine with no .NET runtime installed at all, which
found and fixed a real shipping defect at P1-15.

What it is **not** is proof on the three machines the system will actually be demonstrated
on. Those belong to three classmates (ADR-012), have never been surveyed, and are recorded
as `Status: none captured` in `docs/environment-manifest.md` §3.2. `DESKTOP-G83CCSH` and
`DESKTOP-F5LK8MA` are the author's lab hardware and are explicitly **not** substitutes.

The step is marked ⚠ rather than ✅ for that reason alone. The mechanism is proven; the
population it was proven on is not the delivery population.

### 4.2 The same limit applies to the negative security tests

§24 step 12 is proven from two real second machines on the lab LAN — which is far stronger
than the same-host simulation it replaced — but again, not from the demo workstations, and
not on the demo network of `docs/environment-manifest.md` §4.2. The firewall rule that makes
the denial hold is scoped to the **Private** profile (`p1-09-firewall-after.txt`); the demo
network's profile classification is a live risk that `scripts/start-demo-network.ps1`
check 3 exists to catch, and that has not yet been exercised in a rehearsal.

### 4.3 "Off-host copy" is proven as a mechanism, not as an operating practice

The spec's *Recovery viability* criterion asks for a valid backup, an **off-host copy**, a
successful restore, and passing data verification. Backup, restore and verification are
proven, with a measured RTO of 3.0 s. The off-host copy is proven as a copy to a separate
destination — it is not proven as a rotation with retention running unattended over time.
Spec §15's full retention and off-host rotation is `plan.md` Phase 6 work (G-15), and P1-17
closed the Phase 1 half of it deliberately.

Recorded here because "backup and restore evidence exists" (the Phase 1 exit wording) and
"backup/restore evidence meets documented targets" (the Phase 6 wording) are different
claims, and this directory only supports the first.

### 4.4 The environment manifest is not fully populated — this is the open item

`docs/environment-manifest.md` §5 carries three unticked boxes, all of which need people and
a room rather than code:

| Outstanding | Owning card | Why it cannot be closed here |
|---|---|---|
| Every demo workstation captured (§3.2) — edition, build, architecture, resolution, scaling, **and local administrator rights** | **P0-02** (🟡) | The three classmates' machines have not been surveyed. Local admin is the one to ask about first: without it, certificate trust and the hosts entry both fail, and it is cheapest to discover now rather than on the day |
| `ping MERCH-HOST` from every demo workstation | **P0-02 / P0-05** (🟡) | Gated on the demo rig existing. The lab-side entry is applied and verified; that proves the host can find itself and a lab client can find it, not that a classmate's laptop can |
| Demo network rehearsed (§4.2) | **P0-05** (🟡) | ADR-015 specified the topology on 2026-08-22 and retired the AP-isolation failure class. Two questions stay open that reasoning cannot settle: whether Windows starts Mobile Hotspot with no connection to share, and whether this adapter sustains station + Wi-Fi Direct GO concurrently |

**Consequence for the gate, stated plainly:** the P1-20 box "Environment manifest fully
populated" is **not** ticked, and Phase 1 therefore closes with one card at 🟡. Every other
element of the closure pack is complete. What remains is field work on hardware this project
does not own, not engineering.

---

## 5. Gap register (§23) — the rows Phase 1 was responsible for

| Gap | Closed by | Artifact | Status |
|---|---|---|---|
| G-01 · No VB Web API template | P1-02 | `p1-02-project-file.txt`, `p1-03-publish.log` | ✅ |
| G-02 · All-VB server support uncertain | P1-02, P1-08 | `p1-02-health-response.txt`, `p1-08-auth-matrix.txt` | ✅ |
| G-03 · XAMPP not production-ready | P0-03, P1-04, P1-10 | `../phase-0/xampp-services.txt`, `../phase-0/p0-03-apache-stopped-reverify.txt`, `p1-10-port-scan.txt`, `p1-10-cross-machine-denials.txt` | ✅ |
| G-04 · Provider/version compatibility | P0-04, P1-05 | `../phase-0/p0-07-mariadb-10.4-constraints.txt`, `p1-05-connection-test.log` | ✅ |
| G-05 · EF Core transaction behaviour | ADR-005/006 (EF Core removed as a dependency) | `p1-12-rollback.txt` | ✅ |
| G-07 · HTTP fallback weakened security | P1-09, P1-21 | `p1-09-*`, `p1-21-tls-pinning.txt` | ✅ |
| G-09 · Service hosting details omitted | P1-16 | `p1-16-*` | ✅ |
| G-10 · Credentials could leak to clients | P1-04, P1-10, guardrails G-B/G-C | `p1-04-negative-tests.txt`, `p1-10-denials.txt`, `p1-20-clean-clone.log` | ✅ |
| G-11 · Negative stock under concurrency | P1-11, P1-13 | `p1-13-concurrency.txt` | ✅ |
| G-12 · Transaction boundaries unspecified | P1-11, P1-12 | `p1-11-happy-path.txt`, `p1-12-rollback.txt` | ✅ |
| G-13 · Duplicate retries | P1-14 | `p1-14-idempotency.txt` | ✅ |
| G-14 · Restore through a live API | P1-18 | `p1-18-restore-log.txt`, `p1-18-verification-checklist.md` | ✅ |
| G-15 · Backup details incomplete | P1-17 (Phase 1 half) | `p1-17-backup-success.log`, `p1-17-backup-failure.log` | ⚠ retention/rotation → Phase 6 |
| G-16 · Deployment runtime ambiguous | P1-03, P1-15 | `p1-03-publish.log`, `p1-15-runtime-free-launch.txt` | ⚠ clean-machine install → Phase 6 |
| G-19 · Auth and role controls | P1-08 (begins) | `p1-08-auth-matrix.txt` | ⚠ full role matrix → Phase 2 |
| G-20 · Audit immutability unenforceable | P1-07, ADR-013 | `p1-07-audit-immutability.txt`, `p1-04a-grant-model-proof.txt` | ✅ |
| G-21 · Data model invariants and precision | P1-07 (begins) | `p1-07-precision-check.txt` | ⚠ full model → Phase 2/4 |
| G-25 · Network/certificate naming | P1-09 | `p1-09-cross-machine-trust.txt`, `p1-09-client-trust-steps.md` | ⚠ demo machines → P0-02 |
| G-26 · Offline behaviour not explicit | P1-15 (partially) | `p1-15-client-behaviour.txt` | ⚠ full client states → Phase 5 |
| G-30 · Unverified environment assumptions | P0-01, P0-02 | `../phase-0/dotnet-info-dev.txt`, `../phase-0/p0-07-visual-studio-workloads.txt` | ⚠ demo workstations → P0-02 |

Gaps G-06, G-08, G-17, G-22, G-23, G-24, G-27, G-28, G-29 are assigned to Phases 2–7 by
`plan.md` §7 and are correctly untouched here.

---

## 6. Test-suite growth across Phase 1

Recorded because the three test-count figures in `tasks.md` differ, and a reader comparing
them should see growth rather than a discrepancy.

| Point | Unit | Integration | Source |
|---|---|---|---|
| P1-19 (suites first wired) | 17 | 44 | `p1-19-test-run.log` |
| P1-21 (TLS pin added) | 21 | 48 | `p1-21-tls-pinning.txt` |
| **P1-20 (clean clone, this run)** | **23** | **62** | `p1-20-clean-clone.log` |

All green, 0 warnings, at every point. The integration suite runs against the real pinned
MariaDB 10.4.32 and never an in-memory substitute (ADR-000, ADR-009).

---

## 7. Verdict this index supports

**Eighteen of nineteen §24 rows are proven by executed evidence.** The nineteenth — the
*Planning baseline* criterion — is complete on its decision half (all twenty ADRs ACCEPTED)
and incomplete on its environment half, for the three reasons in §4.4.

Phase 1's engineering is done. What is outstanding is a survey of three machines this
project does not own and one rehearsal on a network that does not exist until someone
switches it on. Neither is deferrable indefinitely: both are `P0-02` and `P0-05`, both are
still 🟡, and under ADR-012 they are the difference between a system that works and a system
that can be handed over.

**This index does not declare the Phase 1 gate passed.** That is `/phase-gate`'s call, made
against these rows.

---

## 8. Gate outcome — 2026-08-22, against commit `41ee833`

`/phase-gate` ran against the rows above and returned **FAIL**: one card open (P1-20) and two
criteria with no artifact — the demo-workstation survey and the demo-network rehearsal, both
Phase 0 carry-forwards rather than Phase 1 engineering.

**The gate was then closed on its engineering criteria by amendment, recorded as ADR-016**,
with **P0-02** and **P0-05** carried to the **Phase 6** gate. `plan.md` §5 and §6.2 were amended
to say so, and Phase 6's exit criteria now name both.

**Nothing in §4 above was withdrawn, softened, or re-marked.** The three ⚠ rows are still ⚠.
§4.4 still says the manifest is not fully populated, because it is not. What changed is the
gate those rows are owed to — not what the evidence proves. An index that quietly upgraded
itself the moment a gate needed to pass would be worth nothing, and this one was built
specifically to be readable by someone auditing that suspicion.

**Verification performed at the gate, rather than read from this file:** every evidence path
referenced by `tasks.md`, `plan.md` and this index was checked to exist and be non-empty;
`docs/adr.md` was scanned for `PENDING` (none, other than the blank template's placeholder);
and the clean-clone run was **reproduced independently at `41ee833`** — fresh clone outside
the tree, guardrails G-A–G-D pass, build 0 warnings / 0 errors, **23 unit / 62 integration**
green against the real pinned MariaDB 10.4.32.

**P1-20 closes ✅ with its fifth box unticked and carried**, not ticked. "Environment manifest
fully populated" remains false and is now owed at Phase 6. The card's own Result said it
could not be ticked here; that judgement stands and the box moved instead.

**One item was argued at the gate and deferred anyway, deliberately.** The P0-05 rehearsal
needs no classmate and no hardware the author lacks — it is the host laptop plus one lab
client, about fifteen minutes — and it can still invalidate ADR-015, which was accepted on a
capability reading rather than a cold start. It was carried with P0-02 by the author's
decision. ADR-016 records the cost rather than the convenience, and Phase 6 cannot pass
without exercising it.
