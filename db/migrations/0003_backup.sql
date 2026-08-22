-- =============================================================================
-- 0003_backup.sql
--
-- P1-17: the BackupLogs ledger. Spec section 12 lists this table under
-- Configuration/operations and spec section 15's "Integrity" control names
-- exactly what a row must carry: file size, checksum, timestamp, source
-- database version, and result.
--
-- APPEND-ONLY, and by the ADR-013 default rather than by a rule written
-- here. merch_backup holds no write privilege at database level, so this
-- table is append-only from the moment it exists;
-- db/grants/0004_backup-grants.sql adds INSERT and nothing else. There is
-- deliberately no UPDATE path: a backup run that fails does not later get
-- edited into a success, it gets a second row.
--
-- Applied by merch_migrator, same as 0001 and 0002. After this file, run
-- db/grants/0004_backup-grants.sql as root - same table-must-exist-first
-- ordering constraint MariaDB 10.4 enforces with ERROR 1146 (ADR-013).
-- =============================================================================

-- --------------------------------------------------------- BackupLogs -----
-- One row per backup ATTEMPT, not per success. A row exists whether the run
-- worked, half-worked, or failed outright, because the failure case is the
-- one this table is really for: spec section 15 requires a failed run to
-- record error details and raise an operational warning rather than pass
-- silently.
--
-- Result is a small closed vocabulary rather than a boolean, because
-- "the dump worked but the off-host copy did not" is a real and likely
-- outcome (the USB stick is not plugged in) and is neither a success nor a
-- failure. Values written by BackupCommand:
--   'Succeeded'  dump written, checksum verified, off-host copy made
--   'Partial'    dump written and verified, off-host copy NOT made
--   'Failed'     no usable dump was produced
--
-- SizeBytes/Sha256/OffHostPath are NULL on a failed run because there is no
-- file to describe. They are NOT NULL-by-default columns with a zero value,
-- which would be indistinguishable from a real zero-byte dump.
CREATE TABLE BackupLogs (
    Id                INT NOT NULL AUTO_INCREMENT,
    StartedAtUtc      DATETIME(6) NOT NULL,
    CompletedAtUtc    DATETIME(6) NULL,
    Result            VARCHAR(50) NOT NULL,
    FilePath          VARCHAR(500) NULL,
    SizeBytes         BIGINT NULL,
    Sha256            CHAR(64) NULL,
    OffHostPath       VARCHAR(500) NULL,
    SourceDbVersion   VARCHAR(100) NULL,
    RetentionCount    INT NULL,
    PrunedFileCount   INT NOT NULL DEFAULT 0,
    CorrelationId     CHAR(36) NOT NULL,
    Detail            TEXT NULL,
    PRIMARY KEY (Id),
    KEY IX_BackupLogs_StartedAtUtc (StartedAtUtc),
    KEY IX_BackupLogs_Result (Result)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ------------------------------------------------- SystemSettings seed -----
-- Spec section 15's "Retention" control: a configurable count, with the
-- configured value recorded in SystemSettings. Seeded here rather than
-- defaulted in code so that the operator can see and change it in one known
-- place, and so a fresh install has a safe value before the first run.
--
-- backup.offHostVolumeLabel is a LABEL, never a drive letter. A USB stick
-- mounts as D: on one machine and F: on the next, and the demo runs on three
-- machines nobody has surveyed (ADR-012, ADR-015). Matching on the label is
-- what makes the same configuration correct everywhere.
--
-- INSERT IGNORE, not INSERT: re-running this migration on a database where
-- an operator has already tuned these values must not silently reset them.
INSERT IGNORE INTO SystemSettings (SettingKey, SettingValue, UpdatedAtUtc, UpdatedByUserId)
VALUES
    ('backup.retentionCount',      '7',            UTC_TIMESTAMP(6), NULL),
    ('backup.offHostVolumeLabel',  'MERCHBACKUP',  UTC_TIMESTAMP(6), NULL),
    ('backup.directory',           'C:\\MerchandisingBackups', UTC_TIMESTAMP(6), NULL);
