' Merchandising.Infrastructure.Data.PurchaseOrderNumberGenerator
'
' P3-03: the server-generated order number, PO-yyyyMMdd-NNNN.
'
' WHY THE SERVER GENERATES IT. Spec section 10.1's purchase order is a
' document a human refers to by name, so it needs a short readable identity
' as well as a surrogate key. A client-supplied one would be a second,
' untrustworthy source for that identity, and it would buy nothing the
' ADR-007 idempotency key does not already buy - a retry is made safe by the
' key, not by the caller inventing the same number twice.
'
' WHY THIS IS NOT THE GUARANTEE, AND WHAT IS. This class reads the highest
' number already issued for today and returns the next one. Under
' concurrency that read is a guess: two transactions can compute the same
' candidate, because READ COMMITTED (ADR-006) does not show either one the
' other's uncommitted row. UQ_PurchaseOrders_OrderNumber (0008) is the real
' guarantee - the loser's INSERT blocks on the unique-index lock and then
' fails ERROR 1062, and PurchaseOrderService retries with a freshly-computed
' candidate. That is the same "a pre-check is a courtesy, the unique index is
' the guarantee" shape SupplierRepository and ProductRepository already use,
' and the same lock mechanism IdempotencyStore's header describes.
'
' The retry is not a rare path being tolerated - it is the designed path, and
' PurchaseOrderCreationTests.CreatePurchaseOrder_TenConcurrentRequestsDistinct
' Keys_ProduceTenDistinctOrderNumbers exercises it deliberately with ten
' simultaneous creates. Without the retry those would surface as 500s.
'
' THE DATE IS A LABEL, NOT A BOUNDARY. yyyyMMdd is UTC, matching the UTC
' CreatedAtUtc it sits beside. Rendering an order's date in the store time
' zone (Asia/Manila), and any filtering on a store-local date boundary,
' belongs to P3-06 which owns store-time-zone boundaries - it must not read
' this string to decide what day an order was raised. Confirmed with the user
' at P3-03.

Imports System.Globalization
Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Data

    Public NotInheritable Class PurchaseOrderNumberGenerator

        ''' <summary>The fixed leading token of every order number.</summary>
        Public Const Prefix As String = "PO-"

        Private Const DatePattern As String = "yyyyMMdd"
        Private Const SequenceFormat As String = "0000"

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Returns the next unused order number for <paramref name="utcNow"/>'s
        ''' UTC date, as a candidate only - the caller must be prepared for
        ''' ERROR 1062 and call again. See this class's header.
        ''' </summary>
        Public Shared Async Function NextCandidateAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            utcNow As DateTime,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of String)

            Dim datePart As String = utcNow.ToString(DatePattern, CultureInfo.InvariantCulture)
            Dim dayPrefix As String = Prefix & datePart & "-"

            Dim highest As String = Nothing

            Using command As MySqlCommand = connection.CreateCommand()
                command.Transaction = transaction
                ' MAX over a fixed-width, zero-padded suffix orders the same
                ' way the numbers do, so no CAST is needed. The LIKE pattern
                ' is parameterised like every other statement in this
                ' project (CLAUDE.md section 10) even though it is built from
                ' a formatted date rather than from input.
                command.CommandText =
                    "SELECT MAX(OrderNumber) FROM PurchaseOrders WHERE OrderNumber LIKE @dayPattern;"
                command.Parameters.AddWithValue("@dayPattern", dayPrefix & "%")

                Dim result As Object = Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False)
                If result IsNot Nothing AndAlso result IsNot DBNull.Value Then
                    highest = CStr(result)
                End If
            End Using

            Return dayPrefix & (HighestSequence(highest, dayPrefix) + 1).ToString(SequenceFormat, CultureInfo.InvariantCulture)

        End Function

        ''' <summary>
        ''' The sequence number embedded in <paramref name="highest"/>, or 0
        ''' when there is none for this day. A stored value whose suffix does
        ''' not parse returns 0 rather than throwing: the unique index still
        ''' prevents a collision, so the worst case is extra retries, not a
        ''' duplicate - and a 500 on every create is a far worse response to
        ''' one malformed legacy row than a slow one.
        ''' </summary>
        Private Shared Function HighestSequence(highest As String, dayPrefix As String) As Integer

            If String.IsNullOrEmpty(highest) OrElse highest.Length <= dayPrefix.Length Then
                Return 0
            End If

            Dim suffix As String = highest.Substring(dayPrefix.Length)
            Dim parsed As Integer

            If Integer.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, parsed) Then
                Return parsed
            End If

            Return 0

        End Function

    End Class

End Namespace
