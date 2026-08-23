---
name: orchestrate
description: Run a fleet of Claude Code workers across this laptop and box3 to execute several task cards. Use for "orchestrate", "dispatch workers", "run tasks in parallel", "use the other laptop", or when a phase has independent cards worth splitting.
argument-hint: "[task-ids or goal]"
arguments: [goal]
allowed-tools: Read, Grep, Glob, Edit, Write, Bash(pwsh *), Bash(git *), Bash(ssh *), ListAgents, SendMessage, AskUserQuestion
---

# Orchestrator — `$0`

You do the thinking. Workers do the typing. Your scarcest resource is **your own context**,
not their time — protect it deliberately, because an orchestrator that has read three
transcripts is worse at deciding than one that has read none.

## 0. Before anything: is this even an orchestration?

Dispatching costs a worker startup, a brief, and a report. **If the whole job is under
roughly two tool calls, just do it yourself.** Orchestrate when:

- there are **≥2 genuinely independent cards** (disjoint file sets), **or**
- one card is long-running and you have other work to place beside it, **or**
- the user explicitly asked for the fleet or for box3.

One card, alone, on this machine, is `/task` — not this skill. Say so and switch.

## 1. Plan before you spawn

1. **Read `tasks.md`** and pick the cards. Nothing else — do not read the source tree to
   "understand context" first. That is the single biggest context leak available to you.
2. **Prove independence.** Two cards may run in parallel only if their file sets are
   disjoint. Shared file → sequence them. When unsure, sequence: a merge conflict across
   two machines costs far more than a serial run.
3. **Place each card.** The fleet is two laptops, and the split is one rule:
   `box1` holds XAMPP/MariaDB, so **every card touching the database, the API at runtime,
   or a migration runs on box1** (ADR-013 pins the three DB identities to that host).
   **box3 takes everything else** — evidence capture, scripts, docs, client-only
   XAML/VB, unit tests that need no database, guardrail runs. It is the worker box, not a
   documentation box; do not leave it idle because a card "feels like box1 work".
4. **Decide how many.** See §2 — the count is your call, bounded by each box's
   `maxConcurrent` in the registry (box1 2, box3 4). The dispatcher enforces the ceiling;
   it does not choose the number for you.
5. **Write the ledger** to `.claude/fleet/mission.md` before dispatching — card, machine,
   model, scope boundary, one line each. On disk, not in your context. It is what lets you
   resume after a compaction without re-deriving the plan.

## 2. Choose model, effort, and how many — deliberately

Three decisions, all yours, none of them a default to skip past. Starting everything on
`opus/high` wastes budget; starting everything on `haiku/low` produces work you rewrite.

| Card looks like | Model | Effort |
|---|---|---|
| Mechanical, fully specified — boilerplate, evidence capture, a scripted run | `haiku` | `low` |
| Ordinary implementation of a card that is already well specified | `sonnet` | `medium` |
| Business rules, transactions, concurrency, money/decimal handling, auth, SQL grants | `opus` | `high` |
| A card that already came back `blocked` once, or where the design is genuinely open | `opus` | `xhigh` |

**Escalate on evidence, never pre-emptively.** Dispatch at the tier the card deserves; if
the report comes back `blocked` or `failed` on something real, re-dispatch that card one
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

Two well-placed `sonnet/medium` workers beat four `haiku/low` ones on cards that were never
independent. When in doubt, dispatch fewer and dispatch again — the second wave costs a
startup, and a merge conflict across two machines costs an afternoon.

## 3. Write the brief

A brief is a **boundary**, not a description. Every brief states, in this order:

1. **Card ID** and the spec sections it cites.
2. **Exact scope** — the files this worker may touch. Name them.
3. **Acceptance checks**, copied from the card. Not paraphrased.
4. **What NOT to do** — the adjacent thing they will be tempted by. Be explicit; this line
   prevents more damage than the other four combined.
5. **Evidence path**, if the card names one.

Never inline CLAUDE.md or the worker contract into a brief. Workers load both from disk.

## 4. Dispatch

> **Folder:** repo root · **USB:** not required · **Shell:** normal (elevation not needed)

```powershell
# check transports and each box's remaining headroom before trusting any of them
pwsh ./scripts/fleet/fleet.ps1 -Action doctor

# bring the worker boxes up to this box's HEAD -- do this BEFORE dispatching real work
pwsh ./scripts/fleet/fleet.ps1 -Action sync

# dispatch (async; returns immediately with a handle)
pwsh ./scripts/fleet/dispatch-worker.ps1 -TaskId P2-03 -Machine box1 `
     -Model sonnet -Effort medium -BriefFile .claude/fleet/briefs/P2-03.md

# the worker box - same command, different -Machine. `ssh -t` opens its tab on YOUR screen
pwsh ./scripts/fleet/dispatch-worker.ps1 -TaskId P2-04 -Machine box3 `
     -Model sonnet -Effort medium -BriefFile .claude/fleet/briefs/P2-04.md

# headless instead, when nobody is going to watch it anyway
pwsh ./scripts/fleet/dispatch-worker.ps1 -TaskId P2-05 -Machine box3 -Mode bg

# collect, stop, survey
pwsh ./scripts/fleet/fleet.ps1 -Action list
pwsh ./scripts/fleet/fleet.ps1 -Action report -TaskId P2-03
pwsh ./scripts/fleet/fleet.ps1 -Action stop   -TaskId P2-03      # or -All
```

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

Defaults, the global fleet cap, the per-box `maxConcurrent` ceilings and the per-worker
budget all live in `.claude/fleet/machines.json`. Raising either cap is a §7 stop
condition — take it to the user with the reason, do not edit the registry mid-mission.

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

- **`done`** → verify the claim is *internally* coherent (status `done` with `tests: fail`
  is a contradiction — treat it as `failed` and re-dispatch). Then tick the card's boxes in
  `tasks.md` **yourself**. Workers never edit that file.
- **`blocked` / `needsDecision`** → this is your job arriving. Decide it. If it is a CLAUDE.md
  §7 stop condition that is genuinely the user's or the professor's, take it to the user
  with the worker's exact wording — do not soften it into a guess.
- **`failed`** → read `failureTail` from the report. That is normally enough. Re-dispatch one
  tier up with the failure quoted in the brief.
- **`scopeCreepRefused`** → record it in the ledger. It is usually a real next card.

## 6. Integrate

Workers commit locally and never push. You handle integration, in one place, once:

> **Folder:** repo root · **USB:** not required · **Shell:** normal

```powershell
git -C . log --oneline -n 10
git -C . status --short
```

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
5. **You have collected every dispatched report and the ledger has no open cards.** That is
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
