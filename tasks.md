# tasks.md — Phase 0 (remainder) and Phase 1 (Foundation Proof-of-Concept)

**Scope:** current phases only. Regenerated at each phase entry from `plan.md`.
**Rules:** one task = one commit, prefixed with the task ID. Never tick a `Done when` box on a failing test or a partial implementation. Stop conditions are in `CLAUDE.md` §7.

**Legend:** ⬜ not started · 🟡 in progress · ✅ done · 🔴 blocked

---

# Phase 0 — Environment baseline

No application code is written in this phase.

---

### 🟡 P0-06 · Professor approvals — decision resolved, evidence outstanding

Manual VB ASP.NET Core API approved; XAMPP confirmed mandatory. **The decision is settled and ADR-000 is ACCEPTED on the strength of it.**

**Marker corrected at P0-07:** this card was ✅ while both of its `Done when` boxes were unticked. The *decision* is resolved; the *card* is not. An approval you cannot point to at sign-off is an approval you do not have.

**Remaining:** capture the original messages into `docs/professor-approvals.md` (PA-001, PA-002) and attach screenshots.

**Unblocked by:** Max locating the original message thread and saving two screenshots. No agent action possible — the agent cannot access the conversation.

**Done when:**

- [ ] PA-001 and PA-002 dated, with evidence attached
- [ ] `evidence/phase-0/pa-001-approval.png` and `pa-002-approval.png` exist

---

### ✅ P0-01 · Capture development machine baseline

**Spec:** §3 · **Closes:** G-30

**Do:** Verify VS 2026 has the **.NET desktop development** and **ASP.NET and web development** workloads plus the .NET 10 component. Capture versions into `docs/environment-manifest.md` §1.

> **Result.** VS Community 2026 (18.7.1+11911.148) initially had **.NET desktop development** but not **ASP.NET and web development** — confirmed missing via `vswhere -requires` and the instance's `state.json`. The workload was subsequently installed by the user and re-verified the same way: `state.json` now lists `CoreEditor`, `ManagedDesktop`, and `NetWeb`. Both workloads confirmed installed. Everything command-line-capturable (`dotnet --info`, `--list-runtimes`, Windows/Git/PowerShell versions) is recorded in manifest §1. Screenshot evidence of the VS Installer state was out of scope for this pass — user confirmed proceeding via command line only.

**Done when:**

> **P0-07 re-verification.** The original pass inferred workload presence partly from the instance's `state.json`. It has now been confirmed the direct way, with `vswhere -requires <workloadId>` returning the instance for both `Microsoft.VisualStudio.Workload.ManagedDesktop` and `Microsoft.VisualStudio.Workload.NetWeb`, and returning nothing for two workloads that are genuinely absent (so the query is discriminating, not always-true). Visual Studio Community 2026, `18.7.1+11911.148`, `isComplete=True`, `isLaunchable=True`. Evidence: `evidence/phase-0/p0-07-visual-studio-workloads.txt`.

- [x] Both workloads confirmed installed — `vswhere -requires`, re-verified at P0-07 (see note above)
- [x] `dotnet --info` captured to `evidence/phase-0/dotnet-info-dev.txt`
- [x] `dotnet --list-runtimes` shows a .NET 10 ASP.NET Core runtime **and** a Desktop runtime
- [x] Manifest §1 fully populated — no blanks

**Evidence:** `evidence/phase-0/dotnet-info-dev.txt`

---

### 🔴 P0-02 · Capture Windows baseline for host and every client — BLOCKED, no clients provisioned

**Spec:** §3 · **Closes:** G-30

**Do:** Record Windows edition, build, and architecture for the host laptop and **each** client laptop. Also record each client's screen resolution and display scaling — the UI baseline is 1366×768 and must stay usable at 125%.

> **Result.** User confirmed the dev machine (`LAPTOP-3HH6OHHE`, Windows 11 Home Single Language build 26200, 64-bit x64) doubles as the host laptop at this stage. That row is filled in manifest §2. **No client laptops are provisioned yet** — this is currently a solo-developer setup. Manifest §3 is left as unfilled placeholders with an explicit status note rather than faked/duplicated data. Card stays open until real client machines exist and are captured individually — no "same as above" shortcuts, per the card's own instruction.

**Done when:**

- [x] Host row complete in manifest §2 (Windows edition/build/architecture/machine name only — other host-row fields belong to P0-03/P0-04/P0-05 and are filled by those cards)
- [ ] One complete block per client in manifest §3 — **not satisfied, no client machines exist**
- [ ] Resolution and scaling recorded per client — **not satisfied, no client machines exist**
- [x] Host machine confirmed 64-bit; client confirmation pending client provisioning

**Unblocked by:** one physical (or virtual) client laptop existing. Nothing else. No agent action can advance this card.

---

### 🟡 P0-03 · Install and strip XAMPP — phpMyAdmin cross-machine test pending client laptop

**Spec:** §3, §17 · **Closes:** G-03 (partially)

**Do:** Install XAMPP on the host. Stop **and disable** Apache, FileZilla, Mercury, and Tomcat. Keep MariaDB only. Confirm the XAMPP dashboard and phpMyAdmin are not reachable from the store LAN.

