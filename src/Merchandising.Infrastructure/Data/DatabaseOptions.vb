' Merchandising.Infrastructure.Data.DatabaseOptions
'
' Binds the ACL-protected host configuration file into strongly typed
' options. The file itself lives outside the repository and outside the
' binaries, at %ProgramData%\MerchandisingSystem\config\database.json on the
' host - see docs/installation-guide.md. Nothing in this type, and no value
' it can hold, is ever written back into a committed file (CLAUDE.md
' section 4, guardrail G-C).
'
' Deserialized with reflection-based System.Text.Json, not source
' generation - the Request Delegate Generator restriction in
' Directory.Build.props applies to ASP.NET Core specifically, but source
' generators generally are a C#-only tool in this project. See CLAUDE.md
' section 3.

Imports System.Text.Json.Serialization

Namespace Data

    ''' <summary>
    ''' Connection settings for the MariaDB database, bound from the host
    ''' configuration file rather than from any committed source.
    ''' </summary>
    Public NotInheritable Class DatabaseOptions

        ''' <summary>Host name or address MySqlConnector should dial. Loopback only per ADR-002/P0-04.</summary>
        <JsonPropertyName("host")>
        Public Property Host As String = "127.0.0.1"

        ''' <summary>TCP port MariaDB listens on.</summary>
        <JsonPropertyName("port")>
        Public Property Port As Integer = 3306

        ''' <summary>Schema name, e.g. "merchandising".</summary>
        <JsonPropertyName("database")>
        Public Property Database As String = String.Empty

        ''' <summary>Least-privilege account created at P1-04, e.g. "merch_api".</summary>
        <JsonPropertyName("userId")>
        Public Property UserId As String = String.Empty

        ''' <summary>Password for <see cref="UserId"/>. Never logged, never serialized back out.</summary>
        <JsonPropertyName("password")>
        Public Property Password As String = String.Empty

    End Class

End Namespace
