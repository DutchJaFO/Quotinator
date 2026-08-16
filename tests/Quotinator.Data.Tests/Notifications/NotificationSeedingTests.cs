using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Notifications;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Tests.Notifications;

/// <summary>
/// Exercises <see cref="NotificationSeeding"/> against a real SQLite schema (#279, relocated here and
/// rekeyed onto structured metadata by #312).
/// <para>
/// Deliberately a real-database test rather than one against fake reader/writer doubles, which is what
/// this covered while it lived in <c>Quotinator.Api.Tests</c>. The behaviour under test is now a JSON
/// payload written into a column and read back out to compare a key — a fake writer that records calls
/// in memory would report success without the payload ever surviving a round-trip through SQLite,
/// which is the only thing that actually matters here.
/// </para>
/// </summary>
[TestClass]
public class NotificationSeedingTests
{
    public TestContext TestContext { get; set; } = null!;

    // v1.8.3's announcement body, exactly as that release wrote it — the text migration 11's content
    // hash is taken over, and still the text Program.cs's producer writes today.
    private const string V183AnnouncementBody =
        "Two REST API operation IDs were renamed for naming consistency (issue #279): " +
        "GetImportBatches → GetAllImportBatches, and GetFileResources → GetAllFileResources. " +
        "This only affects a generated API client keyed by operation ID — routes and behaviour are unchanged.";

    private string _tempDir = null!;
    private string _dbPath = null!;
    private NotificationWriter _writer = null!;
    private NotificationReader _reader = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_notification_seeding_test_").FullName;
        _dbPath = Path.Combine(_tempDir, "test.db");

        using SqliteConnection conn = new($"Data Source={_dbPath}");
        conn.Open();
        // Same real migration sequence NotificationWriterTests replays — v1.8.0's CREATE, #81's
        // System_AppVersion (which #312's AppVersionId FK targets), then #312's reshape.
        conn.Execute(NotificationMigrations.CreateNotificationTable);
        conn.Execute(AppVersionMigrations.CreateAppVersionTable);
        conn.Execute(NotificationSchemaMigrations.SplitMessageAndAddMetadata);

        SqliteConnectionFactory factory = new(_dbPath);
        _writer = new NotificationWriter(factory);
        _reader = new NotificationReader(factory);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>An empty history writes, and returns the entity it wrote.</summary>
    [TestMethod]
    public async Task SeedOnceAsync_EmptyHistory_Writes()
    {
        NotificationEntity? written = await SeedAsync("some-announcement", "a body");

        Assert.IsNotNull(written);
        Assert.HasCount(1, (await _reader.GetPagedAsync(1, 0)).Items);
    }

    /// <summary>The same payload twice writes once — the whole point of the helper, across restarts.</summary>
    [TestMethod]
    public async Task SeedOnceAsync_SameIdentityTwice_WritesOnce()
    {
        await SeedAsync("some-announcement", "first body");
        NotificationEntity? second = await SeedAsync("some-announcement", "second body");

        Assert.IsNull(second, "The second call must report that it suppressed the write, not silently return an entity.");
        Assert.HasCount(1, (await _reader.GetPagedAsync(1, 0)).Items);
    }

    /// <summary>A different payload is a different notification.</summary>
    [TestMethod]
    public async Task SeedOnceAsync_DifferentIdentity_WritesAgain()
    {
        await SeedAsync("announcement-a", "body");
        await SeedAsync("announcement-b", "body");

        Assert.HasCount(2, (await _reader.GetPagedAsync(1, 0)).Items);
    }

    /// <summary>
    /// Two payloads of different kinds never collide, even with identical-looking identity values.
    /// Kind participates in identity, so a producer cannot accidentally suppress another's notification
    /// by choosing the same name.
    /// </summary>
    [TestMethod]
    public async Task SeedOnceAsync_SameValuesDifferentKind_BothWrite()
    {
        await SeedAsync("1.9.1", "an announcement that happens to be named like a version");
        await NotificationSeeding.SeedOnceAsync(
            _reader, _writer, NotificationType.Information,
            new WhatsNewMetadataDto { ReleaseState = NotificationReleaseState.Released, Version = "1.9.1" },
            body: "what's new in 1.9.1", appVersionId: null);

        Assert.HasCount(2, (await _reader.GetPagedAsync(1, 0)).Items);
    }

