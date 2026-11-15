using Quotinator.Data.Database.Checks;
using Quotinator.Data.Enums;

namespace Quotinator.Data.Testing.NoOps;

/// <summary>
/// A database check that checks nothing and reports every layer passed (#490), for a test whose database is
/// not a file the SQLite check can read, such as a shared-cache in-memory database. Never for a test whose
/// subject is the check, or one that relies on the check judging a file: those use a real
/// <see cref="SqliteDatabaseCheck"/> through <c>TestDatabaseChecks</c>.
/// </summary>
/// <param name="databaseName">The name the database is reported under.</param>
public sealed class NoOpDatabaseCheck(string databaseName) : IDatabaseCheck
{
    /// <summary>The finding every layer carries, so a report from this stub cannot pass for a real one.</summary>
    public const string Finding = "Not checked: a test database the SQLite check cannot read.";

    /// <inheritdoc/>
    public string DatabaseName { get; } = databaseName;

    /// <inheritdoc/>
    public Task<DatabaseCheckResult> CheckAsync(DatabaseCheckDepth depth) =>
        Task.FromResult(new DatabaseCheckResult
        {
            DatabaseName = DatabaseName,
            Depth        = depth,
            Layers       = [.. Enum.GetValues<DatabaseCheckLayer>().Select(layer => new DatabaseCheckLayerResult
            {
                Layer   = layer,
                Status  = DatabaseCheckLayerStatus.Passed,
                Finding = Finding,
            })],
        });
}
