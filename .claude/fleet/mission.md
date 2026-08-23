# Mission ledger

The orchestrator's working state, kept **on disk on purpose**. This file is what lets an
orchestrator session survive a compaction without re-deriving the plan, and it is why the
plan does not have to live in context.

One row per card. Update it when you dispatch and when you collect. Delete rows when a
mission closes — this is a worksheet, not a history. `git log` is the history.

| Card | Machine | Model/Effort | Scope boundary | Status | Note |
|---|---|---|---|---|---|
| _(none open)_ | | | | | Phase 2 not started; fleet setup complete |

## Fleet state — 2026-08-23 · two laptops, ssh throughout

| Box | Machine | Transport | Ceiling | Ready | Owns |
|---|---|---|---|---|---|
| box1 | LAPTOP-3HH6OHHE (laptop) | local | 2 | yes | `src/` `tests/` `db/` `docs/` — and **every** DB / migration / running-API card |
| box3 | DESKTOP-F5LK8MA (**laptop**, 57.8 GB) | ssh | 4 | yes | everything else |

**Both boxes are laptops on the same hotspot, so the fleet is whole away from home** — at
school, on battery, anywhere. That is the property the desktop's retirement bought, and it
is worth more than the desktop's 200 GB was.

`maxConcurrent` is a **ceiling, not a target**. How many workers run, and at what model and
effort, is the orchestrator's decision per mission — `/orchestrate` §2. The dispatcher
enforces the ceiling per box and the global `maxFleet` (5) across the fleet; raising either
is a stop condition, not an edit to make mid-mission.

box3 has no XAMPP/MariaDB and that is correct, not missing. ADR-013 pins the three DB
identities to box1, so every card touching the database, a migration, or the API at runtime
is placed on box1 no matter which box is idle.

**box3's disk (57.8 GB) is the tightest constraint on the fleet.** Each concurrent worker
carries its own restore and build. Before dispatching three or four:
`ssh box3 "pwsh -NoProfile -Command (Get-PSDrive C).Free/1GB"`.

## SSH transport — live on box3

`SMOKE-02`, 2026-08-23: dispatched from box1 over SSH at `haiku`/`low`, worker started on
box3, structured report back, $0.069, compact digest — no transcript entered the
orchestrator's context. `fleet.ps1 -Action doctor` reports
`[box3] transport=ssh enabled=True · ssh + claude : OK`.

Shape of it: `sshTarget` is the ssh_config **Host alias** `box3`; user, identity file and
hostname live in box1's `~/.ssh/config`, so the fleet registry carries nothing
credentials-adjacent. Key-only auth, `PasswordAuthentication no`, firewall scoped to
`192.168.100.0/24` — the subnet rather than box1's current IP, because DHCP will move it and
a rule pinned to one lease fails silently later. No Tailscale needed while both laptops
share the hotspot; it remains a one-line upgrade for when they do not.

Why it mattered: `rc` gives the orchestrator exactly one verb, **prompt**. It cannot start a
session, set its model or effort, or stop one — so on an `rc` box the model/effort table in
`/orchestrate` §2 is advice nothing enforces. `ssh` closed that gap. Nothing in the fleet
uses `rc` any more; box3 keeps an `rcSessionName` only as a manual fallback, and those names
change on every session restart, so confirm against a live `ListAgents` before trusting one.

## What the tty transport cost — 2026-08-23

