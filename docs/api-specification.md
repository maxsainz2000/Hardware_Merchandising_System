# API Specification

**Status: procurement section only (P3-08).** This document is built incrementally, one
section per phase, per `plan.md` §7 — Products/Suppliers/Auth (Phase 1–2), Inventory
(Phase 4), POS (Phase 5), Reports (Phase 6–7) each append their own section when their
phase closes. This first section covers exactly what Phase 3 Track C added:
`PurchaseOrdersController`'s eight routes. It does not cover Suppliers (`SuppliersController`,
P2-10) — that record predates Phase 3 and has no card assigning its documentation yet.

**Verification.** `ApiSpecificationDocumentationTests` (`Merchandising.Tests.Integration`)
reflects over the live `PurchaseOrdersController` — its `<Route>`, `<HttpGet>`/`<HttpPost>`
templates, and declared `<Authorize(Policy:=...)>` values — and cross-checks the constants in
`PolicyRegistry.Names` / `PurchaseOrderTransitionErrors` against the text below. A route
renamed, a policy swapped, or an error code renamed without updating this file fails that
suite. This is the P2-12 shape applied to a prose document rather than a pure-data one: a
reflection-based drift check against the running registration, not a byte-for-byte
regeneration (unlike `docs/role-permission-matrix.md`, this document is not entirely derivable
from one data table).

**Scope note.** This system is an **academic prototype**. MariaDB is supplied through XAMPP
because the course requires it (ADR-000), and XAMPP is documented by Apache Friends as
intended for development environments. Nothing in this document should be read as a
production-readiness claim.

---

## Conventions used throughout this document

- All routes are versioned under `/api/v1` and require the `Session` authentication scheme
  (a bearer token from `POST /api/v1/auth/login`) unless stated otherwise. An unauthenticated
  request to any route below receives `401 UNAUTHORIZED`.
- A **policy name**, not a role string, gates every route (CLAUDE.md §5, ADR-017). The policy
  name given below is `PolicyRegistry.Names.*` — the same constant the controller's
  `<Authorize(Policy:=...)>` attribute carries, so the enforced check and this document read
  the identical string. A request whose actor's role does not satisfy the policy receives
  `403 FORBIDDEN`.
- Every error response is the same envelope (`Merchandising.Contracts.Errors.ApiErrorResponse`,
  CLAUDE.md §5): `errorCode` (stable, machine-readable), `message` (human-readable), `correlationId`
  (also echoed on the `X-Correlation-Id` response header by `CorrelationIdMiddleware`), and
  `errors` (field name → message array, present only on `VALIDATION_FAILED`). No response body
  ever carries a stack trace, SQL text, connection string, or other internal detail.
- Money fields are `DECIMAL(19,4)`; quantity fields are `DECIMAL(19,3)` (CLAUDE.md §5, ADR-004).
  A client-supplied value with more decimal places than the storage scale allows is refused as
  `VALIDATION_FAILED` at the API boundary, before any database parameter is bound (ADR-004.1) —
  it is never silently rounded on the caller's behalf.
- Every purchase-order status value is the `PurchaseOrderStatus` enum **name** (`"Draft"`,
  never an ordinal like `"1"`) — ADR-020 §5. A caller that switches on the string keeps working
  when a status is added; one that switched on a number would not.
- Timestamps are ISO-8601 UTC on the wire; the store time zone (`Asia/Manila`,
  `Merchandising.Domain.StoreTimeZone.IanaId`) applies only to the `history` endpoint's date
  filters below, never to how a timestamp is transmitted.

**Cross-cutting error codes** — not repeated under every route below because they apply to
every route in this section identically:

| Error code | HTTP status | Condition |
|---|---|---|
| `UNAUTHORIZED` | 401 | No session token, or an expired/invalid one (`SessionAuthenticationHandler.HandleChallengeAsync`). |
| `FORBIDDEN` | 403 | The authenticated actor's role does not satisfy the route's policy (`SessionAuthenticationHandler.HandleForbiddenAsync`), or — `PurchaseOrders.Approve` only — the actor is the order's own requester (self-approval, ADR-017 §6). |
| `INTERNAL_ERROR` | 500 | Any unhandled exception (`ExceptionHandlingMiddleware`). The response reveals nothing about the exception; the correlation ID is the caller's whole route to a diagnosis, via an operator reading the server log. |

---

