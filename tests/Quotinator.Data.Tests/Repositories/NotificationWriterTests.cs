using System.Reflection;
using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Testing.Database;
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
    public async Task TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_notification_writer_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");

        // The schema the application actually creates. This used to replay a hand-listed sequence,
        // which reads as honest but is a maintained copy that drifts — see CurrentSchema.
        await CurrentSchema.ApplyDataSchemaAsync(_dbPath);

        SqliteConnectionFactory factory = new SqliteConnectionFactory(_dbPath);
        _writer = new NotificationWriter(factory);
        _reader = TestNotificationReader.Create(factory);
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
        Assert.AreEqual(NotificationDismissReason.Dismissed, persisted.DismissReason.Parsed,
            "#304: dismissing by id is the user setting it aside, which must be recorded as such rather than as resolved.");
    }

    /// <summary>
    /// #304: every caller of the trigger-based dismiss is an action that carried out the work, so the
    /// row records that it was resolved. Without this the UI can only report it as dismissed, telling a
    /// user who ran the action that they declined it — which is what T1 found.
    /// </summary>
    [TestMethod]
    public async Task DismissByTriggerAsync_RecordsResolvedRatherThanDismissed()
    {
        NotificationEntity entity = await _writer.WriteAsync(
            NotificationType.ActionRequired, "reseed me", appVersionId: null,
            dismissTrigger: NotificationDismissTrigger.Reseed);

        int dismissed = await _writer.DismissByTriggerAsync(NotificationDismissTrigger.Reseed);

        Assert.AreEqual(1, dismissed);

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        NotificationEntity persisted = conn.QuerySingle<NotificationEntity>(
            "SELECT * FROM System_Notification WHERE Id = @id;", new { id = entity.Id.ToString("D") });

        Assert.IsTrue(persisted.IsDismissed);
        Assert.AreEqual(NotificationDismissReason.Resolved, persisted.DismissReason.Parsed);
    }

    /// <summary>
    /// #303: a batch-scoped dismissal reaches only the notification naming that batch, and records the
    /// caller's own reason. Both halves matter — a trigger-wide dismissal would clear every file's alert,
    /// and a hardcoded `Resolved` would claim a removed batch had been reviewed.
    /// </summary>
    [TestMethod]
    public async Task DismissedAsObsolete_ReadsBackAsObsolete()
    {
        string targetBatch = Guid.NewGuid().ToString("D");
        string otherBatch  = Guid.NewGuid().ToString("D");

        NotificationEntity target = await WriteReviewAlertAsync(targetBatch);
        NotificationEntity other  = await WriteReviewAlertAsync(otherBatch);

        int dismissed = await _writer.DismissByTriggerAndBatchAsync(
            NotificationDismissTrigger.ImportReviewResolved, targetBatch, NotificationDismissReason.Obsolete);

        Assert.AreEqual(1, dismissed, "Exactly the one alert naming that batch — not both, and not none.");

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();

        NotificationEntity persistedTarget = conn.QuerySingle<NotificationEntity>(
            "SELECT * FROM System_Notification WHERE Id = @id;", new { id = target.Id.ToString("D") });
        NotificationEntity persistedOther = conn.QuerySingle<NotificationEntity>(
            "SELECT * FROM System_Notification WHERE Id = @id;", new { id = other.Id.ToString("D") });

        Assert.IsTrue(persistedTarget.IsDismissed);
        Assert.AreEqual(NotificationDismissReason.Obsolete, persistedTarget.DismissReason.Parsed,
            "An inactive notification has to say what happened to it without anyone reading the audit trail.");

        Assert.IsFalse(persistedOther.IsDismissed,
            "The other batch is still genuinely awaiting review — scoping is the point of this method.");
    }

    /// <summary>
    /// #308: a notification carried to completion by its own action records <em>which</em> resolution
    /// settled it, not only that it settled. Found in T1 — a resolved review alert read `Done` while its
    /// body still asked for a decision, because the body is frozen at write time and the choice was
    /// discarded.
    /// </summary>
    [TestMethod]
    public async Task DismissedAsResolved_RecordsTheResolution()
    {
        string batchId = Guid.NewGuid().ToString("D");
        NotificationEntity alert = await WriteReviewAlertAsync(batchId);

        await _writer.DismissByTriggerAndBatchAsync(
            NotificationDismissTrigger.ImportReviewResolved, batchId, NotificationDismissReason.Resolved,
            NotificationResolution.TookIncoming);

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        NotificationEntity persisted = conn.QuerySingle<NotificationEntity>(
            "SELECT * FROM System_Notification WHERE Id = @id;", new { id = alert.Id.ToString("D") });

        Assert.AreEqual(NotificationDismissReason.Resolved, persisted.DismissReason.Parsed);
        Assert.AreEqual(NotificationResolution.TookIncoming, persisted.Resolution.Parsed,
            "Which side won is what the row has to be able to say afterwards.");

        // Read it back the way the application does, not just with SELECT *. Found in T2: the write
        // was correct and the read query's explicit column list omitted Resolution, so every consumer
        // saw null while the database held the value. A raw-SQL assertion cannot see that.
        NotificationEntity throughReader = (await _reader.GetPagedAsync(1, 0)).Items
            .Single(n => n.Id == alert.Id);
        Assert.AreEqual(NotificationResolution.TookIncoming, throughReader.Resolution.Parsed,
            "The read path must select the column, or the value is stored and invisible.");
    }

    /// <summary>
    /// #308, found in T2: the reseed and reset actions dismiss through `DismissByTriggerAsync`, not the
    /// by-batch method. Wiring only the latter left `Reseeded` and `Reset` defined, translated, and
    /// never written — and rows 17/18 did not catch it, because both only exercised the by-batch path.
    /// </summary>
    [TestMethod]
    public async Task DismissedByTrigger_RecordsTheResolution()
    {
        NotificationEntity written = await _writer.WriteAsync(
            NotificationType.ActionRequired, "The database holds no quotes.", appVersionId: null,
            dismissTrigger: NotificationDismissTrigger.Reseed);

        await _writer.DismissByTriggerAsync(NotificationDismissTrigger.Reseed, NotificationResolution.Reseeded);

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        NotificationEntity persisted = conn.QuerySingle<NotificationEntity>(
            "SELECT * FROM System_Notification WHERE Id = @id;", new { id = written.Id.ToString("D") });

        Assert.AreEqual(NotificationDismissReason.Resolved, persisted.DismissReason.Parsed);
        Assert.AreEqual(NotificationResolution.Reseeded, persisted.Resolution.Parsed,
            "A reseed run from its own notification must say so, not merely that it is done.");
    }

    /// <summary>
    /// #308, the second defect found in T2: the value was stored correctly and every consumer saw
    /// null, because the read query's explicit column list did not name <c>Resolution</c>. The two
    /// tests above could not catch it — both read with a raw <c>SELECT *</c>, which no consumer uses.
    /// A stored value nothing can read is indistinguishable from a value never written.
    /// </summary>
    [TestMethod]
    public async Task DismissedAsResolved_TheResolutionSurvivesTheReadPath()
    {
        NotificationEntity written = await _writer.WriteAsync(
            NotificationType.ActionRequired, "The database holds no quotes.", appVersionId: null,
            dismissTrigger: NotificationDismissTrigger.Reseed);

        await _writer.DismissByTriggerAsync(NotificationDismissTrigger.Reseed, NotificationResolution.Reseeded);

        NotificationEntity read = (await _reader.GetPagedAsync(1, 0)).Items.Single(n => n.Id == written.Id);

        Assert.AreEqual(NotificationResolution.Reseeded, read.Resolution.Parsed,
            "The reader must return the stored resolution — a column the read query omits is invisible to every consumer.");
    }

    /// <summary>
    /// The negative case: `Resolution` means "how the action settled it", not "how it went inactive".
    /// A notification the operator simply dismissed had no action run, so it records none — otherwise
    /// the field would claim an outcome nobody chose.
    /// </summary>
    [TestMethod]
    public async Task DismissedByUser_RecordsNoResolution()
    {
        string batchId = Guid.NewGuid().ToString("D");
        NotificationEntity alert = await WriteReviewAlertAsync(batchId);

        await _writer.DismissAsync(alert.Id);

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        NotificationEntity persisted = conn.QuerySingle<NotificationEntity>(
            "SELECT * FROM System_Notification WHERE Id = @id;", new { id = alert.Id.ToString("D") });

        Assert.IsTrue(persisted.IsDismissed, "Positive control: the dismissal itself must have happened.");
        Assert.IsNull(persisted.Resolution.Parsed,
            "No action ran, so there is no resolution to record.");
    }

    private async Task<NotificationEntity> WriteReviewAlertAsync(string batchId)
    {
        ImportReviewPendingMetadataDto metadata = new()
        {
            FileName     = "curated.json",
            Origin       = FileResourceOrigin.System,
            BatchId      = batchId,
            Counts       = [new ImportReviewCountDto { Status = "Pending", Count = 1 }],
            ReleaseState = NotificationReleaseState.NotApplicable,
        };

        return await _writer.WriteAsync(
            NotificationType.ActionRequired, "review me", appVersionId: null,
            dismissTrigger: NotificationDismissTrigger.ImportReviewResolved,
            metadata: NotificationMetadataKinds.Serialize(metadata),
            metadataKind: NotificationMetadataKind.ImportReviewPending);
    }

    /// <summary>The CHECK rejects a reason outside the enum, per ADR 008.</summary>
    [TestMethod]
    public async Task DismissReason_UnknownValue_IsRejectedByTheCheckConstraint()
    {
        NotificationEntity entity = await _writer.WriteAsync(NotificationType.Information, "x", appVersionId: null);

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
            "UPDATE System_Notification SET DismissReason = 'NotARealReason' WHERE Id = @id;",
            new { id = entity.Id.ToString("D") }));
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
