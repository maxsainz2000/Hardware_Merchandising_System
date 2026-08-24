---
name: orchestrate
description: Run a fleet of Claude Code workers across this laptop and box3 to execute several scopes - tracks of related task cards. Use for "orchestrate", "dispatch workers", "run tasks in parallel", "use the other laptop", "what is next", or when a phase has independent tracks worth splitting.
argument-hint: "[scope, task-ids, or goal - omit to take the next open scope]"
arguments: [goal]
allowed-tools: Read, Grep, Glob, Edit, Write, Bash(pwsh *), Bash(git *), Bash(ssh *), ListAgents, SendMessage, AskUserQuestion
---

# Orchestrator — `$0`

You do the thinking. Workers do the typing. Your scarcest resource is **your own context**,
not their time — protect it deliberately, because an orchestrator that has read three
transcripts is worse at deciding than one that has read none.

## What this session should be running on

**`opus` at `xhigh`.** Start the orchestrator session with
`claude --model opus --effort xhigh`, then `/orchestrate`. If you are reading this on a
weaker configuration, say so to the user before planning a mission — the split only pays off
when the deciding half is the capable half.

The reasoning, not a preference: this session does the planning, the placement, the
decisions a worker escalates, and the verification of every report. Anthropic's own
orchestrator-worker guidance puts the coordinator on `claude-opus-5` and the workers on
smaller models for exactly this reason — *"the large model spends its tokens on planning,
checking, and synthesis; the small model does the bulk reading."* On effort, the documented
rule is to run long-horizon agentic work at `high`/`xhigh`, and `xhigh` is Claude Code's own
default for agentic use.

`max` is deliberately not the answer here. It is reserved for cards where correctness
outranks cost — the money, transaction and grant work in §2 — and spending it on the
dispatcher rather than on those cards inverts the point.

**Workers run lower on purpose, and that is not thrift.** A worker with a card, a scope
boundary, and acceptance checks copied verbatim is doing bounded implementation; the
judgement was already made here. That is what makes `sonnet` the right default for them and
`opus` the right one for you.

## Efficiency is structural — never an instruction

**No worker is ever told about a budget, a token count, a cost, or a time limit, and
neither are you.** This is a deliberate design decision by the user, not an oversight to
correct. A model told it is running out of something changes how it works: it paces itself,
consolidates prematurely, truncates reasoning and settles for the first adequate answer.
That is a quality loss bought with a saving nobody measured.

Efficiency here comes from **architecture and placement**, both of which act on a model
without it perceiving anything:

- **Separate context windows.** A worker's transcript never enters yours. This is the
  single largest saving in the design and it costs no quality at all.
- **Reports, not logs** (§5). One small object crosses back per scope, not a session — and
  a scope is several cards, so the saving grows with the size of the track.
- **Briefs are boundaries** (§3). A tight scope means less exploration, because there is
  less in scope — not because anyone asked for restraint.
- **Asking `-Action next` instead of reading `tasks.md`** (§1). The source-tree wander is
  the biggest avoidable spend available to you and reading the whole card file is its close
  cousin; a script that answers the same question costs you three lines of output.
- **Placement** (§2). `low` effort on a genuinely mechanical card produces fewer and more
  consolidated tool calls — as a property of the setting, not as an instruction obeyed.
- **External ceilings the worker cannot see.** A per-run timeout enforced by a watchdog
  process, a fleet cap, per-box concurrency ceilings. All of them live outside the session
  and none of them reaches a prompt.

**So: never write "be concise", "save tokens", "you have a limited budget", or a time
pressure into a brief, and never add one to this skill.** If a card is costing more than it
should, the lever is its placement in §2 or the boundary in §3 — never a plea to the model.

## 0. Before anything: is this even an orchestration?

Dispatching has overhead — a worker startup, a brief, and a report. **If the whole job is
under roughly two tool calls, just do it yourself.** Orchestrate when:

