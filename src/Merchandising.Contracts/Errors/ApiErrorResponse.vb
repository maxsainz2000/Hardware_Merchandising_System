' Merchandising.Contracts.Errors.ApiErrorResponse
'
' Minimal error envelope satisfying CLAUDE.md section 5: every error
' response carries a stable error code, a human-readable message, a
' correlation Id, and field-level validation detail where applicable -
' never a stack trace, SQL text, connection string, or environment detail.
'
' First real callers are P1-08's 400/401/403/423 auth responses. A future
' task may generalise this into a global exception-handling middleware for
' every controller; that is not built here; scope is the responses this
' card itself produces.

Imports System.Text.Json.Serialization

Namespace Errors

    Public NotInheritable Class ApiErrorResponse

        <JsonPropertyName("errorCode")>
        Public Property ErrorCode As String = String.Empty

        <JsonPropertyName("message")>
        Public Property Message As String = String.Empty

        <JsonPropertyName("correlationId")>
        Public Property CorrelationId As String = String.Empty

        ''' <summary>Field name to validation message(s). Nothing when the error is not a validation failure.</summary>
        <JsonPropertyName("errors")>
        Public Property Errors As IReadOnlyDictionary(Of String, String()) = Nothing

    End Class

End Namespace
