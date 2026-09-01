using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Notifications;
using Quotinator.Data.Repositories;

using Quotinator.Data.Testing.Database;

namespace Quotinator.Data.Tests.Notifications;

/// <summary>
/// #302's payload, exercised through a real <c>Metadata</c> column rather than in memory. The file name
/// and its per-entity breakdown are what identify the confirmation, so they have to survive
/// serialization and come back as the same identity — an in-memory comparison would agree with itself
/// without ever proving the round-trip works.
/// </summary>
[TestClass]
public class ReseedFileAppliedMetadataTests
{
    public TestContext TestContext { get; set; } = null!;

    private string _tempDir = null!;
    private string _dbPath = null!;
    private NotificationWriter _writer = null!;
    private NotificationReader _reader = null!;

    [TestInitialize]
    public async Task TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_reseed_applied_metadata_test_").FullName;
        _dbPath = Path.Combine(_tempDir, "test.db");

        await CurrentSchema.ApplyDataSchemaAsync(_dbPath);

        SqliteConnectionFactory factory = new(_dbPath);
        _writer = new NotificationWriter(factory);
        _reader = TestNotificationReader.Create(factory);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>
    /// The file name and every entity type's counts survive the column round-trip, and the row reads
    /// back as the same identity — which is what the dedupe comparison depends on.
    /// </summary>
    [TestMethod]
    public async Task Payload_RoundTripsFileNameAndBreakdown()
    {
        ReseedFileAppliedMetadataDto written = new()
        {
            FileName = "quotinator-curated.json",
            Counts =
            [
                new ReseedEntityCountDto { EntityType = "Quote",  Added = 120, Modified = 3 },
                new ReseedEntityCountDto { EntityType = "Source", Added = 14,  Modified = 1 },
            ],
            ReleaseState = NotificationReleaseState.NotApplicable,
        };

        await NotificationSeeding.SeedWhileUnresolvedAsync(
            _reader, _writer, NotificationType.Success, written,
            body: "quotinator-curated.json reseeded cleanly.", appVersionId: null);

        NotificationEntity stored = (await _reader.GetPagedAsync(1, 0)).Items.Single();

        NotificationMetadataDto? read =
            NotificationMetadataKinds.TryDeserialize(stored.MetadataKind.Parsed, stored.Metadata);

        Assert.IsInstanceOfType<ReseedFileAppliedMetadataDto>(read,
            "The row's own MetadataKind must select the payload type, with no knowledge of which producer wrote it.");

        ReseedFileAppliedMetadataDto payload = (ReseedFileAppliedMetadataDto)read;
        Assert.AreEqual("quotinator-curated.json", payload.FileName);
        Assert.HasCount(2, payload.Counts);
        Assert.AreEqual("Quote", payload.Counts[0].EntityType);
        Assert.AreEqual(120, payload.Counts[0].Added);
        Assert.AreEqual(3, payload.Counts[0].Modified);
        Assert.AreEqual("Source", payload.Counts[1].EntityType);
        Assert.AreEqual(14, payload.Counts[1].Added);
        Assert.AreEqual(1, payload.Counts[1].Modified);

        Assert.IsTrue(written.IsSameNotificationAs(payload),
            "A stored payload must read back as the same identity, or dedupe compares against something it can never match.");
    }

    /// <summary>
    /// A different result for the same file is a different confirmation, and the order the producer
    /// happened to group its counts in is not part of that.
    /// <para>
    /// The ordering half is the one a natural implementation gets wrong: counts come from a
    /// <c>GroupBy</c> over the file's actions, whose order follows whatever order the actions were
    /// planned in. Two reseeds of an unchanged file would then produce two identities for one result,
    /// and the confirmation would re-announce itself on every reseed — the exact behaviour dedupe
    /// exists to prevent.
    /// </para>
    /// </summary>
    [TestMethod]
    public void Identity_DiffersByBreakdown_AndIsOrderIndependent()
    {
        ReseedFileAppliedMetadataDto quotesThenSources = Payload("a.json", ("Quote", 10, 0), ("Source", 2, 1));
        ReseedFileAppliedMetadataDto sourcesThenQuotes = Payload("a.json", ("Source", 2, 1), ("Quote", 10, 0));
        ReseedFileAppliedMetadataDto differentCounts   = Payload("a.json", ("Quote", 11, 0), ("Source", 2, 1));
        ReseedFileAppliedMetadataDto differentFile     = Payload("b.json", ("Quote", 10, 0), ("Source", 2, 1));
        ReseedFileAppliedMetadataDto fewerTypes        = Payload("a.json", ("Quote", 10, 0));

        Assert.IsTrue(quotesThenSources.IsSameNotificationAs(sourcesThenQuotes),
            "The same result grouped in a different order is the same confirmation — otherwise an unchanged file " +
            "re-notifies whenever the planner's action order shifts.");
        Assert.IsFalse(quotesThenSources.IsSameNotificationAs(differentCounts),
            "A different count is a different result, and the operator has not seen it yet.");
        Assert.IsFalse(quotesThenSources.IsSameNotificationAs(differentFile),
            "The same result for a different file is a different confirmation.");
        Assert.IsFalse(quotesThenSources.IsSameNotificationAs(fewerTypes),
            "A breakdown covering fewer entity types is a different result.");
    }

    private static ReseedFileAppliedMetadataDto Payload(string fileName, params (string Type, int Added, int Modified)[] counts) => new()
    {
        FileName = fileName,
        Counts = [.. counts.Select(c => new ReseedEntityCountDto { EntityType = c.Type, Added = c.Added, Modified = c.Modified })],
        ReleaseState = NotificationReleaseState.NotApplicable,
    };
}
