using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Database;
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

        List<string> languages = (await connection.QueryAsync<string>(
            "SELECT Language FROM System_NotificationTranslation ORDER BY Language;")).ToList();

        Assert.AreSequenceEqual(new[] { "de", "nl" }, languages,
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
