' Merchandising.Api.Middleware.AuditPipelineFilter
'
' P2-04: the mechanism half of "audit becomes one server-side pipeline
' component". Registered as a GLOBAL MVC action filter (Program.vb), so it
' wraps every controller action without any per-controller wiring.
'
' WHY A FILTER, NOT RAW MIDDLEWARE, DESPITE LIVING IN THE Middleware FOLDER.
' A middleware component that inspects context.Response after `Await
' _next(context)` is too late here: for an ordinary controller action
' (Ok(...), Conflict(...), etc.), the response body has already been written
' to the wire by the time control returns to an outer middleware, so
' HasStarted is already True and the response cannot be replaced -
' ExceptionHandlingMiddleware only gets away with this because an exception
' propagates BEFORE the result is executed. IAsyncActionFilter's
' OnActionExecutionAsync runs strictly between "the action method returned a
' result object" and "that result object is executed against the response",
' which is exactly the window this needs. It is still a pipeline component in
' the sense the task card means - VB/ASP.NET Core just has two request
' pipelines (middleware and MVC filters), and this is the one that can
' actually swap a result before it is sent.
'
' WHAT THIS DOES AND DOES NOT ENFORCE.
' Only 2xx (successful) results are checked. A denial - 401, 403, 409, a
' validation 400 - is not required to have audited, because most of this
' API's denial paths (insufficient stock, maintenance-already-active, a
' malformed request) are not privileged actions with an actor/result worth
' recording, and several already-correct call sites (LoginFailed,
' AccountLocked, MaintenanceReleaseRefused) DO audit certain denials by
' choice, not by this filter's compulsion. Requiring every non-2xx path to
' audit as well would be a second, larger design decision this card's
' done-when boxes do not ask for - see the P2-04 task card's own scope.
'
' WHAT "REJECTED" MEANS HERE. The underlying operation may already have
' committed (the audit gap this exists to catch is "it succeeded and forgot
' to audit", not "it is about to succeed"). Converting the response to a 500
' after the fact is still correct: CLAUDE.md's audit requirement is a
' security/compliance guarantee, not a convenience, and a caller seeing a
' loud, correlatable failure is strictly better than a silent audit gap that
' nothing else in the system will ever surface. In practice this fires only
' when a future endpoint is wired up without calling AuditLogWriter, which
' AuditPipelineTests' coverage sweep and rejection test both catch long
' before any real request reaches it.

Imports System.Text.Json
Imports System.Threading.Tasks
Imports Merchandising.Contracts.Errors
Imports Merchandising.Domain.Security
Imports Merchandising.Infrastructure.Data
Imports Microsoft.AspNetCore.Http
Imports Microsoft.AspNetCore.Mvc
Imports Microsoft.AspNetCore.Mvc.Filters

Namespace Middleware

    Public NotInheritable Class AuditPipelineFilter
        Implements IAsyncActionFilter

        ''' <summary>Stable error code for a sensitive action that completed without declaring an audit intent.</summary>
        Public Const ErrorCode As String = "AUDIT_NOT_RECORDED"

        Public Async Function OnActionExecutionAsync(
            context As ActionExecutingContext,
            nextDelegate As ActionExecutionDelegate) As Task Implements IAsyncActionFilter.OnActionExecutionAsync

            Dim actionNeedsAudit As Boolean = IsAuditRequired(context)

            If Not actionNeedsAudit Then
                Await nextDelegate()
                Return
            End If

            Using AuditLogWriter.BeginDeclarationScope()

                Dim executedContext As ActionExecutedContext = Await nextDelegate()

                ' An exception already means no result was produced here;
                ' ExceptionHandlingMiddleware owns turning that into a
                ' response, and this filter has nothing to add or replace.
                If executedContext.Exception IsNot Nothing Then
                    Return
                End If

                If IsSuccessResult(executedContext.Result) AndAlso Not AuditLogWriter.Declared Then

                    executedContext.Result = New ObjectResult(New ApiErrorResponse With {
                        .ErrorCode = ErrorCode,
                        .Message =
                            "This request completed without recording an audit entry, which this action requires. " &
                            "Report this as a defect and quote the correlation ID.",
                        .CorrelationId = executedContext.HttpContext.GetCorrelationId()
                    }) With {.StatusCode = StatusCodes.Status500InternalServerError}

                End If

            End Using

        End Function

        Private Shared Function IsAuditRequired(context As ActionExecutingContext) As Boolean

            For Each metadataItem As Object In context.ActionDescriptor.EndpointMetadata
                If TypeOf metadataItem Is AuditRequiredAttribute Then
                    Return True
                End If
            Next

            Return False

        End Function

        ''' <summary>
        ''' True for any 2xx result. Covers both result shapes this API
        ''' returns: <see cref="ObjectResult"/> (<c>Ok(...)</c>,
        ''' <c>Conflict(...)</c>, ...), whose <c>StatusCode</c> is nullable
        ''' and defaults to 200 when unset, and <see cref="StatusCodeResult"/>
        ''' (<c>NoContent()</c>, a bare <c>Ok()</c>), whose <c>StatusCode</c>
        ''' is always set.
        ''' </summary>
        Private Shared Function IsSuccessResult(result As IActionResult) As Boolean

            Dim statusCode As Integer? = Nothing

            Dim objectResult As ObjectResult = TryCast(result, ObjectResult)
            If objectResult IsNot Nothing Then
                statusCode = objectResult.StatusCode
            Else
                Dim statusCodeResult As StatusCodeResult = TryCast(result, StatusCodeResult)
                If statusCodeResult IsNot Nothing Then
                    statusCode = statusCodeResult.StatusCode
                End If
            End If

            Dim effectiveStatusCode As Integer = If(statusCode, StatusCodes.Status200OK)

            Return effectiveStatusCode >= 200 AndAlso effectiveStatusCode < 300

        End Function

    End Class

End Namespace