- there are **≥2 genuinely independent scopes** (disjoint file sets), **or**
- one scope is long-running and you have other work to place beside it, **or**
- the user explicitly asked for the fleet or for box3.

One scope, alone, on this machine, is `/task` card by card — not this skill. Say so and
switch. And a scope of one card is still just a card: Track D is P2-10 and nothing else,
so it is `/task` unless something else is running beside it.

## 1. Plan before you spawn — and the unit is a SCOPE, not a card

**A scope is one `## Track` from `tasks.md`: an ordered list of cards that only make sense
together.** Track A is P2-01 → P2-02 → P2-03, and that order is real — the migration has to
land before the policies that read it, and the policies before the tests that assert them.
**Tracks are the things that are independent of each other; the cards inside one are not.**

Dispatching a card at a time splits work that has to stay together, pays a worker startup
for each piece, and forces a hand-off in the middle of a dependency chain. Dispatching a
track hands one worker a boundary that matches how the work actually decomposes. One scope,
one worker, one commit *per card*.

1. **Ask which scope is next. Do not read `tasks.md` to find out.**

   ```powershell
   pwsh ./scripts/fleet/fleet.ps1 -Action next
   ```

   That is how the next scope is chosen when nobody names one, and it is deliberately a
   command rather than an instruction you follow by hand. It parses `tasks.md` itself and
   returns, for each open scope: the ordered open cards, the union of their `**Files:**`
   lines, a box recommendation, and every pair of scopes that collide. Reading `tasks.md`
   yourself to answer the same question costs you the whole file and answers it less
   reliably — the source-tree wander is the biggest avoidable spend available to you, and
   this is its close cousin.

   The rule it implements, so you can predict it: **the next scope is the first track in
   file order with at least one card not `✅`.** Carried-forward cards are listed but never
   proposed — each is owed to a named later gate (ADR-016), so they are open work, not next
   work. Take one only if the user asks.

2. **Independence is computed for you, and it is stricter than string equality.** Two
   scopes may run in parallel only if their file sets are disjoint, where a declared
   *directory* collides with any file inside it. `-Action next` prints the collisions and
   proposes a mutually-compatible wave. When it says nothing is parallel-safe, believe it
   and sequence: a merge conflict across two machines costs far more than a serial run.

   **Disjoint files are not disjoint work.** There is exactly one MariaDB, on box1, so two
   database-touching scopes collide through it even when they share no file — one applies a
   migration while the other's integration tests are reading the tables, and the failure
   lands in whichever worker read second looking exactly like a code bug. The database is
   therefore modelled as an **exclusive resource**: at most one database scope runs at a
   time, `-Action next` shows it as a conflict, and **the dispatcher refuses the second one**
   rather than trusting anyone to remember. That is a wall, not advice.

   The consequence for Phase 2 is worth stating plainly: **every track needs the database,
   so Phase 2 runs one scope at a time.** The fleet's parallelism arrives when there is
   non-database work — docs, scripts, client XAML, evidence capture — to place beside it.

   Its one real limit, and it is worth holding in mind: it is only as good as the
   `**Files:**` lines in the cards. **A card that understates its files understates its
   conflicts.** If a scope's file list looks thin for what the card actually describes,
   sequence it rather than trusting the green line.

3. **Place each scope.** The fleet is two laptops, and the split is one rule:
   `box1` holds XAMPP/MariaDB, so **every card touching the database, the API at runtime,
   or a migration runs on box1** (ADR-013 pins the three DB identities to that host).
   **box3 takes everything else** — evidence capture, scripts, docs, client-only
   XAML/VB, unit tests that need no database, guardrail runs. It is the worker box, not a
   documentation box; do not leave it idle because a card "feels like box1 work".

   **Check this against reality before you plan a wide mission.** In Phase 2 every single
   track routes to box1 — all five touch a migration, a grant, or an integration test —
   so box3's four-worker ceiling is not reachable no matter how the cards are cut, and
   box1's ceiling of 2 is the real bound on the phase. That is a fact about Phase 2, not
   a rule; re-run `-Action next` at the next phase rather than assuming it still holds.
