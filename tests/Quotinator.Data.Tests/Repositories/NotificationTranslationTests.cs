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

    private static async Task<SqliteConnection> OpenAsync(TempDatabase temp)
    {
        SqliteConnection connection = new($"Data Source={temp.DbPath}");
        await connection.OpenAsync();
        return connection;
    }
}
