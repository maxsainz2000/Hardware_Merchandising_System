' Merchandising.Api.Controllers.SystemSettingsController
'
' P2-05: spec section 12 ("The system stores a configurable currency code
' and uses one defined rounding policy") and section 17 ("Configuration
' changes audited"). Promotes SystemSettings from an internally-read table
' (P1-17's backup retention, P1-18's maintenance banner) to a real
' administered surface for the two settings section 12 actually names -
' see SystemSettingRegistry's own header for why this is not widened to
' the pre-existing backup./maintenance. keys.

Imports System.Collections.Generic
Imports System.Data
Imports System.Security.Claims
Imports System.Threading.Tasks
Imports Merchandising.Api.Middleware
Imports Merchandising.Api.Security
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Settings
Imports Merchandising.Domain.Configuration
Imports Merchandising.Domain.Security
Imports Merchandising.Infrastructure.Data
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Mvc
Imports MySqlConnector

Namespace Controllers

    <ApiController>
    <Route("api/v1/admin/settings")>
    Public Class SystemSettingsController
        Inherits ControllerBase

        Private ReadOnly _connectionFactory As ConnectionFactory

        Public Sub New(connectionFactory As ConnectionFactory)
            _connectionFactory = connectionFactory
        End Sub

        ''' <summary>
        ''' Every registered setting, current value if one has been written
        ''' or its registry default otherwise. Any authenticated caller -
        ''' currency code and rounding policy are system-wide operational
        ''' facts a client legitimately needs to display (spec section 12),
        ''' not a secret gated behind an administrative policy the way
        ''' writing them is.
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName)>
        <HttpGet>
        Public Async Function GetSettings() As Task(Of IActionResult)

            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync(HttpContext.RequestAborted)

                Dim stored As Dictionary(Of String, String) =
                    Await SystemSettingsRepository.LoadByPrefixAsync(connection, "currency.", HttpContext.RequestAborted)

                Dim response As New List(Of SystemSettingResponse)

                For Each definition As SystemSettingDefinition In SystemSettingRegistry.Definitions

                    Dim value As String = Nothing
                    If Not stored.TryGetValue(definition.Key, value) Then
                        value = definition.DefaultValue
                    End If

                    response.Add(New SystemSettingResponse With {.Key = definition.Key, .Value = value})

                Next

                Return Ok(response)

            End Using

        End Function

        ''' <summary>
        ''' Writes one setting. Rejects an unregistered key or a value that
        ''' fails its definition's validator with a stable error code and
        ''' field-level detail (ADR-014) rather than storing it. Every
        ''' successful change writes an audit row carrying both the
        ''' previous and new value, in the same transaction as the value
        ''' change itself, through P2-04's audit pipeline.
        ''' </summary>
        <Authorize(AuthenticationSchemes:=SessionAuthenticationHandler.SchemeName, Policy:=PolicyRegistry.Names.ConfigurationManage)>
        <AuditRequired>
        <HttpPut("{key}")>
        Public Async Function UpdateSetting(key As String, <FromBody> request As UpdateSystemSettingRequest) As Task(Of IActionResult)

            Dim correlationId As String = HttpContext.GetCorrelationId()

            Dim definition As SystemSettingDefinition = SystemSettingRegistry.Find(key)
            If definition Is Nothing Then
                Return BadRequest(New ApiErrorResponse With {
                    .ErrorCode = "UNKNOWN_SETTING_KEY",
                    .Message = $"'{key}' is not a recognized setting.",
                    .CorrelationId = correlationId
                })
            End If

            Dim newValue As String = If(request?.Value, String.Empty)
            Dim validationError As String = definition.Validate(newValue)

            If validationError IsNot Nothing Then
                Return BadRequest(New ApiErrorResponse With {
                    .ErrorCode = "VALIDATION_FAILED",
                    .Message = "The setting value failed validation.",
                    .CorrelationId = correlationId,
                    .Errors = New Dictionary(Of String, String()) From {{"value", New String() {validationError}}}
                })
            End If

            Dim actorUserId As Integer = Integer.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier))

            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync(HttpContext.RequestAborted)

                ' P4-04/CARRY-03/ADR-006 amendment: the session-level
                ' READ-COMMITTED setting does not survive BeginTransaction -
                ' it must be passed here explicitly (measured at P3-03).
                ' Found by this card's own source scan - not in the card's
                ' original enumerated list, which predates this controller's
                ' addition of a transaction here.
                Dim transaction As MySqlTransaction =
                    Await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, HttpContext.RequestAborted)

                Dim previousValue As String =
                    Await SystemSettingsRepository.ReadForUpdateAsync(
                        connection, transaction, key, HttpContext.RequestAborted)

                Await SystemSettingsRepository.UpsertAsync(
                    connection, transaction, key, newValue, actorUserId, HttpContext.RequestAborted)

                ' A key that has never been written has no row, so its
                ' "previous" value - for the audit trail's purposes - is
                ' whatever the system was actually using: the registry
                ' default, not an absent value nobody would recognize.
                Dim effectivePreviousValue As String = If(previousValue, definition.DefaultValue)

                Await AuditLogWriter.WriteAsync(
                    connection, actorUserId, "SystemSettingChanged", key, "Success", correlationId,
                    detail:=$"'{effectivePreviousValue}' -> '{newValue}'",
                    cancellationToken:=HttpContext.RequestAborted,
                    transaction:=transaction)

                Await transaction.CommitAsync(HttpContext.RequestAborted)
                Await transaction.DisposeAsync()

                Return Ok(New SystemSettingResponse With {.Key = key, .Value = newValue})

            End Using

        End Function

    End Class

End Namespace
