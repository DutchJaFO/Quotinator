using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Notifications;
using Quotinator.Data.Repositories;

using Quotinator.Data.Testing.Database;

namespace Quotinator.Data.Tests.Notifications;

/// <summary>
/// #304's payload, exercised through a real <c>Metadata</c> column rather than in memory. The reason and
/// the changed-file set are what identify the recommendation, so they have to survive serialization and
/// come back as the same identity — an in-memory comparison would agree with itself without ever
/// proving the round-trip works.
/// </summary>
[TestClass]
public class ReseedRecommendedMetadataTests
{
    public TestContext TestContext { get; set; } = null!;

    private string _tempDir = null!;
    private string _dbPath = null!;
    private NotificationWriter _writer = null!;
    private NotificationReader _reader = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_reseed_metadata_test_").FullName;
        _dbPath = Path.Combine(_tempDir, "test.db");

        using SqliteConnection conn = new($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute(NotificationMigrations.CreateNotificationTable);
        conn.Execute(AppVersionMigrations.CreateAppVersionTable);
        conn.Execute(NotificationSchemaMigrations.SplitMessageAndAddMetadata);
        conn.Execute(NotificationTranslationMigrations.AddOriginalLanguageColumn);
        conn.Execute(NotificationTranslationMigrations.CreateNotificationTranslationTable);
        // #304's own rebuild — without it the CHECK rejects 'ReseedRecommended' and every test here
        // fails on the write rather than on what it is actually asserting.
        conn.Execute(NotificationReseedTriggerMigrations.WidenDismissTriggerAndMetadataKind);
        // #304: the read projection selects DismissReason.
        conn.Execute(NotificationDismissReasonMigrations.AddDismissReasonColumn);

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
    /// The payload's own properties survive the column round-trip, and the row reads back as the same
    /// identity — which is what the dedupe comparison depends on.
    /// </summary>
    [TestMethod]
    public async Task Payload_RoundTripsReasonAndChangedFiles()
    {
        ReseedRecommendedMetadataDto written = new()
        {
            Reason = ReseedReason.ContentChanged,
            ChangedFiles = ["vilaboim_movie-quotes.json", "quotinator-curated.json"],
            ReleaseState = NotificationReleaseState.NotApplicable,
        };

        await NotificationSeeding.SeedWhileUnresolvedAsync(
            _reader, _writer, NotificationType.ActionRequired, written,
            body: "Two source files changed.", appVersionId: null,
            dismissTrigger: NotificationDismissTrigger.Reseed);

        NotificationEntity stored = (await _reader.GetPagedAsync(1, 0)).Items.Single();

        NotificationMetadataDto? read =
            NotificationMetadataKinds.TryDeserialize(stored.MetadataKind.Parsed, stored.Metadata);

        Assert.IsInstanceOfType<ReseedRecommendedMetadataDto>(read,
            "The row's own MetadataKind must select the payload type, with no knowledge of which producer wrote it.");

        ReseedRecommendedMetadataDto payload = (ReseedRecommendedMetadataDto)read;
        Assert.AreEqual(ReseedReason.ContentChanged, payload.Reason);
        Assert.AreSequenceEqual(written.ChangedFiles, payload.ChangedFiles);
        Assert.IsTrue(written.IsSameNotificationAs(payload),
            "A stored payload must read back as the same identity, or dedupe compares against something it can never match.");
    }

    /// <summary>
    /// A different set of changed files is a different recommendation. The positive control for the
    /// assertion above: identity that matched everything would satisfy the round-trip just as happily.
    /// </summary>
    [TestMethod]
    public void Identity_DiffersByChangedFileSet_AndByReason()
    {
        ReseedRecommendedMetadataDto twoFiles = Payload(ReseedReason.ContentChanged, "a.json", "b.json");
        ReseedRecommendedMetadataDto sameTwoFiles = Payload(ReseedReason.ContentChanged, "a.json", "b.json");
        ReseedRecommendedMetadataDto oneFile = Payload(ReseedReason.ContentChanged, "a.json");
        ReseedRecommendedMetadataDto afterReset = Payload(ReseedReason.AfterReset);

        Assert.IsTrue(twoFiles.IsSameNotificationAs(sameTwoFiles),
            "The same condition recurring is the same recommendation — otherwise it re-notifies on every restart.");
        Assert.IsFalse(twoFiles.IsSameNotificationAs(oneFile),
            "A different set of changed files is a different recommendation.");
        Assert.IsFalse(twoFiles.IsSameNotificationAs(afterReset),
            "A different reason is a different recommendation, even though both recommend the same action.");
    }

    private static ReseedRecommendedMetadataDto Payload(ReseedReason reason, params string[] changedFiles) => new()
    {
        Reason = reason,
        ChangedFiles = changedFiles,
        ReleaseState = NotificationReleaseState.NotApplicable,
    };
}
