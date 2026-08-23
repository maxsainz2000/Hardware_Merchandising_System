---
name: orchestrate
description: Run a fleet of Claude Code workers across this machine and box2 to execute several task cards. Use for "orchestrate", "dispatch workers", "run tasks in parallel", "use the laptop too", or when a phase has independent cards worth splitting.
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
- the user explicitly asked for the fleet or for box2.

One card, alone, on this machine, is `/task` — not this skill. Say so and switch.

## 1. Plan before you spawn

1. **Read `tasks.md`** and pick the cards. Nothing else — do not read the source tree to
   "understand context" first. That is the single biggest context leak available to you.
2. **Prove independence.** Two cards may run in parallel only if their file sets are
   disjoint. Shared file → sequence them. When unsure, sequence: a merge conflict across
   two machines costs far more than a serial run.
3. **Place each card.** `box1` holds XAMPP/MariaDB — **every card touching the database,
   the API at runtime, or a migration runs on box1.** box2 takes work that needs no
   database: evidence capture, scripts, docs, client-only XAML, guardrail runs.
4. **Write the ledger** to `.claude/fleet/mission.md` before dispatching — card, machine,
   model, scope boundary, one line each. On disk, not in your context. It is what lets you
   resume after a compaction without re-deriving the plan.

## 2. Choose model and effort per card — deliberately

This is a real decision, not a default to skip past. Starting everything on `opus/high`
wastes budget; starting everything on `haiku/low` produces work you rewrite.

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
# check transports before trusting any of them
pwsh ./scripts/fleet/fleet.ps1 -Action doctor

# dispatch (async; returns immediately with a handle)
pwsh ./scripts/fleet/dispatch-worker.ps1 -TaskId P2-03 -Machine box1 `
     -Model sonnet -Effort medium -BriefFile .claude/fleet/briefs/P2-03.md

# a visible terminal instead, when the user wants to watch it work
pwsh ./scripts/fleet/dispatch-worker.ps1 -TaskId P2-03 -Mode tty

# collect, stop, survey
pwsh ./scripts/fleet/fleet.ps1 -Action list
pwsh ./scripts/fleet/fleet.ps1 -Action report -TaskId P2-03
pwsh ./scripts/fleet/fleet.ps1 -Action stop   -TaskId P2-03      # or -All
```

Defaults, the fleet cap (3 live), and the per-worker budget live in
`.claude/fleet/machines.json`. Raise the cap only with a reason.

**Remote Control machines are not dispatched with this script.** An RC session is driven
by messages: `ListAgents` to find it, then `SendMessage` with the brief. The script refuses
RC on purpose rather than pretending to have started something.

## 5. Collect — and the rule that protects your context

**Read reports. Never read worker logs.** `fleet.ps1 -Action report` prints the parsed
digest and a path. The path is for the user, or for a later targeted `grep` when a report
says something specific is wrong. Opening `raw.json` to "see what happened" imports an
entire worker session into your context and is the failure this design exists to prevent.

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
4. You are about to raise the fleet cap, switch a worker to `bypassPermissions`, or push.
5. **You have collected every dispatched report and the ledger has no open cards.** That is
   done. Say so and stop — do not look for more work to dispatch.

Point 5 is the one that gets skipped. An orchestrator with idle capacity is not a problem
to solve.

## Provisioning a worker box

**Remote Control is the transport in use** (box2, box3). SSH stays supported and is better
when it is available — headless, structured reports, no interactive session to babysit —
but it needs `sshd` on that machine and LAN reachability, neither of which the current
fleet has.

A box is ready only when **all six** of these hold. Five of the six have bitten already:

1. **`claude --remote-control` running there**, signed into the same Anthropic account, and
   **visibly listed by `ListAgents` here**. A named session that never connected looks
   exactly like a working one until a brief vanishes into it.
2. **This session has Remote Control on too.** `--remote-control` is a *startup* flag; a
   session started without it sees no peers at all. `/rc` retrofits it, and only the user
   can type that.
3. **The session's cwd is the repo checkout, not a user-profile or drive root.** From a
   root, no project `CLAUDE.md` loads — that session does not know VB-only, the MariaDB
   10.4 dialect limits, or that the ledgers are append-only — and `/worker` does not
   resolve at all. A root-dir session can bootstrap its machine and nothing more.
4. **SDK exactly `10.0.301`.** `global.json` sets `rollForward: disable`, so a box with only
   a newer SDK does not build differently — it refuses to restore. Install side-by-side;
   never remove the newer one.
