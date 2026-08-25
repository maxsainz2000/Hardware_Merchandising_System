' Merchandising.Tests.Integration.AuditPipelineTests
'
' P2-04: proves both halves of "audit becomes one server-side pipeline
' component".
'
' EveryMutatingAction_DeclaresAuditRequired is the coverage half, the same
' shape as AuthorizationMatrixTests.EveryControllerAction_Declares... - it
' enumerates every action in the real API assembly and demands every
' mutating one carry [AuditRequired]. A future POST/PUT/DELETE endpoint that
' forgets the attribute fails this test by name.
'
' The AuditPipelineFilter_* tests exercise the real filter class directly
' against hand-built ActionExecutingContext/ActionExecutedContext objects -
' the same no-HTTP-round-trip technique SelfApprovalHandlerTests uses for
' SelfApprovalHandler - so the enforcement behavior (pass through when
' declared, reject when forgotten, ignore non-2xx results) is proven without
' needing a dedicated test-only controller action wired into the real
' pipeline. Two of the four call AuditLogWriter.WriteAsync for real, against
' the real pinned MariaDB, so "declared" is proven by an actual audit row,
' not a fake.
'
' DeclarationScope_ResetsBetweenScopes proves the AsyncLocal claim in
' AuditLogWriter's own header directly: a fresh scope starts undeclared, a
' real WriteAsync call declares it, and a second scope does not inherit the
' first one's declaration.

Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading.Tasks
Imports Merchandising.Api.Middleware
Imports Merchandising.Contracts.Errors
Imports Merchandising.Domain.Security
Imports Merchandising.Infrastructure.Data
Imports Merchandising.Maintenance.Users
Imports Microsoft.AspNetCore.Http
Imports Microsoft.AspNetCore.Mvc
Imports Microsoft.AspNetCore.Mvc.Abstractions
Imports Microsoft.AspNetCore.Mvc.Controllers
Imports Microsoft.AspNetCore.Mvc.Filters
Imports Microsoft.AspNetCore.Mvc.Infrastructure
Imports Microsoft.AspNetCore.Routing
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