4. **Decide how many.** See §2 — the count is your call, bounded by each box's
   `maxConcurrent` in the registry (box1 2, box3 4). The dispatcher enforces the ceiling
   and **refuses** past it (proven, health check X3); it does not choose the number for you.
5. **Write the ledger** to `.claude/fleet/mission.md` before dispatching — scope, cards, machine,
   model, scope boundary, one line each. On disk, not in your context. It is what lets you
   resume after a compaction without re-deriving the plan.

## 2. Choose model, effort, and how many — deliberately

Three decisions, all yours, none of them a default to skip past.

| Scope looks like | Model | Effort |
|---|---|---|
| Mechanical, fully specified — evidence capture, a scripted run, boilerplate | `sonnet` | `low` |
| Ordinary implementation of a card that is already well specified | `sonnet` | `high` |
| Business rules, transactions, concurrency, money/decimal handling, auth, SQL grants | `opus` | `max` |
| A card that already came back `blocked` once, or where the design is genuinely open | `opus` | `xhigh` |

**This table is derived from Anthropic's documented guidance, not from intuition** — an
earlier version of it was the reverse, and was wrong in three ways worth naming so they are
not reintroduced:

- **`medium` is below the floor for this kind of work.** The documented rule is a *minimum*
  of `high` for intelligence-sensitive work, `high`/`xhigh` for long-horizon agentic tasks,
  and `max` when correctness matters more than cost. `xhigh` is **Claude Code's own
  default**. A fleet running `medium` is running below what Claude Code would have picked
  for itself, which is not a saving anybody chose.
- **`haiku` is gone, and not on quality grounds.** Haiku 4.5 is a 4.5-generation model:
  `effort` is **not supported on it** — a `haiku`/`low` dispatch gives you haiku with the
  effort flag doing nothing, while reading like a deliberate tuning choice. Its context is
  200K against 1M for Opus 5 and Sonnet 5, which is a real constraint for a worker holding
  the spec, CLAUDE.md and a card at once. And Sonnet 5 is $3/$15 per MTok against Haiku's
  $1/$5 — two to three times, not the order of magnitude the old table implied.
- **`opus/max`, not `opus/high`, for the money and grant cards.** `DECIMAL(19,4)`
  arithmetic, transaction atomicity and the `merch_api` grant model are exactly the
  "correctness matters more than cost" case the guidance names.

**Escalate on evidence, never pre-emptively.** Dispatch at the tier the scope deserves; if
the report comes back `blocked` or `failed` on something real, re-dispatch the remaining cards one
tier up with the blocker written into the brief. Escalation with the failure in hand beats
starting high and hoping.

**How many workers on box3 is the same kind of decision.** The registry's `maxConcurrent`
is a ceiling the dispatcher enforces (box3 4, box1 2) — it is not a target to fill. Pick
the count from the work, against three real limits:

- **Independence, first.** The count can never exceed the number of genuinely disjoint
  card sets you proved in §1. Four workers on three independent cards is three workers and
  one merge conflict.
- **box3's disk is 57.8 GB and it is the tightest thing about that box.** Each worker
  carries its own build and restore. Before dispatching three or four, check it:
  `ssh box3 "pwsh -NoProfile -Command (Get-PSDrive C).Free/1GB"`. Heavy-build cards go on
  box1 instead.
- **Attention.** In the default `tty` mode every worker opens a tab **on your screen**.
  Four tabs is four things the user could be watching and is not.

Two well-placed `sonnet/high` workers beat four poorly-placed ones on cards that were never
independent. When in doubt, dispatch fewer and dispatch again — the second wave is one more
startup, and a merge conflict across two machines is an afternoon.

## 3. Write the brief

A brief is a **boundary**, not a description. Every brief states, in this order:

