# Merchandising System for a Mid-Scale Hardware Store
## Planning-Ready Academic MVP Project Specification

**Document status:** Revised planning baseline; implementation is conditional on the Foundation Proof-of-Concept gate  
**Prepared by:** Manus AI  
**Revision date:** 13 August 2026  
**Primary development environment:** Visual Studio 2026  
**Required application language:** Visual Basic .NET for all application source code  
**Desktop framework:** Modern .NET WPF  
**API framework:** ASP.NET Core Web API implemented in Visual Basic .NET  
**Database environment:** MariaDB supplied through XAMPP, as required by the course  
**Operating model:** Single store, central host laptop, private local network, online-only clients  
**Target framework:** .NET 10, subject to confirmation on the actual classroom machines  
**Deployment classification:** Controlled academic prototype; not a production-readiness claim for XAMPP

> **Important constraint statement:** This project must use Visual Basic because it is the language taught and required by the course. Microsoft states that Visual Basic supports ASP.NET Core Web API application types, although the standard ASP.NET Core Web API Visual Studio templates are not offered in Visual Basic.[1] [2] The API project will therefore be created manually or from a custom project template and must pass the Foundation Proof-of-Concept before full implementation begins.

> **Important XAMPP statement:** XAMPP is used because it is required by the course. Apache Friends states that XAMPP is intended for development environments and is not meant for production use.[3] This document therefore defines the result as a private-LAN academic prototype, requires basic hardening and recovery controls, and does not claim that XAMPP is suitable for a production retail deployment.

## 1. Executive Decision Summary

The revised project is **feasible for coursework** under the required all-Visual-Basic and XAMPP constraints. The three WPF applications, shared Visual Basic class libraries, manually authored Visual Basic ASP.NET Core API, Visual Basic maintenance utility, and Visual Basic test code can form one solution. The absence of a standard Visual Basic ASP.NET Core Web API template is a tooling risk, not a language-platform prohibition: Microsoft’s Visual Basic team explicitly lists ASP.NET Core Web API among the application types supported by Visual Basic, and an ASP.NET Core maintainer states that Visual Basic is supported even though Visual Basic-based templates are not planned.[1] [2]

The revised baseline resolves the previously identified gaps as follows. XAMPP is explicitly limited to the classroom prototype and is hardened rather than described as production-ready. The API will use HTTPS in the production-like demonstration profile, with HTTP permitted only for isolated local development. The exact MariaDB version and .NET data provider will be selected and pinned during the Foundation Proof-of-Concept. Stock changes will use server-side atomic conditional updates and transactions. Restore will be performed by a separate Visual Basic maintenance utility rather than by an ordinary live API request. Deployment will name the `win-x64` target, runtime prerequisites, certificate procedure, migration process, and rollback procedure. The implementation phases are reordered so Procurement is established before receiving is integrated into Inventory.

The project may proceed to detailed planning immediately. Full feature implementation may proceed only after the Foundation Proof-of-Concept demonstrates the all-Visual-Basic API, XAMPP/MariaDB connection, authentication, transaction behavior, HTTPS, Windows Service hosting, backup/restore rehearsal, and one WPF-to-API workflow.

| Decision | Revised decision |
|---|---|
| Is all-Visual-Basic implementation possible? | **Yes, conditionally.** It is supported in principle; the API requires manual project setup and an early proof-of-concept. |
| Is XAMPP permitted? | **Yes, for the mandated academic prototype.** It is not represented as production-ready. |
| Can the three WPF applications remain separate? | **Yes.** They will share Visual Basic libraries and a central API. |
| Can the API be a Windows Service? | **Yes.** ASP.NET Core Windows Service hosting is documented by Microsoft.[4] |
| Can the database remain inaccessible to clients? | **Yes.** MariaDB will bind to the host only; clients will call the API over HTTPS. |
| Is offline operation included? | **No.** Host/API/database/network availability is required during use. |
| What blocks full implementation? | Only the Foundation Proof-of-Concept exit criteria. |

## 2. Project Overview

The Merchandising System is a Windows-based client-server solution for a single mid-scale hardware store. It coordinates purchasing, stock control, point-of-sale operations, user access, auditability, backup, and operational reporting through three separate deployable desktop applications that share one central API, one database, and one set of server-side business rules.

The three user-facing applications are **Procurement**, **Inventory**, and **POS**. Procurement manages suppliers, purchase orders, approvals, receiving requests, and purchase history. Inventory manages the product master, stock visibility, stock counts, movement history, low-stock review, and controlled adjustments. POS manages cashier sessions, product lookup, sales, payment recording, returns, receipts, and daily closing. These applications are separate user experiences, not separate sources of truth.

The MVP is intentionally limited to one physical store, one central stock pool, and online-only operation. It does not include accounting, e-commerce, online ordering, mobile applications, multi-branch operation, offline POS, payment-terminal integration, receipt-printer integration, cash-drawer integration, weighing-scale integration, batch/expiry/serial-number tracking, or advanced tax processing. These exclusions focus the first release on the core cycle: **buy goods, receive goods, maintain stock, sell goods, record operational activity, and review results**.

> **MVP objective:** Provide a reliable, maintainable, and visually polished Windows system that gives store staff one consistent operational source of truth for products, suppliers, stock, purchases, sales, users, audit records, and reports within the mandated academic technology environment.

## 3. Academic Constraints and Assumptions

The system is being designed under explicit academic constraints. These constraints take priority over production-platform recommendations, but the documentation must distinguish a course requirement from a general engineering recommendation. The implementation must use Visual Basic for all authored application source code. XAML markup, SQL migration scripts, configuration files, and documentation are supporting artifacts; no C# application source is permitted in the solution.

XAMPP will supply MariaDB for the prototype. Apache, FileZilla, Mercury, and other unused XAMPP components will be disabled or stopped. MariaDB will be configured for local host access by the API only, and the database port will not be reachable from ordinary client laptops. The API will not depend on Apache for hosting.

The host laptop, client laptops, local network, Windows configuration, XAMPP installation, MariaDB version, .NET SDK/runtime, certificate trust, and backup directory are all part of the tested system environment. The system is not considered accepted merely because it works on the developer’s machine.

| Assumption | Required verification |
|---|---|
| Classroom computers run a supported 64-bit Windows version | Record the exact Windows edition/build for host and clients during Phase 1. |
| Visual Studio 2026 and the .NET desktop workload are available | Install and verify the required workload and .NET 10 components on the development machine.[5] |
| A manually authored Visual Basic ASP.NET Core API project is workable | Pass the API proof-of-concept. No permission is required: a hand-authored VB API does not violate the Visual Basic constraint, and Visual Studio template coverage is a tooling gap, not a course rule (PA-001). |
| XAMPP is mandatory | Use XAMPP/MariaDB for the prototype and document that it is a classroom constraint, not a production recommendation.[3] |
| One host laptop can serve approximately 5–10 concurrent sessions | Treat this as a test target, not a capacity guarantee. Validate against the contention profile defined in PA-004 — concurrent commands against a single product row, not idle sessions. |
| Clients are online while operating | Show a clear connection-unavailable state and document that offline transactions are not supported. |
| The store can provide a private LAN and a reserved host address | Verify addressing, firewall, name resolution, and client reachability before UAT. |

## 4. Business Problem

A hardware store manages a broad product catalog containing tools, fasteners, electrical supplies, plumbing items, building materials, adhesives, safety equipment, and related merchandise. Without a shared system, purchasing records, physical stock, and sales activity can become inconsistent. Staff may not know the current quantity of an item, its latest recorded purchase cost, whether it should be reordered, or which transaction caused a stock change.

