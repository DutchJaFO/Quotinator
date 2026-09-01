using System.Text.Json;
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
            Origin = FileResourceOrigin.User,
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
        Assert.AreEqual(FileResourceOrigin.User, payload.Origin, "Origin must survive the column round-trip, or identity cannot use it.");
        Assert.HasCount(2, payload.Counts);
        Assert.AreEqual("Quote", payload.Counts[0].EntityType);
        Assert.AreEqual(120, payload.Counts[0].Added);
        Assert.AreEqual(3, payload.Counts[0].Modified);
        Assert.AreEqual("Source", payload.Counts[1].EntityType);
        Assert.AreEqual(14, payload.Counts[1].Added);
        Assert.AreEqual(1, payload.Counts[1].Modified);

        Assert.IsTrue(written.IsSameNotificationAs(payload),
            "A stored payload must read back as the same identity, or dedupe compares against something it can never match.");

        AssertWireNames(stored.Metadata!);
    }

    /// <summary>
    /// Pins the property names actually written to the <c>Metadata</c> column.
    /// </summary>
    /// <remarks>
    /// The round-trip assertions above cannot do this: they write and read with the same DTO, so
    /// renaming a <c>[JsonPropertyName]</c> changes both sides identically and every one of them still
    /// passes — measured, 2026-09-01. What that proves is that serialization is self-consistent, which
    /// was never in doubt.
    /// <para>
    /// The wire names are a real contract because the column outlives the build that wrote it: a rename
    /// silently stops a new build from reading rows an old one stored, which is the exact class of
    /// change <c>NotificationLegacyMetadataMigrations</c> exists to repair. Changing a name here is
    /// allowed — it just has to be a decision, with the backfill that goes with it, rather than a
    /// refactor nothing notices.
    /// </para>
    /// </remarks>
    /// <param name="metadata">The stored JSON.</param>
    private static void AssertWireNames(string metadata)
    {
        using JsonDocument document = JsonDocument.Parse(metadata);
        List<string> top = [.. document.RootElement.EnumerateObject().Select(p => p.Name)];

        foreach (string expected in (string[])["fileName", "origin", "counts"])
            Assert.Contains(expected, top, $"'{expected}' is the stored wire name and rows already carry it.");

        List<string> perCount = [.. document.RootElement.GetProperty("counts")[0].EnumerateObject().Select(p => p.Name)];
        foreach (string expected in (string[])["entityType", "added", "modified"])
            Assert.Contains(expected, perCount, $"'{expected}' is the stored wire name inside each count.");
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

    /// <summary>
    /// Two files of the same name from different directories are two files, and must not collapse into
    /// one notification.
    /// <para>
    /// Found live during #302's T2 pass, running the bundled + user-imports variant: a user copy of
    /// <c>quotinator-curated.json</c> applied after the bundled one had already added everything, so
    /// its breakdown filtered to empty. Both were written that time only because their breakdowns
    /// happened to differ — had the bundled copy also been a no-op, the two would have shared an
    /// identity and the second would have been silently suppressed.
    /// </para>
    /// </summary>
    [TestMethod]
    public void Identity_DiffersByOrigin_ForTheSameFileNameAndBreakdown()
    {
        ReseedFileAppliedMetadataDto bundled = Payload("quotinator-curated.json", FileResourceOrigin.System);
        ReseedFileAppliedMetadataDto user    = Payload("quotinator-curated.json", FileResourceOrigin.User);

        Assert.IsFalse(bundled.IsSameNotificationAs(user),
            "Same bare file name, same empty breakdown, different directory — two files, so two confirmations.");
    }

    private static ReseedFileAppliedMetadataDto Payload(string fileName, params (string Type, int Added, int Modified)[] counts) =>
        Payload(fileName, FileResourceOrigin.System, counts);

    private static ReseedFileAppliedMetadataDto Payload(
        string fileName, FileResourceOrigin origin, params (string Type, int Added, int Modified)[] counts) => new()
    {
        FileName = fileName,
        Origin = origin,
        Counts = [.. counts.Select(c => new ReseedEntityCountDto { EntityType = c.Type, Added = c.Added, Modified = c.Modified })],
        ReleaseState = NotificationReleaseState.NotApplicable,
    };
}
