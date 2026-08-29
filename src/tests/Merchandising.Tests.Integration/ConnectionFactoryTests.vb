Imports System.Data
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

    ''' <summary>
    ''' P4-04/CARRY-03: the session-level assertion above is truthful but not
    ''' sufficient - it never proved what P3-03 measured, that MySqlConnector's
    ''' `BeginTransaction` sends its OWN `SET TRANSACTION ISOLATION LEVEL` for
    ''' the transaction it opens, overriding the session-level setting this
    ''' factory issues. Both halves are asserted here: a transaction opened
    ''' with no isolation level argument reports `REPEATABLE-READ` from
    ''' INSIDE itself (the bug every unfixed call site still had, and the
    ''' reason ADR-006's own default-isolation warning exists), and a
    ''' transaction opened with <see cref="IsolationLevel.ReadCommitted"/>
    ''' passed explicitly reports `READ-COMMITTED` (the fix every call site
    ''' now applies, per ADR-006's amendment).
    ''' </summary>
    <TestMethod>
    Public Async Function OpenedConnection_TransactionIsolation_MatchesWhatIsPassedToBeginTransaction() As Task

        Dim options As DatabaseOptions = DatabaseOptionsLoader.Load()
        Dim factory As New ConnectionFactory(options)

        Using connection As MySqlConnection = Await factory.CreateOpenConnectionAsync()

            Using defaultTransaction As MySqlTransaction = Await connection.BeginTransactionAsync()
                Dim defaultIsolation As String = Await ScalarAsync(connection, "SELECT @@tx_isolation;", defaultTransaction)
                Assert.AreEqual("REPEATABLE-READ", defaultIsolation,
                    "BeginTransaction with no isolation level argument must NOT inherit the session-level READ-COMMITTED setting - this is the divergence P3-03 measured and CARRY-03 tracks.")
                Await defaultTransaction.RollbackAsync()
            End Using

            Using explicitTransaction As MySqlTransaction = Await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted)
                Dim explicitIsolation As String = Await ScalarAsync(connection, "SELECT @@tx_isolation;", explicitTransaction)
                Assert.AreEqual("READ-COMMITTED", explicitIsolation,
                    "BeginTransaction(IsolationLevel.ReadCommitted) must report READ-COMMITTED from INSIDE the transaction, not just on the session.")
                Await explicitTransaction.RollbackAsync()
            End Using

        End Using

    End Function

    Private Shared Async Function ScalarAsync(connection As MySqlConnection, sql As String, Optional transaction As MySqlTransaction = Nothing) As Task(Of String)
        Using command As MySqlCommand = connection.CreateCommand()
            command.CommandText = sql
            command.Transaction = transaction
            Dim result As Object = Await command.ExecuteScalarAsync()
            Return result.ToString()
        End Using
    End Function

End Class
