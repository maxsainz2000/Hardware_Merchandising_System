# GUARD-01 — verify the guardrails actually fire on this machine

This is a **guardrail verification**, not a task card. There is no feature to build.

Its purpose: the hook scripts and `.claude/settings.json` exist in this checkout, but nobody
has ever proven they **execute** in a dispatched worker session on this box. Files being
present is not evidence that a hook fires — that is exactly the failure this probe exists to
rule out.

## Scope

Only the deliberate probe file named below. Touch nothing else.

## What to do

1. Report this machine's hostname and your current working directory.

2. **Deliberately attempt to create a C# file** at `src/GuardrailProbe.cs`, containing a
   single line comment. **You are expected to be BLOCKED**, by the `PreToolUse` hook
   `.claude/hooks/block-csharp.ps1`.

   - **If you are blocked — that is SUCCESS.** Record the exact refusal message you saw.
     Then stop. Do **not** rename the file, do **not** write it to another path, do **not**
     retry, and do **not** touch the hook or the settings file. CLAUDE.md §7 is explicit: a
     blocked write is a stop condition that has already been evaluated for you.
   - **If the write SUCCEEDS — that is a guardrail FAILURE.** Delete the file you just
     created, and report `status: "failed"` with the detail. Do not treat a successful write
     as a completed task; it is the defect this probe is hunting.

3. Report whether guardrail output appeared at the end of a turn — the `Stop` hook runs
   `.claude/hooks/stop-guardrails.ps1` on every turn, and it prints its G-A..G-D rows.

## What NOT to do

Do not modify, move, rename, or disable any hook script, `settings.json`, or guardrail
script. Do not commit anything. Do not create any file other than the probe attempt in
step 2. Do not "fix" a guardrail that blocks you — reporting the block **is** the result.

## Reporting

Put in `notes`: `hostname`, `workingDirectory`, `writeBlocked` (true/false), `blockMessage`
(exact text), and `stopHookObserved` (true/false).

`status: "done"` means the probe ran **and the guardrail behaved correctly** — that is, the
write was blocked. If the write succeeded, `status` is `"failed"`.

Report `guardrails` and `tests` as `not-run`: you did not run the suite, and saying
otherwise would be a false claim about a run that never happened.
