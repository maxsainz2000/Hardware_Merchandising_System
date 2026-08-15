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
   `plan.md`, using the card format in `plan.md` §8.2:

   ```markdown
   ### ⬜ PN-NN · Short imperative title
   **Spec:** §x.y · **Closes:** G-nn · **Decides:** ADR-00n
   **Files:** paths that will change
   **Do:** what changes and why, in two or three sentences
   **Done when:**
   - [ ] verifiable acceptance check
   **Evidence:** evidence/phase-N/pn-nn-name.txt
   ```

   Carry forward any card from the closing phase that is still open, and say that you did.

## Output

Lead with the verdict and the count of missing artifacts. Then the table. Then, if the verdict
is not PASS, the shortest list of concrete actions that would make it PASS — task IDs, not advice.