The proposed system addresses these problems by centralizing operational data in MariaDB while providing separate experiences for Procurement, Inventory, and POS. All stock-changing actions pass through the Visual Basic ASP.NET Core API. This enables common validation, authorization, audit logging, transactional consistency, and a single implementation of cross-module rules.

## 5. Goals, Non-Goals, and Measurable Success Criteria

The MVP will be considered successful when store personnel can perform the core workflows without maintaining parallel spreadsheets or manually reconciling separate systems. The following targets are planning baselines and must be measured in the intended host, client, and network environment.

| Goal | Acceptance measure |
|---|---|
| Centralize merchandising data | Products, suppliers, purchases, receipts, stock balances, stock movements, sales, payments, users, and audit records are stored in one MariaDB database. |
| Improve stock visibility | Inventory can display current stock, movement history, low-stock items, receiving history, sales deductions, and approved adjustments. |
| Reduce purchase-to-stock errors | A valid receiving transaction creates exactly one receipt, the expected stock movements, and the correct stock balance within one committed transaction. |
| Support daily selling | A cashier can search, add items, record an allowed payment, calculate cash change, complete a sale, display receipt data, and view sales history. |
| Prevent inconsistent stock | Concurrent sale tests cannot create negative stock or duplicate movement records. |
| Protect operational data | Clients cannot connect to MariaDB; protected API operations require authentication and authorization; sensitive actions create audit records. |
| Provide recoverability | A scheduled backup is created, copied to a separate location, and restored successfully in a documented maintenance procedure. |
| Maintain acceptable pilot performance | Per PA-004, measured against the 5–10 session contention profile and excluding deliberate maintenance operations: ordinary reads target a p95 of **300 ms or less** and stock-changing commands a p95 of **800 ms or less**. The original 2-second and 4-second figures are retained as the hard-fail ceiling — exceeding either is a failure, not a near-miss. |
| Define recovery expectations | The academic prototype targets an RPO of no more than 24 hours with a daily backup. Per PA-005 the demonstrated RTO target is a restore to verified state within 15 minutes, performed live on the demo host; 120 minutes is retained as the documented worst case for a full host rebuild. Actual measured results must be recorded. |
| Provide maintainable code | All authored application code is Visual Basic, shared rules are not duplicated across clients, migrations are versioned, and every release has a reproducible build procedure. |
| Provide a consistent user experience | The three clients use the same navigation conventions, validation language, connection-status behavior, focus states, and visual resources. |

The project does not promise high availability, zero downtime, statutory accounting correctness, payment authorization, public-cloud deployment, multi-store operation, or production-grade XAMPP security. Those are outside the academic MVP boundary.

## 6. Approved Technical Direction

### 6.1 WPF clients

The desktop clients will use the modern WPF Application template in Visual Studio 2026 with Visual Basic and .NET 10. Microsoft’s WPF tutorial documents the .NET desktop development workload, the .NET 10 component, the WPF Application template, Visual Basic as an available language, and the instruction to avoid the legacy WPF Application (.NET Framework) template.[5]

WPF is a Windows-only UI framework that provides XAML, controls, data binding, styles, templates, layout, graphics, media, text, and typography.[6] The project uses these capabilities for a polished Windows desktop interface. The specification does not claim that WPF is universally superior to browser-based clients; that is a project choice validated by the Windows-only classroom environment and the desired desktop workflow.

The clients will use MVVM. WPF data binding supports separation between presentation and application data, and Microsoft’s CommunityToolkit.Mvvm provides reusable observable objects, validators, commands, dependency-injection helpers, and related MVVM building blocks.[7] [8] The toolkit does not automatically implement the project’s domain rules, authorization, API boundary, or database transactions. Those remain server responsibilities.

### 6.2 All-Visual-Basic API

The central API will be an ASP.NET Core Web API written in Visual Basic .NET. Microsoft’s Visual Basic team lists ASP.NET Core Web API among the application types supported by Visual Basic beginning with .NET 5.[1] An ASP.NET Core maintainer separately states that Visual Basic is supported in ASP.NET Core, while noting that Visual Basic-based templates are not planned.[2]

The API project will use the ASP.NET Core Web SDK through a manually authored or custom-templated Visual Basic project. Microsoft documents `Microsoft.NET.Sdk.Web` as the SDK for building ASP.NET Core applications and states that it is the recommended SDK for most ASP.NET Core users.[9] The project must not assume that selecting the standard C# Web API template and changing files is sufficient; the manual project structure, package references, startup code, controller/endpoint code, build, publish, and hosting behavior are explicit proof-of-concept items.

The API owns authentication, authorization, cross-module business rules, database access, transaction boundaries, audit events, reporting queries, backup/restore coordination, configuration validation, health checks, and consistent error responses. The clients may validate input for usability, but the API must repeat all business-critical validation because clients cannot be trusted to enforce rules consistently.

### 6.3 Database access and migration decision

The prototype will use MariaDB supplied by XAMPP. To reduce provider and ORM uncertainty, the initial database access strategy will use the MariaDB-recommended .NET connector path and parameterized ADO.NET commands rather than relying on an unverified EF Core provider combination. MariaDB documents .NET connector options and recommends MySqlConnector for MariaDB Server.[10]

The exact MariaDB version, connector package/version, connection settings, supported transaction behavior, and backup command will be pinned during the Foundation Proof-of-Concept. Database changes will use versioned SQL migration scripts executed by the Visual Basic maintenance/migration utility. A `SchemaMigrations` table will record the migration identifier, checksum, execution timestamp, and result. EF Core may be evaluated later, but it is not required for the MVP and is not a hidden dependency of the transaction design.

### 6.4 Kestrel and Windows Service hosting

The API will run on Kestrel. Microsoft identifies Kestrel as the recommended ASP.NET Core web server and documents its default use in ASP.NET Core project templates.[11] The API will be installed as a Windows Service. Microsoft documents ASP.NET Core Windows Service hosting, including service lifetime configuration and startup behavior after a reboot.[4]

The implementation must add the required Windows Service hosting package/configuration, define a service identity, restrict filesystem access, configure event logging, define service recovery actions, and expose a non-sensitive health endpoint. The system will not treat “runs as a Windows Service” as sufficient operational proof until reboot, failure recovery, logging, and health checks have been tested.

## 7. Solution Structure

All project source code must be Visual Basic .NET. The solution contains separate WPF applications, a manually authored Visual Basic API, shared Visual Basic libraries, a Visual Basic maintenance utility, and Visual Basic test projects. The libraries reduce duplication but do not merge the three user-facing applications into one executable.

| Project | Type | Responsibility | Language rule |
|---|---|---|---|
| `Merchandising.Procurement` | WPF Application | Suppliers, purchase orders, approvals, receiving requests, and procurement history. | Visual Basic; XAML for views. |
| `Merchandising.Inventory` | WPF Application | Products, stock visibility, counts, movements, adjustments, and inventory reports. | Visual Basic; XAML for views. |
| `Merchandising.POS` | WPF Application | Cashier sessions, product lookup, cart, checkout, returns, receipt data, and sales history. | Visual Basic; XAML for views. |
| `Merchandising.Api` | ASP.NET Core Web API | Authentication, authorization, business rules, database access, transactions, reporting, audit, and API health. | Visual Basic; manually authored Web SDK project. |
| `Merchandising.Domain` | Shared class library | Domain entities, value objects, enumerations, invariants, and domain services. | Visual Basic. |
| `Merchandising.Contracts` | Shared class library | Request/response DTOs, error models, shared enums, and API contract types. | Visual Basic. |
| `Merchandising.Infrastructure` | Server class library | MariaDB connection handling, parameterized queries, transactions, repositories, audit persistence, and configuration. | Visual Basic. |
| `Merchandising.ClientCommon` | Shared client class library | API client, token/session handling, common controls, converters, styles, validation helpers, and connection-state behavior. | Visual Basic and XAML resources. |
| `Merchandising.Maintenance` | Console/maintenance utility | Migrations, scheduled backup, restore workflow, schema verification, and maintenance logs. | Visual Basic. |
| `Merchandising.Tests.Unit` | Test project | Domain and calculation tests. | Visual Basic test source. |
| `Merchandising.Tests.Integration` | Test project | MariaDB, API, transaction, concurrency, backup/restore, and service tests. | Visual Basic test source. |

