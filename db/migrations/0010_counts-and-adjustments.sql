-- =============================================================================
-- 0010_counts-and-adjustments.sql
--
-- P4-03: the stock-count header (status, counted-by, approved-by,
-- timestamps UTC), its lines (product, counted quantity, system quantity at
-- count time, variance), and the adjustment table with its reason,
-- requester, approver and threshold outcome - spec section 10.2's "Stock
-- counts record the counted quantity, system quantity, variance, count
-- session, counted by, reviewed by, reason, and approval state" and
-- "Adjustment approval is required when the absolute variance exceeds the
-- configured threshold."
--
-- Applied by merch_migrator, after 0001-0009. After this file, run
-- db/grants/0012_counts-and-adjustments-grants.sql as root - same table-
-- must-exist-first ordering constraint as every prior migration -> grants
-- pair (ADR-013, ERROR 1146 if reversed).
--
-- All money DECIMAL(19,4), all quantity DECIMAL(19,3) (ADR-004). Timestamps
-- DATETIME(6) UTC. COLLATE stated explicitly on every table (ADR-003) - the
-- server default here is utf8mb4_general_ci, and inheriting it earns an
-- "Illegal mix of collations" on a join months later.
--
-- -----------------------------------------------------------------------------
-- "StockCountLines", NOT "StockCountItems". Spec section 12's entity list
-- writes `StockCountItems`; this card's own text, docs/adr.md's P3-02 entry,
-- and 0008/0009's identical resolution for PurchaseOrderLines/ReceiptLines
-- all write "Lines". Spec section 12 delegates exactly this - "Exact
-- columns, names, and indexes are finalized in the database design
-- deliverable" - the same delegation P3-02 already exercised. Confirmed with
-- the user at P4-03.
--
-- FOUR STATUSES EACH, INCLUDING TWO NOT YET DRIVEN. StockCounts.Status
-- (Open, Closed, Approved, Rejected) and StockAdjustments.Status (Pending,
-- Approved, Rejected, Applied) both model their whole lifecycle now, the
-- same PurchaseOrderStatus precedent (P3-02) of naming every state a later
-- phase drives rather than rewriting the table when P4-09/P4-10 arrive.
-- Confirmed with the user at P4-03.
--
-- Status CARRIES COLLATE utf8mb4_bin on both tables, for the identical
-- reason 0008 gives PurchaseOrders.Status and 0009 gives PurchaseReturns.Status:
-- under the table's own utf8mb4_unicode_ci collation, 'open' IN ('Open', ...)
-- evaluates to TRUE, so the CHECK would pass while storing a value no enum
-- name matches. utf8mb4_bin makes the CHECK compare byte for byte.
--
-- StockAdjustments CARRIES NO StockCountId. The card's column list is
-- "reason, requester, approver and threshold outcome" - no count linkage
-- named, and P4-10 describes an adjustment generically ("applies a variance
-- to stock"), not as count-derived only. If a later card needs that link,
-- it is a new numbered migration, not invented here.
--
-- ExceedsThreshold IS CAPTURED AT REQUEST TIME, never recomputed. The same
-- principle P4-03's own first done-when box states for StockCountLines.Variance
-- - "the whole point of a count is what was true then" - applies equally to
-- why an adjustment was or was not routed for approval: SystemSettings'
-- threshold value can change after the request, and the recorded outcome
-- must not silently reinterpret history.
-- -----------------------------------------------------------------------------
-- =============================================================================

-- -------------------------------------------------------------- StockCounts -
-- CountedByUserId is not null - a count session cannot exist without someone
-- performing it. ApprovedByUserId is nullable - an Open or Closed-but-
-- unreviewed count has no approver yet, the same nullable-until-decided shape
-- 0008 gives PurchaseOrders.ApprovedByUserId and 0009 gives
-- PurchaseReturns.ApprovedByUserId.
--
-- No ON DELETE clause on either FK, so MariaDB's default RESTRICT applies -
-- the same unadorned-FK pattern every prior migration uses. Spec section 12:
-- "Foreign-key behavior must prevent accidental deletion of records
-- referenced by sales, receipts, returns, movements, or audit events."
CREATE TABLE StockCounts (
    Id               INT NOT NULL AUTO_INCREMENT,
    Status           VARCHAR(20) COLLATE utf8mb4_bin NOT NULL,
    CountedByUserId  INT NOT NULL,
    ApprovedByUserId INT NULL,
    CountedAtUtc     DATETIME(6) NOT NULL,
    ApprovedAtUtc    DATETIME(6) NULL,
    RowVersion       BIGINT NOT NULL DEFAULT 0,
    CreatedAtUtc     DATETIME(6) NOT NULL,
    UpdatedAtUtc     DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    CONSTRAINT FK_StockCounts_CountedBy  FOREIGN KEY (CountedByUserId)  REFERENCES Users (Id),
    CONSTRAINT FK_StockCounts_ApprovedBy FOREIGN KEY (ApprovedByUserId) REFERENCES Users (Id),
    CONSTRAINT CK_StockCounts_Status CHECK (Status IN ('Open', 'Closed', 'Approved', 'Rejected'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Spec section 12's index paragraph names status and dates explicitly, the
-- same pair 0008 and 0009 index on their own status/date columns.
ALTER TABLE StockCounts
    ADD INDEX IX_StockCounts_Status (Status),
    ADD INDEX IX_StockCounts_CountedAtUtc (CountedAtUtc);

-- ----------------------------------------------------------- StockCountLines
-- SystemQuantity is the balance AT THE MOMENT OF COUNTING, captured once and
-- never recomputed - card done-when box 1: "the whole point of a count is
-- what was true then." Variance is likewise stored, not derived by a later
-- SELECT, for the identical reason: StockBalances.Quantity moves on, and a
-- generated column here would silently reinterpret a historical count.
--
-- No CHECK ties Variance to (CountedQuantity - SystemQuantity): MariaDB has
-- no generated-column-from-two-siblings shape that survives a signed
-- subtraction cleanly across all three DECIMAL(19,3) columns without risking
-- exactly the silent-rounding trap CLAUDE.md section 6.3 warns about, and the
-- API is what computes and writes all three together in one INSERT (P4-09) -
-- there is no path where they could disagree once written.
CREATE TABLE StockCountLines (
    Id               INT NOT NULL AUTO_INCREMENT,
    StockCountId     INT NOT NULL,
    ProductId        INT NOT NULL,
    CountedQuantity  DECIMAL(19,3) NOT NULL,
    SystemQuantity   DECIMAL(19,3) NOT NULL,
    Variance         DECIMAL(19,3) NOT NULL,
    CreatedAtUtc     DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    CONSTRAINT FK_StockCountLines_StockCounts FOREIGN KEY (StockCountId) REFERENCES StockCounts (Id),
    CONSTRAINT FK_StockCountLines_Products     FOREIGN KEY (ProductId)    REFERENCES Products (Id),
    CONSTRAINT CK_StockCountLines_CountedQuantity CHECK (CountedQuantity >= 0),
    CONSTRAINT CK_StockCountLines_SystemQuantity  CHECK (SystemQuantity >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------- StockAdjustments -
-- RequestedByUserId and ApprovedByUserId are two columns, not one - card
-- done-when box 2: "the threshold rule compares them and cannot if they are
-- one field." RequestedByUserId matches the property name
-- Merchandising.Domain.Security.IOwnershipResource / SelfApprovalHandler
-- already compares against (docs/adr.md's ADR-017 section 6 entry), so
-- P4-10 needs no new column when it wires the self-approval veto.
--
-- QuantityVariance is SIGNED (no CHECK bounding its sign): spec section
-- 10.2's variance can correct stock upward (found more than recorded) or
-- downward (found less), and CLAUDE.md's DECIMAL(19,3) quantity rule does
-- not itself imply non-negative here the way a receipt or sale quantity is
-- non-negative - an adjustment's whole purpose is signed correction.
--
-- ExceedsThreshold is TINYINT(1), not derived from QuantityVariance by a
-- CHECK: the threshold itself lives in SystemSettings (P4-10), a value this
-- migration has no way to read at CHECK-evaluation time, and MariaDB 10.4
-- CHECK constraints cannot reference another table regardless.
CREATE TABLE StockAdjustments (
    Id                INT NOT NULL AUTO_INCREMENT,
    ProductId         INT NOT NULL,
    QuantityVariance  DECIMAL(19,3) NOT NULL,
    Reason            VARCHAR(255) NOT NULL,
    RequestedByUserId INT NOT NULL,
    ApprovedByUserId  INT NULL,
    ExceedsThreshold  TINYINT(1) NOT NULL,
    Status            VARCHAR(20) COLLATE utf8mb4_bin NOT NULL,
    RowVersion        BIGINT NOT NULL DEFAULT 0,
    CreatedAtUtc      DATETIME(6) NOT NULL,
    UpdatedAtUtc      DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    CONSTRAINT FK_StockAdjustments_Products    FOREIGN KEY (ProductId)         REFERENCES Products (Id),
    CONSTRAINT FK_StockAdjustments_Requester   FOREIGN KEY (RequestedByUserId) REFERENCES Users (Id),
    CONSTRAINT FK_StockAdjustments_Approver    FOREIGN KEY (ApprovedByUserId)  REFERENCES Users (Id),
    CONSTRAINT CK_StockAdjustments_QuantityVariance CHECK (QuantityVariance <> 0),
    CONSTRAINT CK_StockAdjustments_Status CHECK (Status IN ('Pending', 'Approved', 'Rejected', 'Applied'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Spec section 12's index paragraph names status and dates explicitly.
ALTER TABLE StockAdjustments
    ADD INDEX IX_StockAdjustments_Status (Status),
    ADD INDEX IX_StockAdjustments_CreatedAtUtc (CreatedAtUtc);
