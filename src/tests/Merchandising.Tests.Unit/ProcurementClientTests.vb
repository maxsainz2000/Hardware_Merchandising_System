' Merchandising.Tests.Unit.ProcurementClientTests
'
' P3-07 evidence for the Procurement client's view models. Every business
' rule these screens touch is already proven server-side (P3-01 through
' P3-06) - this suite exists for what the CARD itself adds on top: the
' client must never invent its own wording for a server refusal, must never
' hide a correlation ID, and must mint a fresh idempotency key per create
' attempt rather than reusing one (SpikeViewModel's DecrementStockAsync
' established the same rule at P1-15).
'
' StubHttpMessageHandler (P1-15) holds the transport still; every other line
' of MerchandisingApiClient and the view models under test is the real one.

Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Text
Imports System.Text.Json
Imports System.Threading.Tasks
Imports Merchandising.ClientCommon.Api
Imports Merchandising.Procurement.ViewModels
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class ProcurementClientTests

    Private Shared ReadOnly ApiRoot As New Uri("https://MERCH-HOST:8443/")

    ''' <summary>
    ''' ADR-007: a repeated key replays ONE intent. Two separate button
    ''' presses are two separate intents, so each of NewPurchaseOrderViewModel's
    ''' create attempts must mint its own key - reusing one across attempts
    ''' would make a deliberate second order look, to the server, like a retry
    ''' of the first.
    ''' </summary>
    <TestMethod>
    Public Async Function CreateOrderAsync_MintsAFreshIdempotencyKeyPerAttempt() As Task

        Using handler As New StubHttpMessageHandler(AddressOf RespondToOrderLifecycle)
            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Await client.LoginAsync("procurementofficer", "password")

                Dim viewModel As New NewPurchaseOrderViewModel(client)
                PrepareOneLine(viewModel)

                Await viewModel.CreateOrderAsync()
                Assert.IsFalse(viewModel.LastCallFailed, "First create attempt should have succeeded: " & viewModel.ResultText)

                PrepareOneLine(viewModel)
                Await viewModel.CreateOrderAsync()
                Assert.IsFalse(viewModel.LastCallFailed, "Second create attempt should have succeeded: " & viewModel.ResultText)

                Dim creates = handler.Requests.Where(Function(r) r.Method = "POST" AndAlso r.Path.EndsWith("purchase-orders", StringComparison.Ordinal)).ToList()

                Assert.HasCount(2, creates)

                Dim firstKey As String = ExtractIdempotencyKey(creates(0).Body)
                Dim secondKey As String = ExtractIdempotencyKey(creates(1).Body)

                Assert.AreNotEqual(firstKey, secondKey,
                                   "Two distinct create attempts shared one idempotency key - a retry-safety key must not double as a request counter.")

            End Using
        End Using

    End Function

    ''' <summary>
    ''' CLAUDE.md section 5 / this card's own Done-when box: a server
    ''' rejection surfaces with the API's OWN error code and message, never a
    ''' client-invented paraphrase, and the correlation ID is never dropped.
    ''' </summary>
    <TestMethod>
    Public Async Function CreateOrderAsync_RejectionSurfacesTheServersOwnWordingAndCorrelationId() As Task

        Using handler As New StubHttpMessageHandler(
            Function(request)
                If request.RequestUri.AbsolutePath.EndsWith("login", StringComparison.Ordinal) Then
                    Return Json(HttpStatusCode.OK, LoginPayload())
                End If
                If request.RequestUri.AbsolutePath.EndsWith("purchase-orders", StringComparison.Ordinal) AndAlso request.Method.Method = "POST" Then
                    Return Problem(HttpStatusCode.BadRequest, "SUPPLIER_INACTIVE",
                                   "A purchase order cannot be raised against a deactivated supplier.")
                End If
                Return Json(HttpStatusCode.OK, New With {.items = Array.Empty(Of Object)(), .totalCount = 0, .page = 1, .pageSize = 25})
            End Function)

            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Await client.LoginAsync("procurementofficer", "password")

                Dim viewModel As New NewPurchaseOrderViewModel(client)
                PrepareOneLine(viewModel)

                Await viewModel.CreateOrderAsync()

                Assert.IsTrue(viewModel.LastCallFailed)
                Assert.AreEqual("SUPPLIER_INACTIVE", viewModel.StatusMessage,
                                "The view model must show the server's own error code, not an invented one.")
                Assert.AreEqual("A purchase order cannot be raised against a deactivated supplier.", viewModel.ResultText,
                                "The view model must show the server's own message verbatim.")
                Assert.IsFalse(String.IsNullOrWhiteSpace(viewModel.CorrelationId),
                               "A rejection must still carry the ID an operator quotes (ADR-014).")

            End Using
        End Using

    End Function

    ''' <summary>
    ''' The self-approval veto (ADR-017 section 6) is a 403 the view model
    ''' must render exactly as the API worded it - PurchaseOrderListViewModel
    ''' never pre-empts the Approve button or guesses why it would fail.
    ''' </summary>
    <TestMethod>
    Public Async Function ApproveAsync_SelfApprovalRefusalSurfacesVerbatim() As Task

        Using handler As New StubHttpMessageHandler(
            Function(request)
                If request.RequestUri.AbsolutePath.EndsWith("login", StringComparison.Ordinal) Then
                    Return Json(HttpStatusCode.OK, LoginPayload())
                End If
                If request.RequestUri.AbsolutePath.EndsWith("/approve", StringComparison.Ordinal) Then
                    Return Problem(HttpStatusCode.Forbidden, "FORBIDDEN", "Self-approval prohibited (spec section 9).")
                End If
                If request.RequestUri.AbsolutePath.EndsWith("/1", StringComparison.Ordinal) Then
                    Return Json(HttpStatusCode.OK, OrderPayload(1, "PO-20260828-0001", "Submitted"))
                End If
                Return Json(HttpStatusCode.OK, New With {.items = New Object() {SummaryPayload(1, "PO-20260828-0001", "Submitted")},
                                                          .totalCount = 1, .page = 1, .pageSize = 100,
                                                          .maxPageSize = 100, .sort = "createdAt:desc"})
            End Function)

            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Await client.LoginAsync("superadmin", "password")

                Dim viewModel As New PurchaseOrderListViewModel(client)
                Await viewModel.RefreshAsync()
                viewModel.SelectedOrderSummary = viewModel.Orders.Single()
                Await viewModel.LoadSelectedOrderDetailAsync()

                Await viewModel.ApproveAsync()

                Assert.IsTrue(viewModel.LastCallFailed)
                Assert.AreEqual("FORBIDDEN", viewModel.StatusMessage)
                Assert.AreEqual("Self-approval prohibited (spec section 9).", viewModel.ResultText,
                                "The client must show the server's own self-approval message, not invent one.")
                Assert.IsFalse(String.IsNullOrWhiteSpace(viewModel.CorrelationId))

            End Using
        End Using

    End Function

    ''' <summary>
    ''' Cancel requires a reason (spec section 9's "require reason where
    ''' configured", mirrored client-side for usability only, CLAUDE.md
    ''' section 5). Missing it must refuse LOCALLY - the request never
    ''' reaches the wire, because the server's own reason requirement is the
    ''' guarantee and this is only the friendlier front door to it.
    ''' </summary>
    <TestMethod>
    Public Async Function CancelAsync_WithoutAReason_NeverReachesTheWire() As Task

        Using handler As New StubHttpMessageHandler(AddressOf RespondToOrderLifecycle)
            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Await client.LoginAsync("procurementofficer", "password")

                Dim viewModel As New PurchaseOrderListViewModel(client)
                Await viewModel.RefreshAsync()
                viewModel.SelectedOrderSummary = viewModel.Orders.Single()
                Await viewModel.LoadSelectedOrderDetailAsync()
                viewModel.CancelReason = "   "

                Await viewModel.CancelAsync()

                Assert.IsTrue(viewModel.LastCallFailed)
                Assert.IsFalse(handler.Requests.Any(Function(r) r.Path.EndsWith("/cancel", StringComparison.Ordinal)),
                               "A blank reason must be refused locally, never sent to the API.")

            End Using
        End Using

    End Function

    ' ---------------------------------------------------------------- helpers

    Private Shared Sub PrepareOneLine(viewModel As NewPurchaseOrderViewModel)

        viewModel.SelectedSupplier = New Merchandising.Contracts.Suppliers.SupplierResponse With {
            .Id = 1, .Name = "Acme Hardware", .IsActive = True
        }
        viewModel.SelectedProduct = New Merchandising.Contracts.Products.ProductResponse With {
            .Id = 1, .Sku = "HW-001", .Name = "Hammer", .Cost = 100D, .Price = 150D, .IsActive = True
        }
        viewModel.NewLineQuantity = "5"
        viewModel.NewLineCost = "100.00"
        viewModel.AddLineCommand.Execute(Nothing)

    End Sub

    Private Shared Function ExtractIdempotencyKey(body As String) As String

        Using document As JsonDocument = JsonDocument.Parse(body)
            Return document.RootElement.GetProperty("idempotencyKey").GetString()
        End Using

    End Function

    Private Shared Function RespondToOrderLifecycle(request As HttpRequestMessage) As HttpResponseMessage

        Dim path As String = request.RequestUri.AbsolutePath

        If path.EndsWith("login", StringComparison.Ordinal) Then
            Return Json(HttpStatusCode.OK, LoginPayload())
        End If

        If path.EndsWith("purchase-orders", StringComparison.Ordinal) AndAlso request.Method.Method = "POST" Then
            Return Json(HttpStatusCode.Created, OrderPayload(1, "PO-20260828-0001", "Draft"))
        End If

        If path.EndsWith("purchase-orders", StringComparison.Ordinal) Then
            Return Json(HttpStatusCode.OK, New With {.items = New Object() {SummaryPayload(1, "PO-20260828-0001", "Draft")},
                                                      .totalCount = 1, .page = 1, .pageSize = 100,
                                                      .maxPageSize = 100, .sort = "createdAt:desc"})
        End If

        If path.EndsWith("/1", StringComparison.Ordinal) Then
            Return Json(HttpStatusCode.OK, OrderPayload(1, "PO-20260828-0001", "Draft"))
        End If

        Return Json(HttpStatusCode.OK, New With {.ok = True})

    End Function

    Private Shared Function LoginPayload() As Object

        Return New With {
            .token = "P3-07-VM-FIXTURE-TOKEN",
            .expiresAtUtc = DateTime.UtcNow.AddHours(8),
            .username = "clerk",
            .roles = New String() {"ProcurementOfficer"}
        }

    End Function

    Private Shared Function OrderPayload(id As Integer, orderNumber As String, status As String) As Object

        Return New With {
            .id = id,
            .orderNumber = orderNumber,
            .supplierId = 1,
            .supplierName = "Acme Hardware",
            .status = status,
            .requestedByUserId = 1,
            .approvedByUserId = CType(Nothing, Integer?),
            .submittedAtUtc = CType(Nothing, DateTime?),
            .approvedAtUtc = CType(Nothing, DateTime?),
            .rowVersion = 1L,
            .createdAtUtc = DateTime.UtcNow,
            .updatedAtUtc = DateTime.UtcNow,
            .lines = New Object() {
                New With {
                    .id = 1, .lineNumber = 1, .productId = 1, .productSku = "HW-001", .productName = "Hammer",
                    .orderedQuantity = 5D, .purchaseCost = 100D, .receivedQuantity = 0D,
                    .rowVersion = 1L, .createdAtUtc = DateTime.UtcNow, .updatedAtUtc = DateTime.UtcNow
                }
            }
        }

    End Function

    Private Shared Function SummaryPayload(id As Integer, orderNumber As String, status As String) As Object

        Return New With {
            .id = id,
            .orderNumber = orderNumber,
            .supplierId = 1,
            .supplierName = "Acme Hardware",
            .status = status,
            .requestedByUserId = 1,
            .approvedByUserId = CType(Nothing, Integer?),
            .submittedAtUtc = CType(Nothing, DateTime?),
            .approvedAtUtc = CType(Nothing, DateTime?),
            .lineCount = 1,
            .rowVersion = 1L,
            .createdAtUtc = DateTime.UtcNow,
            .updatedAtUtc = DateTime.UtcNow
        }

    End Function

    Private Shared Function Json(status As HttpStatusCode, payload As Object) As HttpResponseMessage

        Dim response As New HttpResponseMessage(status) With {
            .Content = New StringContent(JsonSerializer.Serialize(payload, payload.GetType()), Encoding.UTF8, "application/json")
        }

        response.Headers.TryAddWithoutValidation(MerchandisingApiClient.CorrelationIdHeaderName, Guid.NewGuid().ToString("D"))

        Return response

    End Function

    Private Shared Function Problem(status As HttpStatusCode, errorCode As String, message As String) As HttpResponseMessage

        Return Json(status, New With {
            .errorCode = errorCode,
            .message = message,
            .correlationId = Guid.NewGuid().ToString("D")
        })

    End Function

End Class
