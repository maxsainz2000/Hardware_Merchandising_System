-- =============================================================================
-- 0001_accounts-and-grants.sql
--
-- Creates the three database identities this system uses and grants each the
-- least privilege that lets it do its job. Run ONCE, as root, against a fresh
-- MariaDB instance, BEFORE any migration.
--
-- Target: MariaDB 10.4.32 as supplied by XAMPP 8.2.12-0 (ADR-002).
--
-- PLACEHOLDERS. Replace the three {{...}} tokens with per-installation
-- passwords before running. ADR-012 requirement 6: credentials are generated
-- per installation and never committed. No password known to the author may
-- be the password protecting a classmate's demo.
--
-- WHY THREE IDENTITIES AND NOT ONE (ADR-013)
--
--   merch_migrator  owns the SCHEMA. It is the only account that may create,
--                   alter or drop a table. It is used by
--                   Merchandising.Maintenance during a migration run and at
--                   no other time.
--
--   merch_api       owns the DATA. It has NO DDL at all - it cannot create,
--                   alter or drop anything. At the database level it holds
--                   only SELECT and INSERT, which makes every table
--                   append-only BY DEFAULT. UPDATE and DELETE are then added
--                   back one table at a time by 0002, and StockMovements and
--                   AuditLogs are deliberately not on that list.
--
--   merch_backup    reads for mysqldump and nothing else.
--
-- THE POINT OF THE SPLIT. CLAUDE.md section 5 requires StockMovements and
-- AuditLogs to be append-only "enforced by database grants as well as by
-- policy". MariaDB unions privileges across scopes and has no DENY, so a
-- database-level GRANT UPDATE ON merchandising.* cannot be subtracted from
-- for one table. The only way to make the guarantee real is to never grant
-- UPDATE at the database level in the first place. That is what this file
-- does, and it is why append-only is the default here rather than an
-- exception carved out later.
-- =============================================================================

-- --------------------------------------------------------------- database ---
CREATE DATABASE IF NOT EXISTS `merchandising`
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;
-- COLLATE is stated explicitly on purpose: the server default here is
-- utf8mb4_general_ci, not unicode_ci (ADR-003). Inheriting it silently gets
-- the wrong collation and an "Illegal mix of collations" error on a join
-- months later.

-- ------------------------------------------------------- merch_migrator -----
-- Schema owner. The ONLY account with DDL. Also holds DML, because a
-- migration legitimately needs to backfill or correct data in the same
-- transaction that changes a table's shape.
CREATE USER IF NOT EXISTS `merch_migrator`@`localhost`
    IDENTIFIED BY '{{MERCH_MIGRATOR_PASSWORD}}';

GRANT SELECT, INSERT, UPDATE, DELETE,
      CREATE, ALTER, DROP, INDEX, REFERENCES
    ON `merchandising`.*
    TO `merch_migrator`@`localhost`;

-- Deliberately NOT granted: GRANT OPTION. The migration runner must never be
-- able to widen anyone's privileges, including its own. The per-table grants
-- in 0002 are applied by root as an explicit install step, not by a migration.
-- Deliberately NOT granted: any privilege on any other schema, and no global
-- privileges beyond USAGE.

-- ------------------------------------------------------------ merch_api -----
-- Runtime account for the API. NO DDL, and at the database level NO WRITE OF
-- ANY KIND - only SELECT. Every write privilege it has is granted one table
-- at a time by 0002, after the migration has created the tables.
CREATE USER IF NOT EXISTS `merch_api`@`localhost`
    IDENTIFIED BY '{{MERCH_API_PASSWORD}}';

GRANT SELECT
    ON `merchandising`.*
    TO `merch_api`@`localhost`;

-- Deliberately NOT granted at database level:
--   INSERT, UPDATE, DELETE  - all three are per-table, in 0002. Granting even
--                             INSERT here would let the API write rows into
--                             SchemaMigrations, which could make the migration
--                             runner skip a migration it never applied.
--   CREATE / ALTER / DROP / INDEX / REFERENCES
--                           - never. The API does not own the schema.
--
-- Database-level SELECT is deliberate and is the one exception: reading is not
-- the threat this split defends against, and scoping it per table would mean
-- every future migration silently breaking reads until someone remembered to
-- add a grant.

-- --------------------------------------------------------- merch_backup -----
-- Unchanged from P1-04. Exactly what mysqldump needs, nothing more.
CREATE USER IF NOT EXISTS `merch_backup`@`localhost`
    IDENTIFIED BY '{{MERCH_BACKUP_PASSWORD}}';

GRANT SELECT, LOCK TABLES, SHOW VIEW, EVENT, TRIGGER
    ON `merchandising`.*
    TO `merch_backup`@`localhost`;

FLUSH PRIVILEGES;
