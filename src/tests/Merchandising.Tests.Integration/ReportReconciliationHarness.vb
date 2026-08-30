' Merchandising.Tests.Integration.ReportReconciliationHarness
'
' P6-01: plan.md section 7's key design call for this phase - "every report
' ships with a reconciliation test asserting the report total equals the
' sum from the corresponding detail screen for the same filter." This is
' the ONE place that assertion is written. Every Track B report test
' (P6-02 - P6-05) calls AssertReconciles with two totals it computed
' itself, from two genuinely different code paths - the report's own query
' on one side, and a client-side sum over the EXISTING detail endpoint's
' Items on the other (PurchaseOrderHistoryResponse.Items, StockMovement
' SearchResponse.Items, and whatever Track B's sales-side cards add). This
' class never calls either side itself - doing so would make it the same
' single code path ADR-023 forbids reusing, just moved one file over.
'
' Decimal equality, no tolerance - ADR-021's LedgerReconciliation makes the
' identical argument for the ledger; a report total is exactly as
' comparable, because every value on both sides is already DECIMAL(19,4)
' or DECIMAL(19,3) (ADR-004) with no floating-point arithmetic anywhere
' between storage and this comparison.
'
' Reconciles() is split out from AssertReconciles() the same way
' PaymentWordingTests.DenylistPattern is separated from the scan test that
' uses it (P5-12) - a pure predicate a "watched fail" test can call
' directly with synthetic totals, proven in ReportReconciliationHarnessTests
' before any real report (P6-02 onward) depends on it.

Imports Microsoft.VisualStudio.TestTools.UnitTesting

Public NotInheritable Class ReportReconciliationHarness

    Private Sub New()
    End Sub

    ''' <summary>
    ''' True when the report-computed total and the detail-endpoint-derived
    ''' total agree exactly. Exact <c>Decimal</c> equality - never a
    ''' tolerance (ADR-021's identical rule for the ledger).
    ''' </summary>
    Public Shared Function Reconciles(reportTotal As Decimal, detailTotal As Decimal) As Boolean
        Return reportTotal = detailTotal
    End Function

    ''' <summary>
    ''' Asserts <paramref name="reportTotal"/> and <paramref name="detailTotal"/>
    ''' agree, naming both values, both labels, and the filter under test in
    ''' the failure message - a report that disagrees with the detail screen
    ''' is, per plan.md section 7, "the single most damaging kind of defect
    ''' in a merchandising system," so the assertion failure has to be
    ''' immediately actionable, not a bare "Assert.AreEqual failed."
    ''' </summary>
    Public Shared Sub AssertReconciles(
        reportTotal As Decimal, detailTotal As Decimal,
        reportLabel As String, detailLabel As String, filterDescription As String)

        Assert.IsTrue(
            Reconciles(reportTotal, detailTotal),
            $"{reportLabel} ({reportTotal}) does not reconcile against {detailLabel} ({detailTotal}) " &
            $"for filter [{filterDescription}]. A report that disagrees with its own detail screen is " &
            "the defect this phase's reconciliation harness exists to catch (plan.md section 7).")

    End Sub

End Class
