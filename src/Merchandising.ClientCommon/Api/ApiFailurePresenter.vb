' Merchandising.ClientCommon.Api.ApiFailurePresenter
'
' Turns a failed ApiResult(Of T) into the three lines of text every screen in
' every client shows the same way. Factored out here because P3-07 gives one
' client five view models that each need this (login, supplier browse, new
' order, order tracking, history), and SpikeViewModel's own ShowFailure -
' written for one screen at P1-15 - is exactly the logic that would otherwise
' be copied five times with five chances to drift.
'
' The three outcomes stay visually distinct, on purpose (gap G-26, CLAUDE.md
' section 5's "errors never leak internals" and "the client never invents
' its own wording"):
'
'   Unavailable  - the server was never reached. No correlation Id exists to
'                  show, because no request was ever logged server-side.
'   Maintenance  - a 503 MAINTENANCE_MODE rejection. Same Rejected outcome as
'                  any other refusal, but P1-18 gives it its own banner.
'   Rejected     - the API's own ErrorCode and Message, verbatim. This
'                  presenter never rewrites, summarises, or drops either one.

Imports Merchandising.Contracts.Errors

Namespace Api

    ''' <summary>What a screen needs to render one failed call.</summary>
    Public NotInheritable Class ApiFailureDisplay

        Public Property StatusMessage As String = String.Empty
        Public Property Detail As String = String.Empty
        Public Property CorrelationId As String = String.Empty
        Public Property IsMaintenance As Boolean
        Public Property MaintenanceMessage As String = String.Empty

    End Class

    ''' <summary>Builds an <see cref="ApiFailureDisplay"/> from a failed <see cref="ApiResult(Of T)"/>.</summary>
    Public NotInheritable Class ApiFailurePresenter

        ''' <summary>P1-18's error code for a write refused while the system is under maintenance.</summary>
        Public Const MaintenanceErrorCode As String = "MAINTENANCE_MODE"

        Private Sub New()
        End Sub

        ''' <summary>Describes any non-success outcome. Do not call this for a successful result.</summary>
        Public Shared Function Describe(Of T As Class)(result As ApiResult(Of T)) As ApiFailureDisplay

            If result Is Nothing Then
                Throw New ArgumentNullException(NameOf(result))
            End If

            If result.Outcome = ApiOutcome.Unavailable Then

                Return New ApiFailureDisplay With {
                    .StatusMessage = "The API could not be reached. Nothing was sent and nothing was saved.",
                    .Detail = result.TransportDetail & Environment.NewLine & Environment.NewLine &
                              "This client is online-only by design. The request has NOT been queued " &
                              "and will NOT be retried automatically - re-run it once the API is back.",
                    .CorrelationId = String.Empty
                }

            End If

            Dim envelope As ApiErrorResponse = result.[Error]

            If envelope IsNot Nothing AndAlso
               String.Equals(envelope.ErrorCode, MaintenanceErrorCode, StringComparison.Ordinal) Then

                Return New ApiFailureDisplay With {
                    .StatusMessage = "The system is under maintenance. Your change was not saved.",
                    .Detail = envelope.Message & Environment.NewLine & Environment.NewLine &
                              "Nothing was written. Wait for maintenance to finish and try again - " &
                              "this client does not queue or retry.",
                    .CorrelationId = result.CorrelationId,
                    .IsMaintenance = True,
                    .MaintenanceMessage = envelope.Message
                }

            End If

            Return New ApiFailureDisplay With {
                .StatusMessage = If(envelope IsNot Nothing, envelope.ErrorCode, "UNKNOWN_ERROR"),
                .Detail = If(envelope IsNot Nothing, envelope.Message, "The API refused the request."),
                .CorrelationId = result.CorrelationId
            }

        End Function

    End Class

End Namespace
