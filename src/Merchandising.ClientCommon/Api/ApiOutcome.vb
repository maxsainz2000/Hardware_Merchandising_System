' Merchandising.ClientCommon.Api.ApiOutcome
'
' How an API call ended, from the client's point of view. Three outcomes, not
' two: "the server said no" and "I could not ask the server" are different
' events and the UI must be able to tell them apart (gap G-26).

Namespace Api

    ''' <summary>The three ways a call to the API can end.</summary>
    Public Enum ApiOutcome

        ''' <summary>2xx. A payload is present.</summary>
        Success = 0

        ''' <summary>
        ''' The API answered with an error status. The server is healthy and
        ''' reachable; it declined this request. An <see cref="Merchandising.Contracts.Errors.ApiErrorResponse"/>
        ''' is present, carrying the correlation ID to quote in a bug report.
        ''' </summary>
        Rejected = 1

        ''' <summary>
        ''' The API was not reached at all. No payload, no error envelope, and
        ''' - critically - no way to know whether any work happened server-side.
        ''' A write in this state is reported as failed and is NOT retried
        ''' automatically or held for later.
        ''' </summary>
        Unavailable = 2

    End Enum

End Namespace
