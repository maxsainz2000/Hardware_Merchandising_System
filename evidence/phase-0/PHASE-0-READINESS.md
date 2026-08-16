# Phase 0 Readiness Report

**Date:** 2026-08-15
**Machine:** `LAPTOP-3HH6OHHE` (dev machine, also serving as the host laptop)
**Session:** P0-07 close-out — everything achievable without a second physical machine
**Constraint honoured:** no project was created under `src/`. `src/` still contains only `.gitkeep`.

---

## Verdict up front

> ## 🟢 GREEN — start Phase 1 now.
>
> **P1-01 through P1-08 and P1-11 through P1-14 are unblocked.** Every outstanding Phase 0 item is blocked on a client laptop, a screenshot, or a decision only Max can make — none of them is engineering work, and none of them gates the tasks Phase 1 starts with.
>
> The dominant project risk — *can an all-VB ASP.NET Core Web API be built, run and published on .NET 10?* — was tested today outside the repository and **it works, cleanly, first attempt, zero warnings.** Full justification in §5.

---

## 1. Verified working

Each line names the artifact that proves it. Nothing here is asserted from memory.

| # | Verified | Evidence |
|---|---|---|
| 1 | **PreToolUse hook genuinely blocks C#.** A `Write` of `src/Merchandising.Domain/Scratch.cs` was refused, exit 2. The file never landed. | `p0-08-guardrail-proofs.txt` §8 A2 |
| 2 | **It discriminates** — `Scratch.vb` was allowed and then deleted. Not a blanket refusal. | §8 A3 |
| 3 | **PostToolUse hook fires live** on a `.vbproj` edit and sweeps the whole repo. | §8 A7 |
| 4 | **Stop hook fires live** and blocked a turn against a violation planted via PowerShell — the exact Visual-Studio-edit path L1/L2 cannot see. | §8 A6 |
| 5 | **Stop hook cost measured:** mean **779 ms** wall clock over 5 runs (sweep ~213 ms, pwsh startup ~566 ms fixed). Loop protection re-confirmed. | §8 A6 |
| 6 | **`/task` skill loads and enforces restate-and-stop.** Cited spec §6.3 for P0-04 and halted before doing work. | §8 A4 |
| 7 | **`/phase-gate` skill loads and fails correctly** — reported Phase 0 incomplete, named 3 missing artifacts, did **not** regenerate `tasks.md`. | §8 A5 |
| 8 | **Git pre-commit hook still blocks** a `.cs` commit after `.gitattributes` and the LF hardening. | §8, and the B4 test in this session |
| 9 | **Visual Studio Community 2026 `18.7.1+11911.148` installed**, `isComplete`, `isLaunchable`; **both required workloads present** (`ManagedDesktop`, `NetWeb`) via `vswhere -requires`, with two absent workloads returning nothing to prove the query discriminates. | `p0-07-visual-studio-workloads.txt` |
| 10 | **MariaDB 10.4.32** — `uca1400` collations: **0 exist**. `utf8mb4_unicode_ci`: present. Server default is `utf8mb4_general_ci`. | `p0-07-mariadb-10.4-constraints.txt` §1–2 |
| 11 | **`innodb_default_row_format=dynamic`**, 16 KB pages. A real table with two `VARCHAR(255)` utf8mb4 **unique** indexes created without error — 1020 of 3072 bytes used. | §5 |
| 12 | **Decimal precision holds:** `12345678901234.5678` and `0.001` round-tripped exactly in `DECIMAL(19,4)`/`DECIMAL(19,3)` with `DATETIME(6)`. | §5 |
| 13 | **`mysqldump.exe` is the only dump tool** — `Ver 10.19 Distrib 10.4.32-MariaDB`. `mariadb-dump.exe` and `mariadb.exe` do not exist. | §7 |
| 14 | **Backup directory `C:\MerchandisingBackups`** created outside repo and XAMPP, ACL inheritance disabled, `Authenticated Users: Modify` removed, create/read/delete proven. | `p0-07-network-and-backup.txt` C4 |
| 15 | **Rung A works end to end** — builds 0/0, runs, serves `/health` 200, publishes both ways, both published `.exe` files run and serve. | `p0-07-rung-a-preflight.txt` |
| 16 | **The Web SDK generates no C# for a VB project** — 4 `.vb` intermediates in `obj/`, **0 `.cs` files** anywhere. G-A will not fight the build. | `p0-07-rung-a-preflight.txt` §7d |

