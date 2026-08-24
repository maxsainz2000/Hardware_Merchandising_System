# Fleet health check — run this in a FRESH session

**This checklist is implemented.** Run it with:

```powershell
claude --model opus --effort high      # a FRESH session, at the repo root
/health-check
```

or, for the mechanical half alone, without a session at all:

```powershell
pwsh ./scripts/fleet/health-check.ps1
```

`high`, not `xhigh`: this is verification, not orchestration, and it must not be the session
that then goes on to run a mission. Run it when something feels wrong, after any edit to the
fleet scripts or skills, and before the first real mission of a phase.

**Why it is a script and not this prose.** Fourteen manual steps get executed unevenly. A
step gets skipped, a step gets *read* instead of run, and the table still says `PASS` on the
line nobody exercised — which is precisely the failure this document opens by warning about.
Everything below that a script can decide, `scripts/fleet/health-check.ps1` decides, by
running the command and keeping its output as the evidence. The items are numbered to match
this file exactly, so the two read side by side. Items numbered `X*` are additions the
script makes beyond this checklist and are labelled as such.

Setup and teardown that the items below describe by hand — creating and deleting the damaged
run directory in item 2, stopping the probe in item 5 — the script does itself, in a
`finally`, so a failed check cannot leave the fleet dirtier than it found it.

**What stays a reading, not a match:** item 9's judgement about whether a sentence *tells a
model to economise*, and item 6's judgement about whether a model's wording constitutes a
refusal. The script does the exhaustive search and hands both to you as `REVIEW`. `REVIEW`
is not a result — resolve each one to `PASS` or `FAIL` in your report.

---

You are auditing a multi-machine Claude Code orchestration setup. **Your job is to find what
is broken, not to confirm it is fine.** A clean bill of health that misses a real fault is
the worst outcome available to you; reporting a fault is a success.

Ground rules, and they matter more than the checklist:

- **Verify by executing, never by reading.** A file containing the right words is not
  evidence that anything runs. Where a check can be run, run it and paste the real output.
- **Where you cannot execute, say so explicitly** and mark the item `UNVERIFIED`, not `PASS`.
  `UNVERIFIED` is an honest result. A `PASS` you did not earn is a lie in a report someone
  will act on.
- **Do not fix anything.** This is a diagnosis. If you find a fault, describe it precisely
  and stop; the repair is a separate decision.
- **Do not dispatch any worker except the one step that explicitly calls for it.**

Work through these and produce a table at the end: item, `PASS` / `FAIL` / `UNVERIFIED`, and
one line of evidence each.

### A. The fleet answers at all

1. `pwsh ./scripts/fleet/fleet.ps1 -Action doctor` — every enabled machine reports OK, and
   each shows a `workers : N live / M ceiling` row.
2. `pwsh ./scripts/fleet/fleet.ps1 -Action list` — runs a clean listing. Then confirm it
   survives a damaged run: create a directory under `.claude/fleet/runs/` containing a
   `handle.json` of `{"taskId":"BROKEN"}` and run `-Action list` again. It must still list
   every run. (One stale directory once broke `list`, `report` and `stop` for all runs.)
   Delete the directory afterwards.
3. `pwsh ./scripts/fleet/fleet.ps1 -Action sync` — reports OK. Then confirm parity for real:
   `git rev-parse HEAD` here must equal
   `ssh box3 "git -C C:/dev/Hardware_Merchandising_System rev-parse HEAD"`.

### B. The invariants that are supposed to be walls

4. Read `scripts/fleet/worker-deny.ps1`. Confirm both runners actually load it —
   `grep -n "worker-deny" scripts/fleet/run-worker.ps1 scripts/fleet/run-worker-tty.ps1` —
   and that each passes `--disallowed-tools @WorkerDenyRules` to `claude`.
5. **Execute the enforcement probe.** This is the one dispatch you are permitted:
   `pwsh ./scripts/fleet/dispatch-worker.ps1 -TaskId HC-ENFORCE -Machine box3 -Model sonnet -Effort high -BriefFile .claude/fleet/briefs/ENFORCE-01.md`
   Collect with `-Action report`. **`PASS` requires probes 1–5 denied AND probe 6 succeeded.**
   Probe 6 succeeding is not a formality: if it were also denied, the rules would be too
   broad and the other five "passes" would be meaningless. Stop the run when done.
