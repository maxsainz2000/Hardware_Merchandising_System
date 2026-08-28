-- P4-01: one-time compensating correction for ledger drift found by
-- LedgerReconciliation before any Phase 4 writer touched the ledger.
--
-- Root cause: StockDecrementTests, ProductLifecycleTests, and
-- AuthorizationMatrixTests each reuse one fixed fixture product across
-- every integration-test run ever executed, and their setup helpers reset
-- StockBalances.Quantity directly to a known baseline before each run.
-- StockMovements is append-only, so every historical decrement from every
-- past run stayed in the ledger, while the balance was repeatedly repinned
-- to the baseline - the two silently diverged since Phase 1/2. The helpers
-- themselves are fixed in this same commit (see LedgerReconciliationTests.vb
-- comment block); this script closes the gap that already existed before
-- that fix, exactly once. It is a correction, never an edit or delete of
-- any existing StockMovements row (CLAUDE.md section 5).
--
-- Each Delta = current StockBalances.Quantity - current SUM(StockMovements)
-- for that product, i.e. exactly what LedgerReconciliation.FindDiscrepanciesAsync
-- reported as "Delta" for these four products before this script ran -
-- see evidence/phase-4/p4-01-ledger-reconciliation.txt. QuantityBefore/After
-- record the ledger's own before/after sum, matching what every other
-- StockMovements row in this table already means.
--
-- Run once, as merch_migrator (schema owner - CLAUDE.md section 6.1), not
-- part of any application code path. ActorUserId is the permanent seeded
-- 'admin' account (Id 12), not a test-fixture user, because this is a real
-- one-time administrative correction, not test setup.

INSERT INTO StockMovements (ProductId, Delta, QuantityBefore, QuantityAfter, Reason, ActorUserId, CorrelationId, CreatedAtUtc) VALUES
    (2,  2010.000, -1914.000,   96.000, 'P4-01 one-time ledger correction: pre-existing test-fixture reset drift closed', 12, '881b2f63-e248-44cf-82b0-6c0ecc90d68c', UTC_TIMESTAMP(6)),
    (3,   156.000,  -156.000,    0.000, 'P4-01 one-time ledger correction: pre-existing test-fixture reset drift closed', 12, '4dff1572-b5b0-4d5d-8669-490d2a361045', UTC_TIMESTAMP(6)),
    (5,   231.000,  -132.000,   99.000, 'P4-01 one-time ledger correction: pre-existing test-fixture reset drift closed', 12, 'ede62b03-3002-4009-b81b-65d0011ea09d', UTC_TIMESTAMP(6)),
    (70,  151.000,  -132.000,   19.000, 'P4-01 one-time ledger correction: pre-existing test-fixture reset drift closed', 12, '90f017d7-0c63-41c8-9510-c071880f9f5d', UTC_TIMESTAMP(6));
