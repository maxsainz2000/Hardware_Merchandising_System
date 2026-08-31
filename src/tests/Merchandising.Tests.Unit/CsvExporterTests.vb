' Merchandising.Tests.Unit.CsvExporterTests
'
' P6-06: no database needed - CsvExporter/ReportCsvFormatters are pure
' functions over hand-built response objects (the same "no database needed"
' shape ReportSpecificationDocumentationTests already uses for this phase),
' reached through the InternalsVisibleTo grant Merchandising.Api.vbproj
' carries for this assembly.
'
' THREE THINGS ARE PROVEN HERE, NOT JUST DESCRIBED:
'   1. Escaping - the five values that actually break CSV (comma, embedded
'      quote, embedded newline, a leading =/+/-/@, and a non-ASCII name),
'      plus the UTF-8 BOM CsvExporter.BuildFile always writes.
'   2. Column order - every one of the twelve reports' header arrays is
'      asserted against a committed literal, so a header reordered later
'      fails this suite rather than a classmate's spreadsheet.
'   3. The permission matrix - EveryReport_ExportPolicyEqualsViewPolicy walks
'      all three report controllers by reflection and asserts, per report,
'      that the CSV export route and its JSON view route carry the exact
'      same <Authorize> policy, and that the policy is Reports.View
'      (docs/report-specification.md section 8).
'
' The formula-injection mitigation and the UTF-8-BOM decision were both
' measured against a real Excel install on this machine before being coded
' here at all - see CsvExporter.vb's own header and ADR-024 for the
' evidence; this suite only proves CsvExporter's OWN output shape, not
' Excel's behavior a second time.

