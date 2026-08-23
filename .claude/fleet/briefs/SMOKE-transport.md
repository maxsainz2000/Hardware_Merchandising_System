# SMOKE-transport — transport probe, not a task card

This is a fleet transport test. There is no card, no spec section, and no code to write.

## Scope

Touch **no files** in the repository. Read-only, except for the one report file the
dispatch instructions name.

## What to do

1. Report your current working directory.
2. Report `git rev-parse --short HEAD` for the checkout you are in.
3. Report whether the project `CLAUDE.md` is loaded — quote the language rule from §2 in
   one short phrase if it is.
4. Report the machine's hostname.

## What NOT to do

Do not run the guardrails, do not run tests, do not build, do not commit, do not clone or
fetch anything. Do not look for a task card to work on. Do not create, edit or delete any
file other than the report file.

## Reporting

Use `status: "done"` — for this probe, "done" means the four facts above were gathered.
Put them in the `notes` field. `filesChanged` must be empty. Report `tests` and
`guardrails` as `"skipped"`, because they were, and saying otherwise would be a false
claim about a run that never happened.
