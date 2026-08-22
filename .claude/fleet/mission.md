# Mission ledger

The orchestrator's working state, kept **on disk on purpose**. This file is what lets an
orchestrator session survive a compaction without re-deriving the plan, and it is why the
plan does not have to live in context.

One row per card. Update it when you dispatch and when you collect. Delete rows when a
mission closes — this is a worksheet, not a history. `git log` is the history.

| Card | Machine | Model/Effort | Scope boundary | Status | Note |
|---|---|---|---|---|---|
| _(none open)_ | | | | | |

## Status values

`planned` → `dispatched` → `done` \| `blocked` \| `failed`

A card is `done` only once **you** have read its report, found it internally coherent, and
ticked its boxes in `tasks.md`. A worker's own `done` is a claim, not the fact.

## Open questions for the user

Anything a worker raised as `needsDecision` that you could not settle. Empty is the goal.

_(none)_
