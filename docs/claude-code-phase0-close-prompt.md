# Prompt for Claude Code — close out everything before Phase 1

Paste everything below the line into Claude Code in
`C:\Users\Admin\Documents\Hardware_Merchandising_System`.

---

Read `CLAUDE.md`, `plan.md`, `tasks.md`, `docs/adr.md`, `docs/environment-manifest.md`, and `evidence/phase-0/p0-08-guardrail-proofs.txt` before doing anything.

Your job: do **everything that can be done without a second physical machine**, so that when the client laptop arrives and Phase 1 starts, nothing is broken, stale, or unverified. Then produce one readiness report.

**Hard rules for this session:**

- **Do not create any project under `src/`.** No `.vbproj`, no `.sln`, no application code. P1-01 and P1-02 are Phase 1 tasks and belong to their own task cards with their own evidence. Part D below is the *only* exception and it happens entirely outside the repository.
- Do not mark any `Done when` box true unless you actually executed the check and can paste the output.
- If something cannot be verified, say so explicitly and put it in the "left for Max" list. A recorded gap is a good outcome. A green checkmark you did not earn is not.
- Report real output, never a summary of a run you did not do.

Work through the parts in order. Part A gates everything else.

---

## PART A — Verify the Claude Code hook wiring (do this first)

Last session created `.claude/settings.json` and the two skills, but they were not live because Claude Code loads them at session start. Section 6.1 of `p0-08-guardrail-proofs.txt` lists four deferred checks. Run them now.

**A1. Confirm the hooks are actually registered.** Run `/hooks` (or whatever the current command is to list active hooks) and paste the output. If the three layers are not listed, stop and diagnose before continuing.

**A2. The critical test — PreToolUse must block.**
Attempt to write `src/Merchandising.Domain/Scratch.cs`.

- **Expected:** blocked, exit 2, with the guardrail message.
- **If the write SUCCEEDS**, the hook is silently broken. This is the failure mode I most need caught. The likely cause: `.claude/settings.json` uses `"$CLAUDE_PROJECT_DIR/.claude/hooks/..."`, which is bash syntax. Your own environment scan found **bash is not on PATH** (Git Bash exists only at `C:\Program Files\Git\bin\bash.exe`). If Claude Code invokes hook commands through PowerShell instead of `sh`, `$CLAUDE_PROJECT_DIR` expands to nothing, the `-File` path becomes invalid, and the script exits 1 — and **exit 1 does not block, it silently allows**.
  - Fix by switching the three commands to a repo-relative path (`.claude/hooks/block-csharp.ps1`), since hooks run with the working directory set to the project root. If that also fails, hard-code the absolute path and record why in a comment.
  - Re-test until it genuinely blocks. Delete `Scratch.cs` if it ever landed, and confirm with `Test-Path`.

**A3.** Write `src/Merchandising.Domain/Scratch.vb` → must be **allowed**. Delete it afterwards.

**A4.** Run `/task P0-04`. Confirm it loads the card, cites the spec section, and **stops to restate scope** instead of charging ahead.

**A5.** Run `/phase-gate`. Confirm it reports Phase 0 as incomplete and names the specific missing artifacts.

**A6.** Trigger a real Stop-hook run and record the measured duration.

Append all six results to `evidence/phase-0/p0-08-guardrail-proofs.txt` under a new "SECTION 8 — hook wiring verified in a live session" heading, and tick the four boxes in section 6.1.

---

## PART B — Correctness fixes

**B1. Pin the MariaDB 10.4 realities into `docs/adr.md` ADR-003 and mark it ACCEPTED.**

Your P0-04 evidence established MariaDB **10.4.32** from XAMPP 8.2.12-0. Two consequences that will cause failures in Phase 1 if not pinned now:

