# tasks.md — Phase 2 (Identity and master data)

**Scope:** current phase only. Regenerated at each phase entry from `plan.md`.
**Rules:** one task = one commit, prefixed with the task ID. Never tick a `Done when` box on a failing test or a partial implementation. Stop conditions are in `CLAUDE.md` §7.

**Legend:** ⬜ not started · 🟡 in progress · ✅ done · 🔴 blocked

---

> ## Phase 1 closed 2026-08-22 — on its engineering criteria, by amendment
>
> The gate ran against commit `41ee833` and returned **FAIL**: one card open and two criteria with no artifact. It was then closed by **ADR-016**, which carries those two criteria to the **Phase 6** gate rather than ticking them. `plan.md` §5, §6.2 and §7 were amended to match.
>
> **What was actually proven, reproduced at the gate rather than read from a log:** a fresh clone of `41ee833` outside the tree passes guardrails G-A–G-D, builds eleven projects at **0 warnings / 0 errors**, and runs **23 unit / 62 integration** tests green against the real pinned MariaDB 10.4.32. Eighteen of spec §24's nineteen rows are backed by artifacts that exist on disk. Every ADR is ACCEPTED. Full record: `evidence/phase-1/INDEX.md` §8.
>
> **What is not proven, and is now Phase 6's:** the three demo workstations are unsurveyed, and the demo network has never been brought up from cold. Every cross-machine proof in `evidence/phase-1/` was captured on the author's lab hardware, which ADR-012 makes explicitly not a substitute.
>
> **One deferral was argued and overruled, and the cards below inherit its risk.** P0-05 needs no classmate — host laptop plus one lab client, about fifteen minutes — and it can still invalidate ADR-015, which was accepted on a capability reading rather than a cold start. It rides to Phase 6 by decision. ADR-016 records the cost.
>
> **Frozen by `plan.md` §3: no phase below may re-litigate a Phase 1 decision.** The connector, transaction pattern, auth scheme, hosting model, migration mechanism and error envelope are settled and recorded. Phase 2 consumes them.

---

# Carried forward — open cards from earlier phases

These are carried, not reopened. Each says which gate now owns it.

---

### 🟡 P0-02 · Capture Windows baseline for every demo workstation — **owed at the Phase 6 gate (ADR-016)**

**Spec:** §3 · **Closes:** G-30

**Do:** One complete block per demo workstation in `docs/environment-manifest.md` §3.2 — Windows edition, build, architecture, screen resolution, display scaling, and whether the owner holds local administrator rights.

**Done when:**

- [x] Host row complete in manifest §2
- [x] Host machine confirmed 64-bit
- [ ] One complete block per **demo workstation** in manifest §3.2 — each filled from that machine's own `client-baseline-<MACHINE>.txt`, produced by `scripts/setup-client.ps1`
- [ ] Resolution and scaling recorded per demo workstation
- [ ] Local administrator rights confirmed per demo workstation

**Evidence:** `evidence/phase-0/client-baseline-<MACHINE>.txt` × 3

> **Nobody has to be interviewed.** `setup-client.ps1` is the client setup these machines need regardless, and it collects every field itself, writing `client-baseline-<MACHINE>.txt` to the Desktop. `-CaptureOnly` collects the baseline without changing anything. What remains is three machines being in front of someone with the package — which is what the Phase 6 clean-installation criterion already requires.
>
> **The one part worth doing today, out of band:** ask whether each classmate holds **local administrator rights on their own laptop**. One message, no machine needed. Without it neither the certificate import nor the hosts entry completes, and `setup-client.ps1` reports it in stage 1 rather than failing halfway through stage 2. It is the only carried item that fails late and unfixably.

---

### 🟡 P0-05 · Demo network rehearsal — **owed at the Phase 6 gate (ADR-016)**

**Spec:** §8 · **Closes:** G-25 (begins)

**Do:** Bring the demo network up from cold with `scripts/start-demo-network.ps1`, join a client, and prove `ping MERCH-HOST` plus a validated HTTPS round trip from every demo workstation via `scripts/setup-client.ps1`.

**Done when:**

