' Merchandising.Infrastructure.Data.ProductRepository
'
' P2-07: Create/read/update/search against Products, plus the pre-checks
' ProductsController uses for a usable duplicate-SKU/barcode error message.
' The pre-checks are a courtesy, not the guarantee - InsertAsync/UpdateAsync
' below are what actually enforce uniqueness, by catching the real unique-
' index violation (ERROR 1062) from UQ_Products_Sku / UQ_Products_
' ActiveBarcode (0006_product-master.sql, ADR-018), the same "the database
' cannot be trusted... the API cannot be trusted under concurrency" shape
' MaintenanceLockRepository/IdempotencyStore already use for their own
' unique constraints. A caller that skips the pre-check entirely (as the
' concurrency proof does, deliberately) still cannot create two rows.
'
' P5-06 REWRITES SearchAsync into two phases (SearchExactAsync then, only if
' that finds nothing, SearchPartialAsync) - spec section 10.3's "exact SKU,
' exact barcode, or partial name" lookup, with exact taking priority so a
' scanned barcode never comes back diluted by an unrelated partial-name
' match (ADR-018's rule - Products.Barcode is the one field this searches -
' stands unmodified). Both phases now join StockBalances so every hit
' carries its current available stock, and both honour a ProductSortField
' the caller passes rather than a fixed ORDER BY Name.

Imports System.Threading
Imports System.Threading.Tasks
Imports Merchandising.Domain.Entities
Imports MySqlConnector

