-- =============================================================================
-- 0002_authentication.sql
--
-- P1-08: login lockout state on Users, and a Sessions table for the opaque
-- server-side session token chosen over JWT bearer (ADR-005) - JWT would
-- have needed Microsoft.AspNetCore.Authentication.JwtBearer and
-- System.IdentityModel.Tokens.Jwt, neither pinned in docs/adr.md and neither
-- present in the installed Microsoft.AspNetCore.App shared framework
-- (confirmed by listing it directly, 2026-08-18). The opaque token needs no
-- new package at all: Microsoft.Extensions.Identity.Core (PasswordHasher)
-- IS already in that shared framework, reached from Merchandising.Infrastructure
-- via a plain FrameworkReference.
--
-- Applied by merch_migrator, same as 0001. After this file, run
-- db/grants/0003_authentication-grants.sql as root - same table-must-exist-
-- first ordering constraint as 0001 -> 0002 (grants).
-- =============================================================================

-- ----------------------------------------------------- Users (lockout) -----
-- Spec section 9: lock an account after five failed attempts within the
-- configured window (Merchandising.Domain.Security.AuthenticationPolicy).
-- FailedLoginAttempts resets to 0 on a successful login; LockedUntilUtc is
-- NULL unless the account is currently locked.
ALTER TABLE Users
    ADD COLUMN FailedLoginAttempts INT NOT NULL DEFAULT 0 AFTER PasswordHash,
    ADD COLUMN LockedUntilUtc DATETIME(6) NULL AFTER FailedLoginAttempts;

-- ---------------------------------------------------------- Sessions -----
-- The opaque token handed to the client is never stored - only its SHA-256
-- hash. Logout is a DELETE of the matching row, not a soft-revoke column,
-- which keeps merch_api's grant to INSERT+DELETE only (no UPDATE needed).
CREATE TABLE Sessions (
    Id           INT NOT NULL AUTO_INCREMENT,
    TokenHash    CHAR(64) NOT NULL,
    UserId       INT NOT NULL,
    CreatedAtUtc DATETIME(6) NOT NULL,
    ExpiresAtUtc DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    UNIQUE KEY UQ_Sessions_TokenHash (TokenHash),
    CONSTRAINT FK_Sessions_Users FOREIGN KEY (UserId) REFERENCES Users (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