The test framework will be selected during the Foundation Proof-of-Concept from a Visual Studio-supported option that permits Visual Basic test source. The framework choice must be recorded in the Architecture Decision Record and must not introduce C# test code as a hidden requirement.

## 8. Client-Server Architecture

The approved architecture is a three-tier local-network design. Client laptops run the WPF applications. The host laptop runs the Visual Basic ASP.NET Core API and XAMPP/MariaDB. Clients never receive MariaDB credentials and never connect directly to MariaDB.

```text
                         Private Store LAN / Wi-Fi

  +-----------------------+        +-----------------------+
  | Procurement Laptop    |        | Inventory Laptop      |
  | VB WPF Application    |        | VB WPF Application    |
  +-----------+-----------+        +-----------+-----------+
              |                                |
              | HTTPS API calls only           |
              |                                |
              +----------------+---------------+
                               |
                    +----------v-----------+
                    | Host Laptop          |
                    | VB ASP.NET Core API  |
                    | Kestrel              |
                    | Windows Service      |
                    +----------+-----------+
                               |
                    Local-only database connection
                               |
                    +----------v-----------+
                    | XAMPP / MariaDB      |
                    | Central prototype DB |
                    | Bind: host only      |
                    +----------------------+
                               ^
                    +----------+-----------+
                    | POS Laptop(s)        |
                    | VB WPF Application   |
                    +----------------------+
```

Production-like demonstration traffic uses HTTPS. The development profile may use HTTP only on an isolated developer machine and must display a non-production warning. The normal client configuration is `https://MERCH-HOST:8443`, where `MERCH-HOST` resolves to the reserved host address and is included in the certificate trust plan. An IP-only URL may be used only if the certificate strategy explicitly supports it.

MariaDB will bind to the host laptop only, preferably through a local-only bind configuration because the API runs on the same host. Windows Firewall will permit the configured HTTPS API port from the store’s private subnet and deny ordinary client access to the MariaDB port. Apache Friends’ documentation confirms that XAMPP includes MariaDB and can install its database component as a Windows service, but the project will use only the required MariaDB component and will not expose the XAMPP administration surface to the store network.[3]

The host laptop must remain powered on and connected while the system is in use. The MVP is online-only. If the host, database, API, certificate, or local network is unavailable, clients must show a clear connection-unavailable state and must not silently complete transactions locally.

The target is approximately 5–10 concurrent sessions. This is a planning workload target, not a documented Kestrel capacity guarantee. It must be validated by a representative load test using product search, reporting reads, receiving, and concurrent sales.

## 9. User Roles, Authentication, and Authorization

The system uses authenticated user accounts and policy-based authorization implemented through the Visual Basic API. A user may have more than one role, but every privileged operation must be attributable to a specific authenticated user. ASP.NET Core supports role-based and policy-based authorization; this project will use policies for sensitive operations such as approvals, returns above a threshold, restores, and price changes.[12]

| Role | Primary responsibilities | Restrictions and approval rules |
|---|---|---|
| **Super Admin** | Full system administration, user/role management, configuration, backup/restore, audit review, emergency administration. | Cannot bypass audit logging. Restore and destructive actions require explicit confirmation, maintenance mode, and a recorded reason. |
| **Admin** | Product/supplier maintenance, purchase approvals, price changes, exceptional returns, adjustment approvals, and reports. | Cannot manage Super Admin accounts. Cannot restore unless explicitly granted a separate policy. |
| **Procurement Officer** | Supplier records, purchase orders, order tracking, receiving preparation. | Cannot approve their own purchase order or restricted receiving action. Cannot modify completed transactional history. |
| **Inventory Clerk** | Product maintenance within permission, receiving confirmation, stock counts, adjustment requests, movement review, and low-stock review. | Adjustments above the configured threshold require Admin approval. Cannot approve their own adjustment. |
| **Cashier** | POS login, product lookup, sales, payment recording, receipt display, permitted returns, and cashier closing. | Cannot change price/cost, edit completed sales, or perform unauthorized adjustments. |

The authentication baseline is a database-backed account system with the following controls:

| Control | Requirement |
|---|---|
| Password storage | Store only salted password hashes using a framework-provided password-hashing implementation; never store plaintext or reversible passwords. |
| Login protection | Lock an account temporarily after five failed attempts within the configured window; log the event without storing the password. |
| Session model | Use a short-lived authenticated API token/session held in client memory; clear it on logout or authentication failure. Exact token implementation is a Foundation Proof-of-Concept decision. |
| Transport | Require HTTPS for production-like operation so credentials and operational data are not sent as ordinary plaintext LAN traffic. |
| Authorization | Enforce policy checks in the API for every protected endpoint. UI hiding is not a security control. |
| Privileged actions | Require reason/confirmation where configured and create an audit record containing actor, timestamp, action, target, result, and correlation identifier. |
| Database credentials | Store only on the host in an ACL-protected configuration location. Never include them in client configuration or source control. |
| Account recovery | Super Admin recovery procedure must be documented and tested without directly editing production transactional data. |

## 10. Functional Scope by Application

### 10.1 Procurement Application

Procurement manages the purchasing workflow from supplier selection through receiving. The MVP includes supplier records, contact information, product selection, purchase orders, purchase-order lines, expected quantities, purchase costs, order statuses, partial receiving, receiving confirmation, purchase returns, and procurement history.

Purchase-order statuses are `Draft`, `Submitted`, `Approved`, `PartiallyReceived`, `FullyReceived`, `Cancelled`, and `Closed`. The allowed transitions are controlled by the API. A user cannot approve their own restricted purchase order. A cancelled order cannot receive goods. A fully received order cannot receive additional quantity unless an authorized override policy is later approved; the MVP default is to reject over-receiving.

When goods are received, the client sends a receiving command containing a unique request identifier. The API validates the order status, line identity, received quantity, duplicate request state, and user permission. The API then creates the goods receipt, receipt lines, immutable stock-in movements, updated stock balances, and audit record in one transaction. Partial receiving is supported. If any step fails, none of the related changes are committed.

Purchase returns are limited to goods previously received through the system. A return request records the receipt line, quantity, reason, user, approval state, and whether stock is removed. The API rejects a return quantity that exceeds the received quantity less prior returns. Supplier financial settlement is outside the MVP; the system records the operational return only.

The MVP excludes supplier quotations, tender comparison, automated bidding, invoice matching, accounts payable, accounting integration, and complex multi-stage procurement approval chains.

### 10.2 Inventory Application

Inventory is the operational control center for products, stock visibility, stock movements, counts, adjustments, and low-stock review. Products contain SKU, name, description, category, brand, unit, optional barcode, recorded purchase cost, current selling price, reorder level, active/inactive state, created/updated metadata, and version/concurrency metadata.

