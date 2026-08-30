' Merchandising.Tests.Unit.SaleArithmeticTests
'
' P5-01 / ADR-022: fixed-precision sale arithmetic in Domain, tested
' exhaustively with no database at all. Every "Done when" box on the P5-01
' card maps to one or more tests below.

Imports System.IO
Imports System.Text.RegularExpressions
Imports Merchandising.Domain
Imports Merchandising.Domain.Configuration
Imports Merchandising.Domain.Entities
Imports Merchandising.Domain.Sales
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class SaleArithmeticTests

    ''' <summary>Same repository-root marker RolePermissionMatrixDocumentationTests / WindowsServiceInfoTests use.</summary>
    Private Shared Function FindRepositoryRoot() As DirectoryInfo

        Dim current As DirectoryInfo = New DirectoryInfo(AppContext.BaseDirectory)

        While current IsNot Nothing
            If File.Exists(Path.Combine(current.FullName, "CLAUDE.md")) Then
                Return current
            End If
            current = current.Parent
        End While

        Assert.Fail(
            "Could not locate the repository root above '" & AppContext.BaseDirectory &
            "'. This test scans src/Merchandising.Domain/Sales from the working tree.")
        Return Nothing

    End Function

    ' --- Done when: every value is Decimal; no Double or Single anywhere, by source scan ---

    <TestMethod>
    Public Sub SalesSourceFiles_ContainNoDoubleOrSingleTokens()

        Dim salesDirectory As String =
            Path.Combine(FindRepositoryRoot().FullName, "src", "Merchandising.Domain", "Sales")

        Assert.IsTrue(Directory.Exists(salesDirectory), $"'{salesDirectory}' does not exist.")

        Dim sourceFiles = Directory.GetFiles(salesDirectory, "*.vb")
        Assert.IsNotEmpty(sourceFiles, "No .vb files found under src/Merchandising.Domain/Sales.")

        For Each filePath In sourceFiles

            Dim text As String = File.ReadAllText(filePath)

            Assert.IsFalse(
                Regex.IsMatch(text, "\bDouble\b"),
                $"{Path.GetFileName(filePath)} contains the disallowed token 'Double'. Money and quantity are Decimal everywhere (CLAUDE.md section 5).")

            Assert.IsFalse(
                Regex.IsMatch(text, "\bSingle\b"),
                $"{Path.GetFileName(filePath)} contains the disallowed token 'Single'. Money and quantity are Decimal everywhere (CLAUDE.md section 5).")

        Next

    End Sub

    ' --- Done when: rounding uses the currency.roundingPolicy setting, never an implicit default, and a changed policy changes the result ---

    <TestMethod>
    Public Sub SaleRoundingPolicy_Parse_ResolvesBothRegisteredSettingValues()

        Assert.AreEqual(MidpointRounding.AwayFromZero, SaleRoundingPolicy.Parse(SystemSettingRegistry.RoundingPolicyNames.AwayFromZero))
        Assert.AreEqual(MidpointRounding.ToEven, SaleRoundingPolicy.Parse(SystemSettingRegistry.RoundingPolicyNames.ToEven))

    End Sub

    <TestMethod>
    Public Sub SaleRoundingPolicy_Parse_RejectsUnrecognizedValue()

        Assert.ThrowsExactly(Of ArgumentException)(
            Function() SaleRoundingPolicy.Parse("BankersRounding"))

    End Sub

    ''' <summary>
    ''' 0.5 (quantity) * 4.0005 (unit price) = 2.00025 exactly - a genuine
    ''' DECIMAL(19,4) midpoint with an even digit (2) immediately before it,
    ''' so AwayFromZero and ToEven provably disagree. Proves the setting
    ''' value actually reaches the arithmetic, not just that the two
    ''' MidpointRounding members behave differently in the abstract.
    ''' </summary>
    <TestMethod>
    Public Sub LineTotal_ChangesWithConfiguredRoundingPolicy()

        Dim awayFromZero As MidpointRounding = SaleRoundingPolicy.Parse(SystemSettingRegistry.RoundingPolicyNames.AwayFromZero)
        Dim toEven As MidpointRounding = SaleRoundingPolicy.Parse(SystemSettingRegistry.RoundingPolicyNames.ToEven)

        Dim lineRoundedAwayFromZero As New SaleLine(1, 0.5D, 4.0005D, 1D, awayFromZero)
        Dim lineRoundedToEven As New SaleLine(1, 0.5D, 4.0005D, 1D, toEven)

        Assert.AreEqual(2.0003D, lineRoundedAwayFromZero.LineTotal)
        Assert.AreEqual(2.0002D, lineRoundedToEven.LineTotal)
        Assert.AreNotEqual(lineRoundedAwayFromZero.LineTotal, lineRoundedToEven.LineTotal)

    End Sub

    ' --- Done when: change = tendered - total, exact to DECIMAL(19,4); tendered < total is a refusal, not a negative change ---

    <TestMethod>
    Public Sub ComputeChange_TenderedAboveTotal_ReturnsExactChange()

        Dim result = CashTender.ComputeChange(tendered:=20.0000D, total:=13.3333D)

        Assert.IsTrue(result.IsAccepted)
        Assert.AreEqual(6.6667D, result.Change)

    End Sub

    <TestMethod>
    Public Sub ComputeChange_TenderedBelowTotal_IsRefusedNotNegative()

        Dim result = CashTender.ComputeChange(tendered:=9.9999D, total:=10.0000D)

        Assert.IsFalse(result.IsAccepted)
        Assert.AreEqual(0D, result.Change)
        Assert.AreEqual(0.0001D, result.ShortfallAmount)

    End Sub

    ' --- Done when: scale validation refuses an over-scale input before it could be silently rounded ---

    <TestMethod>
    Public Sub ComputeChange_OverScaleTenderedAmount_IsRejectedBeforeRounding()

        Assert.ThrowsExactly(Of ArgumentException)(
            Function() CashTender.ComputeChange(tendered:=1.99999D, total:=1D))

    End Sub

    <TestMethod>
    Public Sub SaleLine_OverScaleUnitPrice_IsRejectedBeforeRounding()

        Assert.ThrowsExactly(Of ArgumentException)(
            Function() New SaleLine(1, 1D, 1.99999D, 1D, MidpointRounding.AwayFromZero))

    End Sub

    ' --- Done when: a sale line holds its captured price/cost as data, unmoved by a later product price change ---

    <TestMethod>
    Public Sub SaleLine_CapturedPriceAndCost_AreUnmovedByLaterProductChange()

        Dim product As New Product With {
            .Id = 42,
            .Price = 100.0000D,
            .Cost = 60.0000D
        }

        Dim line As New SaleLine(product.Id, 2D, product.Price, product.Cost, MidpointRounding.AwayFromZero)

        product.Price = 150.0000D
        product.Cost = 90.0000D

        Assert.AreEqual(100.0000D, line.CapturedUnitPrice)
        Assert.AreEqual(60.0000D, line.CapturedUnitCost)
        Assert.AreEqual(200.0000D, line.LineTotal)

    End Sub

    ' --- Done when: boundary cases enumerated ---

    <TestMethod>
    Public Sub SaleLine_ZeroQuantity_IsRefused()

        Assert.ThrowsExactly(Of ArgumentOutOfRangeException)(
            Function() New SaleLine(1, 0D, 1D, 1D, MidpointRounding.AwayFromZero))

    End Sub

    <TestMethod>
    Public Sub SaleLine_NegativeQuantity_IsRefused()

        Assert.ThrowsExactly(Of ArgumentOutOfRangeException)(
            Function() New SaleLine(1, -1D, 1D, 1D, MidpointRounding.AwayFromZero))

    End Sub

    <TestMethod>
    Public Sub ComputeChange_ExactTender_GivesExactZeroChange()

        Dim result = CashTender.ComputeChange(tendered:=42.5000D, total:=42.5000D)

        Assert.IsTrue(result.IsAccepted)
        Assert.AreEqual(0.0000D, result.Change)

    End Sub

    ''' <summary>The largest value DECIMAL(19,4) can hold: 15 integer digits, 4 fractional.</summary>
    <TestMethod>
    Public Sub SaleLine_LargestColumnValue_RoundTripsExactly()

        Dim largestMoneyValue As Decimal = 999999999999999.9999D

        Dim line As New SaleLine(1, 1D, largestMoneyValue, 0D, MidpointRounding.AwayFromZero)

        Assert.AreEqual(largestMoneyValue, line.LineTotal)

    End Sub

    ' --- SaleTotals: sum of already-rounded components, never re-rounded ---

    <TestMethod>
    Public Sub ComputeSaleTotal_SumsAlreadyRoundedLineTotals()

        Dim lines As New List(Of SaleLine) From {
            New SaleLine(1, 3D, 10.3333D, 6D, MidpointRounding.AwayFromZero),
            New SaleLine(2, 2D, 5.1250D, 3D, MidpointRounding.AwayFromZero)
        }

        Dim expected As Decimal = lines(0).LineTotal + lines(1).LineTotal

        Assert.AreEqual(expected, SaleTotals.ComputeSaleTotal(lines))

    End Sub

End Class
