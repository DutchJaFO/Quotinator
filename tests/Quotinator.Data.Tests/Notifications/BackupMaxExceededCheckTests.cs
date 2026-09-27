using Microsoft.Data.Sqlite;
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
/// <see cref="BackupMaxExceededCheck"/> (#348): the error is raised while the backups folder is at or
/// above its maximum and resolved once it is back under, against a real database and a real folder.
/// <para>
/// The maximum is 1 GB throughout, and the folder is filled with a sparse file sized as a share of it, so
/// the arithmetic is exact without writing a gigabyte. A test that needs the error already open seeds it
/// directly rather than through the check, so a check that does nothing cannot satisfy its precondition.
/// </para>
/// </summary>
[TestClass]
public class BackupMaxExceededCheckTests
{
    private const long OneGigabyte = 1_073_741_824L;

    public TestContext TestContext { get; set; } = null!;

    private string _tempDir = null!;
    private string _dbPath = null!;
    private string _backups = null!;
    private NotificationWriter _writer = null!;
    private NotificationReader _reader = null!;

    [TestInitialize]
    public async Task TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_backup_max_check_").FullName;
        _dbPath = Path.Combine(_tempDir, "test.db");
        // Not "backups": CurrentSchema writes its own backups there while building the schema, and this
        // folder must hold exactly what each test puts in it.
        _backups = Path.Combine(_tempDir, "max-backups");
        Directory.CreateDirectory(_backups);

        await CurrentSchema.ApplyDataSchemaAsync(_dbPath);

        SqliteConnectionFactory factory = new(_dbPath);
        _writer = new NotificationWriter(factory);
        _reader = TestNotificationReader.Create(factory);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Exactly at the maximum every backup is already refused, so the band starts here.</summary>
    [TestMethod]
    public async Task AtTheMax_RaisesTheError()
    {
        FillTo(percentOfMax: 100);

        await CreateCheck().CheckAsync();

        Assert.AreEqual(1, await OpenErrorsAsync());
    }

    [TestMethod]
    public async Task AtTheMax_ReportsRaised()
    {
        FillTo(percentOfMax: 100);

        Assert.AreEqual(NotificationConditionOutcome.Raised, await CreateCheck().CheckAsync());
    }

    /// <summary>An error, not a warning: at the maximum nothing that needs a backup can run.</summary>
    [TestMethod]
    public async Task AtTheMax_RaisesItAsAnError()
    {
        FillTo(percentOfMax: 100);

        await CreateCheck().CheckAsync();

        NotificationEntity? error = (await _reader.GetByMetadataKindAsync(NotificationMetadataKind.BackupMaxExceeded)).SingleOrDefault();
        Assert.AreEqual(NotificationType.Error, error?.Type.Parsed);
    }

    /// <summary>Inside the reserve backups still succeed, which is the warning's band and not this one.</summary>
    [TestMethod]
    public async Task BelowTheMax_RaisesNothing()
    {
        FillTo(percentOfMax: 95);

        await CreateCheck().CheckAsync();

        Assert.AreEqual(0, await OpenErrorsAsync());
    }

    [TestMethod]
    public async Task AboveTheMaxOnTwoChecks_RaisesOneError()
    {
        FillTo(percentOfMax: 110);

        await CreateCheck().CheckAsync();
        await CreateCheck().CheckAsync();

        Assert.AreEqual(1, await OpenErrorsAsync());
    }

    [TestMethod]
    public async Task BackUnderTheMax_ClearsTheError()
    {
        await SeedErrorAsync();
        FillTo(percentOfMax: 50);

        await CreateCheck().CheckAsync();

        Assert.AreEqual(0, await OpenErrorsAsync());
    }

    [TestMethod]
    public async Task BackUnderTheMax_RecordsThatTheFolderCameBackUnder()
    {
        await SeedErrorAsync();
        FillTo(percentOfMax: 50);

        await CreateCheck().CheckAsync();

        NotificationEntity? resolved = (await _reader.GetByMetadataKindAsync(NotificationMetadataKind.BackupMaxExceeded)).SingleOrDefault();
        Assert.AreEqual(NotificationResolution.UnderMax, resolved?.Resolution.Parsed);
    }