1. **Scope ID and its cards, in execution order** — `P2-TRACK-A: P2-01 → P2-02 → P2-03` —
   plus the spec sections each card cites. The order is the instruction: a worker does them
   in it, and stops at the first one it cannot finish rather than skipping ahead.
2. **Exact scope** — the files this worker may touch, across the whole track. Name them.
   `-Action next` already printed this union; copy it rather than re-deriving it.
3. **Acceptance checks per card**, copied from the cards. Not paraphrased, not merged into
   one list — the worker reports per card and cannot do that against a merged list.
4. **What NOT to do** — the adjacent thing they will be tempted by. Be explicit; this line
   prevents more damage than the other four combined.
   **Scope, never scarcity.** Name files and behaviours that are out of bounds. Never write
   a budget, a token count, a cost, a deadline or "be brief" into this section — bounding
   the work is what makes a worker efficient; telling it to hurry is what makes it worse.
5. **Evidence path**, if the card names one.

Never inline CLAUDE.md or the worker contract into a brief. Workers load both from disk.

## 4. Dispatch

> **Folder:** repo root · **USB:** not required · **Shell:** normal (elevation not needed)

**Run the pre-flight first, every mission. If it says NOT READY, you do not dispatch.**

```powershell
pwsh ./scripts/fleet/fleet.ps1 -Action preflight
```

It checks, in one command, every state that has actually poisoned a mission here: a box that
cannot be reached, a box holding commits nobody collected, a box that is not at this box's
HEAD, a ceiling already full, an OVERDUE run that is probably stalled, and disk headroom for
the workers a ceiling allows. Uncommitted work here is a **warning**, not a refusal — a
worker box is synced from HEAD, so your uncommitted changes will not reach it.

**It repairs nothing, deliberately.** Every condition it reports has a one-command fix and it
applies none of them. A mission that heals itself is a mission that hides the state which
told you something was wrong — the last phantom "live" worker this fleet produced turned out
to be a pid-recycling bug with `stop` aimed at a system service, and an auto-prune would have
buried it. Detect early, refuse loudly, decide yourself.

```powershell
# which scope is next, what it collides with, and where it should run
pwsh ./scripts/fleet/fleet.ps1 -Action next

# transport detail, when preflight says a box is unreachable and you want to know why
pwsh ./scripts/fleet/fleet.ps1 -Action doctor

# bring the worker boxes up to this box's HEAD -- do this BEFORE dispatching real work
pwsh ./scripts/fleet/fleet.ps1 -Action sync

# dispatch one SCOPE (async; returns immediately with a handle)
pwsh ./scripts/fleet/dispatch-worker.ps1 -Scope P2-TRACK-A -Machine box1 `
     -Model sonnet -Effort high -BriefFile .claude/fleet/briefs/P2-TRACK-A.md

# the worker box - same command, different -Machine. `ssh -t` opens its tab on YOUR screen
pwsh ./scripts/fleet/dispatch-worker.ps1 -Scope P2-TRACK-B -Machine box3 `
     -Model sonnet -Effort high -BriefFile .claude/fleet/briefs/P2-TRACK-B.md

# headless instead, when nobody is going to watch it anyway
pwsh ./scripts/fleet/dispatch-worker.ps1 -Scope P2-TRACK-C -Machine box3 -Mode bg

# collect, stop, survey
pwsh ./scripts/fleet/fleet.ps1 -Action list
pwsh ./scripts/fleet/fleet.ps1 -Action report -TaskId P2-TRACK-A
pwsh ./scripts/fleet/fleet.ps1 -Action stop   -TaskId P2-TRACK-A      # or -All
```

**`-Scope` and `-TaskId` are the same parameter.** `-Scope` is the name that matches what is
dispatched; `-TaskId` stays because every run directory, handle and watchdog already written
on both boxes keys on it, and renaming a key breaks collection of runs in flight. Collection
still takes `-TaskId`, and the value is the scope id.

