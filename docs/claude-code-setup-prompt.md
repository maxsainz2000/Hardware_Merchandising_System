# Prompt for Claude Code — install skills, hooks, and git guardrails

Paste everything below the line into Claude Code running in
`C:\Users\Admin\Documents\Hardware_Merchandising_System`.

---

Read `CLAUDE.md`, `plan.md`, `tasks.md`, and `scripts/check-no-csharp.ps1` first. You are setting up automation for this repo. Do not write any application source code — the project is in Phase 0 and application code is forbidden until Phase 1.

There are four deliverables. Build them in order and **test each one before moving on**.

## Environment detection first

Before writing anything, determine and report:

1. Is Git Bash available? (`where bash` / does `C:\Program Files\Git\bin\bash.exe` exist)
2. Is `jq` on PATH? (`where jq`)
3. What is `$env:CLAUDE_PROJECT_DIR` inside a hook, and does the repo have `.git` yet?
4. PowerShell version (`$PSVersionTable.PSVersion`) and whether `pwsh` or only `powershell` exists.

**Design constraint: do not depend on `jq`.** It is a Unix tool and is very likely absent on this Windows machine. All hook scripts must be PowerShell, parse the stdin JSON with `ConvertFrom-Json`, and be invoked explicitly via `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ...` so they work whether or not Git Bash is present. If you find `pwsh`, prefer it, but fall back to `powershell.exe`.

---

## Deliverable 1 — Skill: `/task`

Create `.claude/skills/task/SKILL.md`.

Frontmatter:

```yaml
---
name: task
description: Start or continue a numbered task from tasks.md in the Merchandising System. Use whenever the user names a task ID like P0-04, P1-02, or P4-07, or says "next task", "continue the task", or "start the next card".
argument-hint: "[task-id]"
arguments: [taskId]
allowed-tools: Read Grep Glob Edit Write Bash(git *) Bash(dotnet *) Bash(powershell *) Bash(pwsh *)
---
```

The body must encode the per-task working loop from `plan.md` §8.1 and the card format from §8.2. Specifically, the loop is:

1. Read the task card for `$0` in `tasks.md`. If `$0` is empty, find the first card that is not `✅` and confirm with the user before starting.
2. Read the spec sections the card cites, from `documentations/Merchandising System for a Mid-Scale Hardware Store.md`.
3. Read any ADR entries the card says it decides, in `docs/adr.md`.
4. **Restate the scope and the acceptance criteria in your own words, and stop for confirmation before writing code.** This step is not optional — it catches misreadings while they are still cheap.
5. For server-side business rules, write the failing test first.
6. Implement in order: API → Domain/Infrastructure → client last.
7. Run `scripts/check-no-csharp.ps1`, then `scripts/run-tests.ps1`.
8. Capture the evidence artifact to the path named on the card, if it requires one.
9. Tick the `Done when` boxes in `tasks.md` — only the ones genuinely satisfied.
10. Resolve any ADR entry the card decides, moving it from PENDING to ACCEPTED.
11. Commit with the task ID as the message prefix: `P1-02: hand-author VB Web SDK API`.

Also include in the body:

- The eight stop conditions from `CLAUDE.md` §7, restated. Reporting a blocker is a successful outcome; guessing is not.
- Never tick a `Done when` box on a failing test, partial implementation, or unresolved error. If blocked, leave the card open and say why.
- A reminder that Phase 1 has a scope discipline rule: one WPF window, two buttons, one product, one protected endpoint. Building a real feature during Phase 1 is a defect.

Keep the body tight. It loads into context on every invocation.

## Deliverable 2 — Skill: `/phase-gate`

Create `.claude/skills/phase-gate/SKILL.md`.

Frontmatter: `name: phase-gate`, a description covering "check whether the current phase is complete", "phase exit", "regenerate tasks.md for the next phase", `allowed-tools: Read Grep Glob Write Edit`.

Body: the phase exit procedure from `plan.md` §9 and the relevant phase section —

1. List every exit criterion for the current phase from `plan.md`.
2. For each, locate the evidence artifact under `evidence/phase-N/`. Report any criterion with no artifact.
3. Verify no ADR entry that the phase was supposed to resolve is still `PENDING`.
4. Verify a clean-clone build succeeds.
5. Report a clear PASS / PARTIAL / FAIL verdict. **Do not declare PASS if any artifact is missing.** "Verified informally" is not evidence.
6. On PASS only: regenerate `tasks.md` for the next phase from that phase's section in `plan.md`, using the card format in §8.2.

## Deliverable 3 — Claude Code hooks

Create `.claude/settings.json` (this file **is** committed — do not use `settings.local.json`, the guardrails must apply to everyone including future you).

Wire **three layers**, deliberately not one. Do not simply run the full guardrail script on every edit — spawning PowerShell and rescanning the tree on every `.vb` write is slow enough that you will be tempted to disable it, and a disabled guardrail is worse than none.

