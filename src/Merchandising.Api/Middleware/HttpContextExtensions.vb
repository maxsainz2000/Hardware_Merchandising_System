' Merchandising.Api.Middleware.HttpContextExtensions
'
' Reads back what CorrelationIdMiddleware stamped onto the request. Falls
' back to TraceIdentifier only if the middleware was somehow skipped (it is
' registered first in Program.vb) - so a caller here never fails outright
' for want of a correlation Id, but the fallback is not expected to fire.

Imports System.Runtime.CompilerServices
Imports Microsoft.AspNetCore.Http

Namespace Middleware

    Public Module HttpContextExtensions

        <Extension()>
        Public Function GetCorrelationId(context As HttpContext) As String

            Dim value As Object = Nothing

            If context.Items.TryGetValue(CorrelationIdMiddleware.ItemsKey, value) AndAlso TypeOf value Is String Then
                Return CStr(value)
            End If

            Return context.TraceIdentifier

        End Function

    End Module

End Namespace
