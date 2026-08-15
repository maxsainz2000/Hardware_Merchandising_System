# Professor Approvals and Course Constraints

**Purpose.** A citable record of every constraint and exception decided by the course instructor. Referenced at acceptance (spec §25) and by ADR-000.

An approval you cannot point to is an approval you do not have. Attach the original message, email, or written note for each entry.

---

## PA-001 · Manually authored Visual Basic ASP.NET Core API

**Status:** ✅ APPROVED
**Date confirmed:** _(record)_
**Asked:** Is a manually authored Visual Basic ASP.NET Core Web API project acceptable, given that Visual Studio provides no Visual Basic template for this project type?

**Answer:** Yes — acceptable.

**Consequence.** The API is hand-authored per ADR-001. All application source remains Visual Basic. **No C# exception was granted**, so fallback rung C in `plan.md` §1.1 is effectively closed.

**Evidence:** ⬜ _attach the original message/email — screenshot to `evidence/phase-0/pa-001-approval.png`_

---

## PA-002 · XAMPP is mandatory

**Status:** ✅ CONFIRMED BINDING
**Date confirmed:** _(record)_
**Asked:** Is XAMPP genuinely required, or merely permitted? Could a standalone MariaDB installation be substituted?

**Answer:** XAMPP is truly mandatory.

**Consequence.**

- MariaDB is used only as supplied by XAMPP. No standalone MariaDB, MySQL, or other database may be substituted — including for testing.
- Apache Friends documents XAMPP as intended for development environments, not production. The system is therefore classified as an **academic prototype** throughout all documentation and in the final presentation. This is not defensive hedging; it is the accurate description of the deliverable.
- Hardening is required rather than optional: unused XAMPP components disabled, MariaDB bound to loopback, least-privilege accounts, phpMyAdmin not exposed to the store LAN, and never exposed to the public internet.

**Evidence:** ⬜ _attach the original message/email — screenshot to `evidence/phase-0/pa-002-approval.png`_

---

## PA-003 · Numeric precision confirmation

**Status:** ⬜ PENDING — raise at task P1-07
**To ask:** Is `DECIMAL(19,4)` for money and `DECIMAL(19,3)` for quantities acceptable for a hardware store, given that some goods sell in fractional units (metres of cable, kilos of nails)?

**Answer:** _(record)_

---

## PA-004 · Performance targets

**Status:** ⬜ PENDING — raise before Phase 7 load testing
**To ask:** Are the provisional targets acceptable — 5–10 concurrent sessions, p95 ≤ 2s for ordinary reads, p95 ≤ 4s for stock-changing commands?

**Answer:** _(record)_

**Note.** The spec marks these as provisional and subject to adjustment. Confirm before the load test so you are measuring against an agreed bar rather than negotiating one afterwards.

---

## PA-005 · Recovery targets

**Status:** ⬜ PENDING — raise before Phase 6 backup work
**To ask:** Are an RPO of ≤ 24 hours (daily backup) and an RTO of ≤ 120 minutes acceptable for the prototype?

**Answer:** _(record)_

---

## Template for new entries

```markdown
## PA-NNN · Short title

**Status:** ⬜ PENDING | ✅ APPROVED | ❌ DECLINED
**Date confirmed:** YYYY-MM-DD
**Asked:** the exact question put to the instructor.

**Answer:** what they said.

**Consequence.** What changes in the project as a result.

**Evidence:** path under evidence/.
```
