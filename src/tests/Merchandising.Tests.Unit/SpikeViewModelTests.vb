' Merchandising.Tests.Unit.SpikeViewModelTests
'
' Regression cover for the view model that renders a call's outcome.
'
' These exist because of a defect found on 2026-08-21 during the cross-machine
' lab run, NOT by reading the code. Every layer beneath the view model was
' individually correct - MerchandisingApiClient generated a fresh correlation
' ID per request, the server echoed it, ApiResult carried it - and the value
' still never reached the screen on a successful call.
'
' The cause was a VB scoping trap. SpikeViewModel.ShowSuccess took a parameter
' named "correlationId" and assigned "CorrelationId = correlationId". VB
' identifiers are case-insensitive, so the parameter SHADOWED the property and
' the statement assigned the parameter to itself. A no-op that reads exactly
' like a working assignment. Option Strict On cannot catch it - self-assignment
' is legal - and the build reported zero warnings.
'
' Two symptoms, one cause, and they looked contradictory in the field:
'   * With a failed call first, ShowFailure (no shadowing parameter in scope)
'     set the property correctly, and every later success left it FROZEN at the
'     failed call's ID.
'   * With no failed call, nothing ever set it, so the correlation line did not
'     render AT ALL.
'
' Why this is worth a test rather than a one-line fix and a shrug: ADR-014 makes
' the correlation ID the identifier an operator reads off the screen and quotes
' when reporting a failure. A frozen value hands support an ID that matches no
' AuditLogs row for the call in question - worse than showing nothing, because
' it points confidently at the wrong request.

