' Merchandising.Tests.Unit.PaymentWordingTests
'
' P5-12 / G-24: "Payment recording could be mistaken for payment
' authorization." The spec's mitigation (section 23) is "label card/e-wallet
' as operational recording only; exclude terminal/bank integration; test UI
' wording and reports" - the last four words are this file. A wording gap is
' invisible to every other kind of test in this suite, so it gets its own.
'
' Two things are scanned: Merchandising.POS's XAML (the UI half - today just
' Resources/PaymentWording.xaml, since P5-13 has not built the payment
' screens yet; this suite will keep scanning whatever XAML that card adds)
' and Merchandising.Contracts/Sales (the report/response half -
' CashierSessionPaymentTotalResponse, SalePaymentResponse, SalesReturnResponse
' and friends).
'
' The denylist below is P5-12's own list verbatim: approved, authorised,
' authorized, accepted, cleared, settled, charged. A bare word boundary match
' is not enough, because the CORRECT wording deliberately contains one of
' them ("not authorised") - the whole point of G-24's mitigation is to state
' what did NOT happen. So a match immediately preceded by "not" or "never" is
' the permitted, negated form; anything else is a violation. This also means
' identifiers like SalesReturnResponse.ApprovedByUserId are never touched -
' "approved" only matches at a \b, and there is no boundary between the 'd'
' of "Approved" and the 'B' of "By" - that field names a manager's approval
' of an exceptional return (P4-10/P5-11), a different "approved" than G-24
' is about, and the regex never sees it as a match in the first place.

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Merchandising.Contracts.Sales
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class PaymentWordingTests

    ''' <summary>Same repository-root marker SaleArithmeticTests / RolePermissionMatrixDocumentationTests use.</summary>
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
            "'. This test scans src/Merchandising.POS and src/Merchandising.Contracts/Sales from the working tree.")
        Return Nothing

    End Function

    ''' <summary>
    ''' P5-12's denylist, applied as: a match is a violation unless the word
    ''' immediately preceding it is "not" or "never" - the only shape G-24's
    ''' correct wording is allowed to take.
    ''' </summary>
    Private Shared ReadOnly DenylistPattern As New Regex(
        "(?<!\b(?:not|never)\s)\b(approved|authorised|authorized|accepted|cleared|settled|charged)\b",
        RegexOptions.IgnoreCase)

    Private Shared Function ScannedFiles() As List(Of String)

        Dim root As String = FindRepositoryRoot().FullName
        Dim files As New List(Of String)()

        Dim posDirectory As String = Path.Combine(root, "src", "Merchandising.POS")
        Assert.IsTrue(Directory.Exists(posDirectory), $"'{posDirectory}' does not exist.")
        files.AddRange(Directory.GetFiles(posDirectory, "*.xaml", SearchOption.AllDirectories).
            Where(Function(p) Not p.Contains(Path.Combine("obj", "")) AndAlso Not p.Contains(Path.Combine("bin", ""))))

        Dim salesContractsDirectory As String = Path.Combine(root, "src", "Merchandising.Contracts", "Sales")
        Assert.IsTrue(Directory.Exists(salesContractsDirectory), $"'{salesContractsDirectory}' does not exist.")
        files.AddRange(Directory.GetFiles(salesContractsDirectory, "*.vb", SearchOption.AllDirectories))

        Return files

    End Function

    ''' <summary>
    ''' Strips developer commentary before the denylist runs. G-24 is about
    ''' what a USER reads - a JSON string literal or XAML element text - not
    ''' about a doc comment explaining the rule, which necessarily names the
    ''' banned words to describe them (this file's own header does the same
    ''' thing, and SalePaymentResponse.vb's header is exactly this case: "never
    ''' 'Approved', 'Authorised', or 'Accepted'" is documentation about the
    ''' rule, not a label a cashier or a report reader would ever see). VB
    ''' comments in this codebase are always whole lines (leading ' or '''),
    ''' never trailing after code, so a line-level strip is exact for .vb; XML
    ''' comments in .xaml are stripped the same way for the same reason.
    ''' </summary>
    Private Shared Function StripCommentary(text As String, filePath As String) As String

        If String.Equals(Path.GetExtension(filePath), ".xaml", StringComparison.OrdinalIgnoreCase) Then
            Return Regex.Replace(text, "<!--[\s\S]*?-->", String.Empty)
        End If

        Dim kept As New List(Of String)()
        For Each line As String In text.Split(ControlChars.Lf)
            If Not line.TrimStart().StartsWith("'", StringComparison.Ordinal) Then
                kept.Add(line)
            End If
        Next

        Return String.Join(ControlChars.Lf, kept)

    End Function

    ' --- Done when: a denylist scan of POS XAML and Sales contracts fails if an unnegated authorising word appears ---

    <TestMethod>
    Public Sub PaymentWordingFiles_ContainNoUnnegatedAuthorisingWords()

        Dim scanned As List(Of String) = ScannedFiles()
        Assert.IsNotEmpty(scanned, "No files were found to scan - the scan itself is broken.")

        Dim violations As New List(Of String)()

        For Each filePath As String In scanned

            Dim text As String = StripCommentary(File.ReadAllText(filePath), filePath)

            For Each match As Match In DenylistPattern.Matches(text)
                violations.Add($"{Path.GetFileName(filePath)}: '{match.Value}' (unnegated) near position {match.Index}")
            Next

        Next

        Assert.IsEmpty(violations,
                       "G-24: card/e-wallet payment must never be described as authorised. These words appear without a preceding 'not'/'never':" &
                       Environment.NewLine & String.Join(Environment.NewLine, violations))

    End Sub

    ' --- Done when: proven falsifiable - the denylist itself, not just the file set, is watched to fail on the exact bad phrase and pass on the correct one ---

    <TestMethod>
    Public Sub Denylist_FiresOnTheExactBadPhrase_AndPassesOnTheCorrectedOne()

        Assert.IsTrue(DenylistPattern.IsMatch("Payment approved"),
                      "The denylist must catch the deliberately bad label this card was watched fail against - if it does not, the scan above is decorative.")

        Assert.IsTrue(DenylistPattern.IsMatch("Card authorised"))
        Assert.IsTrue(DenylistPattern.IsMatch("Transaction accepted"))
        Assert.IsTrue(DenylistPattern.IsMatch("Payment cleared"))
        Assert.IsTrue(DenylistPattern.IsMatch("Payment settled"))
        Assert.IsTrue(DenylistPattern.IsMatch("Card charged"))

        Assert.IsFalse(DenylistPattern.IsMatch("Card and e-wallet payments shown here are recorded for this sale only, not authorised."),
                       "G-24's actual wording negates the word - a correctly-worded label must not itself be flagged.")

    End Sub

    ' --- Done when: the affirmative wording exists in the UI, not merely the absence of the bad wording ---

    <TestMethod>
    Public Sub PosPaymentWordingResource_StatesRecordedNotAuthorised()

        Dim resourcePath As String = Path.Combine(FindRepositoryRoot().FullName, "src", "Merchandising.POS", "Resources", "PaymentWording.xaml")
        Assert.IsTrue(File.Exists(resourcePath), $"'{resourcePath}' does not exist - P5-12 was to add it.")

        Dim text As String = File.ReadAllText(resourcePath)

        StringAssert.Contains(text, "CardEWalletRecordedNotice",
                              "The resource key P5-13's screens will bind to for the recorded-not-authorised notice is missing.")
        StringAssert.Contains(text, "recorded",
                              "The affirmative statement - card/e-wallet is recorded - must be present, not merely assumed absent of the bad wording.")
        StringAssert.Contains(text, "not authorised",
                              "The negation must be spelled out, not implied.")

    End Sub

    ' --- Done when: the exclusion is stated where a user reads it, not only in the spec ---

    <TestMethod>
    Public Sub PosPaymentWordingResource_StatesTheIntegrationExclusion()

        Dim resourcePath As String = Path.Combine(FindRepositoryRoot().FullName, "src", "Merchandising.POS", "Resources", "PaymentWording.xaml")
        Dim text As String = File.ReadAllText(resourcePath)

        StringAssert.Contains(text, "CardEWalletIntegrationExclusionNotice")

        Dim requiredTerms As String() = {"terminal", "bank", "cash drawer", "weighing scale", "customer display", "receipt printer"}

        For Each term As String In requiredTerms
            StringAssert.Contains(text, term,
                                  $"The exclusion notice must name '{term}' explicitly - spec section 10.3's full exclusion list, stated where a cashier reads it.")
        Next

    End Sub

    ' --- Done when: the response side of the wording (SalePaymentResponse) agrees with the UI side ---

    <TestMethod>
    Public Sub SalePaymentResponse_StatusConst_IsTheStableRecordedWording()

        Dim response As New SalePaymentResponse()
        Assert.AreEqual(SalePaymentResponse.RecordedStatus, response.Status,
                        "A default-constructed SalePaymentResponse must already carry the recorded wording, whatever the payment method.")
        Assert.IsFalse(DenylistPattern.IsMatch(SalePaymentResponse.RecordedStatus),
                       "SalePaymentResponse.RecordedStatus is the wording P5-05's payment totals and P5-12's UI notice both agree on - it must never drift into an authorising word.")

    End Sub

End Class