**Sync before you dispatch.** The runner travels with the dispatch; **the project does
not**. A worker box only moves when you move it, and a card worked against a stale tree can
pass its own tests and still not apply here — so `-Action sync` comes before the first
dispatch of any mission. The dispatcher warns when the commits differ; the warning is a
backstop, not the plan. Sync refuses a box with uncommitted changes rather than merging over
a worker's unfinished work.

**`tty` is the default mode, including for box3.** A remote worker is reached with
`ssh -t`, whose ConPTY gives the TUI a real terminal, so box3's session renders in a tab
**here** rather than on a laptop nobody is sitting at. The trade: an interactive session's
final message is not captured anywhere, so the brief instructs the worker to write its
report to a file, and `-Action report` fetches that file back over `scp`.

**A `tty` worker can stall where a `bg` worker would fail.** In a visible session a
permission prompt or a folder-trust dialog waits *forever* — the worker sits at flat CPU,
holding its slot, and from here that looks exactly like working hard. Headless mode has no
such state: the prompt is auto-denied and the run dies loudly. Two consequences, and both
are yours:

- **`tty` mode assumes someone is watching.** That is the whole point of it. If nobody
  will be at the screen, dispatch `-Mode bg` instead — a mode whose failures are visible
  beats a mode whose stalls are not.
- **The registry's `permissionMode` is `bypassPermissions`, by the user's explicit
  decision on 2026-08-23. Do not lower it and do not re-ask per dispatch.** Both milder
  modes were measured stalling a worker on box3: `acceptEdits` auto-accepts file *edits*
  only and still prompts on every Bash/PowerShell call, and `auto` does the same because on
  Windows it cannot sandbox a shell call. **This is a permission-mode property, not a model
  one** — do not diagnose it as "the small model cannot run unattended"; `opus` stalls in
  exactly the same place. What still guards a worker: the L1–L3 hooks fire regardless of
  permission mode, the L4 pre-commit hook is on every worker clone, workers never push, and
  both boxes are the user's own. Dispatching a *milder* mode is the thing that now warns.
- **`-Action list` prints elapsed minutes and marks a run `OVERDUE` past its timeout.**
  Treat `OVERDUE` on a `tty` worker as *stalled at a prompt* until proven otherwise. Look
  at the tab — or ask the user to — rather than waiting longer. Waiting is the one response
  that is always wrong.

Defaults, the global fleet cap, the per-box `maxConcurrent` ceilings and the per-run
timeout all live in `.claude/fleet/machines.json`. Raising a cap is a §7 stop condition —
take it to the user with the reason, do not edit the registry mid-mission.

**The ceilings are external, and that is the point.** An async dispatch starts a watchdog
process that kills the run after `timeoutMinutes` **only if it has produced no report** —
so a runaway is bounded, while a finished `tty` worker whose tab you are still reading is
left alone. `maxBudgetUsd` is a hard spend cut-off that applies to `-Mode bg` only, because
`--max-budget-usd` requires `--print`; it terminates the session from outside and is never
shown to the worker. None of these are things the worker knows about, and none of them
should become things it is told.

**Remote Control is not dispatched with this script, and no machine uses it now.** It
survives only as a manual fallback if box3's ssh transport ever breaks: `ListAgents` to
find the session, then `SendMessage` with the brief. The script refuses `rc` on purpose
rather than pretending to have started something. Remember what you give up on that path —
RC has exactly one verb, **prompt**, so model, effort and stop are all unavailable and §2
becomes advice nothing enforces.

## 5. Collect — and the rule that protects your context

**Every worker has its own context window, entirely separate from yours.** A worker is a
separate `claude` process — a separate tab on box1, or a separate process on box3 — with
its own session and its own token budget. Nothing of its transcript is in your context and
nothing of yours is in its. The **only** thing that crosses back is the report object.
That separation is the whole point of the fleet, and it is one command away from being
thrown away:

**Read reports. Never read worker logs.** `fleet.ps1 -Action report` prints the parsed
digest and a path. The path is for the user, or for a later targeted `grep` when a report
says something specific is wrong. Opening `raw.json` or `report.json` to "see what
happened" imports an entire worker session into your context and is the failure this
design exists to prevent. The same goes for a `tty` worker's terminal tab: it is the
user's window onto the work, not an input to yours — never read the scrollback back.

