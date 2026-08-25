' Merchandising.Domain.Security.RolePermissionMatrixFormatter
'
' Renders docs/role-permission-matrix.md from PolicyRegistry.Definitions.
' P2-02's done-when box requires the document be "generated from or verified
' against the registration, so the document cannot silently drift from the
' code" - this is the generator half. RolePermissionMatrixDocumentationTests
' (Merchandising.Tests.Unit) is the verification half: it calls Render()
' again and asserts the result equals the committed file, so a policy added
' to the registry without regenerating the document fails the suite rather
' than shipping a stale doc.
'
' Lines are joined with an explicit vbLf, never StringBuilder.AppendLine
' (which writes Environment.NewLine - CRLF on Windows, LF elsewhere). A
' clean clone on a different machine must render byte-for-byte the same
' content this file was generated with; platform-dependent line endings
' would make the equality test in RolePermissionMatrixDocumentationTests
' fail on line endings alone, not on real drift.
'
' Pure string formatting, no I/O - Domain depends on nothing (CLAUDE.md
' section 4). Whoever regenerates the committed file writes Render()'s
' result to docs/role-permission-matrix.md directly.

Namespace Security

    Public NotInheritable Class RolePermissionMatrixFormatter

        ''' <summary>
        ''' Renders the full matrix document: one section per role (spec
        ''' section 9's row order) listing every policy that role satisfies,
        ''' followed by the full policy table. Lines are joined with vbLf and
        ''' the result ends with a single trailing vbLf.
        ''' </summary>
        Public Shared Function Render(definitions As IReadOnlyList(Of PolicyDefinition)) As String

            Dim lines As New List(Of String)

            lines.Add("# Role / Permission Matrix")
            lines.Add(String.Empty)
            lines.Add("Generated from `Merchandising.Domain.Security.PolicyRegistry.Definitions` (P2-02 / ADR-017).")
            lines.Add("`RolePermissionMatrixDocumentationTests` regenerates this file's content from the same")
            lines.Add("registry and asserts it is unchanged - do not hand-edit this file. Change")
            lines.Add("`PolicyRegistry.vb` and regenerate instead.")
            lines.Add(String.Empty)
            lines.Add("Out of scope: user/role management has no HTTP policy at all. Spec section 13's API")
            lines.Add("area table has no ""Users"" row - accounts are created only through the")
            lines.Add("`Merchandising.Maintenance` CLI's `create-user` (P1-08), never over HTTP.")
            lines.Add(String.Empty)

            For Each roleName As String In New String() {
                RoleNames.SuperAdmin,
                RoleNames.Admin,
                RoleNames.ProcurementOfficer,
                RoleNames.InventoryClerk,
                RoleNames.Cashier
            }

                lines.Add($"## {roleName}")
                lines.Add(String.Empty)

                For Each definition As PolicyDefinition In definitions
                    If definition.AllowedRoles.Contains(roleName) Then
                        lines.Add($"- `{definition.PolicyName}` - {definition.Description}")
                    End If
                Next

                lines.Add(String.Empty)

            Next

            lines.Add("## Every policy")
            lines.Add(String.Empty)
            lines.Add("| Policy | Description | Allowed roles | Spec basis |")
            lines.Add("|---|---|---|---|")

            For Each definition As PolicyDefinition In definitions
                Dim roles As String = String.Join(", ", definition.AllowedRoles)
                lines.Add($"| `{definition.PolicyName}` | {definition.Description} | {roles} | {definition.SpecBasis} |")
            Next

            Return String.Join(vbLf, lines) & vbLf

        End Function

    End Class

End Namespace