    /// <summary>
    /// The regression that motivated dropping composed key strings entirely: <c>1.9.1</c> is a
    /// substring of <c>1.9.10</c>, so #278's <c>Contains</c>-based check treated the second as already
    /// seeded. Comparing version values structurally has no substring relationship to get wrong.
    /// </summary>
    [TestMethod]
    public async Task SeedOnceAsync_VersionIsSubstringOfAnother_BothWrite()
    {
        await SeedWhatsNewAsync("1.9.1");
        await SeedWhatsNewAsync("1.9.10");

        Assert.HasCount(2, (await _reader.GetPagedAsync(1, 0)).Items,
            "A version that is a substring of another must not suppress it — exactly what the old Contains check got wrong.");
    }

    /// <summary>Identity comes from metadata, never body text — an identifier appearing only in prose must not suppress a write.</summary>
    [TestMethod]
    public async Task SeedOnceAsync_IdentityAppearsInBodyButNotMetadata_StillWrites()
    {
        await _writer.WriteAsync(NotificationType.Information, body: "this body mentions some-announcement in passing", appVersionId: null);

        NotificationEntity? written = await SeedAsync("some-announcement", "the real one");

        Assert.IsNotNull(written, "Matching body text is the behaviour #312 replaced; only metadata counts now.");
    }

    /// <summary>
    /// A row whose <c>MetadataKind</c> is set is read back as that exact type — the round-trip the
    /// column exists to make trivial. Guards against the comparison silently falling back to "unknown
    /// shape, cannot identify", which would make every notification re-announce itself forever.
    /// </summary>
    [TestMethod]
    public async Task SeedOnceAsync_StoredRowRoundTripsViaItsMetadataKind()
    {
        await SeedWhatsNewAsync("1.9.0");

        NotificationEntity stored = (await _reader.GetPagedAsync(1, 0)).Items.Single();
        NotificationMetadataDto? readBack =
            NotificationMetadataKinds.TryDeserialize(stored.MetadataKind.Parsed, stored.Metadata);

        Assert.IsInstanceOfType<WhatsNewMetadataDto>(readBack);
        Assert.AreEqual("1.9.0", ((WhatsNewMetadataDto)readBack!).Version);
    }

    /// <summary>
    /// A row predating #312 has no <c>Metadata</c> at all. It must be skipped rather than throwing —
    /// otherwise one legacy row would break seeding on every subsequent startup.
    /// </summary>
    [TestMethod]
    public async Task SeedOnceAsync_HistoryContainsPre312RowWithNoMetadata_StillWrites()
    {
        await _writer.WriteAsync(NotificationType.Warning, body: "a v1.8.0-era notification", appVersionId: null);

        NotificationEntity? written = await SeedAsync("some-announcement", "the new one");

        Assert.IsNotNull(written);
    }

    /// <summary>A derived payload's own properties survive the round-trip — serialization uses the runtime type, not the declared one.</summary>
    [TestMethod]
    public async Task SeedOnceAsync_DerivedMetadata_PersistsItsOwnProperties()
    {
        await NotificationSeeding.SeedOnceAsync(
            _reader, _writer, NotificationType.ActionRequired,
            new SchemaVersionOvershootMetadataDto
            {
                DataSchemaVersion = 7,
                AppSchemaVersion  = 5,
                ReleaseState      = NotificationReleaseState.NotApplicable,
            },
            body: "recorded version is ahead", appVersionId: null);

        NotificationEntity stored = (await _reader.GetPagedAsync(1, 0)).Items.Single();
        Assert.IsNotNull(stored.Metadata);

        SchemaVersionOvershootMetadataDto? readBack =
            JsonSerializer.Deserialize<SchemaVersionOvershootMetadataDto>(stored.Metadata);
        Assert.IsNotNull(readBack);
        Assert.AreEqual(7, readBack.DataSchemaVersion, "A derived property was lost — serialization used the declared type instead of the runtime one.");
        Assert.AreEqual(5, readBack.AppSchemaVersion);
        Assert.AreEqual(NotificationMetadataKind.SchemaVersionOvershoot, stored.MetadataKind.Parsed);
    }

