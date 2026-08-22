-- =============================================================================
-- 0005_maintenance-grants.sql
--
-- Grants merch_api its privileges on the table introduced by
-- 0004_maintenance.sql. Run as root, AFTER that migration.
--
-- INSERT + UPDATE, and no DELETE. The lock has a lifecycle - acquired, then
-- released - so the release is an UPDATE of the acquiring row. That follows
-- the Sessions precedent from 0003_authentication-grants.sql rather than the
-- StockMovements one: CLAUDE.md's append-only rule names the two ledgers
-- specifically, and this is operational state, not a ledger.
--
-- DELETE is withheld on purpose. There is no legitimate reason to erase the
-- record that the system was in maintenance - that history is exactly what
-- someone reconstructing an incident needs, and a lock that can be deleted
-- rather than released is a lock whose audit trail can be made to disappear.
-- Retention of old lock rows is not a problem worth solving: this table gains
-- a row per maintenance window, which is a handful per year.
--
-- merch_backup is granted NOTHING here. It holds database-level SELECT
-- already (0001), which is all mysqldump needs to include this table in a
-- dump. It has no business changing maintenance state.
--
-- Table name is lowercase because @@lower_case_table_names = 1 on Windows
-- (ADR-003.1): `MaintenanceLocks` in the migration is stored as
-- `maintenancelocks`.
--
-- Re-runnable: GRANT is idempotent.
-- =============================================================================

GRANT INSERT, UPDATE ON `merchandising`.`maintenancelocks` TO `merch_api`@`localhost`;

-- No grant is needed for reading the maintenance message from SystemSettings:
-- merch_api holds database-level SELECT (ADR-013), and it already holds
-- INSERT, UPDATE on systemsettings from 0002_post-migration-grants.sql.
-- Stated so the absence reads as deliberate rather than forgotten.

FLUSH PRIVILEGES;