- [x] Host address settled — ADR-015 pins it at the Windows ICS constant `192.168.137.1`; nothing to reserve, nothing to acquire
- [x] Subnet recorded in manifest §4
- [x] Name choice recorded in ADR-011
- [ ] Mobile Hotspot starts **from cold on this host** — answers whether Windows will start it with no connection to share
- [ ] Station + Wi-Fi Direct GO concurrency confirmed on this adapter under real load
- [ ] `ping MERCH-HOST` succeeds from **every demo workstation**
- [ ] `GET /health` over HTTPS succeeds from every demo workstation, certificate trusted

**Evidence:** `evidence/phase-0/p0-05-demo-network-rehearsal.txt`

> **The two unticked mechanism boxes need nobody but you.** ADR-015 was accepted on `netsh wlan show wirelesscapabilities` reporting `Wi-Fi Direct GO: Supported` — a capability reading, not a cold start. If Mobile Hotspot will not start with nothing to share, or the adapter will not sustain station + GO concurrently, **ADR-015 is wrong** and the topology needs re-planning. A lab-grade rehearsal (host + `DESKTOP-F5LK8MA`, which is wireless and already trusts the certificate) answers both without a classmate present. Only the last two boxes genuinely need the demo machines.
>
> **The trap that will otherwise present as an API defect:** Windows classifies a brand-new hotspot network as **Public**, and the P1-09 firewall rule is `-Profile Private`. On an unclassified hotspot the rule does not apply, 8443 stays shut, and every client reports a connection timeout that reads exactly like a server bug. `start-demo-network.ps1` check 3 exists solely for this and has never been exercised.

---

### 🟡 P0-07 · Repository structure — **the last box closes in Phase 7**

**Done when:**

- [x] `git log` shows an initial commit
- [x] `pwsh ./scripts/install-hooks.ps1` run; pre-commit hook installed
- [x] A test commit confirms the hook fires
- [ ] Folder structure matches `plan.md` §2 — seven `docs/*.md` documents remain, each assigned to a later phase

> **Deliberately spans phases, and that is real information about the project's shape.** `plan.md` §7 assigns each remaining document to the phase whose subject matter it records: `role-permission-matrix.md` + `database-design.md` (first draft) → **Phase 2, and P2-12 below writes both**; `api-specification.md` → Phase 3; `database-design.md` finalised → Phase 4; `ui-specification.md` → Phase 5; `backup-restore-guide.md` + `user-guide.md` → Phase 6; `test-plan.md` → Phase 7. The box ticks when the last one is written.

---

# Phase 2 — Identity and master data

**Entry:** Phase 1 gate passed (ADR-016). **Closes:** G-19, G-21.
**Docs produced:** `docs/role-permission-matrix.md`, `docs/database-design.md` (first complete draft).

**What Phase 1 already built, so no card below rebuilds it.** Tables `Users`, `Roles`, `UserRoles`, `Products`, `StockBalances`, `StockMovements`, `AuditLogs`, `IdempotencyKeys`, `SystemSettings`, `SchemaMigrations`, `Sessions`, `BackupLogs`, `MaintenanceLocks`. Working login with lockout, bearer tokens, one protected endpoint, the atomic stock decrement, the error envelope, and the append-only grant model.

> ### Three things that will bite in this phase specifically
>
> **1. Every new table is append-only until you grant otherwise, and the grant must come *after* the migration.** `merch_api` holds no database-level write privilege (ADR-013). A new table gets `SELECT` and nothing else until a new `db/grants/00NN_*.sql` adds writes back per table — and MariaDB 10.4 rejects a table-level `GRANT` naming a table that does not exist (`ERROR 1146`, measured). Install order is always: migration → grants. Table names in grant files are **lowercase** (`@@lower_case_table_names = 1` on Windows). Next free numbers: migration `0005`, grants `0007`.
>
> **2. `PriceHistory` and `AuditLogs` are ledgers.** They get `INSERT` and `SELECT`, never `UPDATE` or `DELETE` — same rule as `StockMovements`, same mechanism, same reason. Leaving them out of the grants file is how you make that true.
>
> **3. Spec §12 asks for a "unique **active** barcode", and MariaDB 10.4 cannot express it.** There are no partial or filtered unique indexes before 10.5. P1-07 already hit this and reduced scope to "unique when present" for the single POC product, flagging it in the migration file rather than guessing silently. Phase 2 is where the real decision is owed — see P2-06 and ADR-018.