Products have an `Active` or `Inactive` lifecycle. Inactive products cannot be newly sold or newly ordered. Historical transactions continue to display the product name and captured price/cost values even if the product is later deactivated. SKU and barcode uniqueness rules are enforced by the database and API.

The system maintains a stock movement ledger and a current balance projection. Each movement records source type, source identifier, product, quantity delta, quantity before, quantity after, reason, actor, timestamp, and correlation/request identifier. Stock increases through approved receiving and eligible sale returns. Stock decreases through completed POS sales, approved purchase returns where applicable, and approved stock adjustments. Completed movement records are append-only; corrections use compensating movements rather than edits or deletions.

Stock counts record the counted quantity, system quantity, variance, count session, counted by, reviewed by, reason, and approval state. Adjustment approval is required when the absolute variance exceeds the configured threshold. The API uses an atomic conditional update or a tested locking/isolation strategy so that two concurrent stock-changing commands cannot both consume the same remaining quantity.

The MVP supports one store location and one central stock pool. It excludes branches, warehouses, inter-location transfers, batch tracking, serial-number tracking, expiry tracking, and automatic replenishment.

### 10.3 POS Application

POS supports online-only sales from laptop computers without specialized hardware. A cashier can search by SKU, optional barcode, or product name; add products to a cart; modify quantities; review available stock; record an allowed payment; calculate cash change; complete the sale; and display receipt data.

A sale requires an open cashier session. The sale command includes a client-generated idempotency key. The API re-checks product activity, current price, available stock, duplicate request state, and payment validity. It captures the effective unit price and recorded cost in the sale line so future price changes do not alter historical sales analysis.

The MVP supports cash, card, and e-wallet **recording**. It does not authorize card/e-wallet payments or integrate with payment terminals, banks, cash drawers, weighing scales, customer displays, or receipt printers. A standard keyboard-input barcode scanner may be added later without changing the core sales workflow.

For cash payments, the API verifies that the tendered amount is at least the sale total and calculates change using fixed-precision decimal arithmetic. For card/e-wallet recording, the API records the selected method and amount but does not claim that payment was externally authorized. The application must make this distinction clear to users.

A sale completion creates the sale header, sale lines, payment record(s), stock-out movements, updated stock balances, cashier-session totals, and audit record in one transaction. The API must reject the operation if stock is insufficient or if an idempotency key has already produced a completed result.

Completed sales are never edited or deleted. A sale may be cancelled only before completion. A completed-sale return must identify the original sale line, cannot exceed the quantity sold minus prior returns, and records whether the returned item is eligible to re-enter stock. Payment reversal is recorded operationally; no bank or terminal reversal is performed by the MVP.

Cashier daily closing records the session totals by payment method, declared cash, calculated cash, variance, closing user, timestamp, and approval/escalation status where configured.

## 11. Shared Business Rules and Transactional Data Flow

The API is authoritative for all rules affecting more than one module. WPF clients may perform immediate validation for usability, but every business-critical rule is repeated on the API. No client may directly write the database.

| Business event | Required atomic result |
|---|---|
| Product created/deactivated | Product master and audit event are committed together; inactive products are rejected by sale/order commands. |
| Purchase order approved | Status changes, approval actor/time, and audit event commit together. |
| Goods received | Receipt, receipt lines, stock-in movements, balance changes, and audit event commit together. |
| Sale completed | Sale, sale lines, payment, stock-out movements, balance changes, session totals, and audit event commit together. |
| Sale returned | Return, return lines, operational payment-reversal record, eligible stock-in movement, balance change, and audit event commit together. |
| Stock count approved | Count, variance, approval, adjustment movement if applicable, balance change, and audit event commit together. |
| Price changed | Current price, price-history record, and audit event commit together. |
| Backup/restore performed | Maintenance log, result, operator/scheduler, file reference, and post-operation verification status are recorded. |

Every stock-changing operation must either commit all related changes or commit none. Microsoft documents the atomicity concept for EF Core transactions and relational providers; this project will implement and test the equivalent behavior using the selected MariaDB-compatible ADO.NET provider and explicit database transactions.[13]

Negative stock is prevented by default. The sale transaction must perform a server-side conditional update such as “decrement only when current quantity is greater than or equal to requested quantity,” then verify the affected-row count before creating the completed sale result. If another transaction wins the race, the losing command returns a controlled insufficient-stock or concurrency response and does not create partial records.

Every command that creates or changes a transaction must include or generate a correlation identifier. Client retries with the same idempotency key must return the original committed result rather than creating duplicate sales, receipts, returns, or adjustments.

## 12. Initial Data Model and Integrity Rules

The following entities represent the MVP database foundation. Exact columns, names, and indexes are finalized in the database design deliverable, but the invariants below are mandatory planning constraints.

| Entity group | Main entities | Core integrity requirements |
|---|---|---|
| Identity/security | `Users`, `Roles`, `UserRoles`, `PermissionPolicies`, `AuditLogs` | Unique login name; password hash only; role assignment audit; append-only audit records. |
| Product master | `Products`, `Categories`, `Brands`, `Units`, `ProductBarcodes`, `PriceHistory` | Unique SKU; unique active barcode; active/inactive lifecycle; price/cost history; no deletion of referenced products. |
| Procurement | `Suppliers`, `PurchaseOrders`, `PurchaseOrderItems`, `GoodsReceipts`, `GoodsReceiptItems`, `PurchaseReturns`, `PurchaseReturnItems` | Valid status transitions; receipt quantity bounded by ordered quantity by default; return quantity bounded by received quantity less prior returns. |
| Inventory | `StockBalances`, `StockMovements`, `StockCounts`, `StockCountItems`, `StockAdjustments` | One balance per product in the single stock pool; movement ledger append-only; balance changes only through authorized transactions. |
| POS | `Sales`, `SaleItems`, `Payments`, `SalesReturns`, `SalesReturnItems`, `CashierSessions`, `CashierClosings` | Sale line captures effective price/cost; payment totals reconcile; return quantities bounded; completed sale immutable. |
| Configuration/operations | `SystemSettings`, `SchemaMigrations`, `BackupLogs`, `ApplicationEvents`, `MaintenanceLocks` | Configuration changes audited; migrations checksummed; backup/restore recorded; maintenance mode prevents normal writes. |

The database will use primary keys, foreign keys, unique constraints, and indexes for SKU, barcode, product name, supplier name, status, dates, and transaction references. Transactional monetary values use fixed-precision decimal storage rather than floating-point types. Money is `DECIMAL(19,4)` and quantities are `DECIMAL(19,3)`, decided on measured round-trip evidence and recorded as ADR-004 / PA-003. Display rounding is defined separately from storage precision.

All timestamps are stored in UTC and displayed in the configured store time zone. The system stores a configurable currency code and uses one defined rounding policy. The MVP does not calculate tax, file tax returns, produce statutory accounting statements, or integrate with accounting software. Gross-margin displays are informational estimates using recorded cost and selling price, not accounting profit statements.

Transactional records are never physically deleted. Master data is deactivated where possible. Foreign-key behavior must prevent accidental deletion of records referenced by sales, receipts, returns, movements, or audit events.

## 13. API Contract and Error Model

The API is versioned under `/api/v1`. It exposes only required endpoints over the private network and requires authentication for all operations except a health endpoint that reveals no secrets or operational credentials. Controller-based endpoints may use ASP.NET Core’s documented routing, model binding, automatic validation, and problem-details behavior.[14]

