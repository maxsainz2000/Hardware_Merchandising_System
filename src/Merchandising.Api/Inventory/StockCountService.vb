' Merchandising.Api.Inventory.StockCountService
'
' P4-09 / ADR-006 / ADR-007. Spec section 10.2: "Stock counts record the
' counted quantity, system quantity, variance, count session, counted by,
' reviewed by, reason, and approval state." Three atomic commands, each its
' own transaction:
'
'   OpenAsync       - claims the idempotency key, inserts the StockCounts
'                     header (Open), audits, commits.
'   RecordLineAsync - claims the key, LOCKS the StockCounts header
'                     (StockCountRepository.GetForUpdateAsync) and refuses
'                     NotOpen if it is not Open, confirms the product exists,
'                     reads StockBalances with StockRepository.GetQuantityAsync -
'                     an ORDINARY, NON-LOCKING SELECT, never FOR UPDATE - and
'                     computes Variance = Counted - System right there, once.
'                     This is what makes the card's Done-when box 2 true: a
'                     count session never takes any lock a sale or a receipt
'                     would also need, so it cannot block one.
'   CloseAsync      - claims the key, locks the header, refuses NotOpen if
'                     already Closed, flips to Closed, audits, commits.
'
' THE COUNT ITSELF CHANGES NO STOCK (the card's own "Do" text). There is no
' StockMovements row anywhere in this file, and no call into StockRepository's
' write methods (IncrementAsync/TryDecrementAsync) - only GetQuantityAsync,
' a read. P4-01's ledger reconciliation is therefore untouched by this card;
' nothing here can ever put StockMovements and StockBalances out of step.
'
' A LATER BALANCE CHANGE NEVER RETROACTIVELY ALTERS A RECORDED VARIANCE
' (card Done-when box 1). SystemQuantity and Variance are computed once,
' inside RecordLineAsync's own transaction, and stored - StockCountLineResponse's
' header explains why there is no generated column or later recomputation.
'
' UNEXPECTED EXCEPTIONS ARE DELIBERATELY NOT CAUGHT, AND THERE IS NO Try
' AROUND ANY TRANSACTION - the identical arrangement every other service in
' this solution uses (ReceivingService's header explains the mechanism in
' full).

Imports System.Data
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports Merchandising.Contracts.Inventory
Imports Merchandising.Domain
Imports Merchandising.Domain.Entities
Imports Merchandising.Infrastructure.Data
Imports MySqlConnector

