' Merchandising.Infrastructure.Data.LedgerReconciliation
'
' P4-01 / ADR-021 / plan.md section 7's "key design call of the phase": one
' query that proves SUM(StockMovements) = StockBalances for every product.
'
' Products is the driving table, not a UNION of the two ledgers, because
' every StockMovements/StockBalances row carries a FOREIGN KEY to Products
' and products are never deleted (spec section 12) - so Products already
' names every ProductId either ledger could ever reference. LEFT JOINing a
' movements subquery and StockBalances onto it, then COALESCEing both sides
' to 0.000 before comparing, is what makes "a product missing from either
' side is a discrepancy, not a skip" fall out for free: a product with
' movements but no balance row compares a real sum against 0 and disagrees;
' a product with a balance row but no movements compares 0 against a real
' balance and disagrees; a product genuinely untouched by both compares
' 0 = 0 and is silent. No special-casing either "missing" case needed.
'
' The inequality runs IN SQL, as DECIMAL(19,3) <> DECIMAL(19,3) - MariaDB's
' DECIMAL comparison is exact, never a floating-point approximation, so
' this is the "never a floating-point tolerance" done when the values are
' compared, not attempted after everything has been read into VB Decimals.
'
' No explicit transaction is opened. ConnectionFactory already pins every
' connection's session to tx_isolation = READ-COMMITTED (ADR-006), so a
' single autocommit SELECT here can only ever see committed rows - it
' cannot be fooled by a concurrent transaction still in flight, without
' this query needing to know or care that the other transaction exists.

Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Data

    Public NotInheritable Class LedgerReconciliation

        ''' <summary>
        ''' Every product whose StockMovements ledger does not sum to its
        ''' StockBalances quantity, right now, as committed. Empty means the
        ''' ledger reconciles for every product in the database.
        ''' </summary>
        Public Shared Async Function FindDiscrepanciesAsync(
            connection As MySqlConnection,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of IReadOnlyList(Of LedgerDiscrepancy))

            Dim discrepancies As New List(Of LedgerDiscrepancy)

            Using command As MySqlCommand = connection.CreateCommand()
                command.CommandText =
                    "SELECT p.Id AS ProductId, " &
                    "       COALESCE(m.TotalDelta, 0.000) AS ExpectedQuantity, " &
                    "       COALESCE(b.Quantity, 0.000) AS ActualQuantity " &
                    "  FROM Products p " &
                    "  LEFT JOIN (SELECT ProductId, SUM(Delta) AS TotalDelta " &
                    "               FROM StockMovements " &
                    "              GROUP BY ProductId) m ON m.ProductId = p.Id " &
                    "  LEFT JOIN StockBalances b ON b.ProductId = p.Id " &
                    " WHERE COALESCE(m.TotalDelta, 0.000) <> COALESCE(b.Quantity, 0.000) " &
                    " ORDER BY p.Id;"

                Using reader As MySqlDataReader = Await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(False)
                    Dim productIdOrdinal As Integer = reader.GetOrdinal("ProductId")
                    Dim expectedOrdinal As Integer = reader.GetOrdinal("ExpectedQuantity")
                    Dim actualOrdinal As Integer = reader.GetOrdinal("ActualQuantity")

                    While Await reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                        Dim productId As Integer = reader.GetInt32(productIdOrdinal)
                        Dim expectedQuantity As Decimal = reader.GetDecimal(expectedOrdinal)
                        Dim actualQuantity As Decimal = reader.GetDecimal(actualOrdinal)
                        discrepancies.Add(New LedgerDiscrepancy(productId, expectedQuantity, actualQuantity))
                    End While
                End Using
            End Using

            Return discrepancies

        End Function

    End Class

End Namespace
