# User guide

**Status: partial.** This file currently covers only account recovery
(P6-12 / ADR-028 / CARRY-01). The full task-first guide covering all five
roles and all three clients (Procurement, Inventory, POS) is written at
P6-15 — this section is written first because it closes a carry-forward
gap that has blocked demo-day account recovery since Phase 2.

---

## Account recovery

There is no self-service "forgot password" link and no "unlock my account"
button anywhere in Procurement, Inventory, or POS. This is deliberate
(ADR-017 §4, ADR-028): no HTTP route exists for user management at all, so
both recovery actions run on the host, from the `Merchandising.Maintenance`
CLI, by whoever has physical or remote access to the host machine and its
`merch_migrator` configuration. If you are demoing on a laptop that is not
the host, you cannot recover your own account — find whoever is at the
host.

### "I forgot my password"

Whoever operates the host runs:

```powershell
Merchandising.Maintenance.exe reset-password <operator-username> <your-username> <new-password>
```

- `<operator-username>` is the account of the person running the command —
  it is recorded on the audit trail as who made the change, exactly like
  every other privileged action in this system.
- `<your-username>` is the account being recovered.
- The new password is hashed through the same password-hashing path
  `create-user` uses (spec §9's "framework-provided password-hashing
  implementation") — it is never stored in plain text, and the operator
  types it once at the console, the same way `create-user` already works.
- The old password stops working immediately; this is a replacement, not
  an additional credential.
- The change is written to `AuditLogs`, naming the operator and the
  account, and cannot later be edited or deleted (CLAUDE.md §5).

This does **not** clear a lockout. If your account is both locked out and
you have forgotten the password, you need both commands below.

### "My account is locked out"

Five wrong password attempts in a row lock an account for 15 minutes
(spec §9). The lock clears on its own after that window — if you can wait,
you do not need this command. If you cannot (for example, mid-demo), the
host operator runs:

```powershell
Merchandising.Maintenance.exe unlock-user <operator-username> <your-username>
```

This clears the lockout timer and the failed-attempt counter immediately,
without changing your password. It is safe to run even if the account was
never locked — it simply has nothing to clear, and it still writes an
audit row recording that it was run. Like `reset-password`, the change is
audited with the operator's and the account's identity and cannot be
edited or deleted afterward.

### Why this is CLI-only, and what it costs

ADR-017 §4 already decided that user/role management gets no HTTP policy —
account creation was CLI-only from Phase 1, and account recovery follows
the same rule rather than opening a second, inconsistent path. The cost,
recorded plainly: on demo day, a classmate who is locked out or has
forgotten a password cannot fix it themselves from their own laptop — they
need someone with host access to run one of the two commands above. There
is no email reset, no SMS code, no self-service flow of any kind. See
ADR-028 for the full reasoning and what was rejected.
