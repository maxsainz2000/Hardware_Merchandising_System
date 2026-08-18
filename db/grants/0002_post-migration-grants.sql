-- =============================================================================
-- 0002_post-migration-grants.sql
--
-- Grants merch_api its write privileges, ONE TABLE AT A TIME.
--
-- Run as root, AFTER migration 0001_foundation.sql has created the tables.
-- It cannot be run before: MariaDB 10.4 rejects a table-level GRANT naming a
-- table that does not exist yet --
--
--     ERROR 1146 (42S02): Table 'merchandising.does_not_exist_yet' doesn't exist
--
-- (verified on this server, 2026-08-18). That ordering constraint is the whole
-- reason this is a separate file from 0001 rather than a section of it.
--
-- Re-runnable: GRANT is idempotent.
--
-- -----------------------------------------------------------------------------
-- THE TWO TABLES THAT ARE ABSENT FROM THIS FILE ARE THE POINT OF THIS FILE.
--
-- `stockmovements` and `auditlogs` receive INSERT and nothing else. They are
-- never granted UPDATE or DELETE, to anyone, at any scope. Combined with
-- 0001 - which gives merch_api no database-level write privilege at all -
-- that makes CLAUDE.md section 5's "append-only, enforced by database grants"
-- a fact about this server rather than a statement of intent.
--
-- Corrections to either table are compensating rows, never edits. That is
-- stop condition 7 in CLAUDE.md, and after this file it is enforced by the
-- server, not by remembering.
--
-- Do not add UPDATE or DELETE on those two tables to this file. If a task
-- appears to need it, the task is wrong.
-- -----------------------------------------------------------------------------
--
-- Table names are lowercase because @@lower_case_table_names = 1 on Windows
-- (ADR-003.1): `StockMovements` in the migration is stored as `stockmovements`.
-- =============================================================================

-- ------------------------------------------------- append-only ledgers ------
-- INSERT only. No UPDATE. No DELETE. Ever.
GRANT INSERT ON `merchandising`.`stockmovements` TO `merch_api`@`localhost`;
GRANT INSERT ON `merchandising`.`auditlogs`      TO `merch_api`@`localhost`;

-- ------------------------------------------------------- mutable tables -----
-- UPDATE is granted where a row legitimately changes in place.
GRANT INSERT, UPDATE ON `merchandising`.`users`          TO `merch_api`@`localhost`;
GRANT INSERT, UPDATE ON `merchandising`.`roles`          TO `merch_api`@`localhost`;
GRANT INSERT, UPDATE ON `merchandising`.`products`       TO `merch_api`@`localhost`;
GRANT INSERT, UPDATE ON `merchandising`.`systemsettings` TO `merch_api`@`localhost`;

-- The conditional decrement in ADR-006 is an UPDATE against this table.
GRANT INSERT, UPDATE ON `merchandising`.`stockbalances`  TO `merch_api`@`localhost`;

-- ------------------------------------------- tables that also need DELETE ---
-- Role assignment is removed, not soft-deleted: a revoked role must stop
-- granting access, and a tombstone row that still satisfies a join does not.
GRANT INSERT, DELETE ON `merchandising`.`userroles` TO `merch_api`@`localhost`;

-- Idempotency keys are UPDATEd once (to store the committed response payload
-- for replay, per ADR-007) and DELETEd by expiry sweep.
GRANT INSERT, UPDATE, DELETE ON `merchandising`.`idempotencykeys` TO `merch_api`@`localhost`;

-- ----------------------------------------------------- deliberately absent --
-- `schemamigrations`  - merch_api gets no write privilege. The migration
--                       runner owns that table and runs as merch_migrator. A
--                       row inserted by the API could make the runner skip a
--                       migration that was never applied.
--
-- DELETE on `users` and `products` - deactivate, do not delete. Deleting a
--                       user or product orphans the StockMovements and
--                       AuditLogs rows that reference it, which is exactly
--                       the history those tables exist to preserve.

FLUSH PRIVILEGES;

-- =============================================================================
-- VERIFY AFTER RUNNING. Both of these must fail with ERROR 1142:
--
--   mysql -u merch_api -p merchandising -e "UPDATE auditlogs SET Actor='x';"
--   mysql -u merch_api -p merchandising -e "DELETE FROM stockmovements;"
--
-- That pair is P1-07's acceptance evidence.
-- =============================================================================