> **Result.** XAMPP 8.2.12-0 was already installed (found during P0-04). Apache/FileZilla/Mercury/Tomcat were stopped by the user via the Control Panel GUI (the `xampp-control.ini` `[EnableModules]` key was locked by the running control-panel process and, on inspection, only governs UI visibility, not autostart — so editing it would not have proven anything). Confirmed only `mysqld.exe` is running; no Windows services registered for any XAMPP component; no autostart entries in HKCU/HKLM Run keys or Startup folders; ports 80/443/21/25/110/8080 not listening. XAMPP version and this evidence recorded in manifest §2. Evidence captured as `evidence/phase-0/xampp-services.txt` (PowerShell process/service/port output substituted for a GUI screenshot, by agreement — see manifest §2 note). **phpMyAdmin unreachable from an actual client laptop is not verified** — no client laptop exists yet (same gap as P0-02). Apache being stopped and port 80/443 not listening makes it unreachable by construction from this host's perspective, but a genuine cross-machine LAN test is deferred until a client is provisioned.

**Done when:**

- [x] Only MariaDB runs; evidence at `evidence/phase-0/xampp-services.txt` (screenshot substituted with PowerShell output, by agreement)
- [x] Apache/FileZilla/Mercury/Tomcat confirmed not running, no OS-level autostart mechanism found
- [ ] phpMyAdmin unreachable from a client laptop — **not verified, no client laptop provisioned; deferred with P0-02**
- [x] XAMPP version recorded in manifest §2 — `8.2.12-0`, now also pinned in ADR-002

**Unblocked by:** a client laptop, to run the cross-machine reachability test from.

> **P0-07 addition.** When this test is finally run, check the **Tailscale** interface too (`100.76.155.51`). The host has a second network path that is not the store LAN, and "unreachable from the client" must hold on both.

---

### ✅ P0-04 · Pin MariaDB details

**Spec:** §6.3 · **Closes:** G-04 (begins)

**Do:** Record the exact MariaDB version, config file path, data directory, port, and **which dump tool ships with this distribution**. Set `bind-address` to loopback.

> **Resolved — do not re-litigate.** This build ships `mysqldump.exe` only. `mariadb-dump.exe` and `mariadb.exe` do not exist here, so the original "`mariadb-dump` preferred over `mysqldump`" wording named a binary that is not present. P1-17 builds on `C:\xampp\mysql\bin\mysqldump.exe`. See ADR-003.1.

The dump tool matters more than it looks: the entire backup strategy (P1-17) is built on whichever binary actually exists, and XAMPP distributions differ.

> **Result.** This XAMPP distribution (MariaDB 10.4.32) ships no `mariadb`/`mariadb-dump` binaries at all — only the `mysql*.exe` family. P1-17 must be built on `mysqldump.exe`, not the preferred `mariadb-dump`. `bind-address` was previously unset (server listened on wildcard `::`); it is now `127.0.0.1`, confirmed by restart and a fresh `Get-NetTCPConnection` check.

**Done when:**

- [x] MariaDB version captured — via `mysqld.exe --version`, since `mariadb.exe` does not exist in this build
- [x] Dump tool identified, path recorded, `--version` captured
- [x] `bind-address` set to loopback; config excerpt at `evidence/phase-0/mariadb-config.txt`
- [x] MariaDB restarted and still serving locally after the bind change
- [x] Values transferred into `docs/adr.md` ADR-002

> **P0-07 extension.** The 10.4-specific realities behind these values are now measured and pinned in **ADR-003 (ACCEPTED)**: `uca1400` collations do not exist here (11.x only), `transaction_isolation` is `tx_isolation` on 10.4, there is no `UUID` type, `innodb_default_row_format=dynamic` so `VARCHAR(255)` utf8mb4 unique indexes are safe, and — most importantly — **`sql_mode` is not strict**, so the server silently truncates and rounds. Evidence: `evidence/phase-0/p0-07-mariadb-10.4-constraints.txt`. P1-04 and P1-05 inherit an action from this.

---

### 🔴 P0-05 · Network and host addressing — BLOCKED, no clients to verify resolution from

**Spec:** §8 · **Closes:** G-25 (begins)

**Do:** Reserve the host's LAN IP. Establish `MERCH-HOST` name resolution from every client (hosts file or DNS). This name must match the certificate SAN in P1-09 — decide it now, not later.

> **Result.** Host IP reserved as a static address on the Wi-Fi adapter (`192.168.100.165/24`, DHCP disabled), applied via an elevated PowerShell session since the working session isn't admin-elevated; post-change connectivity to the gateway verified (2/2 successful). Subnet `192.168.100.0/24` recorded in manifest §4. Host name choice `MERCH-HOST` confirmed and noted in ADR-011 (without resolving the ADR — the certificate-strategy decision itself is still P1-09's job). **`MERCH-HOST` resolution cannot be established or tested from any client** — no client laptops are provisioned yet, same gap as P0-02/P0-03. Card stays open until a client exists to configure and ping.

**Done when:**

- [x] Host IP reserved; method recorded (static IP on host adapter, not a router DHCP reservation — see manifest §4 for why)
- [ ] `ping MERCH-HOST` succeeds from **every** client — **not satisfied, no client machines exist**
- [x] Subnet recorded in manifest §4
- [x] Name choice recorded in ADR-011 (confirmation note only; ADR itself stays PENDING for P1-09)

**Unblocked by:** (a) a client laptop to resolve `MERCH-HOST` from, and (b) router admin access to clear the conflict risk below. Neither is an agent action.

