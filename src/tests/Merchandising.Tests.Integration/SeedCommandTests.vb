' Merchandising.Tests.Integration.SeedCommandTests
'
' P2-11 evidence: Merchandising.Maintenance.Seed.SeedCommand against the
' real, pinned MariaDB instance (ADR-000, ADR-002, ADR-009) - never a
' substitute.
'
' Two things are proven here, deliberately kept separate:
'
'   1. The PRODUCTION file (db/seed/seed-data.json) actually loads and is
'      idempotent - the exact file bootstrap.ps1 runs against a clean
'      install. Run twice inside SetUpAsync/the first test; the SECOND call
'      in each test method must report zero creates across every category.
'
'   2. The get-or-create MECHANICS (new row on first sight, no duplicate on
'      a repeat) are proven against an isolated, disposable fixture file -
'      not the production one - because production catalog rows have no
'      DELETE grant for merch_api and are meant to be permanent (ADR-013),
'      so a test cannot clean up rows created against the real file the way
'      MigrationRunnerTests cleans up its own "migtest_" tables.
'
' The five fixed role usernames (superadmin/admin/procurementofficer/
' inventoryclerk/cashier) are asserted for EXISTENCE only, not for which
' role each currently carries - this machine's Users table already carried
' an "admin" row from before this card existed (see the task's own evidence
' log), and a fresh clone would never have that collision. Existence is the
' thing every Done-when box actually asks for ("reaches a working login
' without manual SQL").

