using Quotinator.Data.Enums;
using Quotinator.Data.Paths;

namespace Quotinator.Data.Database.Checks;

/// <summary>
/// The SQLite implementation of <see cref="IDatabaseCheck"/> (#490, ADR 028): each layer mapped to what
/// sqlite.org documents, and every undocumented case treated as its worst case.
/// <para>
/// <b>A signature only:</b> <see cref="CheckAsync"/> runs no layer and reports every one
/// <see cref="DatabaseCheckLayerStatus.NotRun"/>, a default no test expects.
/// </para>
/// </summary>
/// <param name="databaseName">The name the database is reported under.</param>
/// <param name="databasePath">The database file to check. Neither it nor any file beside it is ever written.</param>
/// <param name="workFolder">
/// A reserved folder inside the data directory where a copy of the database and its sidecars is checked when
/// its <c>-wal</c> holds data. Not the system temporary folder: the add-on's AppArmor profile grants no lock
/// permission there, and SQLite locks the files it opens.
/// </param>
/// <param name="expectedSchemaScripts">The DDL that defines the schema the Schema layer compares against (#305).</param>
/// <param name="canCreateFileIn">
/// Answers whether a file can be created in a directory, for the Storage layer. <see langword="null"/> uses
/// <see cref="DirectoryWriteProbe.CanCreateFileIn"/>; a test supplies its own, since a directory that cannot
/// be written is not something every platform lets a test create.
/// </param>
public sealed class SqliteDatabaseCheck(
    string databaseName,
    string databasePath,
    string workFolder,
    IReadOnlyList<string> expectedSchemaScripts,
    Func<string, bool>? canCreateFileIn = null) : IDatabaseCheck
{
    /// <summary>
    /// The reserved folder's name inside the data directory, where a copy is checked when the <c>-wal</c> holds
    /// data. The leading dot keeps it out of what an operator browses, and no enumeration of the data
    /// directory reads it.
    /// </summary>
    public const string WorkFolderName = ".database-check";

    /// <summary>Every layer, in the order the check runs them.</summary>
    private static readonly DatabaseCheckLayer[] LayerOrder =
    [
        DatabaseCheckLayer.Storage,
        DatabaseCheckLayer.Format,
        DatabaseCheckLayer.Journal,
        DatabaseCheckLayer.Pages,
        DatabaseCheckLayer.Indexes,
        DatabaseCheckLayer.References,
        DatabaseCheckLayer.Schema,
    ];

    /// <inheritdoc/>
    public string DatabaseName { get; } = databaseName;

    /// <summary>The database file this check reads.</summary>
    public string DatabasePath { get; } = databasePath;

    /// <summary>The reserved folder a copy is checked in when the <c>-wal</c> holds data.</summary>
    public string WorkFolder { get; } = workFolder;

    /// <summary>The DDL that defines the expected schema.</summary>
    public IReadOnlyList<string> ExpectedSchemaScripts { get; } = expectedSchemaScripts;

    /// <summary>Answers whether a file can be created in a directory.</summary>
    public Func<string, bool> CanCreateFileIn { get; } = canCreateFileIn ?? DirectoryWriteProbe.CanCreateFileIn;

    /// <inheritdoc/>
    public Task<DatabaseCheckResult> CheckAsync(DatabaseCheckDepth depth) =>
        Task.FromResult(new DatabaseCheckResult
        {
            DatabaseName = DatabaseName,
            Depth        = depth,
            Layers       = [.. LayerOrder.Select(layer => new DatabaseCheckLayerResult { Layer = layer, Status = DatabaseCheckLayerStatus.NotRun })],
        });
}
