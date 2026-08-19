' Merchandising.Infrastructure.Data.AuditLogWriter
'
' Single insertion point for AuditLogs (CLAUDE.md section 5: append-only,
' enforced by grant - merch_api holds INSERT only, never UPDATE or DELETE,
' on this table). First real caller is P1-08's login/lockout/logout
' handling; later tasks reuse this rather than writing their own INSERT.

Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Data

    Public NotInheritable Class AuditLogWriter

        ''' <summary>
        ''' Writes one audit record. <paramref name="detail"/> is
        ''' free-text context for a human reviewing the log - it must never
        ''' contain a password, token, or other secret. Callers are
        ''' responsible for that; this method does not attempt to redact
        ''' anything, because a redaction filter here would be the second
        ''' place a secret could leak from, not the first place it is kept
        ''' out.
        ''' </summary>
        ''' <param name="transaction">
        ''' Optional, added at P1-11. Pass the caller's transaction so this
        ''' insert commits or rolls back atomically with the rest of a
        ''' multi-table command (ADR-006) - for example StockService, which
        ''' writes a StockMovements row and this audit row in the same
        ''' transaction as the balance update. Nothing (the default) keeps
        ''' every P1-08 call site unchanged: the command runs outside any
        ''' explicit transaction, exactly as before this parameter existed.
        ''' Appended last, after the existing Optional parameters, so no
        ''' positional call site written before P1-11 shifts arguments.
        ''' </param>
        Public Shared Async Function WriteAsync(
            connection As MySqlConnection,
            actorUserId As Integer?,
            action As String,
            target As String,
            result As String,
            correlationId As String,
            Optional detail As String = Nothing,
            Optional cancellationToken As CancellationToken = Nothing,
            Optional transaction As MySqlTransaction = Nothing) As Task

            Using command As MySqlCommand = connection.CreateCommand()
                If transaction IsNot Nothing Then
                    command.Transaction = transaction
                End If
                command.CommandText =
                    "INSERT INTO AuditLogs (ActorUserId, Action, Target, Result, CorrelationId, Detail, CreatedAtUtc) " &
                    "VALUES (@actorUserId, @action, @target, @result, @correlationId, @detail, UTC_TIMESTAMP(6));"
                command.Parameters.AddWithValue("@actorUserId", If(CObj(actorUserId), DBNull.Value))
                command.Parameters.AddWithValue("@action", action)
                command.Parameters.AddWithValue("@target", target)
                command.Parameters.AddWithValue("@result", result)
                command.Parameters.AddWithValue("@correlationId", correlationId)
                command.Parameters.AddWithValue("@detail", If(CObj(detail), DBNull.Value))

                Await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(False)
            End Using

        End Function

    End Class

End Namespace
