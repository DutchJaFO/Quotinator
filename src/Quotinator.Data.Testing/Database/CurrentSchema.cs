using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Data.Testing.Database;

/// <summary>
/// Builds a test database at <c>Quotinator.Data</c>'s current schema by running the real
/// <see cref="DatabaseInitializer"/>, rather than by hand-listing the migrations that produce it.
/// <para>
/// A hand-listed sequence is a maintained copy of production's, and it drifts: every migration touching
/// a table has to be added to every fixture that replays it, and the failure surfaces as
/// <c>no such column</c> in tests that have nothing to do with the change. #304 hit that twice, for two
/// different migrations, across four fixtures. Running the initializer cannot drift — it applies exactly
/// what the application applies, which is also the only schema worth asserting against.
/// </para>
/// <para>
/// This is for tests that want *the current schema*. A test deliberately standing at an older schema to
/// exercise a migration is a different thing and should keep listing what it means — see
/// <c>NotificationTranslationTests</c>'s own frozen "schema before #319" array, which exists precisely so
/// a later migration cannot silently change what it is testing.
/// </para>
/// </summary>
public static class CurrentSchema
{
    /// <summary>
    /// Creates the Data-owned schema in the database at <paramref name="dbPath"/>, as the application
    /// itself creates it. No consumer migrations and no consumer baseline: this is the infrastructure
    /// schema only, which is what a <c>Quotinator.Data</c> test is about.
    /// </summary>
    /// <param name="dbPath">Path to the SQLite file to initialise.</param>
    public static async Task ApplyDataSchemaAsync(string dbPath)
    {
        SqliteConnectionFactory factory = new(dbPath);
        DatabaseOptions options = new()
        {
            DbPath      = dbPath,
            BackupsPath = Path.Combine(Path.GetDirectoryName(dbPath)!, "backups"),
        };

        DatabaseInitializer initializer = new(
            factory, options, migrations: [],
            NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance,
            NullLogger<DatabaseInitializer>.Instance,
            NoOpDiskSpaceProvider.Instance);

        await initializer.InitialiseAsync();
    }
}
