' Merchandising.Tests.Integration.WindowsServiceInfoTests
'
' P1-16: the service's identity is declared in exactly one place, and the
' install script is held to it.
'
' THE BUG CLASS THIS EXISTS TO CATCH, because it is not obvious. The service
' name appears in four places that must agree, and nothing in the compiler or
' the shell relates them:
'
'   1. sc.exe create <name>            - what Windows registers
'   2. the Event Log source            - what Kestrel's logs are written under
'   3. NT SERVICE\<name>               - the virtual account whose SID gets the
'                                        ACL grant on the config directory
'   4. Get-Service <name>              - what the post-reboot health check asks
'                                        for, and what the bootstrap reports
'
' Rename the service in the script alone and every one of those drifts apart
' silently. The service still starts. The ACL grant still exists - for an
' account nothing runs as any more, so the API cannot read database.json and
' fails at boot on a machine the author has never seen. Event Log entries
' appear under a source nobody looks at. And the reboot check reports a
' missing service that is in fact running under a different name.
'
' Under ADR-012 that mis-edit happens on a classmate's laptop during setup,
' with no author present, which is exactly the class of failure the handover
' cannot absorb. So the string lives in Visual Basic, and this test asserts
' the PowerShell agrees with it.
'
' Deliberately a file-content assertion rather than anything cleverer. The
' install script must remain readable and runnable on a machine with no build
' tools, so it cannot import the constant at run time. Comparing the text is
' the only mechanism available, and a crude mechanism that runs on every build
' beats an elegant one that does not exist.

' TWO TESTS WERE WRITTEN HERE AND DELETED BEFORE THIS FILE LANDED. Do not add
' them back. They asserted that AccountName equals "NT SERVICE\" & ServiceName,
' and that DisplayName reads as prose. Both compare Const values, so the
' compiler folds them and MSTEST0032 correctly reported the conditions as
' known-always-true: they cannot fail at run time, because the constant IS the
' expression they check. A test that cannot fail is worse than no test - it
' occupies a name in the run output that implies coverage nobody has.
'
' The invariant they were reaching for is real, and it is enforced where it can
' actually break: WindowsServiceInfo composes AccountName from ServiceName
' rather than repeating it, and the two surviving tests below check the thing
' that genuinely drifts - a PowerShell file the compiler never sees.
Imports System.IO
Imports Merchandising.Api.Hosting
Imports Microsoft.VisualStudio.TestTools.UnitTesting

''' <summary>
''' P1-16: <c>scripts/install-service.ps1</c> must name the same service that
''' <see cref="WindowsServiceInfo"/> does.
''' </summary>
<TestClass>
Public Class WindowsServiceInfoTests

    ''' <summary>
    ''' Walks up from the test assembly until it finds the repository root,
    ''' identified by <c>CLAUDE.md</c> - the same marker
    ''' <c>scripts/publish-release.ps1</c> uses to validate its own
    ''' <c>-RepoRoot</c>.
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
            "'. This test reads scripts/install-service.ps1 from the working tree.")
        Return Nothing

    End Function

    Private Shared Function ReadInstallScript() As String

        Dim scriptPath As String =
            Path.Combine(FindRepositoryRoot().FullName, "scripts", "install-service.ps1")

        Assert.IsTrue(
            File.Exists(scriptPath),
            "scripts/install-service.ps1 is missing. P1-16 owes an install script, not a " &
            "documented procedure: under ADR-012 three people who did not build this system " &
            "install it on machines the author does not own.")

        Return File.ReadAllText(scriptPath)

    End Function

    ''' <summary>
    ''' The service name the script registers must be the compiled-in one.
    ''' </summary>
    <TestMethod>
    Public Sub InstallScript_RegistersTheServiceNameTheApiCompilesIn()

        Dim script As String = ReadInstallScript()

        Assert.IsTrue(
            script.Contains("'" & WindowsServiceInfo.ServiceName & "'", StringComparison.Ordinal),
            "scripts/install-service.ps1 does not contain the service name '" &
            WindowsServiceInfo.ServiceName & "' declared by WindowsServiceInfo.ServiceName. " &
            "If the service was deliberately renamed, change the Visual Basic constant and let " &
            "this test drive the script - not the other way round.")

    End Sub

    ''' <summary>
    ''' The install script must grant config-directory access to that same
    ''' account. This is the grant without which the API starts, fails to read
    ''' database.json, and stops - on someone else's machine.
    ''' </summary>
    <TestMethod>
    Public Sub InstallScript_GrantsConfigAccessToTheServiceAccount()

        Dim script As String = ReadInstallScript()

        Assert.IsTrue(
            script.Contains(WindowsServiceInfo.AccountName, StringComparison.Ordinal),
            "scripts/install-service.ps1 never mentions '" & WindowsServiceInfo.AccountName &
            "'. The config directory ACL is SYSTEM/Administrators only (P0-07), so without an " &
            "explicit grant the service cannot read database.json or the certificate.")

    End Sub

End Class
