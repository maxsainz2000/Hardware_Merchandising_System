-- =============================================================================
-- 0001_foundation.sql
--
-- P1-07: the POC schema slice. Ten tables named on the task card, applied by
-- Merchandising.Maintenance's MigrationRunner (ADR-008) as merch_migrator.
--
-- After this file, run db/grants/0002_post-migration-grants.sql as root -
-- MariaDB 10.4 rejects a table-level GRANT naming a table that does not yet
-- exist (ERROR 1146), so grant order is: 0001 (accounts) -> this file ->
-- 0002 (per-table grants).
--
-- Append-only is already the default (ADR-013): merch_api holds no
-- database-level write privilege, so every table below is append-only until
-- 0002 grants otherwise, one table at a time. StockMovements and AuditLogs
-- are deliberately never on that list.
--
-- All money DECIMAL(19,4), all quantity DECIMAL(19,3) (ADR-004). Timestamps
-- DATETIME(6) UTC. COLLATE stated explicitly on every table - the server
-- default here is utf8mb4_general_ci, not unicode_ci (ADR-003). Surrogate
-- keys are INT AUTO_INCREMENT: MariaDB 10.4 has no UUID column type (added
-- in 10.7), and this store's scale does not need distributed identifiers.
-- CHAR(36) is used only where a value is genuinely GUID-shaped:
-- StockMovements.CorrelationId and IdempotencyKeys.KeyValue.
-- =============================================================================

