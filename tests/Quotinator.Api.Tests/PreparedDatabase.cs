using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Data.Database;

namespace Quotinator.Api.Tests;

/// <summary>
/// A database prepared once per test run, ahead of any test, for the hosts that need one (#424).
/// <para>
/// Every host starts from its own empty data directory, so a test whose host replaces the database
/// initializer (with <c>NoOpDatabaseInitializer</c>) has no schema at all unless it is given one. Until
/// #424 such tests read tables that some other test's startup had created in the build output's shared
/// data folder, on an earlier run or earlier in the same one: found 2026-09-27, when eight notification
/// endpoint tests answered <c>500</c> the first time they ran against a fresh directory.
/// </para>
/// <para>
/// Built by a real startup, with the bundled sources and the source refresh off, so it holds the current
/// build's schema and nothing the outside world supplies. It is copied out with SQLite's own backup, so the
/// file is complete and consistent however far the live database's write-ahead log had got.
/// </para>
/// </summary>
internal static class PreparedDatabase
{
    /// <summary>Where the prepared database is; empty until <see cref="Prepare"/> has run.</summary>
    internal static string FilePath { get; private set; } = string.Empty;

    /// <summary>Builds the prepared database. Run once, from <c>[AssemblyInitialize]</c> (ADR 006).</summary>
    internal static void Prepare()
    {
        string template = Path.Combine(Path.GetTempPath(), "quotinator-apitest-prepared-" + Guid.NewGuid().ToString("N") + ".db");

        using (QuotinatorWebApplicationFactory factory = new())
        {
            string live = factory.Services.GetRequiredService<DatabaseOptions>().DbPath;

            using SqliteConnection source = new($"Data Source={live};Pooling=False");
            using SqliteConnection destination = new($"Data Source={template};Pooling=False");
            source.Open();
            destination.Open();
            source.BackupDatabase(destination);
        }

        FilePath = template;
    }

    /// <summary>Deletes the prepared database. Run once, from <c>[AssemblyCleanup]</c>.</summary>
    internal static void Remove()
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);
    }
}
