' Merchandising.Tests.Integration.ReportReconciliationHarnessTests
'
' P6-01 Done when: "the harness is watched fail against a deliberately
' off-by-one date boundary and a deliberately current-cost join, before any
' real report uses it." No real report exists yet (Track B starts at
' P6-02), so both scenarios are built from synthetic totals computed by
' hand here - the same "prove the detector, not a live example" technique
' PaymentWordingTests.Denylist_FiresOnTheExactBadPhrase_AndPassesOnThe
' CorrectedOne (P5-12) already uses for its own denylist regex. No database
' needed - pure arithmetic and Merchandising.Domain.StoreTimeZone.
'
' THE BOUNDARY SCENARIO, WITH REAL NUMBERS (docs/report-specification.md
' section 2 explains why the divergent case is early-morning local time,
' not 23:59:59 - Asia/Manila is UTC+8 with no DST, so a store-local
' 23:59:59 timestamp converts to the SAME UTC calendar date, never a
' different one; the risk plan.md section 7 point 2 warns about
' ("a day boundary converted in the wrong direction moves a sale between
' reports") only bites for local times 00:00:00-07:59:59).
'
'   Two sales, both genuinely on store-local 2026-08-30:
'     Sale A: local 2026-08-30 02:00:00 -> UTC 2026-08-29 18:00:00, amount 150.0000
'     Sale B: local 2026-08-30 14:00:00 -> UTC 2026-08-30 06:00:00, amount 200.0000
'   Correct detail total (StoreTimeZone's own store-local boundary, the
'   existing detail endpoint's own filter): both included = 350.0000.
'   Deliberately wrong report total (grouping by the raw UTC calendar date
'   of CreatedAtUtc - MariaDB's naive `DATE(CreatedAtUtc) = '2026-08-30'`,
'   never converting to store-local first): Sale A's UTC date is 2026-08-29,
'   so it is silently dropped; only Sale B survives = 200.0000.
'   These must not reconcile.
'
' THE COST-BASIS SCENARIO: two units, captured unit cost 250.0000 at sale
' time (P5-07's SaleLines.Cost) versus the same two units re-priced at a
' current Products.Cost of 300.0000 after a later cost change - exactly the
' drift ADR-004.1/P5-07 exist to prevent, and exactly what "cost basis is
' captured, never current" (docs/report-specification.md section 5) rules
' out. Detail total (captured) = 500.0000; wrong report total (current) =
' 600.0000. These must not reconcile either.

