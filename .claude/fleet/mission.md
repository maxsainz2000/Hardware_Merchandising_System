# Mission ledger

The orchestrator's working state, kept **on disk on purpose**. This file is what lets an
orchestrator session survive a compaction without re-deriving the plan, and it is why the
plan does not have to live in context.

One row per card. Update it when you dispatch and when you collect. Delete rows when a
mission closes — this is a worksheet, not a history. `git log` is the history.

| Card | Machine | Model/Effort | Scope boundary | Status | Note |
|---|---|---|---|---|---|
| _(none open)_ | | | | | Phase 2 not started; fleet setup in progress |

## Fleet state — 2026-08-23 · fleet complete

| Box | Machine | Transport | Ready | Notes |
|---|---|---|---|---|
| box1 | LAPTOP-3HH6OHHE (laptop) | local | yes | Orchestrator host. Only box with XAMPP/MariaDB. |
| box2 | DESKTOP-G83CCSH (desktop, 200.9 GB) | rc | **yes** | READY, no failing rows. Owns `evidence/`, `scripts/`. |
| box3 | DESKTOP-F5LK8MA (**laptop**, 57.8 GB) | rc | **yes** | READY, no failing rows. Owns `documentations/`. Tightest disk. |

All three boxes are provisioned and `enabled: true`. Both secondaries verified on ground before
the bootstrap ran: cwd inside the checkout, HEAD at `05d73d6`, project CLAUDE.md loaded. The
bootstrap installed the **L4 pre-commit hook** on each clone — `.git/hooks` does not travel with
a clone, so both fresh checkouts had that gap open until the run.

What provisioning cost, recorded so it is not rediscovered: the private-repo clone cannot be
driven from an RC session at all (no TTY, so Git Credential Manager cannot prompt — see readiness
condition 6). The user signs in once from a real terminal per box; the cached credential makes
every later RC fetch headless. **The clone precedes the RC session.**

Neither secondary has XAMPP/MariaDB, and that is correct, not missing. ADR-013 pins the three DB
identities to box1, so every card touching the database, a migration, or the API at runtime is
placed on box1 no matter which box is idle.

RC session names change on every restart. The names below are hints — confirm against a live
`ListAgents` before dispatching. As of 2026-08-23: box2 `desktop-g83ccsh-joyful-bubble`,
box3 `desktop-f5lk8ma-rosy-flute`.

## SSH transport pilot — box2 · BLOCKED on OS corruption; Windows repair underway

Why: `rc` gives the orchestrator only **prompt**. It cannot start a session, set its model or
effort, or stop one — so on box2/box3 the model/effort table in `/orchestrate` §2 is advice
nothing enforces. `ssh` delivers all of it and the branch in `dispatch-worker.ps1` is already
written. This pilot proves the transport on one box before box3 gets the same treatment.

Done:
- Tailscale installed on box2 (winget, 1.102.2) and joined to `maxsainz2000@` as `box2` —
  `100.100.110.86`, **direct** path from box1, 3ms. Chosen over a hotspot firewall rule so the
  transport survives moving off the hotspot. Tailnet: `tail74f74a.ts.net`.
- box1 client side complete: `~/.ssh/fleet_ed25519` (ed25519, no passphrase — required for
  `BatchMode`), **NTFS ACLs locked to Admin+SYSTEM** because pwsh uses the System32 ssh client
  and refuses an over-permissive key; `~/.ssh/config` holds `Host box2` → tailnet hostname,
  user `maxsa`, identity file. So `sshTarget` stays a bare alias.
- `OpenSSH.Server~~~~0.0.1.0` capability installed on box2 (DISM CLI; the PS servicing cmdlets
  are COM-broken on that box, CBS itself is healthy).

**Blocked on: a reboot of box2, at the user's convenience.** DISM exits 0 and reports
`State = Installed`, but the `sshd` service does not exist until the machine restarts. The user
has asked to hold off. Nothing is mid-flight on box2, so the reboot costs only the RC session.