> **P0-07 additions — two things this card now also owes.**
>
> 1. **The static IP is very likely inside the router's DHCP pool.** Live neighbours were observed at `.1, .6, .74, .83, .149, .174, .175, .187, .191` — on both sides of `.165` and up to `.191`, all randomised MACs typical of phones cycling through a pool. Windows DAD said `Preferred` at assignment, so there is no conflict *today*; that is not a guarantee. A duplicate address handed out mid-demo is the most likely way this bites. **Max: open `http://192.168.100.1` and answer "is 192.168.100.165 inside the DHCP pool, and what is the range?"** Then move the host IP out of the pool, shrink the pool, or convert to a MAC reservation.
> 2. **The host's own hosts-file entry was not applied.** It needs an elevated shell; the agent session was not elevated and self-elevation was refused by the tooling's permission boundary. The one-line command is in `docs/installation-guide.md` §1.2. Applying it does **not** close this card — the card requires resolution *from a client*.
>
> Also recorded: this whole configuration is bound to the `HUAWEI-5G-fP2f 2` network and must be redone on the classroom or store network.

---

### 🟡 P0-07 · Initialise the repository

**Do:** `git init`; create the folder structure from `plan.md` §2; commit the scaffold (`CLAUDE.md`, `plan.md`, `tasks.md`, `Directory.Build.props`, `Directory.Build.targets`, `.gitignore`, `.editorconfig`, `docs/`, `scripts/`). Create empty `src/`, `db/migrations/`, and `evidence/phase-0..7/` directories.

Also delivers the session automation: `.claude/skills/task/`, `.claude/skills/phase-gate/`, the three-layer Claude Code hooks in `.claude/settings.json`, and the git pre-commit hook.

**Done when:**

- [x] `git log` shows an initial commit
- [ ] Folder structure matches `plan.md` §2
- [x] `pwsh ./scripts/install-hooks.ps1` run; pre-commit hook installed
- [x] A test commit confirms the hook fires

> **Left open deliberately.** The directory skeleton exists (`src/`, `db/migrations/`, `evidence/phase-0..7/`, `.claude/`), but `plan.md` §2 also lists `docs/*.md` documents that do not exist yet (`database-design.md`, `api-specification.md`, `role-permission-matrix.md`, `ui-specification.md`, `backup-restore-guide.md`, `user-guide.md`, `test-plan.md`), plus `scripts/publish-release.ps1` and `Merchandising.sln`. The eleven `src/` project directories are P1-01's job. Tick the structure box when those exist — not before.
>
> **P0-07 progress:** `docs/installation-guide.md` now exists (created for the `MERCH-HOST` procedure), so one of the eight is done. `.gitattributes` — which `plan.md` §2 does *not* list but should — was also created; see the P0-07 close-out note below.

> **P0-07 close-out — what this session added to the scaffold.**
>
> - **`.gitattributes` created.** It was missing, and `core.autocrlf=true` with no attributes file meant git decided line endings by guesswork. The git pre-commit hook is a `#!/bin/sh` script: a CRLF shebang makes `sh` look for an interpreter named `/bin/sh\r`, fail, and **the hook stops running without saying anything**. That hook is the only layer that catches Visual Studio edits.
> - **A trap in the fix itself.** Marking `*.ps1` as `eol=crlf` would have converted `scripts/install-hooks.ps1`, whose here-string *contains* the hook body — reintroducing the exact CRLF shebang the file exists to prevent. `install-hooks.ps1` now normalises the body to LF, writes bytes directly, and **refuses to install** a hook whose shebang ends CRLF. Verified: reinstalled hook has 0 CRLF pairs and still blocked a commit containing a `.cs` file.
> - **`git add --renormalize .` run** — no unexpected churn; stored content was already LF.
> - **Two hook scripts fixed** (`2>&1` → `*>&1`) after a live run showed them blocking with an empty message. See P0-08 SECTION 8.
> - **`Directory.Build.targets` confirmed dead code** and commented as such — its guardrail target is conditioned on `Merchandising.Api`, which does not exist until P1-02.

> **Environment note.** `git init` had not been run in this directory. `git rev-parse` was resolving to a stray repository at `C:\.git` (someone ran `git init` at the drive root), which would have made `git add -A` catastrophic. A repository now exists at the project root and shadows it. **The stray `C:\.git` was left in place — deleting it is your call, not the agent's.**

---

### ✅ P0-08 · Prove the guardrails actually fail

**Closes:** G-01 (mechanism), G-10 (mechanism)

**Do:** Run `scripts/check-no-csharp.ps1` on the clean repo — it must pass. Then **deliberately break each guardrail** and confirm each one fails. A guardrail nobody has seen fail is a guardrail nobody knows works.

**Done when:**

- [x] Clean run passes
- [x] Drop `src/Merchandising.Domain/Test.cs` → G-A fails → file deleted
- [x] Add a fake `Merchandising.Infrastructure` reference to a client `.vbproj` → G-B fails → reverted
- [x] Add `Server=localhost;Database=x;` to a client file → G-C fails → reverted
- [x] Add `<OptionStrict>Off</OptionStrict>` to a `.vbproj` → G-D fails → reverted
- [x] Pre-commit hook blocks a commit containing a `.cs` file

**Evidence:** `evidence/phase-0/p0-08-guardrail-proofs.txt` — paste each failure output

