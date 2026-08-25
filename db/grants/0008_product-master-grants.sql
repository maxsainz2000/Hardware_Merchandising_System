-- =============================================================================
-- 0008_product-master-grants.sql
--
-- Run as root, AFTER migration 0006_product-master.sql has created
-- Categories, Brands, Units, ProductBarcodes, and PriceHistory, and altered
-- Products. Same table-must-exist-first ordering constraint as every prior
-- migration -> grants pair (ADR-013, ERROR 1146 if reversed).
--
-- Re-runnable: GRANT is idempotent.
--
-- Products itself needs no new GRANT here: 0002_post-migration-grants.sql
-- already gave merch_api INSERT, UPDATE on `products`, and that privilege is
-- table-level, not column-level - it already covers every column this
-- migration added.
--
-- -----------------------------------------------------------------------------
-- THE TABLE THAT IS ABSENT FROM THIS FILE IS THE POINT OF THIS FILE.
--
-- `pricehistory` receives INSERT only - never UPDATE, never DELETE, to
-- anyone, at any scope. Same treatment as `stockmovements`/`auditlogs` in
-- 0002_post-migration-grants.sql: a price/cost correction is a new row, not
-- an edit to an old one. merch_api already holds database-level SELECT
-- (ADR-013).
-- -----------------------------------------------------------------------------
--
-- Table names are lowercase because @@lower_case_table_names = 1 on Windows
-- (ADR-003.1): `Categories` in the migration is stored as `categories`.
-- =============================================================================

-- ------------------------------------------------------- append-only ledger --
GRANT INSERT ON `merchandising`.`pricehistory` TO `merch_api`@`localhost`;

-- ------------------------------------------------------------- mutable catalogs --
-- Deletion is never granted: a category/brand/unit referenced by a product
-- is blocked from deletion by FK regardless, and one that is not referenced
-- has no scenario in this MVP that needs it removed rather than left unused
-- - same treatment as `roles` in 0002_post-migration-grants.sql.
GRANT INSERT, UPDATE ON `merchandising`.`categories` TO `merch_api`@`localhost`;
GRANT INSERT, UPDATE ON `merchandising`.`brands`     TO `merch_api`@`localhost`;
GRANT INSERT, UPDATE ON `merchandising`.`units`      TO `merch_api`@`localhost`;

-- ---------------------------------------------------------- alternate barcodes --
-- An alternate barcode is added and removed as a real row, like `userroles`:
-- a retired alternate code must stop matching a lookup, and a tombstone row
-- that still satisfies a join does not achieve that. UPDATE covers
-- correcting a mis-scanned/mis-entered value without a delete-then-reinsert.
GRANT INSERT, UPDATE, DELETE ON `merchandising`.`productbarcodes` TO `merch_api`@`localhost`;

FLUSH PRIVILEGES;

-- =============================================================================
-- VERIFY AFTER RUNNING. Must fail with ERROR 1142:
--
--   mysql -u merch_api -p merchandising -e "UPDATE pricehistory SET OldValue=0;"
--   mysql -u merch_api -p merchandising -e "DELETE FROM pricehistory;"
-- =============================================================================
