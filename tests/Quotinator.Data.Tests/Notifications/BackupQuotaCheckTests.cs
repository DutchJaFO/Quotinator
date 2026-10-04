using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Helpers;
using Quotinator.Data.Notifications;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.Database;
using Quotinator.Data.Testing.Fakes;

namespace Quotinator.Data.Tests.Notifications;

/// <summary>
/// <see cref="BackupQuotaCheck"/> (#348): the warning is raised while the backups folder is past its
/// operating quota and resolved once it is back under, against a real database and a real folder.
/// <para>
/// The ceiling is 1 GB throughout, and the folder is filled with a sparse file sized as a share of it, so
/// the arithmetic is exact without writing a gigabyte. A test that needs the warning already open seeds it
/// directly rather than through the check, so a check that does nothing cannot satisfy its precondition.
/// </para>
/// </summary>
[TestClass]
public class BackupQuotaCheckTests
{
    private const long OneGigabyte = 1_073_741_824L;

    public TestContext TestContext { get; set; } = null!;

    private TempDirectory _tempDir = null!;
    private string _dbPath = null!;
    private string _backups = null!;
    private NotificationWriter _writer = null!;
    private NotificationReader _reader = null!;

    [TestInitialize]
    public async Task TestInitialize()
    {
        _tempDir = new TempDirectory("quotinator_backup_quota_check_");
        _dbPath = Path.Combine(_tempDir.Path, "test.db");
        // Not "backups": CurrentSchema writes its own backups there while building the schema, and this
        // folder must hold exactly what each test puts in it.
        _backups = Path.Combine(_tempDir.Path, "quota-backups");
        Directory.CreateDirectory(_backups);

        await CurrentSchema.ApplyDataSchemaAsync(_dbPath);

        SqliteConnectionFactory factory = new(_dbPath);
        _writer = new NotificationWriter(factory);
        _reader = TestNotificationReader.Create(factory);
    }

    [TestCleanup]
    public void TestCleanup() => _tempDir.Dispose();

    [TestMethod]
    public async Task AboveTheQuota_RaisesTheWarning()
    {
        FillTo(percentOfCeiling: 95);

        await CreateCheck().CheckAsync();

        Assert.AreEqual(1, await OpenWarningsAsync());
    }

    [TestMethod]
    public async Task AboveTheQuota_ReportsRaised()
    {
        FillTo(percentOfCeiling: 95);

        Assert.AreEqual(NotificationConditionOutcome.Raised, await CreateCheck().CheckAsync());
    }

    [TestMethod]
    public async Task BelowTheQuota_RaisesNothing()
    {
        FillTo(percentOfCeiling: 50);

        await CreateCheck().CheckAsync();

        Assert.AreEqual(0, await OpenWarningsAsync());
    }

    /// <summary>The same unresolved condition is one warning, however often it is checked.</summary>
    [TestMethod]
    public async Task AboveTheQuotaOnTwoChecks_RaisesOneWarning()
    {
        FillTo(percentOfCeiling: 95);

        await CreateCheck().CheckAsync();
        await CreateCheck().CheckAsync();

        Assert.AreEqual(1, await OpenWarningsAsync());
    }

    /// <summary>
    /// A folder that has grown since the warning was raised is the same unresolved condition, not a new one.
    /// The production path grows it between checks: a backup taken from the reserve runs the check after
    /// writing, so a size held in the payload's identity would re-announce the warning on every backup.
    /// </summary>
    [TestMethod]
    public async Task AboveTheQuotaAndGrowing_RaisesOneWarning()
    {
        FillTo(percentOfCeiling: 92);
        await CreateCheck().CheckAsync();

        FillTo(percentOfCeiling: 95);
        await CreateCheck().CheckAsync();

        Assert.AreEqual(1, await OpenWarningsAsync());
    }

    /// <summary>
    /// The warning says backups are still being taken from the reserve. Above the max they are refused,
    /// so it is no longer valid and the generic check removes it; the error from the refused attempt is
    /// what applies there. A warning claiming the reserve and an error claiming the max cannot both stand.
    /// </summary>
    [TestMethod]
    public async Task AboveTheMax_RemovesTheWarning()
    {
        await SeedWarningAsync();
        FillTo(percentOfCeiling: 110);

        await CreateCheck().CheckAsync();

        Assert.AreEqual(0, await OpenWarningsAsync());
    }

    [TestMethod]
    public async Task AboveTheMax_RaisesNothing()
    {
        FillTo(percentOfCeiling: 110);

        await CreateCheck().CheckAsync();

        Assert.AreEqual(0, await OpenWarningsAsync());
    }

    [TestMethod]
    public async Task BackUnderTheQuota_ClearsTheWarning()
    {
        await SeedWarningAsync();
        FillTo(percentOfCeiling: 50);

        await CreateCheck().CheckAsync();

        Assert.AreEqual(0, await OpenWarningsAsync());
    }

