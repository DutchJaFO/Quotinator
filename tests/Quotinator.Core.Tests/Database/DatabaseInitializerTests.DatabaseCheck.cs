using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Quotinator.Core.Database;
using Quotinator.Data.Database;
using Quotinator.Data.Database.Checks;
using Quotinator.Data.Enums;
using Quotinator.Data.Testing.Database;
using Quotinator.Data.Testing.Fakes;

namespace Quotinator.Core.Tests.Database;

/// <summary>
/// #490, ADR 028: startup and a Reset both judge the database with the layered check. Startup returns a failed
/// check as an outcome without opening the database for writing; a Reset always checks at <c>Full</c>, sets
/// aside a file that is not a sound copy or could not be checked, and backs up one whose only fault is a
/// dangling reference.
/// <para>
/// Each damaged database is a real migrated one, with extra objects added and damaged through
/// <see cref="DocumentedFormatFixtures"/>. Extra objects leave the schema intact (#305), so the damage is the
/// only fault the database has.
/// </para>
/// </summary>
public partial class DatabaseInitializerTests
{
    private const string FixtureIndex = "IX_CheckFixture_Rank";

    /// <summary>
    /// An extra table and index whose ranks are 10 to 50, each stored as an 8-bit integer, so
    /// <see cref="DocumentedFormatFixtures.RewriteLastIndexKey"/> can rewrite the last entry in place.
    /// </summary>
    private static readonly string[] IndexedFixture =
    [
        "CREATE TABLE CheckFixture (Id INTEGER PRIMARY KEY, Rank INTEGER NOT NULL);",
        $"CREATE INDEX {FixtureIndex} ON CheckFixture (Rank);",
        "INSERT INTO CheckFixture (Id, Rank) VALUES (1, 10), (2, 20), (3, 30), (4, 40), (5, 50);",
    ];

    private static readonly string[] ReferencingFixture =
    [
        "CREATE TABLE CheckParent (Id INTEGER PRIMARY KEY);",
        "CREATE TABLE CheckChild (Id INTEGER PRIMARY KEY, ParentId INTEGER NOT NULL REFERENCES CheckParent (Id));",
    ];

    /// <summary>
    /// Row 13: a layer that fails stops startup as an outcome, before the database is opened for writing. The
    /// file is byte for byte as it was, and no <c>-wal</c> or <c>-shm</c> was created beside it, which a
    /// read-write open in WAL mode would have done.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_LayerFails_ReturnsThatLayersOutcomeWithoutOpeningForWrite()
    {
        await MigrateThenDamageAsync(IndexedFixture, () => DocumentedFormatFixtures.RewriteLastIndexKey(_dbPath, FixtureIndex, 100));
        string before = HashOf(_dbPath);

        DatabaseOperationResult result = await CreateInitializer([]).InitialiseAsync();

        Assert.IsFalse(result.Succeeded, "Startup succeeded over an index that disagrees with its table.");
        Assert.AreEqual(DatabaseCheckLayer.Indexes, result.Check?.FirstFailure?.Layer);
        SqliteConnection.ClearAllPools();
        Assert.AreEqual(before, HashOf(_dbPath), "The database file was written to.");
        Assert.IsFalse(File.Exists(_dbPath + "-wal"), "A -wal was created: the database was opened for writing.");
        Assert.IsFalse(File.Exists(_dbPath + "-shm"), "A -shm was created: the database was opened for writing.");
    }

    /// <summary>
    /// Row 13's positive control: the same database without the damage starts, and the result carries the
    /// check startup ran, with every layer passed.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_HealthyDatabase_SucceedsCarryingAPassedCheck()
    {
        await MigrateThenDamageAsync(IndexedFixture, () => { });

        DatabaseOperationResult result = await CreateInitializer([]).InitialiseAsync();

        Assert.IsTrue(result.Succeeded, "Startup failed over a healthy database.");
        Assert.IsNotNull(result.Check, "The result does not carry the check startup ran.");
        Assert.HasCount(Enum.GetValues<DatabaseCheckLayer>().Length, result.Check.Layers);
        Assert.IsTrue(result.Check.Layers.All(l => l.Status == DatabaseCheckLayerStatus.Passed),
            string.Join(", ", result.Check.Layers.Select(l => $"{l.Layer}: {l.Status}")));
    }

    /// <summary>
    /// Decision F, no database: there is no file to check before anything is written, so the database is
    /// checked once it has been created. The check startup keeps is of a real file: no layer says there was
    /// nothing to check.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_NoDatabase_ChecksTheCreatedDatabase()
    {
        Assert.IsFalse(File.Exists(_dbPath), "Precondition: a database file already exists.");

        DatabaseOperationResult result = await CreateInitializer([]).InitialiseAsync();

        Assert.IsTrue(result.Succeeded, "Startup failed to create a database.");
        Assert.IsNotNull(result.Check, "The result does not carry a check.");
        Assert.IsTrue(result.Check.IsSound, $"First failure: {result.Check.FirstFailure?.Layer} ({result.Check.FirstFailure?.Finding})");
        Assert.DoesNotContain(l => l.Finding == LayerResults.NoFileYet, result.Check.Layers,
            "The kept check is the one taken before the database existed.");
    }

