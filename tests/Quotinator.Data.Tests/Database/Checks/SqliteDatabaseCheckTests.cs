using System.Security.Cryptography;
using Quotinator.Data.Database.Checks;
using Quotinator.Data.Enums;
using Quotinator.Data.Testing.Database;

namespace Quotinator.Data.Tests.Database.Checks;

/// <summary>
/// #490, ADR 028: the SQLite database check, layer by layer. Every damaged file is built by
/// <see cref="DocumentedFormatFixtures"/> through the documented file format, never by relying on how SQLite
/// reacts to a misuse.
/// </summary>
[TestClass]
public class SqliteDatabaseCheckTests
{
    private const string DatabaseName = "test";
    private const string IndexName = "IX_Child_Rank";
    private const string WalOnlyTable = "AddedInTheWal";

    /// <summary>
    /// A schema with every kind of object a layer checks: a table spanning several pages, an index, and a
    /// declared reference. The ranks are 10 to 50, so each is stored as an 8-bit integer (fileformat.html
    /// §2.1); 0 and 1 have serial types with no body, which <see cref="DocumentedFormatFixtures.RewriteLastIndexKey"/>
    /// cannot rewrite.
    /// </summary>
    private static readonly string[] Schema =
    [
        "CREATE TABLE Parent (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);",
        "CREATE TABLE Child (Id INTEGER PRIMARY KEY, ParentId INTEGER NOT NULL REFERENCES Parent (Id), Rank INTEGER NOT NULL);",
        $"CREATE INDEX {IndexName} ON Child (Rank);",
    ];

    private static readonly string[] Rows =
    [
        "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 200) INSERT INTO Parent (Id, Name) SELECT i, printf('%.200c', 'x') FROM n;",
        "INSERT INTO Child (Id, ParentId, Rank) VALUES (1, 1, 10), (2, 1, 20), (3, 2, 30), (4, 2, 40), (5, 3, 50);",
    ];

    private static readonly string[] WalSchema = [$"CREATE TABLE {WalOnlyTable} (Id INTEGER PRIMARY KEY);"];

    private static readonly DatabaseCheckLayer[] QueryLayers =
    [
        DatabaseCheckLayer.Pages, DatabaseCheckLayer.Indexes, DatabaseCheckLayer.References, DatabaseCheckLayer.Schema,
    ];

    /// <summary>Row 1: a healthy database passes every layer.</summary>
    [TestMethod]
    public async Task Check_HealthyDatabase_PassesEveryLayer()
    {
        using Scenario scenario = Scenario.Build(Damage.None);

        DatabaseCheckResult result = await scenario.Check().CheckAsync(DatabaseCheckDepth.Full);

        AssertEveryLayer(result, DatabaseCheckLayerStatus.Passed);
        Assert.IsTrue(result.IsSound);
    }

    /// <summary>Row 2: a file without the header's magic string fails at Format, and nothing after it runs.</summary>
    [TestMethod]
    public async Task Check_NotADatabase_FailsAtFormatAndRunsNoLaterLayer()
    {
        using Scenario scenario = Scenario.Build(Damage.NotADatabase);

        DatabaseCheckResult result = await scenario.Check().CheckAsync(DatabaseCheckDepth.Full);

        AssertFailsAt(result, DatabaseCheckLayer.Format, DatabaseCheckLayerStatus.Failed);
    }

    /// <summary>
    /// Row 3: a database cut to half its length, whose in-header size is valid, fails at Pages. Whether pages
    /// are missing is the Pages layer's documented question; the Format layer makes no claim from the length.
    /// </summary>
    [TestMethod]
    public async Task Check_TruncatedDatabase_FailsAtPagesAndRunsNoLaterLayer()
    {
        using Scenario scenario = Scenario.Build(Damage.Truncated);

        DatabaseCheckResult result = await scenario.Check().CheckAsync(DatabaseCheckDepth.Full);

        AssertFailsAt(result, DatabaseCheckLayer.Pages, DatabaseCheckLayerStatus.Failed);
    }

