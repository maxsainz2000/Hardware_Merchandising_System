' Merchandising.ClientCommon.Api.MerchandisingApiClient
'
' The one seam between a WPF client and the API. Every call a client makes
' goes through here, so the rules that keep a client trustworthy are stated
' once, in one file:
'
'   * The token lives in memory only (InMemoryTokenStore) and is attached as
'     "Authorization: Bearer <token>" - the Session scheme, ADR-005.
'   * Every request carries a fresh X-Correlation-Id in canonical UUID form.
'     ADR-014 makes the API reject any other shape with a controlled 400, so
'     "D" formatting here is a contract, not a preference.
'   * A request that never reached the server is reported as Unavailable and
'     is NOT retried, NOT queued, and NOT recorded locally as if it had
'     happened. That is gap G-26, and it is the reason this class has no
'     retry policy and no outbox - their absence is the feature.
'   * Certificate validation is left at the platform default. A client that
'     cannot verify MERCH-HOST must fail loudly (P1-09), so there is
'     deliberately no callback here to wave a bad certificate through.
'
' Reflection-based JSON only. System.Text.Json source generation is C#-only
' and unavailable to this project (CLAUDE.md section 3).

Imports System.Collections.Generic
Imports System.Globalization
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Net.Sockets
Imports System.Security.Authentication
Imports System.Text
Imports System.Text.Json
Imports Merchandising.Contracts.Auth
Imports Merchandising.Contracts.Errors
Imports Merchandising.Contracts.Inventory
Imports Merchandising.Contracts.Procurement
Imports Merchandising.Contracts.Products
Imports Merchandising.Contracts.Suppliers

