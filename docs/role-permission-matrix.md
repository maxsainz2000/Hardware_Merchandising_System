# Role / Permission Matrix

Generated from `Merchandising.Domain.Security.PolicyRegistry.Definitions` (P2-02 / ADR-017).
`RolePermissionMatrixDocumentationTests` regenerates this file's content from the same
registry and asserts it is unchanged - do not hand-edit this file. Change
`PolicyRegistry.vb` and regenerate instead.

Out of scope: user/role management has no HTTP policy at all. Spec section 13's API
area table has no "Users" row - accounts are created only through the
`Merchandising.Maintenance` CLI's `create-user` (P1-08), never over HTTP.

**Scope note.** This system is an **academic prototype**. MariaDB is supplied through XAMPP
because the course requires it (ADR-000), and XAMPP is documented by Apache Friends as
intended for development environments. Nothing in this document should be read as a
production-readiness claim.

## SuperAdmin

- `Products.Read` - View product master data.
- `Products.Manage` - Create, update or deactivate a product (excluding price/cost).
- `Products.ChangePrice` - Change a product's selling price or recorded cost.
- `Suppliers.Read` - View supplier records.
- `Suppliers.Manage` - Create or update a supplier record.
- `PurchaseOrders.Create` - Create a purchase order.
- `PurchaseOrders.Submit` - Submit a draft purchase order for approval.
- `PurchaseOrders.Approve` - Approve a submitted purchase order. Also requires the actor not be the order's own creator (SelfApprovalRequirement, wired in Merchandising.Api.Security).
- `PurchaseOrders.Cancel` - Abandon a purchase order before any goods have been received.
- `PurchaseOrders.Close` - Finish a purchase order, accepting whatever has been received.
- `PurchaseOrders.Track` - View purchase order status and history.
- `Receiving.Prepare` - Prepare a purchase order for receiving.
- `Receiving.Confirm` - Confirm receipt of goods against a purchase order.
- `PurchaseReturns.Manage` - Record a purchase return to a supplier.
- `Stock.Read` - View current stock balances.
- `Stock.ReviewMovements` - View the stock movement ledger.
- `StockCounts.Perform` - Record a stock count session.
- `Adjustments.Request` - Request a stock adjustment (includes the P1-11 stock-decrement proof endpoint).
- `Adjustments.Approve` - Approve a requested stock adjustment. Also requires the actor not be the adjustment's own requester (SelfApprovalRequirement, wired in Merchandising.Api.Security).
- `LowStock.Review` - View low-stock review data.
- `Sales.Create` - Complete a POS sale.
- `SalesReturns.Create` - Record a permitted sales return at POS.
- `SalesReturns.ApproveExceptional` - Approve a sales return outside the cashier's permitted scope.
- `CashierSessions.Manage` - Open or close a cashier session.
- `Reports.View` - View operational reports.
- `Configuration.Manage` - Read or write SystemSettings.
- `Backup.Perform` - Trigger an on-demand backup.
- `Restore.Perform` - Perform a database restore. Deliberately its own policy, not implied by any other - see this file's header.
- `Audit.Review` - Review audit log records.
- `Maintenance.Perform` - Enter or release maintenance mode.
- `Diagnostics.AdminPing` - The P1-08 proof endpoint confirming policy-based authorization is enforced. Not a spec section 9 cell - kept only so no endpoint in the codebase authorizes by role string.

## Admin