5. **`git config user.name` / `user.email` set on that machine.** Box1 sets these at the
   *repo* level, so a clone does not inherit them and git refuses to commit without them.
   Never invent an identity — it lands on every commit; confirm it with the user.
6. **Git credentials already cached on that machine, established out of band.** An RC
   session has **no TTY** — Git Credential Manager cannot prompt in one, so a private-repo
   clone dies on `fatal: Cannot prompt because user interactivity has been disabled` before
   any browser opens. There is no flag that fixes this and no workaround a worker may take:
   never hand a box a token over `SendMessage`, and never ask one to install `gh` and
   authenticate itself. The user signs in **once, from a real terminal on that machine**;
   Windows Credential Manager caches it and every later RC fetch works headlessly. Read the
   provisioning order off this: **the clone precedes the RC session, not the other way round.**

Then run, on that machine:

> **Folder:** anywhere · **USB:** not required · **Shell:** normal

```powershell
pwsh <repo>/scripts/fleet/bootstrap-worker-machine.ps1 -RepoUrl <url> -RepoPath C:/dev/Hardware_Merchandising_System
```

It clones or fetches, **installs the L4 git pre-commit hook** — `.git/hooks` does not travel
with a clone, so every new box reopens the exact gap CLAUDE.md §11 says L4 exists to cover —
runs the guardrails, and prints `READY` or `NOT READY` with the failing rows. Record the
result in `machines.json`, set `enabled: true`, confirm with `fleet.ps1 -Action doctor`.

### Wiring the `ssh` transport on a Windows box

`rc` gives you exactly one verb: **prompt**. You cannot start a session, set its model or
effort, or stop it. `ssh` gives you all of them, and `dispatch-worker.ps1` already has the
branch — so a box on `rc` is a box where the model/effort table in §2 is advice nothing
enforces. Four things bite, in this order:

1. **The PowerShell servicing cmdlets may be broken while DISM itself is fine.**
   `Get-WindowsCapability` failing with `Class not registered` is a broken COM registration
   in the PS module, *not* an unhealthy CBS stack. Check with `dism /online /get-capabilities`
   before concluding the box needs a Windows repair — that repair is not a fleet task and it
   will swallow the session that starts it.
2. **`dism /online /add-capability` for OpenSSH.Server needs a reboot before the service
   exists.** It exits 0, reports `State = Installed`, and `Start-Service sshd` still fails with
   "service was not found". Plan the reboot into the provisioning window; it also kills that
   box's RC session, which then has to be restarted inside the checkout again.
3. **If the box's account is a local Administrator, sshd reads
   `C:\ProgramData\ssh\administrators_authorized_keys` and ignores `~/.ssh/authorized_keys`
   entirely.** ASCII, no BOM, ACLs `/inheritance:r` granting only `Administrators` and `SYSTEM`.
   The obvious placement produces a setup that looks correct and rejects every login.
4. **Lock the private key's NTFS ACLs on the orchestrator box, not just its `chmod`.**
   `dispatch-worker.ps1` runs `ssh` from pwsh — the System32 client, which refuses an
   over-permissive key. Git Bash `chmod` does not touch NTFS ACLs, so the key works from Bash
   and fails from the dispatcher, which reads as a key fault rather than a permissions one.

**Tailscale is the better network path than a LAN firewall rule** when the boxes are not
always on the same network — MagicDNS names stay valid, and sshd can be scoped to the
orchestrator's tailnet IP alone rather than to a subnet. **Tailscale's own SSH server is
Linux-only**: on Windows it does nothing, and the box still needs OpenSSH Server. Keep
`sshTarget` a bare `Host` alias and let `~/.ssh/config` hold the user, identity file, and
hostname — the fleet registry should not carry credentials-adjacent detail.

**Address a box by hostname, never by a hand-set static IP** — a settled decision here, and a
DHCP lease handing the same address back is exactly the trap that makes a static IP look
like it works. Hostnames also do not indicate form factor: `DESKTOP-F5LK8MA` is physically a
laptop. Read `formFactor` from the registry rather than guessing from a name.

**Driving an `rc` box:** `ListAgents` → `SendMessage`. `dispatch-worker.ps1` refuses `rc` on
purpose. RC session names change on every restart, so confirm against a live `ListAgents`
rather than trusting `rcSessionName` in the registry.