Imports System.Collections.Generic
Imports System.Reflection
Imports Merchandising.Api.Controllers
Imports Merchandising.Api.Reporting
Imports Merchandising.Contracts.Inventory
Imports Merchandising.Contracts.Reporting
Imports Merchandising.Domain.Security
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Mvc
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class CsvExporterTests

    ' ------------------------------------------------------------- escaping

    <TestMethod>
    Public Sub EscapeField_EmbeddedComma_QuotesTheWholeField()
        Assert.AreEqual("""Hammer, 16oz""", CsvExporter.EscapeField("Hammer, 16oz"))
    End Sub

    <TestMethod>
    Public Sub EscapeField_EmbeddedDoubleQuote_QuotesAndDoublesTheQuote()
        Assert.AreEqual("""16"""" pipe wrench""", CsvExporter.EscapeField("16"" pipe wrench"))
    End Sub

    <TestMethod>
    Public Sub EscapeField_EmbeddedNewline_QuotesTheWholeField()
        Assert.AreEqual("""line one" & vbLf & "line two""", CsvExporter.EscapeField("line one" & vbLf & "line two"))
    End Sub

    <TestMethod>
    Public Sub EscapeField_EmbeddedCarriageReturn_QuotesTheWholeField()
        Assert.AreEqual("""line one" & vbCr & "line two""", CsvExporter.EscapeField("line one" & vbCr & "line two"))
    End Sub

    ''' <summary>The four characters CLAUDE.md's Done-when box names, one at a time - the CsvExporter.vb header records what Excel actually does with each, measured via COM automation.</summary>
    <TestMethod>
    Public Sub EscapeField_LeadingEquals_PrefixedWithApostrophe()
        Assert.AreEqual("'=1+1", CsvExporter.EscapeField("=1+1"))
    End Sub

    <TestMethod>
    Public Sub EscapeField_LeadingPlus_PrefixedWithApostrophe()
        Assert.AreEqual("'+1+1", CsvExporter.EscapeField("+1+1"))
    End Sub

    <TestMethod>
    Public Sub EscapeField_LeadingMinus_PrefixedWithApostrophe()
        Assert.AreEqual("'-1+1", CsvExporter.EscapeField("-1+1"))
    End Sub

    <TestMethod>
    Public Sub EscapeField_LeadingAt_PrefixedWithApostrophe()
        Assert.AreEqual("'@SUM(1+1)", CsvExporter.EscapeField("@SUM(1+1)"))
    End Sub

    ''' <summary>The mitigation and RFC4180 quoting compose - a formula-shaped value that also contains a comma is both prefixed and quoted.</summary>
    <TestMethod>
    Public Sub EscapeField_LeadingEqualsWithEmbeddedComma_PrefixedAndQuoted()
        Assert.AreEqual("""'=1+1,2""", CsvExporter.EscapeField("=1+1,2"))
    End Sub

    <TestMethod>
    Public Sub EscapeField_NonAsciiName_PassesThroughUnescaped()
        Assert.AreEqual("José Muñoz Ñañez café", CsvExporter.EscapeField("José Muñoz Ñañez café"))
    End Sub

    <TestMethod>
    Public Sub EscapeField_Nothing_BecomesEmptyField()
        Assert.AreEqual(String.Empty, CsvExporter.EscapeField(Nothing))
    End Sub

    ' ------------------------------------------------------------------ BOM

    <TestMethod>
    Public Sub BuildFile_AlwaysBeginsWithUtf8Bom()

        Dim bytes As Byte() = CsvExporter.BuildFile({"a", "b"}, {New String() {"1", "2"}})

        Assert.IsGreaterThanOrEqualTo(
            3, bytes.Length, "Expected at least the three-byte UTF-8 BOM.")
        Assert.AreEqual(&HEF, bytes(0))
        Assert.AreEqual(&HBB, bytes(1))
        Assert.AreEqual(&HBF, bytes(2))

    End Sub

    <TestMethod>
    Public Sub BuildFile_UsesCrlfLineEndingsAndCommaSeparators()

        Dim bytes As Byte() = CsvExporter.BuildFile({"a", "b"}, {New String() {"1", "2"}})

        ' Skip the three-byte BOM this class always writes (proven separately by
        ' BuildFile_AlwaysBeginsWithUtf8Bom) - GetString would otherwise decode it
        ' as a leading U+FEFF character.
        Dim text As String = New Text.UTF8Encoding(True).GetString(bytes, 3, bytes.Length - 3)

        Assert.AreEqual("a,b" & vbCrLf & "1,2" & vbCrLf, text)

    End Sub

    ' -------------------------------------------------------------- decimal scale

    ''' <summary>DECIMAL(19,4)-shaped money keeps all four places, trailing zeros included - never routed through Double, never truncated to "12.4".</summary>
    <TestMethod>
    Public Sub FormatDecimal_PreservesMoneyScale()
        Assert.AreEqual("1234.5000", CsvExporter.FormatDecimal(1234.5000D))
    End Sub

    ''' <summary>DECIMAL(19,3)-shaped quantity keeps all three places.</summary>
    <TestMethod>
    Public Sub FormatDecimal_PreservesQuantityScale()
        Assert.AreEqual("12.500", CsvExporter.FormatDecimal(12.500D))
    End Sub

    <TestMethod>
    Public Sub FormatDecimal_NegativeValue_KeepsSignAndScale()
        Assert.AreEqual("-3.0000", CsvExporter.FormatDecimal(-3.0000D))
    End Sub

    ' -------------------------------------------------------------- filenames

    <TestMethod>
    Public Sub BuildFileName_BothDatesUnbounded_UsesAllDates()
        Assert.AreEqual("sales-by-product_all-dates.csv", CsvExporter.BuildFileName("sales-by-product", Nothing, Nothing))
    End Sub

    <TestMethod>
    Public Sub BuildFileName_SameFromAndToDate_UsesSingleDate()
        Assert.AreEqual("daily-sales-summary_2026-08-31.csv", CsvExporter.BuildFileName("daily-sales-summary", "2026-08-31", "2026-08-31"))
    End Sub

    <TestMethod>
    Public Sub BuildFileName_DifferentFromAndToDate_UsesRange()
        Assert.AreEqual(
            "sales-by-product_2026-08-01_to_2026-08-31.csv",
            CsvExporter.BuildFileName("sales-by-product", "2026-08-01", "2026-08-31"))
    End Sub

    <TestMethod>
    Public Sub BuildFileName_ExtraSuffix_IsAppendedBeforeExtension()
        Assert.AreEqual(
            "stock-movements_all-dates_product-42.csv",
            CsvExporter.BuildFileName("stock-movements", Nothing, Nothing, "product-42"))
    End Sub

    ' ------------------------------------------------------- column order (twelve reports)

    <TestMethod>
    Public Sub DailySalesSummaryHeaders_MatchCommittedOrder()
        CollectionAssert.AreEqual(
            New String() {"date", "completedSalesCount", "completedSalesTotal", "completedReturnsCount",
                          "completedReturnsTotal", "netSalesTotal", "cashTotal", "cardTotal", "eWalletTotal"},
            ReportCsvFormatters.DailySalesSummaryHeaders())
    End Sub

    <TestMethod>
    Public Sub PaymentMethodSummaryHeaders_MatchCommittedOrder()
        CollectionAssert.AreEqual(
            New String() {"fromDate", "toDate", "cashTotal", "cardTotal", "eWalletTotal", "note"},
            ReportCsvFormatters.PaymentMethodSummaryHeaders())
    End Sub

    <TestMethod>
    Public Sub SalesByProductHeaders_MatchCommittedOrder()
        CollectionAssert.AreEqual(
            New String() {"productId", "productSku", "productName", "quantitySold", "quantityReturned",
                          "netQuantity", "grossSalesValue", "capturedCostBasis"},
            ReportCsvFormatters.SalesByProductHeaders())
    End Sub

    <TestMethod>
    Public Sub SalesByCashierHeaders_MatchCommittedOrder()
        CollectionAssert.AreEqual(
            New String() {"cashierUserId", "cashierUsername", "completedSalesCount", "completedSalesTotal",
                          "returnsCount", "returnsTotal", "netValue", "cashTotal", "cardTotal", "eWalletTotal"},
            ReportCsvFormatters.SalesByCashierHeaders())
    End Sub

    <TestMethod>
    Public Sub ReturnsAndCancellationsHeaders_MatchCommittedOrder()
        CollectionAssert.AreEqual(
            New String() {"salesReturnId", "salesReturnLineId", "saleId", "productId", "productSku", "productName",
                          "quantityReturned", "reason", "returnedByUserId", "returnedByUsername", "approvedByUserId",
                          "approvedByUsername", "status", "restocksItem", "returnedAtUtc"},
            ReportCsvFormatters.ReturnsAndCancellationsHeaders())
    End Sub

    <TestMethod>
    Public Sub ProductPerformanceHeaders_MatchCommittedOrder()
        CollectionAssert.AreEqual(
            New String() {"productId", "productSku", "productName", "quantitySold", "quantityReturned", "netQuantity",
                          "netSalesValue", "recordedCostEstimate", "marginEstimate", "marginEstimateLabel", "currentStockQuantity"},
            ReportCsvFormatters.ProductPerformanceHeaders())
    End Sub

    <TestMethod>
    Public Sub PurchaseOrderHistoryHeaders_MatchCommittedOrder()
        CollectionAssert.AreEqual(
            New String() {"id", "orderNumber", "supplierId", "supplierName", "status", "createdAtUtc",
                          "orderedQuantity", "orderedValue", "receivedQuantity", "receivedValue", "outstandingQuantity"},
            ReportCsvFormatters.PurchaseOrderHistoryHeaders())
    End Sub

    <TestMethod>
    Public Sub GoodsReceivingHistoryHeaders_MatchCommittedOrder()
        CollectionAssert.AreEqual(
            New String() {"receiptId", "receiptLineId", "referenceNumber", "purchaseOrderId", "orderNumber", "supplierId",
                          "supplierName", "receivedAtUtc", "productId", "productSku", "productName", "orderedQuantity",
                          "receivedQuantity", "receivedByUserId", "receivedByUsername"},
            ReportCsvFormatters.GoodsReceivingHistoryHeaders())
    End Sub

    <TestMethod>
    Public Sub CurrentStockHeaders_MatchCommittedOrder()
        CollectionAssert.AreEqual(
            New String() {"productId", "sku", "name", "categoryId", "categoryName", "isActive",
                          "quantity", "reorderLevel", "stockStatus"},
            ReportCsvFormatters.CurrentStockHeaders())
    End Sub

    <TestMethod>
    Public Sub LowStockHeaders_MatchCommittedOrder()
        CollectionAssert.AreEqual(
            New String() {"productId", "sku", "name", "quantity", "reorderLevel"},
            ReportCsvFormatters.LowStockHeaders())
    End Sub

    <TestMethod>
    Public Sub StockMovementHeaders_MatchCommittedOrder()
        CollectionAssert.AreEqual(
            New String() {"id", "productId", "productSku", "productName", "delta", "quantityBefore", "quantityAfter",
                          "reason", "actorUserId", "actorUsername", "correlationId", "createdAtUtc"},
            ReportCsvFormatters.StockMovementHeaders())
    End Sub

    <TestMethod>
    Public Sub StockAdjustmentHeaders_MatchCommittedOrder()
        CollectionAssert.AreEqual(
            New String() {"id", "productId", "productSku", "productName", "quantityVariance", "reason",
                          "requestedByUserId", "requestedByUsername", "approvedByUserId", "approvedByUsername",
                          "exceedsThreshold", "status", "stockEffectApplied", "createdAtUtc", "updatedAtUtc"},
            ReportCsvFormatters.StockAdjustmentHeaders())
    End Sub

    ' -------------------------------------------------------- row content, one report

    ''' <summary>
    ''' One report's row-building exercised end to end with the values that
    ''' actually break CSV, all at once, on a real report DTO rather than a
    ''' synthetic string array - a product name carrying a comma, an
    ''' embedded quote, and a non-ASCII character; a cost basis at full
    ''' DECIMAL(19,4) scale; a null-safe nullable field elsewhere in the same
    ''' report family (ReturnsAndCancellationsRow's ApprovedByUserId) is
    ''' covered separately below.
    ''' </summary>
    <TestMethod>
    Public Sub SalesByProductRow_EscapesSpecialCharactersAndPreservesDecimalScale()

        Dim item As New SalesByProductItemResponse With {
            .ProductId = 7,
            .ProductSku = "NAIL-16",
            .ProductName = "Nails, 2"" - café",
            .QuantitySold = 100.000D,
            .QuantityReturned = 5.000D,
            .NetQuantity = 95.000D,
            .GrossSalesValue = 1999.9900D,
            .CapturedCostBasis = 1000.0000D
        }

        Dim row As String() = ReportCsvFormatters.SalesByProductRow(item)

        Assert.AreEqual("7", row(0))
        Assert.AreEqual("NAIL-16", row(1))
        Assert.AreEqual("Nails, 2"" - café", row(2))
        Assert.AreEqual("100.000", row(3))
        Assert.AreEqual("5.000", row(4))
        Assert.AreEqual("95.000", row(5))
        Assert.AreEqual("1999.9900", row(6))
        Assert.AreEqual("1000.0000", row(7))

        Dim escaped As String = CsvExporter.EscapeField(row(2))
        Assert.AreEqual("""Nails, 2"""" - café""", escaped)

    End Sub

    <TestMethod>
    Public Sub ReturnsAndCancellationsRow_NullApprovedByUserId_BecomesEmptyField()

        Dim item As New ReturnsAndCancellationsItemResponse With {
            .SalesReturnId = 1,
            .SalesReturnLineId = 1,
            .SaleId = 1,
            .ProductId = 1,
            .ProductSku = "SKU",
            .ProductName = "Product",
            .QuantityReturned = 1.000D,
            .Reason = "Damaged",
            .ReturnedByUserId = 1,
            .ReturnedByUsername = "cashier1",
            .ApprovedByUserId = Nothing,
            .ApprovedByUsername = Nothing,
            .Status = "PendingApproval",
            .RestocksItem = False,
            .ReturnedAtUtc = New DateTime(2026, 8, 31, 4, 30, 0, DateTimeKind.Utc)
        }

        Dim row As String() = ReportCsvFormatters.ReturnsAndCancellationsRow(item)

        ' approvedByUserId is a nullable Integer - CsvExporter.FormatNullableInteger
        ' pre-normalizes Nothing to "" inside the row itself.
        Assert.AreEqual(String.Empty, row(10))

        ' approvedByUsername is a plain String field, passed through the row
        ' raw (Nothing, not "") - CsvExporter.EscapeField is what normalizes
        ' Nothing to an empty CSV field, at BuildFile time, the same as every
        ' other field. Both halves of that contract are asserted here.
        Assert.IsNull(row(11))
        Assert.AreEqual(String.Empty, CsvExporter.EscapeField(row(11)))

        Assert.AreEqual("2026-08-31T04:30:00.000Z", row(14)) ' returnedAtUtc

    End Sub

    ' ------------------------------------------------------- permission matrix

    Private Shared ReadOnly ReportControllerTypes As Type() = {
        GetType(ReportsController), GetType(ProcurementReportsController), GetType(InventoryReportsController)}

    ''' <summary>
    ''' Per report, not by inspection: every JSON view route
    ''' (&lt;HttpGet("...")&gt;) across the three report controllers is
    ''' paired with its CSV export sibling ("...&#47;csv"), and both must
    ''' carry the exact same resolved &lt;Authorize&gt; policy - which must
    ''' itself be Reports.View (docs/report-specification.md section 8).
    ''' Twelve reports go in; this walks all twelve, not a sample.
    ''' </summary>
    <TestMethod>
    Public Sub EveryReport_ExportPolicyEqualsViewPolicy()

        Dim reportsChecked As Integer = 0

        For Each controllerType As Type In ReportControllerTypes

            Dim viewRoutes As New Dictionary(Of String, MethodInfo)(StringComparer.OrdinalIgnoreCase)
            Dim exportRoutes As New Dictionary(Of String, MethodInfo)(StringComparer.OrdinalIgnoreCase)

            For Each method As MethodInfo In controllerType.GetMethods(BindingFlags.Public Or BindingFlags.Instance Or BindingFlags.DeclaredOnly)

                Dim httpGet As HttpGetAttribute = method.GetCustomAttribute(Of HttpGetAttribute)()

                If httpGet Is Nothing OrElse httpGet.Template Is Nothing Then
                    Continue For
                End If

                Dim template As String = httpGet.Template

                If template.EndsWith("/csv", StringComparison.OrdinalIgnoreCase) Then
                    exportRoutes(template.Substring(0, template.Length - "/csv".Length)) = method
                Else
                    viewRoutes(template) = method
                End If

            Next

            Assert.HasCount(
                viewRoutes.Count, exportRoutes,
                $"{controllerType.Name}: expected exactly one CSV export route per JSON view route.")

            For Each pair As KeyValuePair(Of String, MethodInfo) In viewRoutes

                Assert.IsTrue(
                    exportRoutes.ContainsKey(pair.Key),
                    $"{controllerType.Name}: report route '{pair.Key}' has no matching '{pair.Key}/csv' export route.")

                Dim viewPolicy As String = ResolveAuthorizePolicy(controllerType, pair.Value)
                Dim exportPolicy As String = ResolveAuthorizePolicy(controllerType, exportRoutes(pair.Key))

                Assert.AreEqual(
                    viewPolicy, exportPolicy,
                    $"{controllerType.Name}: route '{pair.Key}' and its CSV export must carry the same Authorize policy.")

                Assert.AreEqual(
                    PolicyRegistry.Names.ReportsView, viewPolicy,
                    $"{controllerType.Name}: route '{pair.Key}' does not carry Reports.View.")

                reportsChecked += 1

            Next

        Next

        Assert.AreEqual(
            12, reportsChecked, "Expected all twelve spec section 14 reports to be checked, not a subset.")

    End Sub

    ''' <summary>Method-level &lt;Authorize&gt; wins if present (none of the report actions carry one today); otherwise the controller's own class-level attribute - the same resolution ASP.NET Core's own authorization middleware performs.</summary>
    Private Shared Function ResolveAuthorizePolicy(controllerType As Type, method As MethodInfo) As String

        Dim methodAttribute As AuthorizeAttribute = method.GetCustomAttribute(Of AuthorizeAttribute)()

        If methodAttribute IsNot Nothing Then
            Return methodAttribute.Policy
        End If

        Dim classAttribute As AuthorizeAttribute = controllerType.GetCustomAttribute(Of AuthorizeAttribute)()
        Assert.IsNotNull(classAttribute, $"{controllerType.Name} carries no class-level <Authorize> and no method-level override.")
        Return classAttribute.Policy

    End Function

End Class
