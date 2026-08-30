' Merchandising.Api.Sales.CashierSessionService
'
' P5-04 / ADR-006 / ADR-007. Spec section 10.3: "A sale requires an open
' cashier session." Two atomic commands, each its own transaction:
'
'   OpenAsync  - claims the idempotency key, then attempts the insert. The
'                unique index on CashierSessions.OpenSessionOwner
'                (0011_pos.sql, ADR-018's generated-column trick) is the
'                thing that actually enforces "one open session per
'                cashier" - this method does not re-check that itself
'                beforehand (that would be read-then-write, the exact
'                defect ADR-006 exists to rule out). Instead it attempts
'                the insert and catches the duplicate-key MySqlException a
'                losing concurrent attempt raises, rolls the whole
'                transaction back (the idempotency claim included - a
'                retry with the same key gets to try again, the same
'                "as if it never happened" shape AdjustmentService.RequestAsync's
'                InsufficientStock rollback uses) and reports AlreadyOpen.
'   CloseAsync - the row must be locked and Open
'                (CashierSessionRepository.GetForUpdateAsync). Flips it to
'                Closed with the closing actor and timestamp - nothing
'                else. DeclaredCash/CalculatedCash/CashVariance are P5-05's
'                columns to set, extending this same method.
'
' UNEXPECTED EXCEPTIONS ARE DELIBERATELY NOT CAUGHT (the duplicate-key catch
' below is narrowly scoped to ErrorCode = DuplicateKeyEntry, nothing else),
' AND THERE IS NO Try AROUND ANY OTHER PART OF EITHER TRANSACTION - the
' identical arrangement every other service in this solution uses
' (ReceivingService's header explains the mechanism in full).

Imports System.Data
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports Merchandising.Contracts.Sales
Imports Merchandising.Domain
Imports Merchandising.Infrastructure.Data
Imports MySqlConnector

