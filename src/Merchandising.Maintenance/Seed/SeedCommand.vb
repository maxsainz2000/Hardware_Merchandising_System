' Merchandising.Maintenance.Seed.SeedCommand
'
' P2-11: one seed pass that takes a freshly migrated, empty database to a
' database a classmate can actually log into and look at - the five spec
' section 9 roles (already seeded by 0001_foundation.sql), one test account
' per role, a small product catalog spanning categories/brands/units, and a
' few suppliers.
'
' Re-runnable by design (ADR-007's spirit: idempotent by key, not by a
' "have I run before" flag). Every entity is matched on its own natural key
' before anything is written:
'   Categories/Brands/Units  - Name
'   Products                 - Sku (ProductRepository.InsertAsync already
'                               reports DuplicateSku as an outcome, not an
'                               exception - P2-07's concurrency-safe shape)
'   Suppliers                - Name (SupplierRepository.InsertAsync, same
'                               shape, P2-10)
'   Users                    - Username, checked before CreateUserCommand
'                               is ever called, so a second run neither
'                               errors nor reprints a password nobody asked
'                               to change.
'
' Passwords for the five test accounts are generated here, per installation,
' the same way bootstrap.ps1's own New-InstallationPassword works for the
' three database identities (ADR-012 requirement 6: no password known to the
' author may protect a classmate's demo, and nothing here is ever written to
' this repository). Program.vb is what prints/records them - this class only
' ever holds a plaintext password in memory, for the account it just created.
'
' Runs as merch_migrator, same identity as "migrate", "create-user" and
' "seed-demo": seeding is a host-side bootstrap operation, not something
' merch_api does for itself.

Imports System.Globalization
Imports System.IO
Imports System.Runtime.ExceptionServices
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports Merchandising.Domain.Entities
Imports Merchandising.Domain.Security
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports MySqlConnector

