# Merchandising System for a Mid-Scale Hardware Store

A Windows client-server merchandising system: three WPF desktop clients, one central API, one
MariaDB database, one private LAN. Built as an academic MVP under binding course constraints.

**Everything in `src/` is Visual Basic .NET.** That is a course requirement, not a preference,
and there is no C# escape hatch anywhere in this repository.

| | |
|---|---|
| **Clients** | Procurement, Inventory, POS — WPF, `net10.0-windows`, framework-dependent |
| **API** | ASP.NET Core Web API, hand-authored VB, runs as a Windows Service, HTTPS on 8443 |
| **Database** | MariaDB 10.4.32 via XAMPP 8.2.12-0 |
| **Target** | .NET 10, `win-x64`, Visual Studio 2026, private LAN, online-only |

---

## Start here

```powershell
git clone <this repository>
cd Hardware_Merchandising_System

# 1. Install the git pre-commit hook (do this on every fresh clone)
pwsh ./scripts/install-hooks.ps1

# 2. Bring up the database, accounts, schema, grants and certificate.
#    Needs an elevated PowerShell 7 session and MariaDB running in XAMPP.
pwsh ./scripts/bootstrap.ps1

# 3. Prove it works
pwsh ./scripts/run-tests.ps1
```

`bootstrap.ps1` checks everything before it writes anything, generates a password per database
account for **your** installation, seeds five test accounts (one per role) plus a small product
catalog and a few suppliers, and finishes by printing the three per-machine steps it deliberately
leaves to you (hosts entry, firewall, client certificate trust). Read its header before running
it — it explains what it refuses to do and why. Every generated credential — the three database
accounts and the five seeded logins — ends up in one ACL-protected file,
`%ProgramData%\MerchandisingSystem\config\installation-credentials.txt`, never in this repository.

### Prerequisites

| | Version | Notes |
|---|---|---|
| .NET SDK | **10.0.301** | `dotnet --version`. Clients need only the Desktop Runtime 10.0.9 x64. |
| XAMPP | **8.2.12-0** | Supplies MariaDB 10.4.32. Install to any path; pass `-XamppRoot` if not `C:\xampp`. |
| PowerShell | **7+** | `pwsh`, not Windows PowerShell 5.1. |
| Visual Studio 2026 | Optional | Workloads: .NET desktop development, ASP.NET and web development. |

Exact pinned versions and the reasoning behind each live in [`docs/adr.md`](docs/adr.md). Do not
substitute versions — a drift between a dev machine and the classroom host is precisely the bug
class this project cannot absorb.

---

## Repository layout

```
src/
  Merchandising.Domain          entities, value rules      (depends on nothing)
  Merchandising.Contracts       DTOs shared with clients   (Domain)
  Merchandising.Infrastructure  MariaDB access, security   (Domain, Contracts)
  Merchandising.Api             the authoritative server   (+ Infrastructure)
  Merchandising.ClientCommon    API client, shared XAML    (Contracts ONLY)
  Merchandising.Procurement     WPF client
  Merchandising.Inventory       WPF client
  Merchandising.POS             WPF client
  Merchandising.Maintenance     migrations, backup, seed   (Infrastructure, Domain)
  tests/                        MSTest, unit and integration

db/
  migrations/   numbered schema scripts, never edited once applied
  grants/       account and privilege scripts, run as root

docs/           ADRs, installation guide, environment manifest
evidence/       proof captured per task, per phase
scripts/        bootstrap, tests, guardrails, certificate, publishing
```

**`ClientCommon` and the three WPF clients must never reference `Infrastructure` or any database
package.** That single rule is what mechanically guarantees a database credential cannot reach a
client laptop. It is enforced by guardrail G-B, not by good intentions.

---

## The rules that shape this code

Read [`CLAUDE.md`](CLAUDE.md) before contributing — it is the working agreement, and it applies
to humans as much as to agents. The short version:

- **The API is authoritative.** Clients may validate for usability; the API re-validates
  everything that matters. No client writes to the database, ever.
- **Every stock change is atomic** — a conditional `UPDATE` (never read-then-write), a
  `StockMovements` row, an `AuditLogs` row, one transaction, affected rows verified.