Namespace Sales

    Public NotInheritable Class CashierSessionService

        ''' <summary>ADR-007's Scope column value for opening a session.</summary>
        Public Const OpenIdempotencyScope As String = "Sales.CashierSessionOpen"

        ''' <summary>ADR-007's Scope column value for closing a session.</summary>
        Public Const CloseIdempotencyScope As String = "Sales.CashierSessionClose"

        Private Const AuditActionOpened As String = "CashierSessionOpened"
        Private Const AuditActionClosed As String = "CashierSessionClosed"
        Private Const AuditActionReplayed As String = "CashierSessionReplayed"

        Private ReadOnly _connectionFactory As ConnectionFactory

        Public Sub New(connectionFactory As ConnectionFactory)

            If connectionFactory Is Nothing Then
                Throw New ArgumentNullException(NameOf(connectionFactory))
            End If

            _connectionFactory = connectionFactory

        End Sub

        ''' <summary>
        ''' Opens a new session for <paramref name="openedByUserId"/>, with
        ''' <paramref name="openingFloat"/> as its declared opening float.
        ''' <paramref name="openingFloat"/> must already be at storage scale -
        ''' the caller's responsibility to refuse a malformed request before
        ''' reaching this method (ADR-004.1), re-asserted here as a hard
        ''' precondition.
        ''' </summary>
        Public Async Function OpenAsync(
            openedByUserId As Integer,
            openingFloat As Decimal,
            correlationId As String,
            idempotencyKey As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of CashierSessionOutcome)

            DecimalScaleGuard.EnsureMoneyScale(openingFloat)

            If openingFloat < 0D Then
                Throw New ArgumentException(
                    "An opening float cannot be negative. The caller is responsible for refusing that with a " &
                    "field-level validation error before reaching this method.", NameOf(openingFloat))
            End If

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim transaction As MySqlTransaction =
                    Await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(False)

                Dim claim =
                    Await IdempotencyStore.TryClaimAsync(
                        connection, transaction, OpenIdempotencyScope, idempotencyKey, cancellationToken).ConfigureAwait(False)

                If Not claim.Claimed Then
                    Return Await ReplayAsync(
                        connection, transaction, OpenIdempotencyScope, idempotencyKey, openedByUserId, correlationId, cancellationToken).ConfigureAwait(False)
                End If

                Dim openedAtUtc As DateTime = DateTime.UtcNow
                Dim sessionId As Integer
                Dim alreadyOpen As Boolean = False

                Try
                    sessionId =
                        Await CashierSessionRepository.InsertOpenAsync(
                            connection, transaction, openedByUserId, openingFloat, openedAtUtc, cancellationToken).ConfigureAwait(False)

                Catch ex As MySqlException When ex.ErrorCode = MySqlErrorCode.DuplicateKeyEntry
                    alreadyOpen = True
                    sessionId = 0
                End Try

                If alreadyOpen Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return CashierSessionOutcome.AlreadyOpen()
                End If

                Dim locked = Await CashierSessionRepository.GetByIdAsync(
                    connection, sessionId, cancellationToken, transaction).ConfigureAwait(False)

                Dim response As CashierSessionResponse = ToResponse(locked)

                Await AuditLogWriter.WriteAsync(
                    connection, openedByUserId, AuditActionOpened, sessionId.ToString(), "Success", correlationId,
                    detail:=$"OpeningFloat={openingFloat}",
                    cancellationToken:=cancellationToken, transaction:=transaction).ConfigureAwait(False)

                Await IdempotencyStore.CompleteAsync(
                    connection, transaction, claim.Id, JsonSerializer.Serialize(response), cancellationToken).ConfigureAwait(False)

                Await transaction.CommitAsync(cancellationToken).ConfigureAwait(False)
                Await transaction.DisposeAsync().ConfigureAwait(False)

                Return CashierSessionOutcome.Created(response)

            End Using

        End Function

        ''' <summary>Closes an Open session. A Closed session is immutable from here - it may never be closed again.</summary>
        Public Async Function CloseAsync(
            sessionId As Integer,
            closedByUserId As Integer,
            correlationId As String,
            idempotencyKey As String,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of CashierSessionOutcome)

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim transaction As MySqlTransaction =
                    Await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(False)

                Dim claim =
                    Await IdempotencyStore.TryClaimAsync(
                        connection, transaction, CloseIdempotencyScope, idempotencyKey, cancellationToken).ConfigureAwait(False)

                If Not claim.Claimed Then
                    Return Await ReplayAsync(
                        connection, transaction, CloseIdempotencyScope, idempotencyKey, closedByUserId, correlationId, cancellationToken).ConfigureAwait(False)
                End If

                Dim locked =
                    Await CashierSessionRepository.GetForUpdateAsync(
                        connection, transaction, sessionId, cancellationToken).ConfigureAwait(False)

                If Not locked.Found Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return CashierSessionOutcome.NotFound()
                End If

                If Not String.Equals(locked.Status, "Open", StringComparison.Ordinal) Then
                    Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Await transaction.DisposeAsync().ConfigureAwait(False)
                    Return CashierSessionOutcome.NotOpen()
                End If

                Dim closedAtUtc As DateTime = DateTime.UtcNow

                Dim closed As Boolean =
                    Await CashierSessionRepository.MarkClosedAsync(
                        connection, transaction, sessionId, closedByUserId, closedAtUtc, cancellationToken).ConfigureAwait(False)

                If Not closed Then
                    Throw New InvalidOperationException(
                        $"CashierSession {sessionId} was locked by GetForUpdateAsync but MarkClosedAsync affected " &
                        "zero rows. This should be unreachable.")
                End If

                Dim reloaded = Await CashierSessionRepository.GetByIdAsync(
                    connection, sessionId, cancellationToken, transaction).ConfigureAwait(False)

                Dim response As CashierSessionResponse = ToResponse(reloaded)

                Await AuditLogWriter.WriteAsync(
                    connection, closedByUserId, AuditActionClosed, sessionId.ToString(), "Success", correlationId,
                    cancellationToken:=cancellationToken, transaction:=transaction).ConfigureAwait(False)

                Await IdempotencyStore.CompleteAsync(
                    connection, transaction, claim.Id, JsonSerializer.Serialize(response), cancellationToken).ConfigureAwait(False)

                Await transaction.CommitAsync(cancellationToken).ConfigureAwait(False)
                Await transaction.DisposeAsync().ConfigureAwait(False)

                Return CashierSessionOutcome.Created(response)

            End Using

        End Function

        ''' <summary>A plain read of one session, open, closed, or otherwise. Used for GET-shaped callers (none in this card; kept for the same reason StockCountService.GetAsync exists ahead of any route needing it).</summary>
        Public Async Function GetAsync(
            sessionId As Integer,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of CashierSessionResponse)

            Using connection As MySqlConnection =
                Await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim found = Await CashierSessionRepository.GetByIdAsync(
                    connection, sessionId, cancellationToken).ConfigureAwait(False)

                Return If(found.Found, ToResponse(found), Nothing)

            End Using

        End Function

        ' ----------------------------------------------------------- helpers

        Private Shared Function ToResponse(
            row As (Found As Boolean, Id As Integer, OpenedByUserId As Integer, ClosedByUserId As Integer?,
                   OpeningFloat As Decimal, DeclaredCash As Decimal?, CalculatedCash As Decimal?, CashVariance As Decimal?,
                   Status As String, OpenedAtUtc As DateTime, ClosedAtUtc As DateTime?,
                   RowVersion As Long, CreatedAtUtc As DateTime, UpdatedAtUtc As DateTime)) As CashierSessionResponse

            Return New CashierSessionResponse With {
                .Id = row.Id,
                .OpenedByUserId = row.OpenedByUserId,
                .ClosedByUserId = row.ClosedByUserId,
                .OpeningFloat = row.OpeningFloat,
                .DeclaredCash = row.DeclaredCash,
                .CalculatedCash = row.CalculatedCash,
                .CashVariance = row.CashVariance,
                .Status = row.Status,
                .OpenedAtUtc = row.OpenedAtUtc,
                .ClosedAtUtc = row.ClosedAtUtc,
                .RowVersion = row.RowVersion,
                .CreatedAtUtc = row.CreatedAtUtc,
                .UpdatedAtUtc = row.UpdatedAtUtc
            }

        End Function

        ''' <summary>ADR-007: a losing claim replays the original committed response rather than doing any work. Shared by both commands.</summary>
        Private Shared Async Function ReplayAsync(
            connection As MySqlConnection,
            transaction As MySqlTransaction,
            scope As String,
            idempotencyKey As String,
            actorUserId As Integer,
            correlationId As String,
            cancellationToken As CancellationToken) As Task(Of CashierSessionOutcome)

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

            Return CashierSessionOutcome.Replayed(storedPayload)

        End Function

    End Class

End Namespace
