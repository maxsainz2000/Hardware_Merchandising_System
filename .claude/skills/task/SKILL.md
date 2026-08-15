---
name: task
description: Start or continue a numbered task from tasks.md in the Merchandising System. Use whenever the user names a task ID like P0-04, P1-02, or P4-07, or says "next task", "continue the task", or "start the next card".
argument-hint: "[task-id]"
arguments: [taskId]
allowed-tools: Read, Grep, Glob, Edit, Write, Bash(git *), Bash(dotnet *), Bash(powershell *), Bash(pwsh *)
---

# Task loop — `$0`

Work exactly one task card. One task = one commit.

## The loop

1. **Read the card.** Find `$0` in `tasks.md`. If `$0` is empty, find the first card that is
   not `✅`, name it, and **confirm with the user before starting**.
2. **Read the spec.** Open the sections the card cites in
   `documentations/Merchandising System for a Mid-Scale Hardware Store.md`. Cite them back.
3. **Read the ADR.** If the card says `Decides: ADR-00N`, read that entry in `docs/adr.md`.
   If it needs a version or package not recorded there — stop and ask.
4. **Restate scope and acceptance criteria in your own words, then STOP for confirmation.**
   Not optional, not a formality, and not something to do *while* writing code. A misreading
   caught here costs a sentence; caught after implementation it costs the session.
5. **Failing test first** for any server-side business rule. Watch it fail for the right reason.
6. **Implement in order: API → Domain/Infrastructure → client last.** The API is authoritative;
   the client is the last thing that should exist and the least trusted thing in the system.
7. **Verify:** `pwsh ./scripts/check-no-csharp.ps1`, then `pwsh ./scripts/run-tests.ps1`.
   Both must be green. Paste the real output — never summarise a run you did not do.
8. **Capture evidence** to the exact path named on the card, if it names one.
9. **Tick `Done when` boxes in `tasks.md`** — only the ones genuinely satisfied.
10. **Resolve the ADR entry** the card decides: move it PENDING → ACCEPTED, recording what was
    decided and any friction encountered.
11. **Commit** with the task ID as the message prefix:
    `P1-02: hand-author VB Web SDK API`

## Never

- Never tick a `Done when` box on a failing test, a partial implementation, or an unresolved
  error. If blocked, leave the card open and say plainly why.
- Never mark a card done to make progress look tidier than it is.

## Stop conditions — halt and ask (CLAUDE.md §7)

Reporting a blocker is a **successful** outcome. Guessing is not.

1. About to write C#, or add a package requiring C# source generation.
2. Need a package or version not recorded in `docs/adr.md`. Never pick "latest".
3. About to put a connection string, credential, or `Infrastructure` reference in a client project.
4. A spec business rule is ambiguous, or two spec sections conflict.
5. A test fails in a way that suggests the **design** is wrong, not the code.
6. About to modify an already-applied migration file. (Write a new numbered migration instead.)
7. About to `UPDATE` or `DELETE` a row in `StockMovements` or `AuditLogs`.
8. The task needs a decision the spec assigns to the Foundation POC or to the professor.

Plus: **if a hook blocks you, that is the answer.** Do not rename the file, work around it, or
disable the hook. Report the blocker.

## Phase 1 scope discipline

Phase 1 is **one WPF window, two buttons, one product, one protected endpoint**. That is the
whole surface. Building a real feature during Phase 1 — a product grid, a search box, a second
screen — is a defect, not initiative. If you catch yourself designing one, stop: that is Phase 2.
