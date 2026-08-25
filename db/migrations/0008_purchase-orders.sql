-- =============================================================================
-- 0008_purchase-orders.sql
--
-- P3-02: the purchase-order header and its lines - spec section 10.1's
-- "purchase orders, purchase-order lines, expected quantities, purchase
-- costs, order statuses", and the first two of the Procurement entities spec
-- section 12 lists after Suppliers (0007).
--
-- Applied by merch_migrator, after 0001-0007. After this file, run
-- db/grants/0010_purchase-order-grants.sql as root - same table-must-exist-
-- first ordering constraint as every prior migration -> grants pair
-- (ADR-013, ERROR 1146 if reversed).
--
-- All money DECIMAL(19,4), all quantity DECIMAL(19,3) (ADR-004). Timestamps
-- DATETIME(6) UTC. COLLATE stated explicitly on both tables (ADR-003) - the
-- server default here is utf8mb4_general_ci, and inheriting it earns an
-- "Illegal mix of collations" on a join months later.
--
-- -----------------------------------------------------------------------------
-- WHY "Lines" AND NOT "Items". Spec section 12's entity list writes
-- `PurchaseOrderItems`; spec section 10.1's own prose, plan.md section 7 and
-- the P3-02/P3-03/P3-06 task cards all write "purchase-order lines". Spec
-- section 12 opens by delegating exactly this: "Exact columns, names, and
-- indexes are finalized in the database design deliverable." Confirmed with
-- the user at P3-02; docs/database-design.md's forward list is corrected in
-- the same commit so the two documents stop disagreeing.
-- -----------------------------------------------------------------------------
-- =============================================================================

