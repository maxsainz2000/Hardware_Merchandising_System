# Mission ledger

The orchestrator's working state, kept **on disk on purpose**. This file is what lets an
orchestrator session survive a compaction without re-deriving the plan, and it is why the
plan does not have to live in context.

One row per card. Update it when you dispatch and when you collect. Delete rows when a
mission closes — this is a worksheet, not a history. `git log` is the history.

| Card | Machine | Model/Effort | Scope boundary | Status | Note |
|---|---|---|---|---|---|
| _(none open)_ | | | | | Phase 2 not started; fleet setup in progress |

## Fleet state — 2026-08-23 · fleet complete

| Box | Machine | Transport | Ready | Notes |
|---|---|---|---|---|
| box1 | LAPTOP-3HH6OHHE (laptop) | local | yes | Orchestrator host. Only box with XAMPP/MariaDB. |
| box2 | DESKTOP-G83CCSH (desktop, 200.9 GB) | rc | **yes** | READY, no failing rows. Owns `evidence/`, `scripts/`. |
| box3 | DESKTOP-F5LK8MA (**laptop**, 57.8 GB) | rc | **yes** | READY, no failing rows. Owns `documentations/`. Tightest disk. |

All three boxes are provisioned and `enabled: true`. Both secondaries verified on ground before
the bootstrap ran: cwd inside the checkout, HEAD at `05d73d6`, project CLAUDE.md loaded. The
bootstrap installed the **L4 pre-commit hook** on each clone — `.git/hooks` does not travel with
a clone, so both fresh checkouts had that gap open until the run.

What provisioning cost, recorded so it is not rediscovered: the private-repo clone cannot be
driven from an RC session at all (no TTY, so Git Credential Manager cannot prompt — see readiness
condition 6). The user signs in once from a real terminal per box; the cached credential makes
every later RC fetch headless. **The clone precedes the RC session.**

Neither secondary has XAMPP/MariaDB, and that is correct, not missing. ADR-013 pins the three DB
identities to box1, so every card touching the database, a migration, or the API at runtime is
placed on box1 no matter which box is idle.

RC session names change on every restart. The names below are hints — confirm against a live
`ListAgents` before dispatching. As of 2026-08-23: box2 `desktop-g83ccsh-joyful-bubble`,
box3 `desktop-f5lk8ma-rosy-flute`.

## SSH transport — box3 LIVE · box2 closed unsolved

Why it mattered: `rc` gives the orchestrator exactly one verb, **prompt**. It cannot start a
session, set its model or effort, or stop one — so on an `rc` box the model/effort table in
`/orchestrate` §2 is advice nothing enforces. That was the gap between what the user asked for
and what existed.

**box3: proven end to end, 2026-08-23.** `SMOKE-02` dispatched from box1 over SSH at
`haiku`/`low`, worker started on box3, structured report back, $0.069, compact digest — no
transcript entered the orchestrator's context. `fleet.ps1 -Action doctor` reports
`[box3] transport=ssh enabled=True · ssh + claude : OK`.

Shape of it: `sshTarget` is the ssh_config **Host alias** `box3`; user, identity file and hostname
live in box1's `~/.ssh/config`, so the fleet registry carries nothing credentials-adjacent. Key-only
auth, `PasswordAuthentication no`, firewall scoped to `192.168.100.0/24` — the subnet rather than
box1's current IP, because DHCP will move it and a rule pinned to one lease fails silently later.
No Tailscale needed: both boxes share the hotspot. Tailscale remains a later one-line upgrade for
when they do not.

**box2: closed unsolved.** `sshd.exe` dies at the Windows loader (`0xC0000135`) while ScanHealth,
sfc, signature validation, the import scan and the DISM CLI all come back clean. One real symptom,
cause unknown, cheap local fixes exhausted. It stays a working fleet box over `rc`. Untried and
free if ever wanted: `dism remove-capability` then re-add, sourced from its own clean component
store. Nothing was ever applied to box2 — no firewall rule, no key, no `sshd_config` change — so
there is nothing to revert.

What the two attempts cost, and what the skill now says so it is not paid twice: pass
`/norestart /quiet` on the first DISM call and treat a *missing* `sshd` service as a diagnosis
rather than a reboot; `Class not registered` predicts nothing; the SCM's 1053 is noise and
`sshd.exe -t` gives the real error; an Administrator account makes sshd ignore
`~/.ssh/authorized_keys`; NTFS ACLs on the private key matter because pwsh uses the System32
client; and dumping a binary's imports to `Test-Path` them cannot work, because ApiSet contracts
are virtual on every healthy box.

## Status values

`planned` → `dispatched` → `done` \| `blocked` \| `failed`

A card is `done` only once **you** have read its report, found it internally coherent, and
ticked its boxes in `tasks.md`. A worker's own `done` is a claim, not the fact.

## Open questions for the user

Anything a worker raised as `needsDecision` that you could not settle. Empty is the goal.

_(none)_
