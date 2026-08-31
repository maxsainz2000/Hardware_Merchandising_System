' Merchandising.Infrastructure.Data.SalesReturnRepository
'
' P5-11: writes and locked reads against SalesReturns and SalesReturnLines
' (db/migrations/0012_sales-returns.sql), and the locked read against
' SaleLines the bound check depends on.
'
' THE CONCURRENCY MECHANISM, NAMED EXPLICITLY - the identical shape
' PurchaseReturnRepository's header describes for receipt lines. Spec
' section 10.3's "cannot exceed the quantity sold minus prior returns" has
' no CHECK-constraint backstop, because the bound is an AGGREGATE over every
' prior sibling SalesReturnLines row for the same SaleLineId - a value a
' single-row CHECK cannot see. So the bound is enforced here, at the API,
' computed from committed rows inside a transaction - and
' GetSaleLineForUpdateAsync's SELECT ... FOR UPDATE is what makes that safe
' under concurrency. Locking a row this class never writes to is
' deliberate: SaleLines has no UPDATE grant to merch_api (db/grants/0013 -
' INSERT only), but SELECT ... FOR UPDATE needs only SELECT privilege,
' which merch_api already holds at the database level (ADR-013). Two return
' attempts against the SAME SaleLineId cannot both proceed past this lock.
'
' PRIOR RETURNED INCLUDES PendingApproval, EXCLUDES Rejected.
' GetPriorReturnedQuantityAsync sums every sibling SalesReturnLines row
' whose parent SalesReturns.Status is NOT Rejected - a Rejected return never
' happened, so it must not consume the bound; a PendingApproval return
' already claims its lines against the bound (the units are already
' considered "returned" pending only the exceptional-approval decision, not
' a decision about whether the return itself occurred) - otherwise two
' large returns against the same line could both sit Pending and both later
' be approved, together exceeding what was actually sold.
'
' EVERY WRITE HERE TAKES A TRANSACTION, WITH NO OPTIONAL OVERLOAD -
' SalesReturnService owns it (ADR-006), the same arrangement every other
' repository in this solution uses.
'
' P6-03: SearchAsync/GetLinesForReturnsAsync are the first PLAIN, NON-LOCKING
' reads this class carries - GET /api/v1/sales/returns, the detail endpoint
' the returns-and-cancellations and product-performance reports reconcile
' against (docs/report-specification.md section 7), the same scope addition
' P6-02 made for GET /api/v1/sales. Lines are fetched in a SECOND query,
' keyed by the page's own return ids, rather than one JOIN across the
' one-to-many SalesReturnLines table - SaleRepository.GetLinesForSalesAsync's
' identical "paginate the header, then fetch children for just this page"
' shape.

Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Merchandising.Domain.Entities
Imports Merchandising.Domain.Sales
Imports MySqlConnector

