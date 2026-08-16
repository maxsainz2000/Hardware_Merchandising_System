Imports System.Reflection
Imports Microsoft.VisualStudio.TestTools.UnitTesting

''' <summary>
''' Placeholder suite created at P1-01 so <c>scripts/run-tests.ps1</c> has a
''' real assembly to execute before P1-19 wires the genuine harness.
''' </summary>
''' <remarks>
''' It asserts the one thing P1-01 actually delivers that is observable at
''' run time: that this project's declared references resolve to loadable
''' assemblies. That is deliberately narrow. It is NOT a test of any business
''' rule, and it must not be treated as evidence for one.
'''
''' P1-19 replaces this with the real unit suite. Delete it then.
'''
''' The template's generated Test1.vb was removed rather than kept: it
''' asserted nothing at all, and it nested a Namespace block inside a
''' RootNamespace of the same name, which would have produced the doubled
''' namespace Merchandising.Tests.Unit.Merchandising.Tests.Unit.
''' </remarks>
<TestClass>
Public Class ScaffoldReferenceTests

    ''' <summary>
    ''' Every project this test project references must load by name at run
    ''' time, proving the P1-01 dependency wiring is real and not merely
    ''' declared in the project file.
    ''' </summary>
    <TestMethod>
    Public Sub ReferencedProjectAssembliesLoad()

        Dim expected = {"Merchandising.Domain", "Merchandising.Contracts"}

        For Each assemblyName In expected
            Dim loaded As Assembly = Assembly.Load(assemblyName)
            Assert.IsNotNull(loaded, $"Project reference '{assemblyName}' did not resolve to a loadable assembly.")
        Next

    End Sub

End Class
