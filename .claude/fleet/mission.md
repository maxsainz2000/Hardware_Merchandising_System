# Mission ledger

The orchestrator's working state, kept **on disk on purpose**. This file is what lets an
orchestrator session survive a compaction without re-deriving the plan, and it is why the
plan does not have to live in context.

One row per card. Update it when you dispatch and when you collect. Delete rows when a
mission closes — this is a worksheet, not a history. `git log` is the history.

| Card | Machine | Model/Effort | Scope boundary | Status | Note |
|---|---|---|---|---|---|
| _(none open)_ | | | | | Phase 2 not started; fleet setup in progress |

## Fleet state — 2026-08-22

| Box | Machine | Transport | Ready | Blocked on |
|---|---|---|---|---|
| box1 | LAPTOP-3HH6OHHE (laptop) | local | yes | — |
| box2 | DESKTOP-G83CCSH (desktop, 200.9 GB) | rc | no | repo clone; restart session inside checkout; git identity |
| box3 | DESKTOP-F5LK8MA (**laptop**, 57.8 GB) | rc | no | same three |

Done: SDK 10.0.301 installed side-by-side on box2/box3; `global.json` pins it with
`rollForward: disable`; git history scanned — no real credentials, only test fixtures.

Open: GitHub private remote not yet created (`gh` installed on box1, not authenticated).

## Status values

`planned` → `dispatched` → `done` \| `blocked` \| `failed`

A card is `done` only once **you** have read its report, found it internally coherent, and
ticked its boxes in `tasks.md`. A worker's own `done` is a claim, not the fact.

## Open questions for the user

Anything a worker raised as `needsDecision` that you could not settle. Empty is the goal.

_(none)_
