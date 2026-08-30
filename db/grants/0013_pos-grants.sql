-- =============================================================================
-- 0013_pos-grants.sql
--
-- Run as root, AFTER migration 0011_pos.sql has created CashierSessions,
-- Sales, SaleLines and SalePayments. Same table-must-exist-first ordering
-- constraint as every prior migration -> grants pair (ADR-013, ERROR 1146
-- if reversed).
--
-- Re-runnable: GRANT is idempotent.
--
-- Table names are lowercase because @@lower_case_table_names = 1 on Windows
-- (ADR-003.1): `CashierSessions` in the migration is stored as `cashiersessions`.
--
-- -----------------------------------------------------------------------------
-- ONE OF THE FOUR IS MUTABLE; THREE ARE NOT - AND THAT SPLIT IS ARGUED, NOT
-- ASSUMED, ON THE ADR-013 PRINCIPLE THAT A NEW TABLE STARTS SELECT-ONLY UNTIL
-- A WRITE GRANT IS ARGUED FOR IT HERE.
--
-- cashiersessions records something that is DECIDED and then REVISED - spec
-- section 10.3's session opens, then closes with declared/calculated cash
-- and variance filled in (P5-04, P5-05), the same in-place-status shape
-- 0008/0009/0010 argue for purchaseorders/purchasereturns/stockcounts/
-- stockadjustments. UPDATE is granted for that transition.
--
-- sales, salelines and salepayments record something that HAPPENED and is
-- never revisited. Spec section 10.3: "Completed sales are never edited or
-- deleted." No card in this phase (or the one card that names this file
-- again, P5-10) edits a sale, a sale line, or a payment record after it is
-- written - P5-10's job is to *prove* that stays true by database grant, not
-- to widen it. INSERT only, the same shape 0011 gives receipts/receiptlines/
-- purchasereturnlines.
--
--   cashiersessions   INSERT, UPDATE (ClosedByUserId, ClosedAtUtc, Status,
--                     DeclaredCash, CalculatedCash, CashVariance, RowVersion,
--                     UpdatedAtUtc) - P5-04, P5-05.
--   sales             INSERT only - P5-07.
--   salelines         INSERT only - P5-07.
--   salepayments      INSERT only - P5-07.
-- -----------------------------------------------------------------------------
-- WHAT IS DELIBERATELY NOT GRANTED
--
-- DELETE, on any of the four, to anyone. Spec section 12: "Transactional
-- records are never physically deleted." A cashier session, a sale, and
-- everything under it are transactional records from the moment they exist,
-- the same as a purchase order (0010's identical argument).
--
-- UPDATE on sales, salelines and salepayments. Nothing in this phase's cards
-- edits a sale, a sale line, or a payment record after it is written - if a
-- later card needs one, that grant is argued in a new numbered file, not
-- added here by assumption, the same rule 0011's header states for receipts.
-- =============================================================================

GRANT INSERT, UPDATE ON `merchandising`.`cashiersessions` TO `merch_api`@`localhost`;
GRANT INSERT          ON `merchandising`.`sales`          TO `merch_api`@`localhost`;
GRANT INSERT          ON `merchandising`.`salelines`      TO `merch_api`@`localhost`;
GRANT INSERT          ON `merchandising`.`salepayments`   TO `merch_api`@`localhost`;

FLUSH PRIVILEGES;

-- =============================================================================
-- VERIFY AFTER RUNNING. All of these must fail with ERROR 1142:
--
--   mysql -u merch_api -p merchandising -e "DELETE FROM cashiersessions;"
--   mysql -u merch_api -p merchandising -e "DELETE FROM sales;"
--   mysql -u merch_api -p merchandising -e "UPDATE sales SET Total = 0;"
--   mysql -u merch_api -p merchandising -e "UPDATE salelines SET Cost = 0;"
--   mysql -u merch_api -p merchandising -e "UPDATE salepayments SET Amount = 0;"
--
-- Asserted continuously by PosSchemaTests, not only here.
-- =============================================================================