    /// <summary>
    /// Decision F, an old database: its schema differs from this build's until its migrations run, which is
    /// expected and does not stop startup. The check runs again after migrating, and that is the one kept.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_OldDatabase_KeepsTheCheckTakenAfterMigrating()
    {
        IReadOnlyList<SchemaMigration> allButTheLast = [.. QuotinatorMigrations.All.Take(QuotinatorMigrations.All.Count - 1)];
        await CreateInitializer([], allButTheLast, useBaseline: false).InitialiseAsync();
        SqliteConnection.ClearAllPools();

        QuotinatorDatabaseInitializer upgrade = CreateInitializer([]);
        DatabaseOperationResult result = await upgrade.InitialiseAsync();

        Assert.IsNotEmpty(upgrade.MigrationsApplied, "Precondition: the database needed no migration.");
        Assert.IsTrue(result.Succeeded, $"Startup failed on an old database: {result.Check?.FirstFailure?.Layer} ({result.Check?.FirstFailure?.Finding})");
        Assert.IsNotNull(result.Check, "The result does not carry a check.");
        Assert.IsTrue(result.Check.IsSound, $"The kept check is not the one taken after migrating: {result.Check.FirstFailure?.Layer} ({result.Check.FirstFailure?.Finding})");
    }

    /// <summary>
    /// T1 found it: startup over an old database logged the schema comparison taken before migrating as an
    /// error, though a difference there is certain and does not stop startup. The log is the subject: it is
    /// what an operator without a UI reads to confirm the server is sound. Before migrating, the Schema layer
    /// is skipped and says so; nothing is logged as a failure.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_OldDatabase_LogsTheSchemaSkippedRatherThanFailedBeforeMigrating()
    {
        IReadOnlyList<SchemaMigration> allButTheLast = [.. QuotinatorMigrations.All.Take(QuotinatorMigrations.All.Count - 1)];
        await CreateInitializer([], allButTheLast, useBaseline: false).InitialiseAsync();
        SqliteConnection.ClearAllPools();
        CapturingLogger<DatabaseInitializer> logger = new();

        QuotinatorDatabaseInitializer upgrade = CreateInitializer([], logger: logger);
        await upgrade.InitialiseAsync();

        Assert.IsNotEmpty(upgrade.MigrationsApplied, "Precondition: the database needed no migration.");
        Assert.DoesNotContain(m => m.Contains("the Schema layer failed", StringComparison.Ordinal), logger.Messages,
            "The schema comparison taken before migrating was logged as a failure.");
        Assert.Contains(m => m.Contains("the Schema layer was skipped", StringComparison.Ordinal), logger.Messages,
            "The check taken before migrating does not say it skipped the Schema layer.");
    }

    /// <summary>
    /// Decision F, an old database whose migrated schema still differs from what this build defines: the check
    /// taken after migrating fails at Schema, and startup returns the schema outcome #305 returns.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_MigratedSchemaStillDiffers_ReturnsTheSchemaOutcome()
    {
        await CreateInitializer([]).InitialiseAsync();
        SqliteConnection.ClearAllPools();
        SchemaMigration dropsAColumn = new()
        {
            Version     = QuotinatorMigrations.All.Count + 1,
            Sql         = "ALTER TABLE Quotinator_Universe DROP COLUMN NoValueKnown;",
            Description = "A test migration that leaves the schema different from the baseline.",
        };

        DatabaseOperationResult result = await CreateInitializer([], [.. QuotinatorMigrations.All, dropsAColumn], useBaseline: true).InitialiseAsync();

        Assert.IsFalse(result.Succeeded, "Startup succeeded over a schema that differs from this build's.");
        Assert.IsTrue(result.SchemaNotIntact, "Startup did not return the schema outcome.");
        Assert.AreEqual(DatabaseCheckLayer.Schema, result.Check?.FirstFailure?.Layer, "The kept check does not name the Schema layer.");
    }