    /// <summary>Seeding applies no expiry unless asked — #312 made expiry opt-in.</summary>
    [TestMethod]
    public async Task SeedOnceAsync_NoExpirySpecified_DoesNotExpire()
    {
        await SeedAsync("some-announcement", "a body");

        NotificationEntity stored = (await _reader.GetPagedAsync(1, 0)).Items.Single();
        Assert.IsNull(stored.ExpiresAt.Parsed);
    }

    /// <summary>
    /// A v1.8.3 notification, once the legacy backfills have run, suppresses the producer that would
    /// otherwise re-announce it — end to end through the writer and reader, not against the migration
    /// SQL alone.
    /// <para>
    /// Regression test for a duplicate reproduced against a real v1.8.3 database: #312 moved identity
    /// out of message text, so a row written before it could not be identified and #279's producer
    /// wrote a second copy on the first startup after upgrading. Every existing install was affected,
    /// not only development machines — v1.8.3 does write this notification, it simply takes longer than
    /// a short smoke check because first-boot seeding runs first.
    /// </para>
    /// <para>
    /// Replays migration 8 <i>and</i> 11, because identity now includes the common release fields:
    /// migration 8's metadata alone no longer deserializes, since the release state is required. The
    /// body text is v1.8.3's own, since the content hash is taken over exactly that text.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task SeedOnceAsync_LegacyRowBackfilledByTheMigrations_DoesNotWriteADuplicate()
    {
        // The v1.8.3 row exactly as that release wrote it: message text only, no Title, no Metadata,
        // and the always-on expiry #312 later made opt-in.
        await _writer.WriteAsync(
            NotificationType.Warning,
            body: V183AnnouncementBody,
            appVersionId: null,
            expiresAt: DateTime.UtcNow.AddDays(30));

        using (SqliteConnection connection = (SqliteConnection)new SqliteConnectionFactory(_dbPath).CreateConnection())
        {
            await connection.OpenAsync(TestContext.CancellationToken);
            await connection.ExecuteAsync(NotificationLegacyMetadataMigrations.BackfillAnnouncementMetadata);
            await connection.ExecuteAsync(NotificationLegacyMetadataMigrations.BackfillCommonReleaseFields);
        }

        NotificationEntity? written = await NotificationSeeding.SeedOnceAsync(
            _reader, _writer, NotificationType.Warning,
            new AnnouncementMetadataDto
            {
                Announcement = "GetAllImportBatches",
                ReleaseState = NotificationReleaseState.Released,
                Version      = "1.8.3",
                ContentHash  = NotificationContentHash.Of(V183AnnouncementBody),
            },
            body: V183AnnouncementBody, appVersionId: null);

        Assert.IsNull(written, "The backfilled v1.8.3 row must be recognised, so the producer writes nothing.");
        Assert.HasCount(1, (await _reader.GetPagedAsync(1, 0)).Items);
    }