Imports System.IO
Imports System.Runtime.ExceptionServices
Imports System.Text.Json
Imports System.Threading.Tasks
Imports Merchandising.Domain.Security
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Seed
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class SeedCommandTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"

    Private _connectionFactory As ConnectionFactory
    Private _productionSeedFilePath As String

    <TestInitialize>
    Public Sub SetUp()

        _connectionFactory = New ConnectionFactory(LoadMigratorOptions())
        _productionSeedFilePath = Path.Combine(FindRepositoryRoot().FullName, "db", "seed", "seed-data.json")

        Assert.IsTrue(File.Exists(_productionSeedFilePath), $"db/seed/seed-data.json is missing at '{_productionSeedFilePath}'.")

    End Sub

    ''' <summary>Done-when box 1: the production file loads and completes without error.</summary>
    <TestMethod>
    Public Async Function RunAsync_ProductionSeedFile_CompletesWithoutError() As Task

        Dim result As SeedResult = Await SeedCommand.RunAsync(_connectionFactory, _productionSeedFilePath)

        Assert.IsNotNull(result)

    End Function

    ''' <summary>
    ''' Done-when box 2: running it TWICE does not duplicate rows or fail.
    ''' First call may create or may find everything already there
    ''' (depends what earlier runs on this machine already did); the second
    ''' call, against the identical file and now-populated database, must
    ''' report zero creates everywhere.
    ''' </summary>
    <TestMethod>
    Public Async Function RunAsync_ProductionSeedFile_SecondRunCreatesNothing() As Task

        Await SeedCommand.RunAsync(_connectionFactory, _productionSeedFilePath)

        Dim second As SeedResult = Await SeedCommand.RunAsync(_connectionFactory, _productionSeedFilePath)

        Assert.AreEqual(0, second.CategoriesCreated)
        Assert.AreEqual(0, second.BrandsCreated)
        Assert.AreEqual(0, second.UnitsCreated)
        Assert.AreEqual(0, second.ProductsCreated)
        Assert.AreEqual(0, second.SuppliersCreated)
        Assert.IsEmpty(second.UsersCreated)

    End Function

    ''' <summary>Done-when box 5: a clean-clone run reaches a working login - the five role accounts exist and are active.</summary>
    <TestMethod>
    Public Async Function RunAsync_ProductionSeedFile_AllFiveRoleAccountsExistAndAreActive() As Task

        Await SeedCommand.RunAsync(_connectionFactory, _productionSeedFilePath)

        Dim expectedUsernames As String() = {"superadmin", "admin", "procurementofficer", "inventoryclerk", "cashier"}

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            For Each username As String In expectedUsernames

                Dim user = Await UserRepository.FindByUsernameAsync(connection, username)
                Assert.IsNotNull(user, $"Expected a seeded account named '{username}'.")
                Assert.IsTrue(user.IsActive, $"'{username}' exists but is not active.")

            Next

        End Using

    End Function

    ''' <summary>
    ''' Get-or-create mechanics, isolated from production data: a disposable
    ''' fixture file with names no other test/card uses. First run creates
    ''' exactly one row per entity; second run against the same file, same
    ''' database, creates none and leaves exactly one row per entity - the
    ''' literal "does not duplicate rows" proof.
    ''' </summary>
    <TestMethod>
    Public Async Function RunAsync_FixtureFile_SecondRunSkipsWithoutDuplicating() As Task

        Dim suffix As String = Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim categoryName As String = $"p2_11_fixture_category_{suffix}"
        Dim brandName As String = $"p2_11_fixture_brand_{suffix}"
        Dim unitName As String = $"p2_11_fixture_unit_{suffix}"
        Dim supplierName As String = $"p2_11_fixture_supplier_{suffix}"
        Dim sku As String = $"P2-11-FIXTURE-{suffix}"

        Dim fixture As New SeedDataFile With {
            .Categories = New List(Of String) From {categoryName},
            .Brands = New List(Of String) From {brandName},
            .Units = New List(Of String) From {unitName},
            .Products = New List(Of SeedProductDefinition) From {
                New SeedProductDefinition With {
                    .Sku = sku,
                    .Name = $"P2-11 fixture product {suffix}",
                    .Category = categoryName,
                    .Brand = brandName,
                    .Unit = unitName,
                    .Price = 100D,
                    .Cost = 60D
                }
            },
            .Suppliers = New List(Of SeedSupplierDefinition) From {
                New SeedSupplierDefinition With {.Name = supplierName}
            }
        }

        Dim fixtureFilePath As String = Path.Combine(Path.GetTempPath(), $"p2_11_seed_fixture_{suffix}.json")
        File.WriteAllText(fixtureFilePath, JsonSerializer.Serialize(fixture))

        ' VB cannot Await inside Finally (BC36943 - CLAUDE.md section 3's
        ' warning). Cleanup that needs Await therefore runs after the Try
        ' closes, driven by a captured failure, not inside Finally itself -
        ' the same shape SeedCommand.vb's own transaction handling uses.
        Dim testFailure As Exception = Nothing

        Try

            Dim first As SeedResult = Await SeedCommand.RunAsync(_connectionFactory, fixtureFilePath)

            Assert.AreEqual(1, first.CategoriesCreated)
            Assert.AreEqual(1, first.BrandsCreated)
            Assert.AreEqual(1, first.UnitsCreated)
            Assert.AreEqual(1, first.ProductsCreated)
            Assert.AreEqual(1, first.SuppliersCreated)

            Dim second As SeedResult = Await SeedCommand.RunAsync(_connectionFactory, fixtureFilePath)

            Assert.AreEqual(0, second.CategoriesCreated)
            Assert.AreEqual(0, second.BrandsCreated)
            Assert.AreEqual(0, second.UnitsCreated)
            Assert.AreEqual(0, second.ProductsCreated)
            Assert.AreEqual(0, second.SuppliersCreated)
            Assert.AreEqual(1, second.CategoriesSkipped)
            Assert.AreEqual(1, second.ProductsSkipped)
            Assert.AreEqual(1, second.SuppliersSkipped)

            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

                Assert.AreEqual(1L, Await CountRowsAsync(connection, "Categories", "Name", categoryName))
                Assert.AreEqual(1L, Await CountRowsAsync(connection, "Products", "Sku", sku))
                Assert.AreEqual(1L, Await CountRowsAsync(connection, "Suppliers", "Name", supplierName))

            End Using

        Catch ex As Exception
            testFailure = ex
        End Try

        File.Delete(fixtureFilePath)

        ' Categories/Brands/Units/Products/Suppliers carry no DELETE grant
        ' for merch_api (ADR-013 - deactivation is the only removal story
        ' for the real application), but this connection is merch_migrator,
        ' which owns full DML on the whole schema. Cleaning up here is what
        ' keeps a fixture row out of the real demo catalog forever - without
        ' it, every test run would add one more "p2_11_fixture_..." entry
        ' that no test ever removes.
        Await DeleteFixtureRowsAsync(sku, categoryName, brandName, unitName, supplierName)

        If testFailure IsNot Nothing Then
            ExceptionDispatchInfo.Capture(testFailure).Throw()
        End If

    End Function

    ''' <summary>A product naming a category the same file never lists is an operator mistake, not a database error.</summary>
    <TestMethod>
    Public Async Function RunAsync_ProductReferencesUnknownCategory_ThrowsSeedCommandException() As Task

        Dim suffix As String = Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim brandName As String = $"p2_11_fixture_brand_{suffix}"
        Dim unitName As String = $"p2_11_fixture_unit_{suffix}"

        Dim fixture As New SeedDataFile With {
            .Categories = New List(Of String)(),
            .Brands = New List(Of String) From {brandName},
            .Units = New List(Of String) From {unitName},
            .Products = New List(Of SeedProductDefinition) From {
                New SeedProductDefinition With {
                    .Sku = $"P2-11-BAD-{suffix}",
                    .Name = "Bad fixture product",
                    .Category = "No Such Category",
                    .Brand = brandName,
                    .Unit = unitName,
                    .Price = 1D,
                    .Cost = 1D
                }
            },
            .Suppliers = New List(Of SeedSupplierDefinition)()
        }

        Dim fixtureFilePath As String = Path.Combine(Path.GetTempPath(), $"p2_11_seed_bad_fixture_{suffix}.json")
        File.WriteAllText(fixtureFilePath, JsonSerializer.Serialize(fixture))

        Dim testFailure As Exception = Nothing

        Try

            Await Assert.ThrowsExactlyAsync(Of SeedCommandException)(
                Function() SeedCommand.RunAsync(_connectionFactory, fixtureFilePath))

        Catch ex As Exception
            testFailure = ex
        End Try

        File.Delete(fixtureFilePath)

        ' The Brands/Units loops run - and commit - BEFORE the Products loop
        ' ever validates the category reference, so both rows exist even
        ' though the run as a whole threw. Same cleanup reasoning as the
        ' test above.
        Await DeleteFixtureRowsAsync(sku:=Nothing, categoryName:=Nothing, brandName:=brandName, unitName:=unitName, supplierName:=Nothing)

        If testFailure IsNot Nothing Then
            ExceptionDispatchInfo.Capture(testFailure).Throw()
        End If

    End Function

    ' --------------------------------------------------------------- shared helpers

    ''' <summary>
    ''' Removes exactly the rows a fixture-file test created, in FK-safe
    ''' order (balance/product before the catalog rows they reference). Any
    ''' parameter left Nothing is skipped - callers pass only the names their
    ''' own fixture actually created.
    ''' </summary>
    Private Async Function DeleteFixtureRowsAsync(sku As String, categoryName As String, brandName As String, unitName As String, supplierName As String) As Task

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()

            If sku IsNot Nothing Then
                Using balanceCommand As MySqlCommand = connection.CreateCommand()
                    balanceCommand.CommandText = "DELETE sb FROM StockBalances sb JOIN Products p ON p.Id = sb.ProductId WHERE p.Sku = @sku;"
                    balanceCommand.Parameters.AddWithValue("@sku", sku)
                    Await balanceCommand.ExecuteNonQueryAsync()
                End Using
                Using productCommand As MySqlCommand = connection.CreateCommand()
                    productCommand.CommandText = "DELETE FROM Products WHERE Sku = @sku;"
                    productCommand.Parameters.AddWithValue("@sku", sku)
                    Await productCommand.ExecuteNonQueryAsync()
                End Using
            End If

            Await DeleteByNameAsync(connection, "Categories", categoryName)
            Await DeleteByNameAsync(connection, "Brands", brandName)
            Await DeleteByNameAsync(connection, "Units", unitName)
            Await DeleteByNameAsync(connection, "Suppliers", supplierName)

        End Using

    End Function

    Private Shared Async Function DeleteByNameAsync(connection As MySqlConnection, tableName As String, name As String) As Task

        If name Is Nothing Then
            Return
        End If

        Using command As MySqlCommand = connection.CreateCommand()
            command.CommandText = $"DELETE FROM {tableName} WHERE Name = @name;"
            command.Parameters.AddWithValue("@name", name)
            Await command.ExecuteNonQueryAsync()
        End Using

    End Function

    Private Shared Async Function CountRowsAsync(connection As MySqlConnection, tableName As String, columnName As String, value As String) As Task(Of Long)

        Using command As MySqlCommand = connection.CreateCommand()
            command.CommandText = $"SELECT COUNT(*) FROM {tableName} WHERE {columnName} = @value;"
            command.Parameters.AddWithValue("@value", value)
            Return CLng(Await command.ExecuteScalarAsync())
        End Using

    End Function

    ''' <summary>Same marker/walk-up as WindowsServiceInfoTests' own FindRepositoryRoot - CLAUDE.md at the repository root.</summary>
    Private Shared Function FindRepositoryRoot() As DirectoryInfo

        Dim current As DirectoryInfo = New DirectoryInfo(AppContext.BaseDirectory)

        While current IsNot Nothing
            If File.Exists(Path.Combine(current.FullName, "CLAUDE.md")) Then
                Return current
            End If
            current = current.Parent
        End While

        Assert.Fail($"Could not locate the repository root above '{AppContext.BaseDirectory}'. This test reads db/seed/seed-data.json from the working tree.")
        Return Nothing

    End Function

    Private Shared Function LoadMigratorOptions() As DatabaseOptions
        Dim migratorConfigPath As String =
            Path.Combine(Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName)
        Return DatabaseOptionsLoader.Load(migratorConfigPath)
    End Function

End Class
