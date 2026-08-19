-- =============================================================================
-- 0003_authentication-grants.sql
--
-- Grants merch_api its write privileges on the table introduced by
-- 0002_authentication.sql. Run as root, AFTER that migration.
--
-- Users already holds INSERT, UPDATE for merch_api (0002_post-migration-
-- grants.sql) - the new FailedLoginAttempts/LockedUntilUtc columns are
-- covered by that existing grant and need nothing here.
--
-- Table name is lowercase because @@lower_case_table_names = 1 on Windows
-- (ADR-003.1): `Sessions` in the migration is stored as `sessions`.
--
-- Re-runnable: GRANT is idempotent.
-- =============================================================================

-- Logout deletes the row rather than soft-revoking it (CLAUDE.md's
-- append-only rule does not apply here - Sessions is not StockMovements or
-- AuditLogs), so no UPDATE is granted, only INSERT and DELETE.
GRANT INSERT, DELETE ON `merchandising`.`sessions` TO `merch_api`@`localhost`;

FLUSH PRIVILEGES;