| API area | Representative endpoints | Required authorization |
|---|---|---|
| Authentication | `POST /api/v1/auth/login`, `POST /api/v1/auth/logout`, `GET /api/v1/auth/me` | Anonymous only for login; authenticated for others. |
| Products | `GET/POST /api/v1/products`, `PUT /api/v1/products/{id}`, `POST /api/v1/products/{id}/deactivate` | Read by operational roles; changes by Admin/authorized Inventory. |
| Suppliers | `GET/POST /api/v1/suppliers`, `PUT /api/v1/suppliers/{id}` | Procurement/Admin. |
| Procurement | `POST /purchase-orders`, `POST /purchase-orders/{id}/submit`, `POST /purchase-orders/{id}/approve`, `POST /purchase-orders/{id}/receive` | Role and policy checks; self-approval prohibited. |
| Inventory | `GET /stock`, `GET /stock/movements`, `POST /stock-counts`, `POST /adjustments`, `POST /adjustments/{id}/approve` | Inventory/Admin policies. |
| POS | `POST /cashier-sessions`, `POST /sales`, `POST /sales/{id}/returns`, `POST /cashier-sessions/{id}/close` | Cashier/Admin policies. |
| Reports | `GET /reports/sales`, `GET /reports/stock`, `GET /reports/purchases`, `GET /reports/movements` | Report permission by role; filters validated. |
| Operations | `GET /health`, `POST /maintenance/request`, `GET /backups` | Health is limited; maintenance is Super Admin or approved policy. |

Every error response has a stable error code, human-readable message, correlation identifier, and field-level validation details when applicable. Internal exception details and database credentials are never returned to clients. List endpoints define pagination, maximum page size, sorting, filtering, and date-boundary behavior. All write commands define idempotency requirements.

## 14. Reporting Scope and Calculation Definitions

Reports provide operational information rather than accounting statements. All reports must define date filters using the store time zone, show the selected date range, and indicate whether returns/cancellations are included or excluded. Report queries use transaction source data and must reconcile to the corresponding detail screens.

| Report | Required definition |
|---|---|
| Daily sales summary | Completed sales, completed returns, net sales total, transaction count, and payment totals for the selected store-local day. |
| Sales by product | Quantity sold, returned quantity, net quantity, gross sales value, and captured cost basis for the selected period. |
| Sales by cashier | Completed sales, returns, net value, transaction count, and payment totals by cashier/session. |
| Payment-method summary | Recorded cash, card, and e-wallet totals; explicitly labeled as operational recordings, not external settlement confirmation. |
| Returns and cancellations | Return/cancellation identifiers, source sale, product, quantity, reason, actor, approval, and stock effect. |
| Purchase-order history | Supplier, order number, statuses, ordered quantity/value, received quantity/value, and outstanding quantity. |
| Goods-receiving history | Receipt, supplier, date, product, ordered quantity, received quantity, and responsible user. |
| Current stock | Product, category, active state, current quantity, reorder level, and stock status. |
| Low-stock report | Active products at or below reorder level, with current quantity and reorder level. |
| Stock movement report | Movement source, product, delta, before/after quantity, user, time, reason, and correlation identifier. |
| Stock-adjustment report | Count/adjustment, variance, reason, requestor, approver, result, and stock effect. |
| Product performance summary | Net quantity, net sales value, recorded cost estimate, informational margin estimate, and current stock position. |

CSV exports use UTF-8, include a header row, use invariant field ordering, escape delimiters/quotes correctly, and include report parameters in the export metadata or filename. Export permissions follow report permissions.

## 15. Backup, Restore, and Recovery Operations

Backup and restore are included because the host laptop is a single point of failure. A local backup folder alone is not sufficient protection against host failure, theft, disk failure, or ransomware. The documented procedure therefore requires a protected local backup directory plus periodic copying to a separate physical drive or trusted cloud storage as permitted by the course environment.

The scheduled backup will be performed by the Visual Basic `Merchandising.Maintenance` utility through Windows Task Scheduler. The utility will use a version-compatible MariaDB logical-backup command selected during the Foundation Proof-of-Concept, preferably `mariadb-dump` when available in the installed distribution. The exact executable path, command options, backup account, and version will be recorded in the installation guide. The backup account will not be the root account.

| Backup control | Requirement |
|---|---|
| Schedule | Daily after the store’s defined closing or low-activity window. |
| Retention | Configurable count, with the configured value recorded in `SystemSettings`. |
| Location | Protected host directory outside the application binaries and not served by the API. |
| Off-host copy | Periodic copy to a separate physical drive or approved cloud location. |
| Integrity | Record file size, checksum, timestamp, source database version, and result. |
| Security | Restrict directory permissions to the maintenance/service administrators; never expose backup files through ordinary API endpoints. |
| Failure handling | Record error details, display an operational warning, and require follow-up before acceptance. |
| Verification | Test restore against an isolated database or controlled maintenance window, not merely file existence. |

Restore is a disruptive maintenance operation. It is not executed as a normal request inside a live API process that depends on the database. The approved procedure is:

1. A Super Admin requests maintenance mode through the authorized administrative workflow and provides a reason.
2. The API writes a maintenance lock, rejects new ordinary writes, and displays a warning to connected clients.
3. Users close client applications and the operator confirms the maintenance window.
4. The operator runs the Visual Basic maintenance utility on the host with the selected backup file.
5. The utility stops or coordinates the API/database services as required, restores the database, applies/validates the expected schema, and records a local maintenance log.
6. The operator restarts services, runs health and data-integrity checks, and confirms that the expected users, products, balances, and recent transactions are present.
7. The API records the completed restore event after service recovery and releases maintenance mode only after verification succeeds.

The project must measure and document the actual recovery time. Per PA-005 the prototype targets an RPO of no more than 24 hours and a demonstrated RTO of no more than 15 minutes for a restore to verified state on the demo host, with 120 minutes retained as the documented worst case for a full host rebuild. Both are validated by test evidence.

## 16. User Interface and Experience

The visual direction is **premium, modern, calm, and lightweight**, inspired by the clarity associated with high-quality desktop applications without copying Apple branding, icons, or proprietary visual assets. The design prioritizes scanability and speed, especially in POS, over decorative effects.

The WPF applications use XAML resource dictionaries, reusable styles, templates, converters, custom controls where justified, and shared client resources. The initial theme is light-first, with the resource architecture left open to a future dark theme. The applications must support keyboard navigation, visible focus states, predictable tab order, clear validation messages, loading indicators, empty states, retry actions, and connection-unavailable states.

The common shell includes current user, active role/context, module navigation, connection status, notifications, maintenance state, and contextual actions. Procurement and Inventory use data grids and detail panels. POS uses a fast checkout layout with prominent search, cart contents, totals, payment controls, completion feedback, and receipt display.

The planning baseline targets laptop screens at 1366×768 and above, with a minimum supported resolution and Windows scaling factor recorded during UI verification. Screens must remain usable at 125% display scaling where the classroom hardware requires it. POS keyboard workflows are tested without a mouse for search, cart changes, payment, completion, and session closing.

## 17. Security and Operational Controls

The system applies least privilege, keeps database credentials on the host, validates every API request, authorizes every protected operation, and records sensitive actions. ASP.NET Core role/policy authorization provides the mechanism, but the project owns the role matrix, policies, approval thresholds, and audit semantics.[12]