- **Collation:** the `uca1400` collation family is MariaDB **11.x only**. A migration written with `utf8mb4_uca1400_ai_ci` will fail at apply time on 10.4. Pin `utf8mb4` / `utf8mb4_unicode_ci` / InnoDB explicitly, and state in the ADR that `uca1400` is unavailable so nobody reaches for it later.
- **Dump tool:** this distribution ships **`mysqldump.exe` only** — there is no `mariadb-dump.exe`. Record the full path `C:\xampp\mysql\bin\mysqldump.exe` and its version (10.19 Distrib 10.4.32-MariaDB). Amend any wording in `plan.md`, `tasks.md`, or `docs/` that says "preferably `mariadb-dump`" so it names the tool that actually exists.

Also verify and record: does 10.4.32 default to `innodb_default_row_format=dynamic`? This matters for `utf8mb4` unique indexes on `VARCHAR(255)` columns (SKU, barcode) — confirm the index key length is not going to hit a limit.

**B2. Finish `docs/adr.md` ADR-002.** The XAMPP version (8.2.12-0) and .NET SDK version (10.0.301, host 10.0.9) are known from your own evidence but the rows are still marked pending. Fill them in. Leave only the MySqlConnector row pending — that legitimately belongs to P1-05.

**B3. Add the version pins to `CLAUDE.md` §6.** Add a short table so it is in context every session:

```
MariaDB       10.4.32 (XAMPP 8.2.12-0) — utf8mb4_unicode_ci, NOT uca1400 (11.x only)
Dump tool     C:\xampp\mysql\bin\mysqldump.exe — mariadb-dump does not exist here
.NET SDK      10.0.301, runtimes 10.0.9 (AspNetCore, NETCore, WindowsDesktop)
TFMs          net10.0 / net10.0-windows
```

Add one line of instruction: when writing SQL, target MariaDB 10.4 syntax — training data skews toward MySQL 8 and MariaDB 11, and features from those versions will fail here.

**B4. Create `.gitattributes` — this is currently missing and it is a real hazard.**
The git pre-commit hook is a `#!/bin/sh` script; if git ever normalises it to CRLF, the shebang breaks and **the hook silently stops running**. Also pin the other file types so migrations and PowerShell stay consistent:

```
* text=auto
*.sh   text eol=lf
*.sql  text eol=lf
*.ps1  text eol=crlf
*.vb   text eol=crlf
*.vbproj text eol=crlf
*.xaml text eol=crlf
*.png binary
*.pfx binary
```

After creating it, run `git add --renormalize .` and confirm `.git/hooks/pre-commit` still executes by making a test commit.

**B5. Check `Directory.Build.targets`.** The guardrail target is conditioned on `'$(MSBuildProjectName)' == 'Merchandising.Api'`, which does not exist yet — so it is currently dead code. Confirm that is the case, and add a comment saying it activates at P1-02 so nobody assumes it is running today.

---

## PART C — Complete what is completable in Phase 0

**C1. Verify Visual Studio 2026 is installed.** P0-01 is marked ✅ but `dotnet --info` cannot prove this and no evidence covers it. Use `vswhere` (`C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe -all -products * -format json`) to record the edition, full version, and installed workloads. You specifically need `Microsoft.VisualStudio.Workload.ManagedDesktop` (.NET desktop) and `Microsoft.VisualStudio.Workload.NetWeb` (ASP.NET and web). If either is missing, or VS is not installed at all, say so plainly — that is a Max task, not a defect.

**C2. Investigate the static-IP conflict risk.** The host is pinned to `192.168.100.165/24` directly on the Wi-Fi adapter, outside the router's knowledge. If that address sits inside the router's DHCP pool, another device can be handed the same address and you get a conflict — most likely during a demo.

Do what you can from the machine: `arp -a` to see the current neighbour table, `Get-NetIPConfiguration`, and check whether anything else on the subnet already answers at .165 or nearby. You **cannot** read the router's DHCP pool range without its admin UI — record that as a Max task with the specific question to answer ("is 192.168.100.165 inside the DHCP pool on the Huawei router, and if so what is the pool range?").

Also record plainly in the manifest that this configuration is bound to the `HUAWEI-5G-fP2f 2` network and will need redoing on the classroom or store network.

