Imports System.Reflection
Imports Microsoft.VisualStudio.TestTools.UnitTesting

''' <summary>
''' Placeholder suite created at P1-01 so <c>scripts/run-tests.ps1</c> has a
''' real assembly to execute before P1-19 wires the genuine harness.
''' </summary>
''' <remarks>
''' It asserts the one thing P1-01 actually delivers that is observable at
''' run time: that this project's declared references resolve to loadable
''' assemblies. That is deliberately narrow.
'''
''' It touches no database. Do not read a green run here as evidence that
''' anything works against MariaDB - the real integration suite arrives at
''' P1-19 and runs against the pinned server per ADR-000 and ADR-009.
''' Delete this class then.
'''
''' The template's generated Test1.vb was removed rather than kept: it
''' asserted nothing at all, and it nested a Namespace block inside a
''' RootNamespace of the same name, which would have produced the doubled
''' namespace Merchandising.Tests.Integration.Merchandising.Tests.Integration.
''' </remarks>
<TestClass>
Public Class ScaffoldReferenceTests

    ''' <summary>
    ''' Every project this test project references must load by name at run
    ''' time, proving the P1-01 dependency wiring is real and not merely
    ''' declared in the project file.
    ''' </summary>
    ''' <remarks>
    ''' Merchandising.Api is included deliberately. At P1-01 it is still a
    ''' plain library shell; P1-02 converts it to the Web SDK. If that
    ''' conversion ever breaks this project's ability to load it, P1-19's
    ''' WebApplicationFactory seam would break with it, and this is the
    ''' cheapest place to find that out.
    ''' </remarks>
    <TestMethod>
    Public Sub ReferencedProjectAssembliesLoad()

        Dim expected = {"Merchandising.Api",
                        "Merchandising.Infrastructure",
                        "Merchandising.Domain",
                        "Merchandising.Contracts"}

        For Each assemblyName In expected
            Dim loaded As Assembly = Assembly.Load(assemblyName)
            Assert.IsNotNull(loaded, $"Project reference '{assemblyName}' did not resolve to a loadable assembly.")
        Next

    End Sub

End Class
