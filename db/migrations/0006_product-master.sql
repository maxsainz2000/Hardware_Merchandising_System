-- =============================================================================
-- 0006_product-master.sql
--
-- P2-06: the five product-master entities spec section 12 lists alongside
-- Products - Categories, Brands, Units, ProductBarcodes, PriceHistory - plus
-- the Products columns P1-07's one-product POC slice deliberately left out
-- (Description, CategoryId, BrandId, UnitId, ReorderLevel, RowVersion).
--
-- Applied by merch_migrator, after 0001-0005. After this file, run
-- db/grants/0008_product-master-grants.sql as root - same table-must-
-- exist-first ordering constraint as every prior migration -> grants pair
-- (ADR-013, ERROR 1146 if reversed).
--
-- All money DECIMAL(19,4), all quantity DECIMAL(19,3) (ADR-004). Timestamps
-- DATETIME(6) UTC. COLLATE stated explicitly on every table (ADR-003).
-- =============================================================================

-- --------------------------------------------------------------- Categories --
-- Permanent reference data, not a lifecycle entity - spec section 12's
-- "active/inactive lifecycle" clause is Products' own lifecycle (P2-09), not
-- this catalog. A category referenced by a Product cannot be deleted: no
-- ON DELETE clause below means MariaDB's default (RESTRICT), the same
-- unadorned-FK pattern 0001 already uses for StockBalances -> Products.
CREATE TABLE Categories (
    Id           INT NOT NULL AUTO_INCREMENT,
    Name         VARCHAR(255) NOT NULL,
    CreatedAtUtc DATETIME(6) NOT NULL,
    UpdatedAtUtc DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    UNIQUE KEY UQ_Categories_Name (Name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ------------------------------------------------------------------ Brands --
CREATE TABLE Brands (
    Id           INT NOT NULL AUTO_INCREMENT,
    Name         VARCHAR(255) NOT NULL,
    CreatedAtUtc DATETIME(6) NOT NULL,
    UpdatedAtUtc DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    UNIQUE KEY UQ_Brands_Name (Name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ------------------------------------------------------------------- Units --
-- e.g. "pcs", "kg", "box" - the unit of measure a product is stocked/sold in.
CREATE TABLE Units (
    Id           INT NOT NULL AUTO_INCREMENT,
    Name         VARCHAR(50) NOT NULL,
    CreatedAtUtc DATETIME(6) NOT NULL,
    UpdatedAtUtc DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    UNIQUE KEY UQ_Units_Name (Name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -------------------------------------------------- Products (add columns) --
-- CategoryId/BrandId/UnitId are nullable at the database layer on purpose:
-- spec section 11 calls only barcode "optional" among these attributes, but
-- making them NOT NULL here would force a backfill decision for the live
-- P1-07 POC row that is an API-validation call (ADR-004.1's "the API's job,
-- not the database's" precedent), not a schema one. P2-07 may require them
-- at the API boundary without a further migration.
ALTER TABLE Products
    ADD COLUMN Description  TEXT NULL AFTER Name,
    ADD COLUMN CategoryId   INT NULL AFTER Description,
    ADD COLUMN BrandId      INT NULL AFTER CategoryId,
    ADD COLUMN UnitId       INT NULL AFTER BrandId,
    ADD COLUMN ReorderLevel DECIMAL(19,3) NOT NULL DEFAULT 0.000 AFTER Cost,
    ADD COLUMN RowVersion   BIGINT NOT NULL DEFAULT 0 AFTER IsActive,
    ADD CONSTRAINT FK_Products_Categories FOREIGN KEY (CategoryId) REFERENCES Categories (Id),
    ADD CONSTRAINT FK_Products_Brands     FOREIGN KEY (BrandId)    REFERENCES Brands (Id),
    ADD CONSTRAINT FK_Products_Units      FOREIGN KEY (UnitId)     REFERENCES Units (Id);

-- --------------------------------------- Products barcode uniqueness (ADR-018) --
-- Decision recorded in full at docs/adr.md ADR-018. Summary: MariaDB 10.4 has
-- no partial/filtered unique index (added 10.5+), so "unique active barcode"
-- is expressed with a VIRTUAL generated column plus an ordinary UNIQUE KEY on
-- it - the same unique-index mechanism P1-13 proved is the only thing that
-- survives concurrency, just scoped to active rows by the generated
-- expression rather than by a WHERE clause MariaDB 10.4 cannot write.
--
-- 0001's unconditional UQ_Products_Barcode is dropped: it currently blocks
-- two *inactive* products from ever sharing a barcode value, which spec
-- section 12 does not ask for and which would block P2-09 reactivation
-- (deactivating a product should free its barcode for reuse). A plain
-- non-unique index replaces it for ordinary lookup, including inactive rows.
ALTER TABLE Products
    DROP INDEX UQ_Products_Barcode,
    ADD INDEX IX_Products_Barcode (Barcode),
    ADD COLUMN ActiveBarcode VARCHAR(255)
        GENERATED ALWAYS AS (CASE WHEN IsActive = 1 THEN Barcode ELSE NULL END) VIRTUAL,
    ADD UNIQUE KEY UQ_Products_ActiveBarcode (ActiveBarcode);

-- Product-name search index (spec section 12's index paragraph).
ALTER TABLE Products
    ADD INDEX IX_Products_Name (Name);

-- ------------------------------------------------------------ ProductBarcodes --
-- Additional/alternate scannable codes for a product, beyond its primary
-- Products.Barcode (e.g. a case/box code alongside the unit code). Globally
-- unique regardless of the owning product's active state: spec section 12's
-- "unique active barcode" integrity requirement names the product's own
-- barcode attribute, not this supplementary table.
CREATE TABLE ProductBarcodes (
    Id           INT NOT NULL AUTO_INCREMENT,
    ProductId    INT NOT NULL,
    Barcode      VARCHAR(255) NOT NULL,
    CreatedAtUtc DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    UNIQUE KEY UQ_ProductBarcodes_Barcode (Barcode),
    CONSTRAINT FK_ProductBarcodes_Products FOREIGN KEY (ProductId) REFERENCES Products (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ------------------------------------------------------------------ PriceHistory --
-- Append-only ledger (CLAUDE.md section 5, ADR-013), like StockMovements and
-- AuditLogs. No UPDATE, no DELETE, ever - 0008 grants INSERT only.
-- ChangedField distinguishes a Price change from a Cost change so both of
-- spec section 12's "price/cost history" live in one ledger table rather
-- than two near-identical ones.
CREATE TABLE PriceHistory (
    Id            INT NOT NULL AUTO_INCREMENT,
    ProductId     INT NOT NULL,
    ChangedField  VARCHAR(10) NOT NULL,
    OldValue      DECIMAL(19,4) NOT NULL,
    NewValue      DECIMAL(19,4) NOT NULL,
    ActorUserId   INT NOT NULL,
    EffectiveAtUtc DATETIME(6) NOT NULL,
    CorrelationId CHAR(36) NOT NULL,
    CreatedAtUtc  DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    CONSTRAINT FK_PriceHistory_Products FOREIGN KEY (ProductId) REFERENCES Products (Id),
    CONSTRAINT FK_PriceHistory_Actor    FOREIGN KEY (ActorUserId) REFERENCES Users (Id),
    CONSTRAINT CK_PriceHistory_ChangedField CHECK (ChangedField IN ('Price', 'Cost'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
