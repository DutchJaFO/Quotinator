using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Enums;
using Quotinator.Data.Paths;

namespace Quotinator.Data.Database.Checks;

/// <summary>
/// The SQLite implementation of <see cref="IDatabaseCheck"/> (#490, ADR 028): each layer mapped to what
/// sqlite.org documents, and every undocumented case treated as its worst case. Each layer is its own class;
/// this one holds their order and the rules that connect them.
/// <list type="bullet">
/// <item>The first layer that fails is the result, and no later layer runs.</item>
/// <item>
/// Except Storage, when the file can be read and only its directory cannot be written: that is no fault in the
/// file, and the reading layers need nothing written, so they still run (decision C).
/// </item>
/// <item>Something the check could not do, a file it could not read, is a Storage failure, never an inconclusive layer.</item>
/// <item>The check needs no disk space: a <c>-wal</c> holding data is applied in memory (decision D as superseded, 2026-11-16).</item>
/// <item>At <see cref="DatabaseCheckDepth.Fast"/>, Indexes and References are reported skipped, never left out.</item>
/// </list>
/// </summary>
/// <param name="databaseName">The name the database is reported under.</param>
/// <param name="databasePath">The database file to check. Neither it nor any file beside it is ever written.</param>
/// <param name="expectedSchemaScripts">The DDL that defines the schema the Schema layer compares against (#305).</param>
/// <param name="expectedSchemaVersions">
/// The version each version table records once every migration this build knows has run. The Schema layer
/// compares only when the database records exactly these, and reports itself skipped otherwise: before its
/// migrations run, an old database's schema differs from <paramref name="expectedSchemaScripts"/> by design.
/// Empty for a database with no version tables, which is always compared.
/// </param>
/// <param name="canCreateFileIn">
/// Answers whether a file can be created in a directory, for the Storage layer. <see langword="null"/> uses
/// <see cref="DirectoryWriteProbe.CanCreateFileIn"/>; a test supplies its own, since a directory that cannot
/// be written is not something every platform lets a test create.
/// </param>
/// <param name="logger">Logs each exception a layer handles (ADR 022). <see langword="null"/> logs nothing.</param>
public sealed class SqliteDatabaseCheck(
    string databaseName,
    string databasePath,
    IReadOnlyList<string> expectedSchemaScripts,
    IReadOnlyDictionary<SchemaVersionTable, int> expectedSchemaVersions,
    Func<string, bool>? canCreateFileIn = null,
    ILogger? logger = null) : IDatabaseCheck
{
    /// <summary>Every layer, in the order the check runs them.</summary>
    private static readonly IDatabaseCheckLayer[] Layers =
    [
        new StorageLayer(),
        new FormatLayer(),
        new JournalLayer(),
        new PagesLayer(),
        new IndexesLayer(),
        new ReferencesLayer(),
        new SchemaLayer(),
    ];

    /// <inheritdoc/>
    public string DatabaseName { get; } = databaseName;

    /// <inheritdoc/>
    public async Task<DatabaseCheckResult> CheckAsync(DatabaseCheckDepth depth)
    {
        using SqliteCheckSubject subject = new(
            databasePath, expectedSchemaScripts, expectedSchemaVersions,
            canCreateFileIn ?? DirectoryWriteProbe.CanCreateFileIn,
            logger ?? NullLogger.Instance);

        List<DatabaseCheckLayerResult> results = [];
        bool stopped = false;
        foreach (IDatabaseCheckLayer layer in Layers)
        {
            if (stopped)
            {
                results.Add(new DatabaseCheckLayerResult { Layer = layer.Layer, Status = DatabaseCheckLayerStatus.NotRun });
                continue;
            }

            if (depth == DatabaseCheckDepth.Fast && !layer.RunsAtFast)
            {
                results.Add(new DatabaseCheckLayerResult { Layer = layer.Layer, Status = DatabaseCheckLayerStatus.Skipped });
                continue;
            }

            Stopwatch elapsed = Stopwatch.StartNew();
            DatabaseCheckLayerResult result = await layer.RunAsync(subject);
            elapsed.Stop();

            results.Add(new DatabaseCheckLayerResult
            {
                Layer = result.Layer, Status = result.Status, Finding = result.Finding, StorageFault = result.StorageFault, Elapsed = elapsed.Elapsed,
            });
            stopped = StopsTheCheck(result, subject);
        }

        return new DatabaseCheckResult { DatabaseName = DatabaseName, Depth = depth, Layers = results };
    }

    // Decision C: an unwritable directory beside a readable file is reported, but the file is still checked.
    // ADR 028: a Storage failure stops the reading layers only when they cannot run, because the file cannot be read.
    private static bool StopsTheCheck(DatabaseCheckLayerResult result, SqliteCheckSubject subject) =>
        result.Status == DatabaseCheckLayerStatus.Failed
        && !(result.Layer == DatabaseCheckLayer.Storage && subject.FileReadable);
}
