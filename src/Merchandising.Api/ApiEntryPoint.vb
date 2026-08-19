' Merchandising.Api.ApiEntryPoint
'
' A type that exists only to be named as WebApplicationFactory's TEntryPoint,
' and a Visual Basic gap worth recording rather than working around silently.
'
' THE PROBLEM. Every C# example writes:
'
'     WebApplicationFactory<Program>
'
' In C#, Program is a class. In Visual Basic the entry point is a MODULE
' (CLAUDE.md section 3 - top-level statements do not exist in VB, so
' Module Program / Sub Main is the only shape available). A VB Module compiles
' to a sealed class with a [StandardModule] attribute, and the VB compiler
' refuses to let you name it as a type:
'
'     BC30371: Module 'Program' cannot be used as a type
'
' Confirmed at P1-19 by trying it, not assumed. The error is a language rule,
' not a missing reference - changing Program's visibility does not help, and
' it is already Public.
'
' THE FIX. WebApplicationFactory uses TEntryPoint for one purpose: to find the
' assembly whose entry point should build the host, and to resolve the content
' root. It does not call anything on the type. So any public class in this
' assembly serves, and a purpose-named empty one is clearer than borrowing a
' controller - which is the other common workaround, and which quietly makes
' the test seam depend on a controller nobody may delete.
'
' This type is never instantiated and has no members. Deleting it breaks
' Merchandising.Tests.Integration.MerchandisingApiFactory and nothing else.

''' <summary>
''' Marker type naming this assembly as the API host for
''' <c>WebApplicationFactory(Of TEntryPoint)</c>. Never instantiated.
''' </summary>
Public NotInheritable Class ApiEntryPoint

    ''' <summary>Not constructible - this type carries no behaviour.</summary>
    Private Sub New()
    End Sub

End Class