-- ---------------------------------------------------------- PurchaseOrders --
-- Status is the ENUM NAME, never the ordinal (ADR-020 section 5). An ordinal
-- column would silently rewrite every stored row's meaning the day a state is
-- inserted into the middle of Merchandising.Domain.Procurement.
-- PurchaseOrderStatus; a name cannot. The CHECK below is what makes that a
-- server-enforced fact rather than a convention - PurchaseOrderSchemaTests
-- walks [Enum].GetNames on the Domain enum and asserts every one of the seven
-- is accepted here, so adding a state without touching this list fails the
-- suite rather than failing in production.
--
-- ENUM(...) was rejected in favour of VARCHAR + CHECK: adding a value to a
-- MariaDB ENUM is an ALTER TABLE that rebuilds the table, and the ordinal
-- semantics ENUM carries underneath are the exact thing ADR-020 rules out.
--
-- WHY Status CARRIES COLLATE utf8mb4_bin AND NOTHING ELSE DOES. This was
-- found by the test, not by review, and the first draft of this file was
-- wrong. utf8mb4_unicode_ci is CASE-INSENSITIVE, so under the table's own
-- collation `'draft' IN ('Draft', ...)` evaluates to TRUE - the CHECK passes
-- and MariaDB then stores the string VERBATIM as 'draft'. The column would
-- hold a value that no PurchaseOrderStatus name matches, which is exactly
-- the drift this column's whole design exists to prevent, arriving through
-- the collation rather than through an ordinal. utf8mb4_bin makes the CHECK
-- compare byte for byte, so only the seven exact names survive.
--
-- Scoped to this one column deliberately. A binary collation is wrong for
-- human text - it would make supplier and product name comparison
-- case-sensitive - but it is exactly right for a machine identifier, and it
-- matches VB's own Option Compare Binary (CLAUDE.md section 3). Comparing
-- this column against a string LITERAL is unaffected: a literal has lower
-- coercibility, so the comparison resolves to _bin rather than erroring.
--
-- WHY RequestedByUserId AND ApprovedByUserId ARE TWO COLUMNS. ADR-017
-- section 6's self-approval veto, applied at P3-04, compares the requester
-- against the approver. One reused "ActorUserId" column would overwrite the
-- requester at the moment of approval and destroy the very evidence the rule
-- is decided on. ApprovedByUserId is nullable because a Draft order has no
-- approver yet; RequestedByUserId is not, because an order without a
-- requester could never have been created.
--
-- No ON DELETE clause on any FK, so MariaDB's default RESTRICT applies - the
-- same unadorned-FK pattern 0001 and 0006 use. Spec section 12: "Foreign-key
-- behavior must prevent accidental deletion of records referenced by sales,
-- receipts, returns, movements, or audit events." Proven with ERROR 1451 in
-- PurchaseOrderSchemaTests, not asserted by reading this comment.
CREATE TABLE PurchaseOrders (
    Id                INT NOT NULL AUTO_INCREMENT,
    OrderNumber       VARCHAR(50) NOT NULL,
    SupplierId        INT NOT NULL,
    Status            VARCHAR(20) COLLATE utf8mb4_bin NOT NULL,
    RequestedByUserId INT NOT NULL,
    ApprovedByUserId  INT NULL,
    SubmittedAtUtc    DATETIME(6) NULL,
    ApprovedAtUtc     DATETIME(6) NULL,
    RowVersion        BIGINT NOT NULL DEFAULT 0,
    CreatedAtUtc      DATETIME(6) NOT NULL,
    UpdatedAtUtc      DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    UNIQUE KEY UQ_PurchaseOrders_OrderNumber (OrderNumber),
    CONSTRAINT FK_PurchaseOrders_Suppliers   FOREIGN KEY (SupplierId)        REFERENCES Suppliers (Id),
    CONSTRAINT FK_PurchaseOrders_Requester   FOREIGN KEY (RequestedByUserId) REFERENCES Users (Id),
    CONSTRAINT FK_PurchaseOrders_Approver    FOREIGN KEY (ApprovedByUserId)  REFERENCES Users (Id),
    CONSTRAINT CK_PurchaseOrders_Status CHECK (Status IN (
        'Draft', 'Submitted', 'Approved', 'PartiallyReceived',
        'FullyReceived', 'Cancelled', 'Closed'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Spec section 12's index paragraph names status and dates explicitly. The
-- supplier and requester indexes come free with their foreign keys; these two
-- do not, and P3-06's order tracking plus spec section 14's purchase-order
-- history report both filter on exactly this pair.
ALTER TABLE PurchaseOrders
    ADD INDEX IX_PurchaseOrders_Status (Status),
    ADD INDEX IX_PurchaseOrders_CreatedAtUtc (CreatedAtUtc);

-- ------------------------------------------------------ PurchaseOrderLines --
-- LineNumber is the caller-visible identity of a line within its order. Spec
-- section 10.1's receiving command "validates the order status, LINE IDENTITY,
-- received quantity ..." - Phase 4 needs a stable handle that is meaningful
-- inside the order rather than a global surrogate, and the unique key below
-- is what stops two lines claiming the same one.
--
-- The same product may legitimately appear on two lines (two delivery
-- expectations, two costs), so there is deliberately NO unique key on
-- (PurchaseOrderId, ProductId).
--
-- PurchaseCost is captured on the line rather than read from Products at
-- receiving time: spec section 10.2 requires historical transactions to keep
-- "captured price/cost values even if the product is later deactivated", and
-- a cost that drifts after the order was placed would silently restate what
-- was agreed with the supplier.
--
-- ReceivedQuantity accumulates across receipts in Phase 4 (partial receiving,
-- spec section 10.1) and starts at zero, never NULL - "nothing received yet"
-- is a quantity, not an absence.
CREATE TABLE PurchaseOrderLines (
    Id              INT NOT NULL AUTO_INCREMENT,
    PurchaseOrderId INT NOT NULL,
    LineNumber      INT NOT NULL,
    ProductId       INT NOT NULL,
    OrderedQuantity DECIMAL(19,3) NOT NULL,
    PurchaseCost    DECIMAL(19,4) NOT NULL,
    ReceivedQuantity DECIMAL(19,3) NOT NULL DEFAULT 0.000,
    RowVersion      BIGINT NOT NULL DEFAULT 0,
    CreatedAtUtc    DATETIME(6) NOT NULL,
    UpdatedAtUtc    DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    UNIQUE KEY UQ_PurchaseOrderLines_OrderLine (PurchaseOrderId, LineNumber),
    CONSTRAINT FK_PurchaseOrderLines_PurchaseOrders FOREIGN KEY (PurchaseOrderId) REFERENCES PurchaseOrders (Id),
    CONSTRAINT FK_PurchaseOrderLines_Products       FOREIGN KEY (ProductId)       REFERENCES Products (Id),
    -- Spec section 12: "receipt quantity bounded by ordered quantity by
    -- default", and spec section 10.1: "the MVP default is to reject
    -- over-receiving." Enforced here as well as at the Phase 4 API, on the
    -- ADR-013 principle that a guarantee the server does not hold is a
    -- guarantee only until someone writes the next endpoint.
    --
    -- ITS HONEST COST, RECORDED RATHER THAN DISCOVERED LATER: spec section
    -- 10.1 anticipates an "authorized override policy" that would permit
    -- over-receiving. That policy is out of MVP scope, and if it is ever
    -- added it needs a new numbered migration to relax this constraint - it
    -- cannot be switched on in application code alone. That is the intended
    -- trade, confirmed with the user at P3-02: the MVP default is enforced
    -- where it cannot be forgotten, and the exception is made expensive.
    CONSTRAINT CK_PurchaseOrderLines_OrderedQuantity  CHECK (OrderedQuantity > 0),
    CONSTRAINT CK_PurchaseOrderLines_PurchaseCost     CHECK (PurchaseCost >= 0),
    CONSTRAINT CK_PurchaseOrderLines_ReceivedQuantity CHECK (ReceivedQuantity >= 0 AND ReceivedQuantity <= OrderedQuantity)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Spec section 12's index paragraph names "transaction references". Phase 4's
-- receiving and spec section 14's outstanding-quantity calculation both walk
-- lines by product; the order and product indexes come free with the foreign
-- keys above, so nothing further is added here.
