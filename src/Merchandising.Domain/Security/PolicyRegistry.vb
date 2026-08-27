' Merchandising.Domain.Security.PolicyRegistry
'
' P2-02 / ADR-017: spec section 9's role table, decomposed into one named
' policy per operation (a "cell" in the done-when box's sense is
' Operation x Role, and the operation IS the policy - its allowed-roles list
' IS the row of cells that resolve to Allow). Operation granularity follows
' section 13's API-area breakdown (Products, Suppliers, Procurement,
' Inventory, POS, Reports) rather than an invented decomposition of section
' 9's prose, because section 13 is where the spec itself already names the
' individual surfaces.
'
' NOT HERE: user/role management. Section 13's API area table has no "Users"
' row at all - accounts are created exclusively through the
' Merchandising.Maintenance CLI's "create-user" (P1-08), never over HTTP. So
' SuperAdmin's "user/role management" responsibility and Admin's "cannot
' manage Super Admin accounts" restriction have no endpoint to gate in this
' MVP, and get no policy. A future card that adds a Users HTTP surface adds
' its own policy and reargues this note.
'
' Two policies below (PurchaseOrders.Approve, Adjustments.Approve) get an
' additional SelfApprovalRequirement in Merchandising.Api.Security.
' AuthorizationPolicyRegistration - a resource-based check that cannot be
' expressed as a role list, so it is wired there rather than as data here.
' Neither has a live endpoint yet (Phase 3 builds purchase orders and stock
' adjustments); this registry and P2-02's tests exercise the requirement
' directly. Restore.Perform is deliberately its own entry, not folded into
' Configuration.Manage, even though both currently list only SuperAdmin -
' spec section 9: "[Admin] cannot restore unless explicitly granted a
' separate policy" means a later grant to Restore.Perform must not also
' widen Configuration.Manage, and structural separation is what guarantees
' that rather than a comment.
Namespace Security

    Public NotInheritable Class PolicyRegistry

        ''' <summary>Every registered policy name, so callers - and AuthorizationPolicyRegistration - never spell one as a string literal.</summary>
        Public NotInheritable Class Names

            Public Const ProductsRead As String = "Products.Read"
            Public Const ProductsManage As String = "Products.Manage"
            Public Const ProductsChangePrice As String = "Products.ChangePrice"
            Public Const SuppliersRead As String = "Suppliers.Read"
            Public Const SuppliersManage As String = "Suppliers.Manage"
            Public Const PurchaseOrdersCreate As String = "PurchaseOrders.Create"
            Public Const PurchaseOrdersSubmit As String = "PurchaseOrders.Submit"
            Public Const PurchaseOrdersApprove As String = "PurchaseOrders.Approve"
            Public Const PurchaseOrdersCancel As String = "PurchaseOrders.Cancel"
            Public Const PurchaseOrdersClose As String = "PurchaseOrders.Close"
            Public Const PurchaseOrdersTrack As String = "PurchaseOrders.Track"
            Public Const ReceivingPrepare As String = "Receiving.Prepare"
            Public Const ReceivingConfirm As String = "Receiving.Confirm"
            Public Const PurchaseReturnsManage As String = "PurchaseReturns.Manage"
            Public Const StockRead As String = "Stock.Read"
            Public Const StockReviewMovements As String = "Stock.ReviewMovements"
            Public Const StockCountsPerform As String = "StockCounts.Perform"
            Public Const AdjustmentsRequest As String = "Adjustments.Request"
            Public Const AdjustmentsApprove As String = "Adjustments.Approve"
            Public Const LowStockReview As String = "LowStock.Review"
            Public Const SalesReturnsCreate As String = "SalesReturns.Create"
            Public Const SalesReturnsApproveExceptional As String = "SalesReturns.ApproveExceptional"
            Public Const SalesCreate As String = "Sales.Create"
            Public Const CashierSessionsManage As String = "CashierSessions.Manage"
            Public Const ReportsView As String = "Reports.View"
            Public Const ConfigurationManage As String = "Configuration.Manage"
            Public Const BackupPerform As String = "Backup.Perform"
            Public Const RestorePerform As String = "Restore.Perform"
            Public Const AuditReview As String = "Audit.Review"
            Public Const MaintenancePerform As String = "Maintenance.Perform"
            Public Const DiagnosticsAdminPing As String = "Diagnostics.AdminPing"

            Private Sub New()
            End Sub

        End Class

        Private Shared ReadOnly SuperAdminOnly As String() = {RoleNames.SuperAdmin}
        Private Shared ReadOnly AdminAndAbove As String() = {RoleNames.SuperAdmin, RoleNames.Admin}
        Private Shared ReadOnly ProcurementAndAbove As String() = {RoleNames.SuperAdmin, RoleNames.Admin, RoleNames.ProcurementOfficer}
        Private Shared ReadOnly InventoryAndAbove As String() = {RoleNames.SuperAdmin, RoleNames.Admin, RoleNames.InventoryClerk}
        Private Shared ReadOnly CashierAndAbove As String() = {RoleNames.SuperAdmin, RoleNames.Admin, RoleNames.Cashier}
        Private Shared ReadOnly EveryOperationalRole As String() = {
            RoleNames.SuperAdmin, RoleNames.Admin, RoleNames.ProcurementOfficer, RoleNames.InventoryClerk, RoleNames.Cashier}

        ''' <summary>
        ''' The single source of truth: what AuthorizationPolicyRegistration
        ''' registers at startup, and what RolePermissionMatrixFormatter
        ''' renders into docs/role-permission-matrix.md. Ordered to match the
        ''' document's intended reading order, not alphabetically.
        ''' </summary>
        Public Shared ReadOnly Property Definitions As IReadOnlyList(Of PolicyDefinition)

        ''' <summary>
        ''' Built with .Add rather than a "From {...}" collection initializer
        ''' spanning many lines - this VB compiler requires an explicit line
        ''' continuation inside a multi-line collection initializer (a blank
        ''' line after the opening brace is parsed as the start of a new
        ''' statement, not a continuation of the expression), so one
        ''' PolicyDefinition per statement is the reliable shape here.
        '''
        ''' The local is named "collected", not "definitions" - VB is
        ''' case-insensitive, and a local differing from the Definitions
        ''' property only by case resolves every reference (including the
        ''' final assignment) to the local, silently leaving the property's
        ''' backing field Nothing. Measured, not theorised: that exact typo
        ''' shipped here first and NullReferenceException'd every caller of
        ''' Definitions.
        ''' </summary>
        Shared Sub New()

            Dim collected As New List(Of PolicyDefinition)()

            collected.Add(New PolicyDefinition(Names.ProductsRead, "View product master data.", EveryOperationalRole, "spec section 13 Products: read by operational roles"))
            collected.Add(New PolicyDefinition(Names.ProductsManage, "Create, update or deactivate a product (excluding price/cost).", InventoryAndAbove, "spec section 9 Admin 'Product/supplier maintenance'; Inventory Clerk 'Product maintenance within permission'"))
            collected.Add(New PolicyDefinition(Names.ProductsChangePrice, "Change a product's selling price or recorded cost.", AdminAndAbove, "spec section 9 Admin 'price changes'; Cashier restriction 'cannot change price/cost'"))

            collected.Add(New PolicyDefinition(Names.SuppliersRead, "View supplier records.", ProcurementAndAbove, "spec section 13 Suppliers: Procurement/Admin"))
            collected.Add(New PolicyDefinition(Names.SuppliersManage, "Create or update a supplier record.", ProcurementAndAbove, "spec section 9 Procurement Officer 'Supplier records'; Admin 'supplier maintenance'"))

            collected.Add(New PolicyDefinition(Names.PurchaseOrdersCreate, "Create a purchase order.", ProcurementAndAbove, "spec section 9 Procurement Officer 'purchase orders'"))
            collected.Add(New PolicyDefinition(Names.PurchaseOrdersSubmit, "Submit a draft purchase order for approval.", ProcurementAndAbove, "spec section 10.1 purchase-order status transitions"))
            collected.Add(New PolicyDefinition(Names.PurchaseOrdersApprove, "Approve a submitted purchase order. Also requires the actor not be the order's own creator (SelfApprovalRequirement, wired in Merchandising.Api.Security).", AdminAndAbove, "spec section 9 Admin 'purchase approvals'; Procurement Officer restriction 'cannot approve their own purchase order'"))
            collected.Add(New PolicyDefinition(Names.PurchaseOrdersCancel, "Abandon a purchase order before any goods have been received.", ProcurementAndAbove, "spec section 9 Procurement Officer 'purchase orders, order tracking' - section 13's endpoint table does not name this operation, so this is a P3-05 decomposition, not a section-13 quote"))
            collected.Add(New PolicyDefinition(Names.PurchaseOrdersClose, "Finish a purchase order, accepting whatever has been received.", ProcurementAndAbove, "spec section 9 Procurement Officer 'order tracking' - section 13's endpoint table does not name this operation, so this is a P3-05 decomposition, not a section-13 quote"))
            collected.Add(New PolicyDefinition(Names.PurchaseOrdersTrack, "View purchase order status and history.", ProcurementAndAbove, "spec section 9 Procurement Officer 'order tracking'"))

            collected.Add(New PolicyDefinition(Names.ReceivingPrepare, "Prepare a purchase order for receiving.", ProcurementAndAbove, "spec section 9 Procurement Officer 'receiving preparation'"))
            collected.Add(New PolicyDefinition(Names.ReceivingConfirm, "Confirm receipt of goods against a purchase order.", InventoryAndAbove, "spec section 9 Inventory Clerk 'receiving confirmation'"))
            collected.Add(New PolicyDefinition(Names.PurchaseReturnsManage, "Record a purchase return to a supplier.", ProcurementAndAbove, "spec section 10.1 purchase returns"))

            collected.Add(New PolicyDefinition(Names.StockRead, "View current stock balances.", EveryOperationalRole, "spec section 10.3 Cashier 'review available stock'; general operational visibility"))
            collected.Add(New PolicyDefinition(Names.StockReviewMovements, "View the stock movement ledger.", InventoryAndAbove, "spec section 9 Inventory Clerk 'movement review'"))
            collected.Add(New PolicyDefinition(Names.StockCountsPerform, "Record a stock count session.", InventoryAndAbove, "spec section 9 Inventory Clerk 'stock counts'"))
            collected.Add(New PolicyDefinition(Names.AdjustmentsRequest, "Request a stock adjustment (includes the P1-11 stock-decrement proof endpoint).", InventoryAndAbove, "spec section 9 Inventory Clerk 'adjustment requests'"))
            collected.Add(New PolicyDefinition(Names.AdjustmentsApprove, "Approve a requested stock adjustment. Also requires the actor not be the adjustment's own requester (SelfApprovalRequirement, wired in Merchandising.Api.Security).", AdminAndAbove, "spec section 9 Admin 'adjustment approvals'; Inventory Clerk restriction 'cannot approve their own adjustment'"))
            collected.Add(New PolicyDefinition(Names.LowStockReview, "View low-stock review data.", InventoryAndAbove, "spec section 9 Inventory Clerk 'low-stock review'"))

            collected.Add(New PolicyDefinition(Names.SalesCreate, "Complete a POS sale.", CashierAndAbove, "spec section 9 Cashier 'sales'"))
            collected.Add(New PolicyDefinition(Names.SalesReturnsCreate, "Record a permitted sales return at POS.", CashierAndAbove, "spec section 9 Cashier 'permitted returns'"))
            collected.Add(New PolicyDefinition(Names.SalesReturnsApproveExceptional, "Approve a sales return outside the cashier's permitted scope.", AdminAndAbove, "spec section 9 Admin 'exceptional returns'"))
            collected.Add(New PolicyDefinition(Names.CashierSessionsManage, "Open or close a cashier session.", CashierAndAbove, "spec section 9 Cashier 'POS login'; 'cashier closing'"))

            collected.Add(New PolicyDefinition(Names.ReportsView, "View operational reports.", AdminAndAbove, "spec section 9 Admin 'reports'"))

            collected.Add(New PolicyDefinition(Names.ConfigurationManage, "Read or write SystemSettings.", SuperAdminOnly, "spec section 9 Super Admin 'configuration'"))
            collected.Add(New PolicyDefinition(Names.BackupPerform, "Trigger an on-demand backup.", SuperAdminOnly, "spec section 9 Super Admin 'backup/restore' (backup half)"))
            collected.Add(New PolicyDefinition(Names.RestorePerform, "Perform a database restore. Deliberately its own policy, not implied by any other - see this file's header.", SuperAdminOnly, "spec section 9 Super Admin 'backup/restore' (restore half); Admin restriction 'cannot restore unless explicitly granted a separate policy'"))
            collected.Add(New PolicyDefinition(Names.AuditReview, "Review audit log records.", SuperAdminOnly, "spec section 9 Super Admin 'audit review'"))
            collected.Add(New PolicyDefinition(Names.MaintenancePerform, "Enter or release maintenance mode.", SuperAdminOnly, "spec section 9 Super Admin 'emergency administration'; spec section 15 maintenance lifecycle"))

            collected.Add(New PolicyDefinition(Names.DiagnosticsAdminPing, "The P1-08 proof endpoint confirming policy-based authorization is enforced. Not a spec section 9 cell - kept only so no endpoint in the codebase authorizes by role string.", AdminAndAbove, "P1-08 proof endpoint, not spec section 9"))

            Definitions = collected

        End Sub

    End Class

End Namespace
