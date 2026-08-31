# User guide

**Status: written at P6-15**, covering all five roles (spec §9) and all three WPF clients as
they stand at Phase 6 close: `Merchandising.Procurement`, `Merchandising.Inventory`,
`Merchandising.POS`. Organized **task-first** ("take a sale", "receive goods", "close the
day"), not screen-first, per the card's own instruction. `UserGuideDocumentationTests`
(`Merchandising.Tests.Unit`, no database required) drift-checks this document the same way
`BackupRestoreGuideDocumentationTests`/`UiSpecificationDocumentationTests` check theirs: every
evidence path below is resolved against disk, and every route/policy string quoted below is
cross-checked against the live controller source, so a renamed route or policy fails the suite
rather than shipping a stale instruction.

**Scope note.** This system is an **academic prototype**. MariaDB is supplied through XAMPP
because the course requires it (ADR-000), and Visual Basic is a binding course requirement
(CLAUDE.md §2). Nothing in this document should be read as a production-readiness claim.

**What this document does not repeat.** [`docs/backup-restore-guide.md`](backup-restore-guide.md)
already covers backup, restore, and maintenance-mode entry/release in full — this guide links to
it rather than duplicating it. [`docs/role-permission-matrix.md`](role-permission-matrix.md) is
the authoritative, generated permission table; this guide names which role can do a task, not
every policy that gates it.

---

## 1. Signing in

Every client (`Merchandising.Procurement`, `Merchandising.Inventory`, `Merchandising.POS`) opens
to the same sign-in panel (top-left, bordered): a **Username** box, a **Password** box, and a
**Sign in** button. Enter submits it (the button is the panel's default action). A failed sign-in
shows the API's own error in the status area below the panel — never a client-invented message
(spec §16, `ui-specification.md` §5).

Once signed in, the top bar shows your username and the connection state, and a **Sign out**
button appears at the far right of every screen in every client — it is always the last control in
the keyboard tab order (`TabIndex="99"`), regardless of which tab you are on.

**Five wrong passwords in a row locks the account for 15 minutes** (spec §9). If you cannot wait,
or you have forgotten the password entirely, see [§9 Account recovery](#9-account-recovery) below
— there is no self-service reset.

---

## 2. Which role uses which client

| Role (spec §9) | Client(s) | What they're there for |
|---|---|---|
| **Cashier** | POS | Sessions, sales, permitted returns. |
| **Inventory Clerk** | Inventory | Stock visibility, receiving confirmation, counts, adjustment requests, low-stock review. |
| **Procurement Officer** | Procurement | Suppliers, purchase orders, order tracking. |
| **Admin** | All three, plus direct API calls for tasks with no screen (§8) | Everything above, plus approvals, price changes, reports. |
| **Super Admin** | All three, plus direct API calls (§8), plus the `Merchandising.Maintenance` CLI on the host | Everything Admin can do, plus configuration, backup/restore, account recovery, maintenance mode. |

A user with a higher role can sign into any client a lower role uses — the client shows the same
tabs; the API is what enforces which buttons actually succeed (CLAUDE.md §5: UI hiding is not a
security control, and none of these clients hide a tab by role). The full policy-by-policy table
is [`docs/role-permission-matrix.md`](role-permission-matrix.md).

---

## 3. Take a sale (Cashier, POS — Session then Checkout)

A sale needs an **open cashier session** first.

1. **Session tab.** Enter the **Opening float** (starting cash in the drawer) and click
   **Open session**.
2. **Checkout tab.** Type a SKU, barcode, or product name into the search box and click
   **Look up**. Select the product in the results grid, enter a **Quantity**, and click
   **Add to cart** — repeat for every item.
3. Pick a **Payment method** (Cash, Card, or E-Wallet). For Cash, enter the amount **Tendered**;
   Card/E-Wallet skip that field.
4. Click **Complete sale**. The receipt panel below shows the committed sale lines, and for a
   cash sale the change is calculated by the API, never the client — the same fixed-precision
   `Decimal` arithmetic CLAUDE.md §5 requires everywhere money is involved.

**Card and e-wallet payments are recorded, not authorised** — the Checkout tab states this
directly beneath the payment controls, and it is worth repeating here: this system never contacts
a bank, a card network, or a payment terminal. See [§10](#10-what-this-system-does-not-do).

If stock is insufficient, or the same sale is retried after a network hiccup, the API's own
refusal or the original committed result is shown — never a duplicate sale (spec §11's
idempotency-key rule).

---

## 4. Close the day (Cashier, POS — Session)

At the end of a session: **Session tab**, enter your **Declared cash** (the physical cash count
from the drawer) and click **Close session**. The **Payment totals** grid then shows the
session's totals by payment method, computed by the API from the committed `Sales`/`Payments`
rows — never a client-supplied figure (`CloseCashierSessionRequest` deliberately carries no
`calculatedCash` field). Any variance between declared and calculated cash is part of that same
committed record.

---

## 5. Record a return at the register (Cashier, POS — Returns)

**Returns tab.** Select the original sale from **Completed sales (this session)**, then the
specific line from **Sale lines**. Enter the **Qty returned**, check **Restocks item** if the
goods go back on the shelf, enter a **Reason**, choose a **Refund method**, and click
**Record return**. The return cannot exceed the quantity sold minus any prior returns on that
line — the API enforces this, not the client.

A return outside your permitted scope (spec §9's "permitted returns" restriction) is refused with
`FORBIDDEN`. An Admin or Super Admin approves an exceptional return by a direct API call — there
is no approval control on this screen; see [§8](#8-admin-and-super-admin-tasks-with-no-client-screen).

---

## 6. Buy and receive goods (Procurement Officer, then Inventory Clerk)

Procurement and receiving are two roles and two clients, in sequence.

### 6.1 Create and submit a purchase order (Procurement — New Order)

1. **Supplier** panel: search and select the supplier.
2. **Add line** panel: search for a product, enter **Qty** and **Cost**, click **Add line** —
   repeat per line.
3. Click **Create purchase order** (status starts `Draft`).
4. **Orders tab**: select the order and click **Submit** (status moves to `Submitted`).

### 6.2 Approve or cancel (Admin/Super Admin approves; Procurement Officer and above can cancel)

**Orders tab**, select the order:

- **Approve** — moves `Submitted` → `Approved`. The API refuses if you are the order's own
  creator (self-approval, spec §9) with `FORBIDDEN`.
- **Cancel order** — type a reason first (the text box beside the button is required), then
  click **Cancel order**. Only allowed before any goods have been received.

There is no **Close** button here — see [§8](#8-admin-and-super-admin-tasks-with-no-client-screen)
for how a Procurement Officer or Admin finishes a partially-received order and accepts what came
in.

### 6.3 Receive goods (Inventory Clerk — Receive)

1. Enter the **Purchase order Id** and click **Load order** — its lines appear in **Order lines**.
2. Select an order line, enter **Qty received** and **Cost**, click **Add line** — repeat for
   every line being received in this delivery (partial receiving is supported: receive fewer than
   ordered now, the rest on a later delivery).
3. Enter a **Reference #** for this delivery and click **Receive goods**.

This commits the goods receipt, the stock-in movements, the updated stock balance, and the audit
record in one transaction (spec §11) — over-receiving beyond what was ordered is rejected by
default.

**Recording a return to the supplier** (damaged or wrong goods) has no client screen at all — see
[§8](#8-admin-and-super-admin-tasks-with-no-client-screen).

---

## 7. Inventory operations (Inventory Clerk)

- **Check stock or review movements — Stock tab.** Search products (optionally **Include
  inactive**), or enter a **Product Id** and date range under **Movements for Product Id** and
  click **Load movements** to see the ledger (delta, before/after quantity, reason) for one
  product.
- **Perform a stock count — Counts tab.** Click **Open new count** (or **Load** an existing count
  by Id), search for each product, enter its **Counted quantity**, click **Record line** —
  repeat per item — then **Close count**. A variance beyond the configured threshold routes the
  resulting adjustment to approval automatically; you do not decide that threshold here.
- **Request a stock adjustment — Adjustments tab.** Search for the product, enter the
  **Variance** (positive for "found more", negative for "found less") and a **Reason**, click
  **Request adjustment**. If the variance is within the configured threshold it applies
  immediately; above it, it waits in the **Adjustments (this session)** grid for an Admin or
  Super Admin.
- **Approve or reject a requested adjustment — Adjustments tab, same grid.** An Admin or Super
  Admin (not the same clerk who requested it — self-approval is refused with `FORBIDDEN`, same
  rule as purchase-order approval) selects the row and clicks **Approve** or **Reject**.
- **Low stock — Low stock tab.** Search (with an optional sort, e.g. `quantity:asc`) to see every
  product at or below its reorder level.

---

## 8. Admin and Super Admin tasks with no client screen

None of the three WPF clients implement a screen for the tasks below — this was confirmed by
reading every `MainWindow.xaml` and cross-checking `PolicyRegistry.vb` against every
`<Authorize(Policy:=...)>` attribute in `src/Merchandising.Api/Controllers/`, not assumed. Stated
plainly rather than glossed over, per this project's own standard for what a document must not
overstate (`plan.md` §6, `docs/adr.md` ADR-016). Every one of these is a real, working, tested
API endpoint (`Merchandising.Tests.Integration`'s `AuthorizationMatrixTests` and the controller's
own suite cover them) — there is simply no button for it yet; that is Phase 7 UI-refinement scope
(`ui-specification.md`'s own "Known gap" note), not a missing feature.

All of the calls below need a bearer token first:

```powershell
$login = Invoke-RestMethod -Method Post https://MERCH-HOST:8443/api/v1/auth/login `
    -ContentType 'application/json' -Body (@{ username = '<you>'; password = '<your password>' } | ConvertTo-Json)
$headers = @{ Authorization = "Bearer $($login.token)" }
```

| Task | Role | Call |
|---|---|---|
| Add a product | Admin, Super Admin | `POST /api/v1/products` — body: `sku`, `name`, `categoryId`, `brandId`, `unitId`, `barcode`, `price`, `cost`, `reorderLevel` |
| Update a product | Admin, Super Admin | `PUT /api/v1/products/{id}` |
| Change a product's price/cost | Admin, Super Admin | `PUT /api/v1/products/{id}/price` |
| Deactivate / reactivate a product | Admin, Super Admin | `POST /api/v1/products/{id}/deactivate` / `.../reactivate` |
| Add a supplier | Admin, Super Admin, Procurement Officer | `POST /api/v1/suppliers` — body: `name`, `contactName`, `phone`, `email`, `address` |
| Update / deactivate / reactivate a supplier | Admin, Super Admin, Procurement Officer | `PUT /api/v1/suppliers/{id}`, `POST /api/v1/suppliers/{id}/deactivate`, `.../reactivate` |
| Close a purchase order (accept what was received, stop waiting for the rest) | Admin, Super Admin, Procurement Officer | `POST /api/v1/purchase-orders/{id}/close` — body: `{ "reason": "..." }` |
| Record a purchase return to a supplier | Admin, Super Admin, Procurement Officer | `POST /api/v1/receipts/{receiptId}/returns` — body: `referenceNumber`, `lines`, `idempotencyKey` (ADR-007: a required 36-character UUID) |
| Approve an exceptional sales return | Admin, Super Admin | `POST /api/v1/sales/returns/{id}/approve-exceptional` — body: `{ "refundMethod": "Cash" }` (or `"Card"`/`"EWallet"`) |
| View a report | Admin, Super Admin | `GET /api/v1/reports/sales/daily-summary?date=2026-08-30` (and the other eleven reports named in [`docs/report-specification.md`](report-specification.md)) |
| Export a report to CSV | Admin, Super Admin | Same route with `/csv` appended, e.g. `GET /api/v1/reports/sales/daily-summary/csv` — export permission always equals that report's view permission (ADR-024) |
| Change system configuration (currency code, rounding policy, adjustment/return-approval thresholds) | Super Admin only | `GET /api/v1/admin/settings` to read every current value; `PUT /api/v1/admin/settings/{key}` — body: `{ "value": "..." }` — keys are `currency.code`, `currency.roundingPolicy`, `inventory.adjustmentThreshold`, `sales.returnApprovalThreshold` |

**Review the audit log.** `Audit.Review` is a registered policy (spec §9's Super Admin "audit
review" responsibility) but, as of Phase 6, no endpoint checks it — there is no report, no
screen, and no API route that returns `AuditLogs` rows. The only way to inspect the append-only
audit trail today is a direct database read **on the host**: MariaDB binds to loopback only
(CLAUDE.md §6), so this cannot be done from a demo workstation. On the host, either open
phpMyAdmin (also loopback-only — never exposed to the store LAN, per `docs/professor-approvals.md`)
or run `mysql.exe` against the `merchandising` database with an account that holds `SELECT`
(`merch_backup` — never `root`, CLAUDE.md §6.1) and query `AuditLogs` directly. This is a real
gap, not an oversight papered over — recorded here rather than implying a review screen exists.

---

## 9. Account recovery

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

### Backup, restore, and maintenance mode

Entering/releasing maintenance mode, triggering a backup, and performing a restore are all
Super-Admin/host operations, fully documented rather than repeated here: see
[`docs/backup-restore-guide.md`](backup-restore-guide.md).

---

## 10. What this system does not do

**No terminal, bank, cash drawer, weighing scale, customer display, or receipt printer
integration** (G-24, spec §21). Every payment this system records — cash, card, or e-wallet — is
an **operational record**, not a claim that a bank or card network authorised anything. The POS
Checkout screen states this in the same words this section uses:

> Card and e-wallet payments shown here are recorded for this sale only, not authorised.

This wording, and the exclusion list above, are asserted verbatim by `PaymentWordingTests`
against `src/Merchandising.POS/Resources/PaymentWording.xaml` on every test run — if either drifts
in the code, that suite fails before this document could go stale silently.

**Also excluded from this MVP** (spec §21, in full): accounting; accounts payable/receivable;
statutory tax filing; advanced tax computation; e-commerce; online ordering; mobile applications;
customer loyalty; customer-specific pricing; promotions and coupons; multi-branch or
multi-warehouse operation; stock transfers; offline POS; automatic synchronization; batch,
expiry, and serial-number tracking; supplier tendering; automated replenishment; advanced
forecasting; cloud hosting; and external payment authorization.

**Online-only.** If the API cannot be reached, the client says so plainly — *"The API could not
be reached. Nothing was sent and nothing was saved."* — and nothing is queued for later. There is
no offline mode (`ui-specification.md` §5, ADR/CLAUDE.md §5).

---

## Evidence

`evidence/phase-6/p6-15-user-guide.txt` — guardrail run and both test suites green, including
`UserGuideDocumentationTests`.
