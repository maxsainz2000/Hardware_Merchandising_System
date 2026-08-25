' Merchandising.Domain.Security.AuditRequiredAttribute
'
' P2-04: marks a controller action as one whose successful completion MUST
' have written an AuditLogs row. Pure metadata - no ASP.NET Core dependency,
' matching PolicyRegistry's "Domain depends on nothing" rule (CLAUDE.md
' section 4). Merchandising.Api.Middleware.AuditPipelineFilter reads this at
' runtime through ActionDescriptor.EndpointMetadata, the same mechanism
' AuthorizationMatrixTests already relies on for [AllowAnonymous]/[Authorize].
'
' Every mutating (non-GET) controller action in this API carries this
' attribute today - see AuditPipelineTests'
' EveryMutatingAction_DeclaresAuditRequired. There is deliberately no
' "opt out" attribute yet: CLAUDE.md's "don't design for hypothetical future
' requirements" applies, and a genuinely audit-exempt mutation can add one
' when it exists, arguing the exemption at that point rather than in advance.

Namespace Security

    <AttributeUsage(AttributeTargets.Method, AllowMultiple:=False, Inherited:=False)>
    Public NotInheritable Class AuditRequiredAttribute
        Inherits Attribute

    End Class

End Namespace