> **Note:** the guardrail script was authored but never executed (the authoring environment has no PowerShell). Treat this task as a real test of the script, not a formality. If a check misfires, fix the script — that is expected first-run work, not a defect in the plan.

> **Result of the first execution: 9 of 9 cases correct, no fixes needed.** Beyond the six boxes above, the run also proved a `MySqlConnector` `PackageReference` trips G-B, `<PublishAot>true</PublishAot>` trips G-D, and `.cs`/`.csproj` files under `bin/`, `obj/`, and `.vs/` are correctly ignored. `src/` was empty, so minimal stub `.vbproj` files were created to make cases 3–7 honest, then removed; `src/` is empty again. `git commit --no-verify` was confirmed to still work as the deliberate escape hatch.
>
> **One item was deferred — it is now closed.** Claude Code loads hooks and skills at *session start*, so `.claude/settings.json` and the two skills — created in the same session — could not be exercised through the runtime. §6.1 of the evidence file listed four checks to re-run at the next session start.
>
> **All four executed live at P0-07 and all four pass.** SECTION 8 of the evidence file records them verbatim. Headlines:
>
> - **PreToolUse genuinely blocks.** A `Write` of `src/Merchandising.Domain/Scratch.cs` was refused with exit 2. The predicted failure mode — `$CLAUDE_PROJECT_DIR` being bash-only syntax that PowerShell leaves unexpanded, turning a block into a silent allow — **did not occur.** Claude Code substitutes the variable before invoking the shell. `.claude/settings.json` needed no change and was not changed.
> - **All three layers proven behaviourally**, each by making it block a real tool call: L1 on a `.cs` write, L2 on a `.vbproj` write, L3 by planting a violation with PowerShell (bypassing L1 exactly as a Visual Studio edit would) and ending the turn.
> - **A real defect was found and fixed.** Both hook scripts captured the guardrail run with `2>&1`, but `check-no-csharp.ps1` reports through `Write-Host` — the *information* stream. The capture was always empty, so the hooks blocked correctly while reporting **nothing about what failed**. Changed to `*>&1`; the failing guardrail and offending path are now named. Earlier tests could not have caught this: they asserted on exit codes, and the git hook runs the script inline where `Write-Host` reaches the console anyway.
> - **Stop hook measured:** mean 779 ms wall clock over 5 runs (guardrail sweep ~213 ms, pwsh startup ~566 ms fixed). Loop protection re-confirmed. Re-measure at the Phase 1 gate.
>
> `/hooks` itself was **not** run: it is an interactive CLI command with no agent-invokable form. Recorded as not-run rather than assumed — the behavioural proof above is stronger than a listing anyway, since a hook can be listed and still be broken.

---

## Phase 0 exit gate

- [ ] Manifest complete for dev machine, host, and **every** client — **host and dev complete; blocked on client provisioning**
- [x] ADR-002 populated with real versions — every row except MySqlConnector, which belongs to P1-05. ADR-003 additionally ACCEPTED with the measured 10.4 constraints.
- [ ] Approvals documented with evidence — **blocked on Max attaching two screenshots (P0-06)**
- [x] Repo scaffolded, hooks installed, guardrails proven to fail correctly — all four layers now proven **live**, not just by script (P0-08 SECTION 8)

**Gate verdict: FAIL — 2 of 4 criteria unmet.** Both remaining criteria are blocked on things no agent can do: a client laptop existing, and two screenshots being attached. Nothing is blocked on engineering work.

> **Phase 0 does not gate Phase 1.** P1-01 through P1-08 and P1-11 through P1-14 need none of the outstanding items. Only P1-09, P1-10, P1-15 and one sub-check of P1-04 need the client laptop. Do not treat this FAIL as a reason to wait — see `evidence/phase-0/PHASE-0-READINESS.md`.

---

## P0-07 close-out session — what changed (2026-08-15)

One session, no application code, no project created under `src/`. Full account in `evidence/phase-0/PHASE-0-READINESS.md`.

| Area | Outcome |
|---|---|
| Hook wiring | All 4 deferred checks executed live and passing. **One real defect found and fixed** (empty guardrail relay). `settings.json` needed no change. |
| MariaDB 10.4 | ADR-003 **ACCEPTED** with 8 measured constraints. **`sql_mode` is not strict — silent truncation demonstrated.** |
| ADR-002 | Completed; only the connector row remains, owed by P1-05. |
| `CLAUDE.md` §6 | Version pins + a MariaDB-10.4-vs-MySQL-8 "do not use" table now in context every session. |
| `.gitattributes` | Created. Fixed a trap in its own fix (`install-hooks.ps1` CRLF shebang). |
| Visual Studio | Re-verified properly with `vswhere -requires`. Both workloads present. |
| Backup directory | `C:\MerchandisingBackups` created, ACL tightened, write-proven. Off-host destination still Max's call. |
| **Rung A pre-flight** | **Built, ran, served `/health`, published both ways — 0 warnings, first attempt, zero friction. P1-02a should be skipped.** |

---

# Phase 1 — Foundation Proof-of-Concept

**The gate.** Prove all twelve items in spec §24 with captured evidence.

**Scope discipline:** one WPF window with two buttons. One product. One protected endpoint. If you catch yourself designing a product grid, stop — that is Phase 2.

---