<TestClass>
Public Class AuditPipelineTests

    Private Const MigratorConfigFileName As String = "database.migrator.json"
    Private Const FixturePassword As String = "P2-04 Fixture Passw0rd!"
    Private Const FixtureUsername As String = "p2_04_fixture_actor"

    Private _factory As MerchandisingApiFactory
    Private _connectionFactory As ConnectionFactory
    Private _actorUserId As Integer

    <TestInitialize>
    Public Async Function SetUpAsync() As Task

        _factory = New MerchandisingApiFactory()
        _connectionFactory = New ConnectionFactory(DatabaseOptionsLoader.Load())
        _actorUserId = Await EnsureFixtureUserAsync()

    End Function

    <TestCleanup>
    Public Sub TearDown()
        _factory?.Dispose()
    End Sub

    ' --------------------------------------------------------------- coverage sweep

    ''' <summary>Done-when box 4: a mutating endpoint without a declared audit intent fails the suite rather than shipping untested.</summary>
    <TestMethod>
    Public Sub EveryMutatingAction_DeclaresAuditRequired()

        Dim provider As IActionDescriptorCollectionProvider =
            _factory.Services.GetRequiredService(Of IActionDescriptorCollectionProvider)()

        Dim uncovered As New List(Of String)

        For Each descriptor As ActionDescriptor In provider.ActionDescriptors.Items

            Dim controllerAction As ControllerActionDescriptor = TryCast(descriptor, ControllerActionDescriptor)
            If controllerAction Is Nothing Then
                Continue For
            End If

            Dim httpMethods As List(Of String) =
                descriptor.EndpointMetadata.OfType(Of HttpMethodMetadata)().
                    SelectMany(Function(m) m.HttpMethods).ToList()

            Dim isMutating As Boolean = httpMethods.Any(Function(m) Not IsSafeHttpMethod(m))
            If Not isMutating Then
                Continue For
            End If

            Dim hasAuditRequired As Boolean =
                descriptor.EndpointMetadata.OfType(Of AuditRequiredAttribute)().Any()

            If Not hasAuditRequired Then
                Dim actionKey As String = $"{controllerAction.ControllerTypeInfo.FullName}.{controllerAction.ActionName}"
                uncovered.Add(
                    $"{actionKey}: mutating action ({String.Join(",", httpMethods)}) carries no [AuditRequired].")
            End If

        Next

        Assert.IsEmpty(
            uncovered,
            "Mutating action(s) not provably covered by the audit pipeline:" & vbLf & String.Join(vbLf, uncovered))

    End Sub

    Private Shared Function IsSafeHttpMethod(method As String) As Boolean

        Return String.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) OrElse
              String.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase) OrElse
              String.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase) OrElse
              String.Equals(method, "TRACE", StringComparison.OrdinalIgnoreCase)

    End Function

    ' --------------------------------------------------------------- filter enforcement

    <TestMethod>
    Public Async Function ActionWithoutAuditRequired_PassesThroughEvenWithoutAudit() As Task

        Dim scenario As FilterScenario = BuildScenario(New List(Of Object)())
        Dim producedResult As New OkObjectResult(New With {.ok = True})

        Await scenario.Filter.OnActionExecutionAsync(
            scenario.ExecutingContext,
            Function() As Task(Of ActionExecutedContext)
                scenario.ExecutedContext.Result = producedResult
                Return Task.FromResult(scenario.ExecutedContext)
            End Function)

        Assert.AreSame(producedResult, scenario.ExecutedContext.Result)

    End Function

    <TestMethod>
    Public Async Function AuditRequired_ActionDeclaresAudit_ResultPassesThroughUnmodified() As Task

        Dim scenario As FilterScenario = BuildScenario(New List(Of Object) From {New AuditRequiredAttribute()})
        Dim producedResult As New OkObjectResult(New With {.ok = True})

        Await scenario.Filter.OnActionExecutionAsync(
            scenario.ExecutingContext,
            Async Function() As Task(Of ActionExecutedContext)

                Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
                    Await AuditLogWriter.WriteAsync(
                        connection, _actorUserId, "AuditPipelineTestDeclared", "test-target", "Success", Guid.NewGuid().ToString())
                End Using

                scenario.ExecutedContext.Result = producedResult
                Return scenario.ExecutedContext

            End Function)

        Assert.AreSame(producedResult, scenario.ExecutedContext.Result)

    End Function

    <TestMethod>
    Public Async Function AuditRequired_ActionForgetsToAudit_ResultIsReplacedWith500() As Task

        Dim scenario As FilterScenario = BuildScenario(New List(Of Object) From {New AuditRequiredAttribute()})
        Dim producedResult As New OkObjectResult(New With {.ok = True})

        Await scenario.Filter.OnActionExecutionAsync(
            scenario.ExecutingContext,
            Function() As Task(Of ActionExecutedContext)
                scenario.ExecutedContext.Result = producedResult
                Return Task.FromResult(scenario.ExecutedContext)
            End Function)

        Dim replaced As ObjectResult = TryCast(scenario.ExecutedContext.Result, ObjectResult)
        Assert.IsNotNull(replaced, "A successful, undeclared [AuditRequired] action must have its result replaced.")
        Assert.AreEqual(StatusCodes.Status500InternalServerError, replaced.StatusCode)

        Dim body As ApiErrorResponse = TryCast(replaced.Value, ApiErrorResponse)
        Assert.IsNotNull(body, "The replacement result must carry the standard ApiErrorResponse envelope.")
        Assert.AreEqual(AuditPipelineFilter.ErrorCode, body.ErrorCode)
        Assert.IsFalse(String.IsNullOrWhiteSpace(body.CorrelationId))

    End Function

    ''' <summary>Denial paths (409, 401, ...) are never forced to audit - see AuditPipelineFilter's own header.</summary>
    <TestMethod>
    Public Async Function AuditRequired_ActionReturnsNonSuccessResult_NotForcedToAudit() As Task

        Dim scenario As FilterScenario = BuildScenario(New List(Of Object) From {New AuditRequiredAttribute()})
        Dim producedResult As New ObjectResult(New With {.denied = True}) With {.StatusCode = StatusCodes.Status409Conflict}

        Await scenario.Filter.OnActionExecutionAsync(
            scenario.ExecutingContext,
            Function() As Task(Of ActionExecutedContext)
                scenario.ExecutedContext.Result = producedResult
                Return Task.FromResult(scenario.ExecutedContext)
            End Function)

        Assert.AreSame(producedResult, scenario.ExecutedContext.Result)

    End Function

    ' --------------------------------------------------------------- declaration scope

    ''' <summary>Proves the AsyncLocal claim in AuditLogWriter's own header: a scope starts undeclared, a real write declares it, and the next scope does not inherit that.</summary>
    <TestMethod>
    Public Async Function DeclarationScope_ResetsBetweenScopes() As Task

        Using AuditLogWriter.BeginDeclarationScope()

            Assert.IsFalse(AuditLogWriter.Declared, "A fresh scope must start undeclared.")

            Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
                Await AuditLogWriter.WriteAsync(
                    connection, _actorUserId, "AuditPipelineTestScope", "test-target", "Success", Guid.NewGuid().ToString())
            End Using

            Assert.IsTrue(AuditLogWriter.Declared, "WriteAsync must flag the current scope as declared.")

        End Using

        Using AuditLogWriter.BeginDeclarationScope()
            Assert.IsFalse(AuditLogWriter.Declared, "A new scope must not inherit the previous scope's declaration.")
        End Using

    End Function

    ' --------------------------------------------------------------- fixtures

    Private NotInheritable Class FilterScenario
        Public Property Filter As AuditPipelineFilter
        Public Property ExecutingContext As ActionExecutingContext
        Public Property ExecutedContext As ActionExecutedContext
    End Class

    Private Shared Function BuildScenario(metadata As IList(Of Object)) As FilterScenario

        Dim actionDescriptor As New ActionDescriptor With {.EndpointMetadata = metadata}
        Dim httpContext As New DefaultHttpContext()
        Dim routeData As New RouteData()
        Dim actionContext As New ActionContext(httpContext, routeData, actionDescriptor)
        Dim filters As New List(Of IFilterMetadata)()
        Dim controllerInstance As New Object()

        Return New FilterScenario With {
            .Filter = New AuditPipelineFilter(),
            .ExecutingContext = New ActionExecutingContext(actionContext, filters, New Dictionary(Of String, Object)(), controllerInstance),
            .ExecutedContext = New ActionExecutedContext(actionContext, filters, controllerInstance)
        }

    End Function

    Private Async Function EnsureFixtureUserAsync() As Task(Of Integer)

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Dim existing As Merchandising.Domain.Entities.User =
                Await UserRepository.FindByUsernameAsync(connection, FixtureUsername)
            If existing IsNot Nothing Then
                Return existing.Id
            End If
        End Using

        Dim migratorFactory As New ConnectionFactory(LoadMigratorOptions())
        Await CreateUserCommand.RunAsync(migratorFactory, FixtureUsername, FixturePassword, "Admin")

        Using connection As MySqlConnection = Await _connectionFactory.CreateOpenConnectionAsync()
            Dim created As Merchandising.Domain.Entities.User =
                Await UserRepository.FindByUsernameAsync(connection, FixtureUsername)
            Return created.Id
        End Using

    End Function

    Private Shared Function LoadMigratorOptions() As DatabaseOptions
        Dim migratorConfigPath As String =
            IO.Path.Combine(IO.Path.GetDirectoryName(DatabaseOptionsLoader.DefaultConfigPath), MigratorConfigFileName)
        Return DatabaseOptionsLoader.Load(migratorConfigPath)
    End Function

End Class