Namespace Data

    ''' <summary>The one sort field <see cref="SalesReturnRepository.SearchAsync"/> honours - kept as an enum, not a bare "always ReturnedAt", the same StockMovementSortField precedent ReportRepository's header names.</summary>
    Public Enum SalesReturnSortField
        ReturnedAt
    End Enum

    Public NotInheritable Class SalesReturnRepository

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Locks one SaleLines row with <c>SELECT ... FOR UPDATE</c>, inside
        ''' <paramref name="transaction"/> - see this file's header for why
        ''' this is the concurrency mechanism rather than a conditional
        ''' UPDATE. Found = False when no such line exists.
        ''' </summary>
        Public Shared Async Function GetSaleLineForUpdateAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            saleLineId As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Found As Boolean, SaleId As Integer, ProductId As Integer, Quantity As Decimal, UnitPrice As Decimal))

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "SELECT SaleId, ProductId, Quantity, UnitPrice " &
                    "  FROM SaleLines " &
                    " WHERE Id = @id " &
                    " FOR UPDATE;"
                command.Parameters.AddWithValue("@id", saleLineId)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    If Not Await reader.ReadAsync(cancellationToken).ConfigureAwait(False) Then
                        Return (Found:=False, SaleId:=0, ProductId:=0, Quantity:=0D, UnitPrice:=0D)
                    End If

                    Return (Found:=True,
                            SaleId:=reader.GetInt32(0),
                            ProductId:=reader.GetInt32(1),
                            Quantity:=reader.GetDecimal(2),
                            UnitPrice:=reader.GetDecimal(3))
                End Using
            End Using

        End Function

        ''' <summary>
        ''' Sum of every non-Rejected SalesReturnLines.QuantityReturned for
        ''' <paramref name="saleLineId"/>, 0 when none exist - see this
        ''' file's header for why PendingApproval counts and Rejected does
        ''' not. Read inside <paramref name="transaction"/>, AFTER the
        ''' caller has already locked the parent SaleLines row via
        ''' <see cref="GetSaleLineForUpdateAsync"/>.
        ''' </summary>
        Public Shared Async Function GetPriorReturnedQuantityAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            saleLineId As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Decimal)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "SELECT COALESCE(SUM(srl.QuantityReturned), 0.000) " &
                    "  FROM SalesReturnLines srl " &
                    "  JOIN SalesReturns sr ON sr.Id = srl.SalesReturnId " &
                    " WHERE srl.SaleLineId = @saleLineId " &
                    "   AND sr.Status <> @rejectedStatus;"
                command.Parameters.AddWithValue("@saleLineId", saleLineId)
                command.Parameters.AddWithValue("@rejectedStatus", SalesReturnStatus.Rejected.ToString())

                Return CDec(Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False))
            End Using

        End Function

        ''' <summary>
        ''' Inserts the return header. <paramref name="refundMethod"/>/
        ''' <paramref name="refundAmount"/> must both be supplied when
        ''' <paramref name="status"/> is Completed (the direct, within-scope
        ''' path) and both Nothing when it is PendingApproval - 0012's
        ''' CK_SalesReturns_RefundPairing is the backstop if that pairing is
        ''' ever wrong. ApprovedByUserId/ApprovedAtUtc are never set here -
        ''' nobody approved a within-scope return; only
        ''' <see cref="MarkCompletedAsync"/>/<see cref="MarkRejectedAsync"/>
        ''' ever set them, for an exceptional return.
        ''' </summary>
        ''' <returns>The new return's Id.</returns>
        Public Shared Async Function InsertSalesReturnAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            saleId As Integer,
            returnedByUserId As Integer,
            reason As String,
            exceedsThreshold As Boolean,
            status As SalesReturnStatus,
            refundMethod As String,
            refundAmount As Decimal?,
            returnedAtUtc As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Integer)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO SalesReturns " &
                    "(SaleId, ReturnedByUserId, Reason, ExceedsThreshold, Status, RefundMethod, RefundAmount, " &
                    " ReturnedAtUtc, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@saleId, @returnedByUserId, @reason, @exceedsThreshold, @status, @refundMethod, @refundAmount, " &
                    " @returnedAtUtc, 0, @returnedAtUtc, @returnedAtUtc);"
                command.Parameters.AddWithValue("@saleId", saleId)
                command.Parameters.AddWithValue("@returnedByUserId", returnedByUserId)
                command.Parameters.AddWithValue("@reason", reason)
                command.Parameters.AddWithValue("@exceedsThreshold", If(exceedsThreshold, 1, 0))
                command.Parameters.AddWithValue("@status", status.ToString())
                command.Parameters.AddWithValue("@refundMethod", If(CObj(refundMethod), DBNull.Value))
                command.Parameters.AddWithValue("@refundAmount", If(CObj(refundAmount), DBNull.Value))
                command.Parameters.AddWithValue("@returnedAtUtc", returnedAtUtc)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return CInt(command.LastInsertedId)
            End Using

        End Function

        ''' <summary>Inserts one return line. ProductId and UnitPrice are the CALLER's responsibility to have already read from the locked SaleLines row - never client-supplied (CreateSalesReturnLineRequest's header).</summary>
        ''' <returns>The new line's Id.</returns>
        Public Shared Async Function InsertSalesReturnLineAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            salesReturnId As Integer,
            saleLineId As Integer,
            productId As Integer,
            quantityReturned As Decimal,
            restocksItem As Boolean,
            createdAtUtc As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Integer)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO SalesReturnLines " &
                    "(SalesReturnId, SaleLineId, ProductId, QuantityReturned, RestocksItem, CreatedAtUtc) " &
                    "VALUES (@salesReturnId, @saleLineId, @productId, @quantityReturned, @restocksItem, @createdAtUtc);"
                command.Parameters.AddWithValue("@salesReturnId", salesReturnId)
                command.Parameters.AddWithValue("@saleLineId", saleLineId)
                command.Parameters.AddWithValue("@productId", productId)
                command.Parameters.AddWithValue("@quantityReturned", quantityReturned)
                command.Parameters.AddWithValue("@restocksItem", If(restocksItem, 1, 0))
                command.Parameters.AddWithValue("@createdAtUtc", createdAtUtc)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return CInt(command.LastInsertedId)
            End Using

        End Function

        ''' <summary>
        ''' Locks one SalesReturns row with <c>SELECT ... FOR UPDATE</c>,
        ''' inside <paramref name="transaction"/> - the same "lock, then
        ''' decide, then write" shape AdjustmentRepository.GetForUpdateAsync
        ''' uses. Found = False when no such return exists.
        ''' </summary>
        Public Shared Async Function GetForUpdateAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            id As Integer,
            Optional cancellationToken As CancellationToken = Nothing) _
            As Task(Of (Found As Boolean, SaleId As Integer, ReturnedByUserId As Integer, Status As SalesReturnStatus))

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "SELECT SaleId, ReturnedByUserId, Status " &
                    "  FROM SalesReturns WHERE Id = @id FOR UPDATE;"
                command.Parameters.AddWithValue("@id", id)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    If Not Await reader.ReadAsync(cancellationToken).ConfigureAwait(False) Then
                        Return (Found:=False, SaleId:=0, ReturnedByUserId:=0, Status:=CType(0, SalesReturnStatus))
                    End If

                    Return (Found:=True,
                            SaleId:=reader.GetInt32(0),
                            ReturnedByUserId:=reader.GetInt32(1),
                            Status:=ParseStatus(reader.GetString(2)))
                End Using
            End Using

        End Function

        ''' <summary>
        ''' Moves a locked (PendingApproval) return to Completed, recording
        ''' the approver and the refund finally decided for it. Always
        ''' called after <see cref="GetForUpdateAsync"/> has already locked
        ''' and confirmed the row within the same transaction, so the
        ''' affected-row count here is a defensive check, not the mechanism
        ''' that prevents a lost update - the row lock is (the same
        ''' arrangement AdjustmentRepository.MarkAppliedAsync uses).
        ''' </summary>
        Public Shared Async Function MarkCompletedAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            id As Integer,
            approvedByUserId As Integer,
            refundMethod As String,
            refundAmount As Decimal,
            approvedAtUtc As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Boolean)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "UPDATE SalesReturns " &
                    "   SET Status = @status, " &
                    "       ApprovedByUserId = @approvedByUserId, " &
                    "       ApprovedAtUtc = @approvedAtUtc, " &
                    "       RefundMethod = @refundMethod, " &
                    "       RefundAmount = @refundAmount, " &
                    "       RowVersion = RowVersion + 1, " &
                    "       UpdatedAtUtc = @approvedAtUtc " &
                    " WHERE Id = @id;"
                command.Parameters.AddWithValue("@status", SalesReturnStatus.Completed.ToString())
                command.Parameters.AddWithValue("@approvedByUserId", approvedByUserId)
                command.Parameters.AddWithValue("@approvedAtUtc", approvedAtUtc)
                command.Parameters.AddWithValue("@refundMethod", refundMethod)
                command.Parameters.AddWithValue("@refundAmount", refundAmount)
                command.Parameters.AddWithValue("@id", id)

                Dim affectedRows As Integer = Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return affectedRows = 1
            End Using

        End Function

        ''' <summary>Moves a locked (PendingApproval) return to Rejected. No stock or refund effect, ever - same locked-row arrangement as <see cref="MarkCompletedAsync"/>.</summary>
        Public Shared Async Function MarkRejectedAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            id As Integer,
            approvedByUserId As Integer,
            approvedAtUtc As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Boolean)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "UPDATE SalesReturns " &
                    "   SET Status = @status, " &
                    "       ApprovedByUserId = @approvedByUserId, " &
                    "       ApprovedAtUtc = @approvedAtUtc, " &
                    "       RowVersion = RowVersion + 1, " &
                    "       UpdatedAtUtc = @approvedAtUtc " &
                    " WHERE Id = @id;"
                command.Parameters.AddWithValue("@status", SalesReturnStatus.Rejected.ToString())
                command.Parameters.AddWithValue("@approvedByUserId", approvedByUserId)
                command.Parameters.AddWithValue("@approvedAtUtc", approvedAtUtc)
                command.Parameters.AddWithValue("@id", id)

                Dim affectedRows As Integer = Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return affectedRows = 1
            End Using

        End Function

        ''' <summary>A plain, non-locking read of one return header - for building a response after a write already committed.</summary>
        Public Shared Async Function GetByIdAsync(
            connection As MySqlConnection,
            id As Integer,
            Optional cancellationToken As CancellationToken = Nothing,
            Optional transaction As MySqlTransaction = Nothing) As Task(Of SalesReturn)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "SELECT Id, SaleId, ReturnedByUserId, ApprovedByUserId, Reason, ExceedsThreshold, Status, " &
                    "       RefundMethod, RefundAmount, ReturnedAtUtc, ApprovedAtUtc, RowVersion, CreatedAtUtc, UpdatedAtUtc " &
                    "  FROM SalesReturns WHERE Id = @id;"
                command.Parameters.AddWithValue("@id", id)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    If Not Await reader.ReadAsync(cancellationToken).ConfigureAwait(False) Then
                        Return Nothing
                    End If
                    Return ReadSalesReturn(reader)
                End Using
            End Using

        End Function

        ''' <summary>
        ''' Every SalesReturnLines row for <paramref name="salesReturnId"/>,
        ''' joined to SaleLines (UnitPrice) and Products (Sku, Name) - the
        ''' projection every response and refund/stock recomputation needs.
        ''' Never locked: SalesReturnLines is INSERT-only after creation
        ''' (db/grants/0014), so nothing can change under a concurrent
        ''' reader once <see cref="GetForUpdateAsync"/> has locked the
        ''' parent header for a status transition.
        ''' </summary>
        Public Shared Async Function GetLinesAsync(
            connection As MySqlConnection,
            salesReturnId As Integer,
            Optional cancellationToken As CancellationToken = Nothing,
            Optional transaction As MySqlTransaction = Nothing) As Task(Of IReadOnlyList(Of SalesReturnLine))

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "SELECT srl.Id, srl.SaleLineId, srl.ProductId, p.Sku, p.Name, srl.QuantityReturned, " &
                    "       sl.UnitPrice, srl.RestocksItem, srl.CreatedAtUtc " &
                    "  FROM SalesReturnLines srl " &
                    "  JOIN SaleLines sl ON sl.Id = srl.SaleLineId " &
                    "  JOIN Products p ON p.Id = srl.ProductId " &
                    " WHERE srl.SalesReturnId = @salesReturnId " &
                    " ORDER BY srl.Id;"
                command.Parameters.AddWithValue("@salesReturnId", salesReturnId)

                Dim lines As New List(Of SalesReturnLine)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)

                        lines.Add(New SalesReturnLine With {
                            .Id = reader.GetInt32(0),
                            .SalesReturnId = salesReturnId,
                            .SaleLineId = reader.GetInt32(1),
                            .ProductId = reader.GetInt32(2),
                            .ProductSku = reader.GetString(3),
                            .ProductName = reader.GetString(4),
                            .QuantityReturned = reader.GetDecimal(5),
                            .UnitPrice = reader.GetDecimal(6),
                            .RestocksItem = reader.GetBoolean(7),
                            .CreatedAtUtc = reader.GetDateTime(8)
                        })

                    End While
                End Using

                Return lines.AsReadOnly()
            End Using

        End Function

        ''' <summary>
        ''' Paginated, filtered, sorted list of return HEADERS only - lines
        ''' are fetched separately by <see cref="GetLinesForReturnsAsync"/>
        ''' once the page's own ids are known (this class's own header
        ''' explains why). <paramref name="fromUtc"/>/<paramref name="toUtcExclusive"/>
        ''' are already-converted UTC instants filtering ReturnedAtUtc - the
        ''' caller does the store-local-to-UTC conversion. Every status
        ''' appears (PendingApproval, Completed, Rejected) - this is a plain
        ''' list, never filtered to only-committed rows (docs/report-
        ''' specification.md section 4 row 5). <paramref name="page"/>/
        ''' <paramref name="pageSize"/> must already be clamped by the
        ''' caller.
        ''' </summary>
        Public Shared Async Function SearchAsync(
            connection As MySqlConnection,
            fromUtc As DateTime?,
            toUtcExclusive As DateTime?,
            sortDescending As Boolean,
            page As Integer,
            pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) _
            As Task(Of (Items As IReadOnlyList(Of SalesReturn), TotalCount As Integer))

            Dim conditions As New List(Of String)

            If fromUtc.HasValue Then
                conditions.Add("ReturnedAtUtc >= @fromUtc")
            End If

            If toUtcExclusive.HasValue Then
                conditions.Add("ReturnedAtUtc < @toUtcExclusive")
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
                End Sub

            Dim totalCount As Integer

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM SalesReturns" & whereClause & ";"
                addFilterParameters(command)
                totalCount = CInt(Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False))
            End Using

            Dim items As New List(Of SalesReturn)

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT Id, SaleId, ReturnedByUserId, ApprovedByUserId, Reason, ExceedsThreshold, Status, " &
                    "       RefundMethod, RefundAmount, ReturnedAtUtc, ApprovedAtUtc, RowVersion, CreatedAtUtc, UpdatedAtUtc " &
                    "  FROM SalesReturns" &
                    whereClause &
                    " ORDER BY ReturnedAtUtc " & If(sortDescending, "DESC", "ASC") & ", Id " & If(sortDescending, "DESC", "ASC") &
                    " LIMIT @pageSize OFFSET @offset;"
                addFilterParameters(command)
                command.Parameters.AddWithValue("@pageSize", pageSize)
                command.Parameters.AddWithValue("@offset", (page - 1) * pageSize)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        items.Add(ReadSalesReturn(reader))
                    End While
                End Using
            End Using

            Return (Items:=items, TotalCount:=totalCount)

        End Function

        ''' <summary>Every SalesReturnLines row for the given return ids, joined to SaleLines (UnitPrice) and Products (Sku, Name) - same projection as <see cref="GetLinesAsync"/>, batched for many returns at once. Empty when <paramref name="salesReturnIds"/> is empty.</summary>
        Public Shared Async Function GetLinesForReturnsAsync(
            connection As MySqlConnection,
            salesReturnIds As IReadOnlyList(Of Integer),
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of ILookup(Of Integer, SalesReturnLine))

            If salesReturnIds.Count = 0 Then
                Return Array.Empty(Of SalesReturnLine)().ToLookup(Function(l) l.SalesReturnId)
            End If

            Dim lines As New List(Of SalesReturnLine)

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT srl.SalesReturnId, srl.Id, srl.SaleLineId, srl.ProductId, p.Sku, p.Name, srl.QuantityReturned, " &
                    "       sl.UnitPrice, srl.RestocksItem, srl.CreatedAtUtc " &
                    "  FROM SalesReturnLines srl " &
                    "  JOIN SaleLines sl ON sl.Id = srl.SaleLineId " &
                    "  JOIN Products p ON p.Id = srl.ProductId " &
                    " WHERE srl.SalesReturnId IN (" & SaleRepository.InClausePlaceholders(salesReturnIds.Count) & ") " &
                    " ORDER BY srl.SalesReturnId, srl.Id;"
                SaleRepository.AddInClauseParameters(command, salesReturnIds)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)

                        lines.Add(New SalesReturnLine With {
                            .SalesReturnId = reader.GetInt32(0),
                            .Id = reader.GetInt32(1),
                            .SaleLineId = reader.GetInt32(2),
                            .ProductId = reader.GetInt32(3),
                            .ProductSku = reader.GetString(4),
                            .ProductName = reader.GetString(5),
                            .QuantityReturned = reader.GetDecimal(6),
                            .UnitPrice = reader.GetDecimal(7),
                            .RestocksItem = reader.GetBoolean(8),
                            .CreatedAtUtc = reader.GetDateTime(9)
                        })

                    End While
                End Using
            End Using

            Return lines.ToLookup(Function(l) l.SalesReturnId)

        End Function

        Private Shared Function ReadSalesReturn(reader As MySqlDataReader) As SalesReturn

            Dim approvedByOrdinal As Integer = reader.GetOrdinal("ApprovedByUserId")
            Dim refundMethodOrdinal As Integer = reader.GetOrdinal("RefundMethod")
            Dim refundAmountOrdinal As Integer = reader.GetOrdinal("RefundAmount")
            Dim approvedAtOrdinal As Integer = reader.GetOrdinal("ApprovedAtUtc")

            Return New SalesReturn With {
                .Id = reader.GetInt32(reader.GetOrdinal("Id")),
                .SaleId = reader.GetInt32(reader.GetOrdinal("SaleId")),
                .ReturnedByUserId = reader.GetInt32(reader.GetOrdinal("ReturnedByUserId")),
                .ApprovedByUserId = If(reader.IsDBNull(approvedByOrdinal), CType(Nothing, Integer?), reader.GetInt32(approvedByOrdinal)),
                .Reason = reader.GetString(reader.GetOrdinal("Reason")),
                .ExceedsThreshold = reader.GetBoolean(reader.GetOrdinal("ExceedsThreshold")),
                .Status = ParseStatus(reader.GetString(reader.GetOrdinal("Status"))),
                .RefundMethod = If(reader.IsDBNull(refundMethodOrdinal), Nothing, reader.GetString(refundMethodOrdinal)),
                .RefundAmount = If(reader.IsDBNull(refundAmountOrdinal), CType(Nothing, Decimal?), reader.GetDecimal(refundAmountOrdinal)),
                .ReturnedAtUtc = reader.GetDateTime(reader.GetOrdinal("ReturnedAtUtc")),
                .ApprovedAtUtc = If(reader.IsDBNull(approvedAtOrdinal), CType(Nothing, DateTime?), reader.GetDateTime(approvedAtOrdinal)),
                .RowVersion = reader.GetInt64(reader.GetOrdinal("RowVersion")),
                .CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("CreatedAtUtc")),
                .UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("UpdatedAtUtc"))
            }

        End Function

        Private Shared Function ParseStatus(storedName As String) As SalesReturnStatus
            Return CType([Enum].Parse(GetType(SalesReturnStatus), storedName), SalesReturnStatus)
        End Function

    End Class

End Namespace