- `Products.Read` - View product master data.
- `Products.Manage` - Create, update or deactivate a product (excluding price/cost).
- `Products.ChangePrice` - Change a product's selling price or recorded cost.
- `Suppliers.Read` - View supplier records.
- `Suppliers.Manage` - Create or update a supplier record.
- `PurchaseOrders.Create` - Create a purchase order.
- `PurchaseOrders.Submit` - Submit a draft purchase order for approval.
- `PurchaseOrders.Approve` - Approve a submitted purchase order. Also requires the actor not be the order's own creator (SelfApprovalRequirement, wired in Merchandising.Api.Security).
- `PurchaseOrders.Cancel` - Abandon a purchase order before any goods have been received.
- `PurchaseOrders.Close` - Finish a purchase order, accepting whatever has been received.
- `PurchaseOrders.Track` - View purchase order status and history.
- `Receiving.Prepare` - Prepare a purchase order for receiving.
- `Receiving.Confirm` - Confirm receipt of goods against a purchase order.
- `PurchaseReturns.Manage` - Record a purchase return to a supplier.
- `Stock.Read` - View current stock balances.
- `Stock.ReviewMovements` - View the stock movement ledger.
- `StockCounts.Perform` - Record a stock count session.
- `Adjustments.Request` - Request a stock adjustment (includes the P1-11 stock-decrement proof endpoint).
- `Adjustments.Approve` - Approve a requested stock adjustment. Also requires the actor not be the adjustment's own requester (SelfApprovalRequirement, wired in Merchandising.Api.Security).
- `LowStock.Review` - View low-stock review data.
- `Sales.Create` - Complete a POS sale.
- `SalesReturns.Create` - Record a permitted sales return at POS.
- `SalesReturns.ApproveExceptional` - Approve a sales return outside the cashier's permitted scope.
- `CashierSessions.Manage` - Open or close a cashier session.
- `Reports.View` - View operational reports.
- `Diagnostics.AdminPing` - The P1-08 proof endpoint confirming policy-based authorization is enforced. Not a spec section 9 cell - kept only so no endpoint in the codebase authorizes by role string.

## ProcurementOfficer

- `Products.Read` - View product master data.
- `Suppliers.Read` - View supplier records.
- `Suppliers.Manage` - Create or update a supplier record.
- `PurchaseOrders.Create` - Create a purchase order.
- `PurchaseOrders.Submit` - Submit a draft purchase order for approval.
- `PurchaseOrders.Cancel` - Abandon a purchase order before any goods have been received.
- `PurchaseOrders.Close` - Finish a purchase order, accepting whatever has been received.
- `PurchaseOrders.Track` - View purchase order status and history.
- `Receiving.Prepare` - Prepare a purchase order for receiving.
- `PurchaseReturns.Manage` - Record a purchase return to a supplier.
- `Stock.Read` - View current stock balances.

## InventoryClerk

- `Products.Read` - View product master data.
- `Products.Manage` - Create, update or deactivate a product (excluding price/cost).
- `Receiving.Confirm` - Confirm receipt of goods against a purchase order.
- `Stock.Read` - View current stock balances.
- `Stock.ReviewMovements` - View the stock movement ledger.
- `StockCounts.Perform` - Record a stock count session.
- `Adjustments.Request` - Request a stock adjustment (includes the P1-11 stock-decrement proof endpoint).
- `LowStock.Review` - View low-stock review data.

## Cashier

- `Products.Read` - View product master data.
- `Stock.Read` - View current stock balances.
- `Sales.Create` - Complete a POS sale.
- `SalesReturns.Create` - Record a permitted sales return at POS.
- `CashierSessions.Manage` - Open or close a cashier session.

## Every policy

