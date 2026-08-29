using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Notifications;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.Database;

namespace Quotinator.Data.Tests.Repositories;

/// <summary>
/// Covers #319's translated notification title/body — the <c>OriginalLanguage</c> column, the
/// <c>System_NotificationTranslation</c> table, and the read-time resolution built on them.
/// <para>
/// Real SQLite against the genuine migration constants, not a hand-written "current shape", matching
/// <see cref="Database.NotificationLegacyBackfillMigrationTests"/>: the backfill exists because of what
/// earlier builds actually stored, so a fixture that skips those builds tests a database no upgrade
/// ever produces.
/// </para>
/// </summary>
[TestClass]
public class NotificationTranslationTests
{
    // Everything up to and including migration 11 — the state a database is in when #319's own
    // migrations run. Listed explicitly rather than derived, so a later migration added to
    // DataOwnedMigrations cannot silently change what "the schema before #319" means.
    private static readonly string[] SchemaThroughMigration11 =
    [
        NotificationMigrations.CreateNotificationTable,
        AppVersionMigrations.CreateAppVersionTable,
        NotificationSchemaMigrations.SplitMessageAndAddMetadata,
        AppVersionHistoryMigrations.AddApplicationColumn,
        AppVersionHistoryMigrations.AddSequenceNumberColumn,
    ];

    private static readonly string[] RecordBaseColumns =
        ["Id", "DateCreated", "DateModified", "DateDeleted", "IsDeleted"];

    // The two non-original languages, alphabetically — the order the assertion's own ORDER BY produces.
    private static readonly string[] ExpectedBackfilledLanguages = ["de", "nl"];

    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// A notification written before #319 existed reads back as English rather than as an empty or
    /// null language. Every notification written to date is English, so <c>'en'</c> is a statement of
    /// fact about the existing corpus, not a guess — and the read path's fallback depends on the
    /// column being populated for exactly these rows.
    /// </summary>
    [TestMethod]
    public async Task Migration_ExistingRows_DefaultToEnglishOriginalLanguage()
    {
        using TempDatabase temp = new(SchemaThroughMigration11);
        using SqliteConnection connection = await OpenAsync(temp);

        string id = Guid.NewGuid().ToString();
        await connection.ExecuteAsync(
            "INSERT INTO System_Notification (Id, Type, Body, DateCreated, IsDeleted, IsDismissed) " +
            "VALUES (@id, 'Warning', 'Written before #319 existed.', @now, 0, 0);",
            new { id, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });

        await connection.ExecuteAsync(NotificationTranslationMigrations.AddOriginalLanguageColumn);

        string? originalLanguage = await connection.ExecuteScalarAsync<string>(
            "SELECT OriginalLanguage FROM System_Notification WHERE LOWER(Id) = LOWER(@id);", new { id });

        Assert.AreEqual("en", originalLanguage,
            "A pre-#319 notification must be recorded as English — the read path falls back to " +
            "OriginalLanguage, so an empty value would make the fallback resolve to nothing.");
    }

