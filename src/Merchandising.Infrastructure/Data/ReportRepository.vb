' Merchandising.Infrastructure.Data.ReportRepository
'
' P6-02: spec section 14's first four reports - daily sales summary, sales
' by product, sales by cashier, payment-method summary. Read-only against
' Sales, SaleLines, SalePayments (0011_pos.sql) and SalesReturns/
' SalesReturnLines (0012_sales-returns.sql) - merch_api already holds
' database-level SELECT (ADR-013), so nothing here needs a new grant
' (docs/report-specification.md section 8).
'
' EVERY DATE FILTER TAKES ALREADY-CONVERTED UTC INSTANTS. The caller
' (ReportService/ReportsController) does the store-local-to-UTC conversion
' through Merchandising.Domain.StoreTimeZone - the same layering
' PurchaseOrderRepository.SearchHistoryAsync already uses. This class never
' computes DATE(CreatedAtUtc) or any other UTC-calendar-date expression -
' docs/report-specification.md section 2's whole point.
'
' COST/PRICE FIGURES READ CAPTURED SaleLines COLUMNS ONLY - never a join to
' Products.Price/Products.Cost. docs/report-specification.md section 5.
'
' RETURNS ARE JOINED THROUGH A PRE-AGGREGATED DERIVED TABLE, NEVER A SECOND
' LEFT JOIN AT ROW GRAIN. SaleLines and SalesReturnLines are both one-to-many
' against a product (or a cashier); joining both directly in one GROUP BY
' would fan the two out against each other and double-count one side the
' moment a product/cashier has more than one row on either. Each side is
' summed in its own subquery first (the same technique
' PurchaseOrderRepository.SearchHistoryAsync avoids needing only because it
' has just ONE one-to-many join); the outer query then joins two
' already-one-row-per-key results, which cannot fan out further.

Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Data

    ''' <summary>The sort fields <see cref="ReportRepository.GetSalesByProductAsync"/> will honour.</summary>
    Public Enum SalesByProductSortField
        ProductName
        QuantitySold
        GrossSalesValue
    End Enum

    ''' <summary>The sort fields <see cref="ReportRepository.GetSalesByCashierAsync"/> will honour.</summary>
    Public Enum SalesByCashierSortField
        CashierUsername
        CompletedSalesTotal
    End Enum

    ''' <summary>The one sort field <see cref="ReportRepository.GetReturnsAndCancellationsAsync"/> honours - see SalesReturnRepository.SalesReturnSortField's identical single-value precedent.</summary>
    Public Enum ReturnsAndCancellationsSortField
        ReturnedAt
    End Enum

    ''' <summary>The sort fields <see cref="ReportRepository.GetProductPerformanceAsync"/> will honour.</summary>
    Public Enum ProductPerformanceSortField
        ProductName
        NetSalesValue
        NetQuantity
    End Enum

    ''' <summary>The sort fields <see cref="ReportRepository.GetPurchaseOrderHistoryReportAsync"/> will honour - the same three P3-06's PurchaseOrderSortField already offers.</summary>
    Public Enum PurchaseOrderHistoryReportSortField
        CreatedAt
        OrderNumber
        Status
    End Enum

    ''' <summary>The one sort field <see cref="ReportRepository.GetGoodsReceivingHistoryAsync"/> honours - the same single-value shape ReturnsAndCancellationsSortField uses.</summary>
    Public Enum GoodsReceivingHistorySortField
        ReceivedAt
    End Enum

    ''' <summary>The sort fields <see cref="ReportRepository.GetCurrentStockReportAsync"/> will honour - the same three names StockSortField already offers for GET /api/v1/inventory/stock, deliberately kept as a SEPARATE enum (this report's query joins Categories; StockRepository's does not).</summary>
    Public Enum CurrentStockReportSortField
        ProductName
        Quantity
        ReorderLevel
    End Enum

    ''' <summary>The one sort field <see cref="ReportRepository.GetStockMovementReportAsync"/> honours - the same single-value shape StockMovementSortField uses for GET /api/v1/inventory/stock/movements.</summary>
    Public Enum StockMovementReportSortField
        CreatedAt
    End Enum

    ''' <summary>The one sort field <see cref="ReportRepository.GetStockAdjustmentReportAsync"/> honours.</summary>
    Public Enum StockAdjustmentReportSortField
        CreatedAt
    End Enum

    Public NotInheritable Class ReportRepository

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Spec section 14 row 1. <paramref name="fromUtc"/>/<paramref name="toUtcExclusive"/>
        ''' bound both the Sales.CreatedAtUtc window (for the sales side) and
        ''' the SalesReturns.ReturnedAtUtc window (for the returns side) - the
        ''' same single store-local day, per docs/report-specification.md
        ''' section 2.
        ''' </summary>
        Public Shared Async Function GetDailySalesSummaryAsync(
            connection As MySqlConnection,
            fromUtc As DateTime,
            toUtcExclusive As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) _
            As Task(Of (CompletedSalesCount As Integer, CompletedSalesTotal As Decimal,
                       CompletedReturnsCount As Integer, CompletedReturnsTotal As Decimal,
                       PaymentTotals As IReadOnlyList(Of (Method As String, Amount As Decimal))))

            Dim completedSalesCount As Integer
            Dim completedSalesTotal As Decimal

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT COUNT(*), COALESCE(SUM(Total), 0.0000) " &
                    "  FROM Sales " &
                    " WHERE Status = 'Completed' " &
                    "   AND CreatedAtUtc >= @fromUtc AND CreatedAtUtc < @toUtcExclusive;"
                command.Parameters.AddWithValue("@fromUtc", fromUtc)
                command.Parameters.AddWithValue("@toUtcExclusive", toUtcExclusive)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                    completedSalesCount = reader.GetInt32(0)
                    completedSalesTotal = reader.GetDecimal(1)
                End Using
            End Using

            Dim completedReturnsCount As Integer
            Dim completedReturnsTotal As Decimal

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT COUNT(*), COALESCE(SUM(RefundAmount), 0.0000) " &
                    "  FROM SalesReturns " &
                    " WHERE Status = 'Completed' " &
                    "   AND ReturnedAtUtc >= @fromUtc AND ReturnedAtUtc < @toUtcExclusive;"
                command.Parameters.AddWithValue("@fromUtc", fromUtc)
                command.Parameters.AddWithValue("@toUtcExclusive", toUtcExclusive)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                    completedReturnsCount = reader.GetInt32(0)
                    completedReturnsTotal = reader.GetDecimal(1)
                End Using
            End Using

            Dim paymentTotals As IReadOnlyList(Of (Method As String, Amount As Decimal)) =
                Await GetPaymentMethodTotalsAsync(connection, fromUtc, toUtcExclusive, cancellationToken).ConfigureAwait(False)

            Return (CompletedSalesCount:=completedSalesCount, CompletedSalesTotal:=completedSalesTotal,
                    CompletedReturnsCount:=completedReturnsCount, CompletedReturnsTotal:=completedReturnsTotal,
                    PaymentTotals:=paymentTotals)

        End Function

        ''' <summary>Spec section 14 row 4 - the standalone payment-method summary. Same underlying query <see cref="GetDailySalesSummaryAsync"/> reuses for its own PaymentTotals.</summary>
        Public Shared Async Function GetPaymentMethodSummaryAsync(
            connection As MySqlConnection,
            fromUtc As DateTime?,
            toUtcExclusive As DateTime?,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of IReadOnlyList(Of (Method As String, Amount As Decimal)))

            Return Await GetPaymentMethodTotalsAsync(connection, fromUtc, toUtcExclusive, cancellationToken).ConfigureAwait(False)

        End Function

        ''' <summary>
        ''' Spec section 14 row 2. Only products with at least one Completed
        ''' sale line in the window appear (the INNER JOIN to the "sold"
        ''' subquery) - see this class's header for why returns are joined
        ''' through a second, separately-aggregated subquery rather than a
        ''' second LEFT JOIN at line grain.
        ''' </summary>
        Public Shared Async Function GetSalesByProductAsync(
            connection As MySqlConnection,
            fromUtc As DateTime?,
            toUtcExclusive As DateTime?,
            sortField As SalesByProductSortField,
            sortDescending As Boolean,
            page As Integer,
            pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) _
            As Task(Of (Items As IReadOnlyList(Of (ProductId As Integer, ProductSku As String, ProductName As String,
                                                    QuantitySold As Decimal, QuantityReturned As Decimal, GrossSalesValue As Decimal,
                                                    CapturedCostBasis As Decimal)),
                       TotalCount As Integer))

            Dim soldWhere As String = DateWhereClause("s.CreatedAtUtc", fromUtc, toUtcExclusive)
            Dim returnedWhere As String = DateWhereClause("sr.ReturnedAtUtc", fromUtc, toUtcExclusive)

            Dim totalCount As Integer

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT COUNT(*) FROM (" &
                    "  SELECT sl.ProductId" &
                    "    FROM SaleLines sl JOIN Sales s ON s.Id = sl.SaleId" &
                    "   WHERE s.Status = 'Completed'" & soldWhere &
                    "   GROUP BY sl.ProductId" &
                    ") counted;"
                AddDateParameters(command, fromUtc, toUtcExclusive)
                totalCount = CInt(Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False))
            End Using

            Dim orderBy As String =
                SalesByProductOrderByColumn(sortField) & If(sortDescending, " DESC", " ASC")

            Dim items As New List(Of (ProductId As Integer, ProductSku As String, ProductName As String,
                                      QuantitySold As Decimal, QuantityReturned As Decimal, GrossSalesValue As Decimal,
                                      CapturedCostBasis As Decimal))

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT p.Id, p.Sku, p.Name, " &
                    "       CAST(sold.QuantitySold AS DECIMAL(19,3)), " &
                    "       CAST(COALESCE(ret.QuantityReturned, 0) AS DECIMAL(19,3)), " &
                    "       CAST(sold.GrossSalesValue AS DECIMAL(19,4)), " &
                    "       CAST(sold.CapturedCostBasis AS DECIMAL(19,4)) " &
                    "  FROM Products p" &
                    "  JOIN (" &
                    "        SELECT sl.ProductId," &
                    "               SUM(sl.Quantity) AS QuantitySold," &
                    "               SUM(sl.LineTotal) AS GrossSalesValue," &
                    "               SUM(sl.Cost * sl.Quantity) AS CapturedCostBasis" &
                    "          FROM SaleLines sl JOIN Sales s ON s.Id = sl.SaleId" &
                    "         WHERE s.Status = 'Completed'" & soldWhere &
                    "         GROUP BY sl.ProductId" &
                    "       ) sold ON sold.ProductId = p.Id" &
                    "  LEFT JOIN (" &
                    "        SELECT srl.ProductId, SUM(srl.QuantityReturned) AS QuantityReturned" &
                    "          FROM SalesReturnLines srl JOIN SalesReturns sr ON sr.Id = srl.SalesReturnId" &
                    "         WHERE sr.Status = 'Completed'" & returnedWhere &
                    "         GROUP BY srl.ProductId" &
                    "       ) ret ON ret.ProductId = p.Id" &
                    " ORDER BY " & orderBy &
                    " LIMIT @pageSize OFFSET @offset;"
                AddDateParameters(command, fromUtc, toUtcExclusive)
                command.Parameters.AddWithValue("@pageSize", pageSize)
                command.Parameters.AddWithValue("@offset", (page - 1) * pageSize)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        items.Add((ProductId:=reader.GetInt32(0), ProductSku:=reader.GetString(1), ProductName:=reader.GetString(2),
                                   QuantitySold:=reader.GetDecimal(3), QuantityReturned:=reader.GetDecimal(4),
                                   GrossSalesValue:=reader.GetDecimal(5), CapturedCostBasis:=reader.GetDecimal(6)))
                    End While
                End Using
            End Using

            Return (Items:=items, TotalCount:=totalCount)

        End Function

        ''' <summary>
        ''' Spec section 14 row 3. Only cashiers with at least one Completed
        ''' sale in the window appear. Payment totals per cashier are fetched
        ''' in a THIRD query, keyed by the page's own cashier ids - the same
        ''' "paginate first, fetch children for just this page" shape
        ''' SaleRepository.GetLinesForSalesAsync uses.
        ''' </summary>
        Public Shared Async Function GetSalesByCashierAsync(
            connection As MySqlConnection,
            fromUtc As DateTime?,
            toUtcExclusive As DateTime?,
            sortField As SalesByCashierSortField,
            sortDescending As Boolean,
            page As Integer,
            pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) _
            As Task(Of (Items As IReadOnlyList(Of (CashierUserId As Integer, CashierUsername As String,
                                                    CompletedSalesCount As Integer, CompletedSalesTotal As Decimal,
                                                    ReturnsCount As Integer, ReturnsTotal As Decimal,
                                                    PaymentTotals As IReadOnlyList(Of (Method As String, Amount As Decimal)))),
                       TotalCount As Integer))

            Dim soldWhere As String = DateWhereClause("s.CreatedAtUtc", fromUtc, toUtcExclusive)
            Dim returnedWhere As String = DateWhereClause("sr.ReturnedAtUtc", fromUtc, toUtcExclusive)

            Dim totalCount As Integer

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT COUNT(*) FROM (" &
                    "  SELECT s.CashierUserId" &
                    "    FROM Sales s" &
                    "   WHERE s.Status = 'Completed'" & soldWhere &
                    "   GROUP BY s.CashierUserId" &
                    ") counted;"
                AddDateParameters(command, fromUtc, toUtcExclusive)
                totalCount = CInt(Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False))
            End Using

            Dim orderBy As String =
                If(sortField = SalesByCashierSortField.CompletedSalesTotal, "sold.CompletedSalesTotal", "u.Username") &
                If(sortDescending, " DESC", " ASC")

            Dim rows As New List(Of (CashierUserId As Integer, CashierUsername As String,
                                     CompletedSalesCount As Integer, CompletedSalesTotal As Decimal,
                                     ReturnsCount As Integer, ReturnsTotal As Decimal))

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT u.Id, u.Username, sold.CompletedSalesCount, CAST(sold.CompletedSalesTotal AS DECIMAL(19,4)), " &
                    "       COALESCE(ret.ReturnsCount, 0), CAST(COALESCE(ret.ReturnsTotal, 0) AS DECIMAL(19,4)) " &
                    "  FROM Users u" &
                    "  JOIN (" &
                    "        SELECT s.CashierUserId, COUNT(*) AS CompletedSalesCount, SUM(s.Total) AS CompletedSalesTotal" &
                    "          FROM Sales s" &
                    "         WHERE s.Status = 'Completed'" & soldWhere &
                    "         GROUP BY s.CashierUserId" &
                    "       ) sold ON sold.CashierUserId = u.Id" &
                    "  LEFT JOIN (" &
                    "        SELECT sr.ReturnedByUserId, COUNT(*) AS ReturnsCount, SUM(sr.RefundAmount) AS ReturnsTotal" &
                    "          FROM SalesReturns sr" &
                    "         WHERE sr.Status = 'Completed'" & returnedWhere &
                    "         GROUP BY sr.ReturnedByUserId" &
                    "       ) ret ON ret.ReturnedByUserId = u.Id" &
                    " ORDER BY " & orderBy &
                    " LIMIT @pageSize OFFSET @offset;"
                AddDateParameters(command, fromUtc, toUtcExclusive)
                command.Parameters.AddWithValue("@pageSize", pageSize)
                command.Parameters.AddWithValue("@offset", (page - 1) * pageSize)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        rows.Add((CashierUserId:=reader.GetInt32(0), CashierUsername:=reader.GetString(1),
                                  CompletedSalesCount:=reader.GetInt32(2), CompletedSalesTotal:=reader.GetDecimal(3),
                                  ReturnsCount:=reader.GetInt32(4), ReturnsTotal:=reader.GetDecimal(5)))
                    End While
                End Using
            End Using

            Dim cashierIds As IReadOnlyList(Of Integer) = rows.Select(Function(r) r.CashierUserId).ToList()
            Dim paymentTotalsByCashier As IReadOnlyDictionary(Of Integer, IReadOnlyList(Of (Method As String, Amount As Decimal))) =
                Await GetPaymentTotalsByCashierAsync(connection, soldWhere, fromUtc, toUtcExclusive, cashierIds, cancellationToken).ConfigureAwait(False)

            Dim items = rows.Select(
                Function(r) (CashierUserId:=r.CashierUserId, CashierUsername:=r.CashierUsername,
                             CompletedSalesCount:=r.CompletedSalesCount, CompletedSalesTotal:=r.CompletedSalesTotal,
                             ReturnsCount:=r.ReturnsCount, ReturnsTotal:=r.ReturnsTotal,
                             PaymentTotals:=If(paymentTotalsByCashier.ContainsKey(r.CashierUserId),
                                                paymentTotalsByCashier(r.CashierUserId),
                                                CType(Array.Empty(Of (Method As String, Amount As Decimal))(), IReadOnlyList(Of (Method As String, Amount As Decimal)))))).
                ToList()

            Return (Items:=items, TotalCount:=totalCount)

        End Function

        ''' <summary>
        ''' Spec section 14 row 5. A FLAT per-line listing, one row per
        ''' SalesReturnLines row - no GROUP BY, so this is a plain multi-table
        ''' join, not the pre-aggregated-subquery shape this class's header
        ''' requires for a SUM() over more than one one-to-many relation.
        ''' Every status appears - see ReturnsAndCancellationsItemResponse's
        ''' header for why this is never filtered to only-Completed rows.
        ''' </summary>
        Public Shared Async Function GetReturnsAndCancellationsAsync(
            connection As MySqlConnection,
            fromUtc As DateTime?,
            toUtcExclusive As DateTime?,
            sortDescending As Boolean,
            page As Integer,
            pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) _
            As Task(Of (Items As IReadOnlyList(Of (SalesReturnId As Integer, SalesReturnLineId As Integer, SaleId As Integer,
                                                    ProductId As Integer, ProductSku As String, ProductName As String,
                                                    QuantityReturned As Decimal, Reason As String,
                                                    ReturnedByUserId As Integer, ReturnedByUsername As String,
                                                    ApprovedByUserId As Integer?, ApprovedByUsername As String,
                                                    Status As String, RestocksItem As Boolean, ReturnedAtUtc As DateTime)),
                       TotalCount As Integer))

            Dim whereClause As String = DateWhereClause("sr.ReturnedAtUtc", fromUtc, toUtcExclusive)
            Dim trimmedWhereClause As String =
                If(whereClause.Length > 0, " WHERE " & whereClause.Substring(" AND ".Length), String.Empty)

            Dim totalCount As Integer

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT COUNT(*) " &
                    "  FROM SalesReturnLines srl JOIN SalesReturns sr ON sr.Id = srl.SalesReturnId" &
                    trimmedWhereClause & ";"
                AddDateParameters(command, fromUtc, toUtcExclusive)
                totalCount = CInt(Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False))
            End Using

            Dim items As New List(Of (SalesReturnId As Integer, SalesReturnLineId As Integer, SaleId As Integer,
                                      ProductId As Integer, ProductSku As String, ProductName As String,
                                      QuantityReturned As Decimal, Reason As String,
                                      ReturnedByUserId As Integer, ReturnedByUsername As String,
                                      ApprovedByUserId As Integer?, ApprovedByUsername As String,
                                      Status As String, RestocksItem As Boolean, ReturnedAtUtc As DateTime))

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT sr.Id, srl.Id, sr.SaleId, srl.ProductId, p.Sku, p.Name, srl.QuantityReturned, sr.Reason, " &
                    "       sr.ReturnedByUserId, ru.Username, sr.ApprovedByUserId, au.Username, " &
                    "       sr.Status, srl.RestocksItem, sr.ReturnedAtUtc " &
                    "  FROM SalesReturnLines srl" &
                    "  JOIN SalesReturns sr ON sr.Id = srl.SalesReturnId" &
                    "  JOIN Products p ON p.Id = srl.ProductId" &
                    "  JOIN Users ru ON ru.Id = sr.ReturnedByUserId" &
                    "  LEFT JOIN Users au ON au.Id = sr.ApprovedByUserId" &
                    trimmedWhereClause &
                    " ORDER BY sr.ReturnedAtUtc " & If(sortDescending, "DESC", "ASC") & ", srl.Id " & If(sortDescending, "DESC", "ASC") &
                    " LIMIT @pageSize OFFSET @offset;"
                AddDateParameters(command, fromUtc, toUtcExclusive)
                command.Parameters.AddWithValue("@pageSize", pageSize)
                command.Parameters.AddWithValue("@offset", (page - 1) * pageSize)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)

                        Dim approvedByOrdinal As Integer = 10
                        Dim approvedByUsernameOrdinal As Integer = 11

                        items.Add((SalesReturnId:=reader.GetInt32(0), SalesReturnLineId:=reader.GetInt32(1), SaleId:=reader.GetInt32(2),
                                   ProductId:=reader.GetInt32(3), ProductSku:=reader.GetString(4), ProductName:=reader.GetString(5),
                                   QuantityReturned:=reader.GetDecimal(6), Reason:=reader.GetString(7),
                                   ReturnedByUserId:=reader.GetInt32(8), ReturnedByUsername:=reader.GetString(9),
                                   ApprovedByUserId:=If(reader.IsDBNull(approvedByOrdinal), CType(Nothing, Integer?), reader.GetInt32(approvedByOrdinal)),
                                   ApprovedByUsername:=If(reader.IsDBNull(approvedByUsernameOrdinal), Nothing, reader.GetString(approvedByUsernameOrdinal)),
                                   Status:=reader.GetString(12), RestocksItem:=reader.GetBoolean(13), ReturnedAtUtc:=reader.GetDateTime(14)))

                    End While
                End Using
            End Using

            Return (Items:=items, TotalCount:=totalCount)

        End Function

        ''' <summary>
        ''' Spec section 14 row 12. Only products with at least one Completed
        ''' sale line in the window appear (same INNER JOIN restriction
        ''' <see cref="GetSalesByProductAsync"/> uses). Sold/returned are each
        ''' pre-aggregated in their own subquery (this class's header) - the
        ''' returned subquery additionally joins SaleLines for UnitPrice/Cost
        ''' so the returned VALUE and returned COST BASIS can be netted
        ''' against the sold side, never against Products.Price/Products.Cost.
        ''' StockBalances is joined directly, unaggregated - it is one row per
        ''' product (1:1), never one-to-many, so joining it a third time
        ''' cannot fan out anything the first two subqueries already
        ''' collapsed to one row per product. This is also why the join
        ''' carries NO date predicate: current stock is read LIVE, regardless
        ''' of the window the sales figures respect (docs/report-
        ''' specification.md, ProductPerformanceItemResponse's own header -
        ''' "the mixed-temporality trap").
        ''' </summary>
        Public Shared Async Function GetProductPerformanceAsync(
            connection As MySqlConnection,
            fromUtc As DateTime?,
            toUtcExclusive As DateTime?,
            sortField As ProductPerformanceSortField,
            sortDescending As Boolean,
            page As Integer,
            pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) _
            As Task(Of (Items As IReadOnlyList(Of (ProductId As Integer, ProductSku As String, ProductName As String,
                                                    QuantitySold As Decimal, QuantityReturned As Decimal,
                                                    GrossSalesValue As Decimal, ReturnedValue As Decimal,
                                                    CapturedCostBasis As Decimal, ReturnedCostBasis As Decimal,
                                                    CurrentStockQuantity As Decimal)),
                       TotalCount As Integer))

            Dim soldWhere As String = DateWhereClause("s.CreatedAtUtc", fromUtc, toUtcExclusive)
            Dim returnedWhere As String = DateWhereClause("sr.ReturnedAtUtc", fromUtc, toUtcExclusive)

            Dim totalCount As Integer

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT COUNT(*) FROM (" &
                    "  SELECT sl.ProductId" &
                    "    FROM SaleLines sl JOIN Sales s ON s.Id = sl.SaleId" &
                    "   WHERE s.Status = 'Completed'" & soldWhere &
                    "   GROUP BY sl.ProductId" &
                    ") counted;"
                AddDateParameters(command, fromUtc, toUtcExclusive)
                totalCount = CInt(Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False))
            End Using

            Dim orderBy As String
            Select Case sortField
                Case ProductPerformanceSortField.NetSalesValue
                    orderBy = "(sold.GrossSalesValue - COALESCE(ret.ReturnedValue, 0))"
                Case ProductPerformanceSortField.NetQuantity
                    orderBy = "(sold.QuantitySold - COALESCE(ret.QuantityReturned, 0))"
                Case Else
                    orderBy = "p.Name"
            End Select
            orderBy &= If(sortDescending, " DESC", " ASC")

            Dim items As New List(Of (ProductId As Integer, ProductSku As String, ProductName As String,
                                      QuantitySold As Decimal, QuantityReturned As Decimal,
                                      GrossSalesValue As Decimal, ReturnedValue As Decimal,
                                      CapturedCostBasis As Decimal, ReturnedCostBasis As Decimal,
                                      CurrentStockQuantity As Decimal))

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT p.Id, p.Sku, p.Name, " &
                    "       CAST(sold.QuantitySold AS DECIMAL(19,3)), " &
                    "       CAST(COALESCE(ret.QuantityReturned, 0) AS DECIMAL(19,3)), " &
                    "       CAST(sold.GrossSalesValue AS DECIMAL(19,4)), " &
                    "       CAST(COALESCE(ret.ReturnedValue, 0) AS DECIMAL(19,4)), " &
                    "       CAST(sold.CapturedCostBasis AS DECIMAL(19,4)), " &
                    "       CAST(COALESCE(ret.ReturnedCostBasis, 0) AS DECIMAL(19,4)), " &
                    "       CAST(COALESCE(sb.Quantity, 0) AS DECIMAL(19,3)) " &
                    "  FROM Products p" &
                    "  JOIN (" &
                    "        SELECT sl.ProductId," &
                    "               SUM(sl.Quantity) AS QuantitySold," &
                    "               SUM(sl.LineTotal) AS GrossSalesValue," &
                    "               SUM(sl.Cost * sl.Quantity) AS CapturedCostBasis" &
                    "          FROM SaleLines sl JOIN Sales s ON s.Id = sl.SaleId" &
                    "         WHERE s.Status = 'Completed'" & soldWhere &
                    "         GROUP BY sl.ProductId" &
                    "       ) sold ON sold.ProductId = p.Id" &
                    "  LEFT JOIN (" &
                    "        SELECT srl.ProductId," &
                    "               SUM(srl.QuantityReturned) AS QuantityReturned," &
                    "               SUM(srl.QuantityReturned * sl.UnitPrice) AS ReturnedValue," &
                    "               SUM(srl.QuantityReturned * sl.Cost) AS ReturnedCostBasis" &
                    "          FROM SalesReturnLines srl" &
                    "          JOIN SalesReturns sr ON sr.Id = srl.SalesReturnId" &
                    "          JOIN SaleLines sl ON sl.Id = srl.SaleLineId" &
                    "         WHERE sr.Status = 'Completed'" & returnedWhere &
                    "         GROUP BY srl.ProductId" &
                    "       ) ret ON ret.ProductId = p.Id" &
                    "  LEFT JOIN StockBalances sb ON sb.ProductId = p.Id" &
                    " ORDER BY " & orderBy &
                    " LIMIT @pageSize OFFSET @offset;"
                AddDateParameters(command, fromUtc, toUtcExclusive)
                command.Parameters.AddWithValue("@pageSize", pageSize)
                command.Parameters.AddWithValue("@offset", (page - 1) * pageSize)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        items.Add((ProductId:=reader.GetInt32(0), ProductSku:=reader.GetString(1), ProductName:=reader.GetString(2),
                                   QuantitySold:=reader.GetDecimal(3), QuantityReturned:=reader.GetDecimal(4),
                                   GrossSalesValue:=reader.GetDecimal(5), ReturnedValue:=reader.GetDecimal(6),
                                   CapturedCostBasis:=reader.GetDecimal(7), ReturnedCostBasis:=reader.GetDecimal(8),
                                   CurrentStockQuantity:=reader.GetDecimal(9)))
                    End While
                End Using
            End Using

            Return (Items:=items, TotalCount:=totalCount)

        End Function

        ''' <summary>
        ''' Spec section 14 row 6. Ordered quantity/value are pre-aggregated
        ''' over PurchaseOrderLines, the same figure P3-06's SearchHistoryAsync
        ''' computes. Received quantity/value are DELIBERATELY NOT read from
        ''' PurchaseOrderLines.ReceivedQuantity (the accumulator column P3-06
        ''' reads) - they are pre-aggregated over ReceiptLines directly, "from
        ''' committed receipt rows" (this card's own Done-when box), so the
        ''' reconciliation test comparing this report's numbers against P3-06's
        ''' genuinely exercises two independently-written queries rather than
        ''' one query compared to itself (docs/report-specification.md
        ''' section 7). Both subqueries are pre-aggregated to one row per
        ''' order BEFORE the outer join, the same fan-out avoidance this
        ''' class's header requires for combining two one-to-many
        ''' relationships (here: order-to-lines and order-to-receipt-lines).
        ''' ReceivedValue prices the received quantity at the ORDER's captured
        ''' PurchaseCost, never the receipt's own (possibly different)
        ''' invoiced Cost - matching P3-06's ReceivedValue definition exactly,
        ''' which is what makes the two numbers comparable at all.
        ''' </summary>
        Public Shared Async Function GetPurchaseOrderHistoryReportAsync(
            connection As MySqlConnection,
            fromUtc As DateTime?,
            toUtcExclusive As DateTime?,
            sortField As PurchaseOrderHistoryReportSortField,
            sortDescending As Boolean,
            page As Integer,
            pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) _
            As Task(Of (Items As IReadOnlyList(Of (Id As Integer, OrderNumber As String, SupplierId As Integer, SupplierName As String,
                                                    Status As String, CreatedAtUtc As DateTime,
                                                    OrderedQuantity As Decimal, OrderedValue As Decimal,
                                                    ReceivedQuantity As Decimal, ReceivedValue As Decimal,
                                                    OutstandingQuantity As Decimal)),
                       TotalCount As Integer))

            Dim whereClause As String = DateWhereClause("o.CreatedAtUtc", fromUtc, toUtcExclusive)
            Dim trimmedWhereClause As String =
                If(whereClause.Length > 0, " WHERE " & whereClause.Substring(" AND ".Length), String.Empty)

            Dim totalCount As Integer

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM PurchaseOrders o" & trimmedWhereClause & ";"
                AddDateParameters(command, fromUtc, toUtcExclusive)
                totalCount = CInt(Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False))
            End Using

            Dim orderBy As String
            Select Case sortField
                Case PurchaseOrderHistoryReportSortField.OrderNumber
                    orderBy = "o.OrderNumber"
                Case PurchaseOrderHistoryReportSortField.Status
                    orderBy = "o.Status"
                Case Else
                    orderBy = "o.CreatedAtUtc"
            End Select
            orderBy &= If(sortDescending, " DESC", " ASC") & ", o.Id " & If(sortDescending, "DESC", "ASC")

            Dim items As New List(Of (Id As Integer, OrderNumber As String, SupplierId As Integer, SupplierName As String,
                                      Status As String, CreatedAtUtc As DateTime,
                                      OrderedQuantity As Decimal, OrderedValue As Decimal,
                                      ReceivedQuantity As Decimal, ReceivedValue As Decimal,
                                      OutstandingQuantity As Decimal))

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT o.Id, o.OrderNumber, o.SupplierId, s.Name, o.Status, o.CreatedAtUtc, " &
                    "       CAST(COALESCE(ordered.OrderedQuantity, 0) AS DECIMAL(19,3)), " &
                    "       CAST(COALESCE(ordered.OrderedValue, 0) AS DECIMAL(19,4)), " &
                    "       CAST(COALESCE(received.ReceivedQuantity, 0) AS DECIMAL(19,3)), " &
                    "       CAST(COALESCE(received.ReceivedValue, 0) AS DECIMAL(19,4)), " &
                    "       CAST(COALESCE(ordered.OrderedQuantity, 0) - COALESCE(received.ReceivedQuantity, 0) AS DECIMAL(19,3)) " &
                    "  FROM PurchaseOrders o" &
                    "  JOIN Suppliers s ON s.Id = o.SupplierId" &
                    "  LEFT JOIN (" &
                    "        SELECT pol.PurchaseOrderId," &
                    "               SUM(pol.OrderedQuantity) AS OrderedQuantity," &
                    "               SUM(pol.OrderedQuantity * pol.PurchaseCost) AS OrderedValue" &
                    "          FROM PurchaseOrderLines pol" &
                    "         GROUP BY pol.PurchaseOrderId" &
                    "       ) ordered ON ordered.PurchaseOrderId = o.Id" &
                    "  LEFT JOIN (" &
                    "        SELECT pol.PurchaseOrderId," &
                    "               SUM(rl.QuantityReceived) AS ReceivedQuantity," &
                    "               SUM(rl.QuantityReceived * pol.PurchaseCost) AS ReceivedValue" &
                    "          FROM ReceiptLines rl JOIN PurchaseOrderLines pol ON pol.Id = rl.PurchaseOrderLineId" &
                    "         GROUP BY pol.PurchaseOrderId" &
                    "       ) received ON received.PurchaseOrderId = o.Id" &
                    trimmedWhereClause &
                    " ORDER BY " & orderBy &
                    " LIMIT @pageSize OFFSET @offset;"
                AddDateParameters(command, fromUtc, toUtcExclusive)
                command.Parameters.AddWithValue("@pageSize", pageSize)
                command.Parameters.AddWithValue("@offset", (page - 1) * pageSize)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        items.Add((Id:=reader.GetInt32(0), OrderNumber:=reader.GetString(1), SupplierId:=reader.GetInt32(2),
                                   SupplierName:=reader.GetString(3), Status:=reader.GetString(4), CreatedAtUtc:=reader.GetDateTime(5),
                                   OrderedQuantity:=reader.GetDecimal(6), OrderedValue:=reader.GetDecimal(7),
                                   ReceivedQuantity:=reader.GetDecimal(8), ReceivedValue:=reader.GetDecimal(9),
                                   OutstandingQuantity:=reader.GetDecimal(10)))
                    End While
                End Using
            End Using

            Return (Items:=items, TotalCount:=totalCount)

        End Function

        ''' <summary>
        ''' Spec section 14 row 7. A FLAT per-receipt-line listing - one row
        ''' per ReceiptLines row, no GROUP BY, the same shape
        ''' GetReturnsAndCancellationsAsync uses for the identical reason
        ''' (this class's header). Filtered on Receipts.ReceivedAtUtc, the
        ''' report's own natural date dimension - deliberately NOT
        ''' PurchaseOrders.CreatedAtUtc, which is what
        ''' GetPurchaseOrderHistoryReportAsync filters on; the reconciliation
        ''' test therefore reconciles the two reports UNBOUNDED (see that
        ''' test's own comment) rather than under one shared date filter.
        ''' </summary>
        Public Shared Async Function GetGoodsReceivingHistoryAsync(
            connection As MySqlConnection,
            fromUtc As DateTime?,
            toUtcExclusive As DateTime?,
            sortDescending As Boolean,
            page As Integer,
            pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) _
            As Task(Of (Items As IReadOnlyList(Of (ReceiptId As Integer, ReceiptLineId As Integer, ReferenceNumber As String,
                                                    PurchaseOrderId As Integer, OrderNumber As String,
                                                    SupplierId As Integer, SupplierName As String, ReceivedAtUtc As DateTime,
                                                    ProductId As Integer, ProductSku As String, ProductName As String,
                                                    OrderedQuantity As Decimal, ReceivedQuantity As Decimal,
                                                    ReceivedByUserId As Integer, ReceivedByUsername As String)),
                       TotalCount As Integer))

            Dim whereClause As String = DateWhereClause("r.ReceivedAtUtc", fromUtc, toUtcExclusive)
            Dim trimmedWhereClause As String =
                If(whereClause.Length > 0, " WHERE " & whereClause.Substring(" AND ".Length), String.Empty)

            Dim totalCount As Integer

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT COUNT(*) FROM ReceiptLines rl JOIN Receipts r ON r.Id = rl.ReceiptId" &
                    trimmedWhereClause & ";"
                AddDateParameters(command, fromUtc, toUtcExclusive)
                totalCount = CInt(Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False))
            End Using

            Dim items As New List(Of (ReceiptId As Integer, ReceiptLineId As Integer, ReferenceNumber As String,
                                      PurchaseOrderId As Integer, OrderNumber As String,
                                      SupplierId As Integer, SupplierName As String, ReceivedAtUtc As DateTime,
                                      ProductId As Integer, ProductSku As String, ProductName As String,
                                      OrderedQuantity As Decimal, ReceivedQuantity As Decimal,
                                      ReceivedByUserId As Integer, ReceivedByUsername As String))

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT r.Id, rl.Id, r.ReferenceNumber, o.Id, o.OrderNumber, o.SupplierId, s.Name, r.ReceivedAtUtc, " &
                    "       rl.ProductId, p.Sku, p.Name, pol.OrderedQuantity, rl.QuantityReceived, " &
                    "       r.ReceivedByUserId, u.Username " &
                    "  FROM ReceiptLines rl" &
                    "  JOIN Receipts r ON r.Id = rl.ReceiptId" &
                    "  JOIN PurchaseOrderLines pol ON pol.Id = rl.PurchaseOrderLineId" &
                    "  JOIN PurchaseOrders o ON o.Id = r.PurchaseOrderId" &
                    "  JOIN Suppliers s ON s.Id = o.SupplierId" &
                    "  JOIN Products p ON p.Id = rl.ProductId" &
                    "  JOIN Users u ON u.Id = r.ReceivedByUserId" &
                    trimmedWhereClause &
                    " ORDER BY r.ReceivedAtUtc " & If(sortDescending, "DESC", "ASC") & ", rl.Id " & If(sortDescending, "DESC", "ASC") &
                    " LIMIT @pageSize OFFSET @offset;"
                AddDateParameters(command, fromUtc, toUtcExclusive)
                command.Parameters.AddWithValue("@pageSize", pageSize)
                command.Parameters.AddWithValue("@offset", (page - 1) * pageSize)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        items.Add((ReceiptId:=reader.GetInt32(0), ReceiptLineId:=reader.GetInt32(1), ReferenceNumber:=reader.GetString(2),
                                   PurchaseOrderId:=reader.GetInt32(3), OrderNumber:=reader.GetString(4),
                                   SupplierId:=reader.GetInt32(5), SupplierName:=reader.GetString(6), ReceivedAtUtc:=reader.GetDateTime(7),
                                   ProductId:=reader.GetInt32(8), ProductSku:=reader.GetString(9), ProductName:=reader.GetString(10),
                                   OrderedQuantity:=reader.GetDecimal(11), ReceivedQuantity:=reader.GetDecimal(12),
                                   ReceivedByUserId:=reader.GetInt32(13), ReceivedByUsername:=reader.GetString(14)))
                    End While
                End Using
            End Using

            Return (Items:=items, TotalCount:=totalCount)

        End Function

        ''' <summary>
        ''' Spec section 14 row 8 - current stock. A point-in-time snapshot
        ''' (no date filter, matching GET /api/v1/inventory/stock's own
        ''' contract). DELIBERATELY A SEPARATE QUERY FROM
        ''' StockRepository.SearchStockAsync, not a call to it: this one joins
        ''' Categories, which that query has no reason to - genuine, not
        ''' incidental, independence for the reconciliation harness
        ''' (docs/report-specification.md section 7), comparing this report's
        ''' Quantity/ReorderLevel per product against GET /api/v1/inventory/stock's
        ''' existing, already-shipped answer for the same product.
        ''' </summary>
        Public Shared Async Function GetCurrentStockReportAsync(
            connection As MySqlConnection,
            includeInactive As Boolean,
            sortField As CurrentStockReportSortField,
            sortDescending As Boolean,
            page As Integer,
            pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) _
            As Task(Of (Items As IReadOnlyList(Of (ProductId As Integer, Sku As String, Name As String, CategoryId As Integer?, CategoryName As String,
                                                     IsActive As Boolean, Quantity As Decimal, ReorderLevel As Decimal)),
                        TotalCount As Integer))

            Dim whereClause As String = If(includeInactive, String.Empty, " WHERE p.IsActive = 1")

            Dim totalCount As Integer

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM Products p" & whereClause & ";"
                totalCount = CInt(Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False))
            End Using

            Dim items As New List(Of (ProductId As Integer, Sku As String, Name As String, CategoryId As Integer?, CategoryName As String,
                                       IsActive As Boolean, Quantity As Decimal, ReorderLevel As Decimal))

            Dim orderByColumn As String
            Select Case sortField
                Case CurrentStockReportSortField.Quantity
                    orderByColumn = "Quantity"
                Case CurrentStockReportSortField.ReorderLevel
                    orderByColumn = "p.ReorderLevel"
                Case Else
                    orderByColumn = "p.Name"
            End Select
            Dim direction As String = If(sortDescending, " DESC", " ASC")

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT p.Id, p.Sku, p.Name, p.CategoryId, c.Name AS CategoryName, p.IsActive, p.ReorderLevel, " &
                    "       COALESCE(b.Quantity, 0.000) AS Quantity " &
                    "  FROM Products p" &
                    "  LEFT JOIN StockBalances b ON b.ProductId = p.Id" &
                    "  LEFT JOIN Categories c ON c.Id = p.CategoryId" &
                    whereClause &
                    " ORDER BY " & orderByColumn & direction & ", p.Id" & direction &
                    " LIMIT @pageSize OFFSET @offset;"
                command.Parameters.AddWithValue("@pageSize", pageSize)
                command.Parameters.AddWithValue("@offset", (page - 1) * pageSize)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)

                        Dim categoryIdOrdinal As Integer = reader.GetOrdinal("CategoryId")
                        Dim categoryNameOrdinal As Integer = reader.GetOrdinal("CategoryName")

                        items.Add((
                            ProductId:=reader.GetInt32(reader.GetOrdinal("Id")),
                            Sku:=reader.GetString(reader.GetOrdinal("Sku")),
                            Name:=reader.GetString(reader.GetOrdinal("Name")),
                            CategoryId:=If(reader.IsDBNull(categoryIdOrdinal), CType(Nothing, Integer?), reader.GetInt32(categoryIdOrdinal)),
                            CategoryName:=If(reader.IsDBNull(categoryNameOrdinal), Nothing, reader.GetString(categoryNameOrdinal)),
                            IsActive:=reader.GetBoolean(reader.GetOrdinal("IsActive")),
                            Quantity:=reader.GetDecimal(reader.GetOrdinal("Quantity")),
                            ReorderLevel:=reader.GetDecimal(reader.GetOrdinal("ReorderLevel"))))

                    End While
                End Using
            End Using

            Return (Items:=items, TotalCount:=totalCount)

        End Function

        ''' <summary>
        ''' Spec section 14 row 10 - stock movement report. UNLIKE
        ''' StockRepository.SearchMovementsAsync, <paramref name="productId"/>
        ''' is OPTIONAL: Nothing returns every product's movements in the date
        ''' window, which this report's own Done-when box needs ("current
        ''' stock and the movement report agree with each other for EVERY
        ''' product"). ReturnsTreatment = Included (docs/report-specification.md
        ''' section 4 row 10) - nothing is excluded, this is a ledger of every
        ''' movement type.
        ''' </summary>
        Public Shared Async Function GetStockMovementReportAsync(
            connection As MySqlConnection,
            productId As Integer?,
            fromUtc As DateTime?,
            toUtcExclusive As DateTime?,
            sortDescending As Boolean,
            page As Integer,
            pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) _
            As Task(Of (Items As IReadOnlyList(Of (Id As Integer, ProductId As Integer, ProductSku As String, ProductName As String,
                                                     Delta As Decimal, QuantityBefore As Decimal, QuantityAfter As Decimal,
                                                     Reason As String, ActorUserId As Integer, ActorUsername As String,
                                                     CorrelationId As String, CreatedAtUtc As DateTime)),
                        TotalCount As Integer))

            Dim conditions As New List(Of String)
            If productId.HasValue Then
                conditions.Add("m.ProductId = @productId")
            End If
            If fromUtc.HasValue Then
                conditions.Add("m.CreatedAtUtc >= @fromUtc")
            End If
            If toUtcExclusive.HasValue Then
                conditions.Add("m.CreatedAtUtc < @toUtcExclusive")
            End If

            Dim whereClause As String = If(conditions.Count = 0, String.Empty, " WHERE " & String.Join(" AND ", conditions))

            Dim addParameters As Action(Of MySqlCommand) =
                Sub(command)
                    If productId.HasValue Then
                        command.Parameters.AddWithValue("@productId", productId.Value)
                    End If
                    AddDateParameters(command, fromUtc, toUtcExclusive)
                End Sub

            Dim totalCount As Integer

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM StockMovements m" & whereClause & ";"
                addParameters(command)
                totalCount = CInt(Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False))
            End Using

            Dim items As New List(Of (Id As Integer, ProductId As Integer, ProductSku As String, ProductName As String,
                                       Delta As Decimal, QuantityBefore As Decimal, QuantityAfter As Decimal,
                                       Reason As String, ActorUserId As Integer, ActorUsername As String,
                                       CorrelationId As String, CreatedAtUtc As DateTime))

            Dim direction As String = If(sortDescending, "DESC", "ASC")

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT m.Id, m.ProductId, p.Sku, p.Name, m.Delta, m.QuantityBefore, m.QuantityAfter, " &
                    "       m.Reason, m.ActorUserId, u.Username, m.CorrelationId, m.CreatedAtUtc " &
                    "  FROM StockMovements m" &
                    "  JOIN Products p ON p.Id = m.ProductId" &
                    "  JOIN Users u ON u.Id = m.ActorUserId" &
                    whereClause &
                    " ORDER BY m.CreatedAtUtc " & direction & ", m.Id " & direction &
                    " LIMIT @pageSize OFFSET @offset;"
                addParameters(command)
                command.Parameters.AddWithValue("@pageSize", pageSize)
                command.Parameters.AddWithValue("@offset", (page - 1) * pageSize)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        items.Add((
                            Id:=reader.GetInt32(0), ProductId:=reader.GetInt32(1), ProductSku:=reader.GetString(2), ProductName:=reader.GetString(3),
                            Delta:=reader.GetDecimal(4), QuantityBefore:=reader.GetDecimal(5), QuantityAfter:=reader.GetDecimal(6),
                            Reason:=reader.GetString(7), ActorUserId:=reader.GetInt32(8), ActorUsername:=reader.GetString(9),
                            CorrelationId:=reader.GetGuid(10).ToString("d"), CreatedAtUtc:=reader.GetDateTime(11)))
                    End While
                End Using
            End Using

            Return (Items:=items, TotalCount:=totalCount)

        End Function

        ''' <summary>
        ''' Spec section 14 row 11 - stock-adjustment report. The FIRST GET
        ''' route StockAdjustments has ever had (AdjustmentsController's own
        ''' header - only POST routes existed before this card). ReturnsTreatment
        ''' = Excluded (docs/report-specification.md section 4 row 11) -
        ''' adjustments and sales returns are distinct transaction types.
        ''' <paramref name="productId"/> is optional, the same shape
        ''' GetStockMovementReportAsync uses.
        ''' </summary>
        Public Shared Async Function GetStockAdjustmentReportAsync(
            connection As MySqlConnection,
            productId As Integer?,
            fromUtc As DateTime?,
            toUtcExclusive As DateTime?,
            sortDescending As Boolean,
            page As Integer,
            pageSize As Integer,
            Optional cancellationToken As CancellationToken = Nothing) _
            As Task(Of (Items As IReadOnlyList(Of (Id As Integer, ProductId As Integer, ProductSku As String, ProductName As String,
                                                     QuantityVariance As Decimal, Reason As String,
                                                     RequestedByUserId As Integer, RequestedByUsername As String,
                                                     ApprovedByUserId As Integer?, ApprovedByUsername As String,
                                                     ExceedsThreshold As Boolean, Status As String,
                                                     CreatedAtUtc As DateTime, UpdatedAtUtc As DateTime)),
                        TotalCount As Integer))

            Dim conditions As New List(Of String)
            If productId.HasValue Then
                conditions.Add("a.ProductId = @productId")
            End If
            If fromUtc.HasValue Then
                conditions.Add("a.CreatedAtUtc >= @fromUtc")
            End If
            If toUtcExclusive.HasValue Then
                conditions.Add("a.CreatedAtUtc < @toUtcExclusive")
            End If

            Dim whereClause As String = If(conditions.Count = 0, String.Empty, " WHERE " & String.Join(" AND ", conditions))

            Dim addParameters As Action(Of MySqlCommand) =
                Sub(command)
                    If productId.HasValue Then
                        command.Parameters.AddWithValue("@productId", productId.Value)
                    End If
                    AddDateParameters(command, fromUtc, toUtcExclusive)
                End Sub

            Dim totalCount As Integer

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText = "SELECT COUNT(*) FROM StockAdjustments a" & whereClause & ";"
                addParameters(command)
                totalCount = CInt(Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False))
            End Using

            Dim items As New List(Of (Id As Integer, ProductId As Integer, ProductSku As String, ProductName As String,
                                       QuantityVariance As Decimal, Reason As String,
                                       RequestedByUserId As Integer, RequestedByUsername As String,
                                       ApprovedByUserId As Integer?, ApprovedByUsername As String,
                                       ExceedsThreshold As Boolean, Status As String,
                                       CreatedAtUtc As DateTime, UpdatedAtUtc As DateTime))

            Dim direction As String = If(sortDescending, "DESC", "ASC")

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT a.Id, a.ProductId, p.Sku, p.Name, a.QuantityVariance, a.Reason, " &
                    "       a.RequestedByUserId, ru.Username, a.ApprovedByUserId, au.Username, " &
                    "       a.ExceedsThreshold, a.Status, a.CreatedAtUtc, a.UpdatedAtUtc " &
                    "  FROM StockAdjustments a" &
                    "  JOIN Products p ON p.Id = a.ProductId" &
                    "  JOIN Users ru ON ru.Id = a.RequestedByUserId" &
                    "  LEFT JOIN Users au ON au.Id = a.ApprovedByUserId" &
                    whereClause &
                    " ORDER BY a.CreatedAtUtc " & direction & ", a.Id " & direction &
                    " LIMIT @pageSize OFFSET @offset;"
                addParameters(command)
                command.Parameters.AddWithValue("@pageSize", pageSize)
                command.Parameters.AddWithValue("@offset", (page - 1) * pageSize)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)

                        ' Positional indices throughout - ru.Username and
                        ' au.Username both project as column name "Username",
                        ' so a name-based GetOrdinal("Username")/GetString("Username")
                        ' would be ambiguous (MySqlConnector resolves it to
                        ' whichever matches first, silently). Every column in
                        ' this row is read positionally for exactly that reason.
                        Const approvedByUserIdOrdinal As Integer = 8
                        Const approvedByUsernameOrdinal As Integer = 9

                        items.Add((
                            Id:=reader.GetInt32(0), ProductId:=reader.GetInt32(1), ProductSku:=reader.GetString(2), ProductName:=reader.GetString(3),
                            QuantityVariance:=reader.GetDecimal(4), Reason:=reader.GetString(5),
                            RequestedByUserId:=reader.GetInt32(6), RequestedByUsername:=reader.GetString(7),
                            ApprovedByUserId:=If(reader.IsDBNull(approvedByUserIdOrdinal), CType(Nothing, Integer?), reader.GetInt32(approvedByUserIdOrdinal)),
                            ApprovedByUsername:=If(reader.IsDBNull(approvedByUsernameOrdinal), Nothing, reader.GetString(approvedByUsernameOrdinal)),
                            ExceedsThreshold:=reader.GetBoolean(10), Status:=reader.GetString(11),
                            CreatedAtUtc:=reader.GetDateTime(12), UpdatedAtUtc:=reader.GetDateTime(13)))

                    End While
                End Using
            End Using

            Return (Items:=items, TotalCount:=totalCount)

        End Function

        ' --------------------------------------------------------------- helpers

        Private Shared Function SalesByProductOrderByColumn(sortField As SalesByProductSortField) As String
            Select Case sortField
                Case SalesByProductSortField.QuantitySold
                    Return "sold.QuantitySold"
                Case SalesByProductSortField.GrossSalesValue
                    Return "sold.GrossSalesValue"
                Case Else
                    Return "p.Name"
            End Select
        End Function

        ''' <summary>
        ''' Empty when both bounds are unset, else " AND col &gt;= @fromUtc"
        ''' and/or " AND col &lt; @toUtcExclusive" - built once per query so
        ''' the SAME two parameter names (<see cref="AddDateParameters"/>)
        ''' are reused across every WHERE clause a query needs, whatever
        ''' column each one filters.
        ''' </summary>
        Private Shared Function DateWhereClause(column As String, fromUtc As DateTime?, toUtcExclusive As DateTime?) As String

            Dim clause As String = String.Empty

            If fromUtc.HasValue Then
                clause &= $" AND {column} >= @fromUtc"
            End If

            If toUtcExclusive.HasValue Then
                clause &= $" AND {column} < @toUtcExclusive"
            End If

            Return clause

        End Function

        Private Shared Sub AddDateParameters(command As MySqlCommand, fromUtc As DateTime?, toUtcExclusive As DateTime?)

            If fromUtc.HasValue Then
                command.Parameters.AddWithValue("@fromUtc", fromUtc.Value)
            End If

            If toUtcExclusive.HasValue Then
                command.Parameters.AddWithValue("@toUtcExclusive", toUtcExclusive.Value)
            End If

        End Sub

        ''' <summary>
        ''' Sums committed SalePayments.Amount by Method for completed sales
        ''' in the window, one row per Merchandising.Domain.Sales.PaymentMethod
        ''' name, COALESCEd to 0.0000 for a method with no rows in scope - the
        ''' same zero-filled shape CashierSessionRepository.GetPaymentTotalsAsync
        ''' already establishes for a single session, applied here to a date
        ''' window instead.
        ''' </summary>
        Private Shared Async Function GetPaymentMethodTotalsAsync(
            connection As MySqlConnection,
            fromUtc As DateTime?,
            toUtcExclusive As DateTime?,
            cancellationToken As CancellationToken) As Task(Of IReadOnlyList(Of (Method As String, Amount As Decimal)))

            Dim totals As New Dictionary(Of String, Decimal)(StringComparer.Ordinal)
            For Each methodName As String In {"Cash", "Card", "EWallet"}
                totals(methodName) = 0D
            Next

            Dim whereClause As String = DateWhereClause("s.CreatedAtUtc", fromUtc, toUtcExclusive)

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT sp.Method, SUM(sp.Amount) " &
                    "  FROM SalePayments sp JOIN Sales s ON s.Id = sp.SaleId " &
                    " WHERE s.Status = 'Completed'" & whereClause &
                    " GROUP BY sp.Method;"
                AddDateParameters(command, fromUtc, toUtcExclusive)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        totals(reader.GetString(0)) = reader.GetDecimal(1)
                    End While
                End Using
            End Using

            Return totals.Select(Function(kvp) (Method:=kvp.Key, Amount:=kvp.Value)).ToList()

        End Function

        ''' <summary>Same shape as <see cref="GetPaymentMethodTotalsAsync"/>, grouped additionally by CashierUserId, restricted to <paramref name="cashierIds"/>. Empty when <paramref name="cashierIds"/> is empty.</summary>
        Private Shared Async Function GetPaymentTotalsByCashierAsync(
            connection As MySqlConnection,
            soldWhere As String,
            fromUtc As DateTime?,
            toUtcExclusive As DateTime?,
            cashierIds As IReadOnlyList(Of Integer),
            cancellationToken As CancellationToken) As Task(Of IReadOnlyDictionary(Of Integer, IReadOnlyList(Of (Method As String, Amount As Decimal))))

            Dim result As New Dictionary(Of Integer, Dictionary(Of String, Decimal))

            If cashierIds.Count = 0 Then
                Return result.ToDictionary(
                    Function(kvp) kvp.Key,
                    Function(kvp) CType(kvp.Value.Select(Function(inner) (Method:=inner.Key, Amount:=inner.Value)).ToList(),
                                        IReadOnlyList(Of (Method As String, Amount As Decimal))))
            End If

            For Each cashierId As Integer In cashierIds
                Dim zeroed As New Dictionary(Of String, Decimal)(StringComparer.Ordinal)
                For Each methodName As String In {"Cash", "Card", "EWallet"}
                    zeroed(methodName) = 0D
                Next
                result(cashierId) = zeroed
            Next

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT s.CashierUserId, sp.Method, SUM(sp.Amount) " &
                    "  FROM SalePayments sp JOIN Sales s ON s.Id = sp.SaleId " &
                    " WHERE s.Status = 'Completed'" & soldWhere &
                    "   AND s.CashierUserId IN (" & SaleRepository.InClausePlaceholders(cashierIds.Count) & ") " &
                    " GROUP BY s.CashierUserId, sp.Method;"
                AddDateParameters(command, fromUtc, toUtcExclusive)
                SaleRepository.AddInClauseParameters(command, cashierIds)

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        result(reader.GetInt32(0))(reader.GetString(1)) = reader.GetDecimal(2)
                    End While
                End Using
            End Using

            Return result.ToDictionary(
                Function(kvp) kvp.Key,
                Function(kvp) CType(kvp.Value.Select(Function(inner) (Method:=inner.Key, Amount:=inner.Value)).ToList(),
                                    IReadOnlyList(Of (Method As String, Amount As Decimal))))

        End Function

    End Class

End Namespace
