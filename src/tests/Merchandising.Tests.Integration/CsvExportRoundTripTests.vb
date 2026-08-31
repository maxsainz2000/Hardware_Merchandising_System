' Merchandising.Tests.Integration.CsvExportRoundTripTests
'
' P6-06's own Done-when box: "The round trip is performed: a real export
' opened in Excel on this machine and read back, with the artifact recorded,
' not described." This class produces the "real export" half - real HTTP,
' through the real ASP.NET Core pipeline (MerchandisingApiFactory, the same
' seam AuthorizationMatrixTests uses), against the real pinned MariaDB, for
' a product row whose Name was chosen to carry every value CsvExporterTests
' already proved in isolation (an embedded comma, an embedded double quote,
' a leading '=', and a non-ASCII name) - all five... four of the "breaks
' CSV" values in ONE live database row, so this is the same escaping
' CsvExporter.EscapeField's unit tests already assert, now proven to survive
' the full ReportService -> ReportCsvFormatters -> CsvExporter -> HTTP
' response path, not merely CsvExporter called directly.
'
' The Excel half - opening the bytes this test writes to
' evidence/phase-6/p6-06-excel-roundtrip/ and reading them back through a
' real Excel install via COM automation - happens outside any test process
' (MSTest cannot drive a GUI application); that transcript is committed
' alongside this test's own output, and evidence/phase-6/p6-06-csv-export.txt
' points to both.
'
' The embedded-newline case is deliberately NOT reproduced here - a raw
' newline in Products.Name is not a scenario this system's own product
' entry ever produces, and CsvExporterTests.EscapeField_EmbeddedNewline_
' QuotesTheWholeField already proves CsvExporter's own handling of it
' directly. This class proves the REST OF THE PIPELINE around CsvExporter,
' not CsvExporter's escaping rules a second time.

Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Net.Http.Json
Imports System.Text
Imports System.Threading.Tasks
Imports Merchandising.Contracts.Auth
Imports Merchandising.Domain.Entities
Imports Merchandising.Domain.Security
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class CsvExportRoundTripTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const FixtureUsername As String = "p6_06_fixture_admin"
    Private Const FixturePassword As String = "P6-06 Fixture Passw0rd!"
    Private Const FixtureProductSku As String = "p6_06_fixture_hostile_sku"

    ''' <summary>
    ''' Carries an embedded comma, an embedded double quote, a leading '=',
    ''' and a non-ASCII name - four of the five values CsvExporterTests
    ''' proves in isolation, combined into one real Products.Name.
    ''' </summary>
    Private Const HostileProductName As String = "=1+1, ""special"" café"

    Private _factory As MerchandisingApiFactory
    Private _connectionFactory As ConnectionFactory

    <TestInitialize>
    Public Sub SetUp()
        _factory = New MerchandisingApiFactory()
        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
    End Sub

    <TestCleanup>
    Public Sub TearDown()
        _factory?.Dispose()
    End Sub

    <TestMethod>
    Public Async Function CurrentStockCsvExport_EscapesHostileProductNameAndWritesEvidence() As Task

        Await EnsureFixtureUserAsync()
        Dim productId As Integer = Await EnsureFixtureProductAsync()

        Using client As HttpClient = _factory.CreateClient()

            Dim token As String = Await LoginAsync(client)

            Dim request As New HttpRequestMessage(HttpMethod.Get, "/api/v1/reports/inventory/current-stock/csv?includeInactive=true")
            request.Headers.Authorization = New AuthenticationHeaderValue("Bearer", token)

            Using response As HttpResponseMessage = Await client.SendAsync(request)

                Assert.AreEqual(
                    HttpStatusCode.OK, response.StatusCode,
                    "GET /api/v1/reports/inventory/current-stock/csv did not succeed. Body: " & Await response.Content.ReadAsStringAsync())

                Assert.IsTrue(
                    response.Content.Headers.ContentType.MediaType.Equals("text/csv", StringComparison.OrdinalIgnoreCase),
                    $"Expected text/csv, got '{response.Content.Headers.ContentType}'.")

                Dim bytes As Byte() = Await response.Content.ReadAsByteArrayAsync()

                Assert.IsGreaterThanOrEqualTo(3, bytes.Length, "Response is too short to carry even a BOM.")
                Assert.AreEqual(&HEF, bytes(0), "Missing UTF-8 BOM byte 1 (ADR-024).")
                Assert.AreEqual(&HBB, bytes(1), "Missing UTF-8 BOM byte 2 (ADR-024).")
                Assert.AreEqual(&HBF, bytes(2), "Missing UTF-8 BOM byte 3 (ADR-024).")

                Dim text As String = New UTF8Encoding(True).GetString(bytes, 3, bytes.Length - 3)

                ' Exactly the shape CsvExporterTests.EscapeField_LeadingEqualsWithEmbeddedComma_PrefixedAndQuoted
                ' proves directly: apostrophe-prefixed (neutralizes the formula), then
                ' RFC4180-quoted with the embedded quote doubled (the comma forces quoting).
                Dim expectedField As String = """'=1+1, """"special"""" café"""
                Assert.Contains(
                    expectedField, text,
                    $"Expected the hostile product name to appear as {expectedField} in the export. Full text:{vbCrLf}{text}")

                Dim evidenceDirectory As String =
                    Path.Combine(FindRepositoryRoot().FullName, "evidence", "phase-6", "p6-06-excel-roundtrip")
                Directory.CreateDirectory(evidenceDirectory)

                Dim evidencePath As String = Path.Combine(evidenceDirectory, "current-stock-live-export.csv")
                Await File.WriteAllBytesAsync(evidencePath, bytes)

            End Using

        End Using

    End Function

    ' --------------------------------------------------------------- fixtures

    Private Async Function LoginAsync(client As HttpClient) As Task(Of String)

        Using response As HttpResponseMessage =
            Await client.PostAsJsonAsync("/api/v1/auth/login",
                                         New LoginRequest With {.Username = FixtureUsername, .Password = FixturePassword})

            Assert.AreEqual(
                HttpStatusCode.OK, response.StatusCode,
                "Fixture admin user could not log in. Body: " & Await response.Content.ReadAsStringAsync())

            Dim login As LoginResponse = Await response.Content.ReadFromJsonAsync(Of LoginResponse)()
            Return login.Token

        End Using

    End Function

    Private Async Function EnsureFixtureUserAsync() As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Dim existing As User = Await UserRepository.FindByUsernameAsync(connection, FixtureUsername)
            If existing IsNot Nothing Then
                Return
            End If
        End Using

        Dim migratorFactory As New ConnectionFactory(LoadMigratorOptions())
        Await CreateUserCommand.RunAsync(migratorFactory, FixtureUsername, FixturePassword, RoleNames.Admin)

    End Function

    ''' <summary>Real, permanent row - the same "never deleted" convention AuthorizationMatrixTests.EnsureFixtureProductAsync already uses for its own fixture product.</summary>
    Private Async Function EnsureFixtureProductAsync() As Task(Of Integer)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            Using selectCommand As MySqlCommand = connection.CreateCommand()
                selectCommand.CommandText = "SELECT Id FROM Products WHERE Sku = @sku;"
                selectCommand.Parameters.AddWithValue("@sku", FixtureProductSku)
                Dim existing As Object = Await selectCommand.ExecuteScalarAsync()
                If existing IsNot Nothing Then
                    Return CInt(existing)
                End If
            End Using

            Dim productId As Integer

            Using insertCommand As MySqlCommand = connection.CreateCommand()
                insertCommand.CommandText =
                    "INSERT INTO Products (Sku, Barcode, Name, Price, Cost, IsActive, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@sku, NULL, @name, 1.0000, 0.5000, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                insertCommand.Parameters.AddWithValue("@sku", FixtureProductSku)
                insertCommand.Parameters.AddWithValue("@name", HostileProductName)
                Await insertCommand.ExecuteNonQueryAsync()
                productId = CInt(insertCommand.LastInsertedId)
            End Using

            Using balanceCommand As MySqlCommand = connection.CreateCommand()
                balanceCommand.CommandText =
                    "INSERT INTO StockBalances (ProductId, Quantity, RowVersion, UpdatedAtUtc) " &
                    "VALUES (@productId, 0.000, 0, UTC_TIMESTAMP(6));"
                balanceCommand.Parameters.AddWithValue("@productId", productId)
                Await balanceCommand.ExecuteNonQueryAsync()
            End Using

            Return productId

        End Using

    End Function

    Private Shared Function LoadMigratorOptions() As DatabaseOptions
        Dim migratorConfigPath As String =
            Path.Combine(Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName)
        Return DatabaseOptionsLoader.Load(migratorConfigPath)
    End Function

    ''' <summary>Same repository-root marker every *DocumentationTests file in this suite uses.</summary>
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
            "'. This test writes evidence/phase-6/p6-06-excel-roundtrip/ into the working tree.")
        Return Nothing

    End Function

End Class
