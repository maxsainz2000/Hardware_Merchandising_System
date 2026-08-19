' Merchandising.Maintenance.Demo.SeedDemoCommand
'
' Creates the one product the P1-15 client spike decrements, and gives it an
' opening balance. Added at P1-15 because no migration seeds data (migrations
' are schema, deliberately) and the integration tests build their fixtures at
' runtime and tear them down - which left nothing on the database for a human
' to point a window at.
'
' Two things it does NOT do, and both are on purpose:
'
'   * It does not write StockBalances.Quantity directly. Opening stock arrives
'     as a MOVEMENT, in the same shape CLAUDE.md section 5 requires of every
'     stock change: conditional update, a StockMovements row, an AuditLogs row,
'     one transaction, affected-row count verified. A seed that bypassed the
'     ledger would leave a balance no movement explains - which is exactly the
'     inconsistency the append-only design exists to make impossible.
'   * It does not create the user. "create-user" already does that, and one
'     command that quietly does two jobs is harder to reason about than two.
'
' Runs as merch_migrator, the same identity as "migrate" and "create-user":
' seeding is a host-side bootstrap operation, not something merch_api does for
' itself.

Imports System.Globalization
Imports System.Runtime.ExceptionServices
Imports System.Threading
Imports Merchandising.Infrastructure.Data
Imports MySqlConnector

