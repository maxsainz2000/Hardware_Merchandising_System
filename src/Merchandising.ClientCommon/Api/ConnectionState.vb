' Merchandising.ClientCommon.Api.ConnectionState
'
' The client's view of whether the API is reachable. Gap G-26 exists because
' "offline behaviour was not sufficiently explicit", so this is a first-class
' type rather than a Boolean hiding inside a view model.
'
' The distinction that matters, and the one G-26 was really about: a server
' that REFUSES a request is Online. Only a failure to reach the server at all
' is Unavailable. Collapsing the two would let a 409 InsufficientStock render
' as "you are offline", which is both wrong and the exact class of lie that
' makes an operator retry a write that already committed.

Namespace Api

    ''' <summary>Whether the API is currently reachable from this client.</summary>
    Public Enum ConnectionState

        ''' <summary>No call has been attempted yet. Never shown as "online".</summary>
        Unknown = 0

        ''' <summary>The API answered. It may have answered with an error.</summary>
        Online = 1

        ''' <summary>
        ''' The API could not be reached: DNS, TCP, TLS trust, or timeout.
        ''' Writes are refused outright in this state - never queued.
        ''' </summary>
        Unavailable = 2

    End Enum

End Namespace