    /// <summary>Row 4: a <c>-wal</c> whose magic number the format does not allow fails at Journal.</summary>
    [TestMethod]
    public async Task Check_InvalidWalMagic_FailsAtJournal()
    {
        using Scenario scenario = Scenario.Build(Damage.InvalidWalMagic);

        DatabaseCheckResult result = await scenario.Check().CheckAsync(DatabaseCheckDepth.Full);

        AssertFailsAt(result, DatabaseCheckLayer.Journal, DatabaseCheckLayerStatus.Failed);
    }

    /// <summary>Row 4: a <c>-wal</c> whose header checksum does not match its header fails at Journal.</summary>
    [TestMethod]
    public async Task Check_InvalidWalHeaderChecksum_FailsAtJournal()
    {
        using Scenario scenario = Scenario.Build(Damage.InvalidWalHeaderChecksum);

        DatabaseCheckResult result = await scenario.Check().CheckAsync(DatabaseCheckDepth.Full);

        AssertFailsAt(result, DatabaseCheckLayer.Journal, DatabaseCheckLayerStatus.Failed);
    }

    /// <summary>Row 5: a b-tree page holding a page type the format forbids fails at Pages.</summary>
    [TestMethod]
    public async Task Check_DamagedPage_FailsAtPages()
    {
        using Scenario scenario = Scenario.Build(Damage.DamagedPage);

        DatabaseCheckResult result = await scenario.Check().CheckAsync(DatabaseCheckDepth.Full);

        AssertFailsAt(result, DatabaseCheckLayer.Pages, DatabaseCheckLayerStatus.Failed);
    }

    /// <summary>
    /// Row 6: an index entry rewritten so it no longer matches its row passes Pages, which documents that it
    /// does not compare index content with table content, and fails at Indexes, which does.
    /// </summary>
    [TestMethod]
    public async Task Check_IndexDisagreesWithItsTable_FailsAtIndexes()
    {
        using Scenario scenario = Scenario.Build(Damage.IndexDisagreesWithItsTable);

        DatabaseCheckResult result = await scenario.Check().CheckAsync(DatabaseCheckDepth.Full);

        AssertFailsAt(result, DatabaseCheckLayer.Indexes, DatabaseCheckLayerStatus.Failed);
    }

    /// <summary>Row 7: a row that references one that does not exist fails at References, the last layer before Schema.</summary>
    [TestMethod]
    public async Task Check_DanglingReference_FailsAtReferences()
    {
        using Scenario scenario = Scenario.Build(Damage.DanglingReference);

        DatabaseCheckResult result = await scenario.Check().CheckAsync(DatabaseCheckDepth.Full);

        AssertFailsAt(result, DatabaseCheckLayer.References, DatabaseCheckLayerStatus.Failed);
    }

    /// <summary>Row 8: <c>Fast</c> leaves out Indexes and References and says so, rather than omitting them.</summary>
    [TestMethod]
    public async Task Check_Fast_SkipsIndexesAndReferences()
    {
        using Scenario scenario = Scenario.Build(Damage.None);

        DatabaseCheckResult result = await scenario.Check().CheckAsync(DatabaseCheckDepth.Fast);

        Assert.AreEqual(DatabaseCheckDepth.Fast, result.Depth);
        Assert.AreEqual(DatabaseCheckLayerStatus.Skipped, StatusOf(result, DatabaseCheckLayer.Indexes));
        Assert.AreEqual(DatabaseCheckLayerStatus.Skipped, StatusOf(result, DatabaseCheckLayer.References));
        foreach (DatabaseCheckLayer layer in new[] { DatabaseCheckLayer.Storage, DatabaseCheckLayer.Format, DatabaseCheckLayer.Journal, DatabaseCheckLayer.Pages, DatabaseCheckLayer.Schema })
            Assert.AreEqual(DatabaseCheckLayerStatus.Passed, StatusOf(result, layer), $"Layer {layer}.");
        Assert.IsTrue(result.IsSound);
    }