`-Mode tty` became the default so a worker can be watched: a box3 worker is reached with
`ssh -t` and renders in a tab **on box1's screen**. Its report cannot come back on stdout
(nothing captures an interactive session's last message), so the brief tells the worker to
write the report to a file and `-Action report` fetches it back with `scp`.

Four defects surfaced getting there, three of them silent. All are fixed; they are recorded
because each one looked like something else first.

1. **The worker box did not have the runner.** box3 only gets repo changes when it pulls, so
   a checkout one commit behind has no `run-worker-tty.ps1` — and it fails as pwsh saying a
   file does not exist, inside a tab that closes. The dispatcher now **carries the runner
   over `scp`** with the brief, so the transport is self-carrying and never older than the
   dispatcher invoking it. It also **warns when the two boxes are on different commits**:
   the runner travels, the project does not, and a card worked against a stale tree can pass
   its own tests and still not apply here.
2. **Copying the runner broke its own path math.** It derived the repo root from
   `$PSScriptRoot`, which is correct in `scripts/fleet` and wrong from a run directory. It
   now takes `-RepoRoot` explicitly and **refuses to start outside a checkout** — a worker
   with no project `CLAUDE.md` does not know VB-only or the MariaDB 10.4 limits, and that
   session looks completely normal while it works without any of the project's rules.
3. **`acceptEdits` stalls a worker on its first shell command.** It auto-accepts file
   *edits* only and still prompts on every Bash/PowerShell call. SMOKE-03 sat at flat CPU
   for 15 minutes holding a fleet slot, and from the orchestrator that is indistinguishable
   from working hard. **This is a permission-mode property, not a model one** — the first
   reading was "haiku cannot run unattended", and that is wrong; opus stalls identically.
   The registry default is now `auto`. `auto` is still not `bypassPermissions`, which stays
   a stop condition needing the user's per-dispatch approval.
   `-Action list` now prints elapsed minutes and marks a run `OVERDUE` past its timeout,
   because a stall is the one failure mode with no other signal.
4. **`-Action stop` orphaned the remote worker.** Killing the local `ssh` client took down
   the tab and left `claude` running on box3, still holding its session and still able to
   spend. The old comment claimed ssh runs "happen to die anyway when their wrapper goes";
   measured, they do not. Stop now reaches over and kills the remote worker too.

   Getting *that* right took two more wrong answers, both of which reported a dead worker
   as alive: trusting `taskkill`'s **exit code** (non-zero for reasons unrelated to whether
   the process died), and then **matching the worker by command line** — a query that
   mentions the pattern *contains* the pattern, so it matches itself and its own `cmd.exe`
   wrapper. The runner now writes its PID to `worker.pid` in the run directory and stop
   kills that, then re-checks and reports what it actually found. A pid on disk has nothing
   to collide with.
5. **A tty worker never "finishes".** An interactive session does not exit after its last
   message — the tab stays open so it can be read, which is the point of the mode. So
   `Alive` stayed true forever and `-Action report` refused to collect, permanently. **The
   completion signal for a tty worker is the report file existing, not the process dying.**
   Report now fetches first and only calls a run "still running" when there is no report;
   when both are true it collects and says the tab is still open.
6. **`bypassPermissions` has a one-time acceptance gate.** Claude Code asks "Yes, I accept"
   the first time a session starts in that mode on a box. A dispatched worker hits it
   before doing anything and stalls at flat CPU. It is not per-command and does not
   return — it is now readiness condition 7 in `/orchestrate`, to be cleared during
   provisioning rather than on the first real card.

**Proven end to end, SMOKE-06, 2026-08-23:** dispatched from box1 → `ssh -t` tab on box1's
screen → worker ran on box3 → wrote its report there → `scp`'d back → parsed digest, 80
seconds, no transcript in the orchestrator's context. Stop then killed the remote worker and
verified it: zero `claude` processes left on box3. The four facts came back correct,
including `HEAD=0e6eaaf` — the stale commit the drift warning had predicted.

## Efficiency without budget-awareness — the user's decision, 2026-08-23

**No worker and no orchestrator is ever told about a budget, a token count, a cost or a
deadline.** The user's reasoning, and it is sound: a model that knows it is running out of
something paces itself, consolidates prematurely, truncates reasoning and settles for the
first adequate answer — a quality loss bought with a saving nobody measured. Anthropic's own
`task_budget` feature works exactly this way *on purpose*, by injecting a countdown the
model sees so it paces itself. This fleet wants the opposite.

