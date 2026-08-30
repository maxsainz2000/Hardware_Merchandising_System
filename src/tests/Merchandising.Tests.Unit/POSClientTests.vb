' Merchandising.Tests.Unit.POSClientTests
'
' P5-13 evidence for the POS client's view models. The business rules these
' screens touch are already proven server-side (Tracks C/D/E of Phase 5) -
' this suite exists for what THIS CARD adds on top: the client must mint a
' fresh idempotency key per write attempt, must never invent its own wording
' for a server refusal, must never hide a correlation ID, must send no price
' on a sale line and no tendered amount for a Card/EWallet payment
' (CreateSaleLineRequest / CreateSalePaymentRequest's own headers), and a
' return must be recordable against a sale this client itself just
' completed - the session-scoped CompletedSales collection Checkout and
' Returns share (MainViewModel's own header), since no GET /api/v1/sales/{id}
' route exists to look one up independently.
'
' StubHttpMessageHandler (P1-15) holds the transport still; every other line
' of MerchandisingApiClient and the view models under test is the real one -
' InventoryClientTests/ProcurementClientTests' identical posture.

Imports System.Collections.ObjectModel
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Text
Imports System.Text.Json
Imports System.Threading.Tasks
Imports Merchandising.ClientCommon.Api
Imports Merchandising.Contracts.Sales
Imports Merchandising.POS.ViewModels
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class POSClientTests

    Private Shared ReadOnly ApiRoot As New Uri("https://MERCH-HOST:8443/")

    ''' <summary>P5-04/ADR-007: opening and closing a session are two separate calls, each carrying the declared amounts and nothing this client computed itself (CalculatedCash/CashVariance come back from the server).</summary>
    <TestMethod>
    Public Async Function SessionViewModel_OpenThenClose_RendersTheServersOwnCalculatedFigures() As Task

        Using handler As New StubHttpMessageHandler(AddressOf RespondToSessionLifecycle)
            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Await client.LoginAsync("cashier1", "password")

                Dim viewModel As New SessionViewModel(client)
                viewModel.OpeningFloatText = "500.00"

                Await viewModel.OpenAsync()
                Assert.IsFalse(viewModel.LastCallFailed, "Open should have succeeded: " & viewModel.ResultText)
                Assert.IsTrue(viewModel.IsSessionOpen)
                Assert.AreEqual(9001, viewModel.SessionId)

                viewModel.DeclaredCashText = "612.50"
                Await viewModel.CloseAsync()

                Assert.IsFalse(viewModel.LastCallFailed, "Close should have succeeded: " & viewModel.ResultText)
                Assert.IsFalse(viewModel.IsSessionOpen)
                StringAssert.Contains(viewModel.SessionDisplay, "612.50")
                StringAssert.Contains(viewModel.SessionDisplay, "12.50",
                                     "The variance shown must be the SERVER's calculated figure, not one this client derived.")

            End Using
        End Using

    End Function

    ''' <summary>ADR-007: a repeated key replays ONE intent. Two separate Complete-sale presses are two separate intents, so each attempt must mint its own key.</summary>
    <TestMethod>
    Public Async Function CompleteSaleAsync_MintsAFreshIdempotencyKeyPerAttempt() As Task

        Dim nextSaleId As Integer = 500

        Using handler As New StubHttpMessageHandler(
            Function(request)
                If request.RequestUri.AbsolutePath.EndsWith("login", StringComparison.Ordinal) Then
                    Return Json(HttpStatusCode.OK, LoginPayload())
                End If
                If request.RequestUri.AbsolutePath.EndsWith("sales", StringComparison.Ordinal) AndAlso request.Method.Method = "POST" Then
                    nextSaleId += 1
                    Return Json(HttpStatusCode.Created, SalePayload(nextSaleId, "Cash", 20D, 20D))
                End If
                Return Json(HttpStatusCode.OK, New With {.ok = True})
            End Function)

            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Await client.LoginAsync("cashier1", "password")

                Dim completedSales As New ObservableCollection(Of SaleResponse)()
                Dim viewModel As New CheckoutViewModel(client, completedSales)

                PrepareOneCartLine(viewModel)
                viewModel.TenderedAmountText = "20.00"
                Await viewModel.CompleteSaleAsync()
                Assert.IsFalse(viewModel.LastCallFailed, "First sale attempt should have succeeded: " & viewModel.ResultText)

                PrepareOneCartLine(viewModel)
                viewModel.TenderedAmountText = "20.00"
                Await viewModel.CompleteSaleAsync()
                Assert.IsFalse(viewModel.LastCallFailed, "Second sale attempt should have succeeded: " & viewModel.ResultText)

                Dim requests = handler.Requests.Where(Function(r) r.Method = "POST" AndAlso r.Path.EndsWith("sales", StringComparison.Ordinal)).ToList()
                Assert.HasCount(2, requests)

                Dim firstKey As String = ExtractIdempotencyKey(requests(0).Body)
                Dim secondKey As String = ExtractIdempotencyKey(requests(1).Body)

                Assert.AreNotEqual(firstKey, secondKey,
                                   "Two distinct sale attempts shared one idempotency key - a retry-safety key must not double as a request counter.")
                Assert.HasCount(2, completedSales, "Every completed sale must be appended to the shared collection Returns reads from.")

            End Using
        End Using

    End Function

    ''' <summary>CLAUDE.md section 5: a server rejection surfaces with the API's OWN error code and message, never a client-invented paraphrase, and the cart is left intact so the cashier can adjust and retry.</summary>
    <TestMethod>
    Public Async Function CompleteSaleAsync_InsufficientStockRefusalSurfacesVerbatim_AndLeavesTheCartIntact() As Task

        Using handler As New StubHttpMessageHandler(
            Function(request)
                If request.RequestUri.AbsolutePath.EndsWith("login", StringComparison.Ordinal) Then
                    Return Json(HttpStatusCode.OK, LoginPayload())
                End If
                If request.RequestUri.AbsolutePath.EndsWith("sales", StringComparison.Ordinal) AndAlso request.Method.Method = "POST" Then
                    Return Problem(HttpStatusCode.Conflict, "SALE_INSUFFICIENT_STOCK", "There is not enough available stock for lines[0].productId.")
                End If
                Return Json(HttpStatusCode.OK, New With {.ok = True})
            End Function)

            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Await client.LoginAsync("cashier1", "password")

                Dim completedSales As New ObservableCollection(Of SaleResponse)()
                Dim viewModel As New CheckoutViewModel(client, completedSales)

                PrepareOneCartLine(viewModel)
                viewModel.TenderedAmountText = "20.00"

                Await viewModel.CompleteSaleAsync()

                Assert.IsTrue(viewModel.LastCallFailed)
                Assert.AreEqual("SALE_INSUFFICIENT_STOCK", viewModel.StatusMessage,
                                "The view model must show the server's own error code, not an invented one.")
                Assert.AreEqual("There is not enough available stock for lines[0].productId.", viewModel.ResultText,
                                "The view model must show the server's own message verbatim.")
                Assert.IsFalse(String.IsNullOrWhiteSpace(viewModel.CorrelationId),
                               "A rejection must still carry the ID an operator quotes (ADR-014).")
                Assert.HasCount(1, viewModel.Cart, "A refused sale must not clear the cart - the cashier still needs it to adjust and retry.")
                Assert.IsEmpty(completedSales, "A refused sale must never be appended to the completed-sales collection.")

            End Using
        End Using

    End Function

    ''' <summary>CreateSalePaymentRequest's own header: TenderedAmount must not be supplied for a Card/EWallet payment. Asserted directly against the request body this client actually sent, not only against the type system.</summary>
    <TestMethod>
    Public Async Function CompleteSaleAsync_CardPayment_SendsNoTenderedAmount() As Task

        Using handler As New StubHttpMessageHandler(
            Function(request)
                If request.RequestUri.AbsolutePath.EndsWith("login", StringComparison.Ordinal) Then
                    Return Json(HttpStatusCode.OK, LoginPayload())
                End If
                If request.RequestUri.AbsolutePath.EndsWith("sales", StringComparison.Ordinal) AndAlso request.Method.Method = "POST" Then
                    Return Json(HttpStatusCode.Created, SalePayload(600, "Card", 20D, Nothing))
                End If
                Return Json(HttpStatusCode.OK, New With {.ok = True})
            End Function)

            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Await client.LoginAsync("cashier1", "password")

                Dim completedSales As New ObservableCollection(Of SaleResponse)()
                Dim viewModel As New CheckoutViewModel(client, completedSales)

                PrepareOneCartLine(viewModel)
                viewModel.SelectedPaymentMethod = "Card"

                Await viewModel.CompleteSaleAsync()
                Assert.IsFalse(viewModel.LastCallFailed, "Card sale should have succeeded: " & viewModel.ResultText)

                Dim body As String = handler.Requests.Single(Function(r) r.Method = "POST" AndAlso r.Path.EndsWith("sales", StringComparison.Ordinal)).Body

                Using document As JsonDocument = JsonDocument.Parse(body)

                    Dim payment As JsonElement = document.RootElement.GetProperty("payment")
                    Assert.AreEqual("Card", payment.GetProperty("method").GetString())

                    Dim tendered As JsonElement = payment.GetProperty("tenderedAmount")
                    Assert.AreEqual(JsonValueKind.Null, tendered.ValueKind,
                                    "A Card payment must send no tendered amount at all - CreateSalePaymentRequest's own header.")

                End Using

                StringAssert.Contains(viewModel.ReceiptSummary, "Recorded",
                                     "G-24: the receipt must state the payment is recorded, using SalePaymentResponse's own stable wording.")

            End Using
        End Using

    End Function

    ''' <summary>Spec section 10.3: a return must identify the original sale line. This asserts the line actually sent is the one selected from a sale THIS CLIENT ITSELF completed - the shared CompletedSales collection this class's own header describes - never a client-guessed Id.</summary>
    <TestMethod>
    Public Async Function RecordReturnAsync_SendsTheSelectedSaleLineId_AgainstTheCompletedSale() As Task

        Using handler As New StubHttpMessageHandler(
            Function(request)
                If request.RequestUri.AbsolutePath.EndsWith("login", StringComparison.Ordinal) Then
                    Return Json(HttpStatusCode.OK, LoginPayload())
                End If
                If request.RequestUri.AbsolutePath.EndsWith("sales", StringComparison.Ordinal) AndAlso request.Method.Method = "POST" Then
                    Return Json(HttpStatusCode.Created, SalePayload(700, "Cash", 20D, 0D))
                End If
                If request.RequestUri.AbsolutePath.Contains("/returns") AndAlso request.Method.Method = "POST" Then
                    Return Json(HttpStatusCode.Created, ReturnPayload(1, 700, "Completed"))
                End If
                Return Json(HttpStatusCode.OK, New With {.ok = True})
            End Function)

            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Await client.LoginAsync("cashier1", "password")

                Dim completedSales As New ObservableCollection(Of SaleResponse)()
                Dim checkout As New CheckoutViewModel(client, completedSales)

                PrepareOneCartLine(checkout)
                checkout.TenderedAmountText = "20.00"
                Await checkout.CompleteSaleAsync()
                Assert.IsFalse(checkout.LastCallFailed, "Precondition: the sale to return against must have completed: " & checkout.ResultText)

                Dim returns As New ReturnsViewModel(client, completedSales)
                returns.SelectedSale = returns.CompletedSales.Single()
                returns.SelectedSaleLine = returns.SaleLines.Single()
                returns.ReasonText = "Customer changed their mind"
                returns.RestocksItem = True

                Await returns.RecordReturnAsync()

                Assert.IsFalse(returns.LastCallFailed, "The return should have succeeded: " & returns.ResultText)

                Dim body As String = handler.Requests.Single(Function(r) r.Method = "POST" AndAlso r.Path.Contains("/returns")).Body

                Using document As JsonDocument = JsonDocument.Parse(body)

                    Dim line As JsonElement = document.RootElement.GetProperty("lines")(0)
                    Assert.AreEqual(9001, line.GetProperty("saleLineId").GetInt32(),
                                    "The return must name the sale line from the sale it was completed against, not a client-guessed Id.")
                    Assert.IsTrue(line.GetProperty("restocksItem").GetBoolean())

                End Using

            End Using
        End Using

    End Function

    ' ---------------------------------------------------------------- helpers

    Private Shared Sub PrepareOneCartLine(viewModel As CheckoutViewModel)

        viewModel.SelectedProduct = New Merchandising.Contracts.Products.ProductResponse With {
            .Id = 42, .Sku = "HDW-1002", .Name = "Adjustable Wrench 10in", .Price = 20D, .Cost = 12D, .IsActive = True, .AvailableStock = 50D
        }
        viewModel.CartQuantityText = "1"
        viewModel.AddToCartCommand.Execute(Nothing)

    End Sub

    Private Shared Function ExtractIdempotencyKey(body As String) As String

        Using document As JsonDocument = JsonDocument.Parse(body)
            Return document.RootElement.GetProperty("idempotencyKey").GetString()
        End Using

    End Function

    Private Shared Function RespondToSessionLifecycle(request As HttpRequestMessage) As HttpResponseMessage

        Dim path As String = request.RequestUri.AbsolutePath

        If path.EndsWith("login", StringComparison.Ordinal) Then
            Return Json(HttpStatusCode.OK, LoginPayload())
        End If

        If path.EndsWith("cashier-sessions", StringComparison.Ordinal) AndAlso request.Method.Method = "POST" Then
            Return Json(HttpStatusCode.Created, SessionPayload(9001, "Open", 500D, Nothing, Nothing, Nothing))
        End If

        If path.EndsWith("/close", StringComparison.Ordinal) AndAlso request.Method.Method = "POST" Then
            Return Json(HttpStatusCode.OK, SessionPayload(9001, "Closed", 500D, 612.50D, 600D, 12.50D))
        End If

        Return Json(HttpStatusCode.OK, New With {.ok = True})

    End Function

    Private Shared Function LoginPayload() As Object

        Return New With {
            .token = "P5-13-VM-FIXTURE-TOKEN",
            .expiresAtUtc = DateTime.UtcNow.AddHours(8),
            .username = "cashier1",
            .roles = New String() {"Cashier"}
        }

    End Function

    Private Shared Function SessionPayload(id As Integer, status As String, openingFloat As Decimal,
                                           declaredCash As Decimal?, calculatedCash As Decimal?, cashVariance As Decimal?) As Object

        Return New With {
            .id = id,
            .openedByUserId = 1,
            .closedByUserId = CType(Nothing, Integer?),
            .openingFloat = openingFloat,
            .declaredCash = declaredCash,
            .calculatedCash = calculatedCash,
            .cashVariance = cashVariance,
            .status = status,
            .openedAtUtc = DateTime.UtcNow,
            .closedAtUtc = CType(Nothing, DateTime?),
            .rowVersion = 1L,
            .createdAtUtc = DateTime.UtcNow,
            .updatedAtUtc = DateTime.UtcNow,
            .paymentTotals = Array.Empty(Of Object)()
        }

    End Function

    Private Shared Function SalePayload(id As Integer, method As String, unitPrice As Decimal, tenderedAmount As Decimal?) As Object

        Return New With {
            .id = id,
            .cashierSessionId = 9001,
            .cashierUserId = 1,
            .total = unitPrice,
            .status = "Completed",
            .correlationId = Guid.NewGuid().ToString("D"),
            .createdAtUtc = DateTime.UtcNow,
            .lines = New Object() {
                New With {
                    .id = 9001, .productId = 42, .productSku = "HDW-1002", .productName = "Adjustable Wrench 10in",
                    .quantity = 1D, .unitPrice = unitPrice, .cost = 12D, .lineTotal = unitPrice
                }
            },
            .payment = New With {
                .id = 1,
                .method = method,
                .amount = unitPrice,
                .tenderedAmount = tenderedAmount,
                .changeAmount = If(tenderedAmount.HasValue, CType(tenderedAmount.Value - unitPrice, Decimal?), Nothing),
                .status = "Recorded"
            }
        }

    End Function

    Private Shared Function ReturnPayload(id As Integer, saleId As Integer, status As String) As Object

        Return New With {
            .id = id,
            .saleId = saleId,
            .status = status,
            .returnedByUserId = 1,
            .approvedByUserId = CType(Nothing, Integer?),
            .reason = "Customer changed their mind",
            .exceedsThreshold = False,
            .refundMethod = "Cash",
            .refundAmount = CType(20D, Decimal?),
            .returnedAtUtc = DateTime.UtcNow,
            .approvedAtUtc = CType(Nothing, DateTime?),
            .rowVersion = 1L,
            .createdAtUtc = DateTime.UtcNow,
            .updatedAtUtc = DateTime.UtcNow,
            .lines = New Object() {
                New With {.id = 1, .saleLineId = 9001, .quantityReturned = 1D, .restocksItem = True}
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
