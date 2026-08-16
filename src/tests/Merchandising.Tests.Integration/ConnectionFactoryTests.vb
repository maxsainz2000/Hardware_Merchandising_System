Imports Merchandising.Infrastructure.Data
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports MySqlConnector

''' <summary>
''' P1-05 evidence: <see cref="ConnectionFactory"/> against the real, pinned
''' MariaDB instance (ADR-000, ADR-002, ADR-009) - never a substitute.
''' </summary>
''' <remarks>
''' Requires the host configuration file at
''' <see cref="DatabaseOptionsLoader.DefaultConfigPath"/> to exist, which is
''' this machine's dev setup from P1-05, not something committed to the
''' repository. See docs/installation-guide.md.
''' </remarks>
<TestClass>
Public Class ConnectionFactoryTests

    ''' <summary>
    ''' A connection opened through the factory must actually reach
    ''' MariaDB, and must carry both session-level settings this card is
    ''' responsible for: STRICT_TRANS_TABLES present in sql_mode
    ''' (ADR-003.2, belt-and-braces with the P1-04 my.ini change) and
    ''' tx_isolation exactly READ-COMMITTED (ADR-006).
    ''' </summary>
    <TestMethod>
    Public Async Function OpenedConnection_HasStrictModeAndReadCommittedIsolation() As Task

        Dim options As DatabaseOptions = DatabaseOptionsLoader.Load()
        Dim factory As New ConnectionFactory(options)

        Using connection As MySqlConnection = Await factory.CreateOpenConnectionAsync()

            Assert.AreEqual(System.Data.ConnectionState.Open, connection.State)

            Dim sqlMode As String = Await ScalarAsync(connection, "SELECT @@SESSION.sql_mode;")
            StringAssert.Contains(sqlMode, "STRICT_TRANS_TABLES")

            Dim isolation As String = Await ScalarAsync(connection, "SELECT @@SESSION.tx_isolation;")
            Assert.AreEqual("READ-COMMITTED", isolation)

        End Using

    End Function

    Private Shared Async Function ScalarAsync(connection As MySqlConnection, sql As String) As Task(Of String)
        Using command As MySqlCommand = connection.CreateCommand()
            command.CommandText = sql
            Dim result As Object = Await command.ExecuteScalarAsync()
            Return result.ToString()
        End Using
    End Function

End Class