    /// <summary>
    /// <c>System_NotificationTranslation</c> carries RecordBase's audit columns. ADR 002 applies to
    /// every table without exception, including a translation/child table — the precedent this issue
    /// mirrors, <c>Quotinator_QuoteTranslation</c>, carries them too.
    /// </summary>
    [TestMethod]
    public async Task NotificationTranslationTable_HasRecordBaseColumns()
    {
        using TempDatabase temp = new(SchemaThroughMigration11);
        using SqliteConnection connection = await OpenAsync(temp);

        await connection.ExecuteAsync(NotificationTranslationMigrations.CreateNotificationTranslationTable);

        HashSet<string> columns = (await connection.QueryAsync<string>(
            "SELECT name FROM pragma_table_info('System_NotificationTranslation');"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string recordBaseColumn in RecordBaseColumns)
            Assert.Contains(recordBaseColumn, columns,
                $"System_NotificationTranslation is missing RecordBase column {recordBaseColumn} (ADR 002).");

        foreach (string ownColumn in new[] { "NotificationId", "Language", "Title", "Body" })
            Assert.Contains(ownColumn, columns,
                $"System_NotificationTranslation is missing its own column {ownColumn}.");
    }

    /// <summary>
    /// v1.8.3's shipped announcement — the only notification any released build persisted — gains its
    /// Dutch and German translations, and the original English stays on the notification row itself.
    /// </summary>
    [TestMethod]
    public async Task Migration_LegacyAnnouncementPresent_GainsDutchAndGermanTranslations()
    {
        using TempDatabase temp = new(SchemaThroughMigration11);
        using SqliteConnection connection = await OpenAsync(temp);

        await SeedLegacyAnnouncementAsync(connection);
        await ApplyTranslationSchemaAsync(connection);
        await connection.ExecuteAsync(NotificationTranslationMigrations.BackfillAnnouncementTranslations);

        List<string> languages = [.. await connection.QueryAsync<string>(
            "SELECT Language FROM System_NotificationTranslation ORDER BY Language;")];

        Assert.AreSequenceEqual(ExpectedBackfilledLanguages, languages,
            "The legacy announcement must gain exactly the two non-original languages.");

        string? dutchTitle = await connection.ExecuteScalarAsync<string>(
            "SELECT Title FROM System_NotificationTranslation WHERE Language = 'nl';");
        Assert.AreEqual("Twee API-bewerkings-ID's zijn hernoemd", dutchTitle);

        string? originalBody = await connection.ExecuteScalarAsync<string>(
            "SELECT Body FROM System_Notification;");
        Assert.Contains("GetAllImportBatches", originalBody!,
            "The original English body must stay on the notification row — the read path's COALESCE " +
            "falls back to it, and every producer's content hash is taken over it.");
    }

    /// <summary>
    /// A database that never ran v1.8.3 gains nothing. The backfill is conditional on the row being
    /// there, which is what stops it inventing content a database has no business carrying.
    /// </summary>
    [TestMethod]
    public async Task Migration_NoLegacyAnnouncement_WritesNoTranslations()
    {
        using TempDatabase temp = new(SchemaThroughMigration11);
        using SqliteConnection connection = await OpenAsync(temp);

        await ApplyTranslationSchemaAsync(connection);
        await connection.ExecuteAsync(NotificationTranslationMigrations.BackfillAnnouncementTranslations);

        int translations = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM System_NotificationTranslation;");

        Assert.AreEqual(0, translations, "Nothing to translate means nothing written.");
    }

    /// <summary>Running the backfill twice leaves one translation per language, not two.</summary>
    [TestMethod]
    public async Task Migration_AppliedTwice_LeavesOneTranslationPerLanguage()
    {
        using TempDatabase temp = new(SchemaThroughMigration11);
        using SqliteConnection connection = await OpenAsync(temp);

        await SeedLegacyAnnouncementAsync(connection);
        await ApplyTranslationSchemaAsync(connection);
        await connection.ExecuteAsync(NotificationTranslationMigrations.BackfillAnnouncementTranslations);
        await connection.ExecuteAsync(NotificationTranslationMigrations.BackfillAnnouncementTranslations);

        int translations = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM System_NotificationTranslation;");

        Assert.AreEqual(2, translations,
            "The NOT EXISTS guard makes each language's insert idempotent.");
    }

    // ── Read-path resolution (steps 4/5) ─────────────────────────────────────

    /// <summary>A language with a translation returns that translation's title and body.</summary>
    [TestMethod]
    public async Task Read_RequestedLanguageHasTranslation_ReturnsTranslatedTitleAndBody()
    {
        using TempDatabase temp = new(SchemaThroughMigration11);
        NotificationReader reader = await SeedTranslatedNotificationAsync(temp);

        PagedItems<NotificationEntity> page = await reader.GetPagedAsync(1, 10, "nl");
        NotificationEntity row = page.Items.Single();

        Assert.AreEqual("Nederlandse kop", row.Title);
        Assert.AreEqual("Nederlandse tekst.", row.Body);
    }

    /// <summary>A language with no translation transparently falls back to the original text.</summary>
    [TestMethod]
    public async Task Read_RequestedLanguageHasNoTranslation_FallsBackToOriginalLanguage()
    {
        using TempDatabase temp = new(SchemaThroughMigration11);
        NotificationReader reader = await SeedTranslatedNotificationAsync(temp);

        PagedItems<NotificationEntity> page = await reader.GetPagedAsync(1, 10, "fr");
        NotificationEntity row = page.Items.Single();

        Assert.AreEqual("English headline", row.Title);
        Assert.AreEqual("English body.", row.Body);
        Assert.AreEqual("en", row.EffectiveLanguage,
            "Falling back must report the language actually returned, not the one asked for.");
    }

    /// <summary>A row with no translations at all still renders — the legacy case.</summary>
    [TestMethod]
    public async Task Read_LegacyRowWithNoTranslations_ReturnsStoredEnglishText()
    {
        using TempDatabase temp = new(SchemaThroughMigration11);
        using SqliteConnection connection = await OpenAsync(temp);
        await ApplyTranslationSchemaAsync(connection);
        await InsertNotificationAsync(connection, "English headline", "English body.");

        NotificationReader reader = CreateReader(temp);
        PagedItems<NotificationEntity> page = await reader.GetPagedAsync(1, 10, "nl");
        NotificationEntity row = page.Items.Single();

        Assert.AreEqual("English body.", row.Body, "A notification with no translations must never render empty.");
        Assert.AreEqual("en", row.EffectiveLanguage);
    }

    /// <summary>
    /// A translation supplying a body but no title falls back to the original title only — the
    /// COALESCE is per field, not per row, so the translated body must survive.
    /// </summary>
    [TestMethod]
    public async Task Read_TranslationHasBodyButNoTitle_FallsBackToOriginalTitleOnly()
    {
        using TempDatabase temp = new(SchemaThroughMigration11);
        using SqliteConnection connection = await OpenAsync(temp);
        await ApplyTranslationSchemaAsync(connection);
        Guid id = await InsertNotificationAsync(connection, "English headline", "English body.");
        await InsertTranslationAsync(connection, id, "nl", title: null, body: "Nederlandse tekst.");

        NotificationReader reader = CreateReader(temp);
        NotificationEntity row = (await reader.GetPagedAsync(1, 10, "nl")).Items.Single();

        Assert.AreEqual("English headline", row.Title, "A missing translated title falls back on its own.");
        Assert.AreEqual("Nederlandse tekst.", row.Body, "The translated body must not be dropped with it.");
    }

    /// <summary>`NL` resolves the `nl` row — case-insensitive by default, per the project-wide rule.</summary>
    [TestMethod]
    public async Task Read_RequestedLanguageDiffersInCase_StillResolvesTheTranslation()
    {
        using TempDatabase temp = new(SchemaThroughMigration11);
        NotificationReader reader = await SeedTranslatedNotificationAsync(temp);

        NotificationEntity row = (await reader.GetPagedAsync(1, 10, "NL")).Items.Single();

        Assert.AreEqual("Nederlandse tekst.", row.Body);
        Assert.AreEqual("nl", row.EffectiveLanguage, "EffectiveLanguage is reported lowercase.");
    }

    /// <summary>
    /// The active-notification query resolves translations too, not only the paged one — the three
    /// queries share a projection, and a missed `@lang` binding on one is the likely defect.
    /// </summary>
    [TestMethod]
    public async Task Read_ActiveQuery_ResolvesTranslationsToo()
    {
        using TempDatabase temp = new(SchemaThroughMigration11);
        NotificationReader reader = await SeedTranslatedNotificationAsync(temp);

        IReadOnlyList<NotificationEntity> active = await reader.GetActiveNotificationsAsync("nl");

        Assert.AreEqual("Nederlandse tekst.", active.Single().Body);
    }

    /// <summary>
    /// The by-id query resolves translations too. It is the third query sharing the projection and the
    /// only one reached through the writer, so it is the one a missed `@lang` binding would hide in.
    /// </summary>
    [TestMethod]
    public async Task Read_ByIdQueryViaDismiss_ResolvesTranslationsToo()
    {
        using TempDatabase temp = new(SchemaThroughMigration11);
        using SqliteConnection connection = await OpenAsync(temp);
        await ApplyTranslationSchemaAsync(connection);
        Guid id = await InsertNotificationAsync(connection, "English headline", "English body.");
        await InsertTranslationAsync(connection, id, "nl", "Nederlandse kop", "Nederlandse tekst.");

        SqliteConnectionFactory factory = new(temp.DbPath);
        NotificationWriter writer = new(factory);

        NotificationEntity? dismissed = await writer.DismissAsync(id, "nl");

        Assert.IsNotNull(dismissed);
        Assert.AreEqual("Nederlandse tekst.", dismissed.Body,
            "The dismiss path echoes the notification back and must resolve it the same way a read does.");
        Assert.IsTrue(dismissed.IsDismissed);
    }

    // ── Write side (step 6) ──────────────────────────────────────────────────

    /// <summary>A producer supplies every language in one call, and each becomes its own row.</summary>
    [TestMethod]
    public async Task Write_WithTranslations_PersistsOneRowPerLanguage()
    {
        using TempDatabase temp = new(SchemaThroughMigration11);
        using SqliteConnection connection = await OpenAsync(temp);
        await ApplyTranslationSchemaAsync(connection);

        SqliteConnectionFactory factory = new(temp.DbPath);
        NotificationWriter writer = new(factory);

        await writer.WriteAsync(
            NotificationType.Information, "English body.", appVersionId: null, title: "English headline",
            translations:
            [
                new NotificationTranslation("nl", "Nederlandse kop", "Nederlandse tekst."),
                new NotificationTranslation("de", "Deutsche Überschrift", "Deutscher Text."),
            ]);

        List<string> languages = [.. await connection.QueryAsync<string>(
            "SELECT Language FROM System_NotificationTranslation ORDER BY Language;")];

        Assert.AreSequenceEqual(ExpectedBackfilledLanguages, languages);

        NotificationReader reader = TestNotificationReader.Create(temp.DbPath);
        Assert.AreEqual("Deutscher Text.", (await reader.GetPagedAsync(1, 10, "de")).Items.Single().Body);
    }

    /// <summary>A producer supplying no translations writes none — the original text stands alone.</summary>
    [TestMethod]
    public async Task Write_WithoutTranslations_PersistsNoTranslationRows()
    {
        using TempDatabase temp = new(SchemaThroughMigration11);
        using SqliteConnection connection = await OpenAsync(temp);
        await ApplyTranslationSchemaAsync(connection);

        NotificationWriter writer = new(new SqliteConnectionFactory(temp.DbPath));
        await writer.WriteAsync(NotificationType.Warning, "English body.", appVersionId: null);

        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM System_NotificationTranslation;"));
    }

    /// <summary>
    /// Identity is structural — the metadata payload — so a second seed carrying different
    /// translations is still the same notification and is suppressed. Guards the invariant the read
    /// path depends on: the original text never moves, so nothing a producer supplies as a translation
    /// can change what a payload's content hash was taken over.
    /// </summary>
    [TestMethod]
    public async Task Seed_SameNotificationWithDifferentTranslations_IsStillSuppressed()
    {
        using TempDatabase temp = new(SchemaThroughMigration11);
        using SqliteConnection connection = await OpenAsync(temp);
        await ApplyTranslationSchemaAsync(connection);

        SqliteConnectionFactory factory = new(temp.DbPath);
        NotificationReader reader = TestNotificationReader.Create(factory);
        NotificationWriter writer = new(factory);

        AnnouncementMetadataDto metadata = new()
        {
            Announcement = "SomethingRenamed",
            ReleaseState = NotificationReleaseState.Released,
            Version      = "1.9.0",
            ContentHash  = NotificationContentHash.Of("English body."),
        };

        NotificationEntity? first = await NotificationSeeding.SeedOnceAsync(
            reader, writer, NotificationType.Warning, metadata, "English body.", appVersionId: null,
            translations: [new NotificationTranslation("nl", "Kop", "Eerste tekst.")]);

        NotificationEntity? second = await NotificationSeeding.SeedOnceAsync(
            reader, writer, NotificationType.Warning, metadata, "English body.", appVersionId: null,
            translations: [new NotificationTranslation("nl", "Andere kop", "Andere tekst.")]);

        Assert.IsNotNull(first);
        Assert.IsNull(second, "A differing translation set does not make it a different notification.");
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM System_Notification;"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM System_NotificationTranslation;"),
            "The suppressed seed must not append a second translation row either.");
    }

    private static async Task<NotificationReader> SeedTranslatedNotificationAsync(TempDatabase temp)
    {
        using SqliteConnection connection = await OpenAsync(temp);
        await ApplyTranslationSchemaAsync(connection);
        Guid id = await InsertNotificationAsync(connection, "English headline", "English body.");
        await InsertTranslationAsync(connection, id, "nl", "Nederlandse kop", "Nederlandse tekst.");
        return CreateReader(temp);
    }

    private static NotificationReader CreateReader(TempDatabase temp)
        => TestNotificationReader.Create(temp.DbPath);

    private static async Task<Guid> InsertNotificationAsync(SqliteConnection connection, string title, string body)
    {
        Guid id = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO System_Notification (Id, Type, Title, Body, DateCreated, IsDeleted, IsDismissed, OriginalLanguage) " +
            "VALUES (@id, 'Information', @title, @body, @now, 0, 0, 'en');",
            new { id = id.ToString(), title, body, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });
        return id;
    }

    private static async Task InsertTranslationAsync(
        SqliteConnection connection, Guid notificationId, string language, string? title, string body)
        => await connection.ExecuteAsync(
            "INSERT INTO System_NotificationTranslation (Id, NotificationId, Language, Title, Body, DateCreated, IsDeleted) " +
            "VALUES (@id, @notificationId, @language, @title, @body, @now, 0);",
            new
            {
                id = Guid.NewGuid().ToString(),
                notificationId = notificationId.ToString(),
                language,
                title,
                body,
                now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            });

    private static async Task ApplyTranslationSchemaAsync(SqliteConnection connection)
    {
        await connection.ExecuteAsync(NotificationTranslationMigrations.AddOriginalLanguageColumn);
        await connection.ExecuteAsync(NotificationTranslationMigrations.CreateNotificationTranslationTable);
    }

    // v1.8.3's announcement exactly as that release wrote it, with #312's migration 8 metadata already
    // applied — the state this backfill actually meets on an upgrading database.
    private static async Task SeedLegacyAnnouncementAsync(SqliteConnection connection)
    {
        await connection.ExecuteAsync(
            "INSERT INTO System_Notification (Id, Type, Title, Body, Metadata, MetadataKind, DateCreated, IsDeleted, IsDismissed) " +
            "VALUES (@id, 'Warning', 'Two API operation IDs were renamed', @body, " +
            """'{"announcement":"GetAllImportBatches"}', 'Announcement', @now, 0, 0);""",
            new
            {
                id   = Guid.NewGuid().ToString(),
                body = "Two REST API operation IDs were renamed for naming consistency (issue #279): " +
                       "GetImportBatches → GetAllImportBatches, and GetFileResources → GetAllFileResources. " +
                       "This only affects a generated API client keyed by operation ID — routes and behaviour are unchanged.",
                now  = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            });
    }

    private static async Task<SqliteConnection> OpenAsync(TempDatabase temp)
    {
        SqliteConnection connection = new($"Data Source={temp.DbPath}");
        await connection.OpenAsync();
        return connection;
    }
}
