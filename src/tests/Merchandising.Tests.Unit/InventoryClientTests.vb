' Merchandising.Tests.Unit.InventoryClientTests
'
' P4-12 evidence for the Inventory client's view models. Every business rule
' these screens touch is already proven server-side (Tracks D/E) - this suite
' exists for what the CARD itself adds on top: the client must never invent
' its own wording for a server refusal, must never hide a correlation ID,
' must mint a fresh idempotency key per write attempt, and must refuse
' locally rather than reach the wire when a required field is missing -
' exactly the properties ProcurementClientTests (P3-07) already establishes
' for the Procurement client's own screens, mirrored here.
'
' Two of these tests also carry forward regression coverage that would
' otherwise have been lost when SpikeViewModelTests.vb was retired alongside
' SpikeViewModel itself (P1-15's window, replaced by this card, not
' extended): a correlation ID that changes with every successful call, and a
' MAINTENANCE_MODE refusal rendered with ApiFailurePresenter's own wording.
' Neither view model below hand-rolls its own ShowSuccess/ShowFailure the way
' SpikeViewModel once did - every one of them delegates to the same shared,
' already-tested ApiFailurePresenter Procurement's five screens use, which is
' what makes the specific case-insensitive parameter-shadowing defect
' structurally unable to recur here.
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
Imports Merchandising.Contracts.Products
Imports Merchandising.Inventory.ViewModels
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class InventoryClientTests

    Private Shared ReadOnly ApiRoot As New Uri("https://MERCH-HOST:8443/")

    ''' <summary>
    ''' ADR-007: a repeated key replays ONE intent. Two separate button presses
    ''' are two separate intents, so each of AdjustmentsViewModel's request
    ''' attempts must mint its own key.
    ''' </summary>
    <TestMethod>
    Public Async Function RequestAdjustmentAsync_MintsAFreshIdempotencyKeyPerAttempt() As Task

        Using handler As New StubHttpMessageHandler(AddressOf RespondToAdjustmentLifecycle)
            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Await client.LoginAsync("inventoryclerk", "password")

                Dim viewModel As New AdjustmentsViewModel(client)
                PrepareOneRequest(viewModel)

                Await viewModel.RequestAsync()
                Assert.IsFalse(viewModel.LastCallFailed, "First request attempt should have succeeded: " & viewModel.ResultText)

                PrepareOneRequest(viewModel)
                Await viewModel.RequestAsync()
                Assert.IsFalse(viewModel.LastCallFailed, "Second request attempt should have succeeded: " & viewModel.ResultText)

                Dim requests = handler.Requests.Where(Function(r) r.Method = "POST" AndAlso r.Path.EndsWith("adjustments", StringComparison.Ordinal)).ToList()

                Assert.HasCount(2, requests)

                Dim firstKey As String = ExtractIdempotencyKey(requests(0).Body)
                Dim secondKey As String = ExtractIdempotencyKey(requests(1).Body)

                Assert.AreNotEqual(firstKey, secondKey,
                                   "Two distinct request attempts shared one idempotency key - a retry-safety key must not double as a request counter.")

            End Using
        End Using

    End Function

    ''' <summary>
    ''' CLAUDE.md section 5 / this card's own Done-when box: a server rejection
    ''' surfaces with the API's OWN error code and message, never a
    ''' client-invented paraphrase, and the correlation ID is never dropped.
    ''' </summary>
    <TestMethod>
    Public Async Function RequestAdjustmentAsync_RejectionSurfacesTheServersOwnWordingAndCorrelationId() As Task

        Using handler As New StubHttpMessageHandler(
            Function(request)
                If request.RequestUri.AbsolutePath.EndsWith("login", StringComparison.Ordinal) Then
                    Return Json(HttpStatusCode.OK, LoginPayload())
                End If
                If request.RequestUri.AbsolutePath.EndsWith("adjustments", StringComparison.Ordinal) AndAlso request.Method.Method = "POST" Then
                    Return Problem(HttpStatusCode.NotFound, "PRODUCT_NOT_FOUND", "No product with that Id exists.")
                End If
                Return Json(HttpStatusCode.OK, New With {.ok = True})
            End Function)

            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Await client.LoginAsync("inventoryclerk", "password")

                Dim viewModel As New AdjustmentsViewModel(client)
                PrepareOneRequest(viewModel)

                Await viewModel.RequestAsync()

                Assert.IsTrue(viewModel.LastCallFailed)
                Assert.AreEqual("PRODUCT_NOT_FOUND", viewModel.StatusMessage,
                                "The view model must show the server's own error code, not an invented one.")
                Assert.AreEqual("No product with that Id exists.", viewModel.ResultText,
                                "The view model must show the server's own message verbatim.")
                Assert.IsFalse(String.IsNullOrWhiteSpace(viewModel.CorrelationId),
                               "A rejection must still carry the ID an operator quotes (ADR-014).")

            End Using
        End Using

    End Function

    ''' <summary>
    ''' Adjustments.Approve is AdminAndAbove (spec section 9: approval needs
    ''' Admin, an InventoryClerk may only request) and carries the self-approval
    ''' veto (ADR-017 section 6) besides. Either refusal comes back as an
    ''' ordinary 403 Rejected outcome that AdjustmentsViewModel must render
    ''' exactly as worded - never pre-empting the Approve button or guessing
    ''' why it would fail, the same posture PurchaseOrderListViewModel's own
    ''' header documents for order approval.
    ''' </summary>
    <TestMethod>
    Public Async Function ApproveAdjustmentAsync_ForbiddenRefusalSurfacesVerbatim() As Task

        Using handler As New StubHttpMessageHandler(
            Function(request)
                If request.RequestUri.AbsolutePath.EndsWith("login", StringComparison.Ordinal) Then
                    Return Json(HttpStatusCode.OK, LoginPayload())
                End If
                If request.RequestUri.AbsolutePath.EndsWith("adjustments", StringComparison.Ordinal) AndAlso request.Method.Method = "POST" Then
                    Return Json(HttpStatusCode.Created, AdjustmentPayload(1, "Pending"))
                End If
                If request.RequestUri.AbsolutePath.EndsWith("/approve", StringComparison.Ordinal) Then
                    Return Problem(HttpStatusCode.Forbidden, "FORBIDDEN", "Actor's role does not hold Adjustments.Approve.")
                End If
                Return Json(HttpStatusCode.OK, New With {.ok = True})
            End Function)

            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Await client.LoginAsync("inventoryclerk", "password")

                Dim viewModel As New AdjustmentsViewModel(client)
                PrepareOneRequest(viewModel)
                Await viewModel.RequestAsync()
                viewModel.SelectedAdjustment = viewModel.Adjustments.Single()

                Await viewModel.ApproveAsync()

                Assert.IsTrue(viewModel.LastCallFailed)
                Assert.AreEqual("FORBIDDEN", viewModel.StatusMessage)
                Assert.AreEqual("Actor's role does not hold Adjustments.Approve.", viewModel.ResultText,
                                "The client must show the server's own refusal message, not invent one.")
                Assert.IsFalse(String.IsNullOrWhiteSpace(viewModel.CorrelationId))
                Assert.AreEqual("Pending", viewModel.Adjustments.Single().Status,
                                "A refused approval must not leave the row looking approved.")

            End Using
        End Using

    End Function

    ''' <summary>
    ''' A reference number is required (spec section 10.1's receiving command;
    ''' mirrored client-side for usability only, CLAUDE.md section 5). Missing
    ''' it must refuse LOCALLY - the request never reaches the wire, because the
    ''' server's own requirement is the guarantee and this is only the
    ''' friendlier front door to it. Mirrors
    ''' PurchaseOrderListViewModel.CancelAsync's own missing-reason rule.
    ''' </summary>
    <TestMethod>
    Public Async Function ReceiveAsync_WithoutAReferenceNumber_NeverReachesTheWire() As Task

        Using handler As New StubHttpMessageHandler(AddressOf RespondToReceivingLifecycle)
            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Await client.LoginAsync("admin", "password")

                Dim viewModel As New ReceivingViewModel(client)
                viewModel.OrderIdText = "1"
                Await viewModel.LoadOrderAsync()
                Assert.IsFalse(viewModel.LastCallFailed, "Precondition: loading the order should have succeeded: " & viewModel.ResultText)

                viewModel.SelectedOrderLine = viewModel.OrderLines.Single()
                viewModel.AddLineCommand.Execute(Nothing)
                Assert.HasCount(1, viewModel.DraftLines, "Precondition: a draft line should have been added.")

                viewModel.ReferenceNumberText = "   "

                Await viewModel.ReceiveAsync()

                Assert.IsTrue(viewModel.LastCallFailed)
                Assert.IsFalse(handler.Requests.Any(Function(r) r.Method = "POST" AndAlso r.Path.EndsWith("receipts", StringComparison.Ordinal)),
                               "A blank reference number must be refused locally, never sent to the API.")

            End Using
        End Using

    End Function

    ''' <summary>
    ''' Each successful call must REPLACE the displayed correlation ID with its
    ''' own - the direct regression test for the P1-15/P1-21 defect class
    ''' SpikeViewModelTests once covered (case-insensitive parameter shadowing
    ''' froze the displayed value at an earlier call's ID). No view model below
    ''' hand-rolls that assignment any more - every one sets CorrelationId
    ''' directly from the result, the same pattern Procurement's five screens
    ''' already use - but the observable guarantee is worth asserting directly
    ''' rather than only by construction.
    ''' </summary>
    <TestMethod>
    Public Async Function SearchAsync_EachSuccessfulCall_ReplacesThePreviousCorrelationId() As Task

        Using handler As New StubHttpMessageHandler(
            Function(request)
                If request.RequestUri.AbsolutePath.EndsWith("login", StringComparison.Ordinal) Then
                    Return Json(HttpStatusCode.OK, LoginPayload())
                End If
                Return Json(HttpStatusCode.OK, New With {.items = Array.Empty(Of Object)(), .totalCount = 0,
                                                          .page = 1, .pageSize = 100, .maxPageSize = 100, .sort = "productName:asc"})
            End Function)

            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Await client.LoginAsync("inventoryclerk", "password")

                Dim viewModel As New StockBrowseViewModel(client)

                Await viewModel.SearchAsync()
                Dim afterFirst As String = viewModel.CorrelationId

                Await viewModel.SearchAsync()
                Dim afterSecond As String = viewModel.CorrelationId

                Assert.IsFalse(String.IsNullOrWhiteSpace(afterFirst), "A successful call left the correlation ID empty.")
                Assert.AreNotEqual(afterFirst, afterSecond,
                                   "Two successive successful calls displayed the same correlation ID - the screen is showing a stale value.")

            End Using
        End Using

    End Function

    ''' <summary>
    ''' P1-18, spec section 15 step 2: a write refused with MAINTENANCE_MODE
    ''' must render distinctly from an ordinary rejection - the exact wording
    ''' ApiFailurePresenter centralises and SpikeViewModelTests once asserted
    ''' directly against SpikeViewModel's own hand-rolled copy of this logic.
    ''' </summary>
    <TestMethod>
    Public Async Function MaintenanceRefusal_IsRenderedWithTheSharedPresentersWording() As Task

        Using handler As New StubHttpMessageHandler(
            Function(request)
                If request.RequestUri.AbsolutePath.EndsWith("login", StringComparison.Ordinal) Then
                    Return Json(HttpStatusCode.OK, LoginPayload())
                End If
                If request.RequestUri.AbsolutePath.EndsWith("adjustments", StringComparison.Ordinal) AndAlso request.Method.Method = "POST" Then
                    Return Json(HttpStatusCode.ServiceUnavailable, New With {
                        .errorCode = "MAINTENANCE_MODE",
                        .message = "The system is under maintenance and is not accepting changes. Reason: Nightly restore rehearsal",
                        .correlationId = "3f2504e0-4f89-41d3-9a0c-0305e82c3301",
                        .errors = CType(Nothing, Object)
                    })
                End If
                Return Json(HttpStatusCode.OK, New With {.ok = True})
            End Function)

            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Await client.LoginAsync("inventoryclerk", "password")

                Dim viewModel As New AdjustmentsViewModel(client)
                PrepareOneRequest(viewModel)

                Await viewModel.RequestAsync()

                Assert.IsTrue(viewModel.LastCallFailed)
                Assert.AreEqual("The system is under maintenance. Your change was not saved.", viewModel.StatusMessage,
                                "A MAINTENANCE_MODE refusal must render with the shared presenter's own wording, not a generic rejection message.")
                StringAssert.Contains(viewModel.ResultText, "Nightly restore rehearsal",
                                      "The operator's own reason did not reach the client.")

            End Using
        End Using

    End Function

    ' ---------------------------------------------------------------- helpers

    Private Shared Sub PrepareOneRequest(viewModel As AdjustmentsViewModel)

        viewModel.SelectedProduct = New ProductResponse With {
            .Id = 1, .Sku = "HW-001", .Name = "Hammer", .Cost = 100D, .Price = 150D, .IsActive = True
        }
        viewModel.VarianceText = "-2"
        viewModel.ReasonText = "Damaged in storage"

    End Sub

    Private Shared Function ExtractIdempotencyKey(body As String) As String

        Using document As JsonDocument = JsonDocument.Parse(body)
            Return document.RootElement.GetProperty("idempotencyKey").GetString()
        End Using

    End Function

    Private Shared Function RespondToAdjustmentLifecycle(request As HttpRequestMessage) As HttpResponseMessage

        Dim path As String = request.RequestUri.AbsolutePath

        If path.EndsWith("login", StringComparison.Ordinal) Then
            Return Json(HttpStatusCode.OK, LoginPayload())
        End If

        If path.EndsWith("adjustments", StringComparison.Ordinal) AndAlso request.Method.Method = "POST" Then
            Return Json(HttpStatusCode.Created, AdjustmentPayload(1, "Applied"))
        End If

        Return Json(HttpStatusCode.OK, New With {.ok = True})

    End Function

    Private Shared Function RespondToReceivingLifecycle(request As HttpRequestMessage) As HttpResponseMessage

        Dim path As String = request.RequestUri.AbsolutePath

        If path.EndsWith("login", StringComparison.Ordinal) Then
            Return Json(HttpStatusCode.OK, LoginPayload())
        End If

        If path.EndsWith("/1", StringComparison.Ordinal) AndAlso request.Method.Method = "GET" Then
            Return Json(HttpStatusCode.OK, OrderPayload())
        End If

        Return Json(HttpStatusCode.OK, New With {.ok = True})

    End Function

    Private Shared Function LoginPayload() As Object

        Return New With {
            .token = "P4-12-VM-FIXTURE-TOKEN",
            .expiresAtUtc = DateTime.UtcNow.AddHours(8),
            .username = "clerk",
            .roles = New String() {"InventoryClerk"}
        }

    End Function

    Private Shared Function AdjustmentPayload(id As Integer, status As String) As Object

        Return New With {
            .id = id,
            .productId = 1,
            .productSku = "HW-001",
            .productName = "Hammer",
            .quantityVariance = -2D,
            .reason = "Damaged in storage",
            .requestedByUserId = 1,
            .approvedByUserId = CType(Nothing, Integer?),
            .exceedsThreshold = False,
            .status = status,
            .movementId = CType(Nothing, Integer?),
            .rowVersion = 1L,
            .createdAtUtc = DateTime.UtcNow,
            .updatedAtUtc = DateTime.UtcNow
        }

    End Function

    Private Shared Function OrderPayload() As Object

        Return New With {
            .id = 1,
            .orderNumber = "PO-20260828-0001",
            .supplierId = 1,
            .supplierName = "Acme Hardware",
            .status = "Approved",
            .requestedByUserId = 1,
            .approvedByUserId = CType(1, Integer?),
            .submittedAtUtc = CType(DateTime.UtcNow, DateTime?),
            .approvedAtUtc = CType(DateTime.UtcNow, DateTime?),
            .rowVersion = 1L,
            .createdAtUtc = DateTime.UtcNow,
            .updatedAtUtc = DateTime.UtcNow,
            .lines = New Object() {
                New With {
                    .id = 10, .lineNumber = 1, .productId = 1, .productSku = "HW-001", .productName = "Hammer",
                    .orderedQuantity = 5D, .purchaseCost = 100D, .receivedQuantity = 0D,
                    .rowVersion = 1L, .createdAtUtc = DateTime.UtcNow, .updatedAtUtc = DateTime.UtcNow
                }
            }
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