    [TestMethod]
    public async Task BackUnderTheQuota_ReportsCleared()
    {
        await SeedWarningAsync();
        FillTo(percentOfCeiling: 50);

        Assert.AreEqual(NotificationConditionOutcome.Cleared, await CreateCheck().CheckAsync());
    }

    /// <summary>Resolved means the folder came back under, and the record says so.</summary>
    [TestMethod]
    public async Task BackUnderTheQuota_RecordsThatTheFolderCameBackUnder()
    {
        await SeedWarningAsync();
        FillTo(percentOfCeiling: 50);

        await CreateCheck().CheckAsync();

        NotificationEntity warning = (await _reader.GetByMetadataKindAsync(NotificationMetadataKind.BackupQuotaReached)).Single();
        Assert.AreEqual(NotificationResolution.UnderQuota, warning.Resolution?.Parsed);
    }

    [TestMethod]
    public async Task StillAboveTheQuota_KeepsTheWarning()
    {
        await SeedWarningAsync();
        FillTo(percentOfCeiling: 95);

        await CreateCheck().CheckAsync();

        Assert.AreEqual(1, await OpenWarningsAsync());
    }

    /// <summary>The warning tells the user where they stand: what is used, the quota, and the ceiling.</summary>
    [TestMethod]
    public async Task Warning_NamesTheBytesUsedTheQuotaAndTheCeiling()
    {
        FillTo(percentOfCeiling: 95);

        await CreateCheck(textSource: new ArgumentsTextSource()).CheckAsync();

        NotificationEntity? warning = (await _reader.GetByMetadataKindAsync(NotificationMetadataKind.BackupQuotaReached)).SingleOrDefault();
        Assert.AreEqual(
            $"{ByteSize.Format(OneGigabyte * 95 / 100)}|{ByteSize.Format(OneGigabyte * 90 / 100)}|{ByteSize.Format(OneGigabyte)}",
            warning?.Body);
    }

    /// <summary>
    /// 60% used is under the default 90% quota and over a configured 50% one: a warning here shows the
    /// configured value is the one consulted. This is where a configured quota takes effect, since the
    /// quota no longer refuses a backup.
    /// </summary>
    [TestMethod]
    public async Task ConfiguredQuotaPercent_IsTheLevelTheWarningIsRaisedFrom()
    {
        FillTo(percentOfCeiling: 60);

        await CreateCheck(quotaPercent: 50).CheckAsync();

        Assert.AreEqual(1, await OpenWarningsAsync());
    }

    /// <summary>An ignored configuration value says so, where the value is used.</summary>
    [TestMethod]
    public async Task QuotaPercent_OutOfRange_IsReported()
    {
        CapturingLogger<BackupQuotaCheck> logger = new();

        await CreateCheck(quotaPercent: 150, logger: logger).CheckAsync();

        Assert.IsTrue(logger.Messages.Exists(m => m.Contains("BackupQuotaPercent", StringComparison.Ordinal)));
    }

    private BackupQuotaCheck CreateCheck(
        int quotaPercent = DatabaseOptions.DefaultBackupQuotaPercent,
        INotificationTextSource? textSource = null,
        ILogger<BackupQuotaCheck>? logger = null) =>
        new(
            new DatabaseOptions { DbPath = _dbPath, BackupsPath = _backups, MaxBackupStorageGb = 1, BackupQuotaPercent = quotaPercent },
            _reader, _writer, textSource ?? Quotinator.Data.Testing.NoOps.NoOpNotificationTextSource.Instance,
            logger ?? NullLogger<BackupQuotaCheck>.Instance);

    private void FillTo(int percentOfCeiling)
    {
        using FileStream filler = new(Path.Combine(_backups, "filler.db"), FileMode.Create, FileAccess.Write);
        filler.SetLength(OneGigabyte * percentOfCeiling / 100);
    }

    private async Task SeedWarningAsync() =>
        await NotificationSeeding.SeedWhileUnresolvedAsync(
            _reader, _writer, NotificationType.Warning,
            new BackupQuotaReachedMetadataDto
            {
                UsedBytes = 1, QuotaBytes = 1, CeilingBytes = 1, ReleaseState = NotificationReleaseState.NotApplicable,
            },
            body: "seeded", appVersionId: null, dismissTrigger: NotificationDismissTrigger.BackupQuotaRestored);

    private async Task<int> OpenWarningsAsync() =>
        (await _reader.GetActiveNotificationsAsync()).Count(n => n.MetadataKind?.Parsed == NotificationMetadataKind.BackupQuotaReached);

    /// <summary>Writes a body's arguments joined by <c>|</c>, so a test can read exactly what was passed.</summary>
    private sealed class ArgumentsTextSource : INotificationTextSource
    {
        /// <inheritdoc/>
        public IReadOnlyDictionary<string, string> ForEveryLanguage(string key, params object[] args) =>
            new Dictionary<string, string> { ["en"] = args.Length == 0 ? key : string.Join("|", args) };
    }
}