So efficiency is **architectural and placement-based** — it acts on a model without the
model perceiving anything: separate context windows, reports instead of transcripts, briefs
that bound scope, reading `tasks.md` and not the source tree, and the model/effort table.
`low` effort produces fewer and more consolidated tool calls as a *property of the setting*,
not as an instruction obeyed. That distinction is the whole design.

**This forced the containment to be real, because it can no longer be a prompt.** Two gaps
were open and are now closed:

- `--max-budget-usd` **only works with `--print`**, so the `maxBudgetUsd: 5` in the registry
  did nothing in the `tty` mode that is now the default.
- The hard timeout only ran under `-Wait`. An **async dispatch — the normal path — had no
  enforced ceiling of any kind**, only the OVERDUE marker in `-Action list`.

An async dispatch now starts a **watchdog process** that kills the run at `timeoutMinutes`
**only if it has produced no report**, so a runaway is bounded while a finished `tty` worker
whose tab is still being read is left alone. Both paths were tested against a synthetic run
with a dummy process rather than by spending a worker: the kill path killed, the
report-present path left the process running.

That test also exposed a separate fault worth keeping: `Get-Runs` read optional handle
fields directly, and `Set-StrictMode` turns a missing property into a terminating error —
so **one stale run directory broke `list`, `report` and `stop` for every run**, including
the commands you would use to clean it up. All optional fields go through `Get-Prop` now.

## Model and effort — corrected against documentation, 2026-08-23

The user asked what the `/orchestrate` §2 model/effort table was based on. The honest
answer was **nothing**: it arrived in `e2bc326` with the skill, was never checked against
Anthropic's guidance, and had since been built on — the "how many workers" section added
the same day called it "the same kind of decision", lending it authority it had not earned.

Checked properly, it was wrong in three ways. The corrected table is in §2; the reasoning
is recorded here because a table that looks plausible is exactly the kind that gets
reverted by someone trying to save money.

- **`medium` was below the floor.** Documented guidance: a *minimum* of `high` for
  intelligence-sensitive work, `high`/`xhigh` for long-horizon agentic tasks, `max` when
  correctness matters more than cost, `low` for simple sub-tasks. **`xhigh` is Claude
  Code's own default.** The fleet was running every worker below what Claude Code would
  have picked for itself — a saving nobody chose. Registry default is `high` now.
- **`haiku`/`low` was not a real setting.** Haiku 4.5 is a 4.5-generation model and
  **`effort` is not supported on it**, so the flag did nothing while reading as a tuning
  decision. Its context is 200K against 1M for Opus 5 and Sonnet 5 — a real limit for a
  worker holding the spec, CLAUDE.md and a card at once. And Sonnet 5 is $3/$15 per MTok
  against Haiku's $1/$5: two to three times, not the order of magnitude assumed. It stays
  *selectable* for a deliberate choice and is no longer a default anywhere.
- **`opus/high` was too low for the cards that matter most here.** `DECIMAL(19,4)`
  arithmetic, transaction atomicity and the `merch_api` grant model are precisely the
  "correctness matters more than cost" case, which is `max`.

**What was verified on this machine versus taken from documentation**, because the
distinction is the whole point: the effort levels (`low`/`medium`/`high`/`xhigh`/`max`) and
the model aliases were read out of `claude --help` here. The per-tier *recommendations* come
from Anthropic's API documentation, and the fleet drives the **CLI**, not the API — carrying
them across is an inference. A reasonable one, since the docs state `xhigh` is Claude Code's
default, but an inference, and it was not confirmed against CLI-specific documentation.

## Syncing a worker box — by bundle, not by pull