Namespace Api

    ''' <summary>Typed HTTPS client for the Merchandising API.</summary>
    Public NotInheritable Class MerchandisingApiClient
        Implements IDisposable

        ''' <summary>The correlation header both sides agree on (ADR-014).</summary>
        Public Const CorrelationIdHeaderName As String = "X-Correlation-Id"

        ''' <summary>
        ''' How long to wait for the API before calling it unreachable. Short on
        ''' purpose: on a LAN, a server that has not answered in ten seconds is
        ''' not "slow", and an operator staring at a frozen till learns nothing
        ''' from a longer wait.
        ''' </summary>
        Public Shared ReadOnly DefaultTimeout As TimeSpan = TimeSpan.FromSeconds(10)

        Private ReadOnly _http As HttpClient
        Private ReadOnly _tokens As New InMemoryTokenStore()
        Private ReadOnly _json As JsonSerializerOptions
        Private _state As ConnectionState = ConnectionState.Unknown
        Private _disposed As Boolean

        ''' <summary>Raised whenever <see cref="State"/> changes value.</summary>
        Public Event ConnectionStateChanged As EventHandler

        ''' <summary>Builds a client against the live API.</summary>
        ''' <param name="baseAddress">Root address, for example https://MERCH-HOST:8443/ .</param>
        Public Sub New(baseAddress As Uri)
            Me.New(baseAddress, Nothing, DefaultTimeout)
        End Sub

        ''' <summary>
        ''' Builds a client over a caller-supplied message handler. This is the
        ''' seam the client-behaviour tests use to hold the transport still -
        ''' refusing a connection on demand is not something a live server can
        ''' be asked to do reliably.
        ''' </summary>
        Public Sub New(baseAddress As Uri, handler As HttpMessageHandler, timeout As TimeSpan)

            If baseAddress Is Nothing Then
                Throw New ArgumentNullException(NameOf(baseAddress))
            End If

            _http = If(handler Is Nothing, New HttpClient(), New HttpClient(handler, disposeHandler:=False))
            _http.BaseAddress = baseAddress
            _http.Timeout = timeout
            _http.DefaultRequestHeaders.Accept.Add(New MediaTypeWithQualityHeaderValue("application/json"))

            _json = New JsonSerializerOptions With {
                .PropertyNameCaseInsensitive = True
            }

        End Sub

        ''' <summary>Current reachability of the API, as last observed.</summary>
        Public ReadOnly Property State As ConnectionState
            Get
                Return _state
            End Get
        End Property

        ''' <summary>True once a login has succeeded and the token is held.</summary>
        Public ReadOnly Property IsAuthenticated As Boolean
            Get
                Return _tokens.HasToken
            End Get
        End Property

        ''' <summary>
        ''' The API root this client talks to. Shown in the UI so the operator
        ''' can see which host a failure message refers to.
        ''' </summary>
        Public ReadOnly Property BaseAddress As Uri
            Get
                Return _http.BaseAddress
            End Get
        End Property

        ''' <summary>
        ''' Exchanges credentials for a session token, held in memory on success.
        ''' </summary>
        Public Async Function LoginAsync(username As String, password As String) As Task(Of ApiResult(Of LoginResponse))

            Dim request As New LoginRequest With {
                .Username = If(username, String.Empty),
                .Password = If(password, String.Empty)
            }

            Dim raw As RawResponse =
                Await SendCoreAsync(HttpMethod.Post, "api/v1/auth/login", request, requiresAuthentication:=False)

            Dim result As ApiResult(Of LoginResponse) = Materialize(Of LoginResponse)(raw)

            If result.IsSuccess Then
                _tokens.Store(result.Value.Token)
            End If

            Return result

        End Function

        ''' <summary>Calls the protected identity endpoint - the round-trip proof.</summary>
        Public Async Function GetCurrentUserAsync() As Task(Of ApiResult(Of MeResponse))

            Dim raw As RawResponse =
                Await SendCoreAsync(HttpMethod.Get, "api/v1/auth/me", Nothing, requiresAuthentication:=True)

            Return Materialize(Of MeResponse)(raw)

        End Function

        ''' <summary>Free-text supplier search, paginated - GET /api/v1/suppliers (P3-07 supplier browse).</summary>
        Public Async Function SearchSuppliersAsync(q As String,
                                                    page As Integer,
                                                    pageSize As Integer,
                                                    includeInactive As Boolean) As Task(Of ApiResult(Of SupplierSearchResponse))

            Dim query As New Dictionary(Of String, String) From {
                {"page", page.ToString(CultureInfo.InvariantCulture)},
                {"pageSize", pageSize.ToString(CultureInfo.InvariantCulture)},
                {"includeInactive", includeInactive.ToString(CultureInfo.InvariantCulture)}
            }

            If Not String.IsNullOrWhiteSpace(q) Then
                query("q") = q
            End If

            Dim raw As RawResponse =
                Await SendCoreAsync(HttpMethod.Get, BuildPath("api/v1/suppliers", query), Nothing, requiresAuthentication:=True)

            Return Materialize(Of SupplierSearchResponse)(raw)

        End Function

        ''' <summary>
        ''' Free-text product search, paginated - GET /api/v1/products. Used by
        ''' the new-order screen to resolve a SKU/name into the ProductId a
        ''' purchase-order line requires; this client never invents a product
        ''' Id of its own.
        ''' </summary>
        Public Async Function SearchProductsAsync(q As String,
                                                   page As Integer,
                                                   pageSize As Integer) As Task(Of ApiResult(Of ProductSearchResponse))

            Dim query As New Dictionary(Of String, String) From {
                {"page", page.ToString(CultureInfo.InvariantCulture)},
                {"pageSize", pageSize.ToString(CultureInfo.InvariantCulture)}
            }

            If Not String.IsNullOrWhiteSpace(q) Then
                query("q") = q
            End If

            Dim raw As RawResponse =
                Await SendCoreAsync(HttpMethod.Get, BuildPath("api/v1/products", query), Nothing, requiresAuthentication:=True)

            Return Materialize(Of ProductSearchResponse)(raw)

        End Function

        ''' <summary>
        ''' Creates a Draft purchase order - POST /api/v1/purchase-orders. Status
        ''' is never sent; the server assigns it (spec section 10.1).
        ''' </summary>
        ''' <param name="idempotencyKey">
        ''' Client-generated per ADR-007. A fresh key per distinct create
        ''' attempt is the caller's responsibility - see NewPurchaseOrderViewModel.
        ''' </param>
        Public Async Function CreatePurchaseOrderAsync(supplierId As Integer,
                                                        lines As IReadOnlyList(Of CreatePurchaseOrderLineRequest),
                                                        idempotencyKey As String) As Task(Of ApiResult(Of PurchaseOrderResponse))

            If String.IsNullOrWhiteSpace(idempotencyKey) Then
                Throw New ArgumentException("An idempotency key is required for every write.", NameOf(idempotencyKey))
            End If

            Dim request As New CreatePurchaseOrderRequest With {
                .SupplierId = supplierId,
                .Lines = lines,
                .IdempotencyKey = idempotencyKey
            }

            Dim raw As RawResponse =
                Await SendCoreAsync(HttpMethod.Post, "api/v1/purchase-orders", request, requiresAuthentication:=True)

            Return Materialize(Of PurchaseOrderResponse)(raw)

        End Function

        ''' <summary>Paginated, filtered, sorted purchase-order headers - GET /api/v1/purchase-orders.</summary>
        Public Async Function SearchPurchaseOrdersAsync(supplierId As Integer?,
                                                        status As String,
                                                        sort As String,
                                                        page As Integer,
                                                        pageSize As Integer) As Task(Of ApiResult(Of PurchaseOrderSearchResponse))

            Dim query As New Dictionary(Of String, String) From {
                {"page", page.ToString(CultureInfo.InvariantCulture)},
                {"pageSize", pageSize.ToString(CultureInfo.InvariantCulture)}
            }

            If supplierId.HasValue Then
                query("supplierId") = supplierId.Value.ToString(CultureInfo.InvariantCulture)
            End If

            If Not String.IsNullOrWhiteSpace(status) Then
                query("status") = status
            End If

            If Not String.IsNullOrWhiteSpace(sort) Then
                query("sort") = sort
            End If

            Dim raw As RawResponse =
                Await SendCoreAsync(HttpMethod.Get, BuildPath("api/v1/purchase-orders", query), Nothing, requiresAuthentication:=True)

            Return Materialize(Of PurchaseOrderSearchResponse)(raw)

        End Function

        ''' <summary>
        ''' The purchase-order history report - GET /api/v1/purchase-orders/history
        ''' (P3-06). fromDate/toDate are store-local yyyy-MM-dd text, or Nothing
        ''' for an unbounded side; the server interprets them against Asia/Manila,
        ''' this client never computes that boundary itself.
        ''' </summary>
        Public Async Function GetPurchaseOrderHistoryAsync(supplierId As Integer?,
                                                           status As String,
                                                           fromDate As String,
                                                           toDate As String,
                                                           sort As String,
                                                           page As Integer,
                                                           pageSize As Integer) As Task(Of ApiResult(Of PurchaseOrderHistoryResponse))

            Dim query As New Dictionary(Of String, String) From {
                {"page", page.ToString(CultureInfo.InvariantCulture)},
                {"pageSize", pageSize.ToString(CultureInfo.InvariantCulture)}
            }

            If supplierId.HasValue Then
                query("supplierId") = supplierId.Value.ToString(CultureInfo.InvariantCulture)
            End If

            If Not String.IsNullOrWhiteSpace(status) Then
                query("status") = status
            End If

            If Not String.IsNullOrWhiteSpace(fromDate) Then
                query("fromDate") = fromDate
            End If

            If Not String.IsNullOrWhiteSpace(toDate) Then
                query("toDate") = toDate
            End If

            If Not String.IsNullOrWhiteSpace(sort) Then
                query("sort") = sort
            End If

            Dim raw As RawResponse =
                Await SendCoreAsync(HttpMethod.Get, BuildPath("api/v1/purchase-orders/history", query), Nothing, requiresAuthentication:=True)

            Return Materialize(Of PurchaseOrderHistoryResponse)(raw)

        End Function

        ''' <summary>One purchase order with every line - GET /api/v1/purchase-orders/{id}.</summary>
        Public Async Function GetPurchaseOrderAsync(id As Integer) As Task(Of ApiResult(Of PurchaseOrderResponse))

            Dim raw As RawResponse =
                Await SendCoreAsync(HttpMethod.Get, $"api/v1/purchase-orders/{id}", Nothing, requiresAuthentication:=True)

            Return Materialize(Of PurchaseOrderResponse)(raw)

        End Function

        ''' <summary>Sends a Draft order for approval - POST /api/v1/purchase-orders/{id}/submit.</summary>
        Public Async Function SubmitPurchaseOrderAsync(id As Integer) As Task(Of ApiResult(Of PurchaseOrderResponse))

            Dim raw As RawResponse =
                Await SendCoreAsync(HttpMethod.Post, $"api/v1/purchase-orders/{id}/submit", Nothing, requiresAuthentication:=True)

            Return Materialize(Of PurchaseOrderResponse)(raw)

        End Function

        ''' <summary>
        ''' Approves a Submitted order - POST /api/v1/purchase-orders/{id}/approve.
        ''' A self-approval refusal comes back as an ordinary 403 Rejected outcome;
        ''' this client never pre-empts the button or guesses the answer client-side
        ''' (CLAUDE.md section 5's "server-side refusals surface as the API's own
        ''' wording" - see PurchaseOrderListViewModel).
        ''' </summary>
        Public Async Function ApprovePurchaseOrderAsync(id As Integer) As Task(Of ApiResult(Of PurchaseOrderResponse))

            Dim raw As RawResponse =
                Await SendCoreAsync(HttpMethod.Post, $"api/v1/purchase-orders/{id}/approve", Nothing, requiresAuthentication:=True)

            Return Materialize(Of PurchaseOrderResponse)(raw)

        End Function

        ''' <summary>Abandons a Draft/Submitted/Approved order - POST /api/v1/purchase-orders/{id}/cancel.</summary>
        Public Async Function CancelPurchaseOrderAsync(id As Integer, reason As String) As Task(Of ApiResult(Of PurchaseOrderResponse))

            Dim request As New CancelPurchaseOrderRequest With {
                .Reason = If(reason, String.Empty)
            }

            Dim raw As RawResponse =
                Await SendCoreAsync(HttpMethod.Post, $"api/v1/purchase-orders/{id}/cancel", request, requiresAuthentication:=True)

            Return Materialize(Of PurchaseOrderResponse)(raw)

        End Function

        ''' <summary>
        ''' Decrements stock. The write path, and therefore the one that carries
        ''' G-26's guarantee: if this returns Unavailable, nothing was queued and
        ''' nothing will be sent later. The caller must show a failure, and the
        ''' operator decides whether to try again.
        ''' </summary>
        ''' <param name="idempotencyKey">
        ''' Client-generated, per CLAUDE.md section 5. Reusing a key for a repeat
        ''' of the same intent is what makes a retry safe; a new key is a new
        ''' movement.
        ''' </param>
        Public Async Function DecrementStockAsync(productId As Integer,
                                                  quantity As Decimal,
                                                  reason As String,
                                                  idempotencyKey As String) As Task(Of ApiResult(Of StockDecrementResponse))

            If String.IsNullOrWhiteSpace(idempotencyKey) Then
                Throw New ArgumentException("An idempotency key is required for every write.", NameOf(idempotencyKey))
            End If

            Dim request As New StockDecrementRequest With {
                .ProductId = productId,
                .Quantity = quantity,
                .Reason = If(reason, String.Empty),
                .IdempotencyKey = idempotencyKey
            }

            Dim raw As RawResponse =
                Await SendCoreAsync(HttpMethod.Post, "api/v1/inventory/stock/decrement", request, requiresAuthentication:=True)

            Return Materialize(Of StockDecrementResponse)(raw)

        End Function

        ''' <summary>
        ''' Ends the session server-side and discards the local token. The token
        ''' is cleared even when the call fails - if the API is unreachable there
        ''' is nothing useful the client can do with a token it cannot present,
        ''' and holding it is strictly worse than dropping it.
        ''' </summary>
        Public Async Function LogoutAsync() As Task(Of ApiResult(Of MeResponse))

            Dim raw As RawResponse =
                Await SendCoreAsync(HttpMethod.Post, "api/v1/auth/logout", Nothing, requiresAuthentication:=True)

            _tokens.Clear()

            If raw.Reached AndAlso raw.StatusCode = HttpStatusCode.NoContent Then
                Return ApiResult(Of MeResponse).FromSuccess(New MeResponse(), raw.CorrelationId)
            End If

            Return Materialize(Of MeResponse)(raw)

        End Function

        ''' <summary>
        ''' Issues the request and classifies the outcome. Never throws for a
        ''' network condition - an unreachable API is an expected state in an
        ''' online-only system, not an exceptional one.
        ''' </summary>
        Private Async Function SendCoreAsync(method As HttpMethod,
                                             path As String,
                                             body As Object,
                                             requiresAuthentication As Boolean) As Task(Of RawResponse)

            Dim correlationId As String = Guid.NewGuid().ToString("D")

            If requiresAuthentication AndAlso Not _tokens.HasToken Then
                Return RawResponse.NotAttempted(correlationId)
            End If

            Dim failureDetail As String

            Try
                Using request As New HttpRequestMessage(method, path)

                    request.Headers.TryAddWithoutValidation(CorrelationIdHeaderName, correlationId)

                    If _tokens.HasToken Then
                        request.Headers.Authorization = New AuthenticationHeaderValue("Bearer", _tokens.Token)
                    End If

                    If body IsNot Nothing Then
                        Dim payload As String = JsonSerializer.Serialize(body, body.GetType(), _json)
                        request.Content = New StringContent(payload, Encoding.UTF8, "application/json")
                    End If

                    Using response As HttpResponseMessage = Await _http.SendAsync(request)

                        Dim content As String = Await response.Content.ReadAsStringAsync()

                        SetState(ConnectionState.Online)

                        Return New RawResponse With {
                            .Reached = True,
                            .StatusCode = response.StatusCode,
                            .Body = content,
                            .CorrelationId = ReadCorrelationId(response, correlationId)
                        }

                    End Using

                End Using

            Catch ex As HttpRequestException
                ' Handled, not swallowed: converted into the Unavailable outcome
                ' below, which the UI is required to render as a refusal.
                failureDetail = Describe(ex)

            Catch ex As TaskCanceledException
                ' HttpClient surfaces its own timeout as a cancellation.
                failureDetail = String.Format(CultureInfo.CurrentCulture,
                                              "The API did not respond within {0} seconds.",
                                              CInt(_http.Timeout.TotalSeconds))
            End Try

            SetState(ConnectionState.Unavailable)

            Return New RawResponse With {
                .Reached = False,
                .TransportDetail = failureDetail
            }

        End Function

        ''' <summary>
        ''' A relative path with a query string appended, each value
        ''' percent-encoded. Centralised so every list/search call builds its
        ''' query the same way rather than each hand-concatenating one.
        ''' </summary>
        Private Shared Function BuildPath(path As String, query As IReadOnlyDictionary(Of String, String)) As String

            If query Is Nothing OrElse query.Count = 0 Then
                Return path
            End If

            Dim parts As New List(Of String)

            For Each pair As KeyValuePair(Of String, String) In query
                parts.Add(Uri.EscapeDataString(pair.Key) & "=" & Uri.EscapeDataString(pair.Value))
            Next

            Return path & "?" & String.Join("&", parts)

        End Function

        ''' <summary>Turns a raw response into a typed result.</summary>
        Private Function Materialize(Of TResponse As Class)(raw As RawResponse) As ApiResult(Of TResponse)

            If Not raw.Reached Then

                If raw.NotAuthenticated Then
                    Return ApiResult(Of TResponse).FromRejection(
                        New ApiErrorResponse With {
                            .ErrorCode = "CLIENT_NOT_AUTHENTICATED",
                            .Message = "Sign in before running this action.",
                            .CorrelationId = raw.CorrelationId
                        },
                        raw.CorrelationId)
                End If

                Return ApiResult(Of TResponse).FromUnavailable(raw.TransportDetail)

            End If

            If raw.StatusCode >= HttpStatusCode.OK AndAlso raw.StatusCode < HttpStatusCode.MultipleChoices Then

                Dim value As TResponse = Deserialize(Of TResponse)(raw.Body)

                If value Is Nothing Then
                    Return ApiResult(Of TResponse).FromRejection(
                        New ApiErrorResponse With {
                            .ErrorCode = "CLIENT_UNREADABLE_RESPONSE",
                            .Message = "The API returned a success status with a body this client could not read.",
                            .CorrelationId = raw.CorrelationId
                        },
                        raw.CorrelationId)
                End If

                Return ApiResult(Of TResponse).FromSuccess(value, raw.CorrelationId)

            End If

            Dim envelope As ApiErrorResponse = Deserialize(Of ApiErrorResponse)(raw.Body)

            If envelope Is Nothing OrElse String.IsNullOrEmpty(envelope.ErrorCode) Then
                ' A non-conforming error body. Report the status honestly rather
                ' than echoing a body of unknown shape back at the operator.
                envelope = New ApiErrorResponse With {
                    .ErrorCode = "HTTP_" & CInt(raw.StatusCode).ToString(CultureInfo.InvariantCulture),
                    .Message = String.Format(CultureInfo.CurrentCulture,
                                             "The API refused the request ({0} {1}).",
                                             CInt(raw.StatusCode),
                                             raw.StatusCode),
                    .CorrelationId = raw.CorrelationId
                }
            End If

            Return ApiResult(Of TResponse).FromRejection(envelope, raw.CorrelationId)

        End Function

        ''' <summary>Reflection-based deserialization; Nothing when the body is unusable.</summary>
        Private Function Deserialize(Of TValue As Class)(body As String) As TValue

            If String.IsNullOrWhiteSpace(body) Then
                Return Nothing
            End If

            Try
                Return JsonSerializer.Deserialize(Of TValue)(body, _json)
            Catch ex As JsonException
                ' Handled meaningfully by the caller: a body that will not parse
                ' becomes a controlled client-side error rather than a crash.
                Return Nothing
            End Try

        End Function

        ''' <summary>The server's correlation ID, falling back to the one sent.</summary>
        Private Shared Function ReadCorrelationId(response As HttpResponseMessage, sent As String) As String

            Dim values As IEnumerable(Of String) = Nothing

            If response.Headers.TryGetValues(CorrelationIdHeaderName, values) Then
                For Each value As String In values
                    If Not String.IsNullOrWhiteSpace(value) Then
                        Return value
                    End If
                Next
            End If

            Return sent

        End Function

        ''' <summary>
        ''' Plain words for a transport failure. The operator needs to know which
        ''' of the three it is, because the fix differs: start the API, install
        ''' the certificate, or correct the address.
        ''' </summary>
        Private Function Describe(ex As Exception) As String

            Dim current As Exception = ex

            Do While current IsNot Nothing

                Dim authFailure As AuthenticationException = TryCast(current, AuthenticationException)
                If authFailure IsNot Nothing Then
                    Return "The API's certificate is not trusted by this computer. " &
                           "Install merch-host.cer into Trusted Root Certification Authorities " &
                           "(installation guide section 4), then try again."
                End If

                Dim socketFailure As SocketException = TryCast(current, SocketException)
                If socketFailure IsNot Nothing Then
                    Select Case socketFailure.SocketErrorCode
                        Case SocketError.ConnectionRefused
                            Return String.Format(CultureInfo.CurrentCulture,
                                                 "Nothing is listening at {0}. The API is not running.",
                                                 _http.BaseAddress)
                        Case SocketError.HostNotFound, SocketError.NoData
                            Return String.Format(CultureInfo.CurrentCulture,
                                                 "The host name in {0} could not be resolved. " &
                                                 "Check the hosts file entry for MERCH-HOST.",
                                                 _http.BaseAddress)
                        Case Else
                            Return String.Format(CultureInfo.CurrentCulture,
                                                 "The API at {0} could not be reached ({1}).",
                                                 _http.BaseAddress,
                                                 socketFailure.SocketErrorCode)
                    End Select
                End If

                current = current.InnerException

            Loop

            Return String.Format(CultureInfo.CurrentCulture,
                                 "The API at {0} could not be reached.",
                                 _http.BaseAddress)

        End Function

        Private Sub SetState(newState As ConnectionState)

            If _state = newState Then
                Return
            End If

            _state = newState
            RaiseEvent ConnectionStateChanged(Me, EventArgs.Empty)

        End Sub

        ''' <summary>Disposes the underlying transport and drops the token.</summary>
        Public Sub Dispose() Implements IDisposable.Dispose

            If _disposed Then
                Return
            End If

            _disposed = True
            _tokens.Clear()
            _http.Dispose()

        End Sub

        ''' <summary>
        ''' What came back from one attempt, before it is given a type. Reached
        ''' is the field that matters: False means the API was never spoken to,
        ''' and no inference about server-side state is available.
        ''' </summary>
        Private NotInheritable Class RawResponse

            Public Property Reached As Boolean
            Public Property NotAuthenticated As Boolean
            Public Property TransportDetail As String = String.Empty
            Public Property StatusCode As HttpStatusCode
            Public Property Body As String = String.Empty
            Public Property CorrelationId As String = String.Empty

            ''' <summary>
            ''' A protected call refused locally because no token is held. The
            ''' request is never put on the wire, so this is not a connection
            ''' failure and must not move the connection state.
            ''' </summary>
            Public Shared Function NotAttempted(correlationId As String) As RawResponse

                Return New RawResponse With {
                    .Reached = False,
                    .NotAuthenticated = True,
                    .CorrelationId = correlationId
                }

            End Function

        End Class

    End Class

End Namespace
