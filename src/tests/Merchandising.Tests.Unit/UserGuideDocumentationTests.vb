' Merchandising.Tests.Unit.UserGuideDocumentationTests
'
' P6-15. Same shape as BackupRestoreGuideDocumentationTests (P6-09) /
' UiSpecificationDocumentationTests (P5-14): the drift-check for
' docs/user-guide.md. Every evidence path it cites is resolved against disk
' and required to contain what it is cited for, and every route/policy this
' document quotes for a task that has no client screen (section 8) is
' cross-checked against the live controller source via reflection, the same
' technique CsvExporterTests already uses in this project, rather than
' trusted prose. This document's own central claim - that Audit.Review and
' Receiving.Prepare are registered policies with no consuming endpoint - is
' itself asserted here, so the day someone builds that endpoint and forgets
' to update the guide, this suite is what catches it.
'
' No database needed - file reads, reflection over already-loaded types, and
' regex only.

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Text.RegularExpressions
Imports Merchandising.Api.Controllers
Imports Merchandising.Domain.Configuration
Imports Merchandising.Domain.Security
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Mvc
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public NotInheritable Class UserGuideDocumentationTests

    ''' <summary>Same repository-root marker every other *DocumentationTests file in this suite uses.</summary>
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
            "'. This test reads docs/user-guide.md and the API controller sources from the working tree.")
        Return Nothing

    End Function

    Private Shared Function ReadRepoFile(relativePath As String) As String

        Dim fullPath As String = Path.Combine(FindRepositoryRoot().FullName, relativePath)
        Assert.IsTrue(File.Exists(fullPath), $"Expected file '{relativePath}' does not exist.")
        Return File.ReadAllText(fullPath)

    End Function

    Private Shared Function ReadDocument() As String

        Dim documentPath As String =
            Path.Combine(FindRepositoryRoot().FullName, "docs", "user-guide.md")

        Assert.IsTrue(
            File.Exists(documentPath),
            "docs/user-guide.md is missing. P6-15 owes a written document, not a planned one.")

        Return File.ReadAllText(documentPath)

    End Function

    ''' <summary>Method-level &lt;Authorize&gt; wins if present; otherwise the controller's own class-level attribute - same resolution CsvExporterTests.ResolveAuthorizePolicy uses.</summary>
    Private Shared Function ResolveAuthorizePolicy(controllerType As Type, method As MethodInfo) As String

        Dim methodAttribute As AuthorizeAttribute = method.GetCustomAttribute(Of AuthorizeAttribute)()

        If methodAttribute IsNot Nothing Then
            Return methodAttribute.Policy
        End If

        Dim classAttribute As AuthorizeAttribute = controllerType.GetCustomAttribute(Of AuthorizeAttribute)()
        Assert.IsNotNull(classAttribute, $"{controllerType.Name} carries no class-level <Authorize> and no method-level override.")
        Return classAttribute.Policy

    End Function

    Private Shared Function RouteBase(controllerType As Type) As String

        Dim routeAttribute As RouteAttribute = controllerType.GetCustomAttribute(Of RouteAttribute)()
        Assert.IsNotNull(routeAttribute, $"{controllerType.Name} carries no class-level [Route].")
        Return routeAttribute.Template

    End Function

    ' --- Done when: covers all five roles and all three clients ---

    <TestMethod>
    Public Sub Document_NamesAllFiveRolesAndAllThreeClients()

        Dim document As String = ReadDocument()

        For Each role As String In New String() {"Super Admin", "Admin", "Procurement Officer", "Inventory Clerk", "Cashier"}
            StringAssert.Contains(document, role, $"docs/user-guide.md never names the '{role}' role.")
        Next

        For Each client As String In New String() {"Merchandising.Procurement", "Merchandising.Inventory", "Merchandising.POS"}
            StringAssert.Contains(document, client, $"docs/user-guide.md never names the '{client}' client.")
        Next

    End Sub

    ' --- Done when: states plainly what the system does not do, where a user reads it (G-24) ---

    <TestMethod>
    Public Sub Document_StatesTheG24ExclusionsAndTheExactRecordedNotAuthorisedWording()

        Dim document As String = ReadDocument()

        Dim resourceText As String = ReadRepoFile(
            Path.Combine("src", "Merchandising.POS", "Resources", "PaymentWording.xaml"))

        ' The affirmative sentence quoted in the guide must be a real,
        ' unbroken substring of the live resource - not a paraphrase that
        ' could quietly drift from what a cashier actually reads.
        Const quotedSentence As String = "Card and e-wallet payments shown here are recorded for this sale only, not authorised."
        StringAssert.Contains(resourceText, quotedSentence,
            "The wording UserGuideDocumentationTests expects to quote is no longer in PaymentWording.xaml verbatim - update both together.")
        StringAssert.Contains(document, quotedSentence)

        For Each term As String In New String() {"terminal", "bank", "cash drawer", "weighing scale", "customer display", "receipt printer"}
            StringAssert.Contains(document, term,
                $"docs/user-guide.md must name '{term}' as an excluded integration (G-24, spec section 21).")
        Next

    End Sub

    ' --- Done when: every route/policy quoted for a no-screen task matches the live controller source ---

    <TestMethod>
    Public Sub Section8Routes_MatchTheLiveControllerSourceExactly()

        Dim document As String = ReadDocument()

        Assert.AreEqual("api/v1/products", RouteBase(GetType(ProductsController)))
        Assert.AreEqual("api/v1/suppliers", RouteBase(GetType(SuppliersController)))
        Assert.AreEqual("api/v1/purchase-orders", RouteBase(GetType(PurchaseOrdersController)))
        Assert.AreEqual("api/v1/receipts", RouteBase(GetType(ReceivingController)))
        Assert.AreEqual("api/v1/sales", RouteBase(GetType(SalesReturnsController)))
        Assert.AreEqual("api/v1/admin/settings", RouteBase(GetType(SystemSettingsController)))

        AssertRoute(GetType(ProductsController), NameOf(ProductsController.CreateProduct),
            GetType(HttpPostAttribute), Nothing, PolicyRegistry.Names.ProductsManage, document, "POST /api/v1/products")

        AssertRoute(GetType(ProductsController), NameOf(ProductsController.UpdateProduct),
            GetType(HttpPutAttribute), "{id}", PolicyRegistry.Names.ProductsManage, document, "PUT /api/v1/products/{id}")

        AssertRoute(GetType(ProductsController), NameOf(ProductsController.ChangePrice),
            GetType(HttpPutAttribute), "{id}/price", PolicyRegistry.Names.ProductsChangePrice, document, "PUT /api/v1/products/{id}/price")

        AssertRoute(GetType(ProductsController), NameOf(ProductsController.DeactivateProduct),
            GetType(HttpPostAttribute), "{id}/deactivate", PolicyRegistry.Names.ProductsManage, document, "POST /api/v1/products/{id}/deactivate")

        AssertRoute(GetType(SuppliersController), NameOf(SuppliersController.CreateSupplier),
            GetType(HttpPostAttribute), Nothing, PolicyRegistry.Names.SuppliersManage, document, "POST /api/v1/suppliers")

        AssertRoute(GetType(SuppliersController), NameOf(SuppliersController.UpdateSupplier),
            GetType(HttpPutAttribute), "{id}", PolicyRegistry.Names.SuppliersManage, document, "PUT /api/v1/suppliers/{id}")

        AssertRoute(GetType(PurchaseOrdersController), NameOf(PurchaseOrdersController.ClosePurchaseOrder),
            GetType(HttpPostAttribute), "{id}/close", PolicyRegistry.Names.PurchaseOrdersClose, document, "POST /api/v1/purchase-orders/{id}/close")

        AssertRoute(GetType(ReceivingController), NameOf(ReceivingController.RecordPurchaseReturn),
            GetType(HttpPostAttribute), "{receiptId}/returns", PolicyRegistry.Names.PurchaseReturnsManage, document, "POST /api/v1/receipts/{receiptId}/returns")

        AssertRoute(GetType(SystemSettingsController), NameOf(SystemSettingsController.UpdateSetting),
            GetType(HttpPutAttribute), "{key}", PolicyRegistry.Names.ConfigurationManage, document, "PUT /api/v1/admin/settings/{key}")

        ' ApproveExceptional is enforced imperatively (self-approval veto, same
        ' shape as PurchaseOrdersController.ApprovePurchaseOrder) - its
        ' declarative <Authorize> carries no Policy, so this asserts Nothing
        ' rather than SalesReturnsApproveExceptional, and separately confirms
        ' the imperative check still names that policy string in source.
        AssertRoute(GetType(SalesReturnsController), NameOf(SalesReturnsController.ApproveExceptional),
            GetType(HttpPostAttribute), "returns/{id}/approve-exceptional", Nothing, document,
            "POST /api/v1/sales/returns/{id}/approve-exceptional")

        Dim salesReturnsSource As String = ReadRepoFile(
            Path.Combine("src", "Merchandising.Api", "Controllers", "SalesReturnsController.vb"))
        StringAssert.Contains(salesReturnsSource, "PolicyRegistry.Names.SalesReturnsApproveExceptional")

        ' Reports: GET .../daily-summary and its CSV sibling, both Reports.View.
        Assert.AreEqual(PolicyRegistry.Names.ReportsView, GetType(ReportsController).GetCustomAttribute(Of AuthorizeAttribute)().Policy)
        Dim reportsRouteBase As String = RouteBase(GetType(ReportsController))
        Assert.AreEqual("api/v1/reports/sales", reportsRouteBase)
        StringAssert.Contains(document, "GET /api/v1/reports/sales/daily-summary?date=2026-08-30")
        StringAssert.Contains(document, "GET /api/v1/reports/sales/daily-summary/csv")

    End Sub

    ''' <summary>
    ''' Resolves one controller action by name, asserts it carries the given
    ''' HTTP-verb attribute with the given route template, asserts its
    ''' resolved policy, and asserts the document quotes the exact
    ''' "VERB /base/template" text - the same cross-check
    ''' BackupRestoreGuideDocumentationTests performs by hand, generalized.
    ''' </summary>
    Private Shared Sub AssertRoute(
        controllerType As Type, methodName As String, verbAttributeType As Type,
        expectedTemplate As String, expectedPolicy As String, document As String, expectedDocumentText As String)

        Dim method As MethodInfo = controllerType.GetMethod(methodName, BindingFlags.Public Or BindingFlags.Instance Or BindingFlags.DeclaredOnly)
        Assert.IsNotNull(method, $"{controllerType.Name} no longer declares a method named '{methodName}' - docs/user-guide.md section 8 depends on it.")

        Dim verbAttribute As Attribute = method.GetCustomAttribute(verbAttributeType)
        Assert.IsNotNull(verbAttribute, $"{controllerType.Name}.{methodName} no longer carries a {verbAttributeType.Name}.")

        Dim actualTemplate As String = CType(verbAttributeType.GetProperty("Template").GetValue(verbAttribute), String)
        Assert.AreEqual(expectedTemplate, actualTemplate,
            $"{controllerType.Name}.{methodName}'s route template changed - update docs/user-guide.md section 8 and this test together.")

        Dim actualPolicy As String = ResolveAuthorizePolicy(controllerType, method)
        Assert.AreEqual(expectedPolicy, actualPolicy,
            $"{controllerType.Name}.{methodName}'s effective policy changed - update docs/user-guide.md section 8 and this test together.")

        StringAssert.Contains(document, expectedDocumentText,
            $"docs/user-guide.md no longer quotes '{expectedDocumentText}' for {controllerType.Name}.{methodName}.")

    End Sub

    ' --- Done when: the settings keys quoted match SystemSettingRegistry's own constants ---

    <TestMethod>
    Public Sub ConfigurationSettingKeys_MatchSystemSettingRegistry()

        Dim document As String = ReadDocument()

        For Each key As String In New String() {
            SystemSettingRegistry.Keys.CurrencyCode,
            SystemSettingRegistry.Keys.CurrencyRoundingPolicy,
            SystemSettingRegistry.Keys.AdjustmentApprovalThreshold,
            SystemSettingRegistry.Keys.SalesReturnApprovalThreshold
        }
            StringAssert.Contains(document, key,
                $"docs/user-guide.md no longer quotes the system setting key '{key}'.")
        Next

    End Sub

    ' --- Done when: the "no screen exists yet" claim for Audit.Review / Receiving.Prepare stays true ---

    <TestMethod>
    Public Sub AuditReviewAndReceivingPrepare_RemainUnconsumedByAnyController()

        Dim document As String = ReadDocument()
        StringAssert.Contains(document, "Audit.Review")

        Dim controllersDirectory As String =
            Path.Combine(FindRepositoryRoot().FullName, "src", "Merchandising.Api", "Controllers")
        Assert.IsTrue(Directory.Exists(controllersDirectory))

        For Each filePath As String In Directory.GetFiles(controllersDirectory, "*.vb", SearchOption.TopDirectoryOnly)

            Dim text As String = File.ReadAllText(filePath)

            Assert.DoesNotContain("Names.AuditReview", text,
                $"'{Path.GetFileName(filePath)}' now references PolicyRegistry.Names.AuditReview - " &
                "docs/user-guide.md section 8 claims this policy has no consuming endpoint; update the document, this test, and section 8's known-gap wording together.")

            Assert.DoesNotContain("Names.ReceivingPrepare", text,
                $"'{Path.GetFileName(filePath)}' now references PolicyRegistry.Names.ReceivingPrepare - " &
                "docs/user-guide.md section 6.2 claims this policy has no consuming endpoint; update the document and this test together.")

        Next

        ' Both must still be registered policies (spec section 9) - this is
        ' "unconsumed", never "removed".
        Dim allPolicyNames As IEnumerable(Of String) =
            PolicyRegistry.Definitions.Select(Function(d) d.PolicyName)

        Assert.Contains(PolicyRegistry.Names.AuditReview, allPolicyNames.ToList())
        Assert.Contains(PolicyRegistry.Names.ReceivingPrepare, allPolicyNames.ToList())

    End Sub

    ' --- Done when: every evidence path this document cites is resolved against disk and contains what it is cited for ---

    <TestMethod>
    Public Sub EveryEvidencePathTheDocumentCites_ExistsAndContainsWhatItIsCitedFor()

        Dim document As String = ReadDocument()
        Dim root As String = FindRepositoryRoot().FullName

        Dim citedPaths As New List(Of String)()

        For Each m As Match In Regex.Matches(document, "evidence/[A-Za-z0-9._/-]+")

            Dim cited As String = m.Value.TrimEnd("."c, ","c, ")"c)

            If Not citedPaths.Contains(cited) Then
                citedPaths.Add(cited)
            End If

        Next

        Assert.IsNotEmpty(citedPaths, "docs/user-guide.md cites no evidence file at all.")

        Dim missing As New List(Of String)()

        For Each cited As String In citedPaths

            If Not File.Exists(Path.Combine(root, cited.Replace("/"c, Path.DirectorySeparatorChar))) Then
                missing.Add(cited)
            End If

        Next

        Assert.IsEmpty(missing,
            "docs/user-guide.md cites evidence files that do not exist on disk:" & Environment.NewLine &
            String.Join(Environment.NewLine, missing))

        Const evidenceFile As String = "evidence/phase-6/p6-15-user-guide.txt"
        Assert.Contains(evidenceFile, citedPaths, $"docs/user-guide.md no longer cites '{evidenceFile}'.")

        Dim body As String = ReadRepoFile(evidenceFile.Replace("/"c, Path.DirectorySeparatorChar))

        StringAssert.Contains(body, "guardrail", StringComparison.OrdinalIgnoreCase,
            $"'{evidenceFile}' is cited for a guardrail run but contains no mention of one.")
        StringAssert.Contains(body, "UserGuideDocumentationTests",
            $"'{evidenceFile}' is cited for this suite's own run but does not mention it.")

    End Sub

    ' --- Done when: docs/backup-restore-guide.md is linked rather than duplicated ---

    <TestMethod>
    Public Sub Document_LinksBackupRestoreGuide_RatherThanDuplicatingIt()

        Dim document As String = ReadDocument()
        StringAssert.Contains(document, "backup-restore-guide.md")
        StringAssert.DoesNotMatch(document, New Regex("mysqldump\.exe", RegexOptions.IgnoreCase),
            "docs/user-guide.md should link to docs/backup-restore-guide.md for backup/restore commands, not repeat them.")

    End Sub

End Class