**C3. Pre-stage `MERCH-HOST` on the host machine.** Add the hosts-file entry mapping `192.168.100.165` to `MERCH-HOST` on this machine and verify `ping MERCH-HOST` resolves locally. This does not close P0-05 — that needs resolution *from a client* — but it removes one step from the day the laptop arrives. Write the exact client-side procedure (the line to add, the file path, the elevation requirement) into `docs/installation-guide.md` so it is copy-pasteable later.

**C4. Create and test the backup directory.** The manifest requires "backup directory and off-host destination chosen and writable". Create a protected directory outside the application binaries and outside the repository (suggest `C:\MerchandisingBackups`), restrict its ACL to administrators, and prove it is writable by creating and deleting a test file. Record the path and the ACL in the manifest. The **off-host** destination needs Max to choose a physical drive — leave that one.

**C5. Update `docs/environment-manifest.md`** with everything now known, and tick only the checklist boxes that are genuinely satisfied.

---

## PART D — Phase 1 pre-flight probe (disposable, outside the repo)

This is the single highest-value risk reduction available today, and it must not touch the repository.

In a scratch directory under `%TEMP%` — **not** in the repo, so the guardrails are not involved — prove the rung A approach works before Phase 1 formally starts:

1. Hand-author a `.vbproj` with `Sdk="Microsoft.NET.Sdk.Web"`, `OutputType=Exe`, `TargetFramework=net10.0`, `EnableRequestDelegateGenerator=false`.
2. Write a `Program.vb` using `Module Program` / `Sub Main` — not top-level statements.
3. Add one controller with a `GET /health` endpoint.
4. `dotnet build`, `dotnet run`, and hit the endpoint.
5. `dotnet publish -r win-x64` framework-dependent, then self-contained.
6. Delete the entire scratch directory.

**Record precisely:** whether it worked, every warning, every property you had to add beyond the standard template, and any error text verbatim.

**This does NOT close P1-02.** P1-02 still runs properly in-repo, with its own evidence and ADR-001 entry. The point of this probe is that if rung A is going to fail, I want to know today — not three tasks into Phase 1. It also directly answers whether the conditional P1-02a insurance spike will be needed.

If rung A fails here, try rung B (`Sdk="Microsoft.NET.Sdk"` + `<FrameworkReference Include="Microsoft.AspNetCore.App" />`) in the same scratch directory and record that result too. **Do not spend more than one hour total on Part D.** If both fail, that is a finding, and a valuable one — report it and stop.

---

## PART E — Status hygiene and commit

Update `tasks.md` so every Phase 0 card carries an accurate marker (✅ / 🟡 / 🔴 / ⬜) and blocked cards state exactly what unblocks them. Then commit everything as one commit:

```
P0-07: verify hook wiring, pin MariaDB 10.4 constraints, close completable Phase 0 items
```

---

## PART F — The readiness report (this is what I need back)

Write `evidence/phase-0/PHASE-0-READINESS.md` and also paste its full contents into your final message. Structure:

**1. Verified working** — one line each, with the evidence that proves it.

**2. Fixed this session** — what was stale or wrong, and what it is now.

**3. Part D probe result** — rung A verdict, verbatim errors or warnings, whether P1-02a will be needed. Be blunt.

**4. Left for Max** — every remaining item, each with: what it is, the exact command or action to perform, why you could not do it, and which task card it belongs to. This should be a short, literal to-do list, not prose.

**5. Phase 1 readiness verdict** — one of:
   - **GREEN** — P1-01 and P1-02 can start now; remaining Phase 0 items do not block them.
   - **AMBER** — can start, with named caveats.
   - **RED** — something must be resolved first; name it.

   Justify the verdict. Remember that P1-09, P1-10, P1-15 and one sub-check of P1-04 need the client laptop, but P1-01 through P1-08 and P1-11 through P1-14 do not — so "no client laptop yet" is not by itself a RED.

**6. Anything you disagree with** in `plan.md`, `tasks.md`, or `CLAUDE.md` after this session's evidence. If something in the plan is now wrong, say so — do not quietly work around it.