    /// <summary>The backfill leaves an already-identified row alone, so replaying the chain cannot rewrite correct metadata.</summary>
    [TestMethod]
    public async Task Migration8_RowThatAlreadyHasMetadata_IsLeftUntouched()
    {
        await NotificationSeeding.SeedOnceAsync(
            _reader, _writer, NotificationType.Warning,
            new AnnouncementMetadataDto
            {
                Announcement = "SomethingElse",
                ReleaseState = NotificationReleaseState.NotApplicable,
            },
            body: "mentions GetAllImportBatches but was written after #312",
            appVersionId: null,
            title: "Its own title");

        using (SqliteConnection connection = (SqliteConnection)new SqliteConnectionFactory(_dbPath).CreateConnection())
        {
            await connection.OpenAsync(TestContext.CancellationToken);
            await connection.ExecuteAsync(NotificationLegacyMetadataMigrations.BackfillAnnouncementMetadata);
        }

        NotificationEntity stored = (await _reader.GetPagedAsync(1, 0)).Items.Single();
        Assert.Contains("SomethingElse", stored.Metadata!, "The backfill overwrote metadata it should have skipped.");
        Assert.AreEqual("Its own title", stored.Title);
    }

    /// <summary>
    /// The unreleased section states what it is, and stores no null-valued property to be interpreted.
    /// <para>
    /// Before #312's step 10 the two cases were told apart by <c>version</c> being null — a convention
    /// every reader had to know, and one that "not set", "failed to parse" and "the producer forgot" are
    /// all indistinguishable from. The stored payload now carries the state outright and omits the
    /// version entirely rather than writing <c>"version":null</c>.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task SeedOnceAsync_UnreleasedEntry_StoresAnExplicitStateAndNoNullVersion()
    {
        await NotificationSeeding.SeedOnceAsync(
            _reader, _writer, NotificationType.Information,
            new WhatsNewMetadataDto { ReleaseState = NotificationReleaseState.Unreleased, ContentHash = "AB12CD34" },
            body: "unreleased highlights", appVersionId: null);

        NotificationEntity stored = (await _reader.GetPagedAsync(1, 0)).Items.Single();

        Assert.Contains("\"releaseState\":\"Unreleased\"", stored.Metadata!,
            "The unreleased state must be stated in the payload, not inferred from an absent version.");
        Assert.DoesNotContain("version", stored.Metadata!, StringComparison.OrdinalIgnoreCase,
            "An unreleased entry has no version, so the property is omitted rather than stored as null.");
    }

    /// <summary>
    /// A released and an unreleased entry are never the same notification, even when every other
    /// identity component coincides — which is exactly why the state is part of the identity rather than
    /// merely stored alongside it.
    /// </summary>
    [TestMethod]
    public async Task SeedOnceAsync_ReleasedAndUnreleasedWithIdenticalComponents_BothWrite()
    {
        WhatsNewMetadataDto released = new()
        {
            ReleaseState = NotificationReleaseState.Released,
            ContentHash  = "AB12CD34",
        };
        WhatsNewMetadataDto unreleased = new()
        {
            ReleaseState = NotificationReleaseState.Unreleased,
            ContentHash  = "AB12CD34",
        };

        await NotificationSeeding.SeedOnceAsync(
            _reader, _writer, NotificationType.Information, released, body: "released", appVersionId: null);
        NotificationEntity? second = await NotificationSeeding.SeedOnceAsync(
            _reader, _writer, NotificationType.Information, unreleased, body: "unreleased", appVersionId: null);

        Assert.IsNotNull(second, "The release state is part of the identity — these are two different notifications.");
        Assert.HasCount(2, (await _reader.GetPagedAsync(1, 0)).Items);
    }

    private Task<NotificationEntity?> SeedAsync(string announcement, string body) =>
        NotificationSeeding.SeedOnceAsync(
            _reader, _writer, NotificationType.Information,
            new AnnouncementMetadataDto
            {
                Announcement = announcement,
                ReleaseState = NotificationReleaseState.NotApplicable,
            },
            body: body, appVersionId: null);

    private Task<NotificationEntity?> SeedWhatsNewAsync(string version) =>
        NotificationSeeding.SeedOnceAsync(
            _reader, _writer, NotificationType.Information,
            new WhatsNewMetadataDto { ReleaseState = NotificationReleaseState.Released, Version = version },
            body: $"highlights for {version}", appVersionId: null);
}