Imports Merchandising.Domain
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public NotInheritable Class ReportReconciliationHarnessTests

    ' --- Done when: the harness passes when both sides genuinely agree ---

    <TestMethod>
    Public Sub Reconciles_IsTrue_WhenReportAndDetailTotalsAgree()

        Assert.IsTrue(ReportReconciliationHarness.Reconciles(350.0000D, 350.0000D))

    End Sub

    <TestMethod>
    Public Sub AssertReconciles_PassesSilently_WhenTotalsAgree()

        ReportReconciliationHarness.AssertReconciles(
            350.0000D, 350.0000D, "Daily sales summary", "Sales detail sum", "date=2026-08-30")

    End Sub

    ' --- Done when: watched fail - a deliberately off-by-one date boundary ---

    <TestMethod>
    Public Sub Reconciles_IsFalse_ForADeliberatelyOffByOneDateBoundary()

        Dim saleALocal As New DateTime(2026, 8, 30, 2, 0, 0, DateTimeKind.Unspecified)
        Dim saleBLocal As New DateTime(2026, 8, 30, 14, 0, 0, DateTimeKind.Unspecified)

        Dim saleAUtc As DateTime = TimeZoneInfo.ConvertTimeToUtc(saleALocal, StoreTimeZone.Zone)
        Dim saleBUtc As DateTime = TimeZoneInfo.ConvertTimeToUtc(saleBLocal, StoreTimeZone.Zone)

        ' Both sales genuinely happened on store-local 2026-08-30 - neither
        ' timestamp construction above is in question; what is under test is
        ' which report a naive UTC-date grouping would put each one in.
        Assert.AreEqual(New DateTime(2026, 8, 29, 18, 0, 0), saleAUtc,
            "Sale A's local-to-UTC conversion no longer matches this test's own worked example - the scenario needs correcting, not the assertion below.")
        Assert.AreEqual(New DateTime(2026, 8, 30, 6, 0, 0), saleBUtc,
            "Sale B's local-to-UTC conversion no longer matches this test's own worked example - the scenario needs correcting, not the assertion below.")

        Dim saleAAmount As Decimal = 150.0000D
        Dim saleBAmount As Decimal = 200.0000D

        ' Correct: the existing detail endpoint would filter by the
        ' STORE-LOCAL day (StoreTimeZone.StartOfDayUtc/EndOfDayUtcExclusive
        ' for 2026-08-30), which includes both UTC instants above.
        Dim detailTotal As Decimal = saleAAmount + saleBAmount

        ' Wrong: grouping by the raw UTC calendar date of each stored
        ' timestamp. Sale A's UTC date (2026-08-29) does not match the
        ' requested "2026-08-30" bucket and is silently dropped.
        Dim reportTotalUnderTheBug As Decimal = saleBAmount

        Assert.IsFalse(
            ReportReconciliationHarness.Reconciles(reportTotalUnderTheBug, detailTotal),
            "A naive UTC-calendar-date report total must NOT reconcile against the correct store-local detail total - if it does, the harness cannot catch the boundary bug plan.md section 7 warns about.")

    End Sub

    <TestMethod>
    Public Sub AssertReconciles_Throws_ForTheOffByOneDateBoundaryScenario()

        Assert.ThrowsExactly(Of AssertFailedException)(
            Sub()
                ReportReconciliationHarness.AssertReconciles(
                    200.0000D, 350.0000D, "Daily sales summary (buggy UTC-date grouping)", "Sales detail sum (store-local)", "date=2026-08-30")
            End Sub,
            "The harness's own assertion must fail loudly on the off-by-one boundary scenario, not pass silently.")

    End Sub

    ' --- Done when: watched fail - a deliberately current-cost join ---

    <TestMethod>
    Public Sub Reconciles_IsFalse_ForADeliberatelyCurrentCostJoin()

        Const quantity As Decimal = 2D
        Const capturedUnitCost As Decimal = 250.0000D
        Const currentUnitCost As Decimal = 300.0000D ' the product's cost AFTER a later change

        ' Correct: SaleLines.Cost, captured at sale time (P5-07) - what the
        ' existing sales detail would sum.
        Dim detailTotal As Decimal = quantity * capturedUnitCost

        ' Wrong: a report that joins Products.Cost instead of reading the
        ' captured line cost - exactly the drift docs/report-specification.md
        ' section 5 forbids.
        Dim reportTotalUnderTheBug As Decimal = quantity * currentUnitCost

        Assert.IsFalse(
            ReportReconciliationHarness.Reconciles(reportTotalUnderTheBug, detailTotal),
            "A report total computed from the CURRENT product cost must NOT reconcile against the captured-cost detail total - if it does, the harness cannot catch a Products.Cost join.")

    End Sub

    <TestMethod>
    Public Sub AssertReconciles_Throws_ForTheCurrentCostJoinScenario()

        Assert.ThrowsExactly(Of AssertFailedException)(
            Sub()
                ReportReconciliationHarness.AssertReconciles(
                    600.0000D, 500.0000D, "Sales by product (buggy current-cost join)", "Sales detail sum (captured cost)", "product=fixture, period=2026-08")
            End Sub,
            "The harness's own assertion must fail loudly on the current-cost-join scenario, not pass silently.")

    End Sub

End Class