Namespace Inventory

    Public NotInheritable Class StockCountService

        ''' <summary>ADR-007's Scope column value for opening a session.</summary>
        Public Const OpenIdempotencyScope As String = "Inventory.StockCountOpen"

        ''' <summary>ADR-007's Scope column value for recording one line.</summary>
        Public Const RecordLineIdempotencyScope As String = "Inventory.StockCountRecordLine"

        ''' <summary>ADR-007's Scope column value for closing a session.</summary>
        Public Const CloseIdempotencyScope As String = "Inventory.StockCountClose"

        Private Const AuditActionOpened As String = "StockCountOpened"
        Private Const AuditActionLineRecorded As String = "StockCountLineRecorded"
        Private Const AuditActionClosed As String = "StockCountClosed"
        Private Const AuditActionReplayed As String = "StockCountReplayed"

        Private ReadOnly _connectionFactory As ConnectionFactory

        Public Sub New(connectionFactory As ConnectionFactory)

            If connectionFactory Is Nothing Then
                Throw New ArgumentNullException(NameOf(connectionFactory))
            End If

            _connectionFactory = connectionFactory

        End Sub

        ''' <summary>Opens a new, empty stock-count session.</summary>
        Public Async Function OpenAsync(
            actorUserId As Integer,
            correlationId As String,
            idempotencyKey As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of StockCountOutcome)

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim transaction As MySqlTransaction =
                    Await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(False)

                Dim claim =
                    Await IdempotencyStore.TryClaimAsync(
                        connection, transaction, OpenIdempotencyScope, idempotencyKey, cancellationToken).ConfigureAwait(False)

                If Not claim.Claimed Then
                    Return Await ReplayAsync(
                        connection, transaction, OpenIdempotencyScope, idempotencyKey, actorUserId, correlationId, cancellationToken).ConfigureAwait(False)
                End If

                Dim countedAtUtc As DateTime = DateTime.UtcNow

                Dim stockCountId As Integer =
                    Await StockCountRepository.InsertStockCountAsync(
                        connection, transaction, actorUserId, countedAtUtc, cancellationToken).ConfigureAwait(False)

                Dim response As New StockCountResponse With {
                    .Id = stockCountId,
                    .Status = "Open",
                    .CountedByUserId = actorUserId,
                    .ApprovedByUserId = Nothing,
                    .CountedAtUtc = countedAtUtc,
                    .ApprovedAtUtc = Nothing,
                    .RowVersion = 0,
                    .CreatedAtUtc = countedAtUtc,
                    .UpdatedAtUtc = countedAtUtc,
                    .Lines = Array.Empty(Of StockCountLineResponse)()
                }

                Await AuditLogWriter.WriteAsync(
                    connection, actorUserId, AuditActionOpened, stockCountId.ToString(), "Success", correlationId,
                    cancellationToken:=cancellationToken, transaction:=transaction).ConfigureAwait(False)

                Await IdempotencyStore.CompleteAsync(
                    connection, transaction, claim.Id, JsonSerializer.Serialize(response), cancellationToken).ConfigureAwait(False)

                Await transaction.CommitAsync(cancellationToken).ConfigureAwait(False)
                Await transaction.DisposeAsync().ConfigureAwait(False)

                Return StockCountOutcome.CreatedSession(response)

            End Using

        End Function

        ''' <summary>
        ''' Records one product's counted quantity against an Open session.
        ''' <paramref name="countedQuantity"/> must already be at storage
        ''' scale - the caller's responsibility to refuse a malformed request
        ''' before reaching this method (ADR-004.1), re-asserted here as a
        ''' hard precondition.
        ''' </summary>
        Public Async Function RecordLineAsync(
            stockCountId As Integer,
            productId As Integer,
            countedQuantity As Decimal,
            actorUserId As Integer,
            correlationId As String,
            idempotencyKey As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of StockCountOutcome)

            DecimalScaleGuard.EnsureQuantityScale(countedQuantity)

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim transaction As MySqlTransaction =
                    Await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(False)

                Dim claim =
                    Await IdempotencyStore.TryClaimAsync(
                        connection, transaction, RecordLineIdempotencyScope, idempotencyKey, cancellationToken).ConfigureAwait(False)

                If Not claim.Claimed Then
                    Return Await ReplayAsync(
                        connection, transaction, RecordLineIdempotencyScope, idempotencyKey, actorUserId, correlationId, cancellationToken).ConfigureAwait(False)
                End If

                Dim locked =
                    Await StockCountRepository.GetForUpdateAsync(
                        connection, transaction, stockCountId, cancellationToken).ConfigureAwait(False)

                If Not locked.Found Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return StockCountOutcome.NotFound()
                End If

                If Not String.Equals(locked.Status, "Open", StringComparison.Ordinal) Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return StockCountOutcome.NotOpen()
                End If

                Dim product As Product =
                    Await ProductRepository.GetByIdAsync(
                        connection, productId, cancellationToken, transaction).ConfigureAwait(False)

                If product Is Nothing Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return StockCountOutcome.ProductNotFound()
                End If

                ' NOT FOR UPDATE - an ordinary, non-locking read. This is the
                ' whole mechanism behind Done-when box 2: capturing "system
                ' quantity at the moment of counting" takes no lock a sale or
                ' a receipt would also need.
                Dim systemQuantity As Decimal =
                    Await StockRepository.GetQuantityAsync(
                        connection, transaction, productId, cancellationToken).ConfigureAwait(False)

                Dim variance As Decimal = countedQuantity - systemQuantity
                Dim createdAtUtc As DateTime = DateTime.UtcNow

                Dim lineId As Integer =
                    Await StockCountRepository.InsertStockCountLineAsync(
                        connection, transaction, stockCountId, productId, countedQuantity, systemQuantity, variance,
                        createdAtUtc, cancellationToken).ConfigureAwait(False)

                Dim response As New StockCountLineResponse With {
                    .Id = lineId,
                    .ProductId = productId,
                    .ProductSku = product.Sku,
                    .ProductName = product.Name,
                    .CountedQuantity = countedQuantity,
                    .SystemQuantity = systemQuantity,
                    .Variance = variance,
                    .CreatedAtUtc = createdAtUtc
                }

                Await AuditLogWriter.WriteAsync(
                    connection, actorUserId, AuditActionLineRecorded, lineId.ToString(), "Success", correlationId,
                    detail:=$"StockCountId={stockCountId}, ProductId={productId}, Variance={variance}",
                    cancellationToken:=cancellationToken, transaction:=transaction).ConfigureAwait(False)

                Await IdempotencyStore.CompleteAsync(
                    connection, transaction, claim.Id, JsonSerializer.Serialize(response), cancellationToken).ConfigureAwait(False)

                Await transaction.CommitAsync(cancellationToken).ConfigureAwait(False)
                Await transaction.DisposeAsync().ConfigureAwait(False)

                Return StockCountOutcome.CreatedLine(response)

            End Using

        End Function

        ''' <summary>Closes an Open session. A Closed count is immutable - no further line may be recorded, and it cannot be closed again.</summary>
        Public Async Function CloseAsync(
            stockCountId As Integer,
            actorUserId As Integer,
            correlationId As String,
            idempotencyKey As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of StockCountOutcome)

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim transaction As MySqlTransaction =
                    Await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(False)

                Dim claim =
                    Await IdempotencyStore.TryClaimAsync(
                        connection, transaction, CloseIdempotencyScope, idempotencyKey, cancellationToken).ConfigureAwait(False)

                If Not claim.Claimed Then
                    Return Await ReplayAsync(
                        connection, transaction, CloseIdempotencyScope, idempotencyKey, actorUserId, correlationId, cancellationToken).ConfigureAwait(False)
                End If

                Dim locked =
                    Await StockCountRepository.GetForUpdateAsync(
                        connection, transaction, stockCountId, cancellationToken).ConfigureAwait(False)

                If Not locked.Found Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return StockCountOutcome.NotFound()
                End If

                If Not String.Equals(locked.Status, "Open", StringComparison.Ordinal) Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return StockCountOutcome.NotOpen()
                End If

                Dim updatedAtUtc As DateTime = DateTime.UtcNow

                Await StockCountRepository.CloseStockCountAsync(
                    connection, transaction, stockCountId, updatedAtUtc, cancellationToken).ConfigureAwait(False)

                Dim lines = Await StockCountRepository.GetLinesAsync(
                    connection, stockCountId, cancellationToken, transaction).ConfigureAwait(False)

                Dim response As StockCountResponse =
                    Await BuildResponseAsync(
                        connection, transaction, stockCountId, lines, cancellationToken).ConfigureAwait(False)

                Await AuditLogWriter.WriteAsync(
                    connection, actorUserId, AuditActionClosed, stockCountId.ToString(), "Success", correlationId,
                    detail:=$"Lines={lines.Count}",
                    cancellationToken:=cancellationToken, transaction:=transaction).ConfigureAwait(False)

                Await IdempotencyStore.CompleteAsync(
                    connection, transaction, claim.Id, JsonSerializer.Serialize(response), cancellationToken).ConfigureAwait(False)

                Await transaction.CommitAsync(cancellationToken).ConfigureAwait(False)
                Await transaction.DisposeAsync().ConfigureAwait(False)

                Return StockCountOutcome.CreatedSession(response)

            End Using

        End Function

        ''' <summary>A plain read of one session, with its lines - open, closed, or otherwise. Used for GET, and shares its row-to-response mapping with CloseAsync.</summary>
        Public Async Function GetAsync(
            stockCountId As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of StockCountResponse)

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim header = Await StockCountRepository.GetByIdAsync(
                    connection, stockCountId, cancellationToken).ConfigureAwait(False)

                If Not header.Found Then
                    Return Nothing
                End If

                Dim lines = Await StockCountRepository.GetLinesAsync(
                    connection, stockCountId, cancellationToken).ConfigureAwait(False)

                Return Await BuildResponseFromHeaderAsync(connection, Nothing, stockCountId, header, lines, cancellationToken).ConfigureAwait(False)

            End Using

        End Function

        ' ----------------------------------------------------------- helpers

        Private Shared Async Function BuildResponseAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            stockCountId As Integer,
            lines As IReadOnlyList(Of (Id As Integer, ProductId As Integer, CountedQuantity As Decimal, SystemQuantity As Decimal, Variance As Decimal, CreatedAtUtc As DateTime)),
            cancellationToken As CancellationToken) As Task(Of StockCountResponse)

            Dim header = Await StockCountRepository.GetByIdAsync(
                connection, stockCountId, cancellationToken, transaction).ConfigureAwait(False)

            Return Await BuildResponseFromHeaderAsync(connection, transaction, stockCountId, header, lines, cancellationToken).ConfigureAwait(False)

        End Function

        Private Shared Async Function BuildResponseFromHeaderAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            stockCountId As Integer,
            header As (Found As Boolean, Status As String, CountedByUserId As Integer, ApprovedByUserId As Integer?,
                       CountedAtUtc As DateTime, ApprovedAtUtc As DateTime?, RowVersion As Long,
                       CreatedAtUtc As DateTime, UpdatedAtUtc As DateTime),
            lines As IReadOnlyList(Of (Id As Integer, ProductId As Integer, CountedQuantity As Decimal, SystemQuantity As Decimal, Variance As Decimal, CreatedAtUtc As DateTime)),
            cancellationToken As CancellationToken) As Task(Of StockCountResponse)

            Dim lineResponses As New List(Of StockCountLineResponse)

            For Each line In lines

                Dim product As Product =
                    Await ProductRepository.GetByIdAsync(
                        connection, line.ProductId, cancellationToken, transaction).ConfigureAwait(False)

                lineResponses.Add(New StockCountLineResponse With {
                    .Id = line.Id,
                    .ProductId = line.ProductId,
                    .ProductSku = If(product IsNot Nothing, product.Sku, String.Empty),
                    .ProductName = If(product IsNot Nothing, product.Name, String.Empty),
                    .CountedQuantity = line.CountedQuantity,
                    .SystemQuantity = line.SystemQuantity,
                    .Variance = line.Variance,
                    .CreatedAtUtc = line.CreatedAtUtc
                })

            Next

            Return New StockCountResponse With {
                .Id = stockCountId,
                .Status = header.Status,
                .CountedByUserId = header.CountedByUserId,
                .ApprovedByUserId = header.ApprovedByUserId,
                .CountedAtUtc = header.CountedAtUtc,
                .ApprovedAtUtc = header.ApprovedAtUtc,
                .RowVersion = header.RowVersion,
                .CreatedAtUtc = header.CreatedAtUtc,
                .UpdatedAtUtc = header.UpdatedAtUtc,
                .Lines = lineResponses
            }

        End Function

        ''' <summary>ADR-007: a losing claim replays the original committed response rather than doing any work. Shared by all three commands.</summary>
        Private Shared Async Function ReplayAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            scope As String,
            idempotencyKey As String,
            actorUserId As Integer,
            correlationId As String,
            cancellationToken As CancellationToken) As Task(Of StockCountOutcome)

            Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
            Await transaction.DisposeAsync().ConfigureAwait(False)

            Dim storedPayload As String =
                Await IdempotencyStore.FindCompletedResponsePayloadAsync(
                    connection, scope, idempotencyKey, cancellationToken).ConfigureAwait(False)

            If storedPayload Is Nothing Then
                Throw New InvalidOperationException(
                    $"IdempotencyKeys row for scope '{scope}', key '{idempotencyKey}' exists but has no completed " &
                    "response. This should be unreachable - see IdempotencyStore's class header.")
            End If

            Await AuditLogWriter.WriteAsync(
                connection, actorUserId, AuditActionReplayed, idempotencyKey, "Success", correlationId,
                detail:="Idempotency key already committed; the original response was replayed.",
                cancellationToken:=cancellationToken).ConfigureAwait(False)

            Return StockCountOutcome.Replayed(storedPayload)

        End Function

    End Class

End Namespace