    /// <summary>
    /// Row 9: whatever the check finds, the database and every file beside it are left byte for byte as they
    /// were, and nothing is left in the work folder.
    /// </summary>
    /// <param name="damage">The fixture to check.</param>
    [TestMethod]
    [DataRow(Damage.None)]
    [DataRow(Damage.NotADatabase)]
    [DataRow(Damage.Truncated)]
    [DataRow(Damage.InvalidWalMagic)]
    [DataRow(Damage.InvalidWalHeaderChecksum)]
    [DataRow(Damage.DamagedPage)]
    [DataRow(Damage.IndexDisagreesWithItsTable)]
    [DataRow(Damage.DanglingReference)]
    [DataRow(Damage.WalHoldsData)]
    public async Task Check_AnyOutcome_LeavesTheFileAndSidecarsUntouched(Damage damage)
    {
        using Scenario scenario = Scenario.Build(damage);
        string[] before = scenario.HashDatabaseFiles();

        DatabaseCheckResult result = await scenario.Check().CheckAsync(DatabaseCheckDepth.Full);

        Assert.HasCount(LayerCount, result.Layers, "Positive control failed: the check did not report every layer.");
        Assert.Contains(l => l.Status != DatabaseCheckLayerStatus.NotRun, result.Layers, "Positive control failed: no layer ran.");
        Assert.AreSequenceEqual(before, scenario.HashDatabaseFiles(), "The check changed, created or removed a database file.");
        Assert.IsEmpty(scenario.FilesInWorkFolder(), "The check left files behind in its work folder.");
    }

    /// <summary>
    /// Row 10, decision C: a data directory that cannot be written fails Storage, because the database could
    /// not be replaced there, but it is no fault in the file, so the reading layers still run and pass.
    /// </summary>
    [TestMethod]
    public async Task Check_DirectoryNotWritable_FailsStorageAndStillChecksTheFile()
    {
        using Scenario scenario = Scenario.Build(Damage.None);

        DatabaseCheckResult result = await scenario.Check(canCreateFileIn: _ => false).CheckAsync(DatabaseCheckDepth.Full);

        Assert.AreEqual(DatabaseCheckLayerStatus.Failed, StatusOf(result, DatabaseCheckLayer.Storage));
        Assert.AreEqual(DatabaseCheckLayer.Storage, result.FirstFailure?.Layer);
        foreach (DatabaseCheckLayer layer in LayersAfter(DatabaseCheckLayer.Storage))
            Assert.AreEqual(DatabaseCheckLayerStatus.Passed, StatusOf(result, layer), $"Layer {layer} did not run on a readable file.");
    }

    /// <summary>
    /// Row 11: a <c>-wal</c> holding committed transactions is checked through a copy that SQLite recovers,
    /// never through an <c>immutable</c> open of the main file, which nothing documents as reading the
    /// <c>-wal</c>. The table that exists only in the <c>-wal</c> is the evidence: the Schema layer expects it,
    /// so a check that read the main file alone would fail there.
    /// </summary>
    [TestMethod]
    public async Task Check_WalHoldsData_ChecksACopyAndLeavesTheOriginalsUntouched()
    {
        using Scenario scenario = Scenario.Build(Damage.WalHoldsData);
        string[] before = scenario.HashDatabaseFiles();

        DatabaseCheckResult result = await scenario.Check().CheckAsync(DatabaseCheckDepth.Full);

        AssertEveryLayer(result, DatabaseCheckLayerStatus.Passed);
        Assert.AreSequenceEqual(before, scenario.HashDatabaseFiles(), "The check changed the original database or its sidecars.");
        Assert.IsEmpty(scenario.FilesInWorkFolder(), "The copy was not removed.");
    }

