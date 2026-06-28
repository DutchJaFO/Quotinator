using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;

namespace Quotinator.Data.Testing.Database;

/// <summary>
/// Disposable temporary SQLite database for integration tests.
/// Creates a real database file in a temp directory, executes the supplied DDL statements in order, and deletes all files on dispose.
/// </summary>
public sealed class TempDatabase : IDisposable
{
    private readonly string _tempDir;

    /// <summary>Absolute path to the temporary database file.</summary>
    public string DbPath { get; }

    /// <summary>Connection factory pointing at <see cref="DbPath"/>.</summary>
    public IDbConnectionFactory ConnectionFactory { get; }

    /// <summary>
    /// Creates a temporary database and executes each statement in <paramref name="ddlStatements"/> in order.
    /// </summary>
    /// <param name="ddlStatements">DDL statements to run after the database file is created (e.g. CREATE TABLE).</param>
    public TempDatabase(IReadOnlyList<string> ddlStatements)
    {
        _tempDir          = Directory.CreateTempSubdirectory("quotinator_test_").FullName;
        DbPath            = Path.Combine(_tempDir, "test.db");
        ConnectionFactory = new SqliteConnectionFactory(DbPath);

        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        foreach (var ddl in ddlStatements)
            conn.Execute(ddl);
    }

    /// <summary>Clears all pooled SQLite connections and deletes the temporary directory and database file.</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