- **`StockMovements` and `AuditLogs` are append-only, by database privilege.** `merch_api` is
  granted no `UPDATE` or `DELETE` on either. Corrections are compensating movements, never edits.
- **Money is `DECIMAL(19,4)`, quantities `DECIMAL(19,3)`.** Never floating point, anywhere.
- **Every transactional command carries a client-generated idempotency key.**
- **Errors never leak internals** — a stable code, a message, a correlation ID, and nothing else.
- **Timestamps are stored UTC**, displayed in Asia/Manila.

Three database identities, and they are not interchangeable: `merch_migrator` owns the schema
and is the only account with DDL; `merch_api` owns the data and has no DDL at all;
`merch_backup` reads for `mysqldump`. Reaching for the wrong one surfaces as `ERROR 1142`
immediately, which is the intent. See [ADR-013](docs/adr.md).

---

## Guardrails

Four overlapping mechanisms, because each has a hole the next one covers:

| Layer | Fires | Catches |
|---|---|---|
| Claude Code `PreToolUse` | before a file is written | a `.cs`/`.csproj`/`.cshtml`/`.razor` path |
| Claude Code `PostToolUse` | after a `.vbproj` is written | client → `Infrastructure`, `Option Strict`, `PublishAot` |
| Claude Code `Stop` | end of every agent turn | G-A…G-D across the whole tree |
| **git `pre-commit`** | every commit | **edits made in Visual Studio, which the others never see** |

The git hook is the only layer that sees a change made through the WPF designer, a project
properties page, or the NuGet UI. Install it on every fresh clone with
`pwsh ./scripts/install-hooks.ps1`, and do not remove it on the grounds that the others exist.

Run the guardrails directly at any time:

```powershell
pwsh ./scripts/check-no-csharp.ps1
```

---

## Working on it

```powershell
pwsh ./scripts/run-tests.ps1                    # guardrails, build, unit, integration
pwsh ./scripts/run-tests.ps1 -SkipIntegration   # fast loop only; never the final check
```

Integration tests run against the **real** MariaDB instance, never an in-memory substitute —
proving behaviour against the exact pinned server version is the entire point of the Phase 1
gate. Start MySQL in the XAMPP Control Panel first.

Useful maintenance commands:

```powershell
dotnet run --project src/Merchandising.Maintenance -- migrate
dotnet run --project src/Merchandising.Maintenance -- seed             # idempotent - bootstrap.ps1 already runs this
dotnet run --project src/Merchandising.Maintenance -- create-user <name> <password> InventoryClerk
dotnet run --project src/Merchandising.Maintenance -- seed-demo <name>
```

**One task = one commit**, with the task ID as the message prefix (`P1-14: idempotency proof`).
Phases, gates and the task cards themselves live in [`plan.md`](plan.md) and
[`tasks.md`](tasks.md).

---

## Documentation map

| Document | What it is |
|---|---|
| [`documentations/Merchandising System…md`](documentations) | The specification — what and why. Outranks everything below. |
| [`plan.md`](plan.md) | Phases, gates, workflow |
| [`tasks.md`](tasks.md) | The current phase's task cards, with results and evidence |
| [`docs/adr.md`](docs/adr.md) | Every pinned version and irreversible decision |
| [`docs/installation-guide.md`](docs/installation-guide.md) | Per-machine setup `bootstrap.ps1` leaves to you |
| [`docs/environment-manifest.md`](docs/environment-manifest.md) | Measured facts about the machines involved |
| [`evidence/`](evidence) | Proof for each task's acceptance criteria |

If two documents disagree, the higher one in that list wins and the lower one gets corrected.

---

## Status

Phase 1 (Foundation Proof-of-Concept) is in progress. Several cards are complete but held open
pending proof that requires a **second machine** — cross-machine HTTPS round trip, the
phpMyAdmin denial, and the client runtime prerequisite check. Those are tracked honestly in
`tasks.md` rather than ticked on the strength of a same-host substitute.

This is an academic prototype. It is delivered as a handover package to be installed and
demonstrated by people who did not build it, on hardware the author does not own — which is why
credentials are generated per installation and nothing in this repository contains a working
password.
