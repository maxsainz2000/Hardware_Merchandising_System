' Merchandising.Infrastructure.Data.PurchaseOrderRepository
'
' P3-03: writes and reads against PurchaseOrders and PurchaseOrderLines
' (db/migrations/0008_purchase-orders.sql).
'
' EVERY WRITE HERE TAKES A TRANSACTION, WITH NO OPTIONAL OVERLOAD. A purchase
' order and its lines are one thing; an order committed without its lines
' would be a document that says nothing, and 0008's CK_PurchaseOrderLines_*
' constraints could reject a line after its header was already durable. There
' is deliberately no way to call these outside a caller's transaction -
' PurchaseOrderService owns it (ADR-006).
'
' A DUPLICATE ORDER NUMBER IS AN OUTCOME, NOT AN EXCEPTION. InsertOrderAsync
' catches ERROR 1062 from UQ_PurchaseOrders_OrderNumber and reports it, the
' same translation SupplierRepository.InsertAsync does for duplicate names.
' The difference is what the caller does with it: a duplicate supplier name
' is the user's problem and becomes a 409, whereas a duplicate order number
' is the SERVER's own race and is retried invisibly - see
' PurchaseOrderNumberGenerator's header.
'
' READS JOIN FOR NAMES RATHER THAN CAPTURING THEM. Supplier name, product SKU
' and product name come from their own tables at read time; only PurchaseCost
' is captured on the line. PurchaseOrderLineResponse's header carries the
' reasoning, and FK RESTRICT is what makes the join total.
'
' SORTING IS AN ENUM, NOT A STRING. SearchAsync cannot be handed a column
' name, so no caller - present or future, careful or not - can concatenate
' one into the ORDER BY. The controller maps a validated whitelist entry onto
' PurchaseOrderSortField; anything else is refused before it reaches here.

Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Tasks
Imports Merchandising.Domain.Entities
Imports Merchandising.Domain.Procurement
Imports MySqlConnector