---

## Track A — Identity model and the authorization matrix

### 🟡 P2-01 · Migration 0005 — `PermissionPolicies` and identity model completion

**Spec:** §9, §12 · **Closes:** G-19 (begins) · **Files:** `db/migrations/0005_identity.sql`, `db/grants/0007_identity-grants.sql`

**Do:** Add `PermissionPolicies` and whatever the five-role model needs that `0001_foundation.sql` did not build for one POC user. Seed the five spec §9 roles (`Super Admin`, `Admin`, `Procurement Officer`, `Inventory Clerk`, `Cashier`) as data, not as an enum in code — role membership is queried and audited, and a role that exists only in a `Select Case` cannot be assigned at runtime.

**Done when:**

- [x] Migration applies clean as `merch_migrator` on a database already carrying 0001–0004
- [x] `db/grants/0007` applied **after**, naming lowercase tables; `PermissionPolicies` gets no `UPDATE`/`DELETE` for `merch_api` unless a card justifies it in writing
- [x] All five roles present with stable identifiers that the policy layer keys on
- [x] A user may hold more than one role, and the schema permits it without a second membership table
- [x] Runner applies it exactly once; second run applies nothing (P1-06 behaviour unchanged)
- [ ] Integration test green against pinned MariaDB — **61/62 pass.** The one failure (`Backup_Succeeds_WritesDumpWithChecksumThatVerifies`) is a pre-existing environmental precondition (no `MERCHBACKUP` USB drive attached to this machine right now — confirmed via `Get-Volume`), unrelated to this card: `0005_identity`/`0007` touch neither `BackupLogs` nor any backup code path, and the full `MigrationRunnerTests` suite (5 tests) passes. Left unticked on principle rather than rounded up — re-run with the USB drive attached to close it.
- [x] `check-no-csharp.ps1` passes

**Evidence:** `evidence/phase-2/p2-01-migration.log`, `evidence/phase-2/p2-01-grants.txt`

---

### ✅ P2-02 · The spec §9 role matrix as ASP.NET Core authorization policies

**Spec:** §9 · **Closes:** G-19 · **Decides:** ADR-017
**Files:** `src/Merchandising.Api/Security/`, `src/Merchandising.Domain/Security/`, `docs/role-permission-matrix.md`

**Do:** Express every cell of spec §9's role table as a named policy registered once at startup. Restrictions are cells too: "cannot approve their own purchase order", "cannot manage Super Admin accounts", "cannot restore unless separately granted" are policy requirements, not comments. Phase 3 and Phase 5 consume these — they do not add their own.

**Done when:**

- [x] Every role × operation cell resolves to exactly one named policy
- [x] Self-approval prohibition is a policy requirement with the actor and the target's owner compared server-side, not a controller `If`
- [x] Restore stays behind its own policy, separate from Admin, per spec §9
- [x] No endpoint authorizes by role string comparison; policies only
- [x] `docs/role-permission-matrix.md` is generated from or verified against the registration, so the document cannot silently drift from the code
- [x] ADR-017 records the policy-naming scheme and where the matrix lives

**Evidence:** `evidence/phase-2/p2-02-policy-registration.txt`

---

### 🟡 P2-03 · Every negative cell of the matrix has a passing test

**Spec:** §9, §19 · **Closes:** G-19 · **Files:** `src/tests/Merchandising.Tests.Integration/AuthorizationMatrixTests.vb`

**Do:** `plan.md` §7 names the negative cells explicitly because they are the ones that pass by accident. A test asserting a Cashier *can* sell proves little; one asserting a Cashier *cannot* change a price is the whole point.

**Done when:**

- [x] Every positive cell asserted 200/2xx with the right role
- [x] **Every negative cell asserted 403** — not 401, not 404, not a redirect
- [x] Unauthenticated access to each protected endpoint asserted 401
- [x] The matrix test is data-driven from the same source as the registration, so a new endpoint without a policy fails the suite rather than passing untested
- [x] Error bodies carry a correlation ID and no stack trace, SQL, or internals (ADR-014)
- [ ] Full suite green against pinned MariaDB — **71/72 pass.** Same pre-existing, unrelated failure as P2-01/P2-02 (`Backup_Succeeds_WritesDumpWithChecksumThatVerifies`, no `MERCHBACKUP` USB drive attached to this machine). All 4 `AuthorizationMatrixTests` and every pre-existing test pass. Left unticked on principle — re-run with the USB drive attached to close it.