## Track A — Solution and API skeleton

### ⬜ P1-01 · Create the solution and all eleven empty VB projects

**Spec:** §7

**Files:** `Merchandising.sln`, `src/**/*.vbproj`

**Do:** Create every project from `plan.md` §2 with the exact names in spec §7 (they are the contract — do not rename). Wire project references to match the dependency diagram. Libraries and API use `$(MerchNetTfm)`; WPF clients use `$(MerchWindowsTfm)`.

**Done when:**

- [ ] All eleven projects exist with correct names and types
- [ ] References match `plan.md` §2 exactly
- [ ] `ClientCommon` references **only** `Contracts` — verified by reading the file, not assumed
- [ ] `dotnet build` succeeds
- [ ] `check-no-csharp.ps1` passes

**Evidence:** `evidence/phase-1/p1-01-build.log`

---

### ⬜ P1-02 · Hand-author the API on the Web SDK (rung A) 🎯

**Spec:** §6.2 · **Closes:** G-01, G-02 · **Decides:** ADR-001

> **This is the task the whole project is gated on.** Everything after it is ordinary engineering.

**Do:** Author `Merchandising.Api.vbproj` with `Sdk="Microsoft.NET.Sdk.Web"`, `OutputType=Exe`. Write `Program.vb` using `Module Program` / `Sub Main` (**not** top-level statements). Add one controller `HealthController` exposing `GET /health` returning `{status, version, utcTime}` — and **nothing** about the database, environment, or configuration.

**Done when:**

- [ ] `.vbproj` uses the Web SDK; no C# files anywhere in the project
- [ ] `dotnet run` starts Kestrel
- [ ] `GET /health` returns 200 with the expected JSON shape
- [ ] Health response contains no secrets, DB status detail, or environment strings
- [ ] `EnableRequestDelegateGenerator=false` inherited from `Directory.Build.props`
- [ ] ADR-001 recorded with the rung reached and **any** friction encountered

**Evidence:** `p1-02-health-response.txt`, `p1-02-project-file.txt`

**Blocker protocol:** if the Web SDK fails on a C#-assuming target, attempt rung B **once**, record the exact error, log it in ADR-001. **Do not spend more than one session fighting rung A.**

---

### ⬜ P1-02a · Rung B insurance spike — conditional, 30 minutes

**Run only if P1-02 showed friction** (a workaround, a suppressed warning, a non-standard property). If rung A built cleanly first try, **skip this** — proving a fallback you have no reason to need is busywork.

> **P0-07 pre-flight says: expect to SKIP this card.** A disposable rung A probe was built outside the repo on 2026-08-15. It built with **0 warnings and 0 errors on the first attempt**, ran under `dotnet run`, served `GET /health` → `200`, and published both framework-dependent and self-contained `win-x64` — with **both published executables actually run and serving**. No SDK target needed a workaround, nothing was suppressed, and `EnableRequestDelegateGenerator=false` was proven to be precaution rather than necessity (removed, rebuilt, still clean — the RDG only affects minimal APIs and this design uses controllers). The Web SDK also generated **zero `.cs` files** for a VB project.
>
> That is advance information, not a result for P1-02. Run P1-02 properly in-repo. **If it behaves differently from the probe, that difference is itself the friction and this card triggers on it.** Evidence: `evidence/phase-0/p0-07-rung-a-preflight.txt`, ADR-001 pre-flight note.

If it did show friction, the calculus changes: friction now suggests a future SDK update could break rung A outright, and PA-001 granted no C# escape hatch, so rung B is the last self-service option.

**Do:** On a scratch branch, build the same `/health` endpoint with `Sdk="Microsoft.NET.Sdk"` + `<FrameworkReference Include="Microsoft.AspNetCore.App" />`.

**Done when:**

- [ ] `/health` responds, **or** the failure mode is recorded in ADR-001
- [ ] Either outcome recorded — a negative result is a valid result

**Evidence:** `p1-02a-rungb-result.txt`

---

### ⬜ P1-03 · Publish `win-x64` and run the published output

**Spec:** §18 · **Closes:** G-16 (begins) · **Decides:** ADR-010

**Do:** Publish framework-dependent, then self-contained. Run each from a clean folder. This keeps the deployment decision open until you have evidence for it.

**Done when:**

- [ ] Framework-dependent publish runs from a clean folder and serves `/health`
- [ ] Self-contained publish also succeeds
- [ ] Neither sets `PublishAot` or `PublishTrimmed`
- [ ] ADR-010 records the choice and why

**Evidence:** `p1-03-publish.log`

> **Decision point.** P1-02 + P1-03 passing retires the project's dominant risk. Record it in the ADR before moving on.

---

## Track B — Database and migrations

### ⬜ P1-04 · MariaDB setup and least-privilege accounts

**Spec:** §17 · **Closes:** G-03, G-10 (begins) · **Decides:** ADR-003

**Do:** Create database `merchandising` (InnoDB, `utf8mb4`). Create `merch_api` (DML + limited DDL) and `merch_backup` (`SELECT, LOCK TABLES, SHOW VIEW, EVENT, TRIGGER` only). Root is used by **no** application.

**Done when:**