Per report:

**A scope report carries a `cards` array — read it, and tick per card.** `-Action report`
prints one line per card with its own status, sha and test result. That array is the part
you act on: the top-level `status` is a rollup and cannot tell you which card to tick.

- **`done`** → verify the claim is *internally* coherent (status `done` with `tests: fail`
  is a contradiction — treat it as `failed` and re-dispatch; so is a top-level `done` over
  a `cards` array containing anything not `done`). Then tick the boxes in `tasks.md`
  **yourself, card by card**. Workers never edit that file.
- **`partial`** → this is the normal shape of a stopped scope, not a failure. The `done`
  cards are real, committed, and get ticked. Exactly one card will be `blocked` or `failed`
  and the rest `not-started`: deal with the stopping card, then re-dispatch **the remainder
  of the track as a new scope** — not the whole track again, which would redo committed
  work and produce a second commit per card.
- **`blocked` / `needsDecision`** → this is your job arriving. Decide it. If it is a CLAUDE.md
  §7 stop condition that is genuinely the user's or the professor's, take it to the user
  with the worker's exact wording — do not soften it into a guess.
- **`failed`** → read `failureTail` from the report. That is normally enough. Re-dispatch one
  tier up with the failure quoted in the brief.
- **`scopeCreepRefused`** → record it in the ledger. It is usually a real next card.

## 6. Integrate

Workers commit locally and never push. You handle integration, in one place, once.

**A remote worker's commits are on ITS box until you collect them.** This is the step that
is easy to skip and impossible to skip safely: the report says `done` with a real sha, but
that sha exists only on box3. Collect first, then look at the log.

> **Folder:** repo root · **USB:** not required · **Shell:** normal

```powershell
# bring box3's commits here. Lands them on refs/remotes/box3/master, then fast-forwards
# this box when it is clean. A DIVERGED box is refused -- that is an integration decision.
pwsh ./scripts/fleet/fleet.ps1 -Action collect

git -C . log --oneline -n 10
git -C . status --short

# and put the boxes back on one line before the next dispatch
pwsh ./scripts/fleet/fleet.ps1 -Action sync
```

**`sync` will refuse a box that is holding uncollected commits**, and say so — it is the
other direction of the same pipe, and fast-forwarding over a worker's only copy of its work
is not something it may do. If you see that warning, the answer is `-Action collect`, never
a harder reset.

Then run the guardrails and the tests **once, yourself**, across the merged result. Green
workers do not imply a green tree — that is precisely what parallel work breaks.

**Pushing is an outward-facing action: confirm with the user before the first `git push`,
every session.** Do not push on a worker's say-so.

## 7. Stop conditions — for you

Stop and hand back to the user when:

1. A worker reports a CLAUDE.md §7 condition that is genuinely a user or professor call.
2. Two workers' reports contradict each other about the same file or rule.
3. The merged tree fails guardrails or tests and the cause is not in a single card.
4. You are about to raise the global fleet cap or a box's `maxConcurrent` ceiling, or push.
5. **You have collected every dispatched report and the ledger has no open scopes.** That is
   done. Say so and stop — do not look for more work to dispatch.

Point 5 is the one that gets skipped. An orchestrator with idle capacity is not a problem
to solve.

## The fleet is two laptops

`box1` (this machine, orchestrator and the only XAMPP/MariaDB host) and `box3` (the worker
box, ssh). Both are laptops on the same hotspot, so **the fleet is whole away from home** —
at school, on battery, anywhere. That property was bought by retiring the desktop; do not
give it back without saying so.