| Control area | Revised requirement and mitigation |
|---|---|
| Client/database boundary | MariaDB binds to the host; clients cannot connect directly. Negative network tests are required. |
| XAMPP exposure | Disable unused components; do not expose the XAMPP dashboard/phpMyAdmin to the store LAN; never expose XAMPP to the public internet. |
| Database accounts | Use separate least-privilege API and backup accounts. Do not use root from the application. |
| API credentials | Store only in an ACL-protected host configuration location or approved Windows-protected secret mechanism. |
| Transport | HTTPS is mandatory for production-like demonstration. Development HTTP is isolated and labeled non-production. |
| Firewall | Allow only the API HTTPS port from the approved private subnet; deny database port from clients and unnecessary inbound traffic. |
| Host identity | Use a reserved IP and stable host name; document DNS/hosts configuration and certificate name. |
| Windows Service | Use a restricted service identity where practical; define startup, recovery, event logging, and filesystem permissions. |
| Audit | Append-only records for user, time, operation, target, before/after summary where safe, result, reason, and correlation ID. Never log passwords or sensitive tokens. |
| Input security | Use parameterized database commands, request-size limits, validation, controlled file paths, and safe error handling. |
| Maintenance | Backup/restore requires maintenance mode, explicit confirmation, operator identity, reason, and post-restore verification. |
| Patch policy | Record the installed Windows, .NET, Visual Studio, XAMPP, MariaDB, and provider versions and update them only through a tested change procedure. |

## 18. Deployment Strategy

The project will target Windows x64 and explicitly record the `win-x64` runtime identifier. Microsoft documents framework-dependent and self-contained .NET publishing. Framework-dependent deployments require the appropriate runtime on the target, while self-contained deployments include the runtime and are platform-specific.[15]

The baseline deployment choice is framework-dependent WPF clients with a prerequisite check for the .NET 10 Desktop Runtime. The API may be framework-dependent if the appropriate ASP.NET Core/.NET runtime is deliberately installed and verified on the host; otherwise, the API will use a self-contained `win-x64` publish to reduce runtime-installation risk. This decision is made during the Foundation Proof-of-Concept and recorded in the release manifest.

| Deployment component | Baseline |
|---|---|
| WPF clients | Versioned `win-x64` framework-dependent packages, with .NET 10 Desktop Runtime prerequisite verification. |
| API Windows Service | Versioned `win-x64` package; framework-dependent only with verified host runtime, otherwise self-contained. |
| Maintenance utility | Versioned `win-x64` package with the same runtime strategy recorded in the release manifest. |
| Database schema | Numbered SQL migrations applied by the Visual Basic maintenance utility; migration result recorded. |
| Configuration | Client API URL and certificate/trust configuration are external to binaries; no database credentials on clients. |
| Certificate | Trusted on clients through the documented classroom procedure; hostname/IP strategy tested before UAT. |
| XAMPP/MariaDB | Installed and configured on host; MariaDB service startup, local bind, account setup, and firewall verified. |
| Versioning | Every release includes version number, commit/source identifier, migration list, release notes, rollback instructions, and tested package hashes. |

During the initial pilot, a controlled shared-folder update procedure may be used. Microsoft documents XCopy as simple but notes that it does not provide versioning, uninstallation, or rollback; Microsoft’s WPF deployment documentation identifies ClickOnce and Windows Installer as stronger deployment alternatives.[16] The shared-folder process therefore requires a release folder per version, client closure before replacement, package checksum verification, migration backup, database migration rehearsal, and a rollback copy. A formal installer or ClickOnce deployment will be considered if updates become frequent or nontechnical users must install them.

## 19. Testing Strategy and Acceptance Evidence

Testing covers individual application behavior and cross-module consistency. A transaction is correct only when the database records, stock balance, movement ledger, audit trail, and reports are all correct.

| Test level | Required coverage and mitigation |
|---|---|
| Foundation proof-of-concept | Manual VB API project creation/build/publish; MariaDB connection; migration; authentication; HTTPS; Windows Service; WPF client call; backup/restore; transaction rollback. |
| Unit tests | Product validation, price/quantity rules, permission policies, state transitions, money/change calculations, return eligibility, reorder logic, report formulas, and idempotency behavior. |
| API tests | Authentication, authorization, request validation, error model, problem details, pagination, product, procurement, receiving, inventory, POS, reports, maintenance authorization, and health endpoint. |
| Database integration tests | Foreign keys, unique constraints, decimal precision, migration checksums, transaction rollback, conditional stock updates, concurrent sales, partial receiving, returns, and adjustment approvals against the real MariaDB version. |
| Client tests | ViewModel commands, input validation, navigation, focus/tab order, loading/error/empty states, connection failures, token expiration, maintenance mode, and permission-aware controls. |
| Concurrency tests | Two or more simultaneous sales against limited stock; repeated identical idempotency keys; simultaneous receiving/adjustment conflicts; no duplicate or negative stock. |
| Security tests | Client-to-database port denial, unauthorized endpoint denial, role escalation denial, self-approval denial, secret non-disclosure, invalid certificate behavior, and audit creation. |
| Backup/recovery tests | Scheduled backup, failed backup alert, off-host copy, isolated restore, maintenance restore, schema verification, data reconciliation, measured RPO/RTO. |
| Deployment tests | Clean client install, host service install, reboot start, service recovery, runtime verification, certificate trust, migration upgrade, rollback rehearsal, and configuration validation. |
| Performance tests | Approved 5–10 session profile with product lookup, report reads, receiving, and concurrent sales; record p50/p95 response times and error rates. |
| End-to-end tests | Purchase order → approval → partial/full receiving → stock increase; product → sale → stock decrease; sale → eligible return → stock increase; count → approval → adjustment; backup → restore → verification. |
| User acceptance tests | Representative workflows executed by Super Admin, Admin, Procurement Officer, Inventory Clerk, and Cashier on the intended laptops and private network. |

Acceptance must verify API unavailability, missing permission, inactive product, insufficient stock, partial receiving, duplicate command retry, conflicting stock changes, invalid payment, failed backup, interrupted maintenance, and failed certificate trust. Each test case records setup, action, expected result, actual result, evidence, and pass/fail status.

## 20. Documentation Deliverables

The MVP project produces the following documents alongside the source code. Each deliverable has an owner and approval point in the implementation plan.

| Document | Required content |
|---|---|
| Revised requirements specification | Scope, assumptions, constraints, business rules, exclusions, measurable acceptance criteria, and this mitigation register. |
| Architecture decision record | All-Visual-Basic API decision, manual Web SDK setup, XAMPP academic classification, provider, migration approach, HTTPS, runtime model, and Windows Service design. |
| Database design | ERD, data dictionary, relationships, keys, constraints, indexes, decimal precision, time zone, migrations, seed data, and retention rules. |
| API specification | Versioned endpoints, DTOs, authorization, validation, idempotency, pagination, error model, correlation IDs, and health behavior. |
| Role-permission matrix | Operation-level permissions, role combinations, approval thresholds, self-approval rules, and emergency procedures. |
| UI specification | Screen inventory, navigation, workflows, visual system, validation states, keyboard behavior, connection states, and target resolutions. |
| Installation guide | Windows prerequisites, Visual Basic/.NET packages, XAMPP/MariaDB setup, accounts, API Windows Service, certificate, firewall, client configuration, and verification. |
| Backup and restore guide | Schedule, command, account, retention, off-host copy, maintenance restore, verification checklist, RPO/RTO, and escalation. |
| User guide | Procurement, Inventory, POS, Admin, and Super Admin operating procedures. |
| Test plan and results | Test cases, data, environment, execution results, defects, performance evidence, recovery evidence, and acceptance sign-off. |
| Release package | Version, source identifier, hashes, migrations, release notes, prerequisites, rollback instructions, and known limitations. |