---

## 2. Fixed this session

### 2.1 Two hooks blocked without saying why — **real defect, fixed**

`stop-guardrails.ps1` and `check-vbproj.ps1` captured the guardrail run with `2>&1`. But `check-no-csharp.ps1` reports through `Write-Host`, which in PowerShell 7 writes to the **information stream (6)** — in neither stdout nor stderr. The capture was always the empty string, so the hooks blocked correctly while relaying **nothing about what failed**.

Observed live, verbatim, with the guardrail body blank:

```
Stop hook feedback: Repository guardrails FAILED at end of turn:


Do not finish the turn on a failing guardrail. ...
```

**Why nothing caught it before:** earlier tests asserted on *exit codes*, which were always right; and the git hook runs the script inline in a shell that owns the console, so `Write-Host` reaches the terminal there and the bug cannot appear. It lived only in the capture-and-relay path, and only a live blocking run exposed it.

**Fixed:** `2>&1` → `*>&1` in both hooks, with a comment at each site. Re-verified — the failing guardrail and offending path are now named.

### 2.2 `.gitattributes` was missing — and the prescribed fix contained a trap

`core.autocrlf=true` with no `.gitattributes` left git guessing line endings. The pre-commit hook is `#!/bin/sh`; a CRLF shebang makes `sh` hunt for `/bin/sh\r`, fail, and **stop running silently** — and it is the only layer that catches Visual Studio edits.

`.gitattributes` created. **But marking `*.ps1` as `eol=crlf` would itself have caused the failure it prevents:** `scripts/install-hooks.ps1` embeds the hook body as a here-string, so once that file is checked out with CRLF the generated shebang ends CRLF too.

`install-hooks.ps1` now normalises the body to LF, writes bytes directly via `WriteAllText`, and **refuses to install** a hook whose shebang ends CRLF. Verified: reinstalled hook has **0 CRLF pairs**, and still blocked a `.cs` commit.

`git add --renormalize .` produced no unexpected churn.

### 2.3 `sql_mode` is not strict — **the most consequential finding of the session**

XAMPP ships `sql_mode=NO_ZERO_IN_DATE,NO_ZERO_DATE,NO_ENGINE_SUBSTITUTION`. **`STRICT_TRANS_TABLES` is absent** — weaker than MariaDB 10.4's own default. Demonstrated on this server:

```
-- as shipped:
INSERT INTO t VALUES ('THIS-SKU-IS-FAR-TOO-LONG', 1.9999);  -- VARCHAR(8), DECIMAL(19,3)
  stored Sku=[THIS-SKU]   stored Qty=[2.000]                 -- reported SUCCESS

-- with STRICT_TRANS_TABLES:
  ERROR 1406 (22001): Data too long for column 'Sku' at row 1
```

A truncated SKU and a rounded quantity, with **no error surfaced to the API**, in a system whose entire value is an accurate stock ledger. The audit log would faithfully record it as a success.

Documented in ADR-003.2; **not yet fixed on the server** — that is a P1-04 + P1-05 action (see §4).

### 2.4 Everything else corrected

| Was | Now |
|---|---|
| ADR-003 `PENDING`, "record baseline" | **ACCEPTED** with 8 measured 10.4 constraints |
| ADR-002 missing XAMPP, SDK, runtimes, VS | Complete; **only** the MySqlConnector row remains (owed by P1-05) |
| `CLAUDE.md` §6 had no version pins | Pins + a MariaDB-10.4-vs-MySQL-8 "do not use" table, in context every session |
| `plan.md` / `tasks.md` said "`mariadb-dump` preferred" | Names `mysqldump.exe`, the tool that exists |
| `Directory.Build.targets` silently dead | Commented: dead until **P1-02**, with the reasoning |
| P0-06 marked ✅ with both boxes unticked | Corrected to 🟡 |
| No `docs/installation-guide.md` | Created with the copy-pasteable `MERCH-HOST` procedure |
| Manifest host section mostly `_(record)_` | Fully populated; change log now has 5 entries |

---

## 3. Part D probe result — blunt

### Rung A works. Cleanly. There is no reason to doubt it.

A disposable project was hand-authored in a scratch directory outside the repository, exercised, and deleted (768 files removed; `src/` untouched throughout).

