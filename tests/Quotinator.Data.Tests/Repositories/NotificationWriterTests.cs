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
    private NotificationReader _reader = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_notification_writer_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        // Replays this table's real migration sequence rather than hand-writing its current shape:
        // v1.8.0's CREATE, then #81's System_AppVersion (which #312's AppVersionId FK targets),
        // then #312's own reshape. Keeps the fixture honest against what a real database has.
        conn.Execute(NotificationMigrations.CreateNotificationTable);
        conn.Execute(AppVersionMigrations.CreateAppVersionTable);
        conn.Execute(NotificationSchemaMigrations.SplitMessageAndAddMetadata);

        var factory = new SqliteConnectionFactory(_dbPath);
        _writer = new NotificationWriter(factory, defaultExpiryHours: 720);
        _reader = new NotificationReader(factory);
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

    /// <summary>
    /// #312 reversed this deliberately. It previously asserted the opposite — that omitting
    /// <c>expiresAt</c> applied the configured 720-hour default — which meant every notification aged
    /// out on a timer, including ones describing conditions that were still unresolved. Expiry is now
    /// opt-in: a producer that wants time-limited behaviour asks for it (see the test below).
    /// </summary>
    [TestMethod]
    public async Task WriteAsync_NoExpirySpecified_DoesNotExpire()
    {
        var entity = await _writer.WriteAsync(NotificationType.Information, "no explicit expiry");

        Assert.IsNull(entity.ExpiresAt.Parsed, "Omitting expiresAt must mean 'never expires', not 'apply the configured default'.");

        // Round-trip through the database, not just the returned entity — a value defaulted on write
        // and a value defaulted on read are different bugs, and only the stored row proves which.
        var stored = (await _reader.GetPagedAsync(1, 0)).Items.Single(n => n.Id == entity.Id);
        Assert.IsNull(stored.ExpiresAt.Parsed);
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