Namespace Data

    Public Enum ProductWriteOutcomeKind
        Success
        DuplicateSku
        DuplicateBarcode
        NotFound
    End Enum

    ''' <summary>The sort fields <see cref="ProductRepository.SearchAsync"/> will honour - the same closed-enum shape StockRepository's StockSortField uses, so no caller-supplied text ever reaches an ORDER BY.</summary>
    Public Enum ProductSortField
        Name
        Sku
    End Enum

    Public NotInheritable Class ProductRepository

        Private Const DuplicateKeyErrorNumber As Integer = 1062

        ''' <summary>Exact-match lookup by SKU, any active state. Nothing if no such SKU exists.</summary>
        Public Shared Async Function FindBySkuAsync(
            connection As MySqlConnection,
            sku As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Product)

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = SelectColumns & " FROM Products WHERE Sku = @sku;"
                command.Parameters.AddWithValue("@sku", sku)
                Return Await ReadOneAsync(command, cancellationToken).ConfigureAwait(False)
            End Using

        End Function

        ''' <summary>
        ''' Exact-match lookup by barcode among ACTIVE products only - mirrors
        ''' what UQ_Products_ActiveBarcode itself enforces (ADR-018). An
        ''' inactive product holding the same barcode value is not a
        ''' collision, so it is not returned here.
        ''' </summary>
        Public Shared Async Function FindByActiveBarcodeAsync(
            connection As MySqlConnection,
            barcode As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Product)

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = SelectColumns & " FROM Products WHERE Barcode = @barcode AND IsActive = 1;"
                command.Parameters.AddWithValue("@barcode", barcode)
                Return Await ReadOneAsync(command, cancellationToken).ConfigureAwait(False)
            End Using

        End Function

        ''' <param name="transaction">
        ''' Optional, added at P3-03 and appended last so no positional call
        ''' site written before it shifts arguments - the same shape
        ''' AuditLogWriter.WriteAsync's transaction parameter took at P1-11.
        ''' PurchaseOrderService passes its own transaction so the "is this
        ''' product still active?" check and the line insert that depends on
        ''' the answer run inside one transaction, rather than the check
        ''' happening outside it and being stale by the time the line is
        ''' written. Nothing (the default) keeps every earlier call site
        ''' behaving exactly as before.
        ''' </param>
        Public Shared Async Function GetByIdAsync(
            connection As MySqlConnection,
            id As Integer,
            Optional cancellationToken As CancellationToken = Nothing,
            Optional transaction As MySqlTransaction = Nothing) As Task(Of Product)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText = SelectColumns & " FROM Products WHERE Id = @id;"
                command.Parameters.AddWithValue("@id", id)
                Return Await ReadOneAsync(command, cancellationToken).ConfigureAwait(False)
            End Using

        End Function

        ''' <summary>
        ''' P2-08: reads a product's current Price/Cost inside the caller's
        ''' transaction, locking the row with <c>FOR UPDATE</c> - the same
        ''' discipline SystemSettingsRepository.ReadForUpdateAsync uses - so
        ''' the "old value" a PriceHistory row records is the value this
        ''' write actually replaced, not a stale read from before a
        ''' concurrent change. Found = False when no such product exists.
        ''' </summary>
        Public Shared Async Function ReadPriceForUpdateAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            productId As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Found As Boolean, Price As Decimal, Cost As Decimal))

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText = "SELECT Price, Cost FROM Products WHERE Id = @id FOR UPDATE;"
                command.Parameters.AddWithValue("@id", productId)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    If Not Await reader.ReadAsync(cancellationToken).ConfigureAwait(False) Then
                        Return (Found:=False, Price:=0D, Cost:=0D)
                    End If
                    Return (Found:=True, Price:=reader.GetDecimal(0), Cost:=reader.GetDecimal(1))
                End Using
            End Using

        End Function

        ''' <summary>
        ''' P2-08: writes new Price/Cost values inside the caller's
        ''' transaction, advancing RowVersion. Always called after
        ''' <see cref="ReadPriceForUpdateAsync"/> has already locked and
        ''' confirmed the row within the same transaction, so the affected-
        ''' row count here is a defensive check, not the mechanism that
        ''' prevents a lost update - the row lock is.
        ''' </summary>
        Public Shared Async Function UpdatePriceCostAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            productId As Integer,
            newPrice As Decimal,
            newCost As Decimal,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Boolean)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "UPDATE Products " &
                    "   SET Price = @price, " &
                    "       Cost = @cost, " &
                    "       RowVersion = RowVersion + 1, " &
                    "       UpdatedAtUtc = UTC_TIMESTAMP(6) " &
                    " WHERE Id = @id;"
                command.Parameters.AddWithValue("@price", newPrice)
                command.Parameters.AddWithValue("@cost", newCost)
                command.Parameters.AddWithValue("@id", productId)

                Dim affectedRows As Integer = Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return affectedRows = 1
            End Using

        End Function

        Public Shared Async Function CategoryExistsAsync(connection As MySqlConnection, id As Integer, Optional cancellationToken As CancellationToken = Nothing) As Task(Of Boolean)
            Return Await ScalarExistsAsync(connection, "SELECT 1 FROM Categories WHERE Id = @id;", id, cancellationToken).ConfigureAwait(False)
        End Function

        Public Shared Async Function BrandExistsAsync(connection As MySqlConnection, id As Integer, Optional cancellationToken As CancellationToken = Nothing) As Task(Of Boolean)
            Return Await ScalarExistsAsync(connection, "SELECT 1 FROM Brands WHERE Id = @id;", id, cancellationToken).ConfigureAwait(False)
        End Function

        Public Shared Async Function UnitExistsAsync(connection As MySqlConnection, id As Integer, Optional cancellationToken As CancellationToken = Nothing) As Task(Of Boolean)
            Return Await ScalarExistsAsync(connection, "SELECT 1 FROM Units WHERE Id = @id;", id, cancellationToken).ConfigureAwait(False)
        End Function

        ''' <summary>
        ''' Lookup a cashier/inventory clerk can drive from the keyboard
        ''' (spec section 10.3: "search by SKU, optional barcode, or product
        ''' name") - EXACT Sku/Barcode match first, so a scanned barcode (or
        ''' a typed exact Sku) never comes back diluted by an unrelated
        ''' partial-name match; only when nothing matches exactly does this
        ''' fall through to the partial <c>LIKE</c> search across Sku,
        ''' Barcode, and Name. Nothing/empty <paramref name="query"/> skips
        ''' the exact phase entirely and returns every product, paginated.
        ''' Each row carries its current available stock (COALESCE'd against
        ''' StockBalances, the same "never touched yet = 0" shape
        ''' StockRepository's own read queries use) so the caller never needs
        ''' a second call to show it.
        ''' <paramref name="includeInactive"/> is False by default (spec
        ''' section 10.2: an inactive product "cannot be newly sold or newly
        ''' ordered", and P2-09 treats ordinary search/lookup as one of
        ''' those paths) - True is for the history/reports case spec section
        ''' 12 requires ("remain visible in history and reports").
        ''' </summary>
        Public Shared Async Function SearchAsync(
            connection As MySqlConnection,
            query As String,
            page As Integer,
            pageSize As Integer,
            sortField As ProductSortField,
            sortDescending As Boolean,
            Optional includeInactive As Boolean = False,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Items As IReadOnlyList(Of ProductSearchItem), TotalCount As Integer))

            Dim trimmedQuery As String = If(query, String.Empty).Trim()

            If trimmedQuery.Length > 0 Then

                Dim exactResult = Await SearchExactAsync(
                    connection, trimmedQuery, page, pageSize, sortField, sortDescending, includeInactive, cancellationToken).ConfigureAwait(False)

                If exactResult.TotalCount > 0 Then
                    Return exactResult
                End If

            End If

            Return Await SearchPartialAsync(
                connection, If(trimmedQuery.Length > 0, trimmedQuery, Nothing), page, pageSize, sortField, sortDescending, includeInactive, cancellationToken).ConfigureAwait(False)

        End Function

        ''' <summary>The exact-match phase <see cref="SearchAsync"/> tries first - an empty result here (never an exception) is what tells the caller to fall through to the partial search.</summary>
        Private Shared Async Function SearchExactAsync(
            connection As MySqlConnection,
            exactValue As String,
            page As Integer,
            pageSize As Integer,
            sortField As ProductSortField,
            sortDescending As Boolean,
            includeInactive As Boolean,
            cancellationToken As CancellationToken) As Task(Of (Items As IReadOnlyList(Of ProductSearchItem), TotalCount As Integer))

            Dim conditions As New List(Of String) From {"(p.Sku = @exact OR p.Barcode = @exact)"}
            If Not includeInactive Then
                conditions.Add("p.IsActive = 1")
            End If
            Dim whereClause As String = " WHERE " & String.Join(" AND ", conditions)

            Dim totalCount As Integer
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM Products p" & whereClause & ";"
                command.Parameters.AddWithValue("@exact", exactValue)
                totalCount = CInt(Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False))
            End Using

            If totalCount = 0 Then
                Return (Items:=Array.Empty(Of ProductSearchItem)(), TotalCount:=0)
            End If

            Dim items As New List(Of ProductSearchItem)
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    SelectColumnsWithStock & FromWithStock & whereClause &
                    " ORDER BY " & ProductOrderByClause(sortField, sortDescending) & " LIMIT @pageSize OFFSET @offset;"
                command.Parameters.AddWithValue("@exact", exactValue)
                command.Parameters.AddWithValue("@pageSize", pageSize)
                command.Parameters.AddWithValue("@offset", (page - 1) * pageSize)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        items.Add(ReadProductSearchItem(reader))
                    End While
                End Using
            End Using

            Return (Items:=items, TotalCount:=totalCount)

        End Function

        ''' <summary>The fallback phase <see cref="SearchAsync"/> uses only when the exact phase found nothing - the original P2-07 <c>LIKE</c> search, now stock-joined and sorted by <paramref name="sortField"/> instead of a fixed <c>ORDER BY Name</c>.</summary>
        Private Shared Async Function SearchPartialAsync(
            connection As MySqlConnection,
            query As String,
            page As Integer,
            pageSize As Integer,
            sortField As ProductSortField,
            sortDescending As Boolean,
            includeInactive As Boolean,
            cancellationToken As CancellationToken) As Task(Of (Items As IReadOnlyList(Of ProductSearchItem), TotalCount As Integer))

            Dim hasQuery As Boolean = Not String.IsNullOrEmpty(query)
            Dim likePattern As String = If(hasQuery, "%" & query & "%", Nothing)

            Dim conditions As New List(Of String)
            If hasQuery Then
                conditions.Add("(p.Sku LIKE @pattern OR p.Barcode LIKE @pattern OR p.Name LIKE @pattern)")
            End If
            If Not includeInactive Then
                conditions.Add("p.IsActive = 1")
            End If
            Dim whereClause As String = If(conditions.Count > 0, " WHERE " & String.Join(" AND ", conditions), "")

            Dim totalCount As Integer
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM Products p" & whereClause & ";"
                If hasQuery Then
                    command.Parameters.AddWithValue("@pattern", likePattern)
                End If
                totalCount = CInt(Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False))
            End Using

            Dim items As New List(Of ProductSearchItem)
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    SelectColumnsWithStock & FromWithStock & whereClause &
                    " ORDER BY " & ProductOrderByClause(sortField, sortDescending) & " LIMIT @pageSize OFFSET @offset;"
                If hasQuery Then
                    command.Parameters.AddWithValue("@pattern", likePattern)
                End If
                command.Parameters.AddWithValue("@pageSize", pageSize)
                command.Parameters.AddWithValue("@offset", (page - 1) * pageSize)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        items.Add(ReadProductSearchItem(reader))
                    End While
                End Using
            End Using

            Return (Items:=items, TotalCount:=totalCount)

        End Function

        ''' <summary>
        ''' Inserts a new, always-Active product (creation has no IsActive
        ''' flag to set - see CreateProductRequest's own header) inside
        ''' <paramref name="transaction"/>. A duplicate Sku or active Barcode
        ''' is reported as an outcome, not an exception - the real unique
        ''' indexes are what make this safe under concurrency; catching
        ''' ERROR 1062 here only translates that guarantee into something a
        ''' caller can branch on instead of a raw MySqlException.
        ''' </summary>
        Public Shared Async Function InsertAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            product As Product,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Kind As ProductWriteOutcomeKind, ProductId As Integer))

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO Products " &
                    "(Sku, Barcode, Name, Description, CategoryId, BrandId, UnitId, Price, Cost, ReorderLevel, IsActive, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES " &
                    "(@sku, @barcode, @name, @description, @categoryId, @brandId, @unitId, @price, @cost, @reorderLevel, 1, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@sku", product.Sku)
                command.Parameters.AddWithValue("@barcode", If(CObj(product.Barcode), DBNull.Value))
                command.Parameters.AddWithValue("@name", product.Name)
                command.Parameters.AddWithValue("@description", If(CObj(product.Description), DBNull.Value))
                command.Parameters.AddWithValue("@categoryId", If(CObj(product.CategoryId), DBNull.Value))
                command.Parameters.AddWithValue("@brandId", If(CObj(product.BrandId), DBNull.Value))
                command.Parameters.AddWithValue("@unitId", If(CObj(product.UnitId), DBNull.Value))
                command.Parameters.AddWithValue("@price", product.Price)
                command.Parameters.AddWithValue("@cost", product.Cost)
                command.Parameters.AddWithValue("@reorderLevel", product.ReorderLevel)

                Try
                    Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                    Return (Kind:=ProductWriteOutcomeKind.Success, ProductId:=CInt(command.LastInsertedId))

                Catch ex As MySqlException When ex.Number = DuplicateKeyErrorNumber
                    Return (Kind:=ClassifyDuplicateKey(ex), ProductId:=0)
                End Try

            End Using

        End Function

        ''' <summary>
        ''' Updates the fields Products.Manage governs (never Price, Cost, or
        ''' IsActive - see UpdateProductRequest's own header), inside
        ''' <paramref name="transaction"/>. RowVersion is incremented so the
        ''' column spec section 10.2 calls "version/concurrency metadata"
        ''' actually advances on every write; no client-supplied expected
        ''' value is checked here - no card has asked for lost-update
        ''' detection on this endpoint yet.
        ''' </summary>
        Public Shared Async Function UpdateAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            id As Integer,
            name As String,
            description As String,
            categoryId As Integer?,
            brandId As Integer?,
            unitId As Integer?,
            barcode As String,
            reorderLevel As Decimal,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of ProductWriteOutcomeKind)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "UPDATE Products " &
                    "   SET Name = @name, " &
                    "       Description = @description, " &
                    "       CategoryId = @categoryId, " &
                    "       BrandId = @brandId, " &
                    "       UnitId = @unitId, " &
                    "       Barcode = @barcode, " &
                    "       ReorderLevel = @reorderLevel, " &
                    "       RowVersion = RowVersion + 1, " &
                    "       UpdatedAtUtc = UTC_TIMESTAMP(6) " &
                    " WHERE Id = @id;"
                command.Parameters.AddWithValue("@name", name)
                command.Parameters.AddWithValue("@description", If(CObj(description), DBNull.Value))
                command.Parameters.AddWithValue("@categoryId", If(CObj(categoryId), DBNull.Value))
                command.Parameters.AddWithValue("@brandId", If(CObj(brandId), DBNull.Value))
                command.Parameters.AddWithValue("@unitId", If(CObj(unitId), DBNull.Value))
                command.Parameters.AddWithValue("@barcode", If(CObj(barcode), DBNull.Value))
                command.Parameters.AddWithValue("@reorderLevel", reorderLevel)
                command.Parameters.AddWithValue("@id", id)

                Dim affectedRows As Integer
                Try
                    affectedRows = Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)

                Catch ex As MySqlException When ex.Number = DuplicateKeyErrorNumber
                    ' Only Barcode can collide on update (Sku is not part of
                    ' this contract - see UpdateProductRequest's header).
                    Return ProductWriteOutcomeKind.DuplicateBarcode
                End Try

                Return If(affectedRows = 1, ProductWriteOutcomeKind.Success, ProductWriteOutcomeKind.NotFound)

            End Using

        End Function

        ''' <summary>
        ''' P2-09: flips IsActive, carrying the "did this actually change
        ''' anything" check inside the same statement as the mutation
        ''' (ADR-006's conditional-update discipline, the same shape
        ''' StockRepository.TryDecrementAsync uses) - never a separate
        ''' read-then-decide-then-write. Returns False both when no such
        ''' product exists and when it already held the target state; the
        ''' caller (ProductLifecycleService) tells those apart with one
        ''' more read only on the False path, where it no longer matters
        ''' whether that read races anything - nothing is being written.
        ''' </summary>
        Public Shared Async Function SetActiveStateAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            productId As Integer,
            targetIsActive As Boolean,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Boolean)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "UPDATE Products " &
                    "   SET IsActive = @targetIsActive, " &
                    "       RowVersion = RowVersion + 1, " &
                    "       UpdatedAtUtc = UTC_TIMESTAMP(6) " &
                    " WHERE Id = @id " &
                    "   AND IsActive <> @targetIsActive;"
                command.Parameters.AddWithValue("@targetIsActive", targetIsActive)
                command.Parameters.AddWithValue("@id", productId)

                Dim affectedRows As Integer = Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return affectedRows = 1
            End Using

        End Function

        ''' <summary>
        ''' MySqlConnector's duplicate-entry message names the violated key,
        ''' for example "Duplicate entry 'X' for key 'UQ_Products_Sku'" - the
        ''' two unique constraints on this table are distinguished by that
        ''' key name, which is deterministic for this codebase's own
        ''' migration (0006_product-master.sql), not a value from outside it.
        ''' </summary>
        Private Shared Function ClassifyDuplicateKey(ex As MySqlException) As ProductWriteOutcomeKind
            If ex.Message.Contains("UQ_Products_Sku") Then
                Return ProductWriteOutcomeKind.DuplicateSku
            End If
            Return ProductWriteOutcomeKind.DuplicateBarcode
        End Function

        Private Const SelectColumns As String =
            "SELECT Id, Sku, Barcode, Name, Description, CategoryId, BrandId, UnitId, Price, Cost, ReorderLevel, IsActive, RowVersion, CreatedAtUtc, UpdatedAtUtc"

        ''' <summary>
        ''' The same 15 Products columns SelectColumns projects, in the same
        ''' order (so ReadProduct's ordinal reads stay valid), plus one more:
        ''' AvailableStock at ordinal 15. Columns are "p."-qualified because
        ''' StockBalances has its own RowVersion/UpdatedAtUtc columns that
        ''' would otherwise be ambiguous once joined.
        ''' </summary>
        Private Const SelectColumnsWithStock As String =
            "SELECT p.Id, p.Sku, p.Barcode, p.Name, p.Description, p.CategoryId, p.BrandId, p.UnitId, p.Price, p.Cost, p.ReorderLevel, p.IsActive, p.RowVersion, p.CreatedAtUtc, p.UpdatedAtUtc, " &
            "COALESCE(b.Quantity, 0.000) AS AvailableStock"

        Private Const FromWithStock As String =
            " FROM Products p LEFT JOIN StockBalances b ON b.ProductId = p.Id"

        ''' <summary>Id is always the final tie-break so paging is stable - the same reasoning StockRepository.StockOrderByClause's own header gives.</summary>
        Private Shared Function ProductOrderByClause(sortField As ProductSortField, sortDescending As Boolean) As String

            Dim column As String = If(sortField = ProductSortField.Sku, "p.Sku", "p.Name")
            Dim direction As String = If(sortDescending, " DESC", " ASC")

            Return column & direction & ", p.Id" & direction

        End Function

        Private Shared Async Function ReadOneAsync(command As MySqlCommand, cancellationToken As CancellationToken) As Task(Of Product)

            Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                If Await reader.ReadAsync(cancellationToken).ConfigureAwait(False) Then
                    Return ReadProduct(reader)
                End If
                Return Nothing
            End Using

        End Function

        Private Shared Function ReadProduct(reader As MySqlDataReader) As Product

            Return New Product With {
                .Id = reader.GetInt32(0),
                .Sku = reader.GetString(1),
                .Barcode = If(reader.IsDBNull(2), Nothing, reader.GetString(2)),
                .Name = reader.GetString(3),
                .Description = If(reader.IsDBNull(4), Nothing, reader.GetString(4)),
                .CategoryId = If(reader.IsDBNull(5), CType(Nothing, Integer?), reader.GetInt32(5)),
                .BrandId = If(reader.IsDBNull(6), CType(Nothing, Integer?), reader.GetInt32(6)),
                .UnitId = If(reader.IsDBNull(7), CType(Nothing, Integer?), reader.GetInt32(7)),
                .Price = reader.GetDecimal(8),
                .Cost = reader.GetDecimal(9),
                .ReorderLevel = reader.GetDecimal(10),
                .IsActive = reader.GetBoolean(11),
                .RowVersion = reader.GetInt64(12),
                .CreatedAtUtc = reader.GetDateTime(13),
                .UpdatedAtUtc = reader.GetDateTime(14)
            }

        End Function

        ''' <summary>Relies on SelectColumnsWithStock projecting the same 15 Products columns, in the same order, as SelectColumns - ReadProduct's ordinals stay valid, with AvailableStock as one more column at ordinal 15.</summary>
        Private Shared Function ReadProductSearchItem(reader As MySqlDataReader) As ProductSearchItem

            Return New ProductSearchItem With {
                .Product = ReadProduct(reader),
                .AvailableStock = reader.GetDecimal(15)
            }

        End Function

        Private Shared Async Function ScalarExistsAsync(connection As MySqlConnection, sql As String, id As Integer, cancellationToken As CancellationToken) As Task(Of Boolean)

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = sql
                command.Parameters.AddWithValue("@id", id)
                Dim result As Object = Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False)
                Return result IsNot Nothing
            End Using

        End Function

    End Class

End Namespace
