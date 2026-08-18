' Merchandising.Maintenance.Migrations.MigrationDiscovery
'
' Discovers "NNNN_description.sql" files under a migrations directory,
' sorted in apply order. Numbering is a 4-digit ordinal prefix, per ADR-008
' and the P1-06 task card.

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions

Namespace Migrations

    Public NotInheritable Class MigrationDiscovery

        Private Shared ReadOnly FileNamePattern As New Regex(
            "^(?<number>\d{4})_.+\.sql$", RegexOptions.Compiled)

        ''' <summary>
        ''' Scans <paramref name="migrationsDirectory"/> for migration files
        ''' and returns them ordered by their numeric prefix.
        ''' </summary>
        ''' <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
        ''' <exception cref="InvalidOperationException">
        ''' A file does not match the "NNNN_description.sql" naming pattern,
        ''' or two files share the same 4-digit number.
        ''' </exception>
        Public Shared Function Discover(migrationsDirectory As String) As IReadOnlyList(Of MigrationFile)

            If Not Directory.Exists(migrationsDirectory) Then
                Throw New DirectoryNotFoundException(
                    $"Migrations directory not found at '{migrationsDirectory}'.")
            End If

            Dim files As New List(Of MigrationFile)
            Dim seenNumbers As New HashSet(Of String)(StringComparer.Ordinal)

            For Each filePath As String In Directory.GetFiles(migrationsDirectory, "*.sql")

                Dim fileName As String = Path.GetFileName(filePath)
                Dim match As Match = FileNamePattern.Match(fileName)

                If Not match.Success Then
                    Throw New InvalidOperationException(
                        $"Migration file '{fileName}' does not match the required " &
                        "'NNNN_description.sql' naming pattern.")
                End If

                Dim number As String = match.Groups("number").Value

                If Not seenNumbers.Add(number) Then
                    Throw New InvalidOperationException(
                        $"Duplicate migration number '{number}' detected in '{migrationsDirectory}'.")
                End If

                Dim identifier As String = Path.GetFileNameWithoutExtension(fileName)
                Dim sql As String = File.ReadAllText(filePath)
                Dim checksum As String = ChecksumCalculator.ComputeSha256Hex(filePath)

                files.Add(New MigrationFile(identifier, filePath, sql, checksum))

            Next

            Return files.OrderBy(Function(f) f.Identifier, StringComparer.Ordinal).ToList()

        End Function

    End Class

End Namespace