- [ ] Both accounts created with least privilege
- [ ] `merch_api` **cannot** `DROP DATABASE` — attempt recorded
- [ ] `merch_backup` **cannot** write — attempt recorded
- [ ] Root login from a client machine fails
- [x] ADR-003 records charset, collation, engine — **already ACCEPTED at P0-07 with measured values.** This card now *consumes* ADR-003 rather than deciding it.
- [ ] **`STRICT_TRANS_TABLES` added to `sql_mode` in `C:\xampp\mysql\bin\my.ini`, server restarted, and the change logged in the manifest §6**

> **P0-07 hands this card a defect to fix, not just a decision to record.**
>
> XAMPP ships `sql_mode=NO_ZERO_IN_DATE,NO_ZERO_DATE,NO_ENGINE_SUBSTITUTION` — **`STRICT_TRANS_TABLES` is absent**, which is weaker than MariaDB 10.4's own default. Demonstrated on this server: inserting `'THIS-SKU-IS-FAR-TOO-LONG'` into `VARCHAR(8)` stored `'THIS-SKU'`, and `1.9999` into `DECIMAL(19,3)` stored `2.000` — **both reported as success**. With strict mode the same insert is rejected with `ERROR 1406 (22001)`.
>
> A silently truncated SKU and a silently rounded quantity, invisible to the API, in a system whose entire value is an accurate ledger. Fix it here in `my.ini` **and** again per-connection at P1-05, so a XAMPP reinstall cannot quietly revert the guarantee. See ADR-003.2.
>
> Also inherit from ADR-003: state `utf8mb4` / `utf8mb4_unicode_ci` / `InnoDB` **explicitly** on every object — the server default collation is `utf8mb4_general_ci`, not what we want. And do not reach for `uca1400` collations; zero of them exist on 10.4.

**Evidence:** `p1-04-grants.txt`, `p1-04-negative-tests.txt`

---

### ⬜ P1-05 · Connection layer using MySqlConnector

**Spec:** §6.3 · **Closes:** G-04 · **Decides:** ADR-002

**Files:** `src/Merchandising.Infrastructure/Data/*.vb`

**Do:** Connection factory + options binding. Config read from an ACL-protected host location **outside** the binaries and outside the repo.

**Done when:**

- [ ] API connects to the pinned MariaDB
- [ ] Connection string appears in **no** committed file (G-C passes)
- [ ] Connection string appears in **no** client project
- [ ] MySqlConnector version pinned in ADR-002 — **the only row still PENDING in that ADR**
- [ ] Config file location documented for the installation guide
- [ ] **Connection factory sets `sql_mode` to include `STRICT_TRANS_TABLES` on every connection** — belt and braces with the `my.ini` change at P1-04, so a XAMPP reinstall cannot silently revert it (ADR-003.2)
- [ ] Connection sets isolation level explicitly — the server default is `REPEATABLE-READ`, but ADR-006 requires `READ COMMITTED`. On 10.4 the variable is **`tx_isolation`**; `transaction_isolation` does not exist and raises `ERROR 1193`.

**Evidence:** `p1-05-connection-test.log`

---

### ⬜ P1-06 · Migration runner

**Spec:** §6.3 · **Decides:** ADR-008

**Files:** `src/Merchandising.Maintenance/Migrations/*.vb`

**Do:** Discover `db/migrations/NNNN_*.sql`, checksum each, apply unapplied ones in order inside a transaction, record identifier/checksum/timestamp/result in `SchemaMigrations`. **Refuse to run** if an applied file's checksum changed.

**Done when:**

- [ ] First run applies migrations and records them
- [ ] Second run applies nothing
- [ ] Tampering with an applied file causes a clear refusal, not a silent skip
- [ ] A failing migration rolls back and records the failure

**Evidence:** `p1-06-migration-run.log`, `p1-06-tamper-refusal.log`

---

### ⬜ P1-07 · Migration 0001 — POC schema slice

**Spec:** §12 · **Closes:** G-20, G-21 (begins) · **Decides:** ADR-004

**Files:** `db/migrations/0001_foundation.sql`

**Do:** `Users`, `Roles`, `UserRoles`, `Products`, `StockBalances`, `StockMovements`, `AuditLogs`, `IdempotencyKeys`, `SystemSettings`, `SchemaMigrations`. Money `DECIMAL(19,4)`, quantity `DECIMAL(19,3)`, timestamps `DATETIME(6)` UTC. `StockMovements` and `AuditLogs` append-only **by grant**, not just by policy.

**Done when:**

- [ ] Migration applies clean on an empty database
- [ ] `UPDATE` on `AuditLogs` as `merch_api` is **rejected by privilege**
- [ ] `DELETE` on `StockMovements` as `merch_api` is **rejected by privilege**
- [ ] `0.001` and `12345678901234.5678` round-trip exactly
- [ ] `IdempotencyKeys` has a unique constraint on `(Scope, KeyValue)`
- [ ] PA-003 raised with the professor

> **P0-07 pre-checks — these were proven on the real server, so 0001 should not surprise you.** A table with two `VARCHAR(255)` utf8mb4 **unique** indexes (SKU and barcode) created without error: `innodb_default_row_format=dynamic`, 16 KB pages, 3072-byte key prefix limit, 255×4 = 1020 bytes used — roughly 3× headroom. `DECIMAL(19,4)` and `DECIMAL(19,3)` round-tripped `12345678901234.5678` and `0.001` exactly alongside a `DATETIME(6)`.
>
> Two 10.4 constraints to write around: there is **no `UUID` column type** (added in 10.7) — use `CHAR(36)` or `BINARY(16)` for correlation and idempotency keys; and `lower_case_table_names=1` on Windows, so `StockBalances` is stored as `stockbalances`. Harmless on a Windows-only system, but a dump from this host would not restore cleanly onto a case-sensitive Linux server.