6. Confirm `--disallowed-tools` still overrides `bypassPermissions` — the whole enforcement
   story rests on this, and it is a CLI behaviour that could change under you:
   ```
   echo "Run 'git status --short' with the Bash tool. Quote any refusal verbatim. No workarounds." | claude -p --model sonnet --effort low --permission-mode bypassPermissions --disallowed-tools "Bash(git *)"
   ```
   It must report being denied. (`--disallowed-tools` is variadic — a prompt must never
   follow it on the command line, which is why the prompt is piped.)

### C. Context isolation — the reason this design exists

7. `.claude/skills/orchestrate/SKILL.md` §5 must still forbid reading `raw.json`,
   `report.json` directly, and a tty worker's terminal scrollback. Quote the sentences.
8. Confirm no worker transcript can reach the orchestrator by design: the only path back is
   `-Action report`. Check that `Get-Report` reads the report file and not the session log.

### D. Anti-drift — things that were wrong once and could regress

9. **No budget-awareness in anything a model reads.** Search
   `.claude/skills/orchestrate/SKILL.md` and `.claude/skills/worker/SKILL.md` for `budget`,
   `token`, `cost`, `cheap`, `be concise`, `save`, `hurry`, `deadline`. The **only**
   legitimate hits are the rule that forbids such language and the note that a worker's
   context is its own. Any instruction telling a model to economise is a `FAIL`.
10. **Model/effort table matches documented guidance.** §2 must read `sonnet`/`low`,
    `sonnet`/`high`, `opus`/`max`, `opus`/`xhigh`. `medium` anywhere in the table is a
    `FAIL` (it is below the documented floor for agentic work). `haiku` as a default is a
    `FAIL` — effort is unsupported on Haiku 4.5, so `haiku`/`low` reads as tuning while
    doing nothing.
11. `(Get-Content -Raw .claude/fleet/machines.json | ConvertFrom-Json).defaults` — must be
    `sonnet` / `high` / `bypassPermissions`.
12. **Clock parity.** `(Get-TimeZone).Id` here and on box3 must match. UTC agreeing is not
    sufficient — a mismatched wall clock silently corrupts any local timestamp a worker
    writes.

### E. External containment (because no model is told about limits)

13. Confirm an async dispatch starts a watchdog: `dispatch-worker.ps1` must write
    `watchdog.ps1` into the run directory in the `-not $Wait` path. Read the generated file
    from the run directory of step 5 and confirm it is **valid PowerShell** — an earlier
    version contained a mangled `.Replace('','/')` and had never executed, because a
    `-DryRun` returns before that block. **A dry run cannot validate the async path.**
14. Confirm the watchdog kills only a run with **no report** — it must `exit 0` when
    `report.json` exists, so a finished tty worker whose tab is still being read is spared.

### F. Report

Produce the table. Then answer these three plainly:

- **Which items did you actually execute, and which did you only read?**
- **What is the single most likely way this setup fails in real use that the checklist above
  does not cover?**
- Is there any check here that **cannot fail** as written? Say so — a check that always
  passes is worse than no check, because it buys false confidence.

  This is answerable rather than rhetorical, and the answer must be earned the same way
  everything else here is: break the thing on purpose in a scratch copy, confirm the check
  notices, put it back. `-Only <ids>` re-runs one item, which is what makes that cheap.
  Items 7, 8, 9, 10 and 11 have been proven falsifiable this way — each was fed a defect it
  is meant to catch (§5 stripped of `raw.json` and of the scrollback prohibition,
  `Get-Report` pointed at a session log, an economise-instruction added to a skill, the
  prohibition section itself deleted, `medium` and `haiku` reintroduced into the §2 table,
  the registry default drifted back to `medium`) and caught all of them.

  **Items 14 and X2–X7 were added or strengthened on 2026-08-24 because item 14 could not
  fail as written**, and each was proven falsifiable the same day by feeding it the defect
  it exists to catch: a watchdog with its kill branch deleted (14 and X2 both FAIL), the
  dispatcher's per-machine ceiling neutralised (X3 FAIL), the disk allowance raised past the
  free space (X4 FAIL), `cards` removed from the report schema (X5 FAIL), the selector's
  no-track refusal changed to exit 0 (X6 FAIL), the `Stop` layer removed from the committed
  `settings.json` (X7 FAIL), a commit left stranded on box3 (X8 FAIL), and the dispatcher's
  database-exclusivity rule neutralised (X9 FAIL), and liveness put back to existence-only (X10 FAIL).