**Layer 1 — `PreToolUse` on `Write|Edit`: prevention.**
Script `.claude/hooks/block-csharp.ps1`. Reads stdin JSON, extracts `.tool_input.file_path`, normalizes backslashes. If the path ends in `.cs`, `.csproj`, `.cshtml`, or `.razor`, **block it**: write a clear reason to stderr and `exit 2`. The reason should say the project is Visual Basic only by binding course requirement, cite `CLAUDE.md` §2, and tell Claude to report a blocker rather than work around it. This must be fast — a pure path check, no tree scan, no build.

**Layer 2 — `PostToolUse` on `Write|Edit`: targeted detection.**
Only run the full `scripts/check-no-csharp.ps1` when the edited file is a `.vbproj`. Project file edits are what break guardrails G-B (client referencing Infrastructure) and G-D (Option Strict overridden), and they are rare enough that a full scan is cheap. Skip the scan for `.vb`, `.xaml`, `.md`, and anything under `bin/`, `obj/`, `evidence/`, or `documentations/`.

**Layer 3 — `Stop`: full sweep.**
Run `scripts/check-no-csharp.ps1` once when the turn ends. This catches everything the targeted layers miss, at a cost of one run per turn rather than one per edit.

Report the measured duration of the Stop hook. If it exceeds roughly two seconds, say so and propose a fix rather than leaving it slow.

## Deliverable 4 — Git pre-commit hook

Run `scripts/install-hooks.ps1` (it exists already — read it first, and fix it if the environment detection is wrong for this machine, e.g. if Git Bash is absent so the `sh` shebang will not work).

**Keep this hook. It is not redundant with the Claude Code hooks and must not be replaced by them.** Claude Code hooks fire only when *Claude* edits a file. You will also be editing in Visual Studio 2026 — the WPF designer, the project properties pages — and every one of those edits bypasses Claude Code entirely. The git hook is the only thing that catches those. The two mechanisms cover different holes.

---

## Testing — run every case and report the actual output

Do not report success without executing these.

**Hook layer 1 (must block):**

1. Attempt to write `src/Merchandising.Domain/Scratch.cs`. → Must be blocked; file must not exist afterwards. Confirm with `Test-Path`.
2. Attempt to write `src/Merchandising.Api/Foo.csproj`. → Must be blocked.
3. Write `src/Merchandising.Domain/Scratch.vb`. → Must be **allowed**. Delete it afterwards.
4. Write `documentations/notes.md` containing the literal text `.cs`. → Must be **allowed** (this is a path check, not a content check). Delete afterwards.

**Guardrail script (must fail correctly) — this is task P0-08:**

5. Create a client `.vbproj` with a `Merchandising.Infrastructure` ProjectReference → G-B must fail. Revert.
6. Put `Server=localhost;Database=merch;Uid=root;` in a client `.vb` file → G-C must fail. Revert.
7. Add `<OptionStrict>Off</OptionStrict>` to any `.vbproj` → G-D must fail. Revert.
8. Add `<PublishAot>true</PublishAot>` to any `.vbproj` → G-D must fail. Revert.
9. Confirm the script **passes** on the clean repo.
10. Confirm the script ignores files under `bin/`, `obj/`, and `.vs/`.

The scaffold's `src/` tree may be empty at this point. If so, create the minimum stub `.vbproj` files needed to execute tests 5–8 honestly, then remove them. **Do not skip a test because the tree is empty and do not simulate a result** — an untested guardrail is the thing this whole exercise exists to avoid.

**Git hook:**

11. `git add` a `.cs` file and attempt `git commit`. → Must be blocked with a readable message.
12. Confirm `git commit --no-verify` still works as the deliberate escape hatch.

**Skills:**

13. Run `/task P0-04` and confirm it reads the right card, cites the right spec section, and **stops to restate scope** rather than charging ahead.
14. Run `/phase-gate` and confirm it correctly reports Phase 0 as incomplete with specific missing items.

Paste all real output into `evidence/phase-0/p0-08-guardrail-proofs.txt`.

## Finally — update CLAUDE.md

Edit `CLAUDE.md`:

- **Slim §8 (Working rhythm)** down to a short pointer: the loop now lives in the `/task` skill; invoke it with a task ID. Keep the one-task-one-commit rule and the commit message format inline, since those apply even outside the skill.
- **Add a new section, "Automated enforcement,"** listing the three hook layers and the git hook, what each catches, and — importantly — **what each does *not* catch**. State plainly that Claude Code hooks do not fire for edits made in Visual Studio, which is why the git hook exists.
- Add a line under §7 (Stop conditions): if a hook blocks you, that is the answer. Do not attempt to work around it, rename the file, or disable the hook. Report the blocker.
- Note that `.claude/settings.json` is committed and `.claude/settings.local.json` is gitignored.

Also add `.claude/settings.local.json` to `.gitignore`.

Then commit everything as one commit: `P0-07: add task skills, guardrail hooks, and git pre-commit enforcement`.

Report back: which environment detections came out which way, every test result with real output, the Stop hook duration, and anything you had to change from these instructions and why.