Imports System.Net
Imports System.Net.Http
Imports System.Text
Imports System.Text.Json
Imports System.Threading.Tasks
Imports Merchandising.ClientCommon.Api
Imports Merchandising.Inventory.ViewModels
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class SpikeViewModelTests

    Private Shared ReadOnly ApiRoot As New Uri("https://MERCH-HOST:8443/")

    ''' <summary>
    ''' A successful call must publish its own correlation ID to the view model.
    ''' This is the direct regression test: before the fix, CorrelationId stayed
    ''' String.Empty here no matter how many successful calls were made.
    ''' </summary>
    <TestMethod>
    Public Async Function SuccessfulCall_PublishesItsCorrelationIdToTheViewModel() As Task

        Using handler As New StubHttpMessageHandler(AddressOf RespondOk)
            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Dim viewModel As New SpikeViewModel(client)
                viewModel.Username = "clerk"

                Await viewModel.SignInAsync("password")

                Assert.IsFalse(String.IsNullOrWhiteSpace(viewModel.CorrelationId),
                               "A successful sign-in left the displayed correlation ID empty. " &
                               "ADR-014 makes this the identifier an operator quotes when reporting " &
                               "a failure; an empty value means there is nothing to quote.")

                Assert.IsTrue(viewModel.HasCorrelationId,
                              "HasCorrelationId gates the visibility of the on-screen line. " &
                              "False here means the correlation ID does not render at all.")

            End Using
        End Using

    End Function

    ''' <summary>
    ''' Each successful call must REPLACE the displayed correlation ID with its
    ''' own. A value that lingers from an earlier call is the frozen-display
    ''' half of the same defect.
    ''' </summary>
    <TestMethod>
    Public Async Function EachSuccessfulCall_ReplacesThePreviousCorrelationId() As Task

        Using handler As New StubHttpMessageHandler(AddressOf RespondOk)
            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Dim viewModel As New SpikeViewModel(client)
                viewModel.Username = "clerk"

                Await viewModel.SignInAsync("password")
                Dim afterSignIn As String = viewModel.CorrelationId

                Await viewModel.CallProtectedEndpointAsync()
                Dim afterFirstCall As String = viewModel.CorrelationId

                Await viewModel.CallProtectedEndpointAsync()
                Dim afterSecondCall As String = viewModel.CorrelationId

                Assert.AreNotEqual(afterSignIn, afterFirstCall,
                                   "The displayed correlation ID did not change after a second " &
                                   "successful call - it is showing a stale value from an earlier request.")

                Assert.AreNotEqual(afterFirstCall, afterSecondCall,
                                   "Two successive calls displayed the same correlation ID. " &
                                   "Each request carries its own on the wire, so the screen is stale.")

            End Using
        End Using

    End Function

    ''' <summary>
    ''' The failure path already worked, and must keep working - it is the half
    ''' that masked the defect in the field, because a failed call left a
    ''' plausible-looking ID on screen that later successes never replaced.
    ''' </summary>
    <TestMethod>
    Public Async Function RejectedCall_StillPublishesItsCorrelationId() As Task

        Using handler As New StubHttpMessageHandler(
            Function(request)
                If request.RequestUri.AbsolutePath.EndsWith("login", StringComparison.Ordinal) Then
                    Return Json(HttpStatusCode.Unauthorized, New With {
                        .errorCode = "INVALID_CREDENTIALS",
                        .message = "The username or password is incorrect.",
                        .correlationId = Guid.NewGuid().ToString("D")
                    })
                End If
                Return Json(HttpStatusCode.OK, MePayload())
            End Function)

            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Dim viewModel As New SpikeViewModel(client)
                viewModel.Username = "clerk"

                Await viewModel.SignInAsync("wrong-password")

                Assert.IsFalse(String.IsNullOrWhiteSpace(viewModel.CorrelationId),
                               "A rejection must carry the ID an operator quotes.")

            End Using
        End Using

    End Function

    ''' <summary>
    ''' An unreachable API must CLEAR the correlation ID rather than leave the
    ''' previous call's value on screen. The client never reached the server, so
    ''' there is no server-side record for any ID to match - displaying one
    ''' would invite an operator to quote an identifier for a request that was
    ''' never made.
    ''' </summary>
    <TestMethod>
    Public Async Function UnreachableCall_ClearsTheCorrelationIdRatherThanLeavingAStaleOne() As Task

        Dim failNext As Boolean = False

        Using handler As New StubHttpMessageHandler(
            Function(request)
                If failNext Then
                    Throw New HttpRequestException("No connection could be made because the target machine actively refused it.")
                End If
                Return Json(HttpStatusCode.OK, If(request.RequestUri.AbsolutePath.EndsWith("login", StringComparison.Ordinal),
                                                  LoginPayload(),
                                                  MePayload()))
            End Function)

            Using client As New MerchandisingApiClient(ApiRoot, handler, TimeSpan.FromSeconds(5))

                Dim viewModel As New SpikeViewModel(client)
                viewModel.Username = "clerk"

                Await viewModel.SignInAsync("password")
                Assert.IsFalse(String.IsNullOrWhiteSpace(viewModel.CorrelationId),
                               "Precondition: the successful sign-in should have published an ID.")

                failNext = True
                Await viewModel.CallProtectedEndpointAsync()

                Assert.AreEqual(String.Empty, viewModel.CorrelationId,
                                "An unreachable API left the previous call's correlation ID on screen. " &
                                "Nothing reached the server, so there is no matching record to quote.")

                Assert.IsFalse(viewModel.HasCorrelationId,
                               "The correlation line must not render when the server was never reached.")

            End Using
        End Using

    End Function

    ' ---------------------------------------------------------------- helpers

    Private Shared Function RespondOk(request As HttpRequestMessage) As HttpResponseMessage

        If request.RequestUri.AbsolutePath.EndsWith("login", StringComparison.Ordinal) Then
            Return Json(HttpStatusCode.OK, LoginPayload())
        End If

        Return Json(HttpStatusCode.OK, MePayload())

    End Function

    Private Shared Function LoginPayload() As Object

        Return New With {
            .token = "P1-15-VM-FIXTURE-TOKEN",
            .expiresAtUtc = DateTime.UtcNow.AddHours(8),
            .username = "clerk",
            .roles = New String() {"InventoryClerk"}
        }

    End Function

    Private Shared Function MePayload() As Object

        Return New With {
            .username = "clerk",
            .roles = New String() {"InventoryClerk"}
        }

    End Function

    ''' <summary>
    ''' Builds a response. Deliberately does NOT set an X-Correlation-Id header:
    ''' the client falls back to the ID it sent, so these tests exercise the
    ''' fallback path and still see a distinct value per request.
    ''' </summary>
    Private Shared Function Json(status As HttpStatusCode, payload As Object) As HttpResponseMessage

        Dim body As String = JsonSerializer.Serialize(payload, payload.GetType())

        Return New HttpResponseMessage(status) With {
            .Content = New StringContent(body, Encoding.UTF8, "application/json")
        }

    End Function

End Class
