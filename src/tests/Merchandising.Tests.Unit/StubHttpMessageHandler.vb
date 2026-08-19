' Merchandising.Tests.Unit.StubHttpMessageHandler
'
' A transport the tests can hold still. P1-15's acceptance criteria are about
' what the client does when the API is UNREACHABLE, and "unplug the network
' now" is not something a live server can be asked to do reliably or
' repeatably. Throwing the exact exception Windows raises for a refused
' connection is both faster and more precise.
'
' This substitutes the transport only. Every line of MerchandisingApiClient
' under test is the real one - the serialization, the headers, the status
' classification, and the token handling are not stubbed.

Imports System.Collections.Generic
Imports System.Net.Http
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>An HttpMessageHandler that answers from a caller-supplied script.</summary>
Friend NotInheritable Class StubHttpMessageHandler
    Inherits HttpMessageHandler

    Private ReadOnly _responder As Func(Of HttpRequestMessage, HttpResponseMessage)
    Private ReadOnly _requests As New List(Of RecordedRequest)()
    Private ReadOnly _gate As New Object()

    ''' <summary>
    ''' Builds a handler. The responder may return a response or throw, which is
    ''' how an unreachable server is expressed.
    ''' </summary>
    Public Sub New(responder As Func(Of HttpRequestMessage, HttpResponseMessage))

        If responder Is Nothing Then
            Throw New ArgumentNullException(NameOf(responder))
        End If

        _responder = responder

    End Sub

    ''' <summary>
    ''' Everything that actually went onto the wire, in order. A request that
    ''' the client refused locally never appears here - which is exactly how
    ''' "the write was not sent" is asserted.
    ''' </summary>
    Public ReadOnly Property Requests As IReadOnlyList(Of RecordedRequest)
        Get
            SyncLock _gate
                Return _requests.ToArray()
            End SyncLock
        End Get
    End Property

    Protected Overrides Async Function SendAsync(request As HttpRequestMessage,
                                                 cancellationToken As CancellationToken) As Task(Of HttpResponseMessage)

        Dim body As String = String.Empty

        If request.Content IsNot Nothing Then
            body = Await request.Content.ReadAsStringAsync(cancellationToken)
        End If

        Dim correlationId As String = String.Empty
        Dim values As IEnumerable(Of String) = Nothing
        If request.Headers.TryGetValues("X-Correlation-Id", values) Then
            correlationId = String.Join(",", values)
        End If

        Dim authorization As String = String.Empty
        If request.Headers.Authorization IsNot Nothing Then
            authorization = request.Headers.Authorization.ToString()
        End If

        ' Recorded BEFORE the responder runs, so an attempt that ends in a
        ' transport failure is still counted as having left the client.
        SyncLock _gate
            _requests.Add(New RecordedRequest(request.Method.Method,
                                              request.RequestUri.AbsolutePath,
                                              correlationId,
                                              authorization,
                                              body))
        End SyncLock

        Return _responder(request)

    End Function

    ''' <summary>One request as it left the client.</summary>
    Friend NotInheritable Class RecordedRequest

        Public Sub New(method As String, path As String, correlationId As String, authorization As String, body As String)

            Me.Method = method
            Me.Path = path
            Me.CorrelationId = correlationId
            Me.Authorization = authorization
            Me.Body = body

        End Sub

        Public ReadOnly Property Method As String
        Public ReadOnly Property Path As String
        Public ReadOnly Property CorrelationId As String
        Public ReadOnly Property Authorization As String
        Public ReadOnly Property Body As String

    End Class

End Class
