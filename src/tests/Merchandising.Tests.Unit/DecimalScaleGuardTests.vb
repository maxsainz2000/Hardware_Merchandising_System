Imports Merchandising.Domain
Imports Microsoft.VisualStudio.TestTools.UnitTesting

''' <summary>
''' P1-07 / ADR-004.1 evidence: over-scale money and quantity values are a
''' validation failure at the API boundary, not a value the database is
''' trusted to round (ADR-003.2's <c>Note 1265</c> stays silent even under
''' <c>STRICT_TRANS_TABLES</c>).
''' </summary>
<TestClass>
Public Class DecimalScaleGuardTests

    ''' <summary>Done-when: money with more than 4 decimal places is rejected.</summary>
    <TestMethod>
    Public Sub EnsureMoneyScale_RejectsOverScaleValue()

        Assert.ThrowsExactly(Of ArgumentException)(
            Function() DecimalScaleGuard.EnsureMoneyScale(1.99999D))

    End Sub

    ''' <summary>Done-when: quantity with more than 3 decimal places is rejected.</summary>
    <TestMethod>
    Public Sub EnsureQuantityScale_RejectsOverScaleValue()

        Assert.ThrowsExactly(Of ArgumentException)(
            Function() DecimalScaleGuard.EnsureQuantityScale(1.9999D))

    End Sub

    <TestMethod>
    Public Sub EnsureMoneyScale_AcceptsValueAtOrBelowScale()

        Assert.AreEqual(1.9999D, DecimalScaleGuard.EnsureMoneyScale(1.9999D))
        Assert.AreEqual(2D, DecimalScaleGuard.EnsureMoneyScale(2D))

    End Sub

    <TestMethod>
    Public Sub EnsureQuantityScale_AcceptsValueAtOrBelowScale()

        Assert.AreEqual(0.001D, DecimalScaleGuard.EnsureQuantityScale(0.001D))

    End Sub

    ''' <summary>
    ''' The API-computed side of ADR-004.1: half-up, not .NET's default
    ''' banker's rounding, matching how a cashier would check a receipt.
    ''' </summary>
    <TestMethod>
    Public Sub RoundMoney_RoundsHalfAwayFromZero()

        Assert.AreEqual(1.2346D, DecimalScaleGuard.RoundMoney(1.23455D))
        Assert.AreEqual(1.2345D, DecimalScaleGuard.RoundMoney(1.23454D))

    End Sub

    <TestMethod>
    Public Sub RoundQuantity_RoundsHalfAwayFromZero()

        Assert.AreEqual(2D, DecimalScaleGuard.RoundQuantity(1.9995D))

    End Sub

    ''' <summary>
    ''' P1-07 acceptance box: the two boundary values from the P0-07
    ''' pre-check round-trip exactly at their declared storage scale.
    ''' </summary>
    <TestMethod>
    Public Sub BoundaryValues_RoundTripExactlyAtStorageScale()

        Assert.AreEqual(0.001D, DecimalScaleGuard.EnsureQuantityScale(0.001D))
        Assert.AreEqual(12345678901234.5678D, DecimalScaleGuard.EnsureMoneyScale(12345678901234.5678D))

    End Sub

End Class
