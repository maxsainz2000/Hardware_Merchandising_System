-- =============================================================================
-- 0012_counts-and-adjustments-grants.sql
--
-- Run as root, AFTER migration 0010_counts-and-adjustments.sql has created
-- StockCounts, StockCountLines and StockAdjustments. Same table-must-exist-
-- first ordering constraint as every prior migration -> grants pair
-- (ADR-013, ERROR 1146 if reversed).
--
-- Re-runnable: GRANT is idempotent.
--
-- Table names are lowercase because @@lower_case_table_names = 1 on Windows
-- (ADR-003.1): `StockCounts` in the migration is stored as `stockcounts`.
--
-- -----------------------------------------------------------------------------
-- TWO OF THE THREE ARE MUTABLE; ONE IS NOT - THE SAME ADR-013 PRINCIPLE THAT A
-- NEW TABLE STARTS SELECT-ONLY UNTIL A WRITE GRANT IS ARGUED FOR IT HERE.
--
-- stockcounts changes in place - Status moves Open -> Closed -> Approved/
-- Rejected, with ApprovedByUserId and ApprovedAtUtc set on the same row, the
-- identical in-place-status shape 0010 (P3-02) argues for purchaseorders and
-- 0011 (P4-02) argues for purchasereturns. INSERT, UPDATE.
--
-- stockcountlines records what was counted, at the moment it was counted -
-- P4-03's own migration header: "the whole point of a count is what was true
-- then." Nothing in this phase (or any later one named so far) edits a count
-- line after it is written; a closed count's lines stay exactly as counted.
-- INSERT only, the same shape 0011 gives receiptlines.
--
-- stockadjustments changes in place - Status moves Pending -> Approved/
-- Rejected -> Applied (or Pending -> Applied directly, below threshold), the
-- same in-place-status shape as stockcounts. INSERT, UPDATE.
--
--   stockcounts        INSERT, UPDATE (Status, ApprovedByUserId,
--                       ApprovedAtUtc, RowVersion) - P4-09.
--   stockcountlines     INSERT only - P4-09.
--   stockadjustments    INSERT, UPDATE (Status, ApprovedByUserId,
--                       RowVersion) - P4-10.
-- -----------------------------------------------------------------------------
-- WHAT IS DELIBERATELY NOT GRANTED
--
-- DELETE, on any of the three, to anyone. Spec section 12: "Transactional
-- records are never physically deleted." A stock count and an adjustment are
-- both transactional records from the moment they exist, the same as a
-- purchase order or a purchase return (0010's/0011's identical argument).
--
-- UPDATE on stockcountlines. Nothing in this phase's cards edits a count
-- line after it is written - if a later card needs one, that grant is
-- argued in a new numbered file, not added here by assumption.
-- =============================================================================

GRANT INSERT, UPDATE ON `merchandising`.`stockcounts`      TO `merch_api`@`localhost`;
GRANT INSERT         ON `merchandising`.`stockcountlines`  TO `merch_api`@`localhost`;
GRANT INSERT, UPDATE ON `merchandising`.`stockadjustments` TO `merch_api`@`localhost`;

FLUSH PRIVILEGES;

-- =============================================================================
-- VERIFY AFTER RUNNING. All of these must fail with ERROR 1142:
--
--   mysql -u merch_api -p merchandising -e "DELETE FROM stockcounts;"
--   mysql -u merch_api -p merchandising -e "DELETE FROM stockcountlines;"
--   mysql -u merch_api -p merchandising -e "UPDATE stockcountlines SET Variance = 0;"
--   mysql -u merch_api -p merchandising -e "DELETE FROM stockadjustments;"
--
-- Asserted continuously by StockCountSchemaTests, not only here.
-- =============================================================================
