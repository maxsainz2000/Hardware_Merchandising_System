' Merchandising.Tests.Unit.RolePermissionMatrixDocumentationTests
'
' P2-02: docs/role-permission-matrix.md "is generated from or verified
' against the registration, so the document cannot silently drift from the
' code" - this is the verification half. RolePermissionMatrixFormatter.Render
' is the generator half; the committed file is expected to be exactly its
' output. A policy added to, removed from, or reworded in PolicyRegistry
' without regenerating the file fails this test rather than shipping a stale
' document.

Imports System.IO
Imports Merchandising.Domain.Security
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class RolePermissionMatrixDocumentationTests

    ''' <summary>
    ''' Walks up from the test assembly until it finds the repository root,
    ''' identified by <c>CLAUDE.md</c> - same marker
    ''' WindowsServiceInfoTests.FindRepositoryRoot uses in the integration
    ''' project.
    ''' </summary>
    Private Shared Function FindRepositoryRoot() As DirectoryInfo

        Dim current As DirectoryInfo = New DirectoryInfo(AppContext.BaseDirectory)

        While current IsNot Nothing
            If File.Exists(Path.Combine(current.FullName, "CLAUDE.md")) Then
                Return current
            End If
            current = current.Parent
        End While

        Assert.Fail(
            "Could not locate the repository root above '" & AppContext.BaseDirectory &
            "'. This test reads docs/role-permission-matrix.md from the working tree.")
        Return Nothing

    End Function

    ''' <summary>
    ''' Line-ending tolerant: the committed file is normalized to LF by
    ''' .gitattributes' generic `text=auto` rule, but a checkout on this
    ''' Windows host (core.autocrlf=true, per docs/adr.md's environment
    ''' pins) restores CRLF - a byte-for-byte comparison against
    ''' RolePermissionMatrixFormatter.Render's LF-only output would fail on
    ''' line endings alone after a fresh clone, not on real content drift.
    ''' </summary>
    Private Shared Function NormalizeLineEndings(value As String) As String
        Return value.Replace(vbCrLf, vbLf)
    End Function

    <TestMethod>
    Public Sub CommittedMatrixDocument_MatchesRenderedRegistry()

        Dim documentPath As String =
            Path.Combine(FindRepositoryRoot().FullName, "docs", "role-permission-matrix.md")

        Assert.IsTrue(
            File.Exists(documentPath),
            "docs/role-permission-matrix.md is missing. P2-02 owes a generated document, not a " &
            "planned one.")

        Dim committed As String = NormalizeLineEndings(File.ReadAllText(documentPath))
        Dim rendered As String = NormalizeLineEndings(RolePermissionMatrixFormatter.Render(PolicyRegistry.Definitions))

        Assert.AreEqual(
            rendered, committed,
            "docs/role-permission-matrix.md has drifted from PolicyRegistry.Definitions. " &
            "Regenerate it from RolePermissionMatrixFormatter.Render(PolicyRegistry.Definitions) " &
            "rather than hand-editing.")

    End Sub

End Class
