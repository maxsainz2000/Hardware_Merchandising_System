-- =============================================================================
-- 0004_maintenance.sql
--
-- P1-18: the maintenance lock. Spec section 12 lists MaintenanceLocks under
-- Configuration/operations with the note "maintenance mode prevents normal
-- writes"; spec section 15 steps 1, 2 and 7 define its lifecycle - a Super
-- Admin requests it with a reason, the API rejects ordinary writes while it
-- is held, and it is released ONLY after verification succeeds.
--
-- Applied by merch_migrator. After this file, run
-- db/grants/0005_maintenance-grants.sql as root - same table-must-exist-first
-- ordering constraint MariaDB 10.4 enforces with ERROR 1146 (ADR-013).
-- =============================================================================

-- --------------------------------------------------- MaintenanceLocks -----
-- NOT append-only, and deliberately so. A lock has a lifecycle - acquired,
-- then released - so the release is an UPDATE of the row that recorded the
-- acquisition, not a second row that a reader would have to correlate. This
-- follows the Sessions precedent from 0002 rather than the StockMovements
-- one: CLAUDE.md's append-only rule names the two ledgers specifically, and
-- this is operational state, not a ledger. The immutable trail lives in
-- AuditLogs, which records both the acquire and the release and cannot be
-- edited by anyone.
--
-- ONE ACTIVE LOCK AT A TIME, ENFORCED BY THE DATABASE.
-- MariaDB 10.4 has no partial or filtered unique index, so "unique among
-- unreleased rows only" cannot be expressed directly. The idiom that works
-- is a persistent generated column that is 1 while the lock is held and
-- NULL once released, with a plain UNIQUE index over it - NULLs do not
-- collide in a unique index, so released rows stack up freely while a second
-- concurrent acquisition is refused outright.
--
-- Measured on this server before being written here:
--     INSERT a second unreleased row -> ERROR 1062 (23000)
--         Duplicate entry '1' for key 'UQ_MaintenanceLocks_Active'
--     UPDATE the first to set ReleasedAtUtc, then INSERT again -> accepted
--
-- This matters more than it looks. Checking "is a lock already held?" in
-- application code before inserting is a read-then-write race: two Super
-- Admins clicking at the same moment both read "no lock" and both insert.
-- The same reasoning ADR-006 applies to stock balances applies here, and the
-- fix has the same shape - let the database refuse it.
--
-- PERSISTENT, not VIRTUAL: MariaDB indexes stored generated columns without
-- qualification, and this column is written once per row lifecycle at most.
CREATE TABLE MaintenanceLocks (
    Id                 INT NOT NULL AUTO_INCREMENT,
    Reason             VARCHAR(500) NOT NULL,
    RequestedByUserId  INT NOT NULL,
    AcquiredAtUtc      DATETIME(6) NOT NULL,
    ReleasedAtUtc      DATETIME(6) NULL,
    ReleasedByUserId   INT NULL,
    VerificationPassed TINYINT(1) NULL,
    CorrelationId      CHAR(36) NOT NULL,
    Detail             TEXT NULL,
    IsActive           TINYINT GENERATED ALWAYS AS
                           (CASE WHEN ReleasedAtUtc IS NULL THEN 1 ELSE NULL END) PERSISTENT,
    PRIMARY KEY (Id),
    UNIQUE KEY UQ_MaintenanceLocks_Active (IsActive),
    KEY IX_MaintenanceLocks_AcquiredAtUtc (AcquiredAtUtc),
    CONSTRAINT FK_MaintenanceLocks_RequestedBy FOREIGN KEY (RequestedByUserId) REFERENCES Users (Id),
    CONSTRAINT FK_MaintenanceLocks_ReleasedBy  FOREIGN KEY (ReleasedByUserId)  REFERENCES Users (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ------------------------------------------------- SystemSettings seed -----
-- The message connected clients display while the lock is held (spec
-- section 15 step 2: "displays a warning to connected clients"). Kept in
-- SystemSettings rather than hard-coded so the operator can say something
-- specific - "back at 10pm" beats a generic banner - without a rebuild.
--
-- INSERT IGNORE for the same reason as 0003: re-running must not reset a
-- value an operator has tuned.
INSERT IGNORE INTO SystemSettings (SettingKey, SettingValue, UpdatedAtUtc, UpdatedByUserId)
VALUES
    ('maintenance.clientMessage',
     'The system is under maintenance. Please save your work and close the application.',
     UTC_TIMESTAMP(6), NULL);