Only 4 of `PolicyRegistry`'s 29 policies have a live endpoint today (`Diagnostics.AdminPing`, `Maintenance.Perform` ×2 routes, `Adjustments.Request`) — every cell for those 4 is covered above. The other 25 have no endpoint until later Phase 2/3 cards; the discovery test (done-when box 4) is what forces each of those cards to extend this suite as it adds its endpoint, rather than this card front-loading tests for routes that do not exist yet.

**Evidence:** `evidence/phase-2/p2-03-matrix-results.txt` — one row per cell, positive and negative

---

## Track B — Audit as a pipeline, not a habit

### ✅ P2-04 · Audit becomes one server-side pipeline component 🎯

**Spec:** §9, §11, §17 · **Closes:** G-19 · **Decides:** ADR-017
**Files:** `src/Merchandising.Api/Middleware/`, `src/Merchandising.Infrastructure/Data/AuditLogWriter.vb`

**Do:** `plan.md` §7 calls this the key design call of the phase: *"implement audit as a single server-side pipeline component so no future endpoint can forget it. Auditing bolted on per-endpoint is auditing that will have gaps by Phase 5."*

**This is a refactor, not a greenfield build, and that is the risk.** Audit is currently written at three call sites — `StockService`, `AuthService`, `MaintenanceController`. Three is small enough to consolidate cleanly and large enough that the fourth would have been forgotten.

**Done when:**

- [x] One component writes every audit row: actor, timestamp, action, target, result, correlation ID (spec §9)
- [x] It participates in the **caller's** transaction — an audit row committing when the operation rolled back is a defect, and P1-12's rollback proof must still pass unchanged
- [x] The three existing call sites route through it; none writes `AuditLogs` directly
- [x] A sensitive endpoint that fails to declare its audit intent is rejected — by a failing test, a startup check, or a required parameter. "Remember to call it" is not a mechanism
- [x] `AuditLogs` remains append-only by grant; no card adds `UPDATE`/`DELETE`
- [x] P1-11 through P1-14 proofs re-run green — the transaction pattern is frozen (ADR-006) and this must not perturb it

**Evidence:** `evidence/phase-2/p2-04-audit-pipeline.txt`, `evidence/phase-2/p2-04-rollback-regression.txt`

> **Stop condition.** If making audit transactional forces a change to the ADR-006 transaction pattern, that is `CLAUDE.md` §7 item 5 — the design is wrong, not the code. Halt and report rather than inventing a second pattern.

---

### ✅ P2-05 · `SystemSettings` read/write, audited

**Spec:** §12, §17 · **Files:** `src/Merchandising.Api/Controllers/`, `src/Merchandising.Infrastructure/Data/SystemSettingsRepository.vb`

**Do:** Promote the Phase 1 settings table to a real administered surface: typed read, policy-gated write, every change audited through P2-04's pipeline with old and new value recorded.

**Done when:**

- [x] Read requires authentication; write requires SuperAdmin (`Configuration.Manage`, already accepted at ADR-017 from spec §9's role table — "Admin-or-above" corrected here per CLAUDE.md's precedence rule; confirmed with the user rather than silently widened)
- [x] Every change writes an audit row carrying **both** the previous and new value
- [x] Currency code and rounding policy are settings, not constants (spec §12)
- [x] An unknown or malformed key is rejected with a stable error code, not silently stored
- [x] Integration test green

**Evidence:** `evidence/phase-2/p2-05-settings-audit.txt`

---

## Track C — Product master

### ⬜ P2-06 · Migration 0006 — product master tables

**Spec:** §12 · **Closes:** G-21 (begins) · **Decides:** ADR-018
**Files:** `db/migrations/0006_product-master.sql`, `db/grants/0008_product-master-grants.sql`

**Do:** `Categories`, `Brands`, `Units`, `ProductBarcodes`, `PriceHistory`, and the `Products` columns Phase 1 deliberately left out. Money `DECIMAL(19,4)`, quantities `DECIMAL(19,3)`, timestamps `DATETIME(6)` UTC, `COLLATE utf8mb4_unicode_ci` stated explicitly on every table.

