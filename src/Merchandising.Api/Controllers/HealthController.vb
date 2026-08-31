' Controller based endpoint, not a minimal API lambda (CLAUDE.md section 3).
' Attributes use angle brackets, each on its own line above the member.

Imports System
Imports System.Globalization
Imports System.Reflection
Imports Microsoft.AspNetCore.Authorization
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
    '''
    ''' P6-10 / CARRY-05 added <c>commitSha</c> and <c>buildTimestampUtc</c> to
    ''' that list of permitted fields, deliberately. A commit hash identifies
    ''' no person, machine, credential or path - it is already public in
    ''' every clone of this repository - and without it nothing in the system
    ''' could tell a currently-running deployment from a four-week-stale one,
    ''' which is exactly what happened at both the Phase 4 and Phase 5 gates.
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
        ''' The commit this binary was published from, and when. Resolved once
        ''' at type initialisation, from <see cref="AssemblyMetadataAttribute"/>
        ''' entries baked in at publish time by
        ''' <c>scripts/publish-release.ps1</c> via
        ''' <c>Merchandising.Api.vbproj</c> - never read from the working tree
        ''' at request time, which would always agree with itself and detect
        ''' nothing. An ordinary dev build carries the literal "unknown" for
        ''' both.
        ''' </summary>
        Private Shared ReadOnly CommitSha As String = ResolveBuildMetadata("MerchCommitSha")

        ''' <summary>See <see cref="CommitSha"/>.</summary>
        Private Shared ReadOnly BuildTimestampUtc As String = ResolveBuildMetadata("MerchBuildTimestampUtc")

        ''' <summary>
        ''' Reports that the API process is running.
        ''' </summary>
        ''' <returns>
        ''' HTTP 200 with <c>status</c>, <c>version</c>, <c>utcTime</c>,
        ''' <c>commitSha</c> and <c>buildTimestampUtc</c>, and nothing else.
        ''' </returns>
        ''' <remarks>
        ''' P2-03: <c>AllowAnonymous</c> made explicit rather than relying on
        ''' the absence of an <c>[Authorize]</c> attribute - the behavior is
        ''' unchanged (no global fallback policy is configured, so an
        ''' unattributed action was already anonymous), but
        ''' AuthorizationMatrixTests' endpoint-discovery coverage check
        ''' classifies every action as AllowAnonymous, Policy-gated, or an
        ''' explicit "authenticated, no policy" exception - this makes Health
        ''' fall into the first bucket on purpose rather than needing a
        ''' fourth, silent one.
        ''' </remarks>
        <AllowAnonymous>
        <HttpGet>
        Public Function GetHealth() As IActionResult

            Dim payload = New With {
                .status = "ok",
                .version = ProductVersion,
                .utcTime = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                .commitSha = CommitSha,
                .buildTimestampUtc = BuildTimestampUtc
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

        ''' <summary>
        ''' Reads one <c>AssemblyMetadata</c> value baked into this assembly at
        ''' publish time by <c>Merchandising.Api.vbproj</c> / P6-10.
        ''' </summary>
        ''' <param name="key">
        ''' <c>MerchCommitSha</c> or <c>MerchBuildTimestampUtc</c> - the
        ''' <c>AssemblyMetadata</c> item names declared in the project file.
        ''' </param>
        ''' <remarks>
        ''' Returns "unknown" when the attribute is absent, which is the
        ''' ordinary case for a plain <c>dotnet build</c> or <c>dotnet test</c>
        ''' that never passed <c>-p:MerchCommitSha=...</c>. A published build
        ''' always carries a real value, because
        ''' <c>Merchandising.Api.vbproj</c> defaults the MSBuild property to the
        ''' same literal when the publish script does not supply it - so this
        ''' method never has to guess whether "unknown" means "not stamped" or
        ''' "stamped as unknown"; they are the same thing.
        ''' </remarks>
        Private Shared Function ResolveBuildMetadata(key As String) As String

            Dim apiAssembly As Assembly = GetType(HealthController).Assembly

            For Each metadata As AssemblyMetadataAttribute In
                apiAssembly.GetCustomAttributes(Of AssemblyMetadataAttribute)()

                If String.Equals(metadata.Key, key, StringComparison.Ordinal) AndAlso
                   Not String.IsNullOrWhiteSpace(metadata.Value) Then

                    Return metadata.Value

                End If
            Next

            Return "unknown"

        End Function

    End Class

End Namespace