Namespace Seed

    Public NotInheritable Class SeedCommand

        Private Const DuplicateKeyErrorNumber As Integer = 1062

        ' Fixed, discoverable usernames - one per spec section 9 role. Order
        ' matches RoleNames' own declaration order.
        Private Shared ReadOnly RoleUsernames As (RoleName As String, Username As String)() = {
            (RoleNames.SuperAdmin, "superadmin"),
            (RoleNames.Admin, "admin"),
            (RoleNames.ProcurementOfficer, "procurementofficer"),
            (RoleNames.InventoryClerk, "inventoryclerk"),
            (RoleNames.Cashier, "cashier")
        }

        Public Shared Async Function RunAsync(connectionFactory As ConnectionFactory,
                                              seedFilePath As String,
                                              Optional cancellationToken As CancellationToken = Nothing) As Task(Of SeedResult)

            If connectionFactory Is Nothing Then
                Throw New ArgumentNullException(NameOf(connectionFactory))
            End If

            Dim data As SeedDataFile = LoadSeedFile(seedFilePath)

            Dim categoryIds As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
            Dim brandIds As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
            Dim unitIds As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)

            Dim categoriesCreated As Integer = 0
            Dim brandsCreated As Integer = 0
            Dim unitsCreated As Integer = 0
            Dim productsCreated As Integer = 0
            Dim productsSkipped As Integer = 0
            Dim suppliersCreated As Integer = 0
            Dim suppliersSkipped As Integer = 0

            Using connection As MySqlConnection = Await connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                For Each name As String In data.Categories
                    Dim outcome = Await GetOrCreateReferenceAsync(connection, "Categories", name, cancellationToken).ConfigureAwait(False)
                    categoryIds(name) = outcome.Id
                    If outcome.Created Then categoriesCreated += 1
                Next

                For Each name As String In data.Brands
                    Dim outcome = Await GetOrCreateReferenceAsync(connection, "Brands", name, cancellationToken).ConfigureAwait(False)
                    brandIds(name) = outcome.Id
                    If outcome.Created Then brandsCreated += 1
                Next

                For Each name As String In data.Units
                    Dim outcome = Await GetOrCreateReferenceAsync(connection, "Units", name, cancellationToken).ConfigureAwait(False)
                    unitIds(name) = outcome.Id
                    If outcome.Created Then unitsCreated += 1
                Next

                For Each definition As SeedProductDefinition In data.Products

                    If Not categoryIds.ContainsKey(definition.Category) Then
                        Throw New SeedCommandException($"Product '{definition.Sku}' references category '{definition.Category}', which is not in this file's categories list.")
                    End If
                    If Not brandIds.ContainsKey(definition.Brand) Then
                        Throw New SeedCommandException($"Product '{definition.Sku}' references brand '{definition.Brand}', which is not in this file's brands list.")
                    End If
                    If Not unitIds.ContainsKey(definition.Unit) Then
                        Throw New SeedCommandException($"Product '{definition.Sku}' references unit '{definition.Unit}', which is not in this file's units list.")
                    End If

                    Dim created As Boolean = Await CreateProductIfMissingAsync(
                        connection, definition, categoryIds(definition.Category), brandIds(definition.Brand), unitIds(definition.Unit),
                        cancellationToken).ConfigureAwait(False)

                    If created Then productsCreated += 1 Else productsSkipped += 1

                Next

                For Each definition As SeedSupplierDefinition In data.Suppliers

                    Dim created As Boolean = Await CreateSupplierIfMissingAsync(connection, definition, cancellationToken).ConfigureAwait(False)

                    If created Then suppliersCreated += 1 Else suppliersSkipped += 1

                Next

            End Using

            Dim usersCreated As New List(Of SeededUserCredential)
            Dim usersSkipped As Integer = 0

            Using connection As MySqlConnection = Await connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                For Each mapping In RoleUsernames

                    Dim existingUser = Await UserRepository.FindByUsernameAsync(connection, mapping.Username, cancellationToken).ConfigureAwait(False)

                    If existingUser IsNot Nothing Then
                        usersSkipped += 1
                    Else
                        Dim password As String = GenerateInstallationPassword()
                        Await CreateUserCommand.RunAsync(connectionFactory, mapping.Username, password, mapping.RoleName).ConfigureAwait(False)
                        usersCreated.Add(New SeededUserCredential(mapping.Username, password, mapping.RoleName))
                    End If

                Next

            End Using

            Return New SeedResult(
                categoriesCreated, data.Categories.Count - categoriesCreated,
                brandsCreated, data.Brands.Count - brandsCreated,
                unitsCreated, data.Units.Count - unitsCreated,
                productsCreated, productsSkipped,
                suppliersCreated, suppliersSkipped,
                usersCreated, usersSkipped)

        End Function

        Private Shared Function LoadSeedFile(path As String) As SeedDataFile

            If Not File.Exists(path) Then
                Throw New SeedCommandException($"Seed data file not found at '{path}'.")
            End If

            Dim json As String = File.ReadAllText(path)
            Dim jsonOptions As New JsonSerializerOptions With {.PropertyNameCaseInsensitive = True}

            Dim data As SeedDataFile = JsonSerializer.Deserialize(Of SeedDataFile)(json, jsonOptions)

            If data Is Nothing Then
                Throw New SeedCommandException($"Seed data file at '{path}' parsed to no data.")
            End If

            Return data

        End Function

        ''' <summary>
        ''' Categories/Brands/Units share the exact same shape (Id, Name,
        ''' CreatedAtUtc, UpdatedAtUtc), so one get-or-create routine serves
        ''' all three - tableName is never anything but a literal this file
        ''' passes in, never external input.
        ''' </summary>
        Private Shared Async Function GetOrCreateReferenceAsync(connection As MySqlConnection,
                                                                 tableName As String,
                                                                 name As String,
                                                                 cancellationToken As CancellationToken) As Task(Of (Id As Integer, Created As Boolean))

            Using selectCommand As MySqlCommand = connection.CreateCommand()
                selectCommand.CommandText = $"SELECT Id FROM {tableName} WHERE Name = @name;"
                selectCommand.Parameters.AddWithValue("@name", name)
                Dim existing As Object = Await selectCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False)
                If existing IsNot Nothing Then
                    Return (Id:=Convert.ToInt32(existing, CultureInfo.InvariantCulture), Created:=False)
                End If
            End Using

            ' VB cannot Await inside Catch (BC36943 - CLAUDE.md section 3's
            ' warning, the same trap SeedDemoCommand's own header calls out).
            ' The race-lost re-select therefore happens after the Try
            ' closes, driven by a flag, not inside the Catch itself.
            Dim lostRace As Boolean = False

            Using insertCommand As MySqlCommand = connection.CreateCommand()
                insertCommand.CommandText = $"INSERT INTO {tableName} (Name, CreatedAtUtc, UpdatedAtUtc) VALUES (@name, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                insertCommand.Parameters.AddWithValue("@name", name)

                Try
                    Await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                    Return (Id:=CInt(insertCommand.LastInsertedId), Created:=True)

                Catch ex As MySqlException When ex.Number = DuplicateKeyErrorNumber
                    lostRace = True
                End Try
            End Using

            ' Lost a race with another seed run between the SELECT and this
            ' INSERT - re-select rather than fail, the same "the database
            ' cannot be trusted under concurrency" precedent
            ' ProductRepository/SupplierRepository use.
            If lostRace Then
                Using reselectCommand As MySqlCommand = connection.CreateCommand()
                    reselectCommand.CommandText = $"SELECT Id FROM {tableName} WHERE Name = @name;"
                    reselectCommand.Parameters.AddWithValue("@name", name)
                    Dim resolved As Object = Await reselectCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False)
                    Return (Id:=Convert.ToInt32(resolved, CultureInfo.InvariantCulture), Created:=False)
                End Using
            End If

            Throw New SeedCommandException($"Insert into {tableName} for '{name}' failed without a recognised duplicate-key error.")

        End Function

        ''' <summary>
        ''' False means the product already existed - this call still makes
        ''' sure it carries a StockBalances row, so a database left half-
        ''' seeded by an earlier interrupted run finishes catching up rather
        ''' than being reported as fully seeded when it is not.
        ''' </summary>
        Private Shared Async Function CreateProductIfMissingAsync(connection As MySqlConnection,
                                                                   definition As SeedProductDefinition,
                                                                   categoryId As Integer,
                                                                   brandId As Integer,
                                                                   unitId As Integer,
                                                                   cancellationToken As CancellationToken) As Task(Of Boolean)

            Dim existing As Product = Await ProductRepository.FindBySkuAsync(connection, definition.Sku, cancellationToken).ConfigureAwait(False)

            If existing IsNot Nothing Then
                Await EnsureZeroBalanceAsync(connection, existing.Id, cancellationToken).ConfigureAwait(False)
                Return False
            End If

            Dim transaction As MySqlTransaction = Await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(False)
            Dim succeeded As Boolean = False
            Dim failure As Exception = Nothing

            Try
                Dim product As New Product With {
                    .Sku = definition.Sku,
                    .Name = definition.Name,
                    .CategoryId = categoryId,
                    .BrandId = brandId,
                    .UnitId = unitId,
                    .Price = definition.Price,
                    .Cost = definition.Cost,
                    .ReorderLevel = 0D
                }

                Dim insertResult = Await ProductRepository.InsertAsync(connection, transaction, product, cancellationToken).ConfigureAwait(False)

                If insertResult.Kind = ProductWriteOutcomeKind.Success Then
                    Await InsertZeroBalanceAsync(connection, transaction, insertResult.ProductId, cancellationToken).ConfigureAwait(False)
                    Await transaction.CommitAsync(cancellationToken).ConfigureAwait(False)
                    succeeded = True
                End If
                ' Kind <> Success means another run's INSERT won the race
                ' between our FindBySkuAsync check and this one - nothing to
                ' commit, and the winner's own call already wrote the
                ' balance row.

            Catch ex As Exception
                failure = ex
            End Try

            If Not succeeded Then
                Try
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                Catch rollbackFailure As MySqlException
                    ' Nothing left to roll back if nothing ever took effect.
                End Try
            End If

            Await transaction.DisposeAsync().ConfigureAwait(False)

            If failure IsNot Nothing Then
                ExceptionDispatchInfo.Capture(failure).Throw()
            End If

            Return succeeded

        End Function

        Private Shared Async Function CreateSupplierIfMissingAsync(connection As MySqlConnection,
                                                                    definition As SeedSupplierDefinition,
                                                                    cancellationToken As CancellationToken) As Task(Of Boolean)

            Dim existing As Supplier = Await SupplierRepository.FindByNameAsync(connection, definition.Name, cancellationToken).ConfigureAwait(False)

            If existing IsNot Nothing Then
                Return False
            End If

            Dim transaction As MySqlTransaction = Await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(False)
            Dim succeeded As Boolean = False
            Dim failure As Exception = Nothing

            Try
                Dim supplier As New Supplier With {
                    .Name = definition.Name,
                    .ContactName = definition.ContactName,
                    .Phone = definition.Phone,
                    .Email = definition.Email,
                    .Address = definition.Address
                }

                Dim insertResult = Await SupplierRepository.InsertAsync(connection, transaction, supplier, cancellationToken).ConfigureAwait(False)

                If insertResult.Kind = SupplierWriteOutcomeKind.Success Then
                    Await transaction.CommitAsync(cancellationToken).ConfigureAwait(False)
                    succeeded = True
                End If

            Catch ex As Exception
                failure = ex
            End Try

            If Not succeeded Then
                Try
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                Catch rollbackFailure As MySqlException
                End Try
            End If

            Await transaction.DisposeAsync().ConfigureAwait(False)

            If failure IsNot Nothing Then
                ExceptionDispatchInfo.Capture(failure).Throw()
            End If

            Return succeeded

        End Function

        ''' <summary>
        ''' A zero balance is establishing the ledger's starting point, not a
        ''' stock-changing operation (CLAUDE.md section 5's conditional-
        ''' update/movement/audit triad governs a CHANGE in quantity) - so no
        ''' StockMovements row is written here, the same reasoning
        ''' SeedDemoCommand.InsertZeroBalanceAsync already documents.
        ''' </summary>
        Private Shared Async Function EnsureZeroBalanceAsync(connection As MySqlConnection, productId As Integer, cancellationToken As CancellationToken) As Task

            Using checkCommand As MySqlCommand = connection.CreateCommand()
                checkCommand.CommandText = "SELECT 1 FROM StockBalances WHERE ProductId = @productId;"
                checkCommand.Parameters.AddWithValue("@productId", productId)
                Dim exists As Object = Await checkCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False)
                If exists IsNot Nothing Then
                    Return
                End If
            End Using

            Using insertCommand As MySqlCommand = connection.CreateCommand()
                insertCommand.CommandText =
                    "INSERT INTO StockBalances (ProductId, Quantity, RowVersion, UpdatedAtUtc) " &
                    "VALUES (@productId, 0.000, 0, UTC_TIMESTAMP(6));"
                insertCommand.Parameters.AddWithValue("@productId", productId)

                Try
                    Await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Catch ex As MySqlException When ex.Number = DuplicateKeyErrorNumber
                    ' Race lost against a concurrent seed run; a balance row
                    ' exists either way, which is all this helper promises.
                End Try
            End Using

        End Function

        Private Shared Async Function InsertZeroBalanceAsync(connection As MySqlConnection,
                                                              transaction As MySqlTransaction,
                                                              productId As Integer,
                                                              cancellationToken As CancellationToken) As Task

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO StockBalances (ProductId, Quantity, RowVersion, UpdatedAtUtc) " &
                    "VALUES (@productId, 0.000, 0, UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@productId", productId)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
            End Using

        End Function

        ''' <summary>
        ''' Same alphabet and byte count as bootstrap.ps1's own
        ''' New-InstallationPassword: letters and digits only (this value is
        ''' bound as a MySqlConnector parameter, never interpolated into SQL
        ''' text, but it is echoed to a console and a text file, where an
        ''' unescaped quote is just an annoyance worth avoiding). 24 bytes of
        ''' a 62-character alphabet is far past anything that matters for an
        ''' account that only ever authenticates over this system's own API.
        ''' </summary>
        Private Shared Function GenerateInstallationPassword() As String

            Const Alphabet As String = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789"

            Dim bytes(23) As Byte
            Using rng As RandomNumberGenerator = RandomNumberGenerator.Create()
                rng.GetBytes(bytes)
            End Using

            Dim builder As New StringBuilder(bytes.Length)
            For Each b As Byte In bytes
                builder.Append(Alphabet(b Mod Alphabet.Length))
            Next

            Return builder.ToString()

        End Function

    End Class

End Namespace
