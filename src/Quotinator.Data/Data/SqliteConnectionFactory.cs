using System.Data;
using Microsoft.Data.Sqlite;

namespace Quotinator.Data.Data;

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
    public IDbConnection CreateConnection() => new SqliteConnection(_connectionString);
}
