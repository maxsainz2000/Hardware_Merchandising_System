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

## Status values

`planned` → `dispatched` → `done` \| `blocked` \| `failed`

A card is `done` only once **you** have read its report, found it internally coherent, and
ticked its boxes in `tasks.md`. A worker's own `done` is a claim, not the fact.

## Open questions for the user

Anything a worker raised as `needsDecision` that you could not settle. Empty is the goal.

_(none)_