    /// <summary>
    /// Row 14: an index that disagrees with its table means the file is not a sound copy (ADR 028), so a Reset
    /// sets it aside rather than keeping it as a backup. <c>quick_check</c> alone, today's test, passes it.
    /// </summary>
    [TestMethod]
    public async Task ResetAsync_IndexDisagreesWithItsTable_SetsTheFileAside()
    {
        await MigrateThenDamageAsync(IndexedFixture, () => DocumentedFormatFixtures.RewriteLastIndexKey(_dbPath, FixtureIndex, 100));

        DatabaseOperationResult result = await CreateInitializer([]).ResetAsync();

        Assert.IsTrue(result.Succeeded, $"The Reset refused ({result.BackupObstacle}, {result.ResetObstacle}).");
        Assert.HasCount(1, MainSetAsideFiles(), "The file was not set aside.");
        Assert.AreEqual(0, BackupFileCount(), "A file that is not a sound copy was kept as a backup.");
        Assert.AreEqual(RestorePointOutcome.SetAside, result.RestorePoint?.Outcome);
    }

    /// <summary>
    /// Row 14, as superseded (developer, 2026-11-16): a sound database whose <c>-wal</c> holds data is checked in
    /// memory, so nothing the data directory refuses the check can stop it showing the file is a sound copy, and
    /// the Reset backs it up. Before, a copy that could not be made in the data directory set a healthy file aside.
    /// </summary>
    [TestMethod]
    public async Task ResetAsync_WalHoldsData_NothingWritableForTheCheck_BacksTheSoundCopyUp()
    {
        await MigrateThenDamageAsync([], () => DocumentedFormatFixtures.AddWalHoldingData(_dbPath, "CREATE TABLE AddedInTheWal (Id INTEGER PRIMARY KEY);"));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(_dbPath)!, ".database-check"),
            "A file where the check once made its copy, so nothing can be written there.");

        DatabaseOperationResult result = await CreateInitializer([]).ResetAsync();

        Assert.IsTrue(result.Succeeded, $"The Reset refused ({result.BackupObstacle}, {result.ResetObstacle}).");
        Assert.IsEmpty(MainSetAsideFiles(), "A sound database was set aside.");
        Assert.AreEqual(1, BackupFileCount(), "A sound database was not backed up.");
    }

    /// <summary>
    /// Row 14, decision E: a dangling reference may be the application's own until #493, and the file is still
    /// a sound copy, so a Reset keeps it as a backup (ADR 025) rather than setting it aside.
    /// </summary>
    [TestMethod]
    public async Task ResetAsync_DanglingReference_BacksTheFileUp()
    {
        await MigrateThenDamageAsync(ReferencingFixture, () => DocumentedFormatFixtures.WriteWithoutForeignKeyEnforcement(_dbPath,
            "INSERT INTO CheckChild (Id, ParentId) VALUES (1, 999);"));

        DatabaseOperationResult result = await CreateInitializer([]).ResetAsync();

        Assert.IsTrue(result.Succeeded, $"The Reset refused ({result.BackupObstacle}, {result.ResetObstacle}).");
        Assert.AreEqual(1, BackupFileCount(), "A sound copy with a dangling reference was not backed up.");
        Assert.IsEmpty(MainSetAsideFiles(), "A sound copy was set aside.");
        Assert.AreEqual(RestorePointOutcome.Succeeded, result.RestorePoint?.Outcome);
    }

    /// <summary>
    /// Row 14: a Reset always checks at <c>Full</c>. With the startup setting at <c>Fast</c>, which skips the
    /// Indexes layer, a Reset still finds the broken index and sets the file aside.
    /// </summary>
    [TestMethod]
    public async Task ResetAsync_FastSetting_StillRunsEveryLayer()
    {
        await MigrateThenDamageAsync(IndexedFixture, () => DocumentedFormatFixtures.RewriteLastIndexKey(_dbPath, FixtureIndex, 100));

        DatabaseOperationResult result = await CreateInitializer([], startupCheckDepth: DatabaseCheckDepth.Fast).ResetAsync();

        Assert.IsTrue(result.Succeeded, $"The Reset refused ({result.BackupObstacle}, {result.ResetObstacle}).");
        Assert.HasCount(1, MainSetAsideFiles(), "The Reset did not find the broken index: it did not check at Full.");
        Assert.AreEqual(0, BackupFileCount(), "A file that is not a sound copy was kept as a backup.");
    }

    /// <summary>
    /// Creates and migrates an empty database, closes it so no sidecar is left, adds <paramref name="extraObjects"/>,
    /// then applies <paramref name="damage"/>.
    /// </summary>
    private async Task MigrateThenDamageAsync(IEnumerable<string> extraObjects, Action damage)
    {
        DatabaseOperationResult migrated = await CreateInitializer([]).InitialiseAsync();
        Assert.IsTrue(migrated.Succeeded, "Precondition: the empty database did not migrate.");
        // Every connection is closed, so the -wal is checkpointed: what is left beside the file holds nothing.
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath + "-wal");
        File.Delete(_dbPath + "-shm");

        string[] statements = [.. extraObjects];
        if (statements.Length > 0)
            DocumentedFormatFixtures.Execute(_dbPath, statements);

        damage();
    }

    private static string HashOf(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
