using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.Database;
using Quotinator.Data.Tests.Helpers;

namespace Quotinator.Data.Tests.Repositories;

/// <summary>Exercises <see cref="NotificationReader"/> against a real SQLite schema (#278).</summary>
[TestClass]
public class NotificationReaderTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private NotificationReader _reader = null!;
    private NotificationWriter _writer = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_notification_reader_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        // Replays this table's real migration sequence rather than hand-writing its current shape:
        // v1.8.0's CREATE, then #81's System_AppVersion (which #312's AppVersionId FK targets),
        // then #312's own reshape. Keeps the fixture honest against what a real database has.
        conn.Execute(NotificationMigrations.CreateNotificationTable);
        conn.Execute(AppVersionMigrations.CreateAppVersionTable);
        conn.Execute(NotificationSchemaMigrations.SplitMessageAndAddMetadata);
        // #319: the language column and the translation table the read projection joins against.
        conn.Execute(NotificationTranslationMigrations.AddOriginalLanguageColumn);
        conn.Execute(NotificationTranslationMigrations.CreateNotificationTranslationTable);

        SqliteConnectionFactory factory = new SqliteConnectionFactory(_dbPath);
        _reader = TestNotificationReader.Create(factory);
        _writer = new NotificationWriter(factory);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public async Task GetActiveNotifications_ReturnsUndismissedOnly()
    {
        NotificationEntity active    = await _writer.WriteAsync(NotificationType.Information, "still active", appVersionId: null);
        NotificationEntity dismissed = await _writer.WriteAsync(NotificationType.Information, "already dismissed", appVersionId: null);
        await _writer.DismissAsync(dismissed.Id);

        IReadOnlyList<NotificationEntity> result = await _reader.GetActiveNotificationsAsync();

        Assert.HasCount(1, result);
        Assert.AreEqual(active.Id.ToString("D"), result[0].Id.ToString("D"));
    }

    [TestMethod]
    public async Task GetActiveNotifications_ExcludesExpiredNotifications()
    {
        await _writer.WriteAsync(NotificationType.Warning, "already expired", appVersionId: null, expiresAt: DateTime.UtcNow.AddHours(-1));
        NotificationEntity stillGood = await _writer.WriteAsync(NotificationType.Warning, "not expired yet", appVersionId: null, expiresAt: DateTime.UtcNow.AddHours(1));

        IReadOnlyList<NotificationEntity> result = await _reader.GetActiveNotificationsAsync();

        Assert.HasCount(1, result);
        Assert.AreEqual(stillGood.Id.ToString("D"), result[0].Id.ToString("D"));
    }

    /// <summary>
    /// #195's own recurring finding: a type-only retrofit onto <c>PagedItems&lt;T&gt;</c> can leave the
    /// underlying <c>LIMIT @pageSize</c> query translating <c>pageSize = 0</c> into a literal
    /// <c>LIMIT 0</c> instead of <c>LIMIT -1</c> — this must be proven against real SQLite, not a fake.
    /// </summary>
    [TestMethod]
    public async Task GetPagedAsync_PageSizeZero_ReturnsAllRows()
    {
        for (int i = 0; i < 3; i++)
            await _writer.WriteAsync(NotificationType.Information, $"notification {i}", appVersionId: null);

        PagedItems<NotificationEntity> result = await _reader.GetPagedAsync(1, 0);

        Assert.HasCount(3, result.Items, "pageSize = 0 must reach SQLite as LIMIT -1, not a literal LIMIT 0");
        Assert.AreEqual(3, result.TotalCount);
        Assert.AreEqual(3, result.PageSize, "PageSize must report the effective count actually returned, not the literal 0 requested");
    }

    [TestMethod]
    public async Task GetPagedAsync_IncludesDismissedAndExpiredNotifications()
    {
        NotificationEntity dismissed = await _writer.WriteAsync(NotificationType.Success, "dismissed", appVersionId: null);
        await _writer.DismissAsync(dismissed.Id);
        await _writer.WriteAsync(NotificationType.Error, "expired", appVersionId: null, expiresAt: DateTime.UtcNow.AddHours(-1));

        PagedItems<NotificationEntity> result = await _reader.GetPagedAsync(1, 0);

        Assert.HasCount(2, result.Items, "the full history endpoint must show dismissed/expired notifications too, unlike GetActiveNotificationsAsync");
    }

    /// <summary>
    /// Found live during a real HA v1.8.2 → v1.8.3-beta upgrade attempt that failed partway through
    /// the migration (unrelated cause): the restored pre-migration database genuinely has no
    /// System_Notification table yet, and both callers reachable during that degraded state (Home's
    /// modal, the /notifications page) previously crashed with an unhandled SqliteException instead of
    /// showing "no notifications" — defeating the whole point of the degraded-state UI.
    /// </summary>
    [TestMethod]
    public async Task GetActiveNotificationsAsync_TableDoesNotExist_ReturnsEmptyInsteadOfThrowing()
    {
        string tempDir = Directory.CreateTempSubdirectory("quotinator_notification_reader_missing_table_test_").FullName;
        try
        {
            string dbPath = Path.Combine(tempDir, "no-notification-table.db");
            using (SqliteConnection conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                conn.Execute("CREATE TABLE Placeholder (Id TEXT PRIMARY KEY);");
            }

            NotificationReader reader = TestNotificationReader.Create(dbPath);

            IReadOnlyList<NotificationEntity> result = await reader.GetActiveNotificationsAsync();

            Assert.IsEmpty(result);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>See <see cref="GetActiveNotificationsAsync_TableDoesNotExist_ReturnsEmptyInsteadOfThrowing"/> — same gap, the paged endpoint.</summary>
    [TestMethod]
    public async Task GetPagedAsync_TableDoesNotExist_ReturnsEmptyInsteadOfThrowing()
    {
        string tempDir = Directory.CreateTempSubdirectory("quotinator_notification_reader_missing_table_test_").FullName;
        try
        {
            string dbPath = Path.Combine(tempDir, "no-notification-table.db");
            using (SqliteConnection conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                conn.Execute("CREATE TABLE Placeholder (Id TEXT PRIMARY KEY);");
            }

            NotificationReader reader = TestNotificationReader.Create(dbPath);

            PagedItems<NotificationEntity> result = await reader.GetPagedAsync(1, 20);

            Assert.IsEmpty(result.Items);
            Assert.AreEqual(0, result.TotalCount);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(tempDir, recursive: true);
        }
    }

    public TestContext TestContext { get; set; }
}
