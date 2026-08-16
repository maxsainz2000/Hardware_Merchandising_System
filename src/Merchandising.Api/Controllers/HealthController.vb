' Controller based endpoint, not a minimal API lambda (CLAUDE.md section 3).
' Attributes use angle brackets, each on its own line above the member.

Imports System
Imports System.Globalization
Imports System.Reflection
Imports Microsoft.AspNetCore.Mvc

Namespace Controllers

    ''' <summary>
    ''' Non sensitive liveness probe.
    ''' </summary>
    ''' <remarks>
    ''' This is the only endpoint that does not require authentication (spec
    ''' section 13), and it is routed at <c>/health</c> rather than under the
    ''' <c>/api/v1</c> prefix, matching the Operations row of the endpoint
    ''' table in that section.
    '''
    ''' Because it is unauthenticated, WHAT IT DOES NOT RETURN MATTERS MORE
    ''' THAN WHAT IT DOES. It must never expose database status or reachability,
    ''' connection details, configuration values, the hosting environment name,
    ''' machine or user names, file paths, or an exception of any kind. Anyone
    ''' who can reach the port can read this response. If a future task needs a
    ''' deeper readiness check, it belongs behind authorization on a separate
    ''' endpoint, not added to this payload.
    ''' </remarks>
    <ApiController>
    <Route("health")>
    Public Class HealthController
        Inherits ControllerBase

        ''' <summary>
        ''' Resolved once at type initialisation. The assembly cannot change
        ''' version while the process runs, and a liveness probe should not pay
        ''' reflection cost on every call.
        ''' </summary>
        Private Shared ReadOnly ProductVersion As String = ResolveProductVersion()

        ''' <summary>
        ''' Reports that the API process is running.
        ''' </summary>
        ''' <returns>
        ''' HTTP 200 with <c>status</c>, <c>version</c> and <c>utcTime</c>, and
        ''' nothing else.
        ''' </returns>
        <HttpGet>
        Public Function GetHealth() As IActionResult

            Dim payload = New With {
                .status = "ok",
                .version = ProductVersion,
                .utcTime = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            }

            Return Ok(payload)

        End Function

        ''' <summary>
        ''' Reads the product version from this assembly rather than hard coding
        ''' it, so it cannot drift from the Version property in
        ''' Directory.Build.props.
        ''' </summary>
        ''' <remarks>
        ''' Anything from a '+' onwards is stripped. The .NET SDK appends a
        ''' source revision identifier there when one is available, and a commit
        ''' hash is internal detail with no place in an unauthenticated response.
        ''' </remarks>
        Private Shared Function ResolveProductVersion() As String

            Dim apiAssembly As Assembly = GetType(HealthController).Assembly

            Dim informational As AssemblyInformationalVersionAttribute =
                apiAssembly.GetCustomAttribute(Of AssemblyInformationalVersionAttribute)()

            If informational IsNot Nothing AndAlso
               Not String.IsNullOrWhiteSpace(informational.InformationalVersion) Then

                Dim value As String = informational.InformationalVersion

                Dim metadataStart As Integer = value.IndexOf("+"c)
                If metadataStart >= 0 Then
                    value = value.Substring(0, metadataStart)
                End If

                Return value

            End If

            ' Fallback. AssemblyInformationalVersionAttribute is emitted by the
            ' SDK for every project here, so this path is not expected to run.
            Dim assemblyVersion As Version = apiAssembly.GetName().Version
            If assemblyVersion IsNot Nothing Then
                Return assemblyVersion.ToString()
            End If

            Return "unknown"

        End Function

    End Class

End Namespace
