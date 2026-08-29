-- =============================================================================
-- 0011_receiving-grants.sql
--
-- Run as root, AFTER migration 0009_receiving.sql has created Receipts,
-- ReceiptLines, PurchaseReturns and PurchaseReturnLines. Same table-must-
-- exist-first ordering constraint as every prior migration -> grants pair
-- (ADR-013, ERROR 1146 if reversed).
--
-- Re-runnable: GRANT is idempotent.
--
-- Table names are lowercase because @@lower_case_table_names = 1 on Windows
-- (ADR-003.1): `Receipts` in the migration is stored as `receipts`.
--
-- -----------------------------------------------------------------------------
-- TWO OF THE FOUR ARE APPEND-ONLY; TWO ARE NOT - AND THAT SPLIT IS ARGUED, NOT
-- ASSUMED, ON THE ADR-013 PRINCIPLE THAT A NEW TABLE STARTS SELECT-ONLY UNTIL
-- A WRITE GRANT IS ARGUED FOR IT HERE.
--
-- receipts / receiptlines record something that HAPPENED - spec section 11's
-- "Goods received" row commits once, and no card in this phase (or any
-- later one) edits a receipt after it exists. INSERT only, the same shape
-- 0002 gives stockmovements and auditlogs, though these two are not ledgers
-- in that same append-only-forever sense: a receipt is a completed
-- transactional record, not a running balance, so it is not on the list
-- CLAUDE.md section 5 names as permanently excluded from ever gaining a
-- write-back grant - it simply has no write-back need today.
--
-- purchasereturns records something that is DECIDED - spec section 10.1's
-- "approval state" on a return request changes in place
-- (Requested -> Approved/Rejected), the same in-place-status shape 0010
-- argues for purchaseorders. UPDATE is granted for that transition alone.
--
-- purchasereturnlines, like receiptlines, is a fixed line item once written -
-- only its parent header's approval state changes, never the line itself.
-- INSERT only.
--
--   receipts             INSERT only - P4-05.
--   receiptlines          INSERT only - P4-05, P4-06.
--   purchasereturns       INSERT, UPDATE (Status, ApprovedByUserId,
--                         ApprovedAtUtc, RowVersion) - P4-08.
--   purchasereturnlines   INSERT only - P4-08.
-- -----------------------------------------------------------------------------
-- WHAT IS DELIBERATELY NOT GRANTED
--
-- DELETE, on any of the four, to anyone. Spec section 12: "Transactional
-- records are never physically deleted." A receipt and a purchase return are
-- both transactional records from the moment they exist, the same as a
-- purchase order (0010's identical argument).
--
-- UPDATE on receipts and receiptlines, and on purchasereturnlines. Nothing in
-- this phase's cards edits a receipt, a receipt line, or a return line after
-- it is written - if a later card needs one, that grant is argued in a new
-- numbered file, not added here by assumption.
-- =============================================================================

GRANT INSERT         ON `merchandising`.`receipts`            TO `merch_api`@`localhost`;
GRANT INSERT         ON `merchandising`.`receiptlines`         TO `merch_api`@`localhost`;
GRANT INSERT, UPDATE ON `merchandising`.`purchasereturns`      TO `merch_api`@`localhost`;
GRANT INSERT         ON `merchandising`.`purchasereturnlines`  TO `merch_api`@`localhost`;

FLUSH PRIVILEGES;

-- =============================================================================
-- VERIFY AFTER RUNNING. All four of these must fail with ERROR 1142:
--
--   mysql -u merch_api -p merchandising -e "DELETE FROM receipts;"
--   mysql -u merch_api -p merchandising -e "DELETE FROM receiptlines;"
--   mysql -u merch_api -p merchandising -e "UPDATE receiptlines SET Cost = 0;"
--   mysql -u merch_api -p merchandising -e "DELETE FROM purchasereturns;"
--
-- Asserted continuously by ReceivingSchemaTests, not only here.
-- =============================================================================