    /// <summary>
    /// Row 12, decision D: when the <c>-wal</c> holds data and no copy can be made, the main file alone cannot
    /// be checked soundly, so every query layer is <c>Inconclusive</c>, which is never a pass.
    /// </summary>
    [TestMethod]
    public async Task Check_CopyCannotBeMade_ReportsInconclusive()
    {
        using Scenario scenario = Scenario.Build(Damage.WalHoldsData);
        File.WriteAllText(scenario.WorkFolder, "A file where the work folder should be, so no copy can be made in it.");

        DatabaseCheckResult result = await scenario.Check().CheckAsync(DatabaseCheckDepth.Full);

        foreach (DatabaseCheckLayer layer in new[] { DatabaseCheckLayer.Storage, DatabaseCheckLayer.Format, DatabaseCheckLayer.Journal })
            Assert.AreEqual(DatabaseCheckLayerStatus.Passed, StatusOf(result, layer), $"Layer {layer}.");
        foreach (DatabaseCheckLayer layer in QueryLayers)
            Assert.AreEqual(DatabaseCheckLayerStatus.Inconclusive, StatusOf(result, layer), $"Layer {layer}.");
        Assert.IsFalse(result.IsSound, "An inconclusive check was reported as sound.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.FirstFailure?.Finding), "An inconclusive layer must say why.");
    }

    /// <summary>
    /// A first start has no database file yet, and a Reset creates one: neither is a fault. Storage passes, and
    /// every later layer passes with a finding saying there was nothing to check, so the report keeps its shape.
    /// </summary>
    [TestMethod]
    public async Task Check_NoDatabaseFileYet_PassesWithNothingToCheck()
    {
        using Scenario scenario = Scenario.Build(Damage.NoFile);

        DatabaseCheckResult result = await scenario.Check().CheckAsync(DatabaseCheckDepth.Full);

        AssertEveryLayer(result, DatabaseCheckLayerStatus.Passed);
        foreach (DatabaseCheckLayer layer in LayersAfter(DatabaseCheckLayer.Storage))
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Layers.Single(l => l.Layer == layer).Finding), $"Layer {layer} passed without saying there was nothing to check.");
        Assert.IsFalse(File.Exists(scenario.DatabasePath), "The check created the database file.");
    }

    /// <summary>The fixtures a scenario can build, one per kind of damage.</summary>
    public enum Damage
    {
        /// <summary>A healthy database.</summary>
        None,

        /// <summary>No database file at all.</summary>
        NoFile,

        /// <summary>A file without the header's magic string.</summary>
        NotADatabase,

        /// <summary>A database cut to half its length.</summary>
        Truncated,

        /// <summary>A <c>-wal</c> with a magic number the format does not allow.</summary>
        InvalidWalMagic,

        /// <summary>A <c>-wal</c> whose header checksum does not match.</summary>
        InvalidWalHeaderChecksum,

        /// <summary>A b-tree page with a forbidden page type.</summary>
        DamagedPage,

        /// <summary>An index entry that no longer matches its row.</summary>
        IndexDisagreesWithItsTable,

        /// <summary>A row referencing one that does not exist.</summary>
        DanglingReference,

        /// <summary>A valid <c>-wal</c> holding committed transactions.</summary>
        WalHoldsData,
    }

    private static int LayerCount => Enum.GetValues<DatabaseCheckLayer>().Length;

    private static IEnumerable<DatabaseCheckLayer> LayersAfter(DatabaseCheckLayer layer) =>
        Enum.GetValues<DatabaseCheckLayer>().SkipWhile(l => l != layer).Skip(1);

    private static DatabaseCheckLayerStatus StatusOf(DatabaseCheckResult result, DatabaseCheckLayer layer) =>
        result.Layers.Single(l => l.Layer == layer).Status;

    private static void AssertEveryLayer(DatabaseCheckResult result, DatabaseCheckLayerStatus expected)
    {
        Assert.AreSequenceEqual(Enum.GetValues<DatabaseCheckLayer>(), result.Layers.Select(l => l.Layer), "Every layer is reported, in order.");
        foreach (DatabaseCheckLayerResult layer in result.Layers)
            Assert.AreEqual(expected, layer.Status, $"Layer {layer.Layer}: {layer.Finding}");
    }

    /// <summary>The fixture's schema records no migrations, so it is always compared.</summary>
    private static readonly IReadOnlyDictionary<SchemaVersionTable, int> NoVersionTables = new Dictionary<SchemaVersionTable, int>();

    // ── Schema is compared only once the recorded version is this build's (#490, T1) ─────────────────────

    private const string VersionTable = "System_SchemaVersion";

    /// <summary>
    /// What the check expects: the database's own schema plus a table it lacks, so a comparison that runs
    /// fails, and a Schema layer that reports anything but a failure did not compare.
    /// </summary>
    private static readonly string[] ExpectedWithAMissingTable =
    [
        .. Schema,
        $"CREATE TABLE {VersionTable} (Version INTEGER NOT NULL, AppliedAt TEXT NOT NULL);",
        "CREATE TABLE AddedByAPendingMigration (Id INTEGER PRIMARY KEY);",
    ];

    /// <summary>
    /// T1 found it: before an old database's migrations run, its schema differs from this build's by design,
    /// so comparing it says nothing, and startup logged that certainty as an error. The layer reports itself
    /// skipped, naming the version table, rather than failed.
    /// </summary>
    [TestMethod]
    public async Task Check_RecordedVersionBehindThisBuild_SkipsSchemaNamingTheVersionTable()
    {
        using TempDirectory directory = new("quotinator_check_");
        string path = CreateWithRecordedVersion(directory, recorded: 1);

        DatabaseCheckResult result = await VersionedCheck(directory, path, expected: 2).CheckAsync(DatabaseCheckDepth.Full);

        DatabaseCheckLayerResult schema = result.Layers.Single(l => l.Layer == DatabaseCheckLayer.Schema);
        Assert.AreEqual(DatabaseCheckLayerStatus.Skipped, schema.Status, $"Schema: {schema.Finding}");
        Assert.Contains(VersionTable, schema.Finding ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>A database ahead of this build has a schema it does not know, so there is nothing to compare either (#350).</summary>
    [TestMethod]
    public async Task Check_RecordedVersionAheadOfThisBuild_SkipsSchema()
    {
        using TempDirectory directory = new("quotinator_check_");
        string path = CreateWithRecordedVersion(directory, recorded: 3);

        DatabaseCheckResult result = await VersionedCheck(directory, path, expected: 2).CheckAsync(DatabaseCheckDepth.Full);

        Assert.AreEqual(DatabaseCheckLayerStatus.Skipped, result.Layers.Single(l => l.Layer == DatabaseCheckLayer.Schema).Status);
    }

    /// <summary>A database with no version table yet has recorded no migration, so its schema is not this build's either.</summary>
    [TestMethod]
    public async Task Check_NoVersionTable_SkipsSchema()
    {
        using TempDirectory directory = new("quotinator_check_");
        string path = Path.Combine(directory.Path, "test.db");
        DocumentedFormatFixtures.Create(path, [.. Schema, .. Rows]);

        DatabaseCheckResult result = await VersionedCheck(directory, path, expected: 2).CheckAsync(DatabaseCheckDepth.Full);

        Assert.AreEqual(DatabaseCheckLayerStatus.Skipped, result.Layers.Single(l => l.Layer == DatabaseCheckLayer.Schema).Status);
    }

    /// <summary>Control: once the recorded version is this build's, a schema that differs still fails.</summary>
    [TestMethod]
    public async Task Check_RecordedVersionIsThisBuilds_ComparesTheSchema()
    {
        using TempDirectory directory = new("quotinator_check_");
        string path = CreateWithRecordedVersion(directory, recorded: 2);

        DatabaseCheckResult result = await VersionedCheck(directory, path, expected: 2).CheckAsync(DatabaseCheckDepth.Full);

        AssertFailsAt(result, DatabaseCheckLayer.Schema, DatabaseCheckLayerStatus.Failed);
    }

    private static string CreateWithRecordedVersion(TempDirectory directory, int recorded)
    {
        string path = Path.Combine(directory.Path, "test.db");
        DocumentedFormatFixtures.Create(path,
        [
            .. Schema, .. Rows,
            $"CREATE TABLE {VersionTable} (Version INTEGER NOT NULL, AppliedAt TEXT NOT NULL);",
            $"WITH RECURSIVE v(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM v WHERE i < {recorded}) INSERT INTO {VersionTable} (Version, AppliedAt) SELECT i, '2026-11-15' FROM v;",
        ]);
        return path;
    }

    private static SqliteDatabaseCheck VersionedCheck(TempDirectory directory, string path, int expected) =>
        new(DatabaseName, path, Path.Combine(directory.Path, "check-work"), ExpectedWithAMissingTable,
            new Dictionary<SchemaVersionTable, int> { [SchemaVersionTable.Data] = expected });

    /// <summary>The named layer is the first failure, every layer before it passed, and none after it ran.</summary>
    private static void AssertFailsAt(DatabaseCheckResult result, DatabaseCheckLayer failing, DatabaseCheckLayerStatus status)
    {
        Assert.AreSequenceEqual(Enum.GetValues<DatabaseCheckLayer>(), result.Layers.Select(l => l.Layer), "Every layer is reported, in order.");
        Assert.AreEqual(failing, result.FirstFailure?.Layer, $"First failure: {result.FirstFailure?.Layer} ({result.FirstFailure?.Finding}).");
        Assert.AreEqual(status, result.FirstFailure!.Status);
        Assert.IsFalse(result.IsSound);

        foreach (DatabaseCheckLayerResult layer in result.Layers.TakeWhile(l => l.Layer != failing))
            Assert.AreEqual(DatabaseCheckLayerStatus.Passed, layer.Status, $"Layer {layer.Layer} before the failure: {layer.Finding}");
        foreach (DatabaseCheckLayer layer in LayersAfter(failing))
            Assert.AreEqual(DatabaseCheckLayerStatus.NotRun, StatusOf(result, layer), $"Layer {layer} ran after the first failure.");
    }

    /// <summary>A data directory holding one fixture, and the check pointed at it.</summary>
    private sealed class Scenario : IDisposable
    {
        private readonly TempDirectory _directory = new("quotinator_check_");
        private readonly IReadOnlyList<string> _expectedSchema;

        private Scenario(IReadOnlyList<string> expectedSchema)
        {
            _expectedSchema = expectedSchema;
            DatabasePath = Path.Combine(_directory.Path, "test.db");
            WorkFolder = Path.Combine(_directory.Path, "check-work");
        }

        public string DatabasePath { get; }

        public string WorkFolder { get; }

        public static Scenario Build(Damage damage)
        {
            Scenario scenario = new(damage == Damage.WalHoldsData ? [.. Schema, .. WalSchema] : Schema);
            string path = scenario.DatabasePath;

            switch (damage)
            {
                case Damage.NoFile:
                    break;
                case Damage.NotADatabase:
                    DocumentedFormatFixtures.WriteNotADatabase(path);
                    break;
                case Damage.InvalidWalMagic:
                    DocumentedFormatFixtures.CreateWithWalHoldingData(path, [.. Schema, .. Rows], WalSchema);
                    DocumentedFormatFixtures.CorruptWalMagic(path + "-wal");
                    break;
                case Damage.InvalidWalHeaderChecksum:
                    DocumentedFormatFixtures.CreateWithWalHoldingData(path, [.. Schema, .. Rows], WalSchema);
                    DocumentedFormatFixtures.CorruptWalHeaderChecksum(path + "-wal");
                    break;
                case Damage.WalHoldsData:
                    DocumentedFormatFixtures.CreateWithWalHoldingData(path, [.. Schema, .. Rows], WalSchema);
                    break;
                default:
                    DocumentedFormatFixtures.Create(path, [.. Schema, .. Rows]);
                    break;
            }

            switch (damage)
            {
                case Damage.Truncated:
                    DocumentedFormatFixtures.TruncateToHalf(path);
                    break;
                case Damage.DamagedPage:
                    DocumentedFormatFixtures.DamageRootPageType(path, "Child");
                    break;
                case Damage.IndexDisagreesWithItsTable:
                    DocumentedFormatFixtures.RewriteLastIndexKey(path, IndexName, 100);
                    break;
                case Damage.DanglingReference:
                    DocumentedFormatFixtures.WriteWithoutForeignKeyEnforcement(path, "INSERT INTO Child (Id, ParentId, Rank) VALUES (6, 999, 60);");
                    break;
            }

            return scenario;
        }

        public SqliteDatabaseCheck Check(Func<string, bool>? canCreateFileIn = null) =>
            new(DatabaseName, DatabasePath, WorkFolder, _expectedSchema, NoVersionTables, canCreateFileIn);

        /// <summary>The database and each of its sidecars that exists, as its name and a hash of its content.</summary>
        public string[] HashDatabaseFiles() =>
        [
            .. new[] { DatabasePath, DatabasePath + "-wal", DatabasePath + "-shm", DatabasePath + "-journal" }
                .Where(File.Exists)
                .Select(path => $"{Path.GetFileName(path)} {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}"),
        ];

        public string[] FilesInWorkFolder() =>
            Directory.Exists(WorkFolder) ? Directory.GetFiles(WorkFolder, "*", SearchOption.AllDirectories) : [];

        public void Dispose() => _directory.Dispose();
    }
}
