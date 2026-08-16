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
>
> **P1-01 progress:** the eleven `src/` project directories and `Merchandising.sln` now exist, so those parts of `plan.md` §2 are satisfied. **Box still unticked** — seven `docs/*.md` documents (`database-design`, `api-specification`, `role-permission-matrix`, `ui-specification`, `backup-restore-guide`, `user-guide`, `test-plan`) and `scripts/publish-release.ps1` do not exist yet. `src/.gitkeep` was removed, having served its purpose.

> **P0-07 close-out — what this session added to the scaffold.**
>
> - **`.gitattributes` created.** It was missing, and `core.autocrlf=true` with no attributes file meant git decided line endings by guesswork. The git pre-commit hook is a `#!/bin/sh` script: a CRLF shebang makes `sh` look for an interpreter named `/bin/sh\r`, fail, and **the hook stops running without saying anything**. That hook is the only layer that catches Visual Studio edits.
> - **A trap in the fix itself.** Marking `*.ps1` as `eol=crlf` would have converted `scripts/install-hooks.ps1`, whose here-string *contains* the hook body — reintroducing the exact CRLF shebang the file exists to prevent. `install-hooks.ps1` now normalises the body to LF, writes bytes directly, and **refuses to install** a hook whose shebang ends CRLF. Verified: reinstalled hook has 0 CRLF pairs and still blocked a commit containing a `.cs` file.
> - **`git add --renormalize .` run** — no unexpected churn; stored content was already LF.
> - **Two hook scripts fixed** (`2>&1` → `*>&1`) after a live run showed them blocking with an empty message. See P0-08 SECTION 8.
> - **`Directory.Build.targets` confirmed dead code** and commented as such — its guardrail target is conditioned on `Merchandising.Api`, which does not exist yet. *(Corrected 2026-08-16: that comment said the target activates at P1-02. It activates at **P1-01**, which creates the project and builds it. The condition keys on the project name, not on the Web SDK.)* **Superseded at P1-02b: it is no longer dead code — it went live at P1-01 and the "DEAD CODE" header it was given here was left behind. See the P1-02b card.**

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

### ✅ P1-01 · Create the solution and all eleven empty VB projects

**Spec:** §7 · **Consumes:** ADR-009 (ACCEPTED — MSTest 4.0.2; do not re-decide)

**Files:** `Merchandising.sln`, `src/**/*.vbproj`, `src/tests/**/*.vbproj`

**Do:** Create every project from `plan.md` §2 with the exact names in spec §7 (they are the contract — do not rename). Wire project references to match the dependency diagram. Libraries and API use `$(MerchNetTfm)`; WPF clients use `$(MerchWindowsTfm)`.

> **Scope boundary with P1-02 — read this before touching `Merchandising.Api`.**
>
> At P1-01, **`Merchandising.Api` is a plain shell on `Sdk="Microsoft.NET.Sdk"`.** It has its project references wired and it compiles. That is all.
>
> Converting it to `Sdk="Microsoft.NET.Sdk.Web"`, writing `Program.vb` with `Module Program` / `Sub Main`, and adding `HealthController` is **P1-02's work and P1-02's evidence.** Do not do it early. P1-02 is the task the whole project is gated on (ADR-001, rung A vs rung B); it is only a proof if the Web SDK conversion happens *there*, under its own acceptance checks, with its own `p1-02-project-file.txt`. Folding it into P1-01 destroys the evidence trail and leaves ADR-001 resolved by a card that was not asked to resolve it.
>
> A shell that builds is a complete P1-01 result. It is not a partial P1-02.

> **Test projects — ADR-009 is already ACCEPTED, do not re-open it.** MSTest, `MSTest` package `4.0.2`. The framework was resolved early precisely so this card is not blocked: `dotnet new` cannot create a test project without one. **ADR-009.2 lists two defects in the generated VB template that this card must fix** — the C#-only `ImplicitUsings` / `Nullable` / `<Using>` cruft, and `MSTestSettings.vb`'s method-level parallelism, which must become `<Assembly: DoNotParallelize>` in the **integration** project because it shares one real MariaDB.

**Done when:**