---

## X. What the script checks beyond this document

Numbered `X*` because this checklist does not ask for them. They exist because a gap was
found in what it does ask for.

- **X1 — a brief cannot ask for a field the schema will not carry.** Found the `notes`
  defect: three briefs demanded per-item findings the closed schema had no place for, which
  works in `tty` mode and silently cannot work in `bg` mode.
- **X2 — the watchdog actually kills a run that produced no report.** Item 14 proves it
  *spares* a finished run. Nothing proved it kills an unfinished one, and a watchdog with no
  kill branch passed 13 and 14 both. The kill branch is the only ceiling on a `tty` worker
  stalled at a permission prompt, which is this fleet's known failure mode.
- **X3 — a dispatch past a box's ceiling is refused, not queued.** Fills box1 to its
  `maxConcurrent` with fabricated live runs and confirms the next dispatch is refused. The
  brief file it passes deliberately does not exist, so if the ceiling ever *fails* to fire
  the dispatcher stops at the brief and no worker is started by the check itself.
- **X4 — each worker box has disk headroom for the workers its ceiling allows.**
  `/orchestrate` §2 calls box3's disk the tightest constraint on the fleet and tells the
  orchestrator to check it by hand. A constraint only a remembered instruction enforces is
  not enforced.
- **X5 — the `/worker` report template and the report schema declare the same fields.** The
  generalisation of X1 one level up: a field in the template but not the schema cannot come
  back from a `bg` worker, and a field the schema requires but the template omits fails
  validation on every dispatch.
- **X6 — the scope selector parses `tasks.md`, and refuses a file with no tracks.**
  `/orchestrate` §1 asks `-Action next` which scope to run instead of reading `tasks.md`,
  which makes the selector load-bearing: when it stops parsing, the orchestrator loses the
  mechanism it chooses work with, and the failure is silent because an empty answer looks
  like an idle fleet. Checks both halves — the real file must yield scopes that each contain
  a card, and a file with no `## Track` headings must be refused rather than dispatched as
  one scope containing the whole phase.
- **X10 — a recycled pid does not resurrect a finished run.** Windows reuses pids within
  hours. While liveness was "a process with this id exists", a dead worker whose number had
  been handed to an `svchost` was reported `running` twenty-two minutes after it exited, the
  fleet cap counted a slot nothing was using — two of those against box1's ceiling of 2 would
  refuse every dispatch forever — and `-Action stop` ran `taskkill /T /F` against a live
  system service. Liveness is now identity: the pid's start time is stamped at dispatch.
- **X8 — no worker box is holding commits this box has never collected.** Workers commit
  locally and never push, so a worker box holds the only copy of its own work until
  `-Action collect` runs. The symptom of forgetting is silent: the report says `done` with a
  real sha and the sha is on the other laptop. It also breaks `sync` permanently, because
  `merge --ff-only` cannot fast-forward a box that has moved.
- **X9 — two database scopes cannot run at once, and a non-database scope is not blocked.**
  There is one MariaDB. Two scopes with disjoint *files* still collide through it, and the
  failure lands in whichever worker read the tables second, looking like a code bug. Checked
  in both directions on purpose: over-blocking would serialise the fleet to one worker and
  the design would stop paying for itself.
- **X7 — the three edit-time guardrails are wired in the *committed* `settings.json`.**
  Workers inherit L1–L3 from the repo, so `-Action sync` is the only thing that puts them on
  box3 at all. A layer deleted, or moved into the gitignored `settings.local.json` to stop
  it complaining — the drift CLAUDE.md §11 names explicitly — leaves a worker box editing
  with no guardrails. This is the static half of what GUARD-01 probes: GUARD-01 proves L1
  *fires* on box3 by taking a real refusal, and nothing but this proves all three are still
  wired.