| Step | Result |
|---|---|
| `.vbproj` on `Sdk="Microsoft.NET.Sdk.Web"`, `OutputType=Exe`, `net10.0` | authored, no template |
| `dotnet build` | **Build succeeded. 0 Warning(s). 0 Error(s).** 7.03 s |
| `dotnet run` → Kestrel | `Now listening on: http://127.0.0.1:5199` |
| `GET /health` | **200**, `application/json`, `{"status":"ok","version":"0.0.1-probe","utcTime":"2026-08-15T11:30:55.7217511Z"}` |
| `dotnet publish -r win-x64 --self-contained false` | EXIT 0 → 7 files, 0.2 MB → **ran, served 200** |
| `dotnet publish -r win-x64 --self-contained true` | EXIT 0 → 341 files, 105.0 MB → **ran, served 200** |
| Scratch directory deleted | ✅ |

**Verbatim errors: there were none.** **Verbatim warnings: there were none.** The build emitted zero warnings and the project contains no `NoWarn`. That is not a summary — it is the whole output.

The single most reassuring log line:

```
info: Microsoft.AspNetCore.Mvc.Infrastructure.ObjectResultExecutor[1]
      Executing OkObjectResult, writing value of type 'VB$AnonymousType_0`3[...]'
```

A **Visual Basic anonymous type** serialised to correct JSON through the reflection-based `System.Text.Json` path — exactly the combination the project is forced into, since source generation is C#-only. Demonstrated, not assumed.

### Will P1-02a be needed? **No — skip it.**

Tested against its own trigger conditions, one at a time:

- **SDK target needing a workaround:** none. No target overridden, no `Import` added, nothing routed around.
- **Warning that had to be suppressed:** none. Zero warnings, no `NoWarn`.
- **Property beyond the standard template:** the obvious suspect was `<EnableRequestDelegateGenerator>false</EnableRequestDelegateGenerator>`. So it was **removed and the project rebuilt from scratch** — still `0 Warning(s), 0 Error(s)`. The RDG only applies to *minimal APIs*; this design uses controllers. The property is **defence in depth, not a workaround.** Keep it set; it is not friction.
- **Bonus:** the SDK generated **0 `.cs` files**.

**Rung B was not attempted, because rung A did not fail.** Attempting it would have been proving a fallback with no reason to exist.

### What this does *not* settle

**This does not close P1-02 or P1-03.** P1-02 runs in-repo, under `Directory.Build.props`, alongside ten sibling projects, and records ADR-001 itself. ADR-001 remains `PENDING` deliberately, with the probe recorded as a pre-flight note. Nothing here touches HTTPS, Windows Service hosting, MariaDB, auth, or the transactional core.

**If P1-02 in-repo behaves differently from this probe, that difference *is* the friction and P1-02a triggers on it.**

---

## 4. Left for Max

A literal to-do list. Each item: what, the exact action, why the agent could not do it, and its card.

### 🔴 Blocking Phase 1 completion (not Phase 1 *start*)

**1. Provision at least one client laptop.**
- **Action:** obtain a Windows x64 machine on the same LAN; record name, edition, build, resolution, scaling; install .NET 10 Desktop Runtime.
- **Why not me:** it is physical hardware.
- **Cards:** P0-02, P0-03 (phpMyAdmin test), P0-05 (`ping MERCH-HOST`), and later P1-09, P1-10, P1-15, one sub-check of P1-04.

**2. Answer the DHCP pool question — the most likely demo-day failure.**
- **Action:** open **`http://192.168.100.1`** (confirmed reachable; port 80 open). Log in. Find the DHCP pool range. Answer: *"Is `192.168.100.165` inside the pool, and what is the range?"* Then **either** move the host static IP outside the pool, **or** shrink the pool to exclude it, **or** convert to a MAC-based reservation.
- **Why not me:** requires router admin credentials.
- **Evidence of the risk:** live neighbours at `.1, .6, .74, .83, .149, .174, .175, .187, .191` — both sides of `.165`, up to `.191`, all randomised MACs (phones cycling a pool). Windows DAD says `Preferred`, so there is no conflict *today*; that is not a guarantee.
- **Card:** P0-05.

