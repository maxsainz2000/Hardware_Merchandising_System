-- =============================================================================
-- 0012_sales-returns.sql
--
-- P5-03: the sales-return header (original sale, returned-by, approver
-- where exceptional, reason, timestamps UTC) and its lines (original sale
-- line, quantity returned, stock-eligibility flag) - spec section 10.3's
-- "A completed-sale return must identify the original sale line, cannot
-- exceed the quantity sold minus prior returns, and records whether the
-- returned item is eligible to re-enter stock. Payment reversal is recorded
-- operationally" and spec section 11's atomic-result row for "Sale
-- returned" (return, return lines, operational payment-reversal record,
-- eligible stock-in movement, balance change, audit event).
--
-- Applied by merch_migrator, after 0001-0011. After this file, run
-- db/grants/0014_sales-returns-grants.sql as root - same table-must-
-- exist-first ordering constraint as every prior migration -> grants pair
-- (ADR-013, ERROR 1146 if reversed).
--
-- All money DECIMAL(19,4), all quantity DECIMAL(19,3) (ADR-004). Timestamps
-- DATETIME(6) UTC. COLLATE stated explicitly on every table (ADR-003).
--
-- -----------------------------------------------------------------------------
-- SalesReturns IS MUTABLE, LIKE PurchaseReturns/StockAdjustments - NOT LIKE
-- Sales/SalePayments. Spec section 9: "policies for sensitive operations
-- such as approvals, returns above a threshold." ADR-017 section 6's self-
-- approval veto compares a distinct requester (ReturnedByUserId) against a
-- distinct approver (ApprovedByUserId) - that only makes sense with a real
-- two-actor workflow: within a cashier's permitted scope, SalesReturns.Create
-- completes a return directly (Status = Completed, no approver); above the
-- configured threshold it lands PendingApproval with no stock or refund
-- effect committed yet - stock movements are append-only and irreversible,
-- so nothing can commit until SalesReturns.ApproveExceptional (a second,
-- distinct actor) either approves (Status -> Completed, effects commit in
-- the same transaction) or rejects (Status -> Rejected, no effects, ever).
--
-- ONLY THREE STATUS NAMES, NOT FOUR. Merchandising.Domain.Sales.
-- SalesReturnStatus (new in this card) names PendingApproval/Completed/
-- Rejected - no separate "Approved, not yet Applied" state the way
-- StockAdjustmentStatus does. P4-10's own evidence
-- (evidence/phase-4/p4-10-adjustments.txt SS0.3) recorded that state was
-- never actually persisted in practice: approval writes the terminal status
-- directly, in the same UPDATE that sets the approver. Known going in here,
-- so the enum names only states something will actually be observed in.
-- Status carries COLLATE utf8mb4_bin, the same defect class 0008/0009/0010/
-- 0011's Status/Method columns already guard against.
--
-- ExceedsThreshold IS CAPTURED AT REQUEST TIME, never recomputed - the
-- identical StockAdjustments (0010) argument: the threshold SystemSettings
-- value can change after the request, and the recorded outcome must not
-- silently reinterpret history.
--
-- SalesReturnLines DOES NOT CAPTURE UnitPrice, UNLIKE ReceiptLines/
-- PurchaseOrderLines. Those capture cost because the actual received/
-- invoiced cost can genuinely differ from what was ordered. A sale's price
-- cannot drift after the fact - SaleLines is itself immutable (0011) - so
-- the refund value for a line is safely computed by joining SaleLineId to
-- SaleLines.UnitPrice rather than re-capturing a value that can never
-- disagree with its source.
--
-- RestocksItem, NOT RemovesStock. 0009's PurchaseReturnLines names its
-- equivalent flag RemovesStock (a purchase return that removes stock from
-- the pool); a sales return runs the opposite direction - RestocksItem
-- names spec section 10.3's "eligible to re-enter stock" from the pool's
-- point of view for THIS flow, and matches the name P5-11's own card text
-- already uses for it.
--
-- RefundMethod/RefundAmount LIVE ON THE HEADER, NOT PER LINE - spec section
-- 11 lists "operational payment-reversal record" as one singular item
-- alongside "return" and "return lines", the same one-record-per-event
-- shape as the return itself. Both are nullable, filled in only once the
-- return actually reaches Completed - whether immediately (within scope) or
-- later via ApproveExceptional - the same nullable-until-decided shape
-- CashierSessions.DeclaredCash/CalculatedCash/CashVariance (0011) uses.
-- RefundMethod carries the identical utf8mb4_bin/CHECK treatment
-- SalePayments.Method (0011) does, and may differ from how the original
-- sale was paid - refunding a card sale in cash is an ordinary case this
-- schema does not forbid. Confirmed with the user at P5-03.
-- -----------------------------------------------------------------------------
-- =============================================================================