**box3 cannot `git pull` over ssh, and this is not fixable on that box.** Git Credential
Manager's default `wincredman` store is bound to an **interactive desktop logon**. Remote
Control sessions are one, which is why the old readiness rule ("sign in once and every later
headless fetch works") was true then and is **wrong for ssh**: sshd's network logon cannot
read that credential. It fails as `Unable to persist credentials with the 'wincredman'
credential store`, then a username prompt against a `/dev/tty` that does not exist.

Two paths were tried and rejected before the one that works:

- **git-over-ssh from box1 to box3** (`git push box3 master` with
  `receive.denyCurrentBranch=updateInstead`). The path arrives literally single-quoted —
  box3's default SSH shell does not strip the quotes git wraps it in — and git reports the
  repository does not exist. Changing that box's `DefaultShell` would fix it **and break
  every quoting pattern the dispatcher, doctor and stop rely on.** Not worth it. Both the
  temporary remote and the `denyCurrentBranch` setting were reverted; nothing was left on
  box3.
- **A token in a plaintext credential store on box3.** Works, and puts a GitHub token in
  cleartext on a laptop, permanently, to save a step. Refused.

**What works: `fleet.ps1 -Action sync`.** `git bundle` on box1 → `scp` → fetch and
fast-forward on box3 → bundle deleted. No credential, no GitHub reachability from the worker
box, no remote shell parsing. It refuses a box with uncommitted changes rather than merging
over a worker's unfinished work, and it verifies the resulting HEAD rather than trusting the
command's exit code.

**Consequence for provisioning:** after the one human clone, a worker box needs **no working
git credential at all**. Workers commit locally and never push.

**Sync before dispatching.** The runner travels with the dispatch; the project does not.

## box2, retired — 2026-08-23

DESKTOP-G83CCSH (desktop, 200.9 GB, SDK 10.0.400) was removed from `machines.json`. Kept
here so none of it is rediscovered the hard way.

**Why it went.** Not the ssh failure — the network. It had no working wireless and was
cabled to the Huawei 5G router, so it was reachable **only while box1 was at home on that
same router**, and the fleet silently shrank to one machine at school, which is exactly when
a demo needs it. Its internet was metered mobile data through that router too, so every
download it made cost. The unsolvable ssh transport was the second reason, not the first: it
was stuck on `rc` permanently, which meant its model and effort could never be set.

**The ssh failure, for the record.** `sshd.exe` dies at the Windows loader,
`0xC0000135` (STATUS_DLL_NOT_FOUND), while `ssh.exe -V` from the same folder runs fine.
ScanHealth, `sfc`, signature validation, the import scan and the DISM CLI all came back
clean — that loader failure was the **only** reproducible symptom, and the two other
"symptoms" first reported on that box were retracted as misreadings. Cause unknown, cheap
fixes exhausted, closed unsolved. Untried and free if it ever matters: `dism
remove-capability` then re-add, sourced from its own clean component store.

**Nothing was ever applied to box2** — no firewall rule, no key, no `sshd_config` change —
so there is nothing to revert on that machine. Its clone at
`C:/dev/Hardware_Merchandising_System` and its cached git credential are still there and
still valid if it is ever re-registered.

**What the two attempts taught, now written into `/orchestrate`** so it is not paid for
twice: pass `/norestart /quiet` on the first DISM call and treat a *missing* `sshd` service
as a diagnosis rather than a reboot; `Class not registered` from `Get-WindowsCapability`
predicts nothing about CBS health; the SCM's error 1053 is noise and `sshd.exe -t` gives the
real error; an Administrator account makes sshd ignore `~/.ssh/authorized_keys` in favour of
`C:\ProgramData\ssh\administrators_authorized_keys`; NTFS ACLs on the private key matter
because pwsh uses the System32 client; and dumping a binary's imports to `Test-Path` them
cannot work, because ApiSet contracts are virtual on every healthy box.

**A general rule out of it:** a loader-level failure on an otherwise-clean box is a machine
fault, not a setup step. Report it and stop — the repair is open-ended, may not fix the
thing you wanted, and the box doing the repairing is the broken one.

## Status values

`planned` → `dispatched` → `done` | `blocked` | `failed`

A card is `done` only once **you** have read its report, found it internally coherent, and
ticked its boxes in `tasks.md`. A worker's own `done` is a claim, not the fact.
