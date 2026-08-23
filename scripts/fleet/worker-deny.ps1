<#
.SYNOPSIS
    The worker deny list: the invariants the fleet enforces MECHANICALLY rather than by
    asking a model to behave. Dot-source this and use $WorkerDenyRules.

.DESCRIPTION
    /worker's SKILL.md already tells a worker not to push, not to tick its own boxes, and
    not to disable a hook that blocked it. Those are instructions, and a model's compliance
    with an instruction is probabilistic -- high, but never 100%. For the handful of actions
    where a single violation is expensive or hard to undo, an instruction is not good enough.

    These rules are passed on the worker's OWN command line by run-worker.ps1 /
    run-worker-tty.ps1, so the worker cannot read them out of a config file and cannot edit
    them away. Measured, not assumed: --disallowed-tools OVERRIDES --permission-mode
    bypassPermissions. Verified 2026-08-23 -- a worker running under bypassPermissions was
    refused `git status` by a Bash(git *) rule and reported the refusal verbatim.

    This mirrors how the platform enforces the same class of invariant: Anthropic's
    multiagent documentation notes that one-level-of-delegation "is enforced rather than
    silently flattened" -- a validation error, not a line in a system prompt. `Agent` is
    denied below for exactly that reason.

    WHAT THIS DOES NOT DO. It cannot make a worker ask instead of guess, keep to a scope
    boundary, or report honestly. Those are judgement, and no permission rule reaches them.
    They stay instructions in the skill, and the orchestrator's job at collection time is to
    treat a report as a claim to be checked -- not as a fact.

    Syntax notes, learned from the CLI's own validator rather than guessed:
      - Edit(path) covers ALL file-editing tools. Write(path) is NOT matched by file
        permission checks and the CLI says so explicitly. Never write a Write(...) rule.
      - Bash rules take a command prefix: Bash(git push:*).
      - The flag is variadic, so a prompt must never follow it on the command line.
#>

$WorkerDenyRules = @(
    # --- The orchestrator's own state. A worker reports; it does not record. -------------
    'Edit(tasks.md)'                      # ticking your own boxes. The orchestrator does this
                                          # after reading your report, because a worker's
                                          # "done" is a claim and the tick is the judgement.
    'Edit(.claude/fleet/mission.md)'      # the mission ledger is the orchestrator's worksheet
    'Edit(.claude/fleet/machines.json)'   # the fleet registry, including every ceiling

    # --- The guardrails themselves. CLAUDE.md section 7: a blocked write is a stop --------
    # condition that has ALREADY been evaluated for you. Do not rename the file, write it
    # elsewhere, or disable the hook. That instruction is now also a wall.
    'Edit(.claude/settings.json)'
    'Edit(.claude/hooks/**)'
    'Edit(.git/hooks/**)'
    'Edit(scripts/check-no-csharp.ps1)'

    # --- Outward-facing and history-rewriting actions ------------------------------------
    'Bash(git push:*)'                    # integration is the orchestrator's, in one place
    'Bash(git commit:*--no-verify*)'      # L4 exists precisely to catch what Claude did not see

    # --- Depth-1 delegation ---------------------------------------------------------------
    # A worker must never become an orchestrator. This is the invariant the platform's own
    # multiagent design enforces structurally, and the reason the split works at all: the
    # orchestrator reasons, the worker implements.
    'Agent'
)