Resume, in order, after the reboot:
1. User restarts box2, then relaunches `claude --remote-control` **inside** `C:/dev/Hardware_Merchandising_System`.
2. `Set-Service sshd -StartupType Automatic; Start-Service sshd`.
3. Disable the install's blanket TCP 22 rule; add `fleet-sshd-tailnet` scoped to `100.76.155.51` only.
4. box1's public key → `C:\ProgramData\ssh\administrators_authorized_keys` (ASCII, no BOM;
   `maxsa` is a local Administrator so `~/.ssh/authorized_keys` is ignored), ACLs to Administrators+SYSTEM.
5. `PasswordAuthentication no`, restart sshd.
6. Test from box1: `ssh box2 "pwsh -NoProfile -Command ..."`. **The open risk lives here** —
   box2's pwsh is Store/MSIX, and its execution alias may not resolve under a non-interactive
   SSH login. If it fails, the fix is a full path in the dispatch invocation, not the transport.
7. Then `transport: "ssh"`, `sshTarget: "box2"` in `machines.json`, and prove it end to end with
   a throwaway card at `-Model haiku -Effort low` — model/effort selection is the whole point.

### Outcome of the box2 attempt — 2026-08-23

Two reboots in, `sshd` finally registered as a service and **still cannot start**. SCM reports
1053 ("did not respond in a timely fashion"), which is noise; running `sshd.exe -t` directly
gives the real error: exit `0xC0000135` = **STATUS_DLL_NOT_FOUND**. The binary fails at the
Windows loader before any config is read. Config and host keys were regenerated (the box had a
stale empty `C:\ProgramData\ssh` dated 2020) and it changed nothing, as expected.

It is not a broken OpenSSH payload: `ssh.exe -V` from the same folder runs fine
(OpenSSH_for_Windows_9.5p1). **Corrected 2026-08-23:** an earlier note here claimed three
symptoms pointed at one cause. Two of the three did not survive checking. `Get-WindowsCapability`
returning `Class not registered` is a trait of these Windows images — box3 returns it too and box3
is healthy. And `Get-AuthenticodeSignature` on sshd.exe is **Valid**, not `UnknownError`; that was
a formatting artifact misread from a bundled command, retracted by box2 itself. The loader failure
is the ONE reproducible symptom, with ScanHealth, sfc, the import scan, and signature validation
all clean. That is still enough to stop — but the box was not "three ways corrupt", and the
inflated framing is what made a Windows repair look justified.

The user was told plainly that repairing it is open-ended, may not fix sshd, and is not fleet
work. They chose to repair. Stage 1 (`DISM /Online /Cleanup-Image /ScanHealth`, read-only) is
running. Stage 2 (`/RestoreHealth`) downloads from Windows Update over the hotspot — check
whether metered data makes a local `/Source` install.wim the better option before starting it.

**Never applied on box2, deliberately, because locking down a server that cannot start is
pointless:** the `fleet-sshd-tailnet` firewall rule, the key in `administrators_authorized_keys`,
and `PasswordAuthentication no`. box2's sshd service is registered but Stopped/Manual. Nothing
needs reverting.

**Reordering worth keeping:** the SSH proof never needed Tailscale. Scoping the firewall rule to
box1's LAN address on the shared hotspot would have proven the transport just as well, with
Tailscale as a later upgrade — one config line and one rule. Making the network path a
prerequisite cost a detour. Tailscale itself was worth having anyway, and its NoState wedge
after the first reboot cleared on the second.

Not yet started on box3. Same sequence, same reboot, once box2 proves it.

## Status values

`planned` → `dispatched` → `done` \| `blocked` \| `failed`

A card is `done` only once **you** have read its report, found it internally coherent, and
ticked its boxes in `tasks.md`. A worker's own `done` is a claim, not the fact.

## Open questions for the user

Anything a worker raised as `needsDecision` that you could not settle. Empty is the goal.

_(none)_
