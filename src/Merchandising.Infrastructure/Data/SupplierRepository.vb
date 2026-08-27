' Merchandising.Infrastructure.Data.SupplierRepository
'
' P2-10: Create/read/update/search against Suppliers, plus the pre-check
' SuppliersController uses for a usable duplicate-name error message. The
' pre-check is a courtesy, not the guarantee - InsertAsync/UpdateAsync below
' are what actually enforce uniqueness, by catching the real unique-index
' violation (ERROR 1062) from UQ_Suppliers_Name (0007_suppliers.sql) - the
' same "the database cannot be trusted... the API cannot be trusted under
' concurrency" shape ProductRepository already uses for Products.

Imports System.Threading
Imports System.Threading.Tasks
Imports Merchandising.Domain.Entities
Imports MySqlConnector

Namespace Data

    Public Enum SupplierWriteOutcomeKind
        Success
        DuplicateName
        NotFound
    End Enum

    Public NotInheritable Class SupplierRepository

        Private Const DuplicateKeyErrorNumber As Integer = 1062

        ''' <summary>Exact-match lookup by Name, any active state. Nothing if no such name exists.</summary>
        Public Shared Async Function FindByNameAsync(
            connection As MySqlConnection,
            name As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Supplier)

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = SelectColumns & " FROM Suppliers WHERE Name = @name;"
                command.Parameters.AddWithValue("@name", name)
                Return Await ReadOneAsync(command, cancellationToken).ConfigureAwait(False)
            End Using

        End Function

        ''' <param name="transaction">
        ''' Optional, added at P3-03 and appended last so no positional call
        ''' site written before it shifts arguments - see
        ''' ProductRepository.GetByIdAsync's identical parameter for the
        ''' reasoning. Nothing (the default) keeps every earlier call site
        ''' behaving exactly as before.
        ''' </param>
        Public Shared Async Function GetByIdAsync(
            connection As MySqlConnection,
            id As Integer,
            Optional cancellationToken As CancellationToken = Nothing,
            Optional transaction As MySqlTransaction = Nothing) As Task(Of Supplier)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText = SelectColumns & " FROM Suppliers WHERE Id = @id;"
                command.Parameters.AddWithValue("@id", id)
                Return Await ReadOneAsync(command, cancellationToken).ConfigureAwait(False)
            End Using

        End Function

        ''' <summary>
        ''' Free-text search against Name (spec section 12's "supplier name"
        ''' index), paginated. Nothing/empty <paramref name="query"/> returns
        ''' every supplier. <paramref name="includeInactive"/> defaults False,
        ''' mirroring ProductRepository.SearchAsync's own default - ordinary
        ''' lookup excludes inactive suppliers; True is for the history/
        ''' procurement-record case.
        ''' </summary>
        Public Shared Async Function SearchAsync(
            connection As MySqlConnection,
            query As String,
            page As Integer,
            pageSize As Integer,
            Optional includeInactive As Boolean = False,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Items As IReadOnlyList(Of Supplier), TotalCount As Integer))

            Dim hasQuery As Boolean = Not String.IsNullOrWhiteSpace(query)
            Dim likePattern As String = If(hasQuery, "%" & query & "%", Nothing)

            Dim conditions As New List(Of String)
            If hasQuery Then
                conditions.Add("Name LIKE @pattern")
            End If
            If Not includeInactive Then
                conditions.Add("IsActive = 1")
            End If
            Dim whereClause As String = If(conditions.Count > 0, " WHERE " & String.Join(" AND ", conditions), "")

            Dim totalCount As Integer
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM Suppliers" & whereClause & ";"
                If hasQuery Then
                    command.Parameters.AddWithValue("@pattern", likePattern)
                End If
                totalCount = CInt(Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False))
            End Using

            Dim items As New List(Of Supplier)
            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    SelectColumns & " FROM Suppliers" & whereClause & " ORDER BY Name LIMIT @pageSize OFFSET @offset;"
                If hasQuery Then
                    command.Parameters.AddWithValue("@pattern", likePattern)
                End If
                command.Parameters.AddWithValue("@pageSize", pageSize)
                command.Parameters.AddWithValue("@offset", (page - 1) * pageSize)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        items.Add(ReadSupplier(reader))
                    End While
                End Using
            End Using

            Return (Items:=items, TotalCount:=totalCount)

        End Function

        ''' <summary>
        ''' Inserts a new, always-Active supplier inside
        ''' <paramref name="transaction"/>. A duplicate Name is reported as an
        ''' outcome, not an exception - the real unique index is what makes
        ''' this safe under concurrency; catching ERROR 1062 here only
        ''' translates that guarantee into something a caller can branch on.
        ''' </summary>
        Public Shared Async Function InsertAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            supplier As Supplier,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Kind As SupplierWriteOutcomeKind, SupplierId As Integer))

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO Suppliers " &
                    "(Name, ContactName, Phone, Email, Address, IsActive, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES " &
                    "(@name, @contactName, @phone, @email, @address, 1, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@name", supplier.Name)
                command.Parameters.AddWithValue("@contactName", If(CObj(supplier.ContactName), DBNull.Value))
                command.Parameters.AddWithValue("@phone", If(CObj(supplier.Phone), DBNull.Value))
                command.Parameters.AddWithValue("@email", If(CObj(supplier.Email), DBNull.Value))
                command.Parameters.AddWithValue("@address", If(CObj(supplier.Address), DBNull.Value))

                Try
                    Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                    Return (Kind:=SupplierWriteOutcomeKind.Success, SupplierId:=CInt(command.LastInsertedId))

                Catch ex As MySqlException When ex.Number = DuplicateKeyErrorNumber
                    Return (Kind:=SupplierWriteOutcomeKind.DuplicateName, SupplierId:=0)
                End Try

            End Using

        End Function

        ''' <summary>
        ''' Updates the fields Suppliers.Manage governs (never IsActive - the
        ''' deactivate/reactivate lifecycle owns that), inside
        ''' <paramref name="transaction"/>. RowVersion is incremented on every
        ''' write, mirroring ProductRepository.UpdateAsync.
        ''' </summary>
        Public Shared Async Function UpdateAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            id As Integer,
            name As String,
            contactName As String,
            phone As String,
            email As String,
            address As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of SupplierWriteOutcomeKind)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "UPDATE Suppliers " &
                    "   SET Name = @name, " &
                    "       ContactName = @contactName, " &
                    "       Phone = @phone, " &
                    "       Email = @email, " &
                    "       Address = @address, " &
                    "       RowVersion = RowVersion + 1, " &
                    "       UpdatedAtUtc = UTC_TIMESTAMP(6) " &
                    " WHERE Id = @id;"
                command.Parameters.AddWithValue("@name", name)
                command.Parameters.AddWithValue("@contactName", If(CObj(contactName), DBNull.Value))
                command.Parameters.AddWithValue("@phone", If(CObj(phone), DBNull.Value))
                command.Parameters.AddWithValue("@email", If(CObj(email), DBNull.Value))
                command.Parameters.AddWithValue("@address", If(CObj(address), DBNull.Value))
                command.Parameters.AddWithValue("@id", id)

                Dim affectedRows As Integer
                Try
                    affectedRows = Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)

                Catch ex As MySqlException When ex.Number = DuplicateKeyErrorNumber
                    Return SupplierWriteOutcomeKind.DuplicateName
                End Try

                Return If(affectedRows = 1, SupplierWriteOutcomeKind.Success, SupplierWriteOutcomeKind.NotFound)

            End Using

        End Function

        ''' <summary>
        ''' Flips IsActive, carrying the "did this actually change anything"
        ''' check inside the same statement as the mutation (ADR-006's
        ''' conditional-update discipline) - mirrors
        ''' ProductRepository.SetActiveStateAsync exactly. Returns False both
        ''' when no such supplier exists and when it already held the target
        ''' state; the caller (SupplierLifecycleService) tells those apart
        ''' with one more read only on the False path.
        ''' </summary>
        Public Shared Async Function SetActiveStateAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            supplierId As Integer,
            targetIsActive As Boolean,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Boolean)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "UPDATE Suppliers " &
                    "   SET IsActive = @targetIsActive, " &
                    "       RowVersion = RowVersion + 1, " &
                    "       UpdatedAtUtc = UTC_TIMESTAMP(6) " &
                    " WHERE Id = @id " &
                    "   AND IsActive <> @targetIsActive;"
                command.Parameters.AddWithValue("@targetIsActive", targetIsActive)
                command.Parameters.AddWithValue("@id", supplierId)

                Dim affectedRows As Integer = Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return affectedRows = 1
            End Using

        End Function

        Private Const SelectColumns As String =
            "SELECT Id, Name, ContactName, Phone, Email, Address, IsActive, RowVersion, CreatedAtUtc, UpdatedAtUtc"

        Private Shared Async Function ReadOneAsync(command As MySqlCommand, cancellationToken As CancellationToken) As Task(Of Supplier)

            Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                If Await reader.ReadAsync(cancellationToken).ConfigureAwait(False) Then
                    Return ReadSupplier(reader)
                End If
                Return Nothing
            End Using

        End Function

        Private Shared Function ReadSupplier(reader As MySqlDataReader) As Supplier

            Return New Supplier With {
                .Id = reader.GetInt32(0),
                .Name = reader.GetString(1),
                .ContactName = If(reader.IsDBNull(2), Nothing, reader.GetString(2)),
                .Phone = If(reader.IsDBNull(3), Nothing, reader.GetString(3)),
                .Email = If(reader.IsDBNull(4), Nothing, reader.GetString(4)),
                .Address = If(reader.IsDBNull(5), Nothing, reader.GetString(5)),
                .IsActive = reader.GetBoolean(6),
                .RowVersion = reader.GetInt64(7),
                .CreatedAtUtc = reader.GetDateTime(8),
                .UpdatedAtUtc = reader.GetDateTime(9)
            }

        End Function

    End Class

End Namespace
