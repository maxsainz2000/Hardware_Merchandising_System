# Mission ledger

The orchestrator's working state, kept **on disk on purpose**. This file is what lets an
orchestrator session survive a compaction without re-deriving the plan, and it is why the
plan does not have to live in context.

One row per card. Update it when you dispatch and when you collect. Delete rows when a
mission closes — this is a worksheet, not a history. `git log` is the history.

| Card | Machine | Model/Effort | Scope boundary | Status | Note |
|---|---|---|---|---|---|
| _(none open)_ | | | | | Phase 2 not started; fleet setup in progress |

## Fleet state — 2026-08-23

| Box | Machine | Transport | Ready | Blocked on |
|---|---|---|---|---|
| box1 | LAPTOP-3HH6OHHE (laptop) | local | yes | — |
| box2 | DESKTOP-G83CCSH (desktop, 200.9 GB) | rc | no | clone → bootstrap verdict → restart session inside checkout |
| box3 | DESKTOP-F5LK8MA (**laptop**, 57.8 GB) | rc | no | same three |

Done: SDK 10.0.301 installed side-by-side on box2/box3 and **verified on both today**;
`global.json` pins it with `rollForward: disable`; git history scanned — no real credentials,
only test fixtures; `gh` authenticated on box1; **private remote created and `master` pushed**
(2026-08-23) — the clone is what every remaining step hung on.

Open: **the clone, blocked on authentication.** Global git identity is now set on both boxes
(`Max Sainz <maxsainz2000@gmail.com>`, confirmed by the user) — that step passed. The clone
itself failed identically on box2 and box3:

    fatal: Cannot prompt because user interactivity has been disabled.
    fatal: could not read Username for 'https://github.com': terminal prompts disabled

An RC session has no TTY, so Git Credential Manager cannot prompt at all — no browser, no 403.
This is now readiness condition 6 in the `/orchestrate` skill. The user is signing in once from
a real terminal on each box; after that the cached credential makes every RC fetch headless.
Then: `bootstrap-worker-machine.ps1` verdict, then the RC session restarted **inside** the
checkout — the step only the user can take, and the reason a green bootstrap alone does not
flip a box to `enabled: true`.

Note: RC session names change on every restart. The names in `machines.json` are hints; confirm
against a live `ListAgents` before dispatching. Today's: box2 `desktop-g83ccsh-scalable-candle`,
box3 `desktop-f5lk8ma-jazzy-karp`.

## Status values

`planned` → `dispatched` → `done` \| `blocked` \| `failed`

A card is `done` only once **you** have read its report, found it internally coherent, and
ticked its boxes in `tasks.md`. A worker's own `done` is a claim, not the fact.

## Open questions for the user

Anything a worker raised as `needsDecision` that you could not settle. Empty is the goal.

_(none)_