    [TestMethod]
    public async Task StillAboveTheMax_KeepsTheError()
    {
        await SeedErrorAsync();
        FillTo(percentOfMax: 110);

        await CreateCheck().CheckAsync();

        Assert.AreEqual(1, await OpenErrorsAsync());
    }

    [TestMethod]
    public async Task Error_NamesTheBytesUsedAndTheMax()
    {
        FillTo(percentOfMax: 110);

        await CreateCheck(textSource: new ArgumentsTextSource()).CheckAsync();

        NotificationEntity? error = (await _reader.GetByMetadataKindAsync(NotificationMetadataKind.BackupMaxExceeded)).SingleOrDefault();
        Assert.AreEqual(
            $"{ByteSize.Format(OneGigabyte * 110 / 100)}|{ByteSize.Format(OneGigabyte)}",
            error?.Body);
    }

    /// <summary>
    /// The two bands cannot describe the same folder: a warning saying backups still succeed from the
    /// reserve and an error saying every backup is refused must never be open together, whichever band
    /// the folder is in.
    /// </summary>
    [TestMethod]
    [DataRow(50, 0, 0)]
    [DataRow(95, 1, 0)]
    [DataRow(110, 0, 1)]
    public async Task EachBand_LeavesOnlyItsOwnNotificationOpen(int percentOfMax, int expectedWarnings, int expectedErrors)
    {
        FillTo(percentOfMax);

        await CreateQuotaCheck().CheckAsync();
        await CreateCheck().CheckAsync();

        IReadOnlyList<NotificationEntity> active = await _reader.GetActiveNotificationsAsync();
        Assert.AreEqual(
            (expectedWarnings, expectedErrors),
            (active.Count(n => n.MetadataKind?.Parsed == NotificationMetadataKind.BackupQuotaReached),
             active.Count(n => n.MetadataKind?.Parsed == NotificationMetadataKind.BackupMaxExceeded)));
    }

    private BackupQuotaCheck CreateQuotaCheck() =>
        new(
            new DatabaseOptions { DbPath = _dbPath, BackupsPath = _backups, MaxBackupStorageGb = 1 },
            _reader, _writer, Quotinator.Data.Testing.NoOps.NoOpNotificationTextSource.Instance,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BackupQuotaCheck>.Instance);

    private BackupMaxExceededCheck CreateCheck(INotificationTextSource? textSource = null) =>
        new(
            new DatabaseOptions { DbPath = _dbPath, BackupsPath = _backups, MaxBackupStorageGb = 1 },
            _reader, _writer, textSource ?? Quotinator.Data.Testing.NoOps.NoOpNotificationTextSource.Instance);

    private void FillTo(int percentOfMax)
    {
        using FileStream filler = new(Path.Combine(_backups, "filler.db"), FileMode.Create, FileAccess.Write);
        filler.SetLength(OneGigabyte * percentOfMax / 100);
    }

    private async Task SeedErrorAsync() =>
        await NotificationSeeding.SeedWhileUnresolvedAsync(
            _reader, _writer, NotificationType.Error,
            new BackupMaxExceededMetadataDto
            {
                UsedBytes = 1, CeilingBytes = 1, ReleaseState = NotificationReleaseState.NotApplicable,
            },
            body: "seeded", appVersionId: null, dismissTrigger: NotificationDismissTrigger.BackupBackUnderMax);

    private async Task<int> OpenErrorsAsync() =>
        (await _reader.GetActiveNotificationsAsync()).Count(n => n.MetadataKind?.Parsed == NotificationMetadataKind.BackupMaxExceeded);

    /// <summary>Writes a body's arguments joined by <c>|</c>, so a test can read exactly what was passed.</summary>
    private sealed class ArgumentsTextSource : INotificationTextSource
    {
        /// <inheritdoc/>
        public IReadOnlyDictionary<string, string> ForEveryLanguage(string key, params object[] args) =>
            new Dictionary<string, string> { ["en"] = args.Length == 0 ? key : string.Join("|", args) };
    }
}
