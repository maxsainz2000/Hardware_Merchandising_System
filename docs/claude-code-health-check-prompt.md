# Prompt for Claude Code — repository health check before P1-06

Paste everything below the line into a **fresh** Claude Code session in
`C:\Users\Admin\Documents\Hardware_Merchandising_System`.

**Run this in a fresh session, not a continuing one.** A fresh session re-reads
`CLAUDE.md` and the memory index from scratch. A session that just performed the
work being audited will confirm its own reasoning — that is the one thing this
check must not do.

**Expect a NO-GO on at least one item.** Section 6 targets a defect this
repository has produced twice. Section 10 asks whether the database connection
opens *right now*, not whether a log says it once did. If the report comes back
clean across all ten sections, treat that as a reason to examine *how* it
verified, not as good news.

If this gets run more than once or twice, promote it to a `/health-check` skill
under `.claude/skills/` — same content, invoked by name.

---

Health-check this repository. Do NOT start P1-06, do not fix anything, and do
not commit. Read-only investigation, then a report.

You have CLAUDE.md, plan.md, tasks.md and docs/adr.md. Treat all four as
CLAIMS TO BE TESTED, not as ground truth.

## The rule for this whole task

**Verify against the machine and the files, never against another document.**
This repository has twice contained documentation that was confidently wrong
about its own machine:

- a task card marked done while both its acceptance boxes were unticked;
- installation-guide.md stating a hosts-file entry had never been applied on
  any machine, when it had been applied and resolved correctly.

Both were found by running a command, not by reading. If a check can only be
answered by re-reading prose, you have not performed it.

Where a claim cannot be verified without elevation, a second machine, or
something you lack, say so explicitly and mark it UNVERIFIABLE. Do not
downgrade it to "looks fine".

## What to check

1. **Build and test health.** Build all projects clean. Report the true
   warning count, do not round it to zero. Run `scripts/run-tests.ps1`. Run the
   guardrails directly and confirm G-A through G-D actually pass.

2. **Guardrail integrity — the mechanisms, not their output.** Are all four
   layers present and armed? `.claude/settings.json` committed; the two hook
   scripts present; `.git/hooks/pre-commit` installed with an LF shebang (a CRLF
   shebang silently disables it — this has been a real defect here). Is
   anything security-relevant hiding in `.claude/settings.local.json` that
   should be in the committed file?

3. **The language rule.** No `.cs`, `.csproj`, `.cshtml` or `.razor` anywhere
   under `src/`. Confirm no project sets `PublishAot`, `PublishTrimmed`, or
   overrides `Option Strict` / `Option Explicit` locally.

4. **Dependency direction.** `ClientCommon` and the three WPF clients must not
   reference `Infrastructure`, MySqlConnector, or any database package. Verify
   from the `.vbproj` files, not from the architecture diagram.

5. **Documentation vs. this actual machine.** Pick the load-bearing pinned
   values out of `docs/adr.md` and `docs/environment-manifest.md` and verify each
   one against the running system: MariaDB version, `sql_mode` (is
   `STRICT_TRANS_TABLES` really present now?), `bind-address`, collation, the dump
   tool's real path, .NET SDK and runtime versions, and whether the `merch_api`
   and `merch_migrator` accounts exist with the grants ADR/tasks claim. Report
   every mismatch, however small.

6. **Card honesty.** For every card in `tasks.md` marked ✅, confirm every
   "Done when" box is genuinely ticked and that each evidence file it names
   actually exists on disk at the stated path. List any card whose status and
   contents disagree, and any referenced evidence file that is missing.

7. **Internal consistency across the four authoritative documents.** CLAUDE.md
   > plan.md > tasks.md > docs/adr.md, higher wins. Report contradictions and
   say which side should change. Check in particular that every ADR's Status
   matches reality, and that no ADR marked PENDING has already been silently
   decided in code.

8. **ADR-012 was applied recently and is the most likely source of fresh
   inconsistency.** It splits everything into "lab" (the author's laptop and
   desktop, never a deliverable) and "demo" (three classmates' workstations
   plus a self-provided LAN). Look for leftovers that contradict it: language
   treating the author's machines as the host or as "Client 1", Tailscale
   still in scope for P0-03/P1-09/P1-10, hardcoded `192.168.100.x` addresses
   presented as configuration rather than as lab facts, or acceptance criteria
   that a lab machine could close when they are about demo machines.

9. **Secrets and personal data.** No credential, connection string or password
   committed anywhere, including in `evidence/` and git history. Separately, and
   just as a factual inventory: list what personal data the repo currently
   carries (home network details, MAC addresses, hostnames, email in commit
   authorship). This is going to be handed to three classmates — I want the
   inventory, not a recommendation.

10. **Readiness for P1-06 specifically.** P1-06 builds the migration runner.
    Confirm the ground it stands on: is `db/migrations/` genuinely empty; does
    the P1-05 connection layer actually open a connection against the live
    database right now; does a `SchemaMigrations` table already exist; and is
    ADR-008 still PENDING as `tasks.md` claims. Name anything that would make
    P1-06 stall five minutes in.

## Output

A single report, no fixes applied:

- **VERDICT: GO or NO-GO for starting P1-06**, in one line, up front.
- **Blockers** — must be resolved before P1-06. Each with the command or file
  that proves it, and the specific fix.
- **Non-blocking findings** — ranked by consequence, same evidence standard.
- **Unverifiable** — what you could not check and precisely why.
- **Verified clean** — a short list, so I know what you actually exercised
  rather than assumed.

Be adversarial. A health check that finds nothing is a health check I do not
believe. If you genuinely find nothing in a section, say what you ran that
would have caught a problem had one existed.