**box2 (DESKTOP-G83CCSH) was retired 2026-08-23** and is gone from the registry. Two
reasons, and the first is the one that mattered: it was cabled to the house router with no
working wireless, so the fleet silently shrank to one machine the moment box1 left the
house — exactly when a demo needs it most. Second, its `ssh` transport was never solvable
(`sshd.exe` dies at the Windows loader, `0xC0000135`, with every health check clean), so it
was permanently stuck on `rc` and its model and effort could never be set. What was learned
on that box — including the DISM and sshd findings below, all of which came from it — is
kept in the "box2, retired" section of `.claude/fleet/mission.md`. Read that before
spending an afternoon on any desktop that rejoins later.

## Provisioning a worker box

**`ssh` is the transport in use** (box3, proven end to end 2026-08-23). It is the one that
gives you every verb: start a session, set its model and effort, run it headless or as a
visible tab over `ssh -t`, and stop it. **Remote Control is the fallback, not the plan** —
one verb, no model control.

A box is ready only when **all seven** of these hold. Six of the seven have bitten already:

1. **`sshd` running there and reachable by key from here.** Verify with
   `pwsh ./scripts/fleet/fleet.ps1 -Action doctor`, not with a Bash `ssh` — see the NTFS
   ACL trap below, which makes a key work from Git Bash and fail from the dispatcher.
2. **`claude` on that machine's PATH under a non-interactive SSH login**, which is a
   stricter test than it working when you sit at the box. Doctor's `ssh + claude` row is
   exactly that probe.
3. **The repo cloned there, and the registry's `repo` path pointing at it.** A worker
   session's cwd must be inside the checkout: from a user-profile or drive root no project
   `CLAUDE.md` loads — that session does not know VB-only, the MariaDB 10.4 dialect limits,
   or that the ledgers are append-only — and `/worker` does not resolve at all.
4. **SDK exactly `10.0.301`.** `global.json` sets `rollForward: disable`, so a box with only
   a newer SDK does not build differently — it refuses to restore. Install side-by-side;
   never remove the newer one.
5. **`git config user.name` / `user.email` set on that machine.** Box1 sets these at the
   *repo* level, so a clone does not inherit them and git refuses to commit without them.
   Never invent an identity — it lands on every commit; confirm it with the user.
6. **The repo cloned there by a human, once, from a real terminal on that box.** A
   private-repo clone needs credentials Git Credential Manager can only obtain
   interactively, so a dispatched session cannot do it: it dies on `fatal: Cannot prompt
   because user interactivity has been disabled` before any browser opens. Never hand a box
   a token in a brief or a `SendMessage`, and never ask one to install `gh` and
   authenticate itself. **The clone precedes the transport, not the other way round.**

   **After that first clone the box needs no working credential at all**, and the older
   version of this rule — "sign in once and every later headless fetch works" — is wrong
   for `ssh`. It was true of Remote Control, whose session is an interactive desktop logon.
   GCM's default `wincredman` store is bound to that logon, so **sshd's network logon cannot
   read it**: a `git pull` over ssh fails with `Unable to persist credentials with the
   'wincredman' credential store` and then a username prompt against a `/dev/tty` that does
   not exist. `-Action sync` sidesteps the whole problem by moving a **git bundle** over
   `scp` — no credential, no GitHub reachability, no remote shell quoting. Workers commit
   locally and never push, so nothing else on that box needs to authenticate.

7. **`bypassPermissions` accepted once on that box, by a human, in a real terminal.**
   Claude Code shows a one-time "Yes, I accept" acknowledgement the first time a session
   starts in that mode. A dispatched worker hits it before it does anything, and then sits
   at flat CPU holding a fleet slot — indistinguishable from working. It is **not** a
   per-command prompt and it does not come back: SMOKE-05 stalled on it, and SMOKE-06 ran
   clean end to end 80 seconds after dispatch. Get it accepted once during provisioning,
   not on the first real card.

Then run, on that machine:

> **Folder:** anywhere · **USB:** not required · **Shell:** normal

```powershell
pwsh <repo>/scripts/fleet/bootstrap-worker-machine.ps1 -RepoUrl <url> -RepoPath C:/dev/Hardware_Merchandising_System
```

