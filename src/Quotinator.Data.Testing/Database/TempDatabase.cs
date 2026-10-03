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
    private readonly TempDirectory _tempDir;

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
        _tempDir          = new TempDirectory("quotinator_test_");
        DbPath            = Path.Combine(_tempDir.Path, "test.db");
        ConnectionFactory = new SqliteConnectionFactory(DbPath);

        using SqliteConnection conn = new($"Data Source={DbPath}");
        conn.Open();
        foreach (string ddl in ddlStatements)
            conn.Execute(ddl);
    }

    /// <summary>
    /// Deletes the temporary directory and the database file in it, via <see cref="TempDirectory"/>:
    /// one implementation of "clear the pools, then delete", and one place a failure is reported from.
    /// </summary>
    public void Dispose() => _tempDir.Dispose();
}
