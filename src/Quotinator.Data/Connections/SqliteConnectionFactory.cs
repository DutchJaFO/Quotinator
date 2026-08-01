using System.Data;
using Microsoft.Data.Sqlite;

namespace Quotinator.Data.Connections;

/// <summary>SQLite implementation of <see cref="IDbConnectionFactory"/> using a file-based connection string.</summary>
public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    /// <summary>Initialises the factory with the path to the SQLite database file.</summary>
    /// <param name="dbPath">Absolute path to the <c>.db</c> file. The file is created if it does not exist.</param>
    public SqliteConnectionFactory(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
    }

    /// <inheritdoc/>
    public IDbConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);

        // Function registrations are per-connection, not global — Microsoft.Data.Sqlite loses them
        // whenever a connection closes and reopens. Registering on every Open (rather than once,
        // here, before the connection even exists) is what makes this work regardless of which
        // caller opens the connection or how many times it's reopened.
        connection.StateChange += (_, e) =>
        {
            if (e.CurrentState != ConnectionState.Open) return;
            connection.CreateFunction<string?, string?, bool>(
                "UNICODE_CONTAINS",
                (haystack, needle) => haystack is not null && needle is not null
                    && haystack.Contains(needle, StringComparison.InvariantCultureIgnoreCase),
                isDeterministic: true);
        };

        return connection;
    }
}
