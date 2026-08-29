-- =============================================================================
-- 0009_receiving.sql
--
-- P4-02: the receiving header and its lines, and the same shape for purchase
-- returns - spec section 12's Procurement entity group ("receipt quantity
-- bounded by ordered quantity by default; return quantity bounded by
-- received quantity less prior returns") and spec section 11's atomic-result
-- rows for "Goods received". Card names the tables Receipts / ReceiptLines /
-- PurchaseReturns / PurchaseReturnLines - not GoodsReceipts / GoodsReceiptItems
-- / PurchaseReturnItems as spec section 12's forward list writes them - the
-- same "Lines, not Items" naming spec section 12 delegates and 0008 already
-- resolved for PurchaseOrderLines.
--
-- Applied by merch_migrator, after 0001-0008. After this file, run
-- db/grants/0011_receiving-grants.sql as root - same table-must-exist-first
-- ordering constraint as every prior migration -> grants pair (ADR-013,
-- ERROR 1146 if reversed).
--
-- All money DECIMAL(19,4), all quantity DECIMAL(19,3) (ADR-004). Timestamps
-- DATETIME(6) UTC. COLLATE stated explicitly on every table (ADR-003) - the
-- server default here is utf8mb4_general_ci, and inheriting it earns an
-- "Illegal mix of collations" on a join months later.
--
-- -----------------------------------------------------------------------------
-- MUTABLE VS. IMMUTABLE, AND WHY THE COLUMN SETS DIFFER ACROSS THESE FOUR
-- TABLES. Receipts and ReceiptLines record something that HAPPENED - spec
-- section 11's row for "Goods received" commits once, with no endpoint that
-- ever edits a receipt afterward. They carry CreatedAtUtc only, no
-- RowVersion, no UpdatedAtUtc - the same shape 0001 gives StockMovements and
-- AuditLogs, for the same reason. PurchaseReturns is different: spec section
-- 10.1's "approval state" on a return request is a fact that changes in
-- place (Requested -> Approved/Rejected), so its header carries RowVersion
-- and UpdatedAtUtc, the PurchaseOrders shape. PurchaseReturnLines, like
-- ReceiptLines, records a fixed line item and does not itself carry an
-- approval state - only its parent header does.
--
-- ONE RETURN, ONE RECEIPT. PurchaseReturns.ReceiptId ties a return to the
-- single receipt its lines were received on, mirroring Receipts.PurchaseOrderId
-- tying a receipt to a single order. Spec section 10.1 never describes a
-- return spanning goods received on two different receipts, so this
-- migration does not build for that case.
--
-- ProductId ON BOTH LINE TABLES, EVEN THOUGH IT IS REACHABLE THROUGH A JOIN.
-- ReceiptLines could reach Products through PurchaseOrderLineId, and
-- PurchaseReturnLines through ReceiptLineId, but 0008's PurchaseOrderLines
-- already sets the precedent of a direct ProductId column on a transactional
-- line for the same two reasons: spec section 14's "Goods-receiving history"
-- and "Returns and cancellations" reports both name "product" as a required
-- column, and a captured product reference on the line survives however the
-- chain above it is later queried.
-- -----------------------------------------------------------------------------
-- =============================================================================