## 21. Explicit MVP Exclusions

The initial release excludes accounting; accounts payable and receivable; statutory tax filing; advanced tax computation; e-commerce; online ordering; mobile applications; customer loyalty; customer-specific pricing; promotions and coupons; multi-branch or multi-warehouse operation; stock transfers; offline POS; automatic synchronization; payment-terminal integration; receipt-printer and cash-drawer integration; weighing-scale integration; batch, expiry, and serial-number tracking; supplier tendering; automated replenishment; advanced forecasting; cloud hosting; formal multi-tenant operation; external payment authorization; and production-grade high availability.

These exclusions are scope controls, not permanent prohibitions. Future features must use the central API and domain model, preserve transaction consistency, respect authorization policies, and pass a new feasibility and security review before being added.

## 22. Implementation Phases and Decision Gates

The first phase is a technical foundation gate rather than a broad feature phase. This is necessary because the API requires custom Visual Basic setup and the course-mandated XAMPP environment must be validated before the rest of the system is built.

| Phase | Scope | Exit criteria |
|---|---|---|
| **Phase 1: Foundation Proof-of-Concept** | Create the Visual Studio solution; manually create the Visual Basic ASP.NET Core API; build/publish; connect to exact XAMPP/MariaDB; run a migration; implement health check, authentication, one protected endpoint, HTTPS, Windows Service hosting, one WPF API call, one atomic stock transaction, rollback test, backup, and restore rehearsal. | All-Visual-Basic source builds; API runs; database connection and migration pass; authentication/authorization pass; HTTPS works; Windows Service survives reboot; client cannot reach MariaDB; transaction/concurrency tests pass; backup/restore evidence exists. |
| **Phase 2: Identity and Master Data** | Users, roles, policies, audit logging, settings, products, categories, brands, units, barcodes, price history, and suppliers. | Role-permission tests pass; audit records reconcile; product lifecycle and uniqueness constraints pass; seed data is available. |
| **Phase 3: Procurement Primitives** | Suppliers, purchase orders, order lines, statuses, approval policy, purchase history, and cancellation/closure rules. | Status transitions and self-approval rules pass; purchase orders can be approved and traced. |
| **Phase 4: Inventory and Receiving** | Stock balances, movement ledger, receiving, partial receiving, purchase returns, stock counts, adjustments, approvals, low-stock logic, and reconciliation. | Receiving produces exactly one atomic stock increase; partial/over-receiving rules pass; stock ledger reconciles; concurrent changes are safe. |
| **Phase 5: POS** | Cashier sessions, product lookup, cart, atomic sale, payments, change calculation, returns, receipt data, sales history, and daily closing. | End-to-end sale/return flows pass; negative stock is prevented; payment recording is clearly non-integrated; idempotent retries pass. |
| **Phase 6: Reporting and Operations** | Operational reports, CSV export, scheduled backup, maintenance restore, health monitoring, release packaging, installation documents, and user guide. | Reports reconcile to source data; CSV exports pass; backup/restore evidence meets documented targets; clean installation succeeds. |
| **Phase 7: Hardening and Acceptance** | Security review, concurrency and performance tests, recovery tests, UI refinement, deployment rehearsal, training, UAT, defect correction, and MVP sign-off. | All critical defects closed; acceptance suite passes; measured performance/RPO/RTO recorded; professor/store representative signs off. |

## 23. Gap Mitigation Register

This register converts every material gap identified during the prior audit into an explicit mitigation. A gap is not considered closed merely because a design statement exists; the indicated evidence must be produced at the specified gate.

| ID | Identified gap or risk | Mitigation | Evidence / closure gate |
|---|---|---|---|
| G-01 | Standard Visual Basic ASP.NET Core Web API template is not provided | Manually author the Visual Basic Web SDK API or create a custom template; prohibit C# source; test build/publish before feature work. | Phase 1 build and publish logs; source review. |
| G-02 | All-Visual-Basic server support was previously uncertain | Use Microsoft’s explicit support statement and official ASP.NET Core maintainer statement as the basis; prove the exact target framework and packages in the POC.[1] [2] | Phase 1 protected endpoint and WPF client call. |
| G-03 | XAMPP is not production-ready | Classify the system as an academic prototype; bind MariaDB locally; harden accounts/firewall; disable unused components; do not expose public internet. | Installation checklist, port scan from client, configuration screenshots/logs, limitation statement. |
| G-04 | MariaDB provider/version compatibility is unspecified | Pin MariaDB version, MySqlConnector version, .NET target, connection settings, and transaction behavior; use parameterized ADO.NET and SQL migrations initially. | Phase 1 provider decision record and integration tests. |
| G-05 | EF Core provider transaction behavior was not proven | Remove EF Core as an MVP dependency; use explicit provider transactions, or separately prove any EF Core adoption against exact MariaDB. | Transaction commit/rollback and concurrency test evidence. |
| G-06 | Capacity of 5–10 concurrent sessions was assumed | Define representative workload and performance targets; load-test the actual host, database, LAN, and client mix. | Phase 7 performance report with p50/p95/error rates. |
| G-07 | HTTP fallback weakened security | Make HTTPS mandatory for production-like use; keep HTTP only for isolated development; define certificate/trust procedure. | Phase 1 certificate test and invalid-certificate behavior test. |
| G-08 | Host laptop is a single point of failure | Document power/restart/network/service failure procedures; maintain off-host backups; define RPO/RTO; rehearse recovery. | Phase 6/7 recovery report and operating checklist. |
| G-09 | API service hosting omitted service details | Add Windows Service package/configuration, service identity, event logging, recovery actions, health endpoint, and file permissions. | Reboot, crash recovery, log, and health evidence in Phase 1. |
| G-10 | Database credentials could leak to clients | Store credentials only on host; use least-privilege API and backup accounts; never put credentials in client configuration or source. | Configuration review and client package inspection. |
| G-11 | Negative stock under concurrent sales was underspecified | Use server-side conditional decrement or tested locking/isolation; return controlled conflict; use idempotency keys. | Two-client last-stock concurrency test. |
| G-12 | Transaction boundaries were described but not operationally specified | Define transaction contents for receiving, sale, return, adjustment, and price change; record correlation IDs; use explicit rollback tests. | Database integration suite and reconciliation report. |
| G-13 | Duplicate retries could create duplicate transactions | Require client-generated idempotency keys for transactional commands and persist command results. | Repeated command tests in API/integration suite. |
| G-14 | Restore through a live API could disrupt its own database | Use a separate Visual Basic maintenance utility and maintenance mode; stop/restart services as required; verify after restore. | Restore rehearsal with maintenance log and post-restore checklist. |
| G-15 | Backup details were incomplete | Define tool, account, schedule, retention, protected folder, checksum, off-host copy, failure alert, and restore test. | Scheduled backup and restore evidence. |
| G-16 | Deployment runtime requirements were ambiguous | Name `win-x64`; record client Desktop Runtime and host API runtime/self-contained choice; verify at installation. | Release manifest and clean-machine installation test. |
| G-17 | Shared-folder updates lacked rollback controls | Use versioned folders, checksums, client shutdown, backup-before-migration, rollback copy, and migration rehearsal; evaluate ClickOnce/installer later.[16] | Deployment rehearsal and rollback evidence. |
| G-18 | Reference numbering in the original deployment section was incorrect | Correct publishing citation to [15] and WPF deployment citation to [16]. | This revised document and future copies use corrected references. |
| G-19 | Authentication and role controls lacked implementation detail | Define password hashing, lockout, token/session behavior, policy authorization, account recovery, and audit rules. | Security tests and role-permission matrix. |
| G-20 | Audit immutability was not enforceable | Make audit records append-only by application policy and database privileges; prohibit ordinary update/delete; record before/after summaries without secrets. | Negative audit-modification test and schema review. |
| G-21 | Data model lacked invariants and precision | Define keys, foreign keys, unique constraints, indexes, decimal precision, time zone, lifecycle/status rules, and migration checksums. | Approved ERD/data dictionary and integration tests. |
| G-22 | Report formulas and date boundaries were ambiguous | Define report calculations, returns treatment, cost basis, rounding, time zone, filters, permissions, and reconciliation samples. | Report specification and reconciliation tests. |
| G-23 | Receiving phase preceded procurement dependency | Reorder phases so procurement primitives precede receiving integration. | Revised Phase 3/4 plan and dependency review. |
| G-24 | Payment recording could be mistaken for payment authorization | Label card/e-wallet as operational recording only; exclude terminal/bank integration; test UI wording and reports. | UAT evidence and report labels. |
| G-25 | Network/certificate naming could cause client failures | Use reserved host name/address; define certificate subject/SAN and client trust installation; test all clients. | Installation verification on intended laptops. |
| G-26 | Offline behavior was not sufficiently explicit | Make online-only status visible; reject writes when API unavailable; do not queue unverified transactions. | API outage and client-state tests. |
| G-27 | UI target hardware and scaling were unspecified | Define target resolution/scaling and test keyboard/focus behavior, loading states, error states, and POS speed. | UI verification report and UAT. |
| G-28 | Operations ownership was unspecified | Assign responsibility for installation, backup, restore, patching, service restart, certificate renewal, user administration, and incident escalation. | Installation/operations guide with named role owners. |
| G-29 | Production versus academic claims could be confused | Mark all documentation and demonstrations as academic prototype; list XAMPP security limitations and excluded production controls. | Cover-page constraint statement and presentation notes. |
| G-30 | Full planning readiness depended on unverified environment assumptions | Record exact Windows, Visual Studio, .NET, XAMPP, MariaDB, provider, network, and hardware versions in the environment baseline. | Phase 1 environment manifest. |

