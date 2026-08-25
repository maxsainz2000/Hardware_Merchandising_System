-- =============================================================================
-- 0005_identity.sql
--
-- P2-01: Phase 2's identity-model completion. Spec section 12 lists
-- `PermissionPolicies` under Identity/security alongside `Users`, `Roles`,
-- `UserRoles`, `AuditLogs` - the first four were built at P1-07/P1-08 for a
-- one-POC-user slice; this is the one piece that slice never needed.
--
-- What is deliberately NOT here: Roles already carries all five spec
-- section 9 roles (SuperAdmin, Admin, ProcurementOfficer, InventoryClerk,
-- Cashier) with a stable INT PK and a unique Name, seeded at 0001. UserRoles
-- is already a composite-PK (UserId, RoleId) join table, so a user holding
-- more than one role needs no second membership table. Neither needed new
-- schema for the five-role model - only PermissionPolicies did.
--
-- What is deliberately NOT here either: PermissionPolicies is created empty.
-- P2-02 (Decides: ADR-017) is where the policy-naming scheme and the
-- code-side registration (one named ASP.NET Core policy per spec section 9
-- role x operation cell) get decided. Seeding rows here would mean guessing
-- that scheme a card early.
--
-- Applied by merch_migrator, same as 0001-0004. After this file, run
-- db/grants/0007_identity-grants.sql as root - same table-must-exist-first
-- ordering constraint as every prior migration -> grants pair (ADR-013,
-- ERROR 1146 if reversed).
-- =============================================================================

-- --------------------------------------------------- PermissionPolicies -----
-- A catalog table, not a ledger: PolicyName is the stable identifier the
-- authorization layer keys on once P2-02 registers policies. No rows are
-- seeded by this migration - see the note above.
CREATE TABLE PermissionPolicies (
    Id           INT NOT NULL AUTO_INCREMENT,
    PolicyName   VARCHAR(100) NOT NULL,
    Description  VARCHAR(500) NOT NULL,
    CreatedAtUtc DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    UNIQUE KEY UQ_PermissionPolicies_PolicyName (PolicyName)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