## Purchase orders — `PurchaseOrdersController`

Base route: `api/v1/purchase-orders`. Backing service: `PurchaseOrderService`
(`Merchandising.Api.Procurement`). Every write below runs inside one transaction with a
`SELECT ... FOR UPDATE` row lock and a conditional status update — the same "commit
everything or nothing, verify the affected-row count" discipline CLAUDE.md §5 requires for
stock, applied here to the status machine instead (P3-01's `CanTransition` table, ADR-020).

### `POST /api/v1/purchase-orders` — create a Draft order

**Policy:** `PurchaseOrders.Create` (SuperAdmin, Admin, ProcurementOfficer).

**Idempotency (ADR-007):** required. `idempotencyKey` must be a canonical 36-character UUID.
A repeated key for a request already committed returns the **original** committed response
body verbatim (HTTP 200, not 201 — this call created nothing) rather than re-serializing a
row that may since have moved on. The claim lives inside the same transaction as the insert,
so a request that fails validation or a reference check never consumes the key — a legitimate
retry with the same key after a failure needs no separate cleanup.

**Request body:**

```json
{
  "supplierId": 12,
  "lines": [
    { "productId": 45, "orderedQuantity": 10.000, "purchaseCost": 250.0000 }
  ],
  "idempotencyKey": "3f2504e0-4f89-41d3-9a0c-0305e82c3301"
}
```

`status` may also appear on the body but must be absent or null — see below. There is no
`orderNumber` or per-line `lineNumber`/`receivedQuantity` on the request: the server assigns
the order number (`PO-yyyyMMdd-NNNN`) and line numbers (1..N in arrival order); a client
cannot supply either.

**Response (201 Created, or 200 on idempotent replay):** `PurchaseOrderResponse` — the full
order with its lines, always `status: "Draft"`, `approvedByUserId`/`submittedAtUtc`/`approvedAtUtc`
all null. `Location` header points to `GET /api/v1/purchase-orders/{id}`.

**Error codes:**

| Error code | HTTP status | Condition |
|---|---|---|
| `VALIDATION_FAILED` | 400 | Missing/malformed body; a non-null `status` (assigned by the server, never client-supplied — see `CreatePurchaseOrderRequest`'s own header for why the property exists only to be rejected); missing/malformed `idempotencyKey`; `supplierId <= 0`; no lines; a line with `productId <= 0`, a non-positive `orderedQuantity`, a negative `purchaseCost`, or a quantity/cost with more decimal places than DECIMAL(19,3)/DECIMAL(19,4) allow; an unknown `supplierId` or `productId` (reported per line, together, not one at a time). |
| `SUPPLIER_INACTIVE` | 400 | The supplier exists but is deactivated. |
| `PRODUCT_INACTIVE` | 400 | Any referenced product exists but is deactivated. |
| `ORDER_NUMBER_UNAVAILABLE` | 409 | The server exhausted its retry bound allocating an order number for the current date. Practically unreachable; retry the request. |

### `GET /api/v1/purchase-orders` — list, headers only

**Policy:** `PurchaseOrders.Track` (SuperAdmin, Admin, ProcurementOfficer).

**Pagination, sorting, filtering (spec §13):**

| Parameter | Default | Behaviour |
|---|---|---|
| `page` | 1 | Values below 1 are clamped up to 1. |
| `pageSize` | 25 | Values below 1 are clamped to the default (25); values above `maxPageSize` (100) are clamped **down**, never honoured. |
| `sort` | `createdAt:desc` | `field` or `field:direction`. Whitelisted fields: `createdAt`, `orderNumber`, `status`. `direction` is `asc` or `desc`. Any other field or direction is refused, never silently ignored — no caller-supplied text ever reaches an `ORDER BY`. |
| `supplierId` | none | Optional exact-match filter. |
| `status` | none | Optional exact-match filter against one of the seven `PurchaseOrderStatus` names. |

Date-boundary filtering is deliberately **absent** here — see the `history` endpoint below,
which owns it.

**Response (200):** `PurchaseOrderSearchResponse` — `items` (array of `PurchaseOrderSummaryResponse`:
header fields plus `lineCount`, no lines), `totalCount`, `page`, `pageSize`, `maxPageSize`, and
`sort` (the "field:direction" text actually applied — a caller must never have to infer what a
clamped or defaulted value became).

**Error codes:**

| Error code | HTTP status | Condition |
|---|---|---|
| `VALIDATION_FAILED` | 400 | Unknown `status` value, or unsupported `sort` field/direction. |

### `GET /api/v1/purchase-orders/history` — purchase-order history report

**Policy:** `PurchaseOrders.Track` — the same policy the plain list uses; this is a second,
report-shaped read of the same operation, not a distinct one.

Adds `fromDate`/`toDate` (each `yyyy-MM-dd`, interpreted as a store-local calendar day,
`Asia/Manila`) to the plain list's `supplierId`/`status`/`sort`/`page`/`pageSize` contract
above, with the same defaults and clamping. `fromDate` and `toDate` are each optional and
independent; omitting one leaves that side unbounded rather than defaulting to "today." When
both are given, `toDate` must not be before `fromDate`.

**Response (200):** `PurchaseOrderHistoryResponse` — `items` (array of
`PurchaseOrderHistoryItemResponse`: header fields plus server-computed `orderedQuantity`,
`orderedValue`, `receivedQuantity`, `receivedValue`, `outstandingQuantity`, all summed over
the order's lines, never re-summed by the client), `totalCount`, `page`, `pageSize`,
`maxPageSize`, `sort`, and the date range **actually applied**: `fromDate`, `toDate` (each
null when unbounded on that side), and `timeZone` (always `"Asia/Manila"`). A cancelled or
closed order appears here like any other — nothing excludes a terminal status by default.

**Error codes:**

| Error code | HTTP status | Condition |
|---|---|---|
| `VALIDATION_FAILED` | 400 | Unknown `status`; unsupported `sort`; `fromDate`/`toDate` not in `yyyy-MM-dd` form; `toDate` before `fromDate`. |

### `GET /api/v1/purchase-orders/{id}` — one order with its lines

**Policy:** `PurchaseOrders.Track`.

**Response (200):** `PurchaseOrderResponse`, with `lines` (`PurchaseOrderLineResponse[]`) —
`productSku`/`productName` are a live join against `Products`, so a deactivated product's
order history keeps displaying its name (spec §10.2); `purchaseCost` is the cost **captured
at order time**, never a later price change.

**Error codes:**

| Error code | HTTP status | Condition |
|---|---|---|
| `PURCHASE_ORDER_NOT_FOUND` | 404 | No order with the given id exists. |

### `POST /api/v1/purchase-orders/{id}/submit` — Draft → Submitted

**Policy:** `PurchaseOrders.Submit` (SuperAdmin, Admin, ProcurementOfficer). Role-only check —
submission carries no self-approval requirement.

**Idempotency:** none. A repeat call after a successful submit finds the order already
`Submitted`; `CanTransition` refuses the second call with `PURCHASE_ORDER_INVALID_TRANSITION`
(409) rather than submitting it twice or replaying the original response. Safety here comes
from the status machine's conditional update, not from a client-generated key.

**Response (200):** `PurchaseOrderResponse` with `status: "Submitted"` and `submittedAtUtc` set.

**Error codes:**

| Error code | HTTP status | Condition |
|---|---|---|
| `PURCHASE_ORDER_NOT_FOUND` | 404 | No order with the given id exists. |
| `PURCHASE_ORDER_CANCELLED` | 409 | The order is Cancelled. |
| `PURCHASE_ORDER_CLOSED` | 409 | The order is Closed. |
| `PURCHASE_ORDER_INVALID_TRANSITION` | 409 | Any other status that is not Draft (e.g. already Submitted, Approved, or Received). |

### `POST /api/v1/purchase-orders/{id}/approve` — Submitted → Approved

**Policy:** `PurchaseOrders.Approve` (SuperAdmin, Admin), enforced **imperatively**, not by a
declarative `<Authorize(Policy:=...)>` on the action. The declarative filter runs before the
method body, with no order loaded yet, so a resource-based self-approval check could never see
a matching resource and would succeed for no one. Instead: (1) role membership is checked
first, against the identical `PolicyRegistry.Definitions` role list, so a disallowed role gets
403 **before** the order is ever loaded — never a 404, which would otherwise leak which order
ids exist to a caller with no right to ask; (2) once role membership passes, the order is
loaded and `IAuthorizationService.AuthorizeAsync` evaluates the resource-based self-approval
veto (ADR-017 §6): the actor may not be the order's own `requestedByUserId`. Either failure
produces the same `403 FORBIDDEN` body and is audited regardless of outcome
(`PurchaseOrderApprovalDenied`).

**Idempotency:** none — same reasoning as Submit above; a repeat call after success is refused
`PURCHASE_ORDER_INVALID_TRANSITION`, not replayed.

**Response (200):** `PurchaseOrderResponse` with `status: "Approved"`, `approvedByUserId`, and
`approvedAtUtc` set.

**Error codes:**

| Error code | HTTP status | Condition |
|---|---|---|
| `FORBIDDEN` | 403 | Actor's role does not hold `PurchaseOrders.Approve`, **or** actor is the order's own requester (self-approval, ADR-017 §6) — indistinguishable from the outside, deliberately, so a self-approval attempt learns nothing about role sufficiency it did not already have. |
| `PURCHASE_ORDER_NOT_FOUND` | 404 | No order with the given id exists. |
| `PURCHASE_ORDER_CANCELLED` | 409 | The order is Cancelled. |
| `PURCHASE_ORDER_CLOSED` | 409 | The order is Closed. |
| `PURCHASE_ORDER_INVALID_TRANSITION` | 409 | Any status other than Submitted. |

### `POST /api/v1/purchase-orders/{id}/cancel` — abandon before receiving

**Policy:** `PurchaseOrders.Cancel` (SuperAdmin, Admin, ProcurementOfficer). Legal from Draft,
Submitted, or Approved; never from PartiallyReceived/FullyReceived, where goods have already
moved (Close is the route for abandoning the remainder there).

**Idempotency:** none — same reasoning as Submit; a repeat call is refused, not replayed.

**Request body:** `{ "reason": "..." }` — required, 1–500 characters.

**Response (200):** `PurchaseOrderResponse` with `status: "Cancelled"`.

**Error codes:**

| Error code | HTTP status | Condition |
|---|---|---|
| `VALIDATION_FAILED` | 400 | `reason` missing/blank, or longer than 500 characters. |
| `PURCHASE_ORDER_NOT_FOUND` | 404 | No order with the given id exists. |
| `PURCHASE_ORDER_CANCELLED` | 409 | The order is already Cancelled (a repeat call, or any other action against an order this state is terminal for — cancelled takes priority over every other refusal reason). |
| `PURCHASE_ORDER_CLOSED` | 409 | The order is Closed. |
| `PURCHASE_ORDER_INVALID_TRANSITION` | 409 | The order is PartiallyReceived or FullyReceived — goods have already moved; use close instead. |

### `POST /api/v1/purchase-orders/{id}/close` — finish, accepting whatever was received

**Policy:** `PurchaseOrders.Close` (SuperAdmin, Admin, ProcurementOfficer). Legal only from
PartiallyReceived or FullyReceived — Phase 4 owns the receiving endpoints that reach those
statuses; this route adds no rule beyond the status table.

**Idempotency:** none — same reasoning as Submit; a repeat call is refused, not replayed.

**Request body:** `{ "reason": "..." }` — required, 1–500 characters.

**Response (200):** `PurchaseOrderResponse` with `status: "Closed"`.

**Error codes:**

| Error code | HTTP status | Condition |
|---|---|---|
| `VALIDATION_FAILED` | 400 | `reason` missing/blank, or longer than 500 characters. |
| `PURCHASE_ORDER_NOT_FOUND` | 404 | No order with the given id exists. |
| `PURCHASE_ORDER_CANCELLED` | 409 | The order is Cancelled. |
| `PURCHASE_ORDER_CLOSED` | 409 | The order is already Closed (a repeat call — closed takes priority over every other refusal reason once reached). |
| `PURCHASE_ORDER_INVALID_TRANSITION` | 409 | The order is Draft, Submitted, or Approved — close is legal only once receiving has started (PartiallyReceived or FullyReceived). |

---

## Audit

Every route marked `<AuditRequired>` above — create, submit, approve (both grant and denial),
cancel, close — writes an append-only `AuditLogs` row (actor, action, target order number,
result, correlation ID; CLAUDE.md §5, spec §17). `AuditRequired` forces the write on a 2xx
result; the approval endpoint's denial path writes its own audit row directly, outside that
filter, because spec §9 asks a privileged action for an audit record regardless of outcome —
approve is audited whether it succeeds, is role-denied, or is self-approval-denied.