It clones or fetches, **installs the L4 git pre-commit hook** — `.git/hooks` does not travel
with a clone, so every new box reopens the exact gap CLAUDE.md §11 says L4 exists to cover —
runs the guardrails, and prints `READY` or `NOT READY` with the failing rows. Record the
result in `machines.json`, set `enabled: true` and a `maxConcurrent` ceiling, then confirm
with `fleet.ps1 -Action doctor`.

### Wiring the `ssh` transport on a Windows box

Four things bite, in this order. All four were paid for once already:

1. **The PowerShell servicing cmdlets may be broken while DISM itself is fine.**
   `Get-WindowsCapability` failing with `Class not registered` is a broken COM registration
   in the PS module, *not* an unhealthy CBS stack. Check with `dism /online /get-capabilities`
   before concluding the box needs a Windows repair — that repair is not a fleet task and it
   will swallow the session that starts it.
2. **Always pass `/norestart /quiet` to `dism /online /add-capability`.** Without them it ends
   at an interactive "restart now?" prompt — which a session with no TTY cannot answer, and
   killing it there leaves the service half-registered and costs two reboots to unpick.
   **Then check `Get-Service sshd` immediately:** on a healthy box it is already registered and
   no reboot is needed at all. A service that does *not* appear after a clean install is itself
   the diagnostic — that box is damaged, and rebooting to chase it is how you spend an afternoon.
3. **If the box's account is a local Administrator, sshd reads
   `C:\ProgramData\ssh\administrators_authorized_keys` and ignores `~/.ssh/authorized_keys`
   entirely.** ASCII, no BOM, ACLs `/inheritance:r` granting only `Administrators` and `SYSTEM`.
   The obvious placement produces a setup that looks correct and rejects every login.
4. **Lock the private key's NTFS ACLs on the orchestrator box, not just its `chmod`.**
   `dispatch-worker.ps1` runs `ssh` from pwsh — the System32 client, which refuses an
   over-permissive key. Git Bash `chmod` does not touch NTFS ACLs, so the key works from Bash
   and fails from the dispatcher, which reads as a key fault rather than a permissions one.

**When to stop and call it a machine fault.** `sshd` failing to start says nothing useful
through the SCM — error 1053 ("did not respond in a timely fashion") is what Windows says
for almost any startup failure. Run `sshd.exe -t` directly for the real one, and compare
against `ssh.exe -V` from the same folder: **if the client runs and the server dies at
`0xC0000135` (STATUS_DLL_NOT_FOUND), the payload is fine and the machine is not.** Stop
there, even when every other health check passes — on box2, `ScanHealth`, `sfc`, signature
validation and the DISM CLI all came back clean, and the loader failure was the only real
symptom and still fatal. Chasing it means a Windows repair: open-ended, may not fix the
thing you wanted, and the box doing the repairing is the broken one. Report it to the user
as a machine problem, not a setup step.

**Tailscale is the better network path than a LAN firewall rule** when the boxes are not
always on the same network — MagicDNS names stay valid, and sshd can be scoped to the
orchestrator's tailnet IP alone rather than to a subnet. **Tailscale's own SSH server is
Linux-only**: on Windows it does nothing, and the box still needs OpenSSH Server. Keep
`sshTarget` a bare `Host` alias and let `~/.ssh/config` hold the user, identity file, and
hostname — the fleet registry should not carry credentials-adjacent detail. Not needed
today: both laptops share the hotspot, and box3's firewall rule is scoped to the
`192.168.100.0/24` **subnet** rather than to box1's current lease, because DHCP will move it
and a rule pinned to one address fails silently later.

**Address a box by hostname, never by a hand-set static IP** — a settled decision here, and a
DHCP lease handing the same address back is exactly the trap that makes a static IP look
like it works. Hostnames also do not indicate form factor: `DESKTOP-F5LK8MA` is physically a
laptop. Read `formFactor` from the registry rather than guessing from a name.
