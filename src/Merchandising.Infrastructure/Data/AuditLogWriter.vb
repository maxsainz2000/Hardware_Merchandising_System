' Merchandising.Infrastructure.Data.AuditLogWriter
'
' Single insertion point for AuditLogs (CLAUDE.md section 5: append-only,
' enforced by grant - merch_api holds INSERT only, never UPDATE or DELETE,
' on this table). First real caller is P1-08's login/lockout/logout
' handling; later tasks reuse this rather than writing their own INSERT.
'
' P2-04: WriteAsync also flags Declared, so the write and the "an audit
' intent was declared" signal Merchandising.Api.Middleware.AuditPipelineFilter
' checks are the SAME call - there is no second step for a caller to
' remember.
'
' WHY A SHARED MUTABLE OBJECT BEHIND AsyncLocal, NOT AsyncLocal(Of Boolean)
' DIRECTLY - this was tried first and measurably failed
' (AuditPipelineTests.AuditRequired_ActionDeclaresAudit_ResultPassesThroughUnmodified
' caught it immediately: a real Login went 500 AUDIT_NOT_RECORDED even though
' AuthService plainly calls WriteAsync on its success path). AsyncLocal.Value
' only flows DOWNWARD, parent to descendant - a descendant assigning a NEW
' value to the AsyncLocal does not propagate back to the ancestor after the
' ancestor's Await resumes; each Await boundary restores the ExecutionContext
' the ancestor already had. Storing a reference to one mutable
' DeclarationState instance sidesteps this entirely: BeginDeclarationScope
' creates the instance ONCE and AsyncLocal carries that same reference down
' into every descendant (that direction is reliable and is what every other
' ambient-context pattern - ILogger.BeginScope, Activity.Current - actually
' depends on). WriteAsync, however deep it is called from, mutates a field ON
' that shared instance rather than reassigning the AsyncLocal itself, so the
' ancestor - still holding the same reference - observes the mutation after
' Await with no propagation required at all; it is ordinary heap mutation,
' not ambient-context magic.
'
' Does not leak between concurrent requests: each request begins a new async
' flow from AuditPipelineFilter's own BeginDeclarationScope call, which
' creates its own DeclarationState instance no other request ever sees.

Imports System.Threading
Imports System.Threading.Tasks
Imports MySqlConnector

Namespace Data

    Public NotInheritable Class AuditLogWriter

        Private NotInheritable Class DeclarationState
            Public Property Declared As Boolean
        End Class

        Private Shared ReadOnly _scope As New AsyncLocal(Of DeclarationState)

        ''' <summary>
        ''' True if <see cref="WriteAsync"/> has completed at least once
        ''' during the current async flow since the nearest
        ''' <see cref="BeginDeclarationScope"/>.
        ''' </summary>
        Public Shared ReadOnly Property Declared As Boolean
            Get
                Dim state As DeclarationState = _scope.Value
                Return state IsNot Nothing AndAlso state.Declared
            End Get
        End Property

        ''' <summary>
        ''' Starts a fresh declaration scope - call once per HTTP request,
        ''' before invoking the action, then read <see cref="Declared"/>
        ''' after it completes. The returned <see cref="IDisposable"/> has
        ''' nothing to release; it exists only so callers can use a
        ''' <c>Using</c> block to mark the scope's extent.
        ''' </summary>
        Public Shared Function BeginDeclarationScope() As IDisposable

            _scope.Value = New DeclarationState()
            Return New NoOpScope()

        End Function

        Private NotInheritable Class NoOpScope
            Implements IDisposable

            Public Sub Dispose() Implements IDisposable.Dispose
            End Sub

        End Class

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

            Dim state As DeclarationState = _scope.Value
            If state IsNot Nothing Then
                state.Declared = True
            End If

        End Function

    End Class

End Namespace