- [x] All eleven projects exist with correct names and types
- [x] References match `plan.md` §2 exactly — full graph dumped in evidence §4.2
- [x] `ClientCommon` references **only** `Contracts` — verified by reading the file, not assumed (evidence §4.3)
- [x] **`Merchandising.Api` is a plain shell on `Microsoft.NET.Sdk`** — no Web SDK, no `Program.vb`, no controller. That is P1-02. (evidence §4.6)
- [x] **Every generated `.vbproj` uses `$(MerchNetTfm)` or `$(MerchWindowsTfm)`** — not the hard-coded TFM `dotnet new` emits. Verified by reading all eleven files; a literal `net10.0` or `net10.0-windows` in any `.vbproj` fails this box. **0 occurrences** (evidence §4.1)
- [x] **Both test projects land at `src/tests/`** — proven behaviourally: `run-tests.ps1` *executed* both suites rather than printing "not created yet (task P1-19)" (evidence §3, §4.4)
- [x] Generated test `.vbproj` files stripped of C#-only properties per ADR-009.2; integration project set to `DoNotParallelize`
- [x] `dotnet build` succeeds — **0 warnings, 0 errors**, all eleven projects
- [x] `check-no-csharp.ps1` passes — under **both** `pwsh` and Windows PowerShell 5.1

> **`Directory.Build.targets` fires for the first time on this card's `dotnet build`** — see the note under P1-02 and the file's own header comment. If it misfires, fix it here; that is expected first-run work.

**Evidence:** `evidence/phase-1/p1-01-build.log`

