using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;
using Quotinator.Data.Notifications;
using Quotinator.Data.Testing.Database;

namespace Quotinator.Data.Tests.Database;

/// <summary>
/// Exercises the data-only backfills that repair rows written before #312's shape existed —
/// <see cref="NotificationLegacyMetadataMigrations.BackfillAnnouncementProvenance"/> (migration 9) and
/// <see cref="NotificationLegacyMetadataMigrations.BackfillWhatsNewReleaseState"/> (migration 10).
/// <para>
/// Real SQLite against the genuine migration chain, not a hand-written "current shape": both backfills
/// exist precisely because of what earlier builds actually stored, so a fixture that skips those builds
/// would be testing a database no upgrade ever produces.
/// </para>
/// </summary>
[TestClass]
public class NotificationLegacyBackfillMigrationTests
{
    // Everything up to and including migration 8, i.e. the state a database is in when migration 9 runs.
    private static readonly string[] SchemaThroughMigration8 =
    [
        NotificationMigrations.CreateNotificationTable,
        AppVersionMigrations.CreateAppVersionTable,
        NotificationSchemaMigrations.SplitMessageAndAddMetadata,
        AppVersionHistoryMigrations.AddApplicationColumn,
        AppVersionHistoryMigrations.AddSequenceNumberColumn,
    ];

    private const string LegacyAnnouncementMetadata = """{"announcement":"GetAllImportBatches"}""";

    // v1.8.3's announcement body, exactly as that release wrote it — the text migration 11's content
    // hash is taken over, and still the text Program.cs's producer writes today.
    private const string V183AnnouncementBody =
        "Two REST API operation IDs were renamed for naming consistency (issue #279): " +
        "GetImportBatches → GetAllImportBatches, and GetFileResources → GetAllFileResources. " +
        "This only affects a generated API client keyed by operation ID — routes and behaviour are unchanged.";

    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// The v1.8.3 announcement gains provenance, and the <c>System_AppVersion</c> row it points at is
    /// created — v1.8.3 is the only version that could have written that notification, so its writer is
    /// knowable rather than a guess.
    /// </summary>
    [TestMethod]
    public async Task Migration9_LegacyAnnouncementPresent_CreatesTheV183RowAndLinksTheNotificationToIt()
    {
        using TempDatabase temp = new(SchemaThroughMigration8);
        using SqliteConnection connection = await OpenAsync(temp);

        await SeedLegacyAnnouncementAsync(connection);
        await connection.ExecuteAsync(NotificationLegacyMetadataMigrations.BackfillAnnouncementProvenance);

        (string? application, string? version) =
            await connection.QuerySingleAsync<(string?, string?)>(
                "SELECT Application, Version FROM System_AppVersion;");

        Assert.AreEqual("Quotinator.Api", application);
        Assert.AreEqual("1.8.3", version);

        string? linkedVersion = await connection.ExecuteScalarAsync<string>(
            "SELECT v.Version FROM System_Notification n JOIN System_AppVersion v ON LOWER(v.Id) = LOWER(n.AppVersionId);");

        Assert.AreEqual("1.8.3", linkedVersion,
            "The legacy announcement must join to the version that wrote it, exactly as a notification written after #312 does.");
    }

