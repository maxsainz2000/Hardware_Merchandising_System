' Merchandising.Infrastructure.Data.SaleRepository
'
' P5-07: writes against Sales, SaleLines and SalePayments (db/migrations/
' 0011_pos.sql). All three are append-only (db/grants/0013_pos-grants.sql
' grants INSERT only, never UPDATE or DELETE) - this class has no update or
' delete method for any of them, on purpose, the same absence
' StockMovementWriter/AuditLogWriter already establish for their own
' append-only tables.
'
' EVERY WRITE HERE TAKES A TRANSACTION, WITH NO OPTIONAL OVERLOAD -
' SaleService owns it (ADR-006), the same arrangement every other repository
' in this solution uses (CashierSessionRepository's identical header note).
'
' P6-02: SearchAsync/GetLinesForSalesAsync/GetPaymentsForSalesAsync are the
' first PLAIN, NON-LOCKING reads this class carries - GET /api/v1/sales, the
' detail endpoint ADR-023 point 6 flagged as owed to this card. Lines and
' payments are fetched in a SECOND and THIRD query, keyed by the page's own
' sale ids, rather than one three-way JOIN - a single JOIN across a
' one-to-many (SaleLines) AND a one-to-one (SalePayments) table fans the
' payment row out once per line, which SUM()s wrong the moment a sale has
' more than one line. Same "paginate the header, then fetch children for
' just this page" shape PurchaseOrderRepository.GetByIdAsync/SearchAsync
' already establish for a single order; this extends it to a page of many.

Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Data

    ''' <summary>The sort fields <see cref="SaleRepository.SearchAsync"/> will honour - a closed set by construction, the same reasoning PurchaseOrderSortField gives.</summary>
    Public Enum SaleSortField
        CreatedAt
        Total
    End Enum

    Public NotInheritable Class SaleRepository

        Private Sub New()
        End Sub

        ''' <summary>Inserts the Sales header, already Completed - a sale row is never inserted in any other status (Merchandising.Domain.Sales.SaleStatus's own header).</summary>
        ''' <returns>The new sale's Id.</returns>
        Public Shared Async Function InsertSaleAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            cashierSessionId As Integer,
            cashierUserId As Integer,
            total As Decimal,
            correlationId As String,
            createdAtUtc As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Integer)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO Sales (CashierSessionId, CashierUserId, Total, Status, CorrelationId, CreatedAtUtc) " &
                    "VALUES (@cashierSessionId, @cashierUserId, @total, 'Completed', @correlationId, @createdAtUtc);"
                command.Parameters.AddWithValue("@cashierSessionId", cashierSessionId)
                command.Parameters.AddWithValue("@cashierUserId", cashierUserId)
                command.Parameters.AddWithValue("@total", total)
                command.Parameters.AddWithValue("@correlationId", correlationId)
                command.Parameters.AddWithValue("@createdAtUtc", createdAtUtc)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return CInt(command.LastInsertedId)
            End Using

        End Function

        ''' <summary>Inserts one SaleLines row - UnitPrice/Cost/LineTotal are the already-captured, already-rounded values (Merchandising.Domain.Sales.SaleLine), never recomputed here.</summary>
        ''' <returns>The new line's Id.</returns>
        Public Shared Async Function InsertLineAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            saleId As Integer,
            productId As Integer,
            quantity As Decimal,
            unitPrice As Decimal,
            cost As Decimal,
            lineTotal As Decimal,
            createdAtUtc As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Integer)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO SaleLines (SaleId, ProductId, Quantity, UnitPrice, Cost, LineTotal, CreatedAtUtc) " &
                    "VALUES (@saleId, @productId, @quantity, @unitPrice, @cost, @lineTotal, @createdAtUtc);"
                command.Parameters.AddWithValue("@saleId", saleId)
                command.Parameters.AddWithValue("@productId", productId)
                command.Parameters.AddWithValue("@quantity", quantity)
                command.Parameters.AddWithValue("@unitPrice", unitPrice)
                command.Parameters.AddWithValue("@cost", cost)
                command.Parameters.AddWithValue("@lineTotal", lineTotal)
                command.Parameters.AddWithValue("@createdAtUtc", createdAtUtc)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return CInt(command.LastInsertedId)
            End Using

        End Function

        ''' <summary>
        ''' Inserts the one SalePayments row a sale carries (SaleService's own
        ''' header explains why this is a single row, not a list, in this
        ''' card). <paramref name="tenderedAmount"/>/<paramref name="changeAmount"/>
        ''' must both be Nothing for Card/EWallet and both set for Cash -
        ''' CK_SalePayments_CashTenderPairing (0011_pos.sql) is the backstop
        ''' if that pairing is ever wrong; SalesController's own validation
        ''' is what makes it a controlled 400 rather than a raw CHECK failure.
        ''' </summary>
        ''' <returns>The new payment's Id.</returns>
        Public Shared Async Function InsertPaymentAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            saleId As Integer,
            method As String,
            amount As Decimal,
            tenderedAmount As Decimal?,
            changeAmount As Decimal?,
            createdAtUtc As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Integer)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO SalePayments (SaleId, Method, Amount, TenderedAmount, ChangeAmount, CreatedAtUtc) " &
                    "VALUES (@saleId, @method, @amount, @tenderedAmount, @changeAmount, @createdAtUtc);"
                command.Parameters.AddWithValue("@saleId", saleId)
                command.Parameters.AddWithValue("@method", method)
                command.Parameters.AddWithValue("@amount", amount)
                command.Parameters.AddWithValue("@tenderedAmount", If(CObj(tenderedAmount), DBNull.Value))
                command.Parameters.AddWithValue("@changeAmount", If(CObj(changeAmount), DBNull.Value))
                command.Parameters.AddWithValue("@createdAtUtc", createdAtUtc)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return CInt(command.LastInsertedId)
            End Using

        End Function

        ''' <summary>P5-11: confirms a Sales row exists before validating its return's lines - the same "confirm first, for a clear NotFound before line-level errors" shape ReceiptRepository.ExistsAsync uses for purchase returns.</summary>
        Public Shared Async Function ExistsAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            saleId As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Boolean)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText = "SELECT 1 FROM Sales WHERE Id = @id;"
                command.Parameters.AddWithValue("@id", saleId)

                Dim result As Object = Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False)
                Return result IsNot Nothing
            End Using

        End Function

        ''' <summary>
        ''' Paginated, filtered, sorted list of sale HEADERS only - lines and
        ''' payment are fetched separately by <see cref="GetLinesForSalesAsync"/>/
        ''' <see cref="GetPaymentsForSalesAsync"/> once the page's own ids are
        ''' known (this class's own header explains why). <paramref name="fromUtc"/>/
        ''' <paramref name="toUtcExclusive"/> are already-converted UTC instants -
        ''' the caller does the store-local-to-UTC conversion (StoreTimeZone),
        ''' the same layering PurchaseOrderRepository.SearchHistoryAsync uses.
        ''' <paramref name="page"/>/<paramref name="pageSize"/> must already be
        ''' clamped by the caller.
        ''' </summary>
        Public Shared Async Function SearchAsync(
            connection As MySqlConnection,
            fromUtc As DateTime?,
            toUtcExclusive As DateTime?,
            cashierUserId As Integer?,
            sortField As SaleSortField,
            sortDescending As Boolean,
            page As Integer,
            pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) _
            As Task(Of (Items As IReadOnlyList(Of (Id As Integer, CashierSessionId As Integer, CashierUserId As Integer,
                                                    Total As Decimal, Status As String, CorrelationId As String, CreatedAtUtc As DateTime)),
                       TotalCount As Integer))

            Dim conditions As New List(Of String)

            If fromUtc.HasValue Then
                conditions.Add("CreatedAtUtc >= @fromUtc")
            End If

            If toUtcExclusive.HasValue Then
                conditions.Add("CreatedAtUtc < @toUtcExclusive")
            End If

            If cashierUserId.HasValue Then
                conditions.Add("CashierUserId = @cashierUserId")
            End If

            Dim whereClause As String =
                If(conditions.Count > 0, " WHERE " & String.Join(" AND ", conditions), String.Empty)

            Dim addFilterParameters As Action(Of MySqlCommand) =
                Sub(command As MySqlCommand)
                    If fromUtc.HasValue Then
                        command.Parameters.AddWithValue("@fromUtc", fromUtc.Value)
                    End If
                    If toUtcExclusive.HasValue Then
                        command.Parameters.AddWithValue("@toUtcExclusive", toUtcExclusive.Value)
                    End If
                    If cashierUserId.HasValue Then
                        command.Parameters.AddWithValue("@cashierUserId", cashierUserId.Value)
                    End If
                End Sub

            Dim totalCount As Integer

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM Sales" & whereClause & ";"
                addFilterParameters(command)
                totalCount = CInt(Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False))
            End Using

            Dim orderBy As String =
                If(sortField = SaleSortField.Total, "Total", "CreatedAtUtc") & If(sortDescending, " DESC", " ASC")

            Dim items As New List(Of (Id As Integer, CashierSessionId As Integer, CashierUserId As Integer,
                                      Total As Decimal, Status As String, CorrelationId As String, CreatedAtUtc As DateTime))

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT Id, CashierSessionId, CashierUserId, Total, Status, CorrelationId, CreatedAtUtc " &
                    "  FROM Sales" &
                    whereClause &
                    " ORDER BY " & orderBy &
                    " LIMIT @pageSize OFFSET @offset;"
                addFilterParameters(command)
                command.Parameters.AddWithValue("@pageSize", pageSize)
                command.Parameters.AddWithValue("@offset", (page - 1) * pageSize)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        ' MySqlConnector maps CHAR(36) to System.Guid, not
                        ' String - reader.GetString() throws InvalidCastException
                        ' on CorrelationId (StockRepository.ReadMovementItem's
                        ' identical header names this exact gotcha).
                        items.Add((Id:=reader.GetInt32(0), CashierSessionId:=reader.GetInt32(1), CashierUserId:=reader.GetInt32(2),
                                   Total:=reader.GetDecimal(3), Status:=reader.GetString(4), CorrelationId:=reader.GetGuid(5).ToString("d"),
                                   CreatedAtUtc:=reader.GetDateTime(6)))
                    End While
                End Using
            End Using

            Return (Items:=items, TotalCount:=totalCount)

        End Function

        ''' <summary>Every SaleLines row for the given sale ids, joined to Products (Sku, Name) - the same projection SaleService's own response construction uses. Empty when <paramref name="saleIds"/> is empty.</summary>
        Public Shared Async Function GetLinesForSalesAsync(
            connection As MySqlConnection,
            saleIds As IReadOnlyList(Of Integer),
            Optional cancellationToken As CancellationToken = Nothing) _
            As Task(Of ILookup(Of Integer, (Id As Integer, ProductId As Integer, ProductSku As String, ProductName As String,
                                            Quantity As Decimal, UnitPrice As Decimal, Cost As Decimal, LineTotal As Decimal)))

            If saleIds.Count = 0 Then
                Return Array.Empty(Of (Integer, (Integer, Integer, String, String, Decimal, Decimal, Decimal, Decimal)))().
                    ToLookup(Function(t) t.Item1, Function(t) t.Item2)
            End If

            Dim rows As New List(Of (SaleId As Integer, Id As Integer, ProductId As Integer, ProductSku As String, ProductName As String,
                                     Quantity As Decimal, UnitPrice As Decimal, Cost As Decimal, LineTotal As Decimal))

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT sl.SaleId, sl.Id, sl.ProductId, p.Sku, p.Name, sl.Quantity, sl.UnitPrice, sl.Cost, sl.LineTotal " &
                    "  FROM SaleLines sl " &
                    "  JOIN Products p ON p.Id = sl.ProductId " &
                    " WHERE sl.SaleId IN (" & InClausePlaceholders(saleIds.Count) & ") " &
                    " ORDER BY sl.SaleId, sl.Id;"
                AddInClauseParameters(command, saleIds)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        rows.Add((SaleId:=reader.GetInt32(0), Id:=reader.GetInt32(1), ProductId:=reader.GetInt32(2),
                                  ProductSku:=reader.GetString(3), ProductName:=reader.GetString(4), Quantity:=reader.GetDecimal(5),
                                  UnitPrice:=reader.GetDecimal(6), Cost:=reader.GetDecimal(7), LineTotal:=reader.GetDecimal(8)))
                    End While
                End Using
            End Using

            Return rows.ToLookup(
                Function(r) r.SaleId,
                Function(r) (Id:=r.Id, ProductId:=r.ProductId, ProductSku:=r.ProductSku, ProductName:=r.ProductName,
                             Quantity:=r.Quantity, UnitPrice:=r.UnitPrice, Cost:=r.Cost, LineTotal:=r.LineTotal))

        End Function

        ''' <summary>Every SalePayments row for the given sale ids - one per sale (SaleService only ever writes one). Empty when <paramref name="saleIds"/> is empty.</summary>
        Public Shared Async Function GetPaymentsForSalesAsync(
            connection As MySqlConnection,
            saleIds As IReadOnlyList(Of Integer),
            Optional cancellationToken As CancellationToken = Nothing) _
            As Task(Of IReadOnlyDictionary(Of Integer, (Id As Integer, Method As String, Amount As Decimal,
                                                         TenderedAmount As Decimal?, ChangeAmount As Decimal?)))

            Dim byId As New Dictionary(Of Integer, (Id As Integer, Method As String, Amount As Decimal,
                                                     TenderedAmount As Decimal?, ChangeAmount As Decimal?))

            If saleIds.Count = 0 Then
                Return byId
            End If

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT SaleId, Id, Method, Amount, TenderedAmount, ChangeAmount " &
                    "  FROM SalePayments " &
                    " WHERE SaleId IN (" & InClausePlaceholders(saleIds.Count) & ");"
                AddInClauseParameters(command, saleIds)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)

                        Dim tenderedOrdinal As Integer = 4
                        Dim changeOrdinal As Integer = 5

                        byId(reader.GetInt32(0)) =
                            (Id:=reader.GetInt32(1), Method:=reader.GetString(2), Amount:=reader.GetDecimal(3),
                             TenderedAmount:=If(reader.IsDBNull(tenderedOrdinal), CType(Nothing, Decimal?), reader.GetDecimal(tenderedOrdinal)),
                             ChangeAmount:=If(reader.IsDBNull(changeOrdinal), CType(Nothing, Decimal?), reader.GetDecimal(changeOrdinal)))
                    End While
                End Using
            End Using

            Return byId

        End Function

        ''' <summary>"@in0,@in1,..." for an IN clause of <paramref name="count"/> values - see <see cref="AddInClauseParameters"/>.</summary>
        Friend Shared Function InClausePlaceholders(count As Integer) As String
            Return String.Join(",", Enumerable.Range(0, count).Select(Function(i) $"@in{i}"))
        End Function

        Friend Shared Sub AddInClauseParameters(command As MySqlCommand, values As IReadOnlyList(Of Integer))
            For index As Integer = 0 To values.Count - 1
                command.Parameters.AddWithValue($"@in{index}", values(index))
            Next
        End Sub

    End Class

End Namespace