**Evidence:** `p1-07-audit-immutability.txt`, `p1-07-precision-check.txt`

---

## Track C — Security seam

### ⬜ P1-08 · Authentication and one protected endpoint

**Spec:** §9 · **Closes:** G-19 (begins) · **Decides:** ADR-005

**Do:** Salted password hashing via a framework-provided hasher. `POST /api/v1/auth/login`, `GET /api/v1/auth/me`, one policy-gated endpoint. Lockout after five failed attempts in the configured window.

**Done when:**

- [ ] Valid login returns a token
- [ ] Protected endpoint: 401 without token, 403 with wrong role, 200 with correct role
- [ ] Six bad passwords trigger lockout; the lockout event is logged
- [ ] **No password value appears in any log** — log scan attached
- [ ] Token lives in client memory only
- [ ] ADR-005 records the token scheme

**Evidence:** `p1-08-auth-matrix.txt` (one row per case), `p1-08-log-scan.txt`

---

### ⬜ P1-09 · HTTPS with a LAN-valid certificate

**Spec:** §8, §17 · **Closes:** G-07, G-25 · **Decides:** ADR-011

**Do:** Kestrel on `https://0.0.0.0:8443`. Certificate subject/SAN covering `MERCH-HOST` (and the reserved IP if clients will use it). Document the client trust procedure. Dev HTTP profile isolated and visibly labelled non-production.

**Done when:**

- [ ] Client reaches `https://MERCH-HOST:8443/health` with no certificate warning after following the procedure
- [ ] Trust procedure written and followed on **every** client
- [ ] An untrusted client fails clearly; the WPF client surfaces a readable message rather than silently proceeding
- [ ] Dev HTTP profile displays a non-production warning
- [ ] Firewall allows 8443 from the private subnet only

**Evidence:** `p1-09-cert-details.txt`, `p1-09-client-trust-steps.md`, `p1-09-invalid-cert-behaviour.png`

---

### ⬜ P1-10 · Negative security tests

**Spec:** §17 · **Closes:** G-03, G-10

**Do:** From a **client laptop**: attempt a direct MariaDB connection; call a protected endpoint unauthenticated; call it with a valid token but insufficient role.

**Done when:**

- [ ] MariaDB port unreachable from the client
- [ ] Unauthenticated → 401
- [ ] Wrong role → 403
- [ ] Error bodies carry a correlation ID and **no** stack trace, SQL, or connection detail
- [ ] phpMyAdmin unreachable from the client

**Evidence:** `p1-10-port-scan.txt`, `p1-10-denials.txt`

---

## Track D — Transactional core

### ⬜ P1-11 · Atomic stock decrement

**Spec:** §11 · **Closes:** G-11 (begins), G-12 (begins) · **Decides:** ADR-006

**Do:** One API command that in **one** transaction: conditionally decrements the balance, inserts a `StockMovements` row, inserts an `AuditLogs` row, and verifies the affected-row count. Use the conditional-update SQL from `CLAUDE.md` §5 — **never** read-then-write.

**Done when:**

- [ ] Success produces exactly one movement, one audit row, correct balance
- [ ] Insufficient stock returns a controlled response and creates **zero** rows
- [ ] Affected-row count is verified before returning success
- [ ] Correlation ID recorded on both movement and audit rows

**Evidence:** `p1-11-happy-path.txt`, `p1-11-insufficient-stock.txt`

---

### ⬜ P1-12 · Forced-failure rollback proof

**Spec:** §11 · **Closes:** G-12

**Do:** Test-only fault injection that throws after the movement insert but before commit.

**Done when:**

- [ ] Balance, movement, **and** audit rows are all absent afterwards
- [ ] Before/after row counts recorded
- [ ] Fault injection cannot be enabled in a release build

> A partial commit here is a **phase failure**, not a bug to note and move past.

**Evidence:** `p1-12-rollback.txt`

---

### ⬜ P1-13 · Concurrency proof

**Spec:** §11 · **Closes:** G-11

**Do:** Integration test firing two, then ten, simultaneous decrements against a product with exactly **one** unit of stock.

**Done when:**

- [ ] Exactly one request succeeds
- [ ] All others receive a controlled conflict/insufficient response
- [ ] Final balance is `0` — never negative
- [ ] Movement count is exactly 1
- [ ] Raw result distribution recorded, not just a pass/fail

**Evidence:** `p1-13-concurrency.txt`

---

### ⬜ P1-14 · Idempotency proof

**Spec:** §11 · **Closes:** G-13 · **Decides:** ADR-007

**Do:** Insert-first strategy against `IdempotencyKeys`; store the committed response payload and replay it on repeat.

**Done when:**

- [ ] The same key sent five times produces **one** movement and five identical responses
- [ ] A different key produces a second movement
- [ ] Concurrent requests with the same key produce one movement
- [ ] A key from a *failed* command does not block a legitimate retry

**Evidence:** `p1-14-idempotency.txt`

---

## Track E — Client seam

### ⬜ P1-15 · WPF client spike over HTTPS