| Policy | Description | Allowed roles | Spec basis |
|---|---|---|---|
| `Products.Read` | View product master data. | SuperAdmin, Admin, ProcurementOfficer, InventoryClerk, Cashier | spec section 13 Products: read by operational roles |
| `Products.Manage` | Create, update or deactivate a product (excluding price/cost). | SuperAdmin, Admin, InventoryClerk | spec section 9 Admin 'Product/supplier maintenance'; Inventory Clerk 'Product maintenance within permission' |
| `Products.ChangePrice` | Change a product's selling price or recorded cost. | SuperAdmin, Admin | spec section 9 Admin 'price changes'; Cashier restriction 'cannot change price/cost' |
| `Suppliers.Read` | View supplier records. | SuperAdmin, Admin, ProcurementOfficer | spec section 13 Suppliers: Procurement/Admin |
| `Suppliers.Manage` | Create or update a supplier record. | SuperAdmin, Admin, ProcurementOfficer | spec section 9 Procurement Officer 'Supplier records'; Admin 'supplier maintenance' |
| `PurchaseOrders.Create` | Create a purchase order. | SuperAdmin, Admin, ProcurementOfficer | spec section 9 Procurement Officer 'purchase orders' |
| `PurchaseOrders.Submit` | Submit a draft purchase order for approval. | SuperAdmin, Admin, ProcurementOfficer | spec section 10.1 purchase-order status transitions |
| `PurchaseOrders.Approve` | Approve a submitted purchase order. Also requires the actor not be the order's own creator (SelfApprovalRequirement, wired in Merchandising.Api.Security). | SuperAdmin, Admin | spec section 9 Admin 'purchase approvals'; Procurement Officer restriction 'cannot approve their own purchase order' |
| `PurchaseOrders.Cancel` | Abandon a purchase order before any goods have been received. | SuperAdmin, Admin, ProcurementOfficer | spec section 9 Procurement Officer 'purchase orders, order tracking' - section 13's endpoint table does not name this operation, so this is a P3-05 decomposition, not a section-13 quote |
| `PurchaseOrders.Close` | Finish a purchase order, accepting whatever has been received. | SuperAdmin, Admin, ProcurementOfficer | spec section 9 Procurement Officer 'order tracking' - section 13's endpoint table does not name this operation, so this is a P3-05 decomposition, not a section-13 quote |
| `PurchaseOrders.Track` | View purchase order status and history. | SuperAdmin, Admin, ProcurementOfficer | spec section 9 Procurement Officer 'order tracking' |
| `Receiving.Prepare` | Prepare a purchase order for receiving. | SuperAdmin, Admin, ProcurementOfficer | spec section 9 Procurement Officer 'receiving preparation' |
| `Receiving.Confirm` | Confirm receipt of goods against a purchase order. | SuperAdmin, Admin, InventoryClerk | spec section 9 Inventory Clerk 'receiving confirmation' |
| `PurchaseReturns.Manage` | Record a purchase return to a supplier. | SuperAdmin, Admin, ProcurementOfficer | spec section 10.1 purchase returns |
| `Stock.Read` | View current stock balances. | SuperAdmin, Admin, ProcurementOfficer, InventoryClerk, Cashier | spec section 10.3 Cashier 'review available stock'; general operational visibility |
| `Stock.ReviewMovements` | View the stock movement ledger. | SuperAdmin, Admin, InventoryClerk | spec section 9 Inventory Clerk 'movement review' |
| `StockCounts.Perform` | Record a stock count session. | SuperAdmin, Admin, InventoryClerk | spec section 9 Inventory Clerk 'stock counts' |
| `Adjustments.Request` | Request a stock adjustment (includes the P1-11 stock-decrement proof endpoint). | SuperAdmin, Admin, InventoryClerk | spec section 9 Inventory Clerk 'adjustment requests' |
| `Adjustments.Approve` | Approve a requested stock adjustment. Also requires the actor not be the adjustment's own requester (SelfApprovalRequirement, wired in Merchandising.Api.Security). | SuperAdmin, Admin | spec section 9 Admin 'adjustment approvals'; Inventory Clerk restriction 'cannot approve their own adjustment' |
| `LowStock.Review` | View low-stock review data. | SuperAdmin, Admin, InventoryClerk | spec section 9 Inventory Clerk 'low-stock review' |
| `Sales.Create` | Complete a POS sale. | SuperAdmin, Admin, Cashier | spec section 9 Cashier 'sales' |
| `SalesReturns.Create` | Record a permitted sales return at POS. | SuperAdmin, Admin, Cashier | spec section 9 Cashier 'permitted returns' |
| `SalesReturns.ApproveExceptional` | Approve a sales return outside the cashier's permitted scope. | SuperAdmin, Admin | spec section 9 Admin 'exceptional returns' |
| `CashierSessions.Manage` | Open or close a cashier session. | SuperAdmin, Admin, Cashier | spec section 9 Cashier 'POS login'; 'cashier closing' |
| `Reports.View` | View operational reports. | SuperAdmin, Admin | spec section 9 Admin 'reports' |
| `Configuration.Manage` | Read or write SystemSettings. | SuperAdmin | spec section 9 Super Admin 'configuration' |
| `Backup.Perform` | Trigger an on-demand backup. | SuperAdmin | spec section 9 Super Admin 'backup/restore' (backup half) |
| `Restore.Perform` | Perform a database restore. Deliberately its own policy, not implied by any other - see this file's header. | SuperAdmin | spec section 9 Super Admin 'backup/restore' (restore half); Admin restriction 'cannot restore unless explicitly granted a separate policy' |
| `Audit.Review` | Review audit log records. | SuperAdmin | spec section 9 Super Admin 'audit review' |
| `Maintenance.Perform` | Enter or release maintenance mode. | SuperAdmin | spec section 9 Super Admin 'emergency administration'; spec section 15 maintenance lifecycle |
| `Diagnostics.AdminPing` | The P1-08 proof endpoint confirming policy-based authorization is enforced. Not a spec section 9 cell - kept only so no endpoint in the codebase authorizes by role string. | SuperAdmin, Admin | P1-08 proof endpoint, not spec section 9 |