Namespace Demo

    ''' <summary>Seeds one demo product with an opening balance. Re-runnable.</summary>
    Public NotInheritable Class SeedDemoCommand

        ''' <summary>Reason recorded on the opening-balance movement.</summary>
        Private Const OpeningReason As String = "Opening balance (seed-demo)"

        ''' <summary>
        ''' Ensures the demo product exists and carries an opening balance.
        ''' Running it twice is safe: the second run finds the product, finds a
        ''' non-zero balance, and changes nothing.
        ''' </summary>
        Public Shared Async Function RunAsync(connectionFactory As ConnectionFactory,
                                              actorUsername As String,
                                              sku As String,
                                              name As String,
                                              price As Decimal,
                                              cost As Decimal,
                                              openingQuantity As Decimal,
                                              Optional cancellationToken As CancellationToken = Nothing) As Task(Of SeedDemoResult)

            If connectionFactory Is Nothing Then
                Throw New ArgumentNullException(NameOf(connectionFactory))
            End If

            If String.IsNullOrWhiteSpace(sku) Then
                Throw New SeedDemoCommandException("SKU must not be empty.")
            End If

            If openingQuantity < 0D Then
                Throw New SeedDemoCommandException("Opening quantity cannot be negative.")
            End If

            Dim correlationId As String = Guid.NewGuid().ToString("D")

            Using connection As MySqlConnection =
                Await connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(False)

                Dim actorUserId As Integer = Await FindUserIdAsync(connection, actorUsername, cancellationToken).ConfigureAwait(False)

                Dim transaction As MySqlTransaction =
                    Await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(False)

                ' VB cannot Await inside Catch or Finally (BC36943 - the trap
                ' CLAUDE.md section 3 warns about, and the one P1-10 hit). The
                ' rollback therefore happens after the Try closes, driven by a
                ' flag, and the original exception is rethrown with its stack
                ' intact rather than being swallowed.
                Dim committed As Boolean = False
                Dim failure As Exception = Nothing
                Dim result As SeedDemoResult = Nothing

                Try
                    Dim productId As Integer =
                        Await FindProductIdAsync(connection, transaction, sku, cancellationToken).ConfigureAwait(False)

                    Dim productCreated As Boolean = productId = 0

                    If productCreated Then
                        productId = Await InsertProductAsync(connection, transaction, sku, name, price, cost, cancellationToken).ConfigureAwait(False)
                        Await InsertZeroBalanceAsync(connection, transaction, productId, cancellationToken).ConfigureAwait(False)
                    End If

                    Dim quantityBefore As Decimal =
                        Await ReadBalanceAsync(connection, transaction, productId, cancellationToken).ConfigureAwait(False)

                    Dim openingApplied As Boolean = False
                    Dim quantityAfter As Decimal = quantityBefore

                    ' Only an untouched product gets an opening balance. Topping
                    ' up a product that already has stock would be an inventory
                    ' adjustment, which is a Phase 2 feature with its own
                    ' authorization rule - not something a seed script decides.
                    If quantityBefore = 0D AndAlso openingQuantity > 0D Then

                        quantityAfter = quantityBefore + openingQuantity

                        Dim affected As Integer =
                            Await ApplyOpeningBalanceAsync(connection, transaction, productId, quantityAfter, cancellationToken).ConfigureAwait(False)

                        If affected <> 1 Then
                            Throw New SeedDemoCommandException(
                                $"Opening balance affected {affected} rows, expected exactly 1. Nothing was committed.")
                        End If

                        Await StockMovementWriter.WriteAsync(connection, transaction, productId,
                                                             openingQuantity, quantityBefore, quantityAfter,
                                                             OpeningReason, actorUserId, correlationId,
                                                             cancellationToken).ConfigureAwait(False)

                        Await AuditLogWriter.WriteAsync(connection, actorUserId, "SeedDemo",
                                                        $"Product:{productId}", "Success", correlationId,
                                                        String.Format(CultureInfo.InvariantCulture,
                                                                      "Opening balance {0} for SKU {1}.",
                                                                      openingQuantity, sku),
                                                        cancellationToken, transaction).ConfigureAwait(False)

                        openingApplied = True

                    End If

                    Await transaction.CommitAsync(cancellationToken).ConfigureAwait(False)
                    committed = True

                    result = New SeedDemoResult(productId, productCreated, openingApplied, quantityAfter, actorUserId)

                Catch ex As Exception
                    failure = ex
                End Try

                If Not committed Then

                    Try
                        Await transaction.RollbackAsync(cancellationToken).ConfigureAwait(False)
                    Catch rollbackFailure As MySqlException
                        ' A statement that never took effect leaves nothing to
                        ' roll back. The original failure below is what gets
                        ' reported - this is not a swallowed error.
                    End Try

                End If

                Await transaction.DisposeAsync().ConfigureAwait(False)

                If failure IsNot Nothing Then
                    ExceptionDispatchInfo.Capture(failure).Throw()
                End If

                Return result

            End Using

        End Function

        ''' <summary>The actor recorded on the opening movement.</summary>
        Private Shared Async Function FindUserIdAsync(connection As MySqlConnection,
                                                      username As String,
                                                      cancellationToken As CancellationToken) As Task(Of Integer)

            If String.IsNullOrWhiteSpace(username) Then
                Throw New SeedDemoCommandException("An actor username is required - every movement names who caused it.")
            End If

            Using command As MySqlCommand = connection.CreateCommand()

                command.CommandText = "SELECT Id FROM Users WHERE Username = @username;"
                command.Parameters.AddWithValue("@username", username)

                Dim found As Object = Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False)

                If found Is Nothing OrElse found Is DBNull.Value Then
                    Throw New SeedDemoCommandException(
                        $"No user named '{username}'. Run create-user first - the opening movement has to name an actor.")
                End If

                Return Convert.ToInt32(found, CultureInfo.InvariantCulture)

            End Using

        End Function

        ''' <summary>Product Id for a SKU, or 0 when it does not exist yet.</summary>
        Private Shared Async Function FindProductIdAsync(connection As MySqlConnection,
                                                         transaction As MySqlTransaction,
                                                         sku As String,
                                                         cancellationToken As CancellationToken) As Task(Of Integer)

            Using command As MySqlCommand = connection.CreateCommand()

                command.Transaction = transaction
                command.CommandText = "SELECT Id FROM Products WHERE Sku = @sku;"
                command.Parameters.AddWithValue("@sku", sku)

                Dim found As Object = Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False)

                If found Is Nothing OrElse found Is DBNull.Value Then
                    Return 0
                End If

                Return Convert.ToInt32(found, CultureInfo.InvariantCulture)

            End Using

        End Function

        Private Shared Async Function InsertProductAsync(connection As MySqlConnection,
                                                         transaction As MySqlTransaction,
                                                         sku As String,
                                                         name As String,
                                                         price As Decimal,
                                                         cost As Decimal,
                                                         cancellationToken As CancellationToken) As Task(Of Integer)

            Using command As MySqlCommand = connection.CreateCommand()

                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO Products (Sku, Name, Price, Cost, IsActive, CreatedAtUtc, UpdatedAtUtc) " &
                    "VALUES (@sku, @name, @price, @cost, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@sku", sku)
                command.Parameters.AddWithValue("@name", name)
                command.Parameters.AddWithValue("@price", price)
                command.Parameters.AddWithValue("@cost", cost)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)

                Return CInt(command.LastInsertedId)

            End Using

        End Function

        Private Shared Async Function InsertZeroBalanceAsync(connection As MySqlConnection,
                                                             transaction As MySqlTransaction,
                                                             productId As Integer,
                                                             cancellationToken As CancellationToken) As Task

            Using command As MySqlCommand = connection.CreateCommand()

                command.Transaction = transaction
                command.CommandText =
                    "INSERT INTO StockBalances (ProductId, Quantity, RowVersion, UpdatedAtUtc) " &
                    "VALUES (@productId, 0.000, 0, UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@productId", productId)

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)

            End Using

        End Function

        Private Shared Async Function ReadBalanceAsync(connection As MySqlConnection,
                                                       transaction As MySqlTransaction,
                                                       productId As Integer,
                                                       cancellationToken As CancellationToken) As Task(Of Decimal)

            Using command As MySqlCommand = connection.CreateCommand()

                command.Transaction = transaction
                command.CommandText = "SELECT Quantity FROM StockBalances WHERE ProductId = @productId;"
                command.Parameters.AddWithValue("@productId", productId)

                Dim found As Object = Await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(False)

                If found Is Nothing OrElse found Is DBNull.Value Then
                    Throw New SeedDemoCommandException(
                        $"Product {productId} has no StockBalances row. The database is in a state this command will not repair.")
                End If

                Return Convert.ToDecimal(found, CultureInfo.InvariantCulture)

            End Using

        End Function

        ''' <summary>
        ''' The conditional update. Guarded on the balance still being zero, for
        ''' the same reason every stock change in this system is conditional: a
        ''' read-then-write would be a race even here.
        ''' </summary>
        Private Shared Async Function ApplyOpeningBalanceAsync(connection As MySqlConnection,
                                                               transaction As MySqlTransaction,
                                                               productId As Integer,
                                                               quantityAfter As Decimal,
                                                               cancellationToken As CancellationToken) As Task(Of Integer)

            Using command As MySqlCommand = connection.CreateCommand()

                command.Transaction = transaction
                command.CommandText =
                    "UPDATE StockBalances " &
                    "   SET Quantity = @quantityAfter, " &
                    "       RowVersion = RowVersion + 1, " &
                    "       UpdatedAtUtc = UTC_TIMESTAMP(6) " &
                    " WHERE ProductId = @productId " &
                    "   AND Quantity = 0.000;"
                command.Parameters.AddWithValue("@quantityAfter", quantityAfter)
                command.Parameters.AddWithValue("@productId", productId)

                Return Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)

            End Using

        End Function

    End Class

End Namespace
