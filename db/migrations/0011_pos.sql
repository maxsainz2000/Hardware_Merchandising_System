-- =============================================================================
-- 0011_pos.sql
--
-- P5-02: the cashier session (spec section 10.3: "A sale requires an open
-- cashier session"), the sale header, its lines, and the payment records -
-- spec section 11's atomic-result row for "Sale completed" ("Sale, sale
-- lines, payment, stock-out movements, balance changes, session totals, and
-- audit event commit together") and spec section 12's POS entity group.
-- Card names the tables CashierSessions / Sales / SaleLines / SalePayments -
-- not SaleItems / Payments as spec section 12's forward list writes them -
-- the same "Lines, not Items" naming precedent 0008/0009/0010 already
-- resolved, extended here to "SalePayments, not Payments" for the same
-- reason: spec section 12 explicitly delegates "exact columns, names, and
-- indexes" to this deliverable.
--
-- Applied by merch_migrator, after 0001-0010. After this file, run
-- db/grants/0013_pos-grants.sql as root - same table-must-exist-first
-- ordering constraint as every prior migration -> grants pair (ADR-013,
-- ERROR 1146 if reversed).
--
-- All money DECIMAL(19,4), all quantity DECIMAL(19,3) (ADR-004). Timestamps
-- DATETIME(6) UTC. COLLATE stated explicitly on every table (ADR-003) - the
-- server default here is utf8mb4_general_ci, and inheriting it earns an
-- "Illegal mix of collations" on a join months later.
--
-- -----------------------------------------------------------------------------
-- THREE STATUS/METHOD COLUMNS, ALL COLLATE utf8mb4_bin, FOR THE SAME REASON
-- FOUND TWICE ALREADY (ADR-020, amended at P3-02; repeated at 0009, 0010).
-- Under the table's own utf8mb4_unicode_ci collation, 'open' IN ('Open', ...)
-- evaluates to TRUE, so a CHECK would pass while storing a value no enum
-- name matches. utf8mb4_bin makes the CHECK compare byte for byte.
--
-- CashierSessions.Status, Sales.Status and SalePayments.Method all carry it.
-- Enum names are the stable identifiers (Merchandising.Domain.Sales.
-- CashierSessionStatus / SaleStatus / PaymentMethod, new in this card, the
-- same StockCountStatus/StockAdjustmentStatus precedent P4-03 set: the
-- schema card defines the enum, and PosSchemaTests walks [Enum].GetNames
-- against each CHECK rather than trusting a hand-typed list to stay in sync).
--
-- NO PERSISTED "IN-PROGRESS" SALE. Spec section 10.3: "A sale completion
-- creates the sale header, sale lines, payment record(s), stock-out
-- movements, updated stock balances, cashier-session totals, and audit
-- record in one transaction." The atomic sale (P5-07) is the only writer of
-- Sales, and it writes a Completed row or nothing at all - there is no
-- earlier "cart" row for a not-yet-submitted sale to attach to, so
-- "cancelled only before completion" (spec section 10.3) is a client-side,
-- pre-API-call fact, never a database transition. SaleStatus.Cancelled is
-- declared for completeness - the same StockCounts/StockAdjustments shape
-- of naming a state before a later card drives it (P4-03) - but no card in
-- this phase writes it. Confirmed with the user at P5-02.
--
-- Sales AND SalePayments ARE APPEND-ONLY, LIKE Receipts/ReceiptLines - NOT
-- LIKE PurchaseReturns. Spec section 10.3: "Completed sales are never
-- edited or deleted." Corrections after completion are returns (P5-11),
-- never edits. So unlike PurchaseOrders/PurchaseReturns/StockCounts/
-- StockAdjustments, Sales and SalePayments (and SaleLines, their child)
-- carry no RowVersion and no UpdatedAtUtc - CreatedAtUtc only, the same
-- immutable shape 0009 gives Receipts/ReceiptLines. db/grants/0013 grants
-- INSERT only on all three; the argument is made there, not assumed here.
--
-- CashierSessions IS THE ONE MUTABLE TABLE IN THIS FILE. Its status changes
-- in place (Open -> Closed, spec section 10.3's "closing" paragraph), and
-- DeclaredCash/CalculatedCash/CashVariance/ClosedByUserId/ClosedAtUtc are
-- unset until that transition - the same nullable-until-decided,
-- RowVersion-carrying shape 0008/0009/0010 give PurchaseOrders/
-- PurchaseReturns/StockCounts/StockAdjustments. These closing columns live
-- on this card even though "Do" names only opening float/opened-closed-by/
-- timestamps/status, because P5-05 (daily closing) has no migration file of
-- its own to add them in - they must exist after this card or not at all.
-- CashierSessions carries no running TotalSalesAmount/TransactionCount:
-- P5-05's own rule ("Calculated cash is computed from committed
-- SalePayments rows, never from a client figure") is a special case of a
-- wider principle already load-bearing in this codebase (ADR-021's ledger
-- reconciliation) - a maintained counter can drift from the ledger it
-- summarizes, so nothing here maintains one. Spec section 11's "session
-- totals" atomic-sale effect is satisfied structurally: Sales.CashierSessionId
-- is written in the same transaction as everything else, and every total is
-- a query over committed Sales/SalePayments rows, never a stored running sum.
--
-- ONE OPEN SESSION PER CASHIER, ENFORCED NOW, BY THE ADR-018 GENERATED-
-- COLUMN TRICK. P5-04 (the API card that opens/closes a session) has no
-- migration file of its own either - its own done-when box asks only for
-- "enforced by a unique index, proven by a concurrent double-open" - so the
-- index itself has to exist after this card. MariaDB 10.4 has no partial/
-- filtered unique index (added 10.5+), so uniqueness is scoped to Open rows
-- by a VIRTUAL generated column (NULL when Closed; MariaDB unique indexes
-- do not collide on NULL) exactly as ADR-018 scopes barcode uniqueness to
-- active rows - the same mechanism, a different predicate.
--
-- CashierUserId ON Sales, EVEN THOUGH IT IS REACHABLE THROUGH
-- CashierSessionId. Same argument 0008/0009/0010 already make for ProductId
-- on every transactional line: spec section 14's "Sales by cashier" report
-- names cashier as a required column, and a captured actor reference on the
-- sale survives however the session chain above it is later queried.
--
-- TenderedAmount/ChangeAmount ON SalePayments ARE NULLABLE, CASH ONLY. Spec
-- section 10.3: "the API verifies that the tendered amount is at least the
-- sale total and calculates change" and "display receipt data." Amount is
-- the value applied to the sale (what the drawer actually keeps for a cash
-- line); Tendered/Change preserve what the customer handed over and got
-- back, for a reprinted receipt, mirroring the CashTenderResult shape P5-01
-- already built in Domain. A CHECK ties their presence to Method = 'Cash'
-- so a card/e-wallet row cannot carry them and a cash row cannot omit them.
-- -----------------------------------------------------------------------------
-- =============================================================================

-- ------------------------------------------------------------ CashierSessions
-- No ON DELETE clause on either user FK, so MariaDB's default RESTRICT
-- applies - the same unadorned-FK pattern every prior migration uses. Spec
-- section 12: "Foreign-key behavior must prevent accidental deletion of
-- records referenced by sales, receipts, returns, movements, or audit events."
CREATE TABLE CashierSessions (
    Id                INT NOT NULL AUTO_INCREMENT,
    OpenedByUserId    INT NOT NULL,
    ClosedByUserId    INT NULL,
    OpeningFloat      DECIMAL(19,4) NOT NULL,
    DeclaredCash      DECIMAL(19,4) NULL,
    CalculatedCash    DECIMAL(19,4) NULL,
    CashVariance      DECIMAL(19,4) NULL,
    Status            VARCHAR(20) COLLATE utf8mb4_bin NOT NULL,
    OpenedAtUtc       DATETIME(6) NOT NULL,
    ClosedAtUtc       DATETIME(6) NULL,
    OpenSessionOwner  INT GENERATED ALWAYS AS (CASE WHEN Status = 'Open' THEN OpenedByUserId ELSE NULL END) VIRTUAL,
    RowVersion        BIGINT NOT NULL DEFAULT 0,
    CreatedAtUtc      DATETIME(6) NOT NULL,
    UpdatedAtUtc      DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    UNIQUE KEY UQ_CashierSessions_OpenSessionOwner (OpenSessionOwner),
    CONSTRAINT FK_CashierSessions_OpenedBy FOREIGN KEY (OpenedByUserId) REFERENCES Users (Id),
    CONSTRAINT FK_CashierSessions_ClosedBy FOREIGN KEY (ClosedByUserId) REFERENCES Users (Id),
    CONSTRAINT CK_CashierSessions_Status CHECK (Status IN ('Open', 'Closed')),
    CONSTRAINT CK_CashierSessions_OpeningFloat CHECK (OpeningFloat >= 0),
    CONSTRAINT CK_CashierSessions_DeclaredCash CHECK (DeclaredCash IS NULL OR DeclaredCash >= 0),
    CONSTRAINT CK_CashierSessions_CalculatedCash CHECK (CalculatedCash IS NULL OR CalculatedCash >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Spec section 12's index paragraph names status and dates explicitly, the
-- same pair every prior status-bearing migration indexes.
ALTER TABLE CashierSessions
    ADD INDEX IX_CashierSessions_Status (Status),
    ADD INDEX IX_CashierSessions_OpenedAtUtc (OpenedAtUtc);

-- ------------------------------------------------------------------- Sales --
CREATE TABLE Sales (
    Id                INT NOT NULL AUTO_INCREMENT,
    CashierSessionId  INT NOT NULL,
    CashierUserId     INT NOT NULL,
    Total             DECIMAL(19,4) NOT NULL,
    Status            VARCHAR(20) COLLATE utf8mb4_bin NOT NULL,
    CorrelationId     CHAR(36) NOT NULL,
    CreatedAtUtc      DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    CONSTRAINT FK_Sales_CashierSessions FOREIGN KEY (CashierSessionId) REFERENCES CashierSessions (Id),
    CONSTRAINT FK_Sales_CashierUser     FOREIGN KEY (CashierUserId)    REFERENCES Users (Id),
    CONSTRAINT CK_Sales_Total  CHECK (Total >= 0),
    CONSTRAINT CK_Sales_Status CHECK (Status IN ('Completed', 'Cancelled'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE Sales
    ADD INDEX IX_Sales_Status (Status),
    ADD INDEX IX_Sales_CreatedAtUtc (CreatedAtUtc);

-- -------------------------------------------------------------- SaleLines --
-- UnitPrice/Cost are the effective, captured values (spec section 10.3:
-- "captures the effective unit price and recorded cost in the sale line so
-- future price changes do not alter historical sales analysis") - never a
-- join to Products.Price/Cost. LineTotal is likewise captured, not derived
-- by a later SELECT: ADR-004.1's "totals are derived from already-rounded
-- components, never rounded independently of them" - the API computes and
-- rounds it once, at construction (Merchandising.Domain.Sales.SaleLine,
-- P5-01), and this column stores that value rather than recomputing
-- Quantity * UnitPrice at read time, which could disagree if the rounding
-- policy setting changes after the sale.
CREATE TABLE SaleLines (
    Id           INT NOT NULL AUTO_INCREMENT,
    SaleId       INT NOT NULL,
    ProductId    INT NOT NULL,
    Quantity     DECIMAL(19,3) NOT NULL,
    UnitPrice    DECIMAL(19,4) NOT NULL,
    Cost         DECIMAL(19,4) NOT NULL,
    LineTotal    DECIMAL(19,4) NOT NULL,
    CreatedAtUtc DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    CONSTRAINT FK_SaleLines_Sales    FOREIGN KEY (SaleId)    REFERENCES Sales (Id),
    CONSTRAINT FK_SaleLines_Products FOREIGN KEY (ProductId) REFERENCES Products (Id),
    CONSTRAINT CK_SaleLines_Quantity  CHECK (Quantity > 0),
    CONSTRAINT CK_SaleLines_UnitPrice CHECK (UnitPrice >= 0),
    CONSTRAINT CK_SaleLines_Cost       CHECK (Cost >= 0),
    CONSTRAINT CK_SaleLines_LineTotal CHECK (LineTotal >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------- SalePayments --
CREATE TABLE SalePayments (
    Id              INT NOT NULL AUTO_INCREMENT,
    SaleId          INT NOT NULL,
    Method          VARCHAR(20) COLLATE utf8mb4_bin NOT NULL,
    Amount          DECIMAL(19,4) NOT NULL,
    TenderedAmount  DECIMAL(19,4) NULL,
    ChangeAmount    DECIMAL(19,4) NULL,
    CreatedAtUtc    DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    CONSTRAINT FK_SalePayments_Sales FOREIGN KEY (SaleId) REFERENCES Sales (Id),
    CONSTRAINT CK_SalePayments_Method CHECK (Method IN ('Cash', 'Card', 'EWallet')),
    CONSTRAINT CK_SalePayments_Amount CHECK (Amount >= 0),
    CONSTRAINT CK_SalePayments_CashTenderPairing CHECK (
        (Method = 'Cash' AND TenderedAmount IS NOT NULL AND ChangeAmount IS NOT NULL) OR
        (Method <> 'Cash' AND TenderedAmount IS NULL AND ChangeAmount IS NULL)
    ),
    CONSTRAINT CK_SalePayments_TenderedAmount CHECK (TenderedAmount IS NULL OR TenderedAmount >= Amount),
    CONSTRAINT CK_SalePayments_ChangeAmount CHECK (ChangeAmount IS NULL OR ChangeAmount = TenderedAmount - Amount)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
