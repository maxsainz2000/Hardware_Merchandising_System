-- =============================================================================
-- 0007_suppliers.sql
--
-- P2-10: the Suppliers table spec section 12 lists alongside PurchaseOrders/
-- GoodsReceipts/PurchaseReturns (0007's own name is reserved for it there),
-- and the unique index on supplier name that spec section 12's index
-- paragraph names but P2-06 deliberately deferred here (0006's own header:
-- "Suppliers is Track D's own migration ... not one of this card's five
-- tables").
--
-- Applied by merch_migrator, after 0001-0006. After this file, run
-- db/grants/0009_supplier-grants.sql as root - same table-must-exist-first
-- ordering constraint as every prior migration -> grants pair (ADR-013,
-- ERROR 1146 if reversed).
--
-- Lifecycle/history shape mirrors Products exactly (P2-09): IsActive +
-- RowVersion, no DELETE grant ever, deactivation is the only removal story.
-- No FK yet references Suppliers - PurchaseOrders is Phase 3
-- (spec section 12's own Procurement row) - so this table starts unreferenced
-- and its deactivate/reactivate lifecycle exists ahead of any consumer.
--
-- Timestamps DATETIME(6) UTC. COLLATE stated explicitly (ADR-003).
-- =============================================================================

-- --------------------------------------------------------------- Suppliers --
-- Contact fields are free-text and optional: spec section 10.1 names
-- "contact information" without enumerating fields, and a supplier record
-- created before every detail is known must still be usable (spec section
-- 9's Procurement Officer role maintains these ahead of any purchase order
-- existing).
CREATE TABLE Suppliers (
    Id           INT NOT NULL AUTO_INCREMENT,
    Name         VARCHAR(255) NOT NULL,
    ContactName  VARCHAR(255) NULL,
    Phone        VARCHAR(50) NULL,
    Email        VARCHAR(255) NULL,
    Address      VARCHAR(500) NULL,
    IsActive     TINYINT(1) NOT NULL DEFAULT 1,
    RowVersion   BIGINT NOT NULL DEFAULT 0,
    CreatedAtUtc DATETIME(6) NOT NULL,
    UpdatedAtUtc DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    UNIQUE KEY UQ_Suppliers_Name (Name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
