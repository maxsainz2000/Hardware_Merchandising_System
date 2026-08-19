' Merchandising.ClientCommon.Api.ApiResult
'
' The single return type of every call on MerchandisingApiClient. It is a
' result object rather than "return the payload, throw on failure" on purpose:
' an unreachable API is an ordinary, expected condition in this system (the
' clients are online-only by design, spec section 8), and expected conditions
' should not arrive as exceptions that a caller can forget to catch.
'
' Option Strict On makes the generic parameter carry its weight - a caller
' cannot read Value without having matched on Outcome first, because there is
' nothing to late-bind against.

Imports Merchandising.Contracts.Errors

Namespace Api

    ''' <summary>
    ''' The outcome of one API call: a payload, a server-side rejection, or an
    ''' unreachable server.
    ''' </summary>
    ''' <typeparam name="T">The response contract this call returns on success.</typeparam>
    Public NotInheritable Class ApiResult(Of T As Class)

        Private Sub New(outcome As ApiOutcome,
                        value As T,
                        [error] As ApiErrorResponse,
                        correlationId As String,
                        transportDetail As String)

            Me.Outcome = outcome
            Me.Value = value
            Me.[Error] = [error]
            Me.CorrelationId = If(correlationId, String.Empty)
            Me.TransportDetail = If(transportDetail, String.Empty)

        End Sub

        ''' <summary>How the call ended.</summary>
        Public ReadOnly Property Outcome As ApiOutcome

        ''' <summary>The deserialized payload. Nothing unless <see cref="IsSuccess"/>.</summary>
        Public ReadOnly Property Value As T

        ''' <summary>The API's error envelope (ADR-014). Nothing unless the outcome is Rejected.</summary>
        Public ReadOnly Property [Error] As ApiErrorResponse

        ''' <summary>
        ''' The correlation ID for this call. Present on every outcome the server
        ''' took part in, so an operator can quote it and have the matching
        ''' AuditLogs row found. Empty only when the server was never reached.
        ''' </summary>
        Public ReadOnly Property CorrelationId As String

        ''' <summary>
        ''' Why the server could not be reached, in words safe to show a user
        ''' ("the API is not responding", "the certificate is not trusted").
        ''' Empty unless the outcome is Unavailable.
        ''' </summary>
        Public ReadOnly Property TransportDetail As String

        ''' <summary>True only for a 2xx with a payload.</summary>
        Public ReadOnly Property IsSuccess As Boolean
            Get
                Return Outcome = ApiOutcome.Success
            End Get
        End Property

        ''' <summary>Builds a successful result.</summary>
        Public Shared Function FromSuccess(value As T, correlationId As String) As ApiResult(Of T)

            If value Is Nothing Then
                Throw New ArgumentNullException(NameOf(value))
            End If

            Return New ApiResult(Of T)(ApiOutcome.Success, value, Nothing, correlationId, Nothing)

        End Function

        ''' <summary>Builds a result for an error status the API returned.</summary>
        Public Shared Function FromRejection([error] As ApiErrorResponse, correlationId As String) As ApiResult(Of T)

            Return New ApiResult(Of T)(ApiOutcome.Rejected, Nothing, [error], correlationId, Nothing)

        End Function

        ''' <summary>Builds a result for a server that could not be reached.</summary>
        Public Shared Function FromUnavailable(transportDetail As String) As ApiResult(Of T)

            Return New ApiResult(Of T)(ApiOutcome.Unavailable, Nothing, Nothing, Nothing, transportDetail)

        End Function

    End Class

End Namespace