> **Result.** Eleven projects, `Merchandising.sln`, full reference graph wired, build clean at 0 warnings, both test suites executing. **It did not go through cleanly on the first attempt — and the three things that broke are the point of this card, not incidental to it.**
>
> **1. `Directory.Build.props` was not valid XML, and had never been parsed.** Line 19's comment contained the flag form of `dotnet --info`; a double hyphen is illegal inside an XML comment. This broke **every** MSBuild invocation in the repository with `MSB3073`/parse failure. It survived the whole of Phase 0 undetected because `src/` was empty and no build had ever run — precisely the blind spot P0-08's "the script was authored but never executed" note warned about, in a file nobody thought to re-check. Fixed, with a comment in the file explaining why the command name is now written without its dashes. *(Reintroduced twice while fixing it — first by writing the sequence in prose, then by quoting the parser's own error message verbatim. Both caught by re-parsing before proceeding.)*
>
> **2. `check-no-csharp.ps1` failed under Windows PowerShell 5.1** — `$RepoRoot` defaulted to `(Split-Path -Parent $PSScriptRoot)` inside the `param()` block, and `$PSScriptRoot` is empty at param-binding time under `powershell -File`. `Directory.Build.targets` invokes `powershell`, not `pwsh`, **deliberately** (its own comment explains: pwsh is not guaranteed on a machine that only has Visual Studio). So the guardrail worked in every context it had ever been tested in and failed in the one context the build actually uses.
>
> **The loud failure was the lucky outcome.** Had `Split-Path` not rejected an empty string, `$RepoRoot` would have been empty, `$srcPath` would have resolved relative to MSBuild's working directory, `src/` would not have been found there, and the script would have printed *"nothing to check yet"* and **exited 0**. A guardrail reporting a pass because it is looking at the wrong directory is worse than no guardrail, because it is trusted. Fixed three ways: a fallback chain for the script directory, an existence check on the resolved root, and a **marker check** — `CLAUDE.md` must be present at the resolved root or the script refuses to report a pass. Verified under both interpreters plus a negative test (`-RepoRoot $env:TEMP` → exit 1, correctly refusing). The identical `$PSScriptRoot` pattern in `run-tests.ps1` was fixed at the same time; the two scripts sit side by side and get copied from each other.
>
> **3. `dotnet new sln` now defaults to `.slnx`,** the XML solution format, on SDK 10.0.301. `plan.md` §2 names `Merchandising.sln`, so the classic format was forced with `--format sln`. Recorded because the default will keep reasserting itself on any future `dotnet new sln`.
>
> **Two template defects also fixed,** beyond the two ADR-009.2 already predicted: `dotnet new wpflib -lang VB` emits a **duplicated `<RootNamespace>`**, and both MSTest templates nest a `Namespace` block matching the project's own `RootNamespace`, which would have produced `Merchandising.Tests.Unit.Merchandising.Tests.Unit`. The generated `Test1.vb` files asserted nothing and were replaced with `ScaffoldReferenceTests.vb`, which asserts the one thing P1-01 delivers that is observable at run time — that every declared project reference resolves to a loadable assembly. P1-19 deletes them.
>
> **Decisions taken, both confirmed with Max before implementation:**
>
> - **`ClientCommon` is `$(MerchWindowsTfm)` with `UseWPF`, not `$(MerchNetTfm)`.** Spec §7 puts "common controls, converters, styles" and "XAML resources" in that project; a plain `net10.0` library cannot hold a `ResourceDictionary` or implement `System.Windows.Data.IValueConverter`. The card's shorthand said "libraries use `$(MerchNetTfm)`" — the spec outranks the card (`CLAUDE.md` §1), and the alternative was a twelfth project split out at P1-15 for no gain. G-B is unaffected: `ClientCommon` still references `Contracts` and nothing else.
> - **Test project references wired now**, not deferred to P1-19: Unit → Domain, Contracts; Integration → Api, Infrastructure, Domain, Contracts. `plan.md` §2's diagram has no rows for the test projects, so this fills a genuine gap rather than contradicting it.

> **Open item raised here, resolved in P1-01a.** G-B could not tell a package reference from the rule written down in a comment, so documenting the rule inside `Merchandising.ClientCommon.vbproj` failed the build. Raised rather than fixed inline, per `CLAUDE.md` §7. **Closed by P1-01a — see the card below.**

---

### ✅ P1-01a · G-B and G-D must ignore XML comments; G-C must not

**Closes:** the open item raised by P1-01 · **Touches:** `scripts/check-no-csharp.ps1`, four client `.vbproj` files

**Why.** G-B matched raw project-file text, so it read its own prohibition as a violation: writing *"never reference &lt;the connector&gt;"* in a comment inside `Merchandising.ClientCommon.vbproj` **failed the build**. The client project file is the single best place to record why that rule exists, and it was the one place the rule could not be recorded.

**What the fix turned out to cover.** Investigating it showed the defect was wider than the one instance reported. Three of the four checks read `.vbproj` text, and they split cleanly:

| Check | Looks for | Comments? | Why |
|---|---|---|---|
| **G-B** | `ProjectReference` / `PackageReference` | **ignored** | A reference must *take effect* to matter. MSBuild never sees a comment. |
| **G-D** | `OptionStrict Off`, `PublishAot`, `PublishTrimmed` | **ignored** | Same reasoning — and the natural way to warn the next reader is to name the property in a comment saying not to set it. |
| **G-C** | connection strings, credentials | **still scanned** | A credential in a comment **is** a leaked credential. It is in the working tree, in the history, and readable by anyone with a clone. |

**That asymmetry is the substance of this card,** not an implementation detail. G-B and G-D check for things that are inert inside a comment; G-C checks for things that are not. Both behaviours are proven by test, so a future reader cannot mistake the difference for an oversight.

**Do:** strip XML comments before matching in G-B and G-D only, preserving newline counts so reported line numbers stay accurate. Report the offending line number in G-B failures. Leave G-C untouched.

**Done when:**

- [x] Real `ProjectReference` to `Infrastructure` in a client project still **fails** (case 2)
- [x] Real `PackageReference` to a connector package still **fails** (case 3)
- [x] Those same names in a comment only now **pass** (case 4)
- [x] Real `<PublishAot>true</PublishAot>` still **fails** (case 5)
- [x] `PublishAot` / `OptionStrict Off` named in a comment only now **pass** (case 6)
- [x] **Credential inside a comment still fails** — the asymmetry holds (case 7)
- [x] Repository clean before and after the run (cases 1 and 8, bracketing)
- [x] Guardrails green under `pwsh` **and** Windows PowerShell 5.1
- [x] `run-tests.ps1` green; `dotnet build` unaffected
- [x] The rule is now actually written down in all four client `.vbproj` files, naming every forbidden package

**Evidence:** `evidence/phase-1/p1-01a-guardrail-comment-fix.txt`

> **Result: 8 of 8 cases correct.** Each case mutates a real project file, runs the guardrail for real through `pwsh`, and is judged on the actual exit code — no assertion is made by reading the script. The file is restored in a `finally` block, and `git diff` confirmed it was restored byte-for-byte.
>
> **Also proven live through the hook path.** The four `.vbproj` writes that added the forbidden package names all passed the `PostToolUse` L2 hook — the same hook that blocked the original attempt. That is the fix verified end to end, not just at the command line.
>
> One cosmetic defect found and fixed during the run: a `ProjectReference` path repeats the project name in both the directory and the file name, so line numbers were reported as `(lines 41, 41)`. Deduplicated.
>
> **G-B failure messages now name the line**, which they did not before: `must not reference 'MySqlConnector' (line 41)`.

---

### ✅ P1-02 · Hand-author the API on the Web SDK (rung A) 🎯

**Spec:** §6.2 · **Closes:** G-01, G-02 · **Decides:** ADR-001

> **This is the task the whole project is gated on.** Everything after it is ordinary engineering.

**Do:** **Convert** `Merchandising.Api.vbproj` from the plain shell P1-01 left behind to `Sdk="Microsoft.NET.Sdk.Web"`, `OutputType=Exe`. Write `Program.vb` using `Module Program` / `Sub Main` (**not** top-level statements). Add one controller `HealthController` exposing `GET /health` returning `{status, version, utcTime}` — and **nothing** about the database, environment, or configuration.

> **Scope boundary with P1-01 — this card owns the whole Web SDK conversion.**
>
> P1-01 leaves `Merchandising.Api` as a **plain shell on `Sdk="Microsoft.NET.Sdk"`** with references wired and nothing else: no Web SDK, no `Program.vb`, no controller. All three arrive **here**, and the evidence for all three is this card's (`p1-02-project-file.txt`, `p1-02-health-response.txt`).
>
> This matters because P1-02 is not ordinary work — it is the rung A proof that resolves ADR-001 and retires the project's dominant risk. If the SDK swap has already happened quietly at P1-01, there is no observation left to make here and the ADR gets resolved by inference rather than by evidence. **If you arrive at this card and the Web SDK is already set, that is a P1-01 scope violation: record it, and re-run the conversion's acceptance checks from a clean build before claiming the proof.**

> **`Directory.Build.targets` has already fired by the time you reach this card.** Its guardrail target is conditioned on `MSBuildProjectName == 'Merchandising.Api'`, and **P1-01 creates that project and builds it** — so P1-01's `dotnet build`, not this card's, is the target's first real exercise. The file's own header comment previously said P1-02; that was wrong and is corrected. Nothing here depends on it, but do not record P1-02 as the target's first run.

**Done when:**

- [x] `.vbproj` uses the Web SDK; no C# files anywhere in the project — `UsingMicrosoftNETSdkWeb=true` evaluated from the resolved SDK, not read off the `Sdk` attribute. **0** `.cs`/`.csproj`/`.cshtml`/`.razor` under the project with `bin/` and `obj/` deliberately included in the scan
- [x] `dotnet run` starts Kestrel — `Now listening on: http://127.0.0.1:5199`, `stderr` empty
- [x] `GET /health` returns 200 with the expected JSON shape — `{"status":"ok","version":"0.1.0","utcTime":"2026-08-16T09:37:27.8769798Z"}`, exactly three fields
- [x] Health response contains no secrets, DB status detail, or environment strings — body **and all four response headers** checked
- [x] `EnableRequestDelegateGenerator=false` inherited from `Directory.Build.props` — proven in two halves: evaluated `false` against the project, **and** shown not to be set locally
- [x] ADR-001 recorded with the rung reached and **any** friction encountered — **ACCEPTED, rung A**, friction audit on all four triggers

**Evidence:** `p1-02-health-response.txt`, `p1-02-project-file.txt`

**Blocker protocol:** if the Web SDK fails on a C#-assuming target, attempt rung B **once**, record the exact error, log it in ADR-001. **Do not spend more than one session fighting rung A.**

> **Result: rung A confirmed in-repo. ADR-001 ACCEPTED. The project's dominant risk is retired.**
>
> **No P1-01 scope violation to record** — checked before any edit, as the card requires. The shell was genuinely still on `Sdk="Microsoft.NET.Sdk"` with no `Program.vb` and no controller, so the conversion was observed happening here.
>
> Three changes, nothing else: SDK swapped, `OutputType=Exe` added, two `.vb` files written. `RootNamespace`, `$(MerchNetTfm)` and all three project references untouched. **The finished project file sets exactly three properties** — everything else comes from the Web SDK or `Directory.Build.props`. That brevity *is* the result: a hand-authored VB project needed no compensating property anywhere.
>
> Build clean at **0 warnings** twice: the API alone, and all eleven projects with `--no-incremental`. `Merchandising.Tests.Integration` builds against the API now that it is an `Exe`, so P1-19's `WebApplicationFactory` seam is not blocked. The `MerchGuardrails` target still fires inline, unaffected by the SDK swap.
>
> **Two risks closed by observation rather than assumption.** A **VB anonymous type** (`VB$AnonymousType_0`) serialised correctly through reflection-based `System.Text.Json` — the required path, since source generation is C#-only. And the Web SDK generated **zero C#** for a VB project: two intermediates in `obj/`, both `.vb`. G-A will not fight the build.
>
> **The inheritance box was the one worth getting right.** Restating `EnableRequestDelegateGenerator` in the `.vbproj` would have ticked it while proving the opposite of what it asks. It is deliberately absent, and the only occurrence of the name in the file is a comment saying why.
>
> **One build failed, and it was not rung A.** First attempt died on `MSB4025: An XML comment cannot contain '--'` — hyphen rules used as separators in the new file's comment block. It failed at XML parse time, before SDK resolution or compilation, and would have failed identically on the plain SDK. Counting it as friction would have triggered P1-02a against a fallback there is no reason to need, on the strength of a typo. **It is the third occurrence of that defect** (`Directory.Build.props` at P1-01, twice more while fixing it) — the P1-01 warning lived only in the file already bitten, so it did not stop a fresh instance in a new file. The note now sits in `Merchandising.Api.vbproj` too.
>
> **Deliberately not created:** `appsettings.json` and `Properties/launchSettings.json`. Config comes from an ACL-protected host location at P1-05 and Kestrel endpoints are P1-09, so `ASPNETCORE_URLS` supplied the address and no development URL entered the repository.
>
> **No failing-test-first step, stated rather than skipped quietly.** `/health` carries no business rule, and the API test seam (`WebApplicationFactory`, `InternalsVisibleTo`) is P1-19's deliverable. The proof here is the captured live HTTP call, which is what the card asks for. The two green tests in the run are P1-01's scaffold assertions and are **not** a test of this endpoint.

---

### ✅ P1-02a · Rung B insurance spike — **SKIPPED, trigger condition not met**

> **Closed at P1-02, not done — the distinction matters.** This card is conditional, and its condition did not fire. Rung A showed **zero friction** on all four triggers, so the spike was never run and rung B was never attempted.
>
> **Its two `Done when` boxes are left unticked on purpose.** They describe outcomes of *running* the spike (`/health` responds under rung B, or the failure mode is recorded). Ticking them would claim a result that does not exist. A conditional card whose condition was not met is closed by the condition, not by evidence.
>
> **Friction audit that closed it** — recorded in `evidence/phase-1/p1-02-project-file.txt` §7 and in ADR-001, checked one trigger at a time rather than concluded from the clean build:
>
> | Trigger | Result |
> |---|---|
> | An SDK target needing a workaround | None. No target overridden, no `Import` added, no property routing around a C#-assuming target |
> | A warning that had to be suppressed | None. 0 warnings on both builds; no `NoWarn` exists anywhere in the repo |
> | A property beyond the standard | None. The file sets exactly three: `OutputType`, `RootNamespace`, `TargetFramework` |
> | Any difference from the P0-07 probe | None attributable to rung A |
>
> The `MSB4025` XML-comment failure on P1-02's first build is **not** friction and did not trigger this card: it failed at XML parse time, before SDK resolution, and would have failed identically on the plain SDK. See ADR-001.
>
> **Phase 1 gate implication:** the "Partial — descended to rung B" branch does **not** apply. Nothing here needs telling the professor.
>
> **If a future SDK update breaks rung A, this card comes back.** Rung B is still the next step and PA-001 grants no C# escape hatch — ADR-000 records rung C as effectively closed.

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

### ✅ P1-02b · `Directory.Build.targets` still called itself dead code

**Closes:** a stale claim found at P1-02 · **Touches:** `Directory.Build.targets`, `plan.md` §2/§3, this file, `evidence/phase-0/PHASE-0-READINESS.md`

**Documentation only. No behaviour changed, no script edited, no evidence file created** — the measurements below are recorded in the target's own comment, which is where a reader needs them.

**Why.** The file's header block read *"THIS TARGET IS DEAD CODE. IT HAS NEVER RUN"*. True when written at P0-07, when `src/` held only `.gitkeep`. False from P1-01 onwards, which created `Merchandising.Api` and built it — the guardrail lines have been appearing inline in every build since. The header survived P1-01 and P1-01a untouched and was caught at P1-02.

**Why it was worth a commit rather than a shrug.** The claim is not inert. P0-07 used it to argue that the git pre-commit hook is the only real protection: *"do not read this file as evidence that guardrails run during a build — they do not, because no build happens."* **That conclusion is still correct and the reason for it is now different and weaker** — which is exactly the kind of drift that gets a load-bearing hook deleted by someone who reads the old reason and finds it no longer applies.

**Measured before writing any of it down** (`Directory.Build.targets` records the same table):

| Invocation | Guardrails |
|---|---|
| `dotnet build`, full solution | run |
| `dotnet build`, nothing changed since last build | **run** — the target declares no `Inputs`/`Outputs`, so MSBuild never skips it as up to date |
| `dotnet build -p:MerchSkipGuardrails=true` | skipped, as designed |

**The corrected reasoning, now in the file:** it is a **safety net, not a gate**. It fires only when a *build* fires, so a Visual Studio edit committed without a rebuild never reaches it, and `MerchSkipGuardrails=true` bypasses it outright. **The git pre-commit hook remains the gate**, for a reason that survives the correction.

**Done when:**

- [x] The header states the real status, with the three measured cases
- [x] The superseded reasoning is spelled out rather than silently replaced — old reason and real reason side by side
- [x] `plan.md` §2 tree and §3 item 3 corrected — both said "inactive until P1-01"
- [x] `PHASE-0-READINESS.md` §6 item 4 **appended to, not rewritten** — it is evidence, and evidence records a moment
- [x] `dotnet build` and `run-tests.ps1` still green afterwards

> **Left alone deliberately:** `docs/claude-code-phase0-close-prompt.md` §B5 also says the target is dead code and activates at P1-02. It is a spent session prompt, not a live document — a record of what a past session was asked to do. Correcting it would falsify that record for no reader's benefit.

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
- [ ] **Integration test:** an over-scale value (money with >4 dp, quantity with >3 dp) is either **rejected** by the API or **explicitly rounded** by it before the parameter is bound — asserted at the API boundary, not by reading the stored value back. Per ADR-004.1, `STRICT_TRANS_TABLES` rounds over-scale decimals silently (`Note 1265`), so a correctly-scaled stored value proves nothing on its own.
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
- [ ] **Integration test:** a decrement whose computed value exceeds storage scale (quantity >3 dp, or any money value >4 dp on the same command) is **rejected** or **explicitly rounded by the API** before insert, per ADR-004.1's half-up policy. Assert on the API's own behaviour — the server rounds silently and would report success either way.

**Evidence:** `p1-11-happy-path.txt`, `p1-11-insufficient-stock.txt`, `p1-11-decimal-scale.txt`

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

**Spec:** §7, §19 · **Consumes:** ADR-009 (ACCEPTED at P1-01 — **this card no longer decides it**)

> **ADR-009 moved to P1-01 and is ACCEPTED: MSTest, package `MSTest` 4.0.2.** P1-01 creates both test projects and `dotnet new` cannot create one without naming a framework, so the decision could not wait for this card. All three candidates were measured on this machine first — see ADR-009.1; none was a risk, so nothing here is a compromise forced by the earlier date.
>
> **Do not re-open the choice.** Switching frameworks at this point would rewrite every test written between P1-01 and here for no stated defect. If MSTest turns out to be genuinely unworkable for something this card needs, that is stop condition 5 in `CLAUDE.md` §7 — report it, do not swap silently.
>
> The projects themselves already exist. **This card wires them up**: `InternalsVisibleTo`, the `WebApplicationFactory` seam, the real-MariaDB fixtures, and the migration of P1-12/P1-13/P1-14 from manual proofs to automated tests.

**Do:** Both test projects in Visual Basic. `InternalsVisibleTo` configured. Integration tests run against the **real** pinned MariaDB — never an in-memory substitute.

**Done when:**

- [ ] `run-tests.ps1` executes both suites green
- [ ] P1-12, P1-13, P1-14 run as **automated tests**, not manual steps
- [ ] All test source is VB (G-A passes)
- [ ] `WebApplicationFactory` can reach `Program`
- [x] ADR-009 records the framework — **already ACCEPTED at P1-01.** This card consumes it.
- [ ] Integration project still carries `<Assembly: DoNotParallelize>` (ADR-009.2) — MSTest's template default is method-level parallelism, which would run these tests concurrently against the one shared MariaDB instance

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
