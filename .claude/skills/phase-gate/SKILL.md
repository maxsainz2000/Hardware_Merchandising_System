---
name: phase-gate
description: Check whether the current phase is complete and may be exited. Use for "phase exit", "is Phase 0 done", "check the gate", "phase exit review", or "regenerate tasks.md for the next phase".
allowed-tools: Read, Grep, Glob, Write, Edit
---

# Phase exit review

Decide whether the current phase may be closed. The default answer is *no* until evidence says
otherwise. This review exists to be failed — a gate that always passes is decoration.

## Procedure

1. **Identify the current phase** from the heading in `tasks.md`, and open that phase's section
   in `plan.md`.
2. **List every exit criterion** for the phase — from `plan.md` (the phase's *Exit criteria*
   line and its gate section) and from the `Phase N exit gate` checklist in `tasks.md`.
   Include the per-task `Evidence:` paths, since a task's evidence is part of the gate.
3. **Locate the artifact for each criterion** under `evidence/phase-N/`. Check the file actually
   exists and is non-empty. Report by name every criterion with no artifact, and every artifact
   path referenced by a card that is missing from disk.
4. **Check the ADR.** Read `docs/adr.md`. Any entry this phase was supposed to resolve that is
   still `PENDING` is a gate failure. Name the entry and the task that owed it.
5. **Verify a clean-clone build succeeds.** For Phase 0 there may be no `src/` tree yet — say so
   explicitly rather than treating "nothing to build" as a pass.
6. **Report a verdict: PASS / PARTIAL / FAIL**, with a table of criterion → artifact → status.
   - **Do not declare PASS if any artifact is missing.**
   - "Verified informally", "confirmed manually", and "obviously fine" are **not evidence**.
     An artifact is a file on disk that someone else could read.
   - PARTIAL is for a phase whose gate section defines a partial outcome (e.g. Phase 1 rung B).
     Do not invent a PARTIAL to soften a FAIL.
7. **On PASS only:** regenerate `tasks.md` for the next phase from that phase's section in
   `plan.md`, using the card format in `plan.md` §8.2, **grouped under `## Track` headings**:

   ```markdown
   ## Track A — What this group of cards is about

   ### ⬜ PN-NN · Short imperative title
   **Spec:** §x.y · **Closes:** G-nn · **Decides:** ADR-00n
   **Files:** paths that will change
   **Do:** what changes and why, in two or three sentences
   **Done when:**
   - [ ] verifiable acceptance check
   **Evidence:** evidence/phase-N/pn-nn-name.txt
   ```

   **The track grouping is load-bearing, not cosmetic.** A track is the fleet's unit of
   dispatch — `/orchestrate` gives one worker one track — and `fleet.ps1 -Action next`
   parses these headings to work out what is open and what may run in parallel. A `tasks.md`
   with no `## Track` headings cannot be orchestrated at all; the selector reports it as
   ungrouped rather than guessing at a split.

   Two rules for where the lines go, and they follow from what a track is for:

   - **Put cards in the same track when they are sequential** — a migration and the code
     that reads it, an endpoint and the tests that assert it. A worker does a track in
     order and stops at the first card it cannot finish.
   - **Put cards in different tracks when their `**Files:**` are disjoint**, because that
     is precisely what lets two of them run on two machines at once. Overlapping file sets
     across tracks are not fatal — the selector detects the collision and sequences them —
     but every overlap is one less thing the fleet can parallelise.

   **`**Files:**` is not decoration either.** It is the only input to the independence
   calculation, so a card that understates its files understates its conflicts and buys a
   merge conflict across two machines. List directories with a trailing slash when the card
   will touch several files under one.

   Carry forward any card from the closing phase that is still open, and say that you did.

## Output

Lead with the verdict and the count of missing artifacts. Then the table. Then, if the verdict
is not PASS, the shortest list of concrete actions that would make it PASS — task IDs, not advice.