**3. Add the `MERCH-HOST` hosts entry on the host.**
- **Action:** in an **elevated** PowerShell:
  ```powershell
  Add-Content -Path "$env:SystemRoot\System32\drivers\etc\hosts" -Value "`n# Merchandising System (P0-05)`n192.168.100.165`tMERCH-HOST"
  ```
  then `ping MERCH-HOST`.
- **Why not me:** needs administrator rights. Self-elevation was **refused by the tooling's permission boundary** — a correct guardrail, not worked around.
- **Note:** does **not** close P0-05, which needs resolution *from a client*. Procedure for clients is in `docs/installation-guide.md` §1.
- **Card:** P0-05.

**4. Attach the professor approval screenshots.**
- **Action:** save the original messages as `evidence/phase-0/pa-001-approval.png` and `pa-002-approval.png`; date PA-001/PA-002 in `docs/professor-approvals.md`.
- **Why not me:** I cannot access the conversation.
- **Card:** P0-06 (marker corrected to 🟡 this session).

**5. Choose the off-host backup destination.**
- **Action:** pick a physical drive (USB/external), confirm writable, record the path in manifest §2.
- **Why not me:** it is a hardware choice, and no second drive is attached.
- **Note:** `C:\MerchandisingBackups` is done — created, ACL-restricted, write-proven. This is the other half.
- **Card:** P1-17.

### 🟡 Do early in Phase 1 — engineering, but flagged now

**6. Add `STRICT_TRANS_TABLES` to `sql_mode`.**
- **Action:** edit `C:\xampp\mysql\bin\my.ini` line 157 to `sql_mode=STRICT_TRANS_TABLES,NO_ZERO_IN_DATE,NO_ZERO_DATE,NO_ENGINE_SUBSTITUTION`, restart MariaDB, log it in manifest §6. Then set it **per connection** at P1-05.
- **Why not me:** it changes server configuration for the whole project and is properly P1-04's decision to make and evidence. Flagged, documented and demonstrated rather than applied unilaterally.
- **Cards:** P1-04, P1-05. See ADR-003.2.

### ⚪ Awareness only — no action required yet

**7. `merchsys_central` database exists on this MariaDB server** and is not part of this project. Origin unknown. Not touched. Decide before P1-04 whether it should be removed or left.

**8. Tailscale is active** (`100.76.155.51/32`) — a second network path that is not the store LAN. P1-09 firewall scoping and P1-10 negative tests must account for it, or those tests prove less than they appear to.

**9. The stray `C:\.git` repository** flagged in P0-07 is still in place. Deleting it is Max's call.

---

## 5. Phase 1 readiness verdict

# 🟢 GREEN

**P1-01 and P1-02 can start now. Nothing outstanding blocks them.**

**Why GREEN and not AMBER.** AMBER would mean "start, with named caveats" — caveats that constrain the work. There are none for the tasks Phase 1 begins with:

- **The dominant risk is retired in all but name.** Rung A was proven today: builds 0/0, runs, serves, publishes both ways, generates no C#. That was the one thing that could have invalidated the plan, and it did not.
- **The guardrails are proven live, not merely written.** All four layers blocked a real violation, and a genuine defect in two of them was found and fixed. Phase 1 starts on enforcement that has been observed working.
- **The versions are pinned from measurement.** ADR-002 is complete but for the connector row P1-05 owns; ADR-003 is ACCEPTED with eight facts queried from the running server.
- **The client laptop is not on the critical path.** P1-01→P1-08 and P1-11→P1-14 — solution skeleton, the API, publishing, database, migrations, schema, auth, and the entire transactional core — need no second machine. That is **12 of 20 Phase 1 tasks**, including every one that carries real design risk.

**Why "no client laptop yet" is not RED.** Only P1-09, P1-10, P1-15 and one sub-check of P1-04 require it. Those are Track C/E tasks that sit well after the foundation work. The laptop needs to exist before Phase 1 *closes*, not before it *starts*.

**The one thing to carry forward, honestly stated:** the `sql_mode` finding is not a blocker but it is not cosmetic either. Until P1-04 and P1-05 fix it, **the database is silently truncating and rounding**, and any Phase 1 data written before then is suspect. Do P1-04's `sql_mode` change before P1-07 loads any data worth trusting.

Phase 0's own gate is still **FAIL** (2 of 4 criteria unmet) — and that is correct and unembarrassing. Both unmet criteria are blocked on a laptop and two screenshots. **Phase 0's gate does not gate Phase 1**, and treating it as though it did would stall the project on a photograph.

---

## 6. Where I disagree with the plan

Six things this session's evidence puts at odds with the documents. Stated rather than worked around.

**1. `tasks.md` assigns ADR-003 to P1-04, but I marked it ACCEPTED now.**
The close-out prompt directed this, and the values are now *measured* rather than guessed — leaving it PENDING would misrepresent what is known. P1-04's card is amended to **consume** ADR-003 rather than decide it, and gains a concrete action (`sql_mode`) instead. Flagging it because it is a deliberate divergence from the card, not an oversight.

**2. The spec still says the backup should "preferably" use `mariadb-dump`.**
Spec §15 (line 336) reads "preferably `mariadb-dump` when available in the installed distribution". That conditional is *technically* satisfied — it is not available, so `mysqldump` is used. **I did not edit the spec**, since it outranks everything and its wording is defensible. But it is the last document that could send someone looking for a binary that does not exist. Worth a one-line amendment at your discretion.

**3. `plan.md` §2's repository layout omits `.gitattributes`.**
It lists `.gitignore` and `.editorconfig` but not `.gitattributes` — which is why it was missing, and which nearly cost the git hook silently. It now exists. **`plan.md` §2 should list it**, so a clean clone is checked against a complete inventory.

**4. `plan.md` §3 says the guardrails are "wired into `Directory.Build.props` as a pre-build target".**
They are wired into `Directory.Build.targets`, and that target has **never run** — it is conditioned on `Merchandising.Api`, which does not exist until P1-02. The claim reads as an active protection today; it is not one. The file is now commented to say so, but §3's wording overstates the position.

> **SUPERSEDED 2026-08-16 at P1-02b — appended, not edited.** Both halves of this item are now resolved, and one of them was wrong in a detail worth naming.
>
> - **"Never run" is no longer true.** The target went live at **P1-01**, not P1-02 as written above: the condition keys on the project *name*, and P1-01 created the project and built it. It has since been measured running on a full build, on a build where nothing changed, and being correctly bypassed by `MerchSkipGuardrails=true`.
> - **`plan.md` §3's wording was corrected** to name `Directory.Build.targets`, and now states the current status plus the limit that matters: it fires only when a build fires, so it is a safety net and not a gate. The git pre-commit hook remains the gate.
>
> The underlying observation this item made was still the right one. A file that describes itself as protection, but is not running, is worse than no file — and the reverse held too: once it started running, the stale "DEAD CODE" header would have told a reader the opposite of the truth for the rest of the project. It went uncorrected through P1-01 and P1-01a and was caught at P1-02.

**5. P0-08's card assumed script verification was equivalent to wiring verification.**
The previous session verified the hook scripts thoroughly — 14 of 14 cases — and still shipped a defect that made two of the three layers report failures blank. The lesson generalises: **testing a hook's exit code is not testing the hook.** Worth carrying into how P1-19's test harness is judged: assert on what the consumer actually receives, not just on the return value.

**6. Phase 0's exit gate cannot pass without hardware, but Phase 1 does not need that hardware.**
The plan gates on "manifest complete for **every** client" while the first twelve Phase 1 tasks need no client at all. As written, a strict reading stalls the project. I recommend `plan.md` §5 explicitly state that **Phase 0's client-dependent criteria may be carried into Phase 1** and must be closed before the *Phase 1* gate — which is where they genuinely bite (P1-09, P1-10, P1-15). Sequencing by dependency is the plan's own stated principle; this is the one place it is not applied.

---

## 7. Artifacts produced this session

| File | Size | Contents |
|---|---|---|
| `p0-08-guardrail-proofs.txt` §8 | +17 KB | Live hook wiring: A1–A7, the defect, the fix, timings |
| `p0-07-mariadb-10.4-constraints.txt` | 5.9 KB | 8 measured 10.4 facts; strict-mode demonstration |
| `p0-07-visual-studio-workloads.txt` | 3.1 KB | `vswhere` instances and workload queries |
| `p0-07-network-and-backup.txt` | 8.4 KB | DHCP conflict analysis, hosts blocker, backup ACL |
| `p0-07-rung-a-preflight.txt` | 12.6 KB | Full rung A probe: sources, build, run, publish, friction audit |
| `PHASE-0-READINESS.md` | this file | The report |

Also changed: `docs/adr.md`, `docs/environment-manifest.md`, `docs/installation-guide.md` (new), `CLAUDE.md`, `tasks.md`, `plan.md`, `.gitattributes` (new), `Directory.Build.targets`, `scripts/install-hooks.ps1`, `.claude/hooks/stop-guardrails.ps1`, `.claude/hooks/check-vbproj.ps1`.

**Repository state at close:** guardrails green (G-A…G-D all pass), `src/` contains only `.gitkeep`, no `.cs` or `.csproj` anywhere, scratch probe fully deleted.
