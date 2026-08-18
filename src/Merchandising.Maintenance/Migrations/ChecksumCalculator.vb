' Merchandising.Maintenance.Migrations.ChecksumCalculator
'
' Computes the SHA-256 checksum used to detect a tampered, already-applied
' migration file (ADR-008, stop condition 6 in CLAUDE.md: applied migrations
' are immutable).

Imports System.IO
Imports System.Security.Cryptography

Namespace Migrations

    Public NotInheritable Class ChecksumCalculator

        ''' <summary>Lower-case hex SHA-256 digest of the file's raw bytes.</summary>
        Public Shared Function ComputeSha256Hex(filePath As String) As String
            Dim bytes As Byte() = File.ReadAllBytes(filePath)
            Dim hash As Byte() = SHA256.HashData(bytes)
            Return Convert.ToHexString(hash).ToLowerInvariant()
        End Function

    End Class

End Namespace
