' Merchandising.Maintenance.Migrations.MigrationFile
'
' A single migration discovered on disk: its identifier (file name without
' extension), its full path, its SQL content, and a SHA-256 checksum of the
' file bytes as read at discovery time. See MigrationDiscovery.

Namespace Migrations

    ''' <summary>
    ''' One <c>NNNN_description.sql</c> file discovered under a migrations
    ''' directory.
    ''' </summary>
    Public NotInheritable Class MigrationFile

        Public Sub New(identifier As String, filePath As String, sql As String, checksum As String)
            Me.Identifier = identifier
            Me.FilePath = filePath
            Me.Sql = sql
            Me.Checksum = checksum
        End Sub

        ''' <summary>File name without the ".sql" extension, e.g. "0001_foundation".</summary>
        Public ReadOnly Property Identifier As String

        ''' <summary>Full path to the file on disk.</summary>
        Public ReadOnly Property FilePath As String

        ''' <summary>Raw SQL content of the file.</summary>
        Public ReadOnly Property Sql As String

        ''' <summary>SHA-256 hex digest of the file's bytes, computed at discovery time.</summary>
        Public ReadOnly Property Checksum As String

    End Class

End Namespace
