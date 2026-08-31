-- =============================================================================
-- 0015_restore-rehearsal-grants.sql
--
-- P6-08. Creates ONE throwaway schema, `merchandising_restoretest`, and grants
-- merch_migrator DDL on it and nothing else. Run as root, once, like every
-- other file in this directory.
--
-- WHY A SECOND SCHEMA EXISTS AT ALL.
-- Spec section 15's "Verification" control asks for a restore "against an
-- isolated database or controlled maintenance window" - and P1-18 already
-- discovered, the hard way, that "isolated" cannot mean "redirect a
-- --databases dump elsewhere": the dump's own CREATE DATABASE/USE lines
-- override any --database= redirect and restore over the LIVE schema instead
-- (evidence/phase-1/p1-18-restore-log.txt, section 1). P1-18's isolated
-- rehearsal used this exact schema name, run manually as root.
--
-- P6-08 promotes that one-off manual rehearsal to an automated, committed
-- test (RestoreCommandTests) that actually measures RTO end to end. Running
-- that test against the LIVE `merchandising` schema - the one 500+ other
-- integration tests in this suite also depend on - would mean a restore that
-- fails partway (this system's own history: the LOCK TABLES gap once dropped
-- `auditlogs` live, see 0006_restore-grants.sql) corrupts every other test in
-- the run, not just this one. `merchandising_restoretest` exists so the
-- timed restore this card requires can DROP and CREATE real tables without
-- that blast radius.
--
-- WHY THIS DOES NOT REOPEN CLAUDE.md SECTION 6.1's "root used by no
-- application, ever". Root is not used by any application process here - it
-- creates a schema and grants a scoped privilege ONCE, as an environment-setup
-- step, exactly like every other file in db/grants/. RestoreCommandTests
-- itself runs entirely as merch_migrator, the real restore identity, so the
-- test proves that identity's grants are sufficient rather than sidestepping
-- the question with an over-privileged account (the gap 0006 records is
-- exactly what happens when that shortcut is taken).
--
-- WHAT merch_migrator GAINS. The same DDL/DML verbs it already holds on
-- `merchandising` (0001_accounts-and-grants.sql), plus LOCK TABLES (0006) -
-- scoped to `merchandising_restoretest` alone. It gains no privilege on any
-- other schema, and no GRANT OPTION.
--
-- Re-runnable: CREATE DATABASE IF NOT EXISTS and GRANT are both idempotent.
-- =============================================================================

CREATE DATABASE IF NOT EXISTS `merchandising_restoretest`
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

GRANT SELECT, INSERT, UPDATE, DELETE,
      CREATE, ALTER, DROP, INDEX, REFERENCES, LOCK TABLES
    ON `merchandising_restoretest`.*
    TO `merch_migrator`@`localhost`;

FLUSH PRIVILEGES;
