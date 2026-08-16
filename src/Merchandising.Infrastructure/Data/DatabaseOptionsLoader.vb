' Merchandising.Infrastructure.Data.DatabaseOptionsLoader
'
' Reads DatabaseOptions from the ACL-protected host configuration file.
' Kept separate from DatabaseOptions itself (CLAUDE.md section 10: one type
' per file) because this type owns file I/O and error handling; the options
' type stays a plain data holder.

Imports System.IO
Imports System.Text.Json

Namespace Data

    ''' <summary>
    ''' Loads <see cref="DatabaseOptions"/> from the host configuration file.
    ''' </summary>
    Public NotInheritable Class DatabaseOptionsLoader

        ''' <summary>
        ''' Default host configuration path. Outside the repository and
        ''' outside the published binaries on purpose - see
        ''' docs/installation-guide.md for the ACL this directory must carry.
        ''' </summary>
        Public Shared ReadOnly DefaultConfigPath As String =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MerchandisingSystem", "config", "database.json")

        ''' <summary>
        ''' Reads and parses the host configuration file at
        ''' <paramref name="configPath"/>.
        ''' </summary>
        ''' <param name="configPath">
        ''' Path to the JSON configuration file. Defaults to
        ''' <see cref="DefaultConfigPath"/>.
        ''' </param>
        ''' <exception cref="FileNotFoundException">
        ''' The configuration file does not exist. Thrown rather than
        ''' falling back to a default connection, because a missing
        ''' configuration file must fail loudly, not connect somewhere
        ''' unintended.
        ''' </exception>
        Public Shared Function Load(Optional configPath As String = Nothing) As DatabaseOptions

            Dim resolvedPath As String = If(configPath, DefaultConfigPath)

            If Not File.Exists(resolvedPath) Then
                Throw New FileNotFoundException(
                    $"Database configuration file not found at '{resolvedPath}'. " &
                    "See docs/installation-guide.md for how to create it.",
                    resolvedPath)
            End If

            Dim json As String = File.ReadAllText(resolvedPath)

            Dim options As DatabaseOptions = JsonSerializer.Deserialize(Of DatabaseOptions)(json)

            If options Is Nothing Then
                Throw New InvalidOperationException(
                    $"Database configuration file at '{resolvedPath}' parsed to no data.")
            End If

            Return options

        End Function

    End Class

End Namespace