-- -------------------------------------------------------------- SalesReturns
-- No ON DELETE clause on any FK, so MariaDB's default RESTRICT applies - the
-- same unadorned-FK pattern every prior migration uses. Spec section 12:
-- "Foreign-key behavior must prevent accidental deletion of records
-- referenced by sales, receipts, returns, movements, or audit events."
CREATE TABLE SalesReturns (
    Id                INT NOT NULL AUTO_INCREMENT,
    SaleId            INT NOT NULL,
    ReturnedByUserId  INT NOT NULL,
    ApprovedByUserId  INT NULL,
    Reason            VARCHAR(255) NOT NULL,
    ExceedsThreshold  TINYINT(1) NOT NULL,
    Status            VARCHAR(20) COLLATE utf8mb4_bin NOT NULL,
    RefundMethod      VARCHAR(20) COLLATE utf8mb4_bin NULL,
    RefundAmount      DECIMAL(19,4) NULL,
    ReturnedAtUtc     DATETIME(6) NOT NULL,
    ApprovedAtUtc     DATETIME(6) NULL,
    RowVersion        BIGINT NOT NULL DEFAULT 0,
    CreatedAtUtc      DATETIME(6) NOT NULL,
    UpdatedAtUtc      DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    CONSTRAINT FK_SalesReturns_Sales       FOREIGN KEY (SaleId)           REFERENCES Sales (Id),
    CONSTRAINT FK_SalesReturns_ReturnedBy  FOREIGN KEY (ReturnedByUserId) REFERENCES Users (Id),
    CONSTRAINT FK_SalesReturns_ApprovedBy  FOREIGN KEY (ApprovedByUserId) REFERENCES Users (Id),
    CONSTRAINT CK_SalesReturns_Status CHECK (Status IN ('PendingApproval', 'Completed', 'Rejected')),
    CONSTRAINT CK_SalesReturns_RefundMethod CHECK (RefundMethod IS NULL OR RefundMethod IN ('Cash', 'Card', 'EWallet')),
    CONSTRAINT CK_SalesReturns_RefundAmount CHECK (RefundAmount IS NULL OR RefundAmount >= 0),
    CONSTRAINT CK_SalesReturns_RefundPairing CHECK (
        (RefundMethod IS NULL AND RefundAmount IS NULL) OR
        (RefundMethod IS NOT NULL AND RefundAmount IS NOT NULL)
    )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Spec section 12's index paragraph names status and dates explicitly, the
-- same pair every prior status-bearing migration indexes.
ALTER TABLE SalesReturns
    ADD INDEX IX_SalesReturns_Status (Status),
    ADD INDEX IX_SalesReturns_ReturnedAtUtc (ReturnedAtUtc);

-- --------------------------------------------------------- SalesReturnLines
CREATE TABLE SalesReturnLines (
    Id                INT NOT NULL AUTO_INCREMENT,
    SalesReturnId     INT NOT NULL,
    SaleLineId        INT NOT NULL,
    ProductId         INT NOT NULL,
    QuantityReturned  DECIMAL(19,3) NOT NULL,
    RestocksItem      TINYINT(1) NOT NULL DEFAULT 1,
    CreatedAtUtc      DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    CONSTRAINT FK_SalesReturnLines_SalesReturns FOREIGN KEY (SalesReturnId) REFERENCES SalesReturns (Id),
    CONSTRAINT FK_SalesReturnLines_SaleLines    FOREIGN KEY (SaleLineId)    REFERENCES SaleLines (Id),
    CONSTRAINT FK_SalesReturnLines_Products     FOREIGN KEY (ProductId)     REFERENCES Products (Id),
    CONSTRAINT CK_SalesReturnLines_QuantityReturned CHECK (QuantityReturned > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
