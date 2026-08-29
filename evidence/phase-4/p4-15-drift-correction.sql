-- P4-15: one-time compensating correction for ledger drift created
-- DELIBERATELY, by the falsification run that proved
-- ReceiveAndAdjustConcurrencyTests can fail.
--
-- Root cause, and it is not a defect in the system: to prove the two new
-- tests are checks rather than decoration (tasks.md's own Phase 4 rule,
-- line 20), StockRepository was temporarily broken in two ways and the
-- tests were run against it -
--
--   1. IncrementAsync rewritten from its single-statement
--      INSERT ... ON DUPLICATE KEY UPDATE upsert into a read-then-write
--      (SELECT, then UPDATE SET Quantity = <computed>), reintroducing the
--      lost update ADR-006 exists to make impossible.
--   2. TryDecrementAsync's "AND Quantity >= @qty" guard removed, so a
--      negative adjustment could take a balance below zero.
--
-- Both tests failed, for exactly the right reasons (see
-- p4-15-concurrent-receive-and-adjust.txt section 3). StockRepository was
-- then reverted with `git checkout --`, verified byte-identical to
-- 31e8255. But the five fixture products those broken runs wrote to are
-- permanent rows: StockMovements is append-only, so the movements the
-- broken code wrote stayed, while the balances it computed wrongly stayed
-- too. SUM(StockMovements) <> StockBalances for five products, and P4-01's
-- <AssemblyCleanup> reconciliation correctly fails the whole integration
-- suite until that is closed.
--
-- Each Delta below = StockBalances.Quantity - SUM(StockMovements) for that
-- product, i.e. exactly the "Delta" LedgerReconciliation.FindDiscrepanciesAsync
-- reported before this script ran. QuantityBefore is the ledger's own sum
-- before the correction and QuantityAfter is the stored balance, the same
-- meaning every other StockMovements row carries and the same shape
-- p4-01-drift-correction.sql used for the same class of problem.
--
-- This is a correction, NEVER an edit or delete of any existing
-- StockMovements row (CLAUDE.md section 5). Run once, as merch_migrator
-- (CLAUDE.md section 6.1), outside every application code path.
--
--   ProductId=3177  Expected=136.000  Actual=106.000  Delta=-30.000
--   ProductId=3184  Expected=136.000  Actual=106.000  Delta=-30.000
--   ProductId=3185  Expected=  2.000  Actual=  7.000  Delta= +5.000
--   ProductId=3186  Expected=136.000  Actual=106.000  Delta=-30.000
--   ProductId=3187  Expected=  2.000  Actual=  7.000  Delta= +5.000

INSERT INTO StockMovements
    (ProductId, Delta, QuantityBefore, QuantityAfter, Reason, ActorUserId, CorrelationId, CreatedAtUtc)
SELECT d.ProductId, d.Delta, d.QuantityBefore, d.QuantityAfter,
       'P4-15 one-time ledger correction: drift induced by the falsification run that proved ReceiveAndAdjustConcurrencyTests can fail',
       u.Id, UUID(), UTC_TIMESTAMP(6)
FROM (
        SELECT 3177 AS ProductId, -30.000 AS Delta, 136.000 AS QuantityBefore, 106.000 AS QuantityAfter
  UNION ALL SELECT 3184, -30.000, 136.000, 106.000
  UNION ALL SELECT 3185,   5.000,   2.000,   7.000
  UNION ALL SELECT 3186, -30.000, 136.000, 106.000
  UNION ALL SELECT 3187,   5.000,   2.000,   7.000
     ) AS d
CROSS JOIN (SELECT Id FROM Users WHERE Username = 'admin' LIMIT 1) AS u;
