-- P5-08: one-time compensating correction for ledger drift created by a
-- DEFECT IN THE TEST FIXTURE ITSELF, caught and fixed during this card's own
-- development - not a defect in StockRepository, SaleService, ReceivingService,
-- or AdjustmentService.
--
-- Root cause: SaleConcurrencyTests.ResetBalanceAsync's first draft reset a
-- fixture product's balance with a bare
--   UPDATE StockBalances SET Quantity = @quantity WHERE ProductId = @productId
-- after computing the delta and writing the matching StockMovements row.
-- Box 1's fixture product (p5_08_fixture_sku_227edbd6940b4cad, ProductId
-- 5718) is created by CreateFixtureProductAsync WITHOUT a StockBalances row -
-- exactly the P4-15 section 2.1(a) fact: a product that has never held a
-- balance has NO row at all (ProductRepository.InsertAsync does not seed
-- one). The bare UPDATE against that missing row affected ZERO rows,
-- silently, while the StockMovements row it wrote alongside (+1.000, before
-- 0.000, after 1.000) was committed regardless - the ledger recorded a
-- balance change that never reached StockBalances. The two concurrent sale
-- attempts that followed then both saw no balance (or an unmet guard) and
-- both correctly returned InsufficientStock, which is what surfaced the bug
-- (Sale_ConcurrentSameProduct_LastUnitExactlyOneSucceeds asserted
-- successCount = 1 and got 0).
--
-- Fixed by rewriting ResetBalanceAsync to go through
-- StockRepository.IncrementAsync (an upsert - creates the row if missing)
-- for a positive delta and StockRepository.TryDecrementAsync for a negative
-- one, the same conditional-write discipline every real command in this
-- codebase already follows. No application code changed - this was confined
-- to the new test file, never committed in its broken form.
--
-- The broken run's write is still a real, permanent row: StockMovements is
-- append-only, so the +1.000 movement it wrote stays in the ledger while
-- StockBalances for that product was never created. Confirmed via
-- LedgerReconciliation's own query before this correction:
--
--   ProductId=5718  Expected=1.000  Actual=0.000  Delta=-1.000
--
-- (StockBalances has no row at all for 5718 - COALESCE(b.Quantity, 0.000)
-- reports 0.000 for a missing row exactly as it would for an explicit zero,
-- and this correction does not create one: nothing in this codebase ever
-- writes StockBalances for a product currently holding zero without a
-- balance-changing command actually happening, and none did here.)
--
-- This is a correction, NEVER an edit or delete of any existing
-- StockMovements row (CLAUDE.md section 5, stop condition 7). Run once, as
-- merch_migrator (CLAUDE.md section 6.1), outside every application code
-- path - the same shape p4-01-drift-correction.sql and
-- p4-15-drift-correction.sql used for the same class of problem.

INSERT INTO StockMovements
    (ProductId, Delta, QuantityBefore, QuantityAfter, Reason, ActorUserId, CorrelationId, CreatedAtUtc)
SELECT 5718, -1.000, 1.000, 0.000,
       'P5-08 one-time ledger correction: drift induced by a defect in SaleConcurrencyTests.ResetBalanceAsync (bare UPDATE against a non-existent StockBalances row), fixed before this card was committed',
       u.Id, UUID(), UTC_TIMESTAMP(6)
FROM (SELECT Id FROM Users WHERE Username = 'admin' LIMIT 1) AS u;