## 24. Foundation Proof-of-Concept Specification

The Foundation Proof-of-Concept is the mandatory first implementation increment. It is intentionally small and must be completed before the team implements all application modules.

The proof-of-concept will contain one Visual Basic WPF client, one manually authored Visual Basic ASP.NET Core API, the shared Contracts library, the Infrastructure library, the Domain library, the Maintenance utility, and the selected Visual Basic test project. It will not attempt to implement the full Procurement, Inventory, or POS screens.

The proof-of-concept must demonstrate the following sequence:

1. Create and load the solution with no C# application source.
2. Build the Visual Basic ASP.NET Core Web SDK API and run a health endpoint.
3. Connect the API to the exact MariaDB instance supplied through XAMPP.
4. Apply a numbered SQL migration and record it in `SchemaMigrations`.
5. Create a test user, authenticate, and call one protected endpoint.
6. Connect one WPF client to the API over HTTPS and display the result.
7. Execute a stock decrement that creates a movement and audit record in one transaction.
8. Force a failure and prove that the sale/movement/balance/audit changes roll back together.
9. Execute two concurrent stock-changing requests and prove that the result cannot become negative or duplicate.
10. Publish the API and install it as a Windows Service; reboot the host and verify recovery.
11. Run the scheduled-backup utility, copy a backup to a separate location, and restore it through the maintenance procedure.
12. Verify that a client laptop cannot connect to MariaDB and that an unauthorized API request is rejected.

| Exit criterion | Required evidence |
|---|---|
| All-Visual-Basic source | Project/source inventory and build output show no C# application files. |
| API viability | Manual project builds, runs, publishes, and responds to a protected request. |
| Database viability | Exact MariaDB/provider combination connects, migrates, commits, and rolls back. |
| Security boundary | HTTPS works; credentials are host-only; client database-port connection fails; unauthorized requests fail. |
| Service viability | Windows Service starts after reboot, logs correctly, and recovers from a controlled stop. |
| Recovery viability | Backup file is valid, off-host copy exists, restore succeeds, and data verification passes. |
| Planning baseline | Provider, project setup, API language, migration, runtime, certificate, transaction, and recovery decisions are recorded. |

If the proof-of-concept fails, implementation pauses at the failed decision. The team must either correct the custom Visual Basic project/provider setup or obtain a documented professor-approved exception. No full-module work should proceed while the API foundation remains unproven.

## 25. MVP Acceptance Statement

The academic MVP is ready for professor/store acceptance when the three separate Visual Basic WPF applications can connect to the Visual Basic ASP.NET Core API over the private LAN using the documented HTTPS configuration; authorized users can log in and see only permitted functions; Procurement can create and approve purchase orders and record partial/full receiving; Inventory can show accurate stock and immutable movements; POS can complete online sales and record operational payments; completed sales reduce stock atomically; eligible returns restore stock; audit records are created for sensitive actions; reports reconcile with source transactions; the Visual Basic maintenance utility can create and restore a backup; the host can recover the API/database procedure after restart; and the documented installation, recovery, and operating procedures have been tested successfully.

Acceptance is conditional on the academic constraints being visible in the documentation. The system must not be described as production-ready because XAMPP is not intended for production use.[3] The final presentation must state that the prototype is limited to one store, one central host, a private LAN, online-only operation, recorded rather than externally authorized payments, and the defined MVP exclusions.

## 26. References

[1]: https://devblogs.microsoft.com/vbteam/visual-basic-support-planned-for-net-5-0/ "Visual Basic support planned for .NET 5.0 - Visual Basic Blog | Microsoft"

[2]: https://github.com/dotnet/aspnetcore/issues/40954 "Add ASP.NET Core Visual Basic project template - dotnet/aspnetcore issue #40954"

[3]: https://www.apachefriends.org/faq_windows.html "XAMPP FAQs for Windows | Apache Friends"

[4]: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/windows-service?view=aspnetcore-10.0 "Host ASP.NET Core in a Windows Service - Microsoft Learn"

[5]: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/get-started/create-app-visual-studio "Create a WPF app with Visual Studio tutorial - WPF | Microsoft Learn"

[6]: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/ "What is Windows Presentation Foundation - WPF | Microsoft Learn"

[7]: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/data/ "Data binding overview - WPF | Microsoft Learn"

[8]: https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/ "Introduction to the MVVM Toolkit - Microsoft Learn"

[9]: https://learn.microsoft.com/en-us/aspnet/core/razor-pages/web-sdk?view=aspnetcore-10.0 "ASP.NET Core Web SDK - Microsoft Learn"

[10]: https://mariadb.com/docs/connectors/mariadb-connector-net ".NET Connector - MariaDB Documentation"

[11]: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel?view=aspnetcore-10.0 "Kestrel web server in ASP.NET Core - Microsoft Learn"

[12]: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/introduction?view=aspnetcore-10.0 "Introduction to authorization in ASP.NET Core - Microsoft Learn"

[13]: https://learn.microsoft.com/en-us/ef/core/saving/transactions "Transactions - EF Core | Microsoft Learn"

[14]: https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-10.0 "Create web APIs with ASP.NET Core - Microsoft Learn"

[15]: https://learn.microsoft.com/en-us/dotnet/core/deploying/ ".NET application publishing overview - Microsoft Learn"

[16]: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/app-development/deploying-a-wpf-application-wpf "Deploy a WPF Application - Microsoft Learn"
