' Merchandising.Tests.Unit.StoreTimeZoneBoundaryTests
'
' P6-01 Done when: "the store-local -> UTC day boundary is stated as an
' example with real timestamps, not as a sentence, and a test asserts a
' sale at 23:59:59 Asia/Manila lands in the day a cashier would expect and
' not the UTC one." docs/report-specification.md section 2 works through
' the exact numbers this file asserts, including the honest caveat that
' Asia/Manila's UTC+8 offset means 23:59:59 local does NOT itself land on a
' different UTC calendar date - what it proves is that the boundary's upper
' end is correctly INCLUSIVE of the last local second of the day. The real
' cross-midnight divergence (early store-local hours) is proven separately
' below and is what ReportReconciliationHarnessTests's off-by-one scenario
' uses.
'
' Pure Merchandising.Domain.StoreTimeZone - no database, no I/O.

Imports Merchandising.Domain
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public NotInheritable Class StoreTimeZoneBoundaryTests

    ' --- Done when: the boundary is an example with real timestamps ---

    <TestMethod>
    Public Sub StartAndEndOfDayUtc_MatchTheWorkedExample_ForStoreLocal20260830()

        Dim date20260830 As New DateOnly(2026, 8, 30)

        Assert.AreEqual(
            New DateTime(2026, 8, 29, 16, 0, 0, DateTimeKind.Utc),
            DateTime.SpecifyKind(StoreTimeZone.StartOfDayUtc(date20260830), DateTimeKind.Utc),
            "StartOfDayUtc(2026-08-30) no longer matches docs/report-specification.md section 2's worked example (2026-08-29 16:00:00 UTC).")

        Assert.AreEqual(
            New DateTime(2026, 8, 30, 16, 0, 0, DateTimeKind.Utc),
            DateTime.SpecifyKind(StoreTimeZone.EndOfDayUtcExclusive(date20260830), DateTimeKind.Utc),
            "EndOfDayUtcExclusive(2026-08-30) no longer matches docs/report-specification.md section 2's worked example (2026-08-30 16:00:00 UTC).")

    End Sub

    ' --- Done when: a sale at 23:59:59 Asia/Manila lands in the day a cashier would expect ---

    <TestMethod>
    Public Sub ASaleAt235959StoreLocal_LandsInsideItsOwnDaysWindow_NotTheNext()

        Dim localMoment As New DateTime(2026, 8, 30, 23, 59, 59, DateTimeKind.Unspecified)
        Dim utcMoment As DateTime = TimeZoneInfo.ConvertTimeToUtc(localMoment, StoreTimeZone.Zone)

        Assert.AreEqual(
            New DateTime(2026, 8, 30, 15, 59, 59), utcMoment,
            "2026-08-30 23:59:59 Asia/Manila no longer converts to 2026-08-30 15:59:59 UTC - StoreTimeZone.Zone's offset has changed.")

        Dim requestedDay As New DateOnly(2026, 8, 30)
        Dim windowStart As DateTime = StoreTimeZone.StartOfDayUtc(requestedDay)
        Dim windowEndExclusive As DateTime = StoreTimeZone.EndOfDayUtcExclusive(requestedDay)

        Assert.IsTrue(
            utcMoment >= windowStart AndAlso utcMoment < windowEndExclusive,
            $"A sale at 23:59:59 Asia/Manila on 2026-08-30 (UTC {utcMoment:O}) must fall inside that day's own window " &
            $"[{windowStart:O}, {windowEndExclusive:O}) - an off-by-one in EndOfDayUtcExclusive would push the last " &
            "second of a cashier's day into tomorrow's report instead.")

        ' It must NOT also satisfy the following day's window - a sale cannot
        ' be double-counted across two consecutive days' reports.
        Dim nextDay As DateOnly = requestedDay.AddDays(1)
        Dim nextWindowStart As DateTime = StoreTimeZone.StartOfDayUtc(nextDay)

        Assert.IsLessThan(nextWindowStart, utcMoment,
            "A sale at 23:59:59 Asia/Manila on 2026-08-30 must not also fall inside 2026-08-31's window.")

    End Sub

    ' --- The honest caveat: 23:59:59 local does not diverge from a naive UTC-date reading in this zone ---

    <TestMethod>
    Public Sub A235959LocalSale_HappensToShareItsUtcCalendarDate_UnlikeAnEarlyMorningOne()

        Dim lateNightLocal As New DateTime(2026, 8, 30, 23, 59, 59, DateTimeKind.Unspecified)
        Dim lateNightUtc As DateTime = TimeZoneInfo.ConvertTimeToUtc(lateNightLocal, StoreTimeZone.Zone)

        Assert.AreEqual(New DateOnly(2026, 8, 30), DateOnly.FromDateTime(lateNightUtc),
            "docs/report-specification.md section 2 states that a 23:59:59 Asia/Manila sale's UTC calendar date happens to equal its store-local date - a naive DATE(CreatedAtUtc) grouping would coincidentally be right here, which is exactly why this timestamp is NOT the divergence example.")

        Dim earlyMorningLocal As New DateTime(2026, 8, 30, 2, 0, 0, DateTimeKind.Unspecified)
        Dim earlyMorningUtc As DateTime = TimeZoneInfo.ConvertTimeToUtc(earlyMorningLocal, StoreTimeZone.Zone)

        Assert.AreNotEqual(New DateOnly(2026, 8, 30), DateOnly.FromDateTime(earlyMorningUtc),
            "An early store-local morning sale (02:00) must diverge from its UTC calendar date - if it no longer does, docs/report-specification.md section 2's divergence example is stale.")

        Assert.AreEqual(New DateOnly(2026, 8, 29), DateOnly.FromDateTime(earlyMorningUtc),
            "The 02:00 Asia/Manila sale's UTC calendar date must be 2026-08-29 - a naive DATE(CreatedAtUtc) grouping would silently misfile this store-local-2026-08-30 sale into 2026-08-29's report.")

    End Sub

End Class
