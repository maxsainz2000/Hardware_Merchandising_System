' Merchandising.Infrastructure.Security.CertificateOptionsLoader
'
' Reads CertificateOptions from the ACL-protected host configuration file.
' Mirrors Merchandising.Infrastructure.Data.DatabaseOptionsLoader exactly -
' same directory, same failure mode, same reasoning: a missing configuration
' file must fail the process at boot, not serve traffic over a fallback
' nobody chose. See that type's comment for the full rationale.

Imports System.IO
Imports System.Text.Json

Namespace Security

    ''' <summary>
    ''' Loads <see cref="CertificateOptions"/> from the host configuration file.
    ''' </summary>
    Public NotInheritable Class CertificateOptionsLoader

        ''' <summary>
        ''' Default host configuration path. Same directory as
        ''' <c>database.json</c> and <c>database.migrator.json</c>, so it
        ''' inherits the ACL already applied there rather than needing a
        ''' second lockdown step. See docs/installation-guide.md.
        ''' </summary>
        Public Shared ReadOnly DefaultConfigPath As String =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MerchandisingSystem", "config", "certificate.json")

        ''' <summary>
        ''' Reads and parses the host certificate configuration file at
        ''' <paramref name="configPath"/>.
        ''' </summary>
        ''' <param name="configPath">
        ''' Path to the JSON configuration file. Defaults to
        ''' <see cref="DefaultConfigPath"/>.
        ''' </param>
        ''' <exception cref="FileNotFoundException">
        ''' The configuration file does not exist. Thrown rather than
        ''' falling back to an unconfigured or ephemeral development
        ''' certificate, because that would silently change what a client is
        ''' being asked to trust between runs.
        ''' </exception>
        Public Shared Function Load(Optional configPath As String = Nothing) As CertificateOptions

            Dim resolvedPath As String = If(configPath, DefaultConfigPath)

            If Not File.Exists(resolvedPath) Then
                Throw New FileNotFoundException(
                    $"Certificate configuration file not found at '{resolvedPath}'. " &
                    "Run scripts/create-dev-certificate.ps1, or see docs/installation-guide.md.",
                    resolvedPath)
            End If

            Dim json As String = File.ReadAllText(resolvedPath)

            Dim options As CertificateOptions = JsonSerializer.Deserialize(Of CertificateOptions)(json)

            If options Is Nothing Then
                Throw New InvalidOperationException(
                    $"Certificate configuration file at '{resolvedPath}' parsed to no data.")
            End If

            If String.IsNullOrWhiteSpace(options.PfxPath) OrElse Not File.Exists(options.PfxPath) Then
                Throw New FileNotFoundException(
                    $"Certificate configuration at '{resolvedPath}' points at a .pfx that does not exist: '{options.PfxPath}'.",
                    options.PfxPath)
            End If

            Return options

        End Function

    End Class

End Namespace