-- --------------------------------------------------------------- Users -----
CREATE TABLE Users (
    Id           INT NOT NULL AUTO_INCREMENT,
    Username     VARCHAR(255) NOT NULL,
    PasswordHash VARCHAR(255) NOT NULL,
    IsActive     TINYINT(1) NOT NULL DEFAULT 1,
    CreatedAtUtc DATETIME(6) NOT NULL,
    UpdatedAtUtc DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    UNIQUE KEY UQ_Users_Username (Username)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -------------------------------------------------------------- Roles -----
-- Spec section 9: Super Admin, Admin, Procurement Officer, Inventory Clerk, Cashier.
CREATE TABLE Roles (
    Id   INT NOT NULL AUTO_INCREMENT,
    Name VARCHAR(50) NOT NULL,
    PRIMARY KEY (Id),
    UNIQUE KEY UQ_Roles_Name (Name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO Roles (Name) VALUES
    ('SuperAdmin'),
    ('Admin'),
    ('ProcurementOfficer'),
    ('InventoryClerk'),
    ('Cashier');

-- ---------------------------------------------------------- UserRoles -----
-- Revoking a role removes the row (0002 grants INSERT+DELETE, no UPDATE) -
-- a tombstone that still satisfies a join is not a revoked role.
CREATE TABLE UserRoles (
    UserId         INT NOT NULL,
    RoleId         INT NOT NULL,
    AssignedAtUtc  DATETIME(6) NOT NULL,
    AssignedByUserId INT NULL,
    PRIMARY KEY (UserId, RoleId),
    CONSTRAINT FK_UserRoles_Users FOREIGN KEY (UserId) REFERENCES Users (Id),
    CONSTRAINT FK_UserRoles_Roles FOREIGN KEY (RoleId) REFERENCES Roles (Id),
    CONSTRAINT FK_UserRoles_AssignedBy FOREIGN KEY (AssignedByUserId) REFERENCES Users (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------------------------------------- Products -----
-- Categories/Brands/Units/ProductBarcodes/PriceHistory are out of scope for
-- this slice (Phase 1 scope discipline: one product) - Price and Cost are
-- kept directly on Products rather than modeled as history here.
--
-- Barcode uniqueness is scoped down to "unique when present" (an ordinary
-- nullable UNIQUE index - MariaDB allows multiple NULLs through it), not
-- "unique among active products only". MariaDB 10.4 has no partial/filtered
-- unique index, and this phase has exactly one product to prove anything
-- against; the active-only nuance is deferred rather than worked around.
CREATE TABLE Products (
    Id           INT NOT NULL AUTO_INCREMENT,
    Sku          VARCHAR(255) NOT NULL,
    Barcode      VARCHAR(255) NULL,
    Name         VARCHAR(255) NOT NULL,
    Price        DECIMAL(19,4) NOT NULL,
    Cost         DECIMAL(19,4) NOT NULL,
    IsActive     TINYINT(1) NOT NULL DEFAULT 1,
    CreatedAtUtc DATETIME(6) NOT NULL,
    UpdatedAtUtc DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    UNIQUE KEY UQ_Products_Sku (Sku),
    UNIQUE KEY UQ_Products_Barcode (Barcode)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ------------------------------------------------------- StockBalances -----
-- One balance per product in the single stock pool (spec section 12).
-- RowVersion/UpdatedAtUtc exist for the conditional UPDATE in ADR-006:
--   UPDATE StockBalances SET Quantity = Quantity - @qty, RowVersion = RowVersion + 1, ...
--   WHERE ProductId = @productId AND Quantity >= @qty
CREATE TABLE StockBalances (
    ProductId    INT NOT NULL,
    Quantity     DECIMAL(19,3) NOT NULL DEFAULT 0.000,
    RowVersion   BIGINT NOT NULL DEFAULT 0,
    UpdatedAtUtc DATETIME(6) NOT NULL,
    PRIMARY KEY (ProductId),
    CONSTRAINT FK_StockBalances_Products FOREIGN KEY (ProductId) REFERENCES Products (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ------------------------------------------------------ StockMovements -----
-- Append-only ledger (CLAUDE.md section 5, ADR-013). No UPDATE, no DELETE,
-- ever - 0002 grants INSERT only. Corrections are compensating movements.
CREATE TABLE StockMovements (
    Id             INT NOT NULL AUTO_INCREMENT,
    ProductId      INT NOT NULL,
    Delta          DECIMAL(19,3) NOT NULL,
    QuantityBefore DECIMAL(19,3) NOT NULL,
    QuantityAfter  DECIMAL(19,3) NOT NULL,
    Reason         VARCHAR(255) NOT NULL,
    ActorUserId    INT NOT NULL,
    CorrelationId  CHAR(36) NOT NULL,
    CreatedAtUtc   DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    CONSTRAINT FK_StockMovements_Products FOREIGN KEY (ProductId) REFERENCES Products (Id),
    CONSTRAINT FK_StockMovements_Actor FOREIGN KEY (ActorUserId) REFERENCES Users (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------------------------------------- AuditLogs -----
-- Append-only (CLAUDE.md section 5, ADR-013). No UPDATE, no DELETE, ever -
-- 0002 grants INSERT only.
CREATE TABLE AuditLogs (
    Id            INT NOT NULL AUTO_INCREMENT,
    ActorUserId   INT NULL,
    Action        VARCHAR(255) NOT NULL,
    Target        VARCHAR(255) NOT NULL,
    Result        VARCHAR(50) NOT NULL,
    CorrelationId CHAR(36) NOT NULL,
    Detail        TEXT NULL,
    CreatedAtUtc  DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    CONSTRAINT FK_AuditLogs_Actor FOREIGN KEY (ActorUserId) REFERENCES Users (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ------------------------------------------------------ IdempotencyKeys -----
-- ADR-007: unique constraint on (Scope, KeyValue). Insert-first strategy -
-- the command claims its key before doing work, and the committed response
-- payload is stored and replayed verbatim on repeat.
CREATE TABLE IdempotencyKeys (
    Id              INT NOT NULL AUTO_INCREMENT,
    Scope           VARCHAR(100) NOT NULL,
    KeyValue        CHAR(36) NOT NULL,
    ResponsePayload TEXT NULL,
    CreatedAtUtc    DATETIME(6) NOT NULL,
    CompletedAtUtc  DATETIME(6) NULL,
    PRIMARY KEY (Id),
    UNIQUE KEY UQ_IdempotencyKeys_Scope_KeyValue (Scope, KeyValue)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ------------------------------------------------------- SystemSettings -----
CREATE TABLE SystemSettings (
    SettingKey      VARCHAR(100) NOT NULL,
    SettingValue    VARCHAR(1000) NOT NULL,
    UpdatedAtUtc    DATETIME(6) NOT NULL,
    UpdatedByUserId INT NULL,
    PRIMARY KEY (SettingKey),
    CONSTRAINT FK_SystemSettings_UpdatedBy FOREIGN KEY (UpdatedByUserId) REFERENCES Users (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------------------------------- SchemaMigrations -----
-- Already bootstrapped by MigrationRunner.vb itself (ADR-008) before this
-- file could ever run - the runner needs the table to exist before it can
-- ask "has 0001 been applied yet?". IF NOT EXISTS makes this line a no-op
-- collision-avoider, not a real create; the column list matches
-- MigrationRunner.EnsureSchemaMigrationsTableAsync exactly on purpose.
CREATE TABLE IF NOT EXISTS SchemaMigrations (
    MigrationId  VARCHAR(255) NOT NULL,
    Checksum     CHAR(64) NOT NULL,
    AppliedAtUtc DATETIME(6) NOT NULL,
    Succeeded    TINYINT(1) NOT NULL,
    ErrorMessage TEXT NULL,
    PRIMARY KEY (MigrationId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
