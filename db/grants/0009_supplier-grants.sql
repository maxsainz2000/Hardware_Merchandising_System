-- =============================================================================
-- 0009_supplier-grants.sql
--
-- Run as root, AFTER migration 0007_suppliers.sql has created Suppliers.
-- Same table-must-exist-first ordering constraint as every prior migration ->
-- grants pair (ADR-013, ERROR 1146 if reversed).
--
-- Re-runnable: GRANT is idempotent.
--
-- INSERT, UPDATE only - same "deactivate, never delete" shape as
-- 0002_post-migration-grants.sql's grant on `products`: no DELETE, ever.
-- Deletion is never attempted anywhere in this codebase for Suppliers -
-- deactivation (P2-10 / ProductLifecycleService's own P2-09 precedent) is
-- the only removal story.
--
-- Table name is lowercase because @@lower_case_table_names = 1 on Windows
-- (ADR-003.1): `Suppliers` in the migration is stored as `suppliers`.
-- =============================================================================

GRANT INSERT, UPDATE ON `merchandising`.`suppliers` TO `merch_api`@`localhost`;

FLUSH PRIVILEGES;

-- =============================================================================
-- VERIFY AFTER RUNNING. Must fail with ERROR 1142:
--
--   mysql -u merch_api -p merchandising -e "DELETE FROM suppliers;"
-- =============================================================================