**Done when:**

- [ ] All five tables created, with foreign keys that **prevent** deletion of a referenced product (spec §12)
- [ ] `PriceHistory` granted `INSERT`/`SELECT` only — it is a ledger, like `StockMovements`
- [ ] **ADR-018 records how "unique active barcode" is enforced**, given MariaDB 10.4 has no partial unique index. Decide it; do not inherit P1-07's POC-scale reduction by silence
- [ ] Indexes present for SKU, barcode, product name, supplier name (spec §12)
- [ ] Applies clean as `merch_migrator`; `db/grants/0008` applied after, lowercase names
- [ ] Round-trip test: `0.001` and `12345678901234.5678` exact through the new decimal columns

**Evidence:** `evidence/phase-2/p2-06-schema.txt`, `evidence/phase-2/p2-06-precision.txt`

> **The barcode decision is the real work here, and it has at least three honest answers:** a trigger enforcing it, a generated column participating in the unique index, or accepting application-level enforcement with a documented race window. Pick one on measured behaviour against the real 10.4 instance and record why the others lost. Application-level uniqueness under concurrency is precisely what P1-13 proved you cannot assume.

---

### ⬜ P2-07 · Product CRUD with uniqueness enforced at DB **and** API

**Spec:** §10, §12 · **Closes:** G-21 · **Files:** `src/Merchandising.Api/Controllers/ProductsController.vb`, `src/Merchandising.Infrastructure/Data/ProductRepository.vb`

**Do:** Create, read, update and search products. `plan.md` §7's exit criterion says uniqueness holds at **both** layers — the API for a usable error message, the database because the API cannot be trusted under concurrency.

**Done when:**

- [ ] Duplicate SKU rejected with a stable error code and a field-level validation detail (ADR-014)
- [ ] Duplicate SKU rejected by the **database** too, proven by a concurrent double-insert test that bypasses the API check
- [ ] Barcode uniqueness enforced per ADR-018's decision, with the same two-layer proof
- [ ] Money and quantity scale validated at the API boundary before binding (ADR-004.1) — a stored value that looks right proves nothing
- [ ] Every mutation audited through P2-04
- [ ] Integration tests green

**Evidence:** `evidence/phase-2/p2-07-uniqueness.txt`

---

### ⬜ P2-08 · Price change writes `PriceHistory` and audit atomically

**Spec:** §11, §12 · **Closes:** G-21 · **Files:** `src/Merchandising.Api/Catalog/`, `src/Merchandising.Infrastructure/Data/`

**Do:** A price or cost change updates `Products`, appends `PriceHistory`, and writes audit — in **one** transaction, using the frozen ADR-006 pattern. Price changes are a policy-gated operation per spec §9.

**Done when:**

- [ ] All three effects commit together or not at all, proven by a forced-failure test in the shape of P1-12
- [ ] `PriceHistory` captures old value, new value, actor, effective timestamp UTC, correlation ID
- [ ] A Cashier is refused (403); an Admin succeeds — asserted in the P2-03 matrix
- [ ] `PriceHistory` proven append-only by privilege: `UPDATE` and `DELETE` as `merch_api` both return `ERROR 1142`
- [ ] Historical rows are never rewritten by a later change

**Evidence:** `evidence/phase-2/p2-08-price-history.txt`, `evidence/phase-2/p2-08-atomicity.txt`

---

### ⬜ P2-09 · Active/inactive lifecycle preserves historical references

**Spec:** §12 · **Closes:** G-21 · **Files:** `src/Merchandising.Api/Catalog/`, `src/Merchandising.Domain/Entities/`

**Do:** Spec §12: *"Transactional records are never physically deleted. Master data is deactivated where possible."* Deactivation must leave every historical reference intact and resolvable.

**Done when:**

- [ ] Deactivating a product referenced by a stock movement succeeds and the movement still resolves its product
- [ ] A **delete** of a referenced product is refused by foreign key, proven with the error, not asserted by inspection
- [ ] Inactive products are excluded from lookup/sale paths but remain visible in history and reports
- [ ] Reactivation restores availability without duplicating the record
- [ ] Deactivation and reactivation are both audited

