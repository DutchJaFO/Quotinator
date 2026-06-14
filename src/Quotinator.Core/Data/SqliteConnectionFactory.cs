using System.Data;
using Microsoft.Data.Sqlite;

namespace Quotinator.Core.Data;

public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
    }

    public IDbConnection CreateConnection() => new SqliteConnection(_connectionString);
}
