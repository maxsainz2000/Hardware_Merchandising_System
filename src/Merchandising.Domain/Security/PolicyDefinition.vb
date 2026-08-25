' Merchandising.Domain.Security.PolicyDefinition
'
' Pure data, no ASP.NET Core dependency - Domain depends on nothing
' (CLAUDE.md section 4). Merchandising.Api.Security.AuthorizationPolicyRegistration
' reads PolicyRegistry.Definitions to build the real AuthorizationOptions at
' startup; RolePermissionMatrixFormatter reads the same list to render
' docs/role-permission-matrix.md. One list, two consumers, so the document
' and the enforced policy can never independently drift (P2-02).

Namespace Security

    ''' <summary>
    ''' One named authorization policy: the operation it gates, which roles
    ''' spec section 9 grants it to, and where that grant comes from.
    ''' </summary>
    Public NotInheritable Class PolicyDefinition

        ''' <summary>
        ''' Parameter names are deliberately NOT the camelCase form of the
        ''' property names (contrast <see cref="PolicyRegistry"/>'s
        ''' "collected" local, same reason): VB is case-insensitive, so
        ''' e.g. a parameter "policyName" and a property "PolicyName" are the
        ''' same identifier in the constructor body, and "PolicyName =
        ''' policyName" resolves to the parameter on both sides - a silent
        ''' self-assignment that leaves the property Nothing. Caught here by
        ''' RolePermissionMatrixDocumentationTests before it shipped: every
        ''' PolicyDefinition constructed this way had all four properties
        ''' Nothing.
        ''' </summary>
        Public Sub New(name As String, summary As String, roles As IReadOnlyList(Of String), basis As String)
            PolicyName = name
            Description = summary
            AllowedRoles = roles
            SpecBasis = basis
        End Sub

        ''' <summary>The name every <c>[Authorize(Policy:=...)]</c> attribute and <c>AddPolicy</c> call uses. Dot-separated <c>Area.Action</c> (ADR-017).</summary>
        Public ReadOnly Property PolicyName As String

        ''' <summary>One sentence, plain language - this is what renders into the matrix document.</summary>
        Public ReadOnly Property Description As String

        ''' <summary>Role names (the exact <c>Roles.Name</c> values seeded by 0001_foundation.sql, see <see cref="RoleNames"/>) that satisfy this policy.</summary>
        Public ReadOnly Property AllowedRoles As IReadOnlyList(Of String)

        ''' <summary>Spec section 9 or 13 citation this policy was decomposed from, for the matrix document and for anyone auditing the mapping later.</summary>
        Public ReadOnly Property SpecBasis As String

    End Class

End Namespace