**Evidence:** `evidence/phase-2/p2-09-lifecycle.txt`

---

## Track D — Suppliers

### ⬜ P2-10 · Supplier master with the same lifecycle rules

**Spec:** §10.1, §12 · **Files:** `db/migrations/0007_suppliers.sql`, `db/grants/0009_supplier-grants.sql`, `src/Merchandising.Api/Controllers/SuppliersController.vb`

**Do:** `Suppliers` with unique name, contact fields, active/inactive lifecycle, policy-gated maintenance, full audit. Phase 3's purchase orders depend on this and should find it finished.

**Done when:**

- [ ] Migration + grants applied in that order; lowercase names in the grant file
- [ ] Duplicate supplier name rejected at both layers, as P2-07
- [ ] Deactivation preserves references from any existing record
- [ ] Procurement Officer may maintain suppliers; Cashier may not — asserted in the P2-03 matrix
- [ ] Every mutation audited
- [ ] Integration tests green

**Evidence:** `evidence/phase-2/p2-10-suppliers.txt`

---

## Track E — Seed and closure

### ⬜ P2-11 · Seed data loads on a clean database

**Spec:** §12, §18 · **Files:** `db/seed/`, `scripts/bootstrap.ps1`

**Do:** One seed pass producing the five roles, a Super Admin, one user per role for testing, a small product set across categories/brands/units, and a few suppliers. It must run from `bootstrap.ps1` so a classmate's clean install arrives usable.

**Done when:**

- [ ] Seed runs on a freshly bootstrapped database and completes without error
- [ ] Running it **twice** does not duplicate rows or fail — idempotent by key, matching ADR-007's spirit
- [ ] Seeded passwords are per-installation, never a literal committed to the repository — guardrail G-C must stay green
- [ ] `bootstrap.ps1` invokes it, and the README says what credentials the operator ends up with
- [ ] A clean-clone run reaches a working login without manual SQL

**Evidence:** `evidence/phase-2/p2-11-seed-clean-db.log`

> **Watch the credential guardrail here.** A seed file is the most natural place in the whole project to write a password literal, and G-C exists to catch exactly that. Generate per installation, as `bootstrap.ps1` already does for the three database identities.

---

### ⬜ P2-12 · Phase 2 documents

**Spec:** §20 · **Files:** `docs/role-permission-matrix.md`, `docs/database-design.md`

**Do:** Write both documents `plan.md` §7 assigns to this phase. `role-permission-matrix.md` is verified against the running registration (P2-02), not transcribed by hand. `database-design.md` is the first complete draft covering every table that exists after 0006 and 0007 — it is finalised in Phase 4, not here.

**Done when:**

- [ ] `role-permission-matrix.md` matches the policy registration cell for cell, checked mechanically
- [ ] `database-design.md` documents every table, key, index, and the append-only grant model with ADR-013 cited
- [ ] Both state the academic-prototype framing required by `plan.md` §5's standing constraint
- [ ] ADR-017 and ADR-018 are ACCEPTED, not PENDING
- [ ] Two of P0-07's seven remaining documents are struck from its list

**Evidence:** the two documents

---

## Phase 2 exit gate

From `plan.md` §7. Every criterion needs an artifact under `evidence/phase-2/` — a file someone else could read.

- [ ] Every cell of the role-permission matrix has a passing test, **including the negative cells** (P2-03)
- [ ] Product SKU and barcode uniqueness enforced at the database **and** the API, both proven under concurrency (P2-06, P2-07)
- [ ] A price change writes `PriceHistory` and audit **atomically**, proven by forced failure (P2-08)
- [ ] Deactivation preserves historical references; referenced master data cannot be deleted (P2-09)
- [ ] Seed data loads on a clean database, idempotently (P2-11)
- [ ] `docs/role-permission-matrix.md` and `docs/database-design.md` written (P2-12)
- [ ] G-19 and G-21 closed in the gap register
- [ ] ADR-017 and ADR-018 ACCEPTED
- [ ] Clean-clone build and both test suites green

**Carried, not owed here:** P0-02 and P0-05 belong to the **Phase 6** gate (ADR-016). P0-07's structure box belongs to **Phase 7**. Do not treat them as Phase 2 blockers, and do not close them with lab hardware.
