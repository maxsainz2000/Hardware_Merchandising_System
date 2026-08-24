---
name: health-check
description: Audit the fleet orchestration setup and report what is broken. Use for "fleet health", "check the fleet", "is the fleet OK", after any edit to the fleet scripts or skills, and before the first real mission of a phase.
allowed-tools: Read, Grep, Glob, Bash(pwsh *), Bash(git *)
---

# Fleet health check

**Your job is to find what is broken, not to confirm it is fine.** A clean bill of health
that misses a real fault is the worst outcome available to you. Reporting a fault is a
success.

The checklist this implements is `.claude/fleet/health-check.md` — that file is the
definition of each item and the reasoning behind it. This skill runs it.

## What this session should be running on

**`opus` at `high`** — `claude --model opus --effort high`, in a **fresh** session.

`high`, not `xhigh`: this is verification, not orchestration. And it must not be the session
that then goes on to run a mission — an orchestrator that has just audited its own fleet is
holding a full audit of detail it will spend the whole mission carrying. If you are
reading this in a session that has been doing something else, say so in the report.

## The ground rules — they matter more than the checklist

- **Verify by executing, never by reading.** That is the whole reason the checklist is a
  script: a long manual checklist gets executed unevenly, and the step that gets skipped is
  the one that reports `PASS` anyway. Run it and paste the real output.
- **Do not fix anything.** This is a diagnosis. If you find a fault, describe it precisely
  and stop; the repair is a separate decision that belongs to the user. The tool list above
  omits `Edit` and `Write` so a fix is not one keystroke away — but be clear-eyed that this
  is friction, not a wall: `pwsh` can write files. The discipline is still yours.
- **Do not dispatch any worker.** Item 5 dispatches exactly one, from inside the script.
  That is the only one.
- **`UNVERIFIED` is an honest result.** A `PASS` you did not earn is a lie in a report
  someone will act on.

## 1. Run it

```powershell
pwsh ./scripts/fleet/health-check.ps1
```

Takes a few minutes: item 3 syncs box3, item 5 dispatches a real worker and waits for its
report, item 6 spends one API call. A machine-readable copy lands in `.claude/fleet/health/`.

Two switches, and only one of them is legitimate to reach for:

- `-Quick` — skips items 5 and 6, which are the two that dispatch a worker and spend an API
  call. Use it when box3 is off or there is no network. They come back **`UNVERIFIED`, and
  you must report them that way**: a quick run has not tested the deny list at all, and the
  deny list is the thing most worth testing.
- `-Only 9,10` — re-checks named items after a repair. A partial run is labelled as one in
  the output and in the JSON. It is never a health check; do not report it as one.

**If the script itself will not run, that is the finding.** Say so and stop. Do not fall
back to working through `health-check.md` by hand and reporting the result as equivalent —
a hand-run audit is exactly what this replaced.

## 2. The four states

| State | Means | What you do |
|---|---|---|
| `PASS` | The check ran and the invariant held. | Nothing. |
| `FAIL` | The check ran and something is broken. | Report it precisely. Do not repair it. |
| `UNVERIFIED` | The check could not run. | Report it as unverified, and say what was not tested. Never round it up. |
| `REVIEW` | The check ran and the verdict needs a reading, not a match. | **Resolve it yourself** — see below. |

`REVIEW` is not a result you may hand back. Every `REVIEW` becomes a `PASS` or a `FAIL`
before you write the table, and your report says which and why.

## 3. Adjudicate the `REVIEW` items

Only two items can land there, and each needs the one thing a script cannot do:

- **Item 9 — budget-awareness.** The script proves no *imperative* to economise exists, and
  prints every mention of `budget`, `token`, `cost`, `cheap`, `be concise`, `save`, `hurry`,
  `deadline` for you to read. Read them. A mention of cost is not the defect — the whole
  model/effort rationale in `/orchestrate` §2 is about cost and is exactly right. The defect
  is a sentence **telling a model to economise**. If you find one the tripwire missed, that
  is a `FAIL` *and* a gap in the tripwire; report both.
- **Item 6 — the CLI override probe.** Lands in `REVIEW` when `claude`'s answer names no
  refusal. That means either the deny rule did not hold, or the model worded it unusually.
  Read the verbatim text in the evidence and decide. If it did not hold, the entire
  enforcement story is void and that is the headline of your report.

## 4. Report

Produce the table — item, state, one line of evidence each — then answer these three
plainly. They are the part of the audit a script cannot do, and they are worth more than
the table:

- **Which items did you actually execute, and which did you only read?** The script executes
  items 1–6, 12–14, X2–X4 and X6 against live machinery — X2 runs a generated watchdog, X3
  fills a box to its ceiling and takes the refusal, X4 reads free disk over ssh, X6 runs the
  scope selector against both the real `tasks.md` and a no-track fixture, X8 asks each worker
  box what it is holding, X9 holds the database with a live run and takes the refusal, X10
  fabricates a recycled pid, X11 fills a box and takes the refusal. Items 7–11, X1, X5 and X7 are static analysis of files in the
  repo. Say so rather than implying every item was exercised equally.
- **What is the single most likely way this setup fails in real use that the checklist does
  not cover?**
- **Is there any check here that cannot fail as written?** A check that always passes is
  worse than no check, because it buys false confidence. If you suspect one, prove it:
  break the thing on purpose in a scratch copy, confirm the check misses it, and put it
  back.

## Stop conditions

- A `FAIL` is a finding, not a task. Report it and stop.
- If a repair is obviously needed and obviously small, **say what you would change and
  wait.** The fleet's invariants are load-bearing; an unrequested edit to one of them is
  the failure mode this whole design exists to prevent.
