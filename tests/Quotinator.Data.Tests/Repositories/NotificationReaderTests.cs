using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;
using Quotinator.Data.Repositories;
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

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute(NotificationMigrations.CreateNotificationTable);

        var factory = new SqliteConnectionFactory(_dbPath);
        _reader = new NotificationReader(factory);
        _writer = new NotificationWriter(factory, defaultExpiryHours: 720);
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
        var active    = await _writer.WriteAsync(NotificationType.Information, "still active");
        var dismissed = await _writer.WriteAsync(NotificationType.Information, "already dismissed");
        await _writer.DismissAsync(dismissed.Id);

        var result = await _reader.GetActiveNotificationsAsync();

        Assert.HasCount(1, result);
        Assert.AreEqual(active.Id.ToString("D"), result[0].Id.ToString("D"));
    }

    [TestMethod]
    public async Task GetActiveNotifications_ExcludesExpiredNotifications()
    {
        await _writer.WriteAsync(NotificationType.Warning, "already expired", expiresAt: DateTime.UtcNow.AddHours(-1));
        var stillGood = await _writer.WriteAsync(NotificationType.Warning, "not expired yet", expiresAt: DateTime.UtcNow.AddHours(1));

        var result = await _reader.GetActiveNotificationsAsync();

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
        for (var i = 0; i < 3; i++)
            await _writer.WriteAsync(NotificationType.Information, $"notification {i}");

        var result = await _reader.GetPagedAsync(1, 0);

        Assert.HasCount(3, result.Items, "pageSize = 0 must reach SQLite as LIMIT -1, not a literal LIMIT 0");
        Assert.AreEqual(3, result.TotalCount);
        Assert.AreEqual(3, result.PageSize, "PageSize must report the effective count actually returned, not the literal 0 requested");
    }

    [TestMethod]
    public async Task GetPagedAsync_IncludesDismissedAndExpiredNotifications()
    {
        var dismissed = await _writer.WriteAsync(NotificationType.Success, "dismissed");
        await _writer.DismissAsync(dismissed.Id);
        await _writer.WriteAsync(NotificationType.Error, "expired", expiresAt: DateTime.UtcNow.AddHours(-1));

        var result = await _reader.GetPagedAsync(1, 0);

        Assert.HasCount(2, result.Items, "the full history endpoint must show dismissed/expired notifications too, unlike GetActiveNotificationsAsync");
    }

    public TestContext TestContext { get; set; }
}