Namespace Data

    ''' <summary>Outcome of a purchase-order header insert.</summary>
    Public Enum PurchaseOrderWriteOutcomeKind
        Success
        DuplicateOrderNumber
    End Enum

    ''' <summary>
    ''' The sort fields <see cref="PurchaseOrderRepository.SearchAsync"/> will
    ''' honour. A closed set by construction - see the class header.
    ''' </summary>
    Public Enum PurchaseOrderSortField
        CreatedAt
        OrderNumber
        Status
    End Enum

    Public NotInheritable Class PurchaseOrderRepository

        Private Const OrderColumns As String =
            "o.Id, o.OrderNumber, o.SupplierId, s.Name AS SupplierName, o.Status, o.RequestedByUserId, " &
            "o.ApprovedByUserId, o.SubmittedAtUtc, o.ApprovedAtUtc, o.RowVersion, o.CreatedAtUtc, o.UpdatedAtUtc"

        Private Const LineColumns As String =
            "l.Id, l.PurchaseOrderId, l.LineNumber, l.ProductId, p.Sku AS ProductSku, p.Name AS ProductName, " &
            "l.OrderedQuantity, l.PurchaseCost, l.ReceivedQuantity, l.RowVersion, l.CreatedAtUtc, l.UpdatedAtUtc"

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Inserts the order header inside <paramref name="transaction"/>,
        ''' always as <see cref="PurchaseOrderStatus.Draft"/>. Reports a
        ''' duplicate order number rather than throwing, so the caller can
        ''' retry with a fresh candidate.
        ''' </summary>
        Public Shared Async Function InsertOrderAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            orderNumber As String,
            supplierId As Integer,
            requestedByUserId As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Kind As PurchaseOrderWriteOutcomeKind, PurchaseOrderId As Integer))

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                ' Status is written as the enum NAME (ADR-020), which is what
                ' 0008's CHECK constraint accepts byte for byte.
                command.CommandText =
                    "INSERT INTO PurchaseOrders " &
                    "(OrderNumber, SupplierId, Status, RequestedByUserId, RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@orderNumber, @supplierId, @status, @requestedByUserId, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@orderNumber", orderNumber)
                command.Parameters.AddWithValue("@supplierId", supplierId)
                command.Parameters.AddWithValue("@status", PurchaseOrderStatus.Draft.ToString())
                command.Parameters.AddWithValue("@requestedByUserId", requestedByUserId)

                Try
                    Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                    Return (Kind:=PurchaseOrderWriteOutcomeKind.Success, PurchaseOrderId:=CInt(command.LastInsertedId))

                Catch ex As MySqlException When ex.ErrorCode = MySqlErrorCode.DuplicateKeyEntry
                    Return (Kind:=PurchaseOrderWriteOutcomeKind.DuplicateOrderNumber, PurchaseOrderId:=0)
                End Try

            End Using

        End Function

        ''' <summary>
        ''' Inserts one line inside <paramref name="transaction"/>.
        ''' <paramref name="lineNumber"/> is server-assigned 1..N;
        ''' ReceivedQuantity is left at its zero default for Phase 4 to move.
        ''' </summary>
        Public Shared Async Function InsertLineAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            purchaseOrderId As Integer,
            lineNumber As Integer,
            productId As Integer,
            orderedQuantity As Decimal,
            purchaseCost As Decimal,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Integer)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO PurchaseOrderLines " &
                    "(PurchaseOrderId, LineNumber, ProductId, OrderedQuantity, PurchaseCost, ReceivedQuantity, " &
                    " RowVersion, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@purchaseOrderId, @lineNumber, @productId, @orderedQuantity, @purchaseCost, 0.000, " &
                    " 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@purchaseOrderId", purchaseOrderId)
                command.Parameters.AddWithValue("@lineNumber", lineNumber)
                command.Parameters.AddWithValue("@productId", productId)
                command.Parameters.AddWithValue("@orderedQuantity", orderedQuantity)
                command.Parameters.AddWithValue("@purchaseCost", purchaseCost)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return CInt(command.LastInsertedId)
            End Using

        End Function

        ''' <summary>
        ''' Locks one order's Status and RequestedByUserId with
        ''' <c>SELECT ... FOR UPDATE</c>, inside <paramref name="transaction"/>.
        ''' P3-04's status-transition writes read through this rather than
        ''' <see cref="GetByIdAsync"/> - the same "lock, then decide, then
        ''' write" shape <c>ProductRepository.ReadPriceForUpdateAsync</c> uses
        ''' for P2-08, and for the same reason: CanTransition's decision and
        ''' the write that acts on it must see the same row no other
        ''' transaction can concurrently move.
        ''' </summary>
        Public Shared Async Function GetStatusForUpdateAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            id As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Found As Boolean, Status As PurchaseOrderStatus, RequestedByUserId As Integer))

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "SELECT Status, RequestedByUserId FROM PurchaseOrders WHERE Id = @id FOR UPDATE;"
                command.Parameters.AddWithValue("@id", id)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    If Not Await reader.ReadAsync(cancellationToken).ConfigureAwait(False) Then
                        Return (Found:=False, Status:=CType(0, PurchaseOrderStatus), RequestedByUserId:=0)
                    End If
                    Return (Found:=True,
                            Status:=ParseStatus(reader.GetString(0)),
                            RequestedByUserId:=reader.GetInt32(1))
                End Using
            End Using

        End Function

        ''' <summary>
        ''' P3-04: moves a locked order to <see cref="PurchaseOrderStatus.Submitted"/>.
        ''' Always called after <see cref="GetStatusForUpdateAsync"/> has
        ''' already locked and confirmed the row within the same transaction,
        ''' so the affected-row count here is a defensive check, not the
        ''' mechanism that prevents a lost update - the row lock is (same
        ''' arrangement as <c>ProductRepository.UpdatePriceCostAsync</c>).
        ''' </summary>
        Public Shared Async Function MarkSubmittedAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            id As Integer,
            submittedAtUtc As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Boolean)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "UPDATE PurchaseOrders " &
                    "   SET Status = @status, " &
                    "       SubmittedAtUtc = @submittedAtUtc, " &
                    "       RowVersion = RowVersion + 1, " &
                    "       UpdatedAtUtc = UTC_TIMESTAMP(6) " &
                    " WHERE Id = @id;"
                command.Parameters.AddWithValue("@status", PurchaseOrderStatus.Submitted.ToString())
                command.Parameters.AddWithValue("@submittedAtUtc", submittedAtUtc)
                command.Parameters.AddWithValue("@id", id)

                Dim affectedRows As Integer = Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return affectedRows = 1
            End Using

        End Function

        ''' <summary>
        ''' P3-04: moves a locked order to <see cref="PurchaseOrderStatus.Approved"/>,
        ''' recording who approved it and when - kept distinct from
        ''' RequestedByUserId so a later read can always tell the two apart
        ''' (ADR-017 section 6). Same locked-row arrangement as
        ''' <see cref="MarkSubmittedAsync"/>.
        ''' </summary>
        Public Shared Async Function MarkApprovedAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            id As Integer,
            approvedByUserId As Integer,
            approvedAtUtc As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Boolean)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "UPDATE PurchaseOrders " &
                    "   SET Status = @status, " &
                    "       ApprovedByUserId = @approvedByUserId, " &
                    "       ApprovedAtUtc = @approvedAtUtc, " &
                    "       RowVersion = RowVersion + 1, " &
                    "       UpdatedAtUtc = UTC_TIMESTAMP(6) " &
                    " WHERE Id = @id;"
                command.Parameters.AddWithValue("@status", PurchaseOrderStatus.Approved.ToString())
                command.Parameters.AddWithValue("@approvedByUserId", approvedByUserId)
                command.Parameters.AddWithValue("@approvedAtUtc", approvedAtUtc)
                command.Parameters.AddWithValue("@id", id)

                Dim affectedRows As Integer = Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return affectedRows = 1
            End Using

        End Function

        ''' <summary>
        ''' P3-05: moves a locked order to <paramref name="targetStatus"/> -
        ''' shared by Cancel and Close, neither of which sets any column
        ''' beyond Status (contrast <see cref="MarkSubmittedAsync"/> /
        ''' <see cref="MarkApprovedAsync"/>, which each own a distinct
        ''' timestamp/actor column 0008 defines). Same locked-row arrangement
        ''' as those two: the affected-row count here is a defensive check,
        ''' not the mechanism that prevents a lost update - the row lock
        ''' <see cref="GetStatusForUpdateAsync"/> already took is.
        ''' </summary>
        Public Shared Async Function MarkStatusAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            id As Integer,
            targetStatus As PurchaseOrderStatus,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Boolean)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "UPDATE PurchaseOrders " &
                    "   SET Status = @status, " &
                    "       RowVersion = RowVersion + 1, " &
                    "       UpdatedAtUtc = UTC_TIMESTAMP(6) " &
                    " WHERE Id = @id;"
                command.Parameters.AddWithValue("@status", targetStatus.ToString())
                command.Parameters.AddWithValue("@id", id)

                Dim affectedRows As Integer = Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return affectedRows = 1
            End Using

        End Function

        ''' <summary>
        ''' P4-05: locks every line of <paramref name="purchaseOrderId"/> with
        ''' <c>SELECT ... FOR UPDATE</c>, inside <paramref name="transaction"/>.
        ''' ReceivingService reads the WHOLE order's lines this way, not just
        ''' the ones a given receipt touches - deciding ReceivePartially vs.
        ''' ReceiveFully (ADR-020) requires knowing whether EVERY line will be
        ''' at its ordered quantity after this receipt, and two receipts
        ''' racing against the same order (even against different lines) must
        ''' serialize on that decision rather than each computing it from a
        ''' stale read. The same "lock, then decide, then write" shape
        ''' <see cref="GetStatusForUpdateAsync"/> already uses for the status
        ''' column.
        ''' </summary>
        Public Shared Async Function GetLinesForUpdateAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            purchaseOrderId As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of IReadOnlyList(Of PurchaseOrderLine))

            Dim lines As New List(Of PurchaseOrderLine)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "SELECT Id, PurchaseOrderId, LineNumber, ProductId, OrderedQuantity, PurchaseCost, " &
                    "       ReceivedQuantity, RowVersion, CreatedAtUtc, UpdatedAtUtc " &
                    "  FROM PurchaseOrderLines " &
                    " WHERE PurchaseOrderId = @purchaseOrderId " &
                    " ORDER BY LineNumber " &
                    " FOR UPDATE;"
                command.Parameters.AddWithValue("@purchaseOrderId", purchaseOrderId)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        lines.Add(New PurchaseOrderLine With {
                            .Id = reader.GetInt32(reader.GetOrdinal("Id")),
                            .PurchaseOrderId = reader.GetInt32(reader.GetOrdinal("PurchaseOrderId")),
                            .LineNumber = reader.GetInt32(reader.GetOrdinal("LineNumber")),
                            .ProductId = reader.GetInt32(reader.GetOrdinal("ProductId")),
                            .OrderedQuantity = reader.GetDecimal(reader.GetOrdinal("OrderedQuantity")),
                            .PurchaseCost = reader.GetDecimal(reader.GetOrdinal("PurchaseCost")),
                            .ReceivedQuantity = reader.GetDecimal(reader.GetOrdinal("ReceivedQuantity")),
                            .RowVersion = reader.GetInt64(reader.GetOrdinal("RowVersion")),
                            .CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("CreatedAtUtc")),
                            .UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("UpdatedAtUtc"))
                        })
                    End While
                End Using
            End Using

            Return lines

        End Function

        ''' <summary>
        ''' P4-05: accumulates <paramref name="quantityReceived"/> onto one
        ''' line's ReceivedQuantity. The affected-row count here is a
        ''' defensive check, not the mechanism that prevents a lost update -
        ''' <see cref="GetLinesForUpdateAsync"/> already holds the row lock
        ''' (same arrangement as <see cref="MarkStatusAsync"/>). 0008's
        ''' CK_PurchaseOrderLines_ReceivedQuantity is the last-resort backstop
        ''' against over-receiving (ADR-013); ReceivingService computes the
        ''' remaining quantity from the locked read before ever reaching this
        ''' call, so reaching the constraint here would mean that computation
        ''' was wrong, not that a caller supplied a bad quantity.
        ''' </summary>
        Public Shared Async Function IncrementReceivedQuantityAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            purchaseOrderLineId As Integer,
            quantityReceived As Decimal,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Boolean)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "UPDATE PurchaseOrderLines " &
                    "   SET ReceivedQuantity = ReceivedQuantity + @quantityReceived, " &
                    "       RowVersion = RowVersion + 1, " &
                    "       UpdatedAtUtc = UTC_TIMESTAMP(6) " &
                    " WHERE Id = @id;"
                command.Parameters.AddWithValue("@quantityReceived", quantityReceived)
                command.Parameters.AddWithValue("@id", purchaseOrderLineId)

                Dim affectedRows As Integer = Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
                Return affectedRows = 1
            End Using

        End Function

        ''' <summary>
        ''' Reads one order with every line, ordered by LineNumber. Nothing if
        ''' no such order exists. <paramref name="transaction"/> is optional so
        ''' the creating transaction can read back what it has just written,
        ''' before committing.
        ''' </summary>
        Public Shared Async Function GetByIdAsync(
            connection As MySqlConnection,
            id As Integer,
            Optional cancellationToken As CancellationToken = Nothing,
            Optional transaction As MySqlTransaction = Nothing) As Task(Of PurchaseOrder)

            Dim order As PurchaseOrder = Nothing

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "SELECT " & OrderColumns &
                    "  FROM PurchaseOrders o" &
                    "  JOIN Suppliers s ON s.Id = o.SupplierId" &
                    " WHERE o.Id = @id;"
                command.Parameters.AddWithValue("@id", id)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    If Await reader.ReadAsync(cancellationToken).ConfigureAwait(False) Then
                        order = ReadOrder(reader)
                    End If
                End Using
            End Using

            If order Is Nothing Then
                Return Nothing
            End If

            Dim lines As New List(Of PurchaseOrderLine)

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText =
                    "SELECT " & LineColumns &
                    "  FROM PurchaseOrderLines l" &
                    "  JOIN Products p ON p.Id = l.ProductId" &
                    " WHERE l.PurchaseOrderId = @purchaseOrderId" &
                    " ORDER BY l.LineNumber;"
                command.Parameters.AddWithValue("@purchaseOrderId", id)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        lines.Add(ReadOrderLine(reader))
                    End While
                End Using
            End Using

            order.Lines = lines
            order.LineCount = lines.Count

            Return order

        End Function

        ''' <summary>
        ''' Paginated, filtered, sorted list of order HEADERS - lines are
        ''' deliberately not fetched (PurchaseOrderSummaryResponse's header
        ''' explains why a list page must not carry them). Both filters are
        ''' optional; <paramref name="page"/> and <paramref name="pageSize"/>
        ''' must already have been clamped by the caller.
        ''' </summary>
        Public Shared Async Function SearchAsync(
            connection As MySqlConnection,
            supplierId As Integer?,
            status As PurchaseOrderStatus?,
            sortField As PurchaseOrderSortField,
            sortDescending As Boolean,
            page As Integer,
            pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Items As IReadOnlyList(Of PurchaseOrder), TotalCount As Integer))

            Dim conditions As New List(Of String)

            If supplierId.HasValue Then
                conditions.Add("o.SupplierId = @supplierId")
            End If

            If status.HasValue Then
                ' Explicit COLLATE so this comparison is byte-exact and
                ' deterministic whatever the parameter's own collation is -
                ' the same reasoning that put utf8mb4_bin on the column in
                ' 0008. The value is always one of the seven enum names, so
                ' this changes no result; it removes an "Illegal mix of
                ' collations" failure mode rather than a wrong answer.
                conditions.Add("o.Status = @status COLLATE utf8mb4_bin")
            End If

            Dim whereClause As String =
                If(conditions.Count > 0, " WHERE " & String.Join(" AND ", conditions), String.Empty)

            Dim totalCount As Integer

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM PurchaseOrders o" & whereClause & ";"
                AddFilterParameters(command, supplierId, status)
                totalCount = CInt(Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False))
            End Using

            Dim items As New List(Of PurchaseOrder)

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT " & OrderColumns & ", " &
                    "       (SELECT COUNT(*) FROM PurchaseOrderLines l WHERE l.PurchaseOrderId = o.Id) AS LineCount" &
                    "  FROM PurchaseOrders o" &
                    "  JOIN Suppliers s ON s.Id = o.SupplierId" &
                    whereClause &
                    " ORDER BY " & OrderByClause(sortField, sortDescending) &
                    " LIMIT @pageSize OFFSET @offset;"
                AddFilterParameters(command, supplierId, status)
                command.Parameters.AddWithValue("@pageSize", pageSize)
                command.Parameters.AddWithValue("@offset", (page - 1) * pageSize)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        Dim order As PurchaseOrder = ReadOrder(reader)
                        order.LineCount = reader.GetInt32(reader.GetOrdinal("LineCount"))
                        items.Add(order)
                    End While
                End Using
            End Using

            Return (Items:=items, TotalCount:=totalCount)

        End Function

        ''' <summary>
        ''' P3-06: paginated, filtered, sorted purchase-order HISTORY - spec
        ''' section 14's report shape, one row per order carrying its line
        ''' aggregates (ordered/received quantity and value, outstanding
        ''' quantity), computed here with SUM/GROUP BY rather than by the
        ''' caller re-summing PurchaseOrderResponse.Lines. Every SUM is CAST
        ''' back to its column's own storage scale (ADR-004) - MariaDB widens
        ''' a DECIMAL product/SUM's scale internally, and the response must
        ''' not leak that intermediate precision onto the wire.
        '''
        ''' <paramref name="fromUtc"/>/<paramref name="toUtcExclusive"/> are
        ''' already-converted UTC instants - StoreTimeZone.StartOfDayUtc/
        ''' EndOfDayUtcExclusive is the caller's job (PurchaseOrderService),
        ''' the same "this layer takes UTC, never a store-local value" rule
        ''' every other write/read in this class already follows.
        ''' </summary>
        Public Shared Async Function SearchHistoryAsync(
            connection As MySqlConnection,
            supplierId As Integer?,
            status As PurchaseOrderStatus?,
            fromUtc As DateTime?,
            toUtcExclusive As DateTime?,
            sortField As PurchaseOrderSortField,
            sortDescending As Boolean,
            page As Integer,
            pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Items As IReadOnlyList(Of PurchaseOrderHistoryItem), TotalCount As Integer))

            Dim conditions As New List(Of String)

            If supplierId.HasValue Then
                conditions.Add("o.SupplierId = @supplierId")
            End If

            If status.HasValue Then
                conditions.Add("o.Status = @status COLLATE utf8mb4_bin")
            End If

            If fromUtc.HasValue Then
                conditions.Add("o.CreatedAtUtc >= @fromUtc")
            End If

            If toUtcExclusive.HasValue Then
                conditions.Add("o.CreatedAtUtc < @toUtcExclusive")
            End If

            Dim whereClause As String =
                If(conditions.Count > 0, " WHERE " & String.Join(" AND ", conditions), String.Empty)

            Dim totalCount As Integer

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM PurchaseOrders o" & whereClause & ";"
                AddHistoryFilterParameters(command, supplierId, status, fromUtc, toUtcExclusive)
                totalCount = CInt(Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False))
            End Using

            Dim items As New List(Of PurchaseOrderHistoryItem)

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT o.Id, o.OrderNumber, o.SupplierId, s.Name AS SupplierName, o.Status, " &
                    "       o.RequestedByUserId, o.ApprovedByUserId, o.SubmittedAtUtc, o.ApprovedAtUtc, o.CreatedAtUtc, " &
                    "       CAST(COALESCE(SUM(l.OrderedQuantity), 0) AS DECIMAL(19,3)) AS OrderedQuantity, " &
                    "       CAST(COALESCE(SUM(l.OrderedQuantity * l.PurchaseCost), 0) AS DECIMAL(19,4)) AS OrderedValue, " &
                    "       CAST(COALESCE(SUM(l.ReceivedQuantity), 0) AS DECIMAL(19,3)) AS ReceivedQuantity, " &
                    "       CAST(COALESCE(SUM(l.ReceivedQuantity * l.PurchaseCost), 0) AS DECIMAL(19,4)) AS ReceivedValue, " &
                    "       CAST(COALESCE(SUM(l.OrderedQuantity - l.ReceivedQuantity), 0) AS DECIMAL(19,3)) AS OutstandingQuantity " &
                    "  FROM PurchaseOrders o" &
                    "  JOIN Suppliers s ON s.Id = o.SupplierId" &
                    "  LEFT JOIN PurchaseOrderLines l ON l.PurchaseOrderId = o.Id" &
                    whereClause &
                    " GROUP BY o.Id, o.OrderNumber, o.SupplierId, s.Name, o.Status, " &
                    "          o.RequestedByUserId, o.ApprovedByUserId, o.SubmittedAtUtc, o.ApprovedAtUtc, o.CreatedAtUtc" &
                    " ORDER BY " & OrderByClause(sortField, sortDescending) &
                    " LIMIT @pageSize OFFSET @offset;"
                AddHistoryFilterParameters(command, supplierId, status, fromUtc, toUtcExclusive)
                command.Parameters.AddWithValue("@pageSize", pageSize)
                command.Parameters.AddWithValue("@offset", (page - 1) * pageSize)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        items.Add(ReadHistoryItem(reader))
                    End While
                End Using
            End Using

            Return (Items:=items, TotalCount:=totalCount)

        End Function

        ' --------------------------------------------------------------- helpers

        Private Shared Sub AddFilterParameters(
            command As MySqlCommand, supplierId As Integer?, status As PurchaseOrderStatus?)

            If supplierId.HasValue Then
                command.Parameters.AddWithValue("@supplierId", supplierId.Value)
            End If

            If status.HasValue Then
                command.Parameters.AddWithValue("@status", status.Value.ToString())
            End If

        End Sub

        Private Shared Sub AddHistoryFilterParameters(
            command As MySqlCommand, supplierId As Integer?, status As PurchaseOrderStatus?,
            fromUtc As DateTime?, toUtcExclusive As DateTime?)

            AddFilterParameters(command, supplierId, status)

            If fromUtc.HasValue Then
                command.Parameters.AddWithValue("@fromUtc", fromUtc.Value)
            End If

            If toUtcExclusive.HasValue Then
                command.Parameters.AddWithValue("@toUtcExclusive", toUtcExclusive.Value)
            End If

        End Sub

        ''' <summary>
        ''' Builds the ORDER BY from a closed enum - no caller-supplied text
        ''' reaches it. Id is always the final tie-break so paging is stable:
        ''' two orders created in the same microsecond, or sharing a status,
        ''' would otherwise be free to swap places between page 1 and page 2
        ''' and so be returned twice, or not at all.
        ''' </summary>
        Private Shared Function OrderByClause(sortField As PurchaseOrderSortField, sortDescending As Boolean) As String

            Dim column As String

            Select Case sortField
                Case PurchaseOrderSortField.OrderNumber
                    column = "o.OrderNumber"
                Case PurchaseOrderSortField.Status
                    column = "o.Status"
                Case Else
                    column = "o.CreatedAtUtc"
            End Select

            Dim direction As String = If(sortDescending, " DESC", " ASC")

            Return column & direction & ", o.Id" & direction

        End Function

        Private Shared Function ReadOrder(reader As MySqlDataReader) As PurchaseOrder

            Dim approvedByOrdinal As Integer = reader.GetOrdinal("ApprovedByUserId")
            Dim submittedOrdinal As Integer = reader.GetOrdinal("SubmittedAtUtc")
            Dim approvedAtOrdinal As Integer = reader.GetOrdinal("ApprovedAtUtc")

            Return New PurchaseOrder With {
                .Id = reader.GetInt32(reader.GetOrdinal("Id")),
                .OrderNumber = reader.GetString(reader.GetOrdinal("OrderNumber")),
                .SupplierId = reader.GetInt32(reader.GetOrdinal("SupplierId")),
                .SupplierName = reader.GetString(reader.GetOrdinal("SupplierName")),
                .Status = ParseStatus(reader.GetString(reader.GetOrdinal("Status"))),
                .RequestedByUserId = reader.GetInt32(reader.GetOrdinal("RequestedByUserId")),
                .ApprovedByUserId = If(reader.IsDBNull(approvedByOrdinal), CType(Nothing, Integer?), reader.GetInt32(approvedByOrdinal)),
                .SubmittedAtUtc = If(reader.IsDBNull(submittedOrdinal), CType(Nothing, DateTime?), reader.GetDateTime(submittedOrdinal)),
                .ApprovedAtUtc = If(reader.IsDBNull(approvedAtOrdinal), CType(Nothing, DateTime?), reader.GetDateTime(approvedAtOrdinal)),
                .RowVersion = reader.GetInt64(reader.GetOrdinal("RowVersion")),
                .CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("CreatedAtUtc")),
                .UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("UpdatedAtUtc"))
            }

        End Function

        ''' <summary>
        ''' Turns the stored status name back into the domain enum. A value
        ''' that is not one of the seven names throws - 0008's CHECK
        ''' constraint under utf8mb4_bin is what makes that unreachable, and
        ''' failing loudly here is the right direction if it ever is reached
        ''' (ADR-020): an unrecognised status must never travel onward as a
        ''' string nobody checked.
        ''' </summary>
        Private Shared Function ParseStatus(storedName As String) As PurchaseOrderStatus
            Return CType([Enum].Parse(GetType(PurchaseOrderStatus), storedName), PurchaseOrderStatus)
        End Function

        Private Shared Function ReadOrderLine(reader As MySqlDataReader) As PurchaseOrderLine

            Return New PurchaseOrderLine With {
                .Id = reader.GetInt32(reader.GetOrdinal("Id")),
                .PurchaseOrderId = reader.GetInt32(reader.GetOrdinal("PurchaseOrderId")),
                .LineNumber = reader.GetInt32(reader.GetOrdinal("LineNumber")),
                .ProductId = reader.GetInt32(reader.GetOrdinal("ProductId")),
                .ProductSku = reader.GetString(reader.GetOrdinal("ProductSku")),
                .ProductName = reader.GetString(reader.GetOrdinal("ProductName")),
                .OrderedQuantity = reader.GetDecimal(reader.GetOrdinal("OrderedQuantity")),
                .PurchaseCost = reader.GetDecimal(reader.GetOrdinal("PurchaseCost")),
                .ReceivedQuantity = reader.GetDecimal(reader.GetOrdinal("ReceivedQuantity")),
                .RowVersion = reader.GetInt64(reader.GetOrdinal("RowVersion")),
                .CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("CreatedAtUtc")),
                .UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("UpdatedAtUtc"))
            }

        End Function

        Private Shared Function ReadHistoryItem(reader As MySqlDataReader) As PurchaseOrderHistoryItem

            Dim approvedByOrdinal As Integer = reader.GetOrdinal("ApprovedByUserId")
            Dim submittedOrdinal As Integer = reader.GetOrdinal("SubmittedAtUtc")
            Dim approvedAtOrdinal As Integer = reader.GetOrdinal("ApprovedAtUtc")

            Return New PurchaseOrderHistoryItem With {
                .Id = reader.GetInt32(reader.GetOrdinal("Id")),
                .OrderNumber = reader.GetString(reader.GetOrdinal("OrderNumber")),
                .SupplierId = reader.GetInt32(reader.GetOrdinal("SupplierId")),
                .SupplierName = reader.GetString(reader.GetOrdinal("SupplierName")),
                .Status = ParseStatus(reader.GetString(reader.GetOrdinal("Status"))),
                .RequestedByUserId = reader.GetInt32(reader.GetOrdinal("RequestedByUserId")),
                .ApprovedByUserId = If(reader.IsDBNull(approvedByOrdinal), CType(Nothing, Integer?), reader.GetInt32(approvedByOrdinal)),
                .SubmittedAtUtc = If(reader.IsDBNull(submittedOrdinal), CType(Nothing, DateTime?), reader.GetDateTime(submittedOrdinal)),
                .ApprovedAtUtc = If(reader.IsDBNull(approvedAtOrdinal), CType(Nothing, DateTime?), reader.GetDateTime(approvedAtOrdinal)),
                .CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("CreatedAtUtc")),
                .OrderedQuantity = reader.GetDecimal(reader.GetOrdinal("OrderedQuantity")),
                .OrderedValue = reader.GetDecimal(reader.GetOrdinal("OrderedValue")),
                .ReceivedQuantity = reader.GetDecimal(reader.GetOrdinal("ReceivedQuantity")),
                .ReceivedValue = reader.GetDecimal(reader.GetOrdinal("ReceivedValue")),
                .OutstandingQuantity = reader.GetDecimal(reader.GetOrdinal("OutstandingQuantity"))
            }

        End Function

    End Class

End Namespace
