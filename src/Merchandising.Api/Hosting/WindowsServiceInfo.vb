' Merchandising.Api.Hosting.WindowsServiceInfo
'
' P1-16 / spec section 6.4: the one place this service's identity is written
' down.
'
' WHY A CONSTANT AND NOT A LITERAL IN THE INSTALL SCRIPT. The same name has to
' agree in four places that nothing relates to each other: the name sc.exe
' registers, the Event Log source the API writes under, the NT SERVICE virtual
' account whose SID receives the config-directory ACL grant, and the name the
' post-reboot health check asks Get-Service for. A rename in one place leaves
' the others silently wrong - the service starts, then cannot read
' database.json, on a machine ADR-012 says the author will never see.
'
' WindowsServiceInfoTests holds scripts/install-service.ps1 to these values, so
' the drift is a failing build rather than a failed setup.

Namespace Hosting

    ''' <summary>
    ''' Identity of the Windows Service this API is installed as.
    ''' </summary>
    Public NotInheritable Class WindowsServiceInfo

        ''' <summary>
        ''' The service key name - what <c>sc.exe create</c> registers and what
        ''' <c>Get-Service</c> is asked for.
        ''' </summary>
        ''' <remarks>
        ''' No spaces, no punctuation: this string is also embedded in the
        ''' virtual account name below, and <c>sc.exe</c>'s own argument syntax
        ''' (<c>obj= "..."</c>) makes a space here a source of quoting bugs on
        ''' someone else's machine.
        ''' </remarks>
        Public Const ServiceName As String = "MerchandisingApi"

        ''' <summary>
        ''' What a person reads in <c>services.msc</c>.
        ''' </summary>
        Public Const DisplayName As String = "Merchandising System API"

        ''' <summary>
        ''' Shown in the service's properties page. Written for whoever is
        ''' looking at this list because something is wrong.
        ''' </summary>
        Public Const Description As String =
            "Serves the Merchandising System API over HTTPS on port 8443 for the " &
            "Procurement, Inventory and POS clients. The store's database is reachable " &
            "only through this service."

        ''' <summary>
        ''' The account the service runs as: a virtual service account, created
        ''' implicitly by Windows when the service is registered.
        ''' </summary>
        ''' <remarks>
        ''' Chosen over LocalSystem and NetworkService deliberately. It has no
        ''' password to store, hand over, or rotate; it is not an administrator,
        ''' which is what spec section 17's "restricted service identity where
        ''' practical" asks for; and it carries its own SID, so the ACL grant on
        ''' the configuration directory names this service alone rather than
        ''' every service on the host that happens to share a built-in account.
        '''
        ''' Composed from <see cref="ServiceName"/> rather than typed out, so a
        ''' rename cannot leave the grant pointing at an account nothing runs as.
        ''' </remarks>
        Public Const AccountName As String = "NT SERVICE\" & ServiceName

        ''' <summary>
        ''' The Event Log source name. Same as the service name, and that is
        ''' the point: an operator who found the service in
        ''' <c>services.msc</c> can find its log without being told a second
        ''' name.
        ''' </summary>
        Public Const EventLogSourceName As String = ServiceName

        ''' <summary>
        ''' Not constructible - this type is a set of names, not state.
        ''' </summary>
        Private Sub New()
        End Sub

    End Class

End Namespace