-- -------------------------------------------------------------- Receipts ----
-- ReferenceNumber is the receiving clerk's own document number (the goods-
-- received note), not the ADR-007 idempotency key - that key lives in the
-- separate IdempotencyKeys table (0001) keyed by (Scope, KeyValue) and is
-- never stored on the business row it protects. Unique for the same reason
-- PurchaseOrders.OrderNumber is unique: two receipts cannot legitimately
-- claim the same document number.
--
-- No ON DELETE clause on any FK, so MariaDB's default RESTRICT applies - the
-- same unadorned-FK pattern 0001, 0006 and 0008 use. Spec section 12:
-- "Foreign-key behavior must prevent accidental deletion of records
-- referenced by sales, receipts, returns, movements, or audit events."
CREATE TABLE Receipts (
    Id                INT NOT NULL AUTO_INCREMENT,
    PurchaseOrderId   INT NOT NULL,
    ReceivedByUserId  INT NOT NULL,
    ReceivedAtUtc     DATETIME(6) NOT NULL,
    ReferenceNumber   VARCHAR(50) NOT NULL,
    CreatedAtUtc      DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    UNIQUE KEY UQ_Receipts_ReferenceNumber (ReferenceNumber),
    CONSTRAINT FK_Receipts_PurchaseOrders FOREIGN KEY (PurchaseOrderId)  REFERENCES PurchaseOrders (Id),
    CONSTRAINT FK_Receipts_ReceivedBy     FOREIGN KEY (ReceivedByUserId) REFERENCES Users (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Spec section 12's index paragraph names dates explicitly; the order and
-- user indexes come free with their foreign keys.
ALTER TABLE Receipts
    ADD INDEX IX_Receipts_ReceivedAtUtc (ReceivedAtUtc);

-- ----------------------------------------------------------- ReceiptLines ---
-- Cost is captured on the line, the same as PurchaseOrderLines.PurchaseCost
-- (0008): the actual invoiced/received cost can differ from the ordered
-- PurchaseCost, and spec section 10.2 requires historical transactions to
-- keep "captured price/cost values even if the product is later
-- deactivated".
--
-- No unique key on (ReceiptId, PurchaseOrderLineId) - deliberately, the same
-- reasoning 0008 gives for no unique key on (PurchaseOrderId, ProductId):
-- nothing in the spec forbids two lines on one receipt against the same
-- order line, and inventing that restriction is not this card's job.
CREATE TABLE ReceiptLines (
    Id                  INT NOT NULL AUTO_INCREMENT,
    ReceiptId           INT NOT NULL,
    PurchaseOrderLineId INT NOT NULL,
    ProductId           INT NOT NULL,
    QuantityReceived    DECIMAL(19,3) NOT NULL,
    Cost                DECIMAL(19,4) NOT NULL,
    CreatedAtUtc        DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    CONSTRAINT FK_ReceiptLines_Receipts           FOREIGN KEY (ReceiptId)           REFERENCES Receipts (Id),
    CONSTRAINT FK_ReceiptLines_PurchaseOrderLines FOREIGN KEY (PurchaseOrderLineId) REFERENCES PurchaseOrderLines (Id),
    CONSTRAINT FK_ReceiptLines_Products           FOREIGN KEY (ProductId)           REFERENCES Products (Id),
    CONSTRAINT CK_ReceiptLines_QuantityReceived CHECK (QuantityReceived > 0),
    CONSTRAINT CK_ReceiptLines_Cost              CHECK (Cost >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------- PurchaseReturns -
-- Status carries the same utf8mb4_bin scoping 0008 gives PurchaseOrders.Status,
-- for the identical reason: under the table's own utf8mb4_unicode_ci
-- collation, 'requested' IN ('Requested', ...) is TRUE, and the CHECK would
-- pass while storing a value no PurchaseReturnStatus name matches.
--
-- ApprovedByUserId is a second, separate, nullable column rather than one
-- reused actor column - the same ADR-017 section 6 shape 0008 gives
-- RequestedByUserId/ApprovedByUserId, kept available here even before a
-- self-approval rule is decided for returns, so that decision costs no new
-- migration when it is made.
CREATE TABLE PurchaseReturns (
    Id                 INT NOT NULL AUTO_INCREMENT,
    ReceiptId          INT NOT NULL,
    RequestedByUserId  INT NOT NULL,
    ApprovedByUserId   INT NULL,
    Status             VARCHAR(20) COLLATE utf8mb4_bin NOT NULL,
    ReturnedAtUtc      DATETIME(6) NOT NULL,
    ApprovedAtUtc      DATETIME(6) NULL,
    ReferenceNumber    VARCHAR(50) NOT NULL,
    RowVersion         BIGINT NOT NULL DEFAULT 0,
    CreatedAtUtc       DATETIME(6) NOT NULL,
    UpdatedAtUtc       DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    UNIQUE KEY UQ_PurchaseReturns_ReferenceNumber (ReferenceNumber),
    CONSTRAINT FK_PurchaseReturns_Receipts  FOREIGN KEY (ReceiptId)         REFERENCES Receipts (Id),
    CONSTRAINT FK_PurchaseReturns_Requester FOREIGN KEY (RequestedByUserId) REFERENCES Users (Id),
    CONSTRAINT FK_PurchaseReturns_Approver  FOREIGN KEY (ApprovedByUserId)  REFERENCES Users (Id),
    CONSTRAINT CK_PurchaseReturns_Status CHECK (Status IN ('Requested', 'Approved', 'Rejected'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Spec section 12's index paragraph names status and dates explicitly, the
-- same pair 0008 indexes on PurchaseOrders.
ALTER TABLE PurchaseReturns
    ADD INDEX IX_PurchaseReturns_Status (Status),
    ADD INDEX IX_PurchaseReturns_ReturnedAtUtc (ReturnedAtUtc);

-- ----------------------------------------------------- PurchaseReturnLines -
-- RemovesStock is spec section 10.1's "whether stock is removed" - a return
-- of goods still fit for resale removes them from the stock pool the same
-- way a purchase return normally would, but a defective item already
-- written off by a prior adjustment records the return operationally
-- without a second stock effect. P4-08 reads this flag rather than assuming
-- every return decrements stock.
--
-- No database CHECK bounds QuantityReturned against "received quantity less
-- prior returns" - that bound is an aggregate over every prior sibling row
-- for the same ReceiptLineId, which a MariaDB CHECK (evaluated one row at a
-- time) cannot express. Spec section 10.1's bound is therefore enforced at
-- the API in P4-08, computed from committed rows, exactly as that card's own
-- text states - it is not re-decided here.
CREATE TABLE PurchaseReturnLines (
    Id                INT NOT NULL AUTO_INCREMENT,
    PurchaseReturnId  INT NOT NULL,
    ReceiptLineId     INT NOT NULL,
    ProductId         INT NOT NULL,
    QuantityReturned  DECIMAL(19,3) NOT NULL,
    Cost              DECIMAL(19,4) NOT NULL,
    Reason            VARCHAR(255) NOT NULL,
    RemovesStock      TINYINT(1) NOT NULL DEFAULT 1,
    CreatedAtUtc      DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    CONSTRAINT FK_PurchaseReturnLines_PurchaseReturns FOREIGN KEY (PurchaseReturnId) REFERENCES PurchaseReturns (Id),
    CONSTRAINT FK_PurchaseReturnLines_ReceiptLines     FOREIGN KEY (ReceiptLineId)    REFERENCES ReceiptLines (Id),
    CONSTRAINT FK_PurchaseReturnLines_Products         FOREIGN KEY (ProductId)        REFERENCES Products (Id),
    CONSTRAINT CK_PurchaseReturnLines_QuantityReturned CHECK (QuantityReturned > 0),
    CONSTRAINT CK_PurchaseReturnLines_Cost              CHECK (Cost >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