    /// <summary>
    /// A database that never ran v1.8.3 gains no v1.8.3 row. It reaches this migration by having been
    /// created fresh at an intermediate #312 build's baseline and then upgraded — history it never had
    /// must not be invented for it, which is why the insert is conditional on the legacy notification
    /// actually being there.
    /// </summary>
    [TestMethod]
    public async Task Migration9_NoLegacyAnnouncement_InsertsNothing()
    {
        using TempDatabase temp = new(SchemaThroughMigration8);
        using SqliteConnection connection = await OpenAsync(temp);

        await connection.ExecuteAsync(NotificationLegacyMetadataMigrations.BackfillAnnouncementProvenance);

        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM System_AppVersion;"),
            "Nothing proves v1.8.3 ever ran here, so nothing may claim it did.");
    }

    /// <summary>
    /// An announcement written after #312 already carries provenance. It is not a legacy row, and the
    /// backfill must neither re-point it nor treat its presence as evidence that v1.8.3 ran.
    /// </summary>
    [TestMethod]
    public async Task Migration9_AnnouncementAlreadyCarryingProvenance_IsLeftUntouched()
    {
        using TempDatabase temp = new(SchemaThroughMigration8);
        using SqliteConnection connection = await OpenAsync(temp);

        Guid appVersionId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO System_AppVersion (Id, Application, Version, DateCreated, SequenceNumber) " +
            "VALUES (@id, 'Quotinator.Api', '1.9.0', '2026-08-16 10:00:00', 1);",
            new { id = appVersionId.ToString() });
        await SeedLegacyAnnouncementAsync(connection, appVersionId);

        await connection.ExecuteAsync(NotificationLegacyMetadataMigrations.BackfillAnnouncementProvenance);

        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM System_AppVersion;"),
            "An already-attributed announcement is not evidence of a v1.8.3 run.");
        string? linked = await connection.ExecuteScalarAsync<string>("SELECT AppVersionId FROM System_Notification;");
        Assert.AreEqual(appVersionId.ToString(), linked,
            "The backfill re-pointed a notification that already had provenance.");
    }

    /// <summary>
    /// The backfilled row sorts *before* whatever history the database already holds. v1.8.3 predates
    /// every row this table can contain — <c>System_AppVersion</c> did not exist in v1.8.3 — so
    /// appending at the end would make "the version that ran last" answer 1.8.3 on a machine that has
    /// since run newer builds, and #81's catch-up would replay releases it already announced.
    /// </summary>
    [TestMethod]
    public async Task Migration9_DatabaseWithLaterHistory_PlacesV183BeforeIt()
    {
        using TempDatabase temp = new(SchemaThroughMigration8);
        using SqliteConnection connection = await OpenAsync(temp);

        await connection.ExecuteAsync(
            "INSERT INTO System_AppVersion (Id, Application, Version, DateCreated, SequenceNumber) " +
            "VALUES (@id, 'Quotinator.Api', '1.8.4', '2026-08-16 10:00:00', 1);",
            new { id = Guid.NewGuid().ToString() });
        await SeedLegacyAnnouncementAsync(connection);

        await connection.ExecuteAsync(NotificationLegacyMetadataMigrations.BackfillAnnouncementProvenance);

        List<string> byRecordingOrder = [.. await connection.QueryAsync<string>(
            "SELECT Version FROM System_AppVersion ORDER BY SequenceNumber;")];

        Assert.AreSequenceEqual<string>(["1.8.3", "1.8.4"], byRecordingOrder,
            "The backfilled 1.8.3 row must sort before history the database already holds — otherwise " +
            "\"the version that ran last\" answers 1.8.3 on a machine that has since run newer builds.");
    }

    /// <summary>
    /// What's-new rows written by an intermediate #312 build carry no release state — step 10 made it a
    /// required property, so without the backfill those rows cannot be deserialized, cannot be
    /// identified, and re-announce themselves. The state is derived from the very convention that wrote
    /// them: a <c>version</c> key present meant a tagged release, absent meant the unreleased section.
    /// </summary>
    [TestMethod]
    public async Task Migration10_LegacyWhatsNewRows_GainTheReleaseStateTheirOwnConventionImplied()
    {
        using TempDatabase temp = new(SchemaThroughMigration8);
        using SqliteConnection connection = await OpenAsync(temp);

        await SeedNotificationAsync(connection, """{"version":"1.8.4"}""", "WhatsNew");
        await SeedNotificationAsync(connection, """{"contentHash":"AB12CD34"}""", "WhatsNew");

        await connection.ExecuteAsync(NotificationLegacyMetadataMigrations.BackfillWhatsNewReleaseState);

        List<string> states = [.. await connection.QueryAsync<string>(
            "SELECT json_extract(Metadata, '$.releaseState') FROM System_Notification ORDER BY Metadata;")];

        Assert.Contains("Released", states, "A row carrying a version described a tagged release.");
        Assert.Contains("Unreleased", states, "A row carrying only a content hash described the unreleased section.");
    }

    /// <summary>A row already carrying a release state is left exactly as it is — replaying the chain cannot rewrite correct data.</summary>
    [TestMethod]
    public async Task Migration10_RowThatAlreadyHasAReleaseState_IsLeftUntouched()
    {
        using TempDatabase temp = new(SchemaThroughMigration8);
        using SqliteConnection connection = await OpenAsync(temp);

        await SeedNotificationAsync(connection, """{"releaseState":"Unreleased","version":"1.9.0"}""", "WhatsNew");

        await connection.ExecuteAsync(NotificationLegacyMetadataMigrations.BackfillWhatsNewReleaseState);

        string? state = await connection.ExecuteScalarAsync<string>(
            "SELECT json_extract(Metadata, '$.releaseState') FROM System_Notification;");

        Assert.AreEqual("Unreleased", state, "The backfill overwrote a state the row already stated for itself.");
    }

    /// <summary>A notification of another kind is none of this migration's business, whatever its payload looks like.</summary>
    [TestMethod]
    public async Task Migration10_NonWhatsNewRow_IsLeftUntouched()
    {
        using TempDatabase temp = new(SchemaThroughMigration8);
        using SqliteConnection connection = await OpenAsync(temp);

        await SeedNotificationAsync(connection, LegacyAnnouncementMetadata, "Announcement");

        await connection.ExecuteAsync(NotificationLegacyMetadataMigrations.BackfillWhatsNewReleaseState);

        string? metadata = await connection.ExecuteScalarAsync<string>("SELECT Metadata FROM System_Notification;");
        Assert.AreEqual(LegacyAnnouncementMetadata, metadata);
    }

    /// <summary>
    /// The legacy announcement gains the release fields that stopped being what's-new-specific, with
    /// values that are historical fact rather than guesses: v1.8.3 shipped the operation-id renames,
    /// and its body text shipped with that release.
    /// </summary>
    [TestMethod]
    public async Task Migration11_LegacyAnnouncement_GainsTheCommonReleaseFields()
    {
        using TempDatabase temp = new(SchemaThroughMigration8);
        using SqliteConnection connection = await OpenAsync(temp);

        await SeedLegacyAnnouncementAsync(connection);
        await connection.ExecuteAsync(NotificationLegacyMetadataMigrations.BackfillCommonReleaseFields);

        string? metadata = await connection.ExecuteScalarAsync<string>("SELECT Metadata FROM System_Notification;");

        Assert.IsNotNull(metadata);
        Assert.Contains("\"releaseState\":\"Released\"", metadata);
        Assert.Contains("\"version\":\"1.8.3\"", metadata);
        Assert.Contains($"\"contentHash\":\"{NotificationContentHash.Of(V183AnnouncementBody)}\"", metadata,
            "The backfilled hash must equal what the producer computes for the same text, or the announcement " +
            "is unidentifiable and gets written a second time.");
    }

    /// <summary>
    /// The real point of the row above: once backfilled, the producer recognises it and writes nothing.
    /// The hash comparison alone would pass against a matching pair of wrong values.
    /// </summary>
    [TestMethod]
    public async Task Migration11_BackfilledAnnouncement_IsRecognisedByTheProducer()
    {
        using TempDatabase temp = new(SchemaThroughMigration8);
        using SqliteConnection connection = await OpenAsync(temp);

        await SeedLegacyAnnouncementAsync(connection);
        await connection.ExecuteAsync(NotificationLegacyMetadataMigrations.BackfillCommonReleaseFields);

        string metadata = (await connection.ExecuteScalarAsync<string>("SELECT Metadata FROM System_Notification;"))!;
        NotificationMetadataDto? stored =
            NotificationMetadataKinds.TryDeserialize(NotificationMetadataKind.Announcement, metadata);

        // Exactly what Program.cs's #279 producer builds.
        AnnouncementMetadataDto current = new()
        {
            Announcement = "GetAllImportBatches",
            ReleaseState = NotificationReleaseState.Released,
            Version      = "1.8.3",
            ContentHash  = NotificationContentHash.Of(V183AnnouncementBody),
        };

        Assert.IsNotNull(stored, "A backfilled row that cannot be deserialized identifies nothing and re-announces itself.");
        Assert.IsTrue(current.IsSameNotificationAs(stored),
            $"The producer no longer recognises its own backfilled row. Stored: {metadata}");
    }

    /// <summary>A notification about no release says so, rather than borrowing the running version.</summary>
    [TestMethod]
    public async Task Migration11_LegacySchemaOvershoot_StatesThatNoReleaseApplies()
    {
        using TempDatabase temp = new(SchemaThroughMigration8);
        using SqliteConnection connection = await OpenAsync(temp);

        await SeedNotificationAsync(connection, """{"dataSchemaVersion":11,"appSchemaVersion":5}""", "SchemaVersionOvershoot");
        await connection.ExecuteAsync(NotificationLegacyMetadataMigrations.BackfillCommonReleaseFields);

        string? metadata = await connection.ExecuteScalarAsync<string>("SELECT Metadata FROM System_Notification;");

        Assert.Contains("\"releaseState\":\"NotApplicable\"", metadata!);
        Assert.DoesNotContain("version\":\"", metadata!,
            "An overshoot is not about a release, so no version may be invented for it.");
    }

    /// <summary>A row already stating its own release state is untouched — replaying cannot rewrite correct data.</summary>
    [TestMethod]
    public async Task Migration11_RowThatAlreadyStatesItsReleaseState_IsLeftUntouched()
    {
        using TempDatabase temp = new(SchemaThroughMigration8);
        using SqliteConnection connection = await OpenAsync(temp);

        const string alreadyStated = """{"announcement":"SomethingElse","releaseState":"Unreleased"}""";
        await SeedNotificationAsync(connection, alreadyStated, "Announcement");

        await connection.ExecuteAsync(NotificationLegacyMetadataMigrations.BackfillCommonReleaseFields);

        Assert.AreEqual(alreadyStated, await connection.ExecuteScalarAsync<string>("SELECT Metadata FROM System_Notification;"));
    }

    private async Task<SqliteConnection> OpenAsync(TempDatabase temp)
    {
        SqliteConnection connection = (SqliteConnection)temp.ConnectionFactory.CreateConnection();
        await connection.OpenAsync(TestContext.CancellationToken);
        return connection;
    }

    // The v1.8.3 announcement as migration 8 leaves it: metadata and kind backfilled, provenance still
    // null because the column did not exist when v1.8.3 wrote the row.
    private static Task<int> SeedLegacyAnnouncementAsync(SqliteConnection connection, Guid? appVersionId = null) =>
        SeedNotificationAsync(connection, LegacyAnnouncementMetadata, "Announcement", appVersionId);

    private static Task<int> SeedNotificationAsync(
        SqliteConnection connection, string metadata, string metadataKind, Guid? appVersionId = null) =>
        connection.ExecuteAsync(
            "INSERT INTO System_Notification (Id, Type, Body, DateCreated, IsDismissed, IsDeleted, Metadata, MetadataKind, AppVersionId) " +
            "VALUES (@id, 'Warning', 'a body', '2026-08-16 09:00:00', 0, 0, @metadata, @metadataKind, @appVersionId);",
            new
            {
                id           = Guid.NewGuid().ToString(),
                metadata,
                metadataKind,
                appVersionId = appVersionId?.ToString(),
            });
}
