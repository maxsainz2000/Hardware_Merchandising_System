' Merchandising.Tests.Integration.DatabaseDesignDocumentationTests
'
' P4-13: docs/database-design.md must be "verified against the running
' database rather than transcribed by hand, in the P2-12 / P3-08 shape - a
' drift check that fails when the schema moves." RolePermissionMatrixDocumentationTests
' achieves that for a pure-data document by regenerating it byte-for-byte;
' ApiSpecificationDocumentationTests achieves it for a prose document by
' reflecting the live controller and asserting facts appear in the text. The
' database design document is prose and tables around a full schema, so this
' file takes the same shape ApiSpecificationDocumentationTests does: read the
' real information_schema catalogue rather than any migration file (a
' migration is what was ASKED for; information_schema is what the server is
' actually running - CLAUDE.md section 6.3's whole point), and assert every
' fact the document claims still holds, plus two schema-wide invariants
' (CLAUDE.md section 5's decimal precision and section 6.2's collation) that
' have nothing to do with the document at all but are exactly the kind of
' thing that should fail loudly the moment a new migration violates them.
'
' A table added to or removed from the schema without updating
' CanonicalTables fails here first. A table added to CanonicalTables without
' updating docs/database-design.md fails second. A decimal column declared
' at any scale other than the two ADR-004 pins, a timestamp not DATETIME(6),
' a table not COLLATE utf8mb4_unicode_ci, a foreign key with any delete rule
' other than RESTRICT, or a merch_api grant that drifts from what section 4
' documents - all of these fail this suite rather than shipping a stale
' document.

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks
Imports Merchandising.Infrastructure.Data
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class DatabaseDesignDocumentationTests

    ''' <summary>
    ''' Walks up from the test assembly until it finds the repository root,
    ''' identified by <c>CLAUDE.md</c> - same marker
    ''' ApiSpecificationDocumentationTests.FindRepositoryRoot and
    ''' RolePermissionMatrixDocumentationTests.FindRepositoryRoot use.
    ''' </summary>
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
            "'. This test reads docs/database-design.md from the working tree.")
        Return Nothing

    End Function

    Private Shared Function ReadDocument() As String
        Dim documentPath As String =
            Path.Combine(FindRepositoryRoot().FullName, "docs", "database-design.md")

        Assert.IsTrue(
            File.Exists(documentPath),
            "docs/database-design.md is missing. P4-13 owes a finalised document, not a planned one.")

        Return File.ReadAllText(documentPath)
    End Function

    Private Shared Function OpenApiConnectionAsync() As Task(Of MySqlConnection)
        Dim factory As New ConnectionFactory(DatabaseOptionsLoader.Load())
        Return factory.CreateOpenConnectionAsync()
    End Function

    ''' <summary>
    ''' Every table this schema carries through migration 0010, lowercase (as
    ''' MariaDB stores it under @@lower_case_table_names = 1, ADR-003.1)
    ''' mapped to the PascalCase name docs/database-design.md's data
    ''' dictionary writes it under. This is the one place in this file that
    ''' must be hand-maintained - a table added to the schema without adding
    ''' it here fails <see cref="LiveSchema_MatchesTheCanonicalTableList"/>
    ''' before it can silently go undocumented.
    ''' </summary>
    Private Shared Function CanonicalTables() As IReadOnlyDictionary(Of String, String)
        Return New Dictionary(Of String, String) From {
            {"users", "Users"}, {"roles", "Roles"}, {"userroles", "UserRoles"},
            {"sessions", "Sessions"}, {"permissionpolicies", "PermissionPolicies"},
            {"auditlogs", "AuditLogs"},
            {"products", "Products"}, {"categories", "Categories"}, {"brands", "Brands"},
            {"units", "Units"}, {"productbarcodes", "ProductBarcodes"},
            {"pricehistory", "PriceHistory"}, {"stockbalances", "StockBalances"},
            {"stockmovements", "StockMovements"},
            {"systemsettings", "SystemSettings"}, {"schemamigrations", "SchemaMigrations"},
            {"backuplogs", "BackupLogs"}, {"maintenancelocks", "MaintenanceLocks"},
            {"idempotencykeys", "IdempotencyKeys"},
            {"suppliers", "Suppliers"}, {"purchaseorders", "PurchaseOrders"},
            {"purchaseorderlines", "PurchaseOrderLines"},
            {"receipts", "Receipts"}, {"receiptlines", "ReceiptLines"},
            {"purchasereturns", "PurchaseReturns"}, {"purchasereturnlines", "PurchaseReturnLines"},
            {"stockcounts", "StockCounts"}, {"stockcountlines", "StockCountLines"},
            {"stockadjustments", "StockAdjustments"}
        }
    End Function

    Private Shared Async Function LiveTableNamesAsync() As Task(Of IReadOnlyList(Of String))

        Dim names As New List(Of String)

        Using connection As MySqlConnection = Await OpenApiConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT LOWER(TABLE_NAME) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE();"
                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync()
                    While Await reader.ReadAsync()
                        names.Add(reader.GetString(0))
                    End While
                End Using
            End Using
        End Using

        Return names

    End Function

    ' =========================================================================
    ' Every table exists and is exactly the documented inventory - no fewer, no more.
    ' =========================================================================

    <TestMethod>
    Public Async Function LiveSchema_MatchesTheCanonicalTableList() As Task

        Dim live As IReadOnlyList(Of String) = Await LiveTableNamesAsync()
        Dim canonical As IReadOnlyList(Of String) = CanonicalTables().Keys.ToList()

        Dim undocumented As String() = live.Except(canonical).ToArray()
        Dim phantom As String() = canonical.Except(live).ToArray()

        Assert.IsEmpty(undocumented,
            $"Table(s) exist in the running schema with no entry in this test's CanonicalTables: {String.Join(", ", undocumented)}. " &
            "Add them here AND to docs/database-design.md before this test can pass.")

        Assert.IsEmpty(phantom,
            $"CanonicalTables names table(s) that no longer exist in the running schema: {String.Join(", ", phantom)}.")

    End Function

    <TestMethod>
    Public Sub EveryCanonicalTable_IsNamedInTheDocument()

        Dim document As String = ReadDocument()

        For Each entry As KeyValuePair(Of String, String) In CanonicalTables()

            Dim marker As String = "`" & entry.Value & "`"

            StringAssert.Contains(document, marker,
                $"docs/database-design.md never mentions '{marker}' - every table through migration 0010 " &
                "must appear in the data dictionary.")

        Next

    End Sub

    ' =========================================================================
    ' Schema-wide invariants CLAUDE.md section 5/6 states for every table, not
    ' just the ones a per-migration schema-test file happened to check.
    ' =========================================================================

    ''' <summary>ADR-004: money is DECIMAL(19,4), quantities are DECIMAL(19,3). Nothing else, anywhere.</summary>
    <TestMethod>
    Public Async Function AllDecimalColumns_AreDeclaredAtOneOfTheTwoPinnedScales() As Task

        Dim offenders As New List(Of String)

        Using connection As MySqlConnection = Await OpenApiConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE FROM information_schema.COLUMNS " &
                    "WHERE TABLE_SCHEMA = DATABASE() AND DATA_TYPE = 'decimal';"
                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync()
                    While Await reader.ReadAsync()
                        Dim columnType As String = reader.GetString(2)
                        If columnType <> "decimal(19,4)" AndAlso columnType <> "decimal(19,3)" Then
                            offenders.Add($"{reader.GetString(0)}.{reader.GetString(1)} is {columnType}")
                        End If
                    End While
                End Using
            End Using
        End Using

        Assert.IsEmpty(offenders,
            "Decimal column(s) declared outside the two ADR-004 pins (19,4 money / 19,3 quantity): " &
            String.Join("; ", offenders))

    End Function

    ''' <summary>CLAUDE.md section 5: all timestamps DATETIME(6), always UTC in storage.</summary>
    <TestMethod>
    Public Async Function AllDatetimeColumns_AreDeclaredWithMicrosecondPrecision() As Task

        Dim offenders As New List(Of String)

        Using connection As MySqlConnection = Await OpenApiConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE FROM information_schema.COLUMNS " &
                    "WHERE TABLE_SCHEMA = DATABASE() AND DATA_TYPE = 'datetime';"
                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync()
                    While Await reader.ReadAsync()
                        Dim columnType As String = reader.GetString(2)
                        If columnType <> "datetime(6)" Then
                            offenders.Add($"{reader.GetString(0)}.{reader.GetString(1)} is {columnType}")
                        End If
                    End While
                End Using
            End Using
        End Using

        Assert.IsEmpty(offenders,
            "Datetime column(s) not declared DATETIME(6): " & String.Join("; ", offenders))

    End Function

    ''' <summary>ADR-003: every table states utf8mb4_unicode_ci explicitly, never inherits the server's utf8mb4_general_ci default.</summary>
    <TestMethod>
    Public Async Function EveryTable_UsesUtf8mb4UnicodeCiCollation() As Task

        Dim offenders As New List(Of String)

        Using connection As MySqlConnection = Await OpenApiConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT TABLE_NAME, TABLE_COLLATION FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE();"
                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync()
                    While Await reader.ReadAsync()
                        If reader.GetString(1) <> "utf8mb4_unicode_ci" Then
                            offenders.Add($"{reader.GetString(0)} is {reader.GetString(1)}")
                        End If
                    End While
                End Using
            End Using
        End Using

        Assert.IsEmpty(offenders,
            "Table(s) not declared utf8mb4_unicode_ci: " & String.Join("; ", offenders))

    End Function

    ''' <summary>
    ''' Spec section 12: "Foreign-key behavior must prevent accidental
    ''' deletion of records referenced by sales, receipts, returns, movements,
    ''' or audit events." Every foreign key in this schema is an unadorned FK
    ''' (no ON DELETE clause), so MariaDB's default RESTRICT applies uniformly
    ''' - this test proves that is still true of literally every one, not
    ''' just the handful earlier schema-test files happened to name.
    ''' </summary>
    <TestMethod>
    Public Async Function EveryForeignKey_IsRestrictOnDeleteAndUpdate() As Task

        Dim offenders As New List(Of String)
        Dim total As Integer = 0

        Using connection As MySqlConnection = Await OpenApiConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT CONSTRAINT_NAME, TABLE_NAME, DELETE_RULE, UPDATE_RULE " &
                    "FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA = DATABASE();"
                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync()
                    While Await reader.ReadAsync()
                        total += 1
                        Dim deleteRule As String = reader.GetString(2)
                        Dim updateRule As String = reader.GetString(3)
                        If deleteRule <> "RESTRICT" OrElse updateRule <> "RESTRICT" Then
                            offenders.Add($"{reader.GetString(1)}.{reader.GetString(0)} DELETE={deleteRule} UPDATE={updateRule}")
                        End If
                    End While
                End Using
            End Using
        End Using

        Assert.IsGreaterThan(0, total, "No foreign keys were found at all - the query itself is broken.")
        Assert.IsEmpty(offenders,
            "Foreign key(s) not RESTRICT on both DELETE and UPDATE: " & String.Join("; ", offenders))

    End Function

    ' =========================================================================
    ' Section 4's per-table grant list, checked against merch_api's ACTUAL
    ' privileges rather than the db/grants/*.sql files that were merely run.
    ' =========================================================================

    ''' <summary>
    ''' One row per table this schema carries, exactly the privileges
    ''' section 4's grant table documents for merch_api. Kept here rather than
    ''' parsed out of the markdown table for the same reason
    ''' ApiSpecificationDocumentationTests.ExpectedRoutes is a hard-coded list
    ''' rather than a markdown parser: a privilege silently regranted (or
    ''' revoked) without updating BOTH this list and section 4 must fail
    ''' loudly, and a parser that tolerated reformatting the markdown table
    ''' would hide exactly that drift.
    ''' </summary>
    Private Shared Function ExpectedGrants() As IReadOnlyDictionary(Of String, ISet(Of String))
        Dim none As ISet(Of String) = New HashSet(Of String)()
        Return New Dictionary(Of String, ISet(Of String)) From {
            {"stockmovements", New HashSet(Of String) From {"INSERT"}},
            {"auditlogs", New HashSet(Of String) From {"INSERT"}},
            {"pricehistory", New HashSet(Of String) From {"INSERT"}},
            {"backuplogs", none},
            {"schemamigrations", none},
            {"permissionpolicies", none},
            {"users", New HashSet(Of String) From {"INSERT", "UPDATE"}},
            {"roles", New HashSet(Of String) From {"INSERT", "UPDATE"}},
            {"userroles", New HashSet(Of String) From {"INSERT", "DELETE"}},
            {"sessions", New HashSet(Of String) From {"INSERT", "DELETE"}},
            {"products", New HashSet(Of String) From {"INSERT", "UPDATE"}},
            {"categories", New HashSet(Of String) From {"INSERT", "UPDATE"}},
            {"brands", New HashSet(Of String) From {"INSERT", "UPDATE"}},
            {"units", New HashSet(Of String) From {"INSERT", "UPDATE"}},
            {"productbarcodes", New HashSet(Of String) From {"INSERT", "UPDATE", "DELETE"}},
            {"stockbalances", New HashSet(Of String) From {"INSERT", "UPDATE"}},
            {"systemsettings", New HashSet(Of String) From {"INSERT", "UPDATE"}},
            {"suppliers", New HashSet(Of String) From {"INSERT", "UPDATE"}},
            {"maintenancelocks", New HashSet(Of String) From {"INSERT", "UPDATE"}},
            {"idempotencykeys", New HashSet(Of String) From {"INSERT", "UPDATE", "DELETE"}},
            {"purchaseorders", New HashSet(Of String) From {"INSERT", "UPDATE"}},
            {"purchaseorderlines", New HashSet(Of String) From {"INSERT", "UPDATE"}},
            {"receipts", New HashSet(Of String) From {"INSERT"}},
            {"receiptlines", New HashSet(Of String) From {"INSERT"}},
            {"purchasereturns", New HashSet(Of String) From {"INSERT", "UPDATE"}},
            {"purchasereturnlines", New HashSet(Of String) From {"INSERT"}},
            {"stockcounts", New HashSet(Of String) From {"INSERT", "UPDATE"}},
            {"stockcountlines", New HashSet(Of String) From {"INSERT"}},
            {"stockadjustments", New HashSet(Of String) From {"INSERT", "UPDATE"}}
        }
    End Function

    <TestMethod>
    Public Async Function MerchApiGrants_MatchTheDocumentedPostureForEveryTable() As Task

        Dim actual As New Dictionary(Of String, ISet(Of String))

        Using connection As MySqlConnection = Await OpenApiConnectionAsync()
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT LOWER(TABLE_NAME), PRIVILEGE_TYPE FROM information_schema.TABLE_PRIVILEGES " &
                    "WHERE GRANTEE LIKE '%merch_api%' AND TABLE_SCHEMA = DATABASE();"
                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync()
                    While Await reader.ReadAsync()
                        Dim tableName As String = reader.GetString(0)
                        If Not actual.ContainsKey(tableName) Then
                            actual(tableName) = New HashSet(Of String)
                        End If
                        actual(tableName).Add(reader.GetString(1))
                    End While
                End Using
            End Using
        End Using

        For Each expected As KeyValuePair(Of String, ISet(Of String)) In ExpectedGrants()

            Dim grantedHere As ISet(Of String) = If(actual.ContainsKey(expected.Key), actual(expected.Key), New HashSet(Of String))

            Dim missing As String() = expected.Value.Except(grantedHere).ToArray()
            Dim extra As String() = grantedHere.Except(expected.Value).ToArray()

            Assert.IsEmpty(missing,
                $"'{expected.Key}' is missing documented grant(s) for merch_api: {String.Join(", ", missing)}.")
            Assert.IsEmpty(extra,
                $"'{expected.Key}' grants merch_api undocumented privilege(s): {String.Join(", ", extra)}.")

        Next

    End Function

End Class
