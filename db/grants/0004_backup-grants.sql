-- =============================================================================
-- 0004_backup-grants.sql
--
-- Grants merch_backup the one write privilege it needs to record its own
-- run. Run as root, AFTER db/migrations/0003_backup.sql.
--
-- WHY merch_backup AND NOT merch_api. The backup job runs as SYSTEM from
-- Task Scheduler, entirely outside the API process, and per ADR-013 each
-- job carries exactly one identity. Having the backup utility open a second
-- connection as merch_api purely to write its log row would blur which
-- account did what, for no gain - the audit trail would say merch_api
-- performed a backup it had no part in.
--
-- AMENDS ADR-013. That entry describes merch_backup as holding
-- "SELECT, LOCK TABLES, SHOW VIEW, EVENT, TRIGGER" and nothing else. It now
-- also holds INSERT on exactly one table. This is a real widening of a
-- least-privilege account and is recorded in the ADR rather than left to be
-- discovered in mysql.tables_priv by whoever next audits the grants.
--
-- WHAT IS DELIBERATELY NOT GRANTED. No UPDATE and no DELETE, so BackupLogs
-- is append-only on the same footing as StockMovements and AuditLogs: a
-- failed run cannot be edited into a successful one. No privilege on any
-- other table - merch_backup still cannot write a single row anywhere else
-- in the schema. Retention pruning deletes FILES on disk, not rows, so it
-- needs no DELETE here.
--
-- Table name is lowercase because @@lower_case_table_names = 1 on Windows
-- (ADR-003.1): `BackupLogs` in the migration is stored as `backuplogs`.
--
-- Re-runnable: GRANT is idempotent.
-- =============================================================================

GRANT INSERT ON `merchandising`.`backuplogs` TO `merch_backup`@`localhost`;

-- merch_backup must also READ SystemSettings to find its retention count,
-- volume label and target directory. It already holds SELECT on
-- merchandising.* from 0001_accounts-and-grants.sql, so nothing is needed
-- here - this comment exists so the absence looks deliberate rather than
-- forgotten.

-- merch_api needs to READ BackupLogs so an operator-facing endpoint can
-- report the last backup result in a later phase. Its database-level SELECT
-- (ADR-013) already covers this. It is granted NO write privilege on this
-- table: the API does not take backups.

FLUSH PRIVILEGES;
