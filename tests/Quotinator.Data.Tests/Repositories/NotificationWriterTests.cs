using System.Reflection;
using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Notifications;
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

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        // Replays this table's real migration sequence rather than hand-writing its current shape:
        // v1.8.0's CREATE, then #81's System_AppVersion (which #312's AppVersionId FK targets),
        // then #312's own reshape. Keeps the fixture honest against what a real database has.
        conn.Execute(NotificationMigrations.CreateNotificationTable);
        conn.Execute(AppVersionMigrations.CreateAppVersionTable);
        // #312's own reshape of that table, so AppVersionTracker's append-only writes work here too.
        conn.Execute(AppVersionHistoryMigrations.AddApplicationColumn);
        conn.Execute(AppVersionHistoryMigrations.AddSequenceNumberColumn);
        conn.Execute(NotificationSchemaMigrations.SplitMessageAndAddMetadata);

        SqliteConnectionFactory factory = new SqliteConnectionFactory(_dbPath);
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

    [TestMethod]
    public async Task WriteAsync_PersistsAllFiveTypes()
    {
        NotificationType[] types = [NotificationType.Information, NotificationType.Warning, NotificationType.Error, NotificationType.Success, NotificationType.ActionRequired];

        foreach (NotificationType type in types)
            await _writer.WriteAsync(type, $"{type} message", appVersionId: null);

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        List<NotificationEntity> rows = [.. conn.Query<NotificationEntity>("SELECT * FROM System_Notification ORDER BY DateCreated;")];

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
        NotificationEntity entity = await _writer.WriteAsync(NotificationType.Information, "no explicit expiry", appVersionId: null);

        Assert.IsNull(entity.ExpiresAt.Parsed, "Omitting expiresAt must mean 'never expires', not 'apply the configured default'.");

        // Round-trip through the database, not just the returned entity — a value defaulted on write
        // and a value defaulted on read are different bugs, and only the stored row proves which.
        NotificationEntity stored = (await _reader.GetPagedAsync(1, 0)).Items.Single(n => n.Id == entity.Id);
        Assert.IsNull(stored.ExpiresAt.Parsed);
    }

    [TestMethod]
    public async Task WriteAsync_ExplicitExpirySpecified_UsesExplicitValueNotDefault()
    {
        DateTime explicitExpiry = DateTime.UtcNow.AddHours(2);

        NotificationEntity entity = await _writer.WriteAsync(
            NotificationType.Warning, "custom expiry", appVersionId: null, expiresAt: explicitExpiry);

        Assert.AreEqual(explicitExpiry.ToString("yyyy-MM-dd HH:mm:ss"), entity.ExpiresAt.Parsed!.Value.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    [TestMethod]
    public async Task DismissAsync_ExistingId_MarksDismissedAndReturnsUpdatedEntity()
    {
        NotificationEntity entity = await _writer.WriteAsync(NotificationType.Information, "dismiss me", appVersionId: null);

        NotificationEntity? dismissed = await _writer.DismissAsync(entity.Id);

        Assert.IsNotNull(dismissed);
        Assert.IsTrue(dismissed.IsDismissed);
        Assert.IsNotNull(dismissed.DismissedAt.Parsed);

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        NotificationEntity persisted = conn.QuerySingle<NotificationEntity>("SELECT * FROM System_Notification WHERE Id = @id;", new { id = entity.Id.ToString("D") });
        Assert.IsTrue(persisted.IsDismissed, "dismissal must be persisted, not just reflected on the in-memory return value");
    }

    [TestMethod]
    public async Task DismissAsync_UnknownId_ReturnsNull()
    {
        NotificationEntity? result = await _writer.DismissAsync(Guid.NewGuid());

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task DismissByTrigger_MarksMatchingActiveNotificationsAsDismissed()
    {
        NotificationEntity matching1 = await _writer.WriteAsync(NotificationType.ActionRequired, "consider a reset", appVersionId: null, dismissTrigger: NotificationDismissTrigger.DatabaseReset);
        NotificationEntity matching2 = await _writer.WriteAsync(NotificationType.ActionRequired, "another reset reminder", appVersionId: null, dismissTrigger: NotificationDismissTrigger.DatabaseReset);
        NotificationEntity unrelated = await _writer.WriteAsync(NotificationType.Information, "no trigger at all", appVersionId: null);

        int dismissedCount = await _writer.DismissByTriggerAsync(NotificationDismissTrigger.DatabaseReset);

        Assert.AreEqual(2, dismissedCount);

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        Assert.IsTrue(conn.QuerySingle<bool>("SELECT IsDismissed FROM System_Notification WHERE Id = @id;", new { id = matching1.Id.ToString("D") }));
        Assert.IsTrue(conn.QuerySingle<bool>("SELECT IsDismissed FROM System_Notification WHERE Id = @id;", new { id = matching2.Id.ToString("D") }));
        Assert.IsFalse(conn.QuerySingle<bool>("SELECT IsDismissed FROM System_Notification WHERE Id = @id;", new { id = unrelated.Id.ToString("D") }));
    }

    [TestMethod]
    public async Task DismissByTrigger_NoMatchingTrigger_IsNoOp()
    {
        await _writer.WriteAsync(NotificationType.Information, "no trigger at all", appVersionId: null);

        int dismissedCount = await _writer.DismissByTriggerAsync(NotificationDismissTrigger.DatabaseReset);

        Assert.AreEqual(0, dismissedCount);
    }

    [TestMethod]
    public async Task DismissByTrigger_AlreadyDismissedMatchingRow_IsNotDoubleCountedOrReDismissed()
    {
        NotificationEntity entity = await _writer.WriteAsync(NotificationType.ActionRequired, "already handled", appVersionId: null, dismissTrigger: NotificationDismissTrigger.DatabaseReset);
        await _writer.DismissAsync(entity.Id);

        int dismissedCount = await _writer.DismissByTriggerAsync(NotificationDismissTrigger.DatabaseReset);

        Assert.AreEqual(0, dismissedCount, "an already-dismissed row is not active, so a trigger sweep must not re-count it");
    }

    /// <summary>
    /// A notification's <c>AppVersionId</c> keeps pointing at the version that wrote it, even after a
    /// newer version is recorded. This is the guarantee the whole append-only conversion of
    /// <c>System_AppVersion</c> exists to provide (#312): against the single upserted row #81 shipped,
    /// the referenced row would be overwritten on upgrade and every historical notification would start
    /// claiming it came from the new version.
    /// </summary>
    [TestMethod]
    public async Task WriteAsync_AppVersionId_StillPointsAtTheWritingVersionAfterAnUpgrade()
    {
        SqliteConnectionFactory factory = new(_dbPath);
        AppVersionTracker tracker = new(factory);

        AppVersionRecord writingVersion = await tracker.RecordCurrentAsync("Quotinator.Api", "1.8.3");
        NotificationEntity written = await _writer.WriteAsync(
            NotificationType.Information, body: "written under 1.8.3", appVersionId: writingVersion.Id);

        // The upgrade: a newer version is recorded, which under the old single-row design would have
        // overwritten the very row this notification references.
        await tracker.RecordCurrentAsync("Quotinator.Api", "1.9.0");

        using SqliteConnection connection = (SqliteConnection)factory.CreateConnection();
        await connection.OpenAsync(TestContext.CancellationToken);
        string? referencedVersion = await connection.ExecuteScalarAsync<string>(
            "SELECT v.Version FROM System_Notification n JOIN System_AppVersion v ON LOWER(v.Id) = LOWER(n.AppVersionId) WHERE LOWER(n.Id) = LOWER(@id);",
            new { id = written.Id.ToString() });

        Assert.AreEqual("1.8.3", referencedVersion,
            "The notification's provenance must stay frozen at the version that wrote it, not follow the latest recorded version.");
    }

    /// <summary>
    /// Provenance is as hard to forget as identity. <c>IdentityComponents</c> is abstract, so no payload
    /// can exist without an identity — the compiler enforces it. An optional <c>appVersionId</c>
    /// defaulting to null gave provenance no such guarantee, and it was duly forgotten (migration 8 left
    /// the legacy announcement unattributed).
    /// <para>
    /// The parameter stays nullable — null is a legitimate answer when recording the current version
    /// failed — but has no default, so a caller must state it. Asserted by reflection because the
    /// guarantee is a compile-time one, and a later edit could quietly restore the default without any
    /// test noticing.
    /// </para>
    /// </summary>
    [TestMethod]
    public void WriteAsync_AppVersionIdParameter_HasNoDefault()
    {
        AssertParameterHasNoDefault(typeof(INotificationWriter), nameof(INotificationWriter.WriteAsync), "appVersionId");
        AssertParameterHasNoDefault(typeof(NotificationSeeding), nameof(NotificationSeeding.SeedOnceAsync), "appVersionId");
    }

    private static void AssertParameterHasNoDefault(Type declaringType, string methodName, string parameterName)
    {
        ParameterInfo parameter = declaringType.GetMethod(methodName)!.GetParameters().Single(p => p.Name == parameterName);

        Assert.IsFalse(parameter.HasDefaultValue,
            $"{declaringType.Name}.{methodName}'s {parameterName} has a default again — provenance can be omitted by " +
            "saying nothing, which is exactly how the legacy notification ended up unattributed.");
    }

    public TestContext TestContext { get; set; }
}
