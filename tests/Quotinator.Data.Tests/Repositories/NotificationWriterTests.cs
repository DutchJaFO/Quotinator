using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Tests.Repositories;

/// <summary>Exercises <see cref="NotificationWriter"/> against a real SQLite schema (#278).</summary>
[TestClass]
public class NotificationWriterTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private NotificationWriter _writer = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_notification_writer_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute(NotificationMigrations.CreateNotificationTable);

        var factory = new SqliteConnectionFactory(_dbPath);
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
    public async Task WriteAsync_PersistsAllFiveTypes()
    {
        NotificationType[] types = [NotificationType.Information, NotificationType.Warning, NotificationType.Error, NotificationType.Success, NotificationType.ActionRequired];

        foreach (var type in types)
            await _writer.WriteAsync(type, $"{type} message");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var rows = conn.Query<NotificationEntity>("SELECT * FROM System_Notification ORDER BY DateCreated;").ToList();

        Assert.HasCount(5, rows);
        Assert.AreSequenceEqual(types, [.. rows.Select(r => r.Type.Parsed!.Value)], Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task WriteAsync_NoExpirySpecified_AppliesConfiguredDefault()
    {
        var before = DateTime.UtcNow;
        var entity = await _writer.WriteAsync(NotificationType.Information, "no explicit expiry");
        var after  = DateTime.UtcNow;

        Assert.IsNotNull(entity.ExpiresAt.Parsed);
        Assert.IsTrue(entity.ExpiresAt.Parsed >= before.AddHours(720).AddMinutes(-1), "Expiry must be roughly 720 hours (the configured default) from creation");
        Assert.IsTrue(entity.ExpiresAt.Parsed <= after.AddHours(720).AddMinutes(1), "Expiry must be roughly 720 hours (the configured default) from creation");
    }

    [TestMethod]
    public async Task WriteAsync_ExplicitExpirySpecified_UsesExplicitValueNotDefault()
    {
        var explicitExpiry = DateTime.UtcNow.AddHours(2);

        var entity = await _writer.WriteAsync(NotificationType.Warning, "custom expiry", expiresAt: explicitExpiry);

        Assert.AreEqual(explicitExpiry.ToString("yyyy-MM-dd HH:mm:ss"), entity.ExpiresAt.Parsed!.Value.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    [TestMethod]
    public async Task DismissAsync_ExistingId_MarksDismissedAndReturnsUpdatedEntity()
    {
        var entity = await _writer.WriteAsync(NotificationType.Information, "dismiss me");

        var dismissed = await _writer.DismissAsync(entity.Id);

        Assert.IsNotNull(dismissed);
        Assert.IsTrue(dismissed.IsDismissed);
        Assert.IsNotNull(dismissed.DismissedAt.Parsed);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var persisted = conn.QuerySingle<NotificationEntity>("SELECT * FROM System_Notification WHERE Id = @id;", new { id = entity.Id.ToString("D") });
        Assert.IsTrue(persisted.IsDismissed, "dismissal must be persisted, not just reflected on the in-memory return value");
    }

    [TestMethod]
    public async Task DismissAsync_UnknownId_ReturnsNull()
    {
        var result = await _writer.DismissAsync(Guid.NewGuid());

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task DismissByTrigger_MarksMatchingActiveNotificationsAsDismissed()
    {
        var matching1 = await _writer.WriteAsync(NotificationType.ActionRequired, "consider a reset", dismissTrigger: NotificationDismissTrigger.DatabaseReset);
        var matching2 = await _writer.WriteAsync(NotificationType.ActionRequired, "another reset reminder", dismissTrigger: NotificationDismissTrigger.DatabaseReset);
        var unrelated = await _writer.WriteAsync(NotificationType.Information, "no trigger at all");

        var dismissedCount = await _writer.DismissByTriggerAsync(NotificationDismissTrigger.DatabaseReset);

        Assert.AreEqual(2, dismissedCount);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        Assert.IsTrue(conn.QuerySingle<bool>("SELECT IsDismissed FROM System_Notification WHERE Id = @id;", new { id = matching1.Id.ToString("D") }));
        Assert.IsTrue(conn.QuerySingle<bool>("SELECT IsDismissed FROM System_Notification WHERE Id = @id;", new { id = matching2.Id.ToString("D") }));
        Assert.IsFalse(conn.QuerySingle<bool>("SELECT IsDismissed FROM System_Notification WHERE Id = @id;", new { id = unrelated.Id.ToString("D") }));
    }

    [TestMethod]
    public async Task DismissByTrigger_NoMatchingTrigger_IsNoOp()
    {
        await _writer.WriteAsync(NotificationType.Information, "no trigger at all");

        var dismissedCount = await _writer.DismissByTriggerAsync(NotificationDismissTrigger.DatabaseReset);

        Assert.AreEqual(0, dismissedCount);
    }

    [TestMethod]
    public async Task DismissByTrigger_AlreadyDismissedMatchingRow_IsNotDoubleCountedOrReDismissed()
    {
        var entity = await _writer.WriteAsync(NotificationType.ActionRequired, "already handled", dismissTrigger: NotificationDismissTrigger.DatabaseReset);
        await _writer.DismissAsync(entity.Id);

        var dismissedCount = await _writer.DismissByTriggerAsync(NotificationDismissTrigger.DatabaseReset);

        Assert.AreEqual(0, dismissedCount, "an already-dismissed row is not active, so a trigger sweep must not re-count it");
    }

    public TestContext TestContext { get; set; }
}