**Spec:** §6.1, §16 · **Closes:** G-26

**Files:** `src/Merchandising.ClientCommon/Api/*.vb`, `src/Merchandising.Inventory/Views/*.xaml`

**Do:** Typed API client with in-memory token storage, connection-state detection, correlation-ID propagation. One WPF window: login, call protected endpoint, display result, trigger the decrement, show connection status.

**Done when:**

- [ ] Full round trip over HTTPS **from a client laptop**, not the dev machine
- [ ] Stopping the API produces a clear connection-unavailable state
- [ ] The client **refuses** to queue or fake the write when offline
- [ ] Token never written to disk
- [ ] `ClientCommon` still references only `Contracts` (G-B passes)

**Evidence:** `p1-15-client-roundtrip.png`, `p1-15-api-down-state.png`

---

## Track F — Hosting and recovery

### ⬜ P1-16 · Windows Service hosting

**Spec:** §6.4 · **Closes:** G-09

**Do:** Service registration, restricted identity, filesystem permissions, Windows Event Log, recovery actions (restart on failure).

**Done when:**

- [ ] Service installs and starts
- [ ] **Host reboots and the API serves with no human action**
- [ ] A forced process kill triggers automatic restart
- [ ] Events appear in Event Viewer
- [ ] Service account is not an administrator where practical

**Evidence:** `p1-16-service-config.txt`, `p1-16-post-reboot-health.txt`, `p1-16-recovery.png`

---

### ⬜ P1-17 · Backup via the maintenance utility

**Spec:** §15 · **Closes:** G-15

**Do:** `Merchandising.Maintenance backup` using **`C:\xampp\mysql\bin\mysqldump.exe`** (P0-04: no `mariadb-dump.exe` exists in this distribution), under `merch_backup`. Write to a protected directory outside the binaries and **not** served by the API. Record size, checksum, timestamp, source DB version, result. Copy off-host. Register in Task Scheduler.

> **P0-07 groundwork.** The protected directory already exists: **`C:\MerchandisingBackups`** — outside the repo, outside `C:\xampp`, inheritance disabled, `Authenticated Users: Modify` removed (a dump contains every password hash), create/read/delete proven after the ACL change. Recorded in manifest §2.
>
> **The off-host destination is still not chosen** — it needs a physical drive Max selects, and this card cannot complete without it. Revisit the ACL when P1-16 defines the service identity: the backup account should end up with the narrowest grant that still lets the scheduled job run.

**Done when:**

- [ ] Scheduled run produces a valid dump + off-host copy + `BackupLogs` row
- [ ] Checksum recorded and verifiable
- [ ] A deliberately broken run (wrong credentials) **records failure and warns** rather than failing silently
- [ ] Backup directory is not reachable through any API endpoint
- [ ] Retention count configurable via `SystemSettings`

**Evidence:** `p1-17-backup-success.log`, `p1-17-backup-failure.log`

---

### ⬜ P1-18 · Restore rehearsal with maintenance mode

**Spec:** §15 · **Closes:** G-14, G-08 (begins)

**Do:** The full seven-step spec §15 procedure. Restore must **not** be executable as an ordinary live API request.

**Done when:**

- [ ] Maintenance lock rejects ordinary writes while held
- [ ] Connected clients display the maintenance warning
- [ ] Restore completes from the **off-host** copy
- [ ] Expected users, products, balances present afterwards
- [ ] **Measured RTO recorded** — an actual number, not the target
- [ ] Lock releases only after verification succeeds
- [ ] No API endpoint can trigger a restore

**Evidence:** `p1-18-restore-log.txt`, `p1-18-verification-checklist.md`

---

## Track G — Test harness and closure

### ⬜ P1-19 · Wire both VB test projects

**Spec:** §7, §19 · **Decides:** ADR-009

**Do:** Both test projects in Visual Basic. `InternalsVisibleTo` configured. Integration tests run against the **real** pinned MariaDB — never an in-memory substitute.

**Done when:**

- [ ] `run-tests.ps1` executes both suites green
- [ ] P1-12, P1-13, P1-14 run as **automated tests**, not manual steps
- [ ] All test source is VB (G-A passes)
- [ ] `WebApplicationFactory` can reach `Program`
- [ ] ADR-009 records the framework

**Evidence:** `p1-19-test-run.log`

---

### ⬜ P1-20 · Phase 1 closure pack

**Spec:** §24

**Do:** Complete `docs/adr.md` (every PENDING resolved), complete the environment manifest, build an evidence index mapping each of spec §24's twelve steps and seven exit criteria to its artifact.

**Done when:**

- [ ] Every ADR entry is ACCEPTED — none left PENDING
- [ ] Every spec §24 row has a linked artifact
- [ ] **No row says "verified informally"**
- [ ] Environment manifest fully populated
- [ ] A clean clone builds and tests green from scratch

**Evidence:** `evidence/phase-1/INDEX.md`

---

## Phase 1 gate

**Pass** — all tasks complete with evidence; every spec §24 exit criterion has an artifact. → Regenerate `tasks.md` for Phase 2.

**Partial** — descended to rung B, everything else passes. → Proceed; update ADR-001; inform the professor.

**Fail** — rungs A and B both fail. → **Stop. Do not start Phase 2.** Take the recorded errors to the professor and request an exception under PA-001. The evidence you captured is what makes that conversation go well.
