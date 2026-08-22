## Pipeline smoke test — not a real task card

This is a dispatch-pipeline test. There is no card in `tasks.md` for it.

**Scope:** read-only. Touch no files. Run no tests, no guardrails, no git commands.
Use only Read/Grep.

**Do this:** read `CLAUDE.md` and report the pinned MariaDB version and the pinned
.NET SDK version, exactly as recorded there, in your `summary`.

**Do NOT:** edit anything, commit anything, or look at `tasks.md`.

**Report:** status `done`, `verification.guardrails` = `not-run`, `verification.tests`
= `not-run`, `commit` = null, `filesChanged` = [].
