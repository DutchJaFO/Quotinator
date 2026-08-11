using System.Data;
using Microsoft.Data.Sqlite;

namespace Quotinator.Data.Connections;

/// <summary>SQLite implementation of <see cref="IDbConnectionFactory"/> using a file-based connection string.</summary>
/// <remarks>Initialises the factory with the path to the SQLite database file.</remarks>
/// <param name="dbPath">Absolute path to the <c>.db</c> file. The file is created if it does not exist.</param>
public sealed class SqliteConnectionFactory(string dbPath) : IDbConnectionFactory
{
    private readonly string _connectionString = $"Data Source={dbPath}";

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

            // #293: temp_store is a per-connection setting (unlike journal_mode=WAL, which persists in
            // the database file itself), so it must be re-applied on every Open here rather than once
            // in DatabaseInitializer. Root cause of a live HA v1.8.2 → v1.8.3-beta migration failure
            // (SQLite Error 14, 'unable to open database file'): SQLite creates a statement journal — a
            // temp file, separate from the main WAL/rollback journal — for any statement that could
            // partially fail without aborting the whole transaction (e.g. a UNIQUE-constraint
            // violation), even under WAL mode. Neither TMPDIR nor SQLITE_TMPDIR is set anywhere in this
            // project's Dockerfile, so SQLite's own fallback chain for where to put that temp file
            // (SQLITE_TMPDIR → TMPDIR → a short hardcoded list → finally the current working directory)
            // is entirely environment-dependent. The HA add-on's own AppArmor profile (apparmor.txt)
            // confirms two real gaps in that chain: `/app/** rixmr` grants no write permission at all
            // (the container's WORKDIR, and SQLite's last-resort fallback), and `/tmp/** rw` grants
            // write but not lock ('k', unlike `/data/** rwk`) — SQLite's own temp/journal files are
            // typically locked. Either gap reproduces this exact error if the addon's container runtime
            // doesn't inherit a usable TMPDIR the way a local Docker Desktop test happens to (found
            // live: the identical migration succeeded in a local Docker repro, confirming this is
            // environment-specific, not a code defect in the migration SQL itself). `temp_store =
            // MEMORY` sidesteps the whole fallback chain by never writing temp data to disk at all —
            // this project's tables are small enough that the memory cost is negligible.
            using var pragmaCommand = connection.CreateCommand();
            pragmaCommand.CommandText = "PRAGMA temp_store=MEMORY;";
            pragmaCommand.ExecuteNonQuery();
        };

        return connection;
    }
}
